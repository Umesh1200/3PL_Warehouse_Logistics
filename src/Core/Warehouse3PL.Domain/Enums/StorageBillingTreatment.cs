namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Contractual billing treatment applied to a specific InventoryHoldingStatus category.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public enum StorageBillingTreatment
{
    /// <summary>
    /// Inventory is fully billable at the standard rate card schedule rate.
    /// </summary>
    BillableStandard = 1,

    /// <summary>
    /// Inventory is contractually excluded/exempt from daily storage charges (e.g. InTransit, ReturnPendingInspection).
    /// </summary>
    Excluded = 2,

    /// <summary>
    /// Inventory incurs a special holding rate, hazardous surcharge, or custom fee.
    /// </summary>
    BillableSpecialRate = 3
}

