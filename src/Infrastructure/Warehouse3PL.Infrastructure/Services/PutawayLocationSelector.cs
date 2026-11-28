using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Putaway;
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
        var incomingVolume = quantity * PutawaySafetyValidator.ResolveItemVolumeCm3(item);

        var excludedSet = excludedLocationIds != null
            ? new HashSet<Guid>(excludedLocationIds)
            : new HashSet<Guid>();

        // 1. Query active candidate storable bins in this facility (MDM-2.1 A01/A06)
        var candidateLocations = await _context.Locations
            .AsNoTracking()
            .Include(l => l.Zone)
            .Include(l => l.StorageProfile)
            .Where(l => l.FacilityId == facilityId &&
                        !l.IsDeleted &&
                        l.IsActive &&
                        l.Status == LocationStatus.Active &&
                        l.IsStorable &&
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

        var effectiveRule = PutawaySafetyValidator.ResolveEffectiveRule(strategyRules, clientId, item);
        bool preferConsolidation = effectiveRule?.PreferConsolidation ?? true;
        bool allowsDynamicOverflow = effectiveRule?.AllowDynamicOverflow == true;

        // 3. Query existing stock and active handling units in candidate locations
        var candidateIds = candidateLocations.Select(c => c.Id).ToHashSet();
        var currentStocks = await _context.StockOnHand
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Item)
            .Where(s => s.FacilityId == facilityId && candidateIds.Contains(s.LocationId))
            .ToListAsync(cancellationToken);

        var stockByLocation = currentStocks
            .GroupBy(s => s.LocationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var huCappedLocationIds = candidateLocations
            .Where(l => l.StorageProfile?.MaxHandlingUnits.HasValue == true)
            .Select(l => l.Id)
            .ToHashSet();

        Dictionary<Guid, int> activeHuCounts = new();
        if (huCappedLocationIds.Count > 0)
        {
            activeHuCounts = await _context.HandlingUnits
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(hu => hu.FacilityId == facilityId &&
                             hu.CurrentLocationId.HasValue &&
                             huCappedLocationIds.Contains(hu.CurrentLocationId.Value) &&
                             !hu.IsDeleted)
                .GroupBy(hu => hu.CurrentLocationId!.Value)
                .Select(g => new { LocationId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.LocationId, x => x.Count, cancellationToken);
        }

        var recommendations = new List<PutawayRecommendation>();

        foreach (var loc in candidateLocations)
        {
            // A0. MDM-2.1 A06 Dedicated Client & Storage Profile Restrictions
            if (loc.Zone?.DedicatedClientId is Guid zoneClientId && zoneClientId != clientId)
            {
                continue;
            }

            var profile = loc.StorageProfile;
            if (profile != null)
            {
                if (!profile.IsPutawayAllowed)
                {
                    continue;
                }

                if (profile.DedicatedClientId.HasValue && profile.DedicatedClientId.Value != clientId)
                {
                    continue;
                }

                if (profile.MaxHandlingUnits.HasValue)
                {
                    activeHuCounts.TryGetValue(loc.Id, out var huCount);
                    if (huCount >= profile.MaxHandlingUnits.Value)
                    {
                        continue;
                    }
                }
            }

            // A. Strict Temperature Compatibility Check (shared single source of truth with confirmation)
            if (!PutawaySafetyValidator.IsZoneTemperatureCompatible(loc.Zone, item, loc, allowsDynamicOverflow, out _))
            {
                continue;
            }

            // B. Physical, Volumetric & Storage-Profile Mixing Constraints
            stockByLocation.TryGetValue(loc.Id, out var existingStockInBin);
            existingStockInBin ??= [];

            if (profile != null && !profile.AllowMixedSku &&
                existingStockInBin.Any(s => s.TotalQuantity > 0m && s.ItemId != itemId))
            {
                continue;
            }

            if (profile != null && !profile.AllowMixedLot &&
                existingStockInBin.Any(s => s.TotalQuantity > 0m && s.LotId != lotId))
            {
                continue;
            }

            var currentWeight = existingStockInBin.Sum(s => s.TotalQuantity * s.Item.WeightKg);
            var currentVolume = existingStockInBin.Sum(s => s.TotalQuantity * PutawaySafetyValidator.ResolveItemVolumeCm3(s.Item));

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

