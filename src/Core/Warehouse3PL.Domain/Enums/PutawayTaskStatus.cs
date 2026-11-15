namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Execution state of a directed putaway task.
/// </summary>
public enum PutawayTaskStatus
{
    Pending = 1,
    Assigned = 2,
    InTransit = 3,
    Completed = 4,
    ExceptionHold = 5,
    ReRouted = 6,
    Cancelled = 7
}

