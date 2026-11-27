using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Operational entity representing an authorized Return-To-Sender (RTS) shipment.
/// Governs reverse logistics lifecycle independently from outbound shipping and warehouse inventory.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public class ReturnShipment : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid OriginFacilityId { get; set; }
    public Guid DestinationFacilityId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? OriginalShipmentId { get; set; }
    public Guid PackageId { get; set; }
    public Guid? DeliveryExceptionId { get; set; }
    public Guid? ReturnAuthorizationId { get; set; }

    public string ReturnRmaNumber { get; set; } = string.Empty;

    public ReturnReason Reason { get; set; }
    public Guid AuthorizedByUserId { get; set; }
    public DateTime AuthorizedAtUtc { get; set; } = DateTime.UtcNow;
    public string AuthorizationNotes { get; set; } = string.Empty;

    public string CarrierCode { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string? CarrierServiceLevel { get; set; }
    public DateTime? LabelGeneratedAtUtc { get; set; }
    public string? CarrierShipmentId { get; set; }
    public string LabelFormat { get; set; } = "ZPL";
    public string? LabelData { get; set; }
    public string? LabelObjectKey { get; set; }
    public decimal? RateCharged { get; set; }
    public string Currency { get; set; } = "USD";
    public bool? CarrierCancellationConfirmed { get; set; }
    public string? CarrierCancellationNotes { get; set; }

    // Origin Address (consignee/depot location where goods are returned from)
    public string OriginAddressLine1 { get; set; } = string.Empty;
    public string? OriginAddressLine2 { get; set; }
    public string OriginCity { get; set; } = string.Empty;
    public string OriginState { get; set; } = string.Empty;
    public string OriginPostalCode { get; set; } = string.Empty;
    public string OriginCountry { get; set; } = string.Empty;

    // Destination Address (receiving warehouse facility)
    public string DestinationAddressLine1 { get; set; } = string.Empty;
    public string? DestinationAddressLine2 { get; set; }
    public string DestinationCity { get; set; } = string.Empty;
    public string DestinationState { get; set; } = string.Empty;
    public string DestinationPostalCode { get; set; } = string.Empty;
    public string DestinationCountry { get; set; } = string.Empty;
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.PendingNormalization;

    // Return Lifecycle Status
    public ReturnShipmentStatus Status { get; set; } = ReturnShipmentStatus.Authorized;

    // Custody and Delivery Milestones
    public DateTime? CarrierHandoverAtUtc { get; set; }
    public DateTime? ExpectedWarehouseArrivalUtc { get; set; }
    public DateTime? DeliveredAtWarehouseUtc { get; set; }
    public DateTime? ReceivedAndInspectedAtUtc { get; set; }
    public Guid? InspectedByUserId { get; set; }

    public Guid? InboundHandoffId { get; set; }

    // Final Disposition (assigned only after warehouse receipt & inspection)
    public ReturnDisposition? FinalDisposition { get; set; }
    public string? DispositionNotes { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility OriginFacility { get; set; } = null!;
    public Facility DestinationFacility { get; set; } = null!;
    public OutboundOrder Order { get; set; } = null!;
    public Shipment? OriginalShipment { get; set; }
    public Package Package { get; set; } = null!;
    public ShipmentDeliveryException? DeliveryException { get; set; }
    public ReturnAuthorization? ReturnAuthorization { get; set; }
    public ReturnInboundHandoff? InboundHandoff { get; set; }
    public User AuthorizedByUser { get; set; } = null!;
    public User? InspectedByUser { get; set; }
    public ICollection<ReturnTrackingEvent> TrackingEvents { get; set; } = [];

    public ReturnShipment()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Records initial carrier handover when reverse transit begins.
    /// Handover timestamp is immutable once established by the first verified carrier custody event.
    /// </summary>
    public void RecordCarrierHandover(DateTime occurredAtUtc)
    {
        if (Status == ReturnShipmentStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot record handover for a cancelled return shipment.");
        }

        // Establish carrier custody timestamp exactly once
        if (!CarrierHandoverAtUtc.HasValue)
        {
            CarrierHandoverAtUtc = occurredAtUtc;
        }

        // Monotonic transition: only advance from Authorized to InTransitToWarehouse
        if (Status == ReturnShipmentStatus.Authorized)
        {
            Status = ReturnShipmentStatus.InTransitToWarehouse;
        }
    }

    /// <summary>
    /// Records in-transit reverse milestones monotonically.
    /// Delayed out-of-order events will not regress subsequent states.
    /// </summary>
    public void RecordInTransit(DateTime occurredAtUtc)
    {
        if (Status == ReturnShipmentStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot update status of a cancelled return shipment.");
        }

        if (!CarrierHandoverAtUtc.HasValue)
        {
            CarrierHandoverAtUtc = occurredAtUtc;
        }

        // Advance to InTransitToWarehouse only if currently in Authorized
        if (Status == ReturnShipmentStatus.Authorized)
        {
            Status = ReturnShipmentStatus.InTransitToWarehouse;
        }
    }

    /// <summary>
    /// Records out for delivery to warehouse monotonically.
    /// </summary>
    public void RecordOutForDelivery(DateTime occurredAtUtc)
    {
        if (Status == ReturnShipmentStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot update status of a cancelled return shipment.");
        }

        if (!CarrierHandoverAtUtc.HasValue)
        {
            CarrierHandoverAtUtc = occurredAtUtc;
        }

        // Advance to OutForDeliveryToWarehouse if prior to delivery
        if (Status == ReturnShipmentStatus.Authorized || Status == ReturnShipmentStatus.InTransitToWarehouse)
        {
            Status = ReturnShipmentStatus.OutForDeliveryToWarehouse;
        }
    }

    /// <summary>
    /// Records physical carrier delivery to the warehouse receiving dock.
    /// Delivery timestamp is immutable once established by the first verified carrier delivery event.
    /// Note: Does not mutate warehouse inventory. Physical receiving and inspection happen downstream.
    /// </summary>
    public void RecordDeliveredAtWarehouse(DateTime deliveredAtUtc)
    {
        if (Status == ReturnShipmentStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot mark a cancelled return shipment as delivered.");
        }

        if (!CarrierHandoverAtUtc.HasValue)
        {
            CarrierHandoverAtUtc = deliveredAtUtc;
        }

        if (!DeliveredAtWarehouseUtc.HasValue)
        {
            DeliveredAtWarehouseUtc = deliveredAtUtc;
        }

        if (Status != ReturnShipmentStatus.ReceivedAndInspected)
        {
            Status = ReturnShipmentStatus.DeliveredAtWarehouse;
        }
    }

    /// <summary>
    /// Records physical warehouse dock receipt, inspection completion, and final disposition.
    /// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4.5: Return Facility Receiving &amp; Inspection.
    /// </summary>
    public void RecordReceivedAndInspected(Guid inspectedByUserId, ReturnDisposition disposition, DateTime inspectedAtUtc, string? notes = null)
    {
        if (Status == ReturnShipmentStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot inspect a cancelled return shipment.");
        }

        InspectedByUserId = inspectedByUserId;
        FinalDisposition = disposition;
        ReceivedAndInspectedAtUtc = inspectedAtUtc;
        Status = ReturnShipmentStatus.ReceivedAndInspected;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            DispositionNotes = notes.Trim();
        }
    }

    /// <summary>
    /// Cancels return authorization before carrier handover.
    /// </summary>
    public void Cancel(string reason, Guid cancelledByUserId)
    {
        if (Status == ReturnShipmentStatus.Cancelled)
        {
            return;
        }

        if (CarrierHandoverAtUtc.HasValue || Status == ReturnShipmentStatus.InTransitToWarehouse || Status == ReturnShipmentStatus.DeliveredAtWarehouse)
        {
            throw new InvalidDomainStateException("Cannot cancel a return shipment that is already in carrier transit or delivered.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Cancellation reason is required.");
        }

        Status = ReturnShipmentStatus.Cancelled;
        DispositionNotes = $"Cancelled: {reason.Trim()}";
    }

    /// <summary>
    /// Marks the return parcel as lost in transit by the carrier.
    /// </summary>
    public void MarkLost(string reason)
    {
        if (Status == ReturnShipmentStatus.DeliveredAtWarehouse || Status == ReturnShipmentStatus.ReceivedAndInspected)
        {
            throw new InvalidDomainStateException("Cannot mark an already received return parcel as lost.");
        }

        Status = ReturnShipmentStatus.LostInReturn;
        DispositionNotes = $"Lost in reverse transit: {reason.Trim()}";
    }
}

