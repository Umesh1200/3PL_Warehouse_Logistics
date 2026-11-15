namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of a warehouse return receiving and quality inspection session.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4.5: Return Facility Receiving &amp; Inspection.
/// </summary>
public enum ReturnReceivingSessionStatus
{
    /// <summary>Session started; expected items loaded from inbound handoff and package.</summary>
    Started = 1,

    /// <summary>Physical package arrival and barcode scan confirmed by receiving operator.</summary>
    PhysicalReceiptConfirmed = 2,

    /// <summary>Quality inspection performed; disposition assigned to all received line items.</summary>
    Inspected = 3,

    /// <summary>Receiving completed, inventory posted through transactions, and return finalized.</summary>
    Completed = 4,

    /// <summary>Session cancelled prior to completion.</summary>
    Cancelled = 5
}

