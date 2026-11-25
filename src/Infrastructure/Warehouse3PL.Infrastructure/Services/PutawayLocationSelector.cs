using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services;

/// <summary>
/// Slotting & Location Allocation Engine for directed putaway operations.
/// Evaluates temperature constraints, ABC velocity routing, physical/volumetric capacity, and SKU/Lot consolidation affinity.
/// </summary>
public class PutawayLocationSelector : IPutawayLocationSelector, IPutawayRecommendationService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PutawayLocationSelector> _logger;

    public PutawayLocationSelector(
        IApplicationDbContext context,
        ILogger<PutawayLocationSelector> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Location?> SelectOptimalLocationAsync(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        decimal quantity,
        Guid? lotId = null,
        IEnumerable<Guid>? excludedLocationIds = null,
        CancellationToken cancellationToken = default)
    {
        var recommendations = await EvaluateCandidateLocationsAsync(
            clientId, facilityId, itemId, quantity, lotId, excludedLocationIds, cancellationToken);

        if (recommendations.Count == 0)
        {
            return null;
        }

        var topRecommendation = recommendations[0];
        return await _context.Locations
            .Include(l => l.Zone)
            .FirstOrDefaultAsync(l => l.Id == topRecommendation.LocationId, cancellationToken);
    }

    public async Task<List<PutawayRecommendation>> EvaluateCandidateLocationsAsync(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        decimal quantity,
        Guid? lotId = null,
        IEnumerable<Guid>? excludedLocationIds = null,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.Items
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ClientId == clientId, cancellationToken);

        if (item == null)
        {
            throw new EntityNotFoundException(nameof(Item), itemId);
        }

        var incomingWeight = quantity * item.WeightKg;
        var incomingVolume = quantity * ResolveItemVolumeCm3(item);

        var excludedSet = excludedLocationIds != null
            ? new HashSet<Guid>(excludedLocationIds)
            : new HashSet<Guid>();

        // 1. Query active candidate storage bins in this facility
        var candidateLocations = await _context.Locations
            .AsNoTracking()
            .Include(l => l.Zone)
            .Where(l => l.FacilityId == facilityId &&
                        !l.IsDeleted &&
                        l.IsActive &&
                        !l.IsLocked &&
                        !l.IsDamaged &&
                        (l.LocationType == LocationType.Bin ||
                         l.LocationType == LocationType.BulkFloor ||
                         l.LocationType == LocationType.Cantilever) &&
                        !excludedSet.Contains(l.Id))
            .ToListAsync(cancellationToken);

        if (candidateLocations.Count == 0)
        {
            return [];
        }

        // 2. Query configured active strategy rules for this tenant/facility
        var strategyRules = await _context.PutawayStrategyRules
            .AsNoTracking()
            .Where(r => r.FacilityId == facilityId &&
                        (r.ClientId == clientId || r.ClientId == Guid.Empty) &&
                        r.IsActive &&
                        !r.IsDeleted)
            .OrderBy(r => r.Sequence)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var effectiveRule = ResolveEffectiveRule(strategyRules, clientId, item);
        bool preferConsolidation = effectiveRule?.PreferConsolidation ?? true;
        bool allowsDynamicOverflow = strategyRules.Any(r =>
            r.TempClassRequirement == item.StorageTempClass && r.AllowDynamicOverflow);

        // 3. Query existing stock in candidate locations to evaluate capacity and SKU consolidation
        var candidateIds = candidateLocations.Select(c => c.Id).ToHashSet();
        var currentStocks = await _context.StockOnHand
            .AsNoTracking()
            .Include(s => s.Item)
            .Where(s => s.FacilityId == facilityId && candidateIds.Contains(s.LocationId))
            .ToListAsync(cancellationToken);

        var stockByLocation = currentStocks
            .GroupBy(s => s.LocationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var recommendations = new List<PutawayRecommendation>();

        foreach (var loc in candidateLocations)
        {
            // A. Strict Temperature Compatibility Check
            var zone = loc.Zone;
            if (item.StorageTempClass == StorageTempClass.Frozen)
            {
                var isFrozenZone = zone != null &&
                    (zone.ZoneType == WarehouseZoneType.ColdStorage || (zone.TempMaxC.HasValue && zone.TempMaxC.Value <= -15m));
                if (!isFrozenZone)
                {
                    continue; // Strictly reject non-freezer locations
                }
            }
            else if (item.StorageTempClass == StorageTempClass.Refrigerated)
            {
                var isChilledZone = zone != null &&
                    (zone.ZoneType == WarehouseZoneType.ColdStorage || (zone.TempMaxC.HasValue && zone.TempMaxC.Value <= 8m));
                if (!isChilledZone)
                {
                    continue; // Strictly reject ambient locations
                }
            }
            else
            {
                // Ambient item: Prefer non-cold locations unless dynamic overflow allows cold room usage
                var isColdStorage = zone != null && zone.ZoneType == WarehouseZoneType.ColdStorage;
                if (isColdStorage && !allowsDynamicOverflow)
                {
                    continue; // Preserve cold space for refrigerated/frozen goods
                }
            }

            // B. Physical & Volumetric Capacity Constraints
            stockByLocation.TryGetValue(loc.Id, out var existingStockInBin);
            existingStockInBin ??= [];

            var currentWeight = existingStockInBin.Sum(s => s.TotalQuantity * s.Item.WeightKg);
            var currentVolume = existingStockInBin.Sum(s => s.TotalQuantity * ResolveItemVolumeCm3(s.Item));

            if (loc.MaxWeightKg.HasValue && (currentWeight + incomingWeight) > loc.MaxWeightKg.Value)
            {
                continue; // Exceeds weight threshold
            }

            if (loc.MaxVolumeCm3.HasValue && (currentVolume + incomingVolume) > loc.MaxVolumeCm3.Value)
            {
                continue; // Exceeds cubic volume capacity
            }

            // C. Scoring Algorithm
            decimal score = 50m;
            var reasons = new List<string>();

            // 1. Item-Location Affinity / Consolidation governed by effective PutawayStrategyRule.PreferConsolidation
            var hasSameSku = existingStockInBin.Any(s => s.ItemId == itemId && s.TotalQuantity > 0m);
            var hasSameLot = lotId.HasValue && existingStockInBin.Any(s => s.ItemId == itemId && s.LotId == lotId && s.TotalQuantity > 0m);
            var hasAnyStock = existingStockInBin.Any(s => s.TotalQuantity > 0m);

            if (preferConsolidation)
            {
                if (hasSameLot)
                {
                    score += 120m;
                    reasons.Add("Consolidation with identical SKU and Lot");
                }
                else if (hasSameSku)
                {
                    score += 100m;
                    reasons.Add("Consolidation with existing SKU stock");
                }
                else if (!hasAnyStock)
                {
                    score += 20m;
                    reasons.Add("Empty bin available");
                }
            }
            else
            {
                if (!hasAnyStock)
                {
                    score += 100m;
                    reasons.Add("Empty bin preferred (consolidation disabled by putaway strategy)");
                }
                else if (hasSameLot || hasSameSku)
                {
                    reasons.Add("Fallback occupied SKU bin available with remaining capacity");
                }
                else
                {
                    score -= 10m;
                    reasons.Add("Mixed-SKU bin penalized (consolidation disabled)");
                }
            }

            // 2. ABC Velocity Routing
            if (item.AbcVelocity == AbcVelocity.A)
            {
                // Fast movers: low shelf levels (1-2) and front-aisle / low pick sequence
                if (loc.ShelfLevel.HasValue && loc.ShelfLevel.Value <= 2)
                {
                    score += 40m;
                    reasons.Add("Low-level ergonomics for fast-moving A-velocity SKU");
                }
                if (loc.PickSequence.HasValue && loc.PickSequence.Value <= 100)
                {
                    score += 25m;
                    reasons.Add("Front-aisle fast travel pick path");
                }
            }
            else if (item.AbcVelocity == AbcVelocity.B)
            {
                // Medium movers: mid-level shelves (2-3)
                if (loc.ShelfLevel.HasValue && (loc.ShelfLevel.Value == 2 || loc.ShelfLevel.Value == 3))
                {
                    score += 20m;
                    reasons.Add("Mid-level shelf for B-velocity SKU");
                }
            }
            else if (item.AbcVelocity == AbcVelocity.C)
            {
                // Slow movers: high shelves (level >= 3) to preserve lower levels for fast pickers
                if (loc.ShelfLevel.HasValue && loc.ShelfLevel.Value >= 3)
                {
                    score += 30m;
                    reasons.Add("High-level shelf preservation for C-velocity SKU");
                }
            }

            // 3. Custom Strategy Rule Adjustments
            if (effectiveRule != null &&
                effectiveRule.TargetVelocity.HasValue &&
                (int)effectiveRule.TargetVelocity.Value == (int)item.AbcVelocity)
            {
                score += 15m;
                reasons.Add($"Matched strategy rule '{effectiveRule.RuleName}'");
            }

            var reasonSummary = reasons.Count > 0 ? string.Join("; ", reasons) : "Standard available capacity";

            recommendations.Add(new PutawayRecommendation(
                loc.Id,
                loc.LocationCode,
                loc.Barcode,
                loc.Zone?.ZoneCode ?? "DEFAULT",
                score,
                reasonSummary));
        }

        return recommendations
            .OrderByDescending(r => r.FitScore)
            .ThenBy(r => r.LocationCode, StringComparer.Ordinal)
            .ThenBy(r => r.LocationId)
            .Take(10)
            .ToList();
    }

    private static PutawayStrategyRule? ResolveEffectiveRule(
        List<PutawayStrategyRule> rules,
        Guid clientId,
        Item item)
    {
        if (rules.Count == 0)
        {
            return null;
        }

        return rules
            .OrderBy(r => r.ClientId == clientId ? 0 : 1)
            .ThenBy(r => r.TempClassRequirement == item.StorageTempClass ? 0 : 1)
            .ThenBy(r => r.TargetVelocity.HasValue && (int)r.TargetVelocity.Value == (int)item.AbcVelocity
                ? 0
                : !r.TargetVelocity.HasValue
                    ? 1
                    : 2)
            .ThenBy(r => r.Sequence)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
    }

    private static decimal ResolveItemVolumeCm3(Item item) =>
        item.VolumeCm3 > 0m
            ? item.VolumeCm3
            : item.LengthCm * item.WidthCm * item.HeightCm;

    // IPutawayRecommendationService implementation for backwards compatibility
    public Task<List<PutawayRecommendation>> RecommendLocationsAsync(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        decimal quantity,
        Guid? lotId = null,
        CancellationToken cancellationToken = default)
    {
        return EvaluateCandidateLocationsAsync(clientId, facilityId, itemId, quantity, lotId, null, cancellationToken);
    }
}

