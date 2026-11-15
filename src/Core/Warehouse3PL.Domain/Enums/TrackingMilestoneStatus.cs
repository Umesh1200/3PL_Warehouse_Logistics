namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Authoritative normalized milestone status across carrier tracking networks.
/// Phase 4.5 Outbound Shipment Tracking & Delivery Confirmation.
/// </summary>
public enum TrackingMilestoneStatus
{
    PendingHandover = 1,       // Label printed / shipment ready, awaiting carrier custody
    Accepted = 2,              // Initial carrier scan at origin facility / sort center
    InTransit = 3,             // Linehaul movement between carrier distribution hubs
    OutForDelivery = 4,        // Loaded on local delivery vehicle for final delivery
    Delivered = 5,             // Final delivery confirmed at consignee address
    DeliveryException = 6,     // Transit exception, delay, or carrier hold
    ReturnedToSender = 7       // Delivery aborted, freight returned to warehouse
}

