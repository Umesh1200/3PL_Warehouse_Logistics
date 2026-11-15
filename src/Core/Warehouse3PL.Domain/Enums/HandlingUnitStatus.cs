namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational status of a movable handling unit (tote / cart / pallet).
/// Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public enum HandlingUnitStatus
{
    Available = 1,          // Empty, clean, and available for wave assignment
    InUse = 2,              // Currently linked to active pick wave / orders
    InTransit = 3,          // Moving through warehouse on pick cart
    AtPackingStation = 4,   // Staged at packing station awaiting pack/ship
    Damaged = 5             // Physical wear or damage; decommissioned
}

