namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Execution state of a warehouse goods receiving session.
/// </summary>
public enum ReceivingStatus
{
    Started = 1,
    InProgress = 2,
    Completed = 3,
    Flagged = 4,
    Cancelled = 5
}

