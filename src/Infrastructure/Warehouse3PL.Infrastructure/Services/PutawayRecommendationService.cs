using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services;

public class PutawayRecommendationService : IPutawayRecommendationService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PutawayRecommendationService> _logger;

    public PutawayRecommendationService(
        IApplicationDbContext context,
        ILogger<PutawayRecommendationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<PutawayRecommendation>> RecommendLocationsAsync(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        decimal quantity,
        Guid? lotId = null,
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
        var incomingVolume = quantity * (item.VolumeCm3 > 0 ? item.VolumeCm3 : (item.LengthCm * item.WidthCm * item.HeightCm));

        // 1. Query candidate storage locations in this facility
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
                         l.LocationType == LocationType.Cantilever))
            .ToListAsync(cancellationToken);

        // 2. Query existing stock in this facility to calculate current capacity utilization
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
            // A. Temperature Validation
            if (item.StorageTempClass == StorageTempClass.Frozen)
            {
                var isFrozen = loc.Zone != null &&
                    (loc.Zone.ZoneType == WarehouseZoneType.ColdStorage || (loc.Zone.TempMaxC.HasValue && loc.Zone.TempMaxC.Value <= -15m));
                if (!isFrozen) continue;
            }
            else if (item.StorageTempClass == StorageTempClass.Refrigerated)
            {
                var isChilled = loc.Zone != null &&
                    (loc.Zone.ZoneType == WarehouseZoneType.ColdStorage || (loc.Zone.TempMaxC.HasValue && loc.Zone.TempMaxC.Value <= 8m));
                if (!isChilled) continue;
            }

            // B. Capacity & Weight Validation
            stockByLocation.TryGetValue(loc.Id, out var existingStockInBin);
            existingStockInBin ??= [];

            var currentWeight = existingStockInBin.Sum(s => s.TotalQuantity * s.Item.WeightKg);
            var currentVolume = existingStockInBin.Sum(s => s.TotalQuantity * s.Item.VolumeCm3);

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

            // Consolidation bonus: does this bin already contain the same SKU?
            var hasSameSku = existingStockInBin.Any(s => s.ItemId == itemId);
            if (hasSameSku)
            {
                score += 100m;
                reasons.Add("Consolidation with existing SKU stock");
            }
            else if (existingStockInBin.Count == 0)
            {
                score += 20m;
                reasons.Add("Empty bin available");
            }

            // ABC Velocity optimization
            if (item.AbcVelocity == AbcVelocity.A)
            {
                // Fast movers preferred at lower shelf levels (1-2) or low pick sequence
                if (loc.ShelfLevel.HasValue && loc.ShelfLevel.Value <= 2)
                {
                    score += 40m;
                    reasons.Add("Low-level ergonomics for fast-moving A-velocity SKU");
                }
                if (loc.PickSequence.HasValue && loc.PickSequence.Value <= 100)
                {
                    score += 20m;
                    reasons.Add("Fast travel pick sequence path");
                }
            }
            else if (item.AbcVelocity == AbcVelocity.C)
            {
                // Slow movers placed on higher shelves (3+) to reserve lower levels for A/B items
                if (loc.ShelfLevel.HasValue && loc.ShelfLevel.Value >= 3)
                {
                    score += 30m;
                    reasons.Add("High-level shelf preservation for C-velocity SKU");
                }
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
            .Take(10)
            .ToList();
    }
}

