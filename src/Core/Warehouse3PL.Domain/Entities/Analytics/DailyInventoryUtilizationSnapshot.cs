using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Aggregated read model projection tracking facility space utilization, pallet rack occupancy, cubic fill, and dead capital.
/// Sourced from Phase 5.2 daily inventory snapshots and facility location ledgers.
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation & KPI Engine.
/// </summary>
public class DailyInventoryUtilizationSnapshot : AuditableEntity<Guid>
{
    public Guid FacilityId { get; set; }
    public Guid? ClientId { get; set; }
    public DateTime DateUtc { get; set; }
    public TimeGrain TimeGrain { get; set; } = TimeGrain.Daily;

    // Physical Capacity & Occupancy
    public int TotalStorageLocations { get; set; }
    public int OccupiedStorageLocations { get; set; }
    public decimal PalletPositionOccupancyPercent { get; set; }

    // Volumetric Cube Fill
    public decimal TotalCapacityCubicMeters { get; set; }
    public decimal OccupiedCubicMeters { get; set; }
    public decimal VolumetricCubeFillPercent { get; set; }

    // Inventory Composition & Dead Capital
    public int TotalActiveSkus { get; set; }
    public int TotalInventoryUnits { get; set; }
    public decimal TotalInventoryValue { get; set; }
    public int DeadStockSkusCount { get; set; }
    public int DeadStockUnitsCount { get; set; }
    public decimal DeadStockValue { get; set; }
    public decimal DeadStockRatioPercent { get; set; }

    public DailyInventoryUtilizationSnapshot()
    {
        Id = Guid.NewGuid();
    }

    public static DailyInventoryUtilizationSnapshot Create(
        Guid facilityId,
        DateTime dateUtc,
        TimeGrain timeGrain = TimeGrain.Daily,
        Guid? clientId = null)
    {
        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        }

        return new DailyInventoryUtilizationSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            ClientId = clientId,
            DateUtc = dateUtc.Kind == DateTimeKind.Utc ? dateUtc : DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc),
            TimeGrain = timeGrain,
            TotalStorageLocations = 0,
            OccupiedStorageLocations = 0,
            PalletPositionOccupancyPercent = 0m,
            TotalCapacityCubicMeters = 0m,
            OccupiedCubicMeters = 0m,
            VolumetricCubeFillPercent = 0m,
            TotalActiveSkus = 0,
            TotalInventoryUnits = 0,
            TotalInventoryValue = 0m,
            DeadStockSkusCount = 0,
            DeadStockUnitsCount = 0,
            DeadStockValue = 0m,
            DeadStockRatioPercent = 0m
        };
    }

    public void UpdateCapacity(
        int totalLocations,
        int occupiedLocations,
        decimal totalCbm,
        decimal occupiedCbm)
    {
        if (totalLocations < 0) throw new ValidationException(nameof(totalLocations), "Total locations cannot be negative.");
        if (occupiedLocations < 0) throw new ValidationException(nameof(occupiedLocations), "Occupied locations cannot be negative.");
        if (occupiedLocations > totalLocations && totalLocations > 0)
        {
            throw new ValidationException(nameof(occupiedLocations), "Occupied locations cannot exceed total locations.");
        }
        if (totalCbm < 0m) throw new ValidationException(nameof(totalCbm), "Total CBM cannot be negative.");
        if (occupiedCbm < 0m) throw new ValidationException(nameof(occupiedCbm), "Occupied CBM cannot be negative.");

        TotalStorageLocations = totalLocations;
        OccupiedStorageLocations = occupiedLocations;
        TotalCapacityCubicMeters = totalCbm;
        OccupiedCubicMeters = occupiedCbm;

        RecalculateMetrics();
    }

    public void UpdateInventoryComposition(
        int activeSkus,
        int totalUnits,
        decimal totalValue,
        int deadSkus,
        int deadUnits,
        decimal deadValue)
    {
        if (activeSkus < 0) throw new ValidationException(nameof(activeSkus), "Active SKUs cannot be negative.");
        if (totalUnits < 0) throw new ValidationException(nameof(totalUnits), "Total units cannot be negative.");
        if (totalValue < 0m) throw new ValidationException(nameof(totalValue), "Total value cannot be negative.");
        if (deadSkus < 0) throw new ValidationException(nameof(deadSkus), "Dead SKUs cannot be negative.");
        if (deadUnits < 0) throw new ValidationException(nameof(deadUnits), "Dead units cannot be negative.");
        if (deadValue < 0m) throw new ValidationException(nameof(deadValue), "Dead value cannot be negative.");

        TotalActiveSkus = activeSkus;
        TotalInventoryUnits = totalUnits;
        TotalInventoryValue = totalValue;
        DeadStockSkusCount = deadSkus;
        DeadStockUnitsCount = deadUnits;
        DeadStockValue = deadValue;

        RecalculateMetrics();
    }

    public void RecalculateMetrics()
    {
        PalletPositionOccupancyPercent = TotalStorageLocations > 0
            ? Math.Round((decimal)OccupiedStorageLocations / TotalStorageLocations * 100m, 2)
            : 0m;

        VolumetricCubeFillPercent = TotalCapacityCubicMeters > 0m
            ? Math.Round((OccupiedCubicMeters / TotalCapacityCubicMeters) * 100m, 2)
            : 0m;

        DeadStockRatioPercent = TotalInventoryValue > 0m
            ? Math.Round((DeadStockValue / TotalInventoryValue) * 100m, 2)
            : 0m;
    }
}

