namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Configurable duration intervals for inventory aging reporting and holding cost analysis.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public enum InventoryAgingBucket
{
    Current_0_30 = 1,
    Aging_31_60 = 2,
    Aging_61_90 = 3,
    SlowMoving_91_180 = 4,
    Dormant_181_365 = 5,
    DeadStock_365Plus = 6
}

