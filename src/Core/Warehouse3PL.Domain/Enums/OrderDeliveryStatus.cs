namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Order-level carrier delivery progress rolled up across all packages.
/// Distinct from OutboundOrderStatus (which tracks warehouse fulfillment).
/// Phase 4.5 Outbound Shipment Tracking & Delivery Confirmation.
/// </summary>
public enum OrderDeliveryStatus
{
    PendingShipment = 1,       // Order undergoing warehouse allocation, picking, or packing
    Dispatched = 2,            // All packages handed over to carrier at dock
    InTransit = 3,             // One or more packages in active transit network
    PartiallyDelivered = 4,    // Subset of packages delivered; remainder outstanding
    Delivered = 5,             // All packages confirmed delivered with POD
    DeliveryException = 6      // One or more packages has active unresolved delivery exception
}

