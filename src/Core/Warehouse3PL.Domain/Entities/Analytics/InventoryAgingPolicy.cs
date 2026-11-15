using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Configurable inventory aging thresholds and dead-stock policy per client or facility.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public class InventoryAgingPolicy : AuditableEntity<Guid>
{
    public Guid? FacilityId { get; set; }
    public Guid? ClientId { get; set; }
    public string Name { get; set; } = "Standard Aging Policy";

    // Thresholds in Days
    public int SlowMovingThresholdDays { get; set; } = 90;
    public int DormantThresholdDays { get; set; } = 180;
    public int DeadStockThresholdDays { get; set; } = 365;
    public int NearExpiryThresholdDays { get; set; } = 60;
    public int CriticalExpiryThresholdDays { get; set; } = 30;

    public string Currency { get; set; } = "USD";
    public decimal DailyHoldingCostPerCbm { get; set; } = 0.05m;
    public decimal DailyHoldingCostPerPallet { get; set; } = 0.50m;

    public InventoryAgingBasis DefaultBasis { get; set; } = InventoryAgingBasis.ReceiptDate;
    public int Version { get; set; } = 1;
    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public InventoryAgingPolicy()
    {
        Id = Guid.NewGuid();
    }

    public static InventoryAgingPolicy Create(
        string name,
        Guid? facilityId = null,
        Guid? clientId = null,
        int slowMovingDays = 90,
        int dormantDays = 180,
        int deadStockDays = 365,
        int nearExpiryDays = 60,
        int criticalExpiryDays = 30,
        InventoryAgingBasis defaultBasis = InventoryAgingBasis.ReceiptDate,
        int version = 1,
        DateTime? effectiveFromUtc = null,
        string currency = "USD",
        decimal dailyHoldingCostPerCbm = 0.05m,
        decimal dailyHoldingCostPerPallet = 0.50m)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException(nameof(name), "Policy name cannot be empty.");

        if (slowMovingDays <= 0)
            throw new ValidationException(nameof(slowMovingDays), "Slow moving threshold must be positive.");

        if (dormantDays <= slowMovingDays)
            throw new ValidationException(nameof(dormantDays), "Dormant threshold must exceed slow moving threshold.");

        if (deadStockDays <= dormantDays)
            throw new ValidationException(nameof(deadStockDays), "Dead stock threshold must exceed dormant threshold.");

        if (criticalExpiryDays <= 0 || nearExpiryDays <= criticalExpiryDays)
            throw new ValidationException(nameof(nearExpiryDays), "Near expiry threshold must exceed critical expiry threshold.");

        return new InventoryAgingPolicy
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            ClientId = clientId,
            Name = name.Trim(),
            SlowMovingThresholdDays = slowMovingDays,
            DormantThresholdDays = dormantDays,
            DeadStockThresholdDays = deadStockDays,
            NearExpiryThresholdDays = nearExpiryDays,
            CriticalExpiryThresholdDays = criticalExpiryDays,
            DefaultBasis = defaultBasis,
            Version = version,
            EffectiveFromUtc = effectiveFromUtc ?? DateTime.UtcNow,
            Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant(),
            DailyHoldingCostPerCbm = Math.Max(0m, dailyHoldingCostPerCbm),
            DailyHoldingCostPerPallet = Math.Max(0m, dailyHoldingCostPerPallet),
            IsActive = true
        };
    }

    /// <summary>
    /// Categorizes elapsed age into a discrete reporting bucket.
    /// </summary>
    public InventoryAgingBucket ClassifyBucket(int ageDays)
    {
        if (ageDays <= 30) return InventoryAgingBucket.Current_0_30;
        if (ageDays <= 60) return InventoryAgingBucket.Aging_31_60;
        if (ageDays <= 90) return InventoryAgingBucket.Aging_61_90;
        if (ageDays <= DormantThresholdDays) return InventoryAgingBucket.SlowMoving_91_180;
        if (ageDays <= DeadStockThresholdDays) return InventoryAgingBucket.Dormant_181_365;
        return InventoryAgingBucket.DeadStock_365Plus;
    }

    /// <summary>
    /// Categorizes inventory movement velocity based on days since last demand/movement.
    /// </summary>
    public InventoryMovementVelocityCategory ClassifyVelocity(int daysSinceLastDemand)
    {
        if (daysSinceLastDemand <= 30) return InventoryMovementVelocityCategory.FastMoving;
        if (daysSinceLastDemand <= SlowMovingThresholdDays) return InventoryMovementVelocityCategory.MediumMoving;
        if (daysSinceLastDemand <= DormantThresholdDays) return InventoryMovementVelocityCategory.SlowMoving;
        if (daysSinceLastDemand <= DeadStockThresholdDays) return InventoryMovementVelocityCategory.NonMoving;
        return InventoryMovementVelocityCategory.DeadStock;
    }

    public bool IsAged(int receiptAgeDays) => receiptAgeDays >= SlowMovingThresholdDays;
    public bool IsSlowMoving(int ageDays) => ageDays >= SlowMovingThresholdDays && ageDays < DormantThresholdDays;
    public bool IsSlowMovingVelocity(InventoryMovementVelocityCategory velocity) => velocity == InventoryMovementVelocityCategory.SlowMoving;
    public bool IsNonMovingVelocity(InventoryMovementVelocityCategory velocity) => velocity == InventoryMovementVelocityCategory.NonMoving;
    
    public bool IsDeadStock(int ageDays) => ageDays >= DeadStockThresholdDays;
    public bool IsDeadStock(int primaryAgeDays, bool hasDemandHistory, int? demandInactivityDays)
    {
        if (hasDemandHistory && demandInactivityDays.HasValue)
        {
            return demandInactivityDays.Value >= DeadStockThresholdDays;
        }

        // Never demanded since receipt: dead stock once primary receipt age reaches threshold
        return primaryAgeDays >= DeadStockThresholdDays;
    }

    public bool IsNearExpiry(int daysUntilExpiry) => daysUntilExpiry > 0 && daysUntilExpiry <= NearExpiryThresholdDays;
    public bool IsCriticalExpiry(int daysUntilExpiry) => daysUntilExpiry > 0 && daysUntilExpiry <= CriticalExpiryThresholdDays;
    public bool IsExpired(int daysUntilExpiry) => daysUntilExpiry <= 0;
}

