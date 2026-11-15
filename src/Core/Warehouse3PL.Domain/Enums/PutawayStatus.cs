namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of a putaway task (maintained for compatibility with PutawayTaskStatus).
/// </summary>
public enum PutawayStatus
{
    Pending = 1,
    Assigned = 2,
    InTransit = 3,
    InProgress = 3,
    Completed = 4,
    ExceptionHold = 5,
    ReRouted = 6,
    Cancelled = 7
}
