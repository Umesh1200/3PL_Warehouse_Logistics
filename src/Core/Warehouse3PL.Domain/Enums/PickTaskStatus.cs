namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Execution lifecycle status of an individual RF directed pick task.
/// Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public enum PickTaskStatus
{
    Pending = 1,        // Created and sequenced, unassigned
    Assigned = 2,       // Dispatched to specific RF operator
    InProgress = 3,     // Operator arrived at location and scanned location barcode
    Completed = 4,      // Fully picked and deposited into target tote/container
    ShortPicked = 5,    // Partial pick confirmed due to discrepancy or damage
    Cancelled = 6       // Task aborted
}

