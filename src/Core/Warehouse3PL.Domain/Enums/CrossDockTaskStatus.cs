namespace Warehouse3PL.Domain.Enums;

public enum CrossDockTaskStatus
{
    Pending = 1,
    Assigned = 2,
    InTransit = 3,
    Completed = 4,
    ExceptionHold = 5,
    Cancelled = 6
}

