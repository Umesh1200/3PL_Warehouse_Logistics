namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of an individual inventory allocation reservation.
/// Phase 4.2 Allocation Engine.
/// </summary>
public enum AllocationStatus
{
    Allocated = 1,        // Hard reserved in storage bin
    PartiallyPicked = 2,  // Portion picked during wave execution
    Picked = 3,           // Fully picked to cart/tote
    Released = 4,         // Unlocked / deallocated back to available
    Cancelled = 5         // Order aborted prior to pick
}

