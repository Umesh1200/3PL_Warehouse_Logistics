namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Strategy algorithm applied for selecting and reserving inventory.
/// Phase 4.2 Allocation Engine.
/// </summary>
public enum AllocationStrategyType
{
    FIFO = 1,   // First In, First Out (Inventory Receipt Timestamp)
    FEFO = 2,   // First Expired, First Out (Lot Expiration Date)
    Manual = 3  // Specific Lot / LPN operator designated
}

