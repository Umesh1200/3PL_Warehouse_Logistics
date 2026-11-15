using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Append-only carrier tracking milestone event.
/// Records historical network scans, hub departures, delivery vehicle dispatches, and exceptions.
/// DB-SPEC-3PL-2026 Phase 4.5 Outbound Shipment Tracking & Delivery Confirmation.
/// </summary>
public class ShipmentTrackingEvent : BaseEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid PackageId { get; set; }
    public Guid? ShipmentId { get; set; }

    public string CarrierCode { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;

    /// <summary>
    /// Carrier-specific unique transaction/event ID, if provided by the carrier network.
    /// </summary>
    public string? CarrierEventId { get; set; }

    /// <summary>
    /// Authoritative normalized 3PL milestone status.
    /// </summary>
    public TrackingMilestoneStatus MilestoneStatus { get; set; }

    /// <summary>
    /// Native carrier status or exception code (e.g. "PU", "IT", "OD", "DL", "X2").
    /// </summary>
    public string CarrierEventCode { get; set; } = string.Empty;

    public string CarrierDescription { get; set; } = string.Empty;

    // Geographic scan location
    public string? EventCity { get; set; }
    public string? EventStateOrProvince { get; set; }
    public string? EventPostalCode { get; set; }
    public string? EventCountry { get; set; }

    /// <summary>
    /// Timestamp when the carrier recorded the physical event occurring in their network.
    /// Used for chronological ordering.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// Timestamp when our system ingested the event (via webhook or polling sync).
    /// Kept separate from OccurredAtUtc to handle out-of-order delivery.
    /// </summary>
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Deduplication hash: SHA256(CarrierCode + TrackingNumber + CarrierEventCode + OccurredAtUtc).
    /// Guaranteed unique per tenant to reject duplicate webhooks.
    /// </summary>
    public string EventFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Full unparsed raw JSON payload from carrier webhook/API for legal auditing.
    /// </summary>
    public string? RawPayloadJson { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Package Package { get; set; } = null!;
    public Shipment? Shipment { get; set; }

    public ShipmentTrackingEvent()
    {
        Id = Guid.NewGuid();
    }
}

