using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Services.Packing;

/// <summary>
/// Algorithmic cartonization engine recommending optimal packaging materials.
/// Evaluates 3D physical orthogonal bounding-box rotation, weight capacities, volumetric limits,
/// and client/facility scoping, supporting multi-carton partitioning when demand exceeds single-carton capacity.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class CartonizationService : ICartonizationService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<CartonizationService> _logger;

    public CartonizationService(
        IApplicationDbContext context,
        ILogger<CartonizationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<CartonizationRecommendation> RecommendCartonizationAsync(
        CartonizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new CartonizationRecommendation();

        // 1. Resolve Items (from request or from OutboundOrder lines)
        var itemsToPack = new List<CartonizationItemRequest>(request.Items);
        if (itemsToPack.Count == 0 && request.OutboundOrderId.HasValue)
        {
            var order = await _context.OutboundOrders
                .Include(o => o.Lines)
                    .ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(o => o.Id == request.OutboundOrderId.Value, cancellationToken);

            if (order != null)
            {
                foreach (var line in order.Lines.Where(l => !l.IsDeleted && l.QuantityAllocated > 0))
                {
                    itemsToPack.Add(new CartonizationItemRequest
                    {
                        ItemId = line.ItemId,
                        Sku = line.Item.Sku,
                        Quantity = line.QuantityAllocated,
                        LengthCm = line.Item.LengthCm > 0 ? line.Item.LengthCm : 10m,
                        WidthCm = line.Item.WidthCm > 0 ? line.Item.WidthCm : 10m,
                        HeightCm = line.Item.HeightCm > 0 ? line.Item.HeightCm : 10m,
                        WeightKg = line.Item.WeightKg > 0 ? line.Item.WeightKg : 0.5m
                    });
                }
            }
        }

        if (itemsToPack.Count == 0)
        {
            result.IsSuccess = false;
            result.Message = "No items specified for cartonization.";
            return result;
        }

        // Hydrate item dimensions if zero
        var missingItemIds = itemsToPack.Where(i => i.LengthCm <= 0 || i.WidthCm <= 0 || i.HeightCm <= 0 || i.WeightKg <= 0)
                                        .Select(i => i.ItemId)
                                        .Distinct()
                                        .ToList();

        if (missingItemIds.Count > 0)
        {
            var dbItems = await _context.Items
                .Where(i => missingItemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, cancellationToken);

            foreach (var reqItem in itemsToPack)
            {
                if (dbItems.TryGetValue(reqItem.ItemId, out var dbItem))
                {
                    if (reqItem.LengthCm <= 0) reqItem.LengthCm = dbItem.LengthCm > 0 ? dbItem.LengthCm : 10m;
                    if (reqItem.WidthCm <= 0) reqItem.WidthCm = dbItem.WidthCm > 0 ? dbItem.WidthCm : 10m;
                    if (reqItem.HeightCm <= 0) reqItem.HeightCm = dbItem.HeightCm > 0 ? dbItem.HeightCm : 10m;
                    if (reqItem.WeightKg <= 0) reqItem.WeightKg = dbItem.WeightKg > 0 ? dbItem.WeightKg : 0.5m;
                }
            }
        }

        // 2. Fetch Available Packaging Materials for Tenant & Facility
        var availableMaterials = await _context.PackagingMaterials
            .Where(m => m.FacilityId == request.FacilityId &&
                        (m.ClientId == request.ClientId || m.ClientId == null) &&
                        m.IsActive &&
                        !m.IsDeleted)
            .OrderBy(m => m.LengthCm * m.WidthCm * m.HeightCm)
            .ToListAsync(cancellationToken);

        if (availableMaterials.Count == 0)
        {
            result.IsSuccess = false;
            result.Message = $"No active packaging materials configured for facility '{request.FacilityId}'.";
            return result;
        }

        // 3. Verify Individual Item Physical Fit Against Catalog
        foreach (var item in itemsToPack)
        {
            var anyCanFit = availableMaterials.Any(m => CanFitSingleUnit(item.LengthCm, item.WidthCm, item.HeightCm, m.LengthCm, m.WidthCm, m.HeightCm));
            if (!anyCanFit)
            {
                result.IsSuccess = false;
                result.Message = $"Item '{item.Sku}' dimensions ({item.LengthCm}x{item.WidthCm}x{item.HeightCm} cm) physically exceed all available packaging materials.";
                return result;
            }
        }

        var utilizationFactor = request.VolumeUtilizationFactor > 0m && request.VolumeUtilizationFactor <= 1m
            ? request.VolumeUtilizationFactor
            : 0.85m;

        decimal totalNetWeight = itemsToPack.Sum(i => i.WeightKg * i.Quantity);
        decimal totalItemVolume = itemsToPack.Sum(i => i.VolumeCm3 * i.Quantity);

        // 4. Try Single Carton Recommendation (Smallest Compatible Box)
        PackagingMaterial? bestSingleBox = null;

        // Check if preferred material requested
        if (request.PreferredPackagingMaterialId.HasValue)
        {
            var preferred = availableMaterials.FirstOrDefault(m => m.Id == request.PreferredPackagingMaterialId.Value);
            if (preferred != null && CanHoldAllItems(preferred, itemsToPack, totalNetWeight, totalItemVolume, utilizationFactor))
            {
                bestSingleBox = preferred;
            }
        }

        if (bestSingleBox == null)
        {
            // Find smallest volume material that can hold all items
            foreach (var mat in availableMaterials)
            {
                if (CanHoldAllItems(mat, itemsToPack, totalNetWeight, totalItemVolume, utilizationFactor))
                {
                    bestSingleBox = mat;
                    break;
                }
            }
        }

        if (bestSingleBox != null)
        {
            // All items fit into a single carton
            var singlePkg = BuildPackageRecommendation(1, bestSingleBox, itemsToPack, totalNetWeight, totalItemVolume);
            result.RecommendedPackages.Add(singlePkg);
            result.IsSuccess = true;
            result.Message = $"Recommended 1 carton: '{bestSingleBox.Code}' ({bestSingleBox.Name}).";
            return result;
        }

        // 5. Multi-Carton Partitioning (Items exceed single carton capacity or weight)
        _logger.LogInformation(
            "Items exceed single-carton capacity (Total Weight: {Weight}kg, Total Vol: {Vol}cm3). Partitioning into multiple cartons.",
            totalNetWeight, totalItemVolume);

        var partitions = PartitionItemsIntoCartons(itemsToPack, availableMaterials, utilizationFactor);
        if (partitions.Count == 0)
        {
            result.IsSuccess = false;
            result.Message = "Unable to partition items into available packaging materials.";
            return result;
        }

        int sequence = 1;
        foreach (var partition in partitions)
        {
            decimal pWeight = partition.AssignedItems.Sum(i => i.WeightKg * i.Quantity);
            decimal pVol = partition.AssignedItems.Sum(i => i.VolumeCm3 * i.Quantity);
            result.RecommendedPackages.Add(BuildPackageRecommendation(sequence++, partition.Material, partition.AssignedItems, pWeight, pVol));
        }

        result.IsSuccess = true;
        result.Message = $"Recommended {result.RecommendedPackageCount} cartons to fulfill demand.";
        return result;
    }

    public async Task<bool> ValidateCartonFitAsync(
        Guid packagingMaterialId,
        IEnumerable<CartonizationItemRequest> items,
        CancellationToken cancellationToken = default)
    {
        var material = await _context.PackagingMaterials
            .FirstOrDefaultAsync(m => m.Id == packagingMaterialId && !m.IsDeleted, cancellationToken);

        if (material == null) return false;

        var itemList = items.ToList();
        decimal totalNetWeight = itemList.Sum(i => i.WeightKg * i.Quantity);
        decimal totalVolume = itemList.Sum(i => i.VolumeCm3 * i.Quantity);

        return CanHoldAllItems(material, itemList, totalNetWeight, totalVolume, 0.95m);
    }

    #region Internal Algorithmic Helpers

    /// <summary>
    /// Checks 3D orthogonal bounding-box rotation. An item fits in a box if sorted item dimensions
    /// are all less than or equal to sorted box dimensions.
    /// </summary>
    private static bool CanFitSingleUnit(decimal itemL, decimal itemW, decimal itemH, decimal boxL, decimal boxW, decimal boxH)
    {
        var itemDims = new[] { itemL, itemW, itemH }.OrderBy(x => x).ToArray();
        var boxDims = new[] { boxL, boxW, boxH }.OrderBy(x => x).ToArray();

        return itemDims[0] <= boxDims[0] &&
               itemDims[1] <= boxDims[1] &&
               itemDims[2] <= boxDims[2];
    }

    private static bool CanHoldAllItems(
        PackagingMaterial material,
        List<CartonizationItemRequest> items,
        decimal totalNetWeight,
        decimal totalItemVolume,
        decimal utilizationFactor)
    {
        // 1. Max Weight Capacity Check
        if (material.MaxWeightKg > 0 && totalNetWeight > material.MaxWeightKg)
        {
            return false;
        }

        // 2. Usable Volume Capacity Check
        var usableVolume = material.CalculatedVolumeCm3 * utilizationFactor;
        if (totalItemVolume > usableVolume)
        {
            return false;
        }

        // 3. Dimensional bounding box check for every individual item
        foreach (var item in items)
        {
            if (!CanFitSingleUnit(item.LengthCm, item.WidthCm, item.HeightCm, material.LengthCm, material.WidthCm, material.HeightCm))
            {
                return false;
            }
        }

        return true;
    }

    private class CartonPartition
    {
        public PackagingMaterial Material { get; set; } = null!;
        public List<CartonizationItemRequest> AssignedItems { get; set; } = [];
        public decimal CurrentWeight => AssignedItems.Sum(i => i.WeightKg * i.Quantity);
        public decimal CurrentVolume => AssignedItems.Sum(i => i.VolumeCm3 * i.Quantity);
    }

    private static List<CartonPartition> PartitionItemsIntoCartons(
        List<CartonizationItemRequest> items,
        List<PackagingMaterial> materials,
        decimal utilizationFactor)
    {
        var partitions = new List<CartonPartition>();

        // Flatten items into single discrete units for bin packing, sorted descending by volume
        var discreteUnits = new List<CartonizationItemRequest>();
        foreach (var item in items)
        {
            for (int q = 0; q < (int)Math.Ceiling(item.Quantity); q++)
            {
                discreteUnits.Add(new CartonizationItemRequest
                {
                    ItemId = item.ItemId,
                    Sku = item.Sku,
                    Quantity = 1m,
                    LengthCm = item.LengthCm,
                    WidthCm = item.WidthCm,
                    HeightCm = item.HeightCm,
                    WeightKg = item.WeightKg
                });
            }
        }

        discreteUnits = discreteUnits.OrderByDescending(u => u.VolumeCm3).ThenByDescending(u => u.WeightKg).ToList();

        // Greedy First-Fit Decreasing
        foreach (var unit in discreteUnits)
        {
            bool placed = false;
            foreach (var partition in partitions)
            {
                var newWeight = partition.CurrentWeight + unit.WeightKg;
                var newVolume = partition.CurrentVolume + unit.VolumeCm3;

                if ((partition.Material.MaxWeightKg <= 0 || newWeight <= partition.Material.MaxWeightKg) &&
                    newVolume <= partition.Material.CalculatedVolumeCm3 * utilizationFactor &&
                    CanFitSingleUnit(unit.LengthCm, unit.WidthCm, unit.HeightCm, partition.Material.LengthCm, partition.Material.WidthCm, partition.Material.HeightCm))
                {
                    // Consolidate quantity if SKU already in partition
                    var existing = partition.AssignedItems.FirstOrDefault(i => i.ItemId == unit.ItemId);
                    if (existing != null)
                    {
                        existing.Quantity += 1m;
                    }
                    else
                    {
                        partition.AssignedItems.Add(new CartonizationItemRequest
                        {
                            ItemId = unit.ItemId,
                            Sku = unit.Sku,
                            Quantity = 1m,
                            LengthCm = unit.LengthCm,
                            WidthCm = unit.WidthCm,
                            HeightCm = unit.HeightCm,
                            WeightKg = unit.WeightKg
                        });
                    }
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                // Open a new partition with the smallest box that fits this unit
                var suitableMaterial = materials.FirstOrDefault(m =>
                    (m.MaxWeightKg <= 0 || unit.WeightKg <= m.MaxWeightKg) &&
                    unit.VolumeCm3 <= m.CalculatedVolumeCm3 * utilizationFactor &&
                    CanFitSingleUnit(unit.LengthCm, unit.WidthCm, unit.HeightCm, m.LengthCm, m.WidthCm, m.HeightCm));

                if (suitableMaterial == null)
                {
                    suitableMaterial = materials.Last(); // largest fallback
                }

                var newPartition = new CartonPartition
                {
                    Material = suitableMaterial,
                    AssignedItems =
                    [
                        new CartonizationItemRequest
                        {
                            ItemId = unit.ItemId,
                            Sku = unit.Sku,
                            Quantity = 1m,
                            LengthCm = unit.LengthCm,
                            WidthCm = unit.WidthCm,
                            HeightCm = unit.HeightCm,
                            WeightKg = unit.WeightKg
                        }
                    ]
                };
                partitions.Add(newPartition);
            }
        }

        // Downgrade optimization: if an opened carton can be downgraded to a smaller box in catalog, do it
        foreach (var partition in partitions)
        {
            var pWeight = partition.CurrentWeight;
            var pVol = partition.CurrentVolume;
            var smallerBox = materials.FirstOrDefault(m =>
                m.CalculatedVolumeCm3 < partition.Material.CalculatedVolumeCm3 &&
                CanHoldAllItems(m, partition.AssignedItems, pWeight, pVol, utilizationFactor));

            if (smallerBox != null)
            {
                partition.Material = smallerBox;
            }
        }

        return partitions;
    }

    private static PackageRecommendation BuildPackageRecommendation(
        int sequence,
        PackagingMaterial material,
        List<CartonizationItemRequest> items,
        decimal netWeight,
        decimal itemVolume)
    {
        var boxVol = material.CalculatedVolumeCm3;
        var estGross = netWeight + material.TareWeightKg;
        var volWeight = material.CalculateVolumetricWeightKg(5000m);
        var utilPct = boxVol > 0 ? Math.Round((itemVolume / boxVol) * 100m, 1) : 0m;

        var rec = new PackageRecommendation
        {
            PackageIndex = sequence,
            PackagingMaterialId = material.Id,
            PackagingMaterialCode = material.Code,
            PackagingMaterialName = material.Name,
            MaterialType = material.MaterialType,
            BoxLengthCm = material.LengthCm,
            BoxWidthCm = material.WidthCm,
            BoxHeightCm = material.HeightCm,
            BoxMaxWeightKg = material.MaxWeightKg,
            TareWeightKg = material.TareWeightKg,
            EstimatedNetWeightKg = Math.Round(netWeight, 2),
            EstimatedGrossWeightKg = Math.Round(estGross, 2),
            EstimatedVolumeCm3 = Math.Round(itemVolume, 2),
            BoxCapacityVolumeCm3 = Math.Round(boxVol, 2),
            VolumetricWeightKg = volWeight,
            VolumeUtilizationPercentage = utilPct,
            Items = items.Select(i => new CartonItemRecommendation
            {
                ItemId = i.ItemId,
                Sku = i.Sku,
                Quantity = i.Quantity,
                TotalWeightKg = Math.Round(i.WeightKg * i.Quantity, 2),
                TotalVolumeCm3 = Math.Round(i.VolumeCm3 * i.Quantity, 2)
            }).ToList()
        };

        return rec;
    }

    #endregion
}

