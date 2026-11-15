namespace Warehouse3PL.Domain.Enums;

public enum OutboundOrderStatus
{
    Draft = 1,
    Released = 2,
    Allocated = 3,
    InProgress = 4,
    Completed = 5,
    Cancelled = 6,
    OnHold = 7,
    PartiallyAllocated = 8
}

