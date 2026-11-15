namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of an active packing bench session.
/// Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public enum PackingSessionStatus
{
    Active = 1,        // Operator actively packing order
    Completed = 2,     // Order fully packed and session closed
    Suspended = 3,     // Paused for break or shift change
    Aborted = 4        // Cancelled due to discrepancy or operator abort
}

