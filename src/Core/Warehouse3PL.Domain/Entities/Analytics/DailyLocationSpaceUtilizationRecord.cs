using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Granular read-model projection recording location-level space utilization, volumetric cube fill, and pallet occupancy.
/// Invariant: Pallet position occupancy and cubic-meter utilization are strictly independent dimensions.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public class DailyLocationSpaceUtilizationRecord : AuditableEntity<Guid>
{
    public Guid FacilityId { get; set; }
    public Guid? ZoneId { get; set; }
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public LocationType LocationType { get; set; } = LocationType.Bin;

    public DateTime SnapshotDateUtc { get; set; }

    // Physical Coordinates for Heatmap Rendering
    public string? AisleCode { get; set; }
    public string? RackCode { get; set; }
    public int? ShelfLevel { get; set; }
    public int? BinNumber { get; set; }
    public decimal? XCoord { get; set; }
    public decimal? YCoord { get; set; }
    public decimal? ZCoord { get; set; }

    // Independent Metric 1: Volumetric Cube Fill & Usable Clearances
    public decimal PhysicalCubicMeters { get; set; }
    public decimal UsableCapacityFactor { get; set; } = 0.85m; // 15% safety clearance for flue space & fire sprinklers
    public decimal UsableCubicMeters { get; set; }
    public decimal OccupiedCubicMeters { get; set; }
    public decimal AvailableCubicMeters { get; set; }
    public decimal CubeUtilizationPercent { get; set; } // Uncapped! Can exceed 100%

    // Independent Metric 2: Pallet Position Occupancy
    public int PhysicalPalletPositions { get; set; }
    public int UsablePalletPositions { get; set; }
    public int OccupiedPalletPositions { get; set; }
    public int AvailablePalletPositions { get; set; }
    public decimal PalletOccupancyPercent { get; set; } // Uncapped! Can exceed 100%

    // Operational Availability & Blocking Status
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }

    // Capacity & SKU Distribution
    public decimal MaxWeightKg { get; set; }
    public decimal OccupiedWeightKg { get; set; }
    public decimal WeightUtilizationPercent { get; set; }
    public bool IsWeightConstrained { get; set; }

    // Over-Capacity Indicators
    public bool IsOverCapacity { get; set; }
    public decimal OverCapacityExcessCbm { get; set; }

    public int DistinctSkuCount { get; set; }
    public decimal TotalUnitsStored { get; set; }
    public bool IsMixedSkuLocation => DistinctSkuCount > 1;
    public bool HasMissingDimensionsFallback { get; set; }

    // Status-Partitioned Quantities (Reconciles with Authoritative Stock)
    public decimal UnitsAvailable { get; set; }
    public decimal UnitsAllocated { get; set; }
    public decimal UnitsReserved { get; set; }
    public decimal UnitsQuarantined { get; set; }
    public decimal UnitsDamaged { get; set; }
    public decimal UnitsOnHold { get; set; }

    // Status-Partitioned Occupied Volume (CBM)
    public decimal OccupiedCbmAvailable { get; set; }
    public decimal OccupiedCbmAllocated { get; set; }
    public decimal OccupiedCbmReserved { get; set; }
    public decimal OccupiedCbmQuarantined { get; set; }
    public decimal OccupiedCbmDamaged { get; set; }
    public decimal OccupiedCbmOnHold { get; set; }

    public LocationSpaceUtilizationClassification SpaceClassification { get; set; } = LocationSpaceUtilizationClassification.Empty;

    public DailyLocationSpaceUtilizationRecord()
    {
        Id = Guid.NewGuid();
    }

    public static DailyLocationSpaceUtilizationRecord Create(
        Guid facilityId,
        Guid locationId,
        string locationCode,
        DateTime snapshotDateUtc,
        decimal usableCbm,
        decimal occupiedCbm,
        int usablePalletPositions,
        int occupiedPalletPositions,
        Guid? zoneId = null,
        LocationType locationType = LocationType.Bin,
        string? aisleCode = null,
        string? rackCode = null,
        int? shelfLevel = null,
        int? binNumber = null,
        decimal? x = null,
        decimal? y = null,
        decimal? z = null,
        decimal maxWeightKg = 0m,
        decimal occupiedWeightKg = 0m,
        int distinctSkuCount = 0,
        decimal totalUnitsStored = 0m,
        bool hasMissingDimensions = false,
        decimal? physicalCbm = null,
        decimal usableCapacityFactor = 0.85m,
        int? physicalPalletPositions = null,
        bool isBlocked = false,
        string? blockReason = null,
        decimal unitsAvailable = 0m,
        decimal unitsAllocated = 0m,
        decimal unitsReserved = 0m,
        decimal unitsQuarantined = 0m,
        decimal unitsDamaged = 0m,
        decimal unitsOnHold = 0m,
        decimal occupiedCbmAvailable = 0m,
        decimal occupiedCbmAllocated = 0m,
        decimal occupiedCbmReserved = 0m,
        decimal occupiedCbmQuarantined = 0m,
        decimal occupiedCbmDamaged = 0m,
        decimal occupiedCbmOnHold = 0m)
    {
        if (facilityId == Guid.Empty) throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        if (locationId == Guid.Empty) throw new ValidationException(nameof(locationId), "Location ID is required.");
        if (string.IsNullOrWhiteSpace(locationCode)) throw new ValidationException(nameof(locationCode), "Location code is required.");

        var factor = usableCapacityFactor > 0m && usableCapacityFactor <= 1.0m ? usableCapacityFactor : 0.85m;
        var pCbm = physicalCbm.HasValue && physicalCbm.Value > 0m 
            ? physicalCbm.Value 
            : (usableCbm > 0m ? Math.Round(usableCbm / factor, 4) : 0m);

        var pPallets = physicalPalletPositions.HasValue && physicalPalletPositions.Value > 0
            ? physicalPalletPositions.Value
            : usablePalletPositions;

        var record = new DailyLocationSpaceUtilizationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            ZoneId = zoneId,
            LocationId = locationId,
            LocationCode = locationCode.Trim(),
            LocationType = locationType,
            SnapshotDateUtc = snapshotDateUtc.Date,
            AisleCode = aisleCode,
            RackCode = rackCode,
            ShelfLevel = shelfLevel,
            BinNumber = binNumber,
            XCoord = x,
            YCoord = y,
            ZCoord = z,
            PhysicalCubicMeters = Math.Max(0m, pCbm),
            UsableCapacityFactor = factor,
            UsableCubicMeters = Math.Max(0m, usableCbm),
            OccupiedCubicMeters = Math.Max(0m, occupiedCbm),
            PhysicalPalletPositions = Math.Max(0, pPallets),
            UsablePalletPositions = Math.Max(0, usablePalletPositions),
            OccupiedPalletPositions = Math.Max(0, occupiedPalletPositions),
            IsBlocked = isBlocked,
            BlockReason = blockReason,
            MaxWeightKg = Math.Max(0m, maxWeightKg),
            OccupiedWeightKg = Math.Max(0m, occupiedWeightKg),
            DistinctSkuCount = Math.Max(0, distinctSkuCount),
            TotalUnitsStored = Math.Max(0m, totalUnitsStored),
            HasMissingDimensionsFallback = hasMissingDimensions,
            UnitsAvailable = Math.Max(0m, unitsAvailable),
            UnitsAllocated = Math.Max(0m, unitsAllocated),
            UnitsReserved = Math.Max(0m, unitsReserved),
            UnitsQuarantined = Math.Max(0m, unitsQuarantined),
            UnitsDamaged = Math.Max(0m, unitsDamaged),
            UnitsOnHold = Math.Max(0m, unitsOnHold),
            OccupiedCbmAvailable = Math.Max(0m, occupiedCbmAvailable),
            OccupiedCbmAllocated = Math.Max(0m, occupiedCbmAllocated),
            OccupiedCbmReserved = Math.Max(0m, occupiedCbmReserved),
            OccupiedCbmQuarantined = Math.Max(0m, occupiedCbmQuarantined),
            OccupiedCbmDamaged = Math.Max(0m, occupiedCbmDamaged),
            OccupiedCbmOnHold = Math.Max(0m, occupiedCbmOnHold)
        };

        record.RecalculateMetrics();
        return record;
    }

    /// <summary>
    /// Independently computes Volumetric Cube Fill % and Pallet Occupancy %.
    /// Invariant: Pallet positions and cubic meters are never conflated or averaged together.
    /// Invariant: Over-capacity conditions are preserved uncapped (e.g. 115%) rather than truncated at 100%.
    /// </summary>
    public void RecalculateMetrics()
    {
        // 1. Available Capacity
        AvailableCubicMeters = Math.Max(0m, UsableCubicMeters - OccupiedCubicMeters);
        AvailablePalletPositions = Math.Max(0, UsablePalletPositions - OccupiedPalletPositions);

        // 2. Independent Volumetric Cube Fill % (Uncapped!)
        if (UsableCubicMeters > 0m)
        {
            CubeUtilizationPercent = Math.Round((OccupiedCubicMeters / UsableCubicMeters) * 100m, 2);
        }
        else
        {
            CubeUtilizationPercent = OccupiedCubicMeters > 0m ? 100m : 0m;
        }

        // 3. Independent Pallet Position Occupancy % (Uncapped!)
        if (UsablePalletPositions > 0)
        {
            PalletOccupancyPercent = Math.Round(((decimal)OccupiedPalletPositions / (decimal)UsablePalletPositions) * 100m, 2);
        }
        else
        {
            PalletOccupancyPercent = OccupiedPalletPositions > 0 ? 100m : 0m;
        }

        // 4. Weight Utilization % (Uncapped!)
        if (MaxWeightKg > 0m)
        {
            WeightUtilizationPercent = Math.Round((OccupiedWeightKg / MaxWeightKg) * 100m, 2);
            IsWeightConstrained = WeightUtilizationPercent >= 95m || OccupiedWeightKg > MaxWeightKg;
        }
        else
        {
            WeightUtilizationPercent = 0m;
            IsWeightConstrained = false;
        }

        // 5. Over-Capacity Evaluation (Critical Invariant: Detect overfilling beyond usable capacity)
        IsOverCapacity = CubeUtilizationPercent > 100m || PalletOccupancyPercent > 100m || WeightUtilizationPercent > 100m;
        OverCapacityExcessCbm = Math.Max(0m, OccupiedCubicMeters - UsableCubicMeters);

        // 6. Space Classification based on primary capacity and operational state
        var primaryPercent = UsableCubicMeters > 0m ? CubeUtilizationPercent : PalletOccupancyPercent;

        if (IsBlocked)
        {
            SpaceClassification = LocationSpaceUtilizationClassification.BlockedOrUnavailable;
        }
        else if (IsOverCapacity || primaryPercent > 100m)
        {
            SpaceClassification = LocationSpaceUtilizationClassification.OverCapacity;
        }
        else if (TotalUnitsStored == 0 && OccupiedPalletPositions == 0)
        {
            SpaceClassification = LocationSpaceUtilizationClassification.Empty;
        }
        else if (primaryPercent < 50m)
        {
            SpaceClassification = LocationSpaceUtilizationClassification.Underutilized;
        }
        else if (primaryPercent <= 85m)
        {
            SpaceClassification = LocationSpaceUtilizationClassification.Optimal;
        }
        else if (primaryPercent <= 95m)
        {
            SpaceClassification = LocationSpaceUtilizationClassification.NearCapacity;
        }
        else
        {
            SpaceClassification = LocationSpaceUtilizationClassification.FullOrOverfilled;
        }
    }
}

