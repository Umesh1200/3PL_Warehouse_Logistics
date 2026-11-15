namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Physical container classification for movable handling units (LPNs).
/// Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public enum HandlingUnitType
{
    Tote = 1,          // Reusable plastic pick tote / bin
    PickTote = 1,      // Alias for Tote
    CartSlot = 2,      // Dedicated slot/tote on a multi-order pick cart
    Pallet = 3,        // Wooden or plastic pallet for bulk / freight handling
    BinContainer = 4,  // Removable inner bin container
    ShippingCarton = 5 // Outbound customer shipping carton / package
}

