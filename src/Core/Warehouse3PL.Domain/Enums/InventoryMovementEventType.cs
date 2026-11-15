namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational event types for warehouse stock movement and demand.
/// Invariant: Internal relocations, putaways, and replenishments are operational handling tasks,
/// while outbound picks and shipment dispatches constitute qualifying customer demand.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public enum InventoryMovementEventType
{
    InboundReceipt = 1,
    Putaway = 2,
    InternalRelocation = 3,
    Replenishment = 4,
    OutboundAllocation = 5,
    OutboundPick = 6,
    ShipmentDispatch = 7,
    ReturnReceipt = 8,
    InventoryAdjustment = 9
}

public static class InventoryMovementEventTypeExtensions
{
    /// <summary>
    /// Indicates whether this event represents actual qualifying customer demand consumption.
    /// </summary>
    public static bool IsQualifyingDemand(this InventoryMovementEventType eventType)
    {
        return eventType is InventoryMovementEventType.OutboundPick or InventoryMovementEventType.ShipmentDispatch;
    }

    /// <summary>
    /// Indicates whether this event represents physical warehouse handling / movement.
    /// </summary>
    public static bool IsPhysicalMovement(this InventoryMovementEventType eventType)
    {
        return eventType is InventoryMovementEventType.InboundReceipt
            or InventoryMovementEventType.Putaway
            or InventoryMovementEventType.InternalRelocation
            or InventoryMovementEventType.Replenishment
            or InventoryMovementEventType.OutboundPick
            or InventoryMovementEventType.ReturnReceipt;
    }

    /// <summary>
    /// Indicates whether this event is a replenishment movement to pick faces.
    /// </summary>
    public static bool IsReplenishment(this InventoryMovementEventType eventType)
    {
        return eventType == InventoryMovementEventType.Replenishment;
    }

    /// <summary>
    /// Indicates whether this event is an internal bin-to-bin relocation.
    /// </summary>
    public static bool IsRelocation(this InventoryMovementEventType eventType)
    {
        return eventType == InventoryMovementEventType.InternalRelocation;
    }
}

