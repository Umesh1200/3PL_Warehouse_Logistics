using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Domain aggregate representing an inbound dock receiving handoff for an authorized Return-To-Sender (RTS) package.
/// Bridges carrier reverse logistics delivery to warehouse inbound dock operations.
/// Explicit architectural boundary: Carrier delivery establishes arrival and creates this handoff,
/// but does NOT post to warehouse stock or increase inventory (Delta Q = 0).
/// Physical receiving, disposition, and stock ledger posting belong to Phase 4.6.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4: Return-to-Sender Authorization, Return Shipment &amp; Tracking.
/// </summary>
public class ReturnInboundHandoff : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid DestinationFacilityId { get; set; }
    public Guid ReturnShipmentId { get; set; }
    public Guid ReturnAuthorizationId { get; set; }
    public Guid OriginalPackageId { get; set; }
    public Guid OriginalOrderId { get; set; }

    public string HandoffReference { get; set; } = string.Empty;
    public string ReturnRmaNumber { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;

    public ReturnInboundHandoffStatus Status { get; set; } = ReturnInboundHandoffStatus.PendingReceiving;
    public DateTime CarrierDeliveredAtUtc { get; set; }
    public DateTime HandoffCreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string? DockDoorLocation { get; set; }
    public string? Notes { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility DestinationFacility { get; set; } = null!;
    public ReturnShipment ReturnShipment { get; set; } = null!;
    public ReturnAuthorization ReturnAuthorization { get; set; } = null!;
    public Package OriginalPackage { get; set; } = null!;
    public OutboundOrder OriginalOrder { get; set; } = null!;

    public ReturnInboundHandoff()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Factory method to construct and initialize a valid ReturnInboundHandoff.
    /// </summary>
    public static ReturnInboundHandoff Create(
        Guid clientId,
        Guid destinationFacilityId,
        Guid returnShipmentId,
        Guid returnAuthorizationId,
        Guid originalPackageId,
        Guid originalOrderId,
        string handoffReference,
        string returnRmaNumber,
        string trackingNumber,
        string carrierCode,
        DateTime carrierDeliveredAtUtc,
        string? dockDoorLocation = null,
        string? notes = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required.");
        }

        if (destinationFacilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(destinationFacilityId), "Destination facility ID is required.");
        }

        if (returnShipmentId == Guid.Empty)
        {
            throw new ValidationException(nameof(returnShipmentId), "Return shipment ID is required.");
        }

        if (returnAuthorizationId == Guid.Empty)
        {
            throw new ValidationException(nameof(returnAuthorizationId), "Return authorization ID is required.");
        }

        if (originalPackageId == Guid.Empty)
        {
            throw new ValidationException(nameof(originalPackageId), "Original package ID is required.");
        }

        if (originalOrderId == Guid.Empty)
        {
            throw new ValidationException(nameof(originalOrderId), "Original order ID is required.");
        }

        if (string.IsNullOrWhiteSpace(handoffReference))
        {
            throw new ValidationException(nameof(handoffReference), "Handoff reference is required.");
        }

        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            throw new ValidationException(nameof(trackingNumber), "Tracking number is required.");
        }

        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            throw new ValidationException(nameof(carrierCode), "Carrier code is required.");
        }

        return new ReturnInboundHandoff
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            DestinationFacilityId = destinationFacilityId,
            ReturnShipmentId = returnShipmentId,
            ReturnAuthorizationId = returnAuthorizationId,
            OriginalPackageId = originalPackageId,
            OriginalOrderId = originalOrderId,
            HandoffReference = handoffReference.Trim().ToUpperInvariant(),
            ReturnRmaNumber = string.IsNullOrWhiteSpace(returnRmaNumber) ? $"RMA-{trackingNumber.Trim()}" : returnRmaNumber.Trim(),
            TrackingNumber = trackingNumber.Trim(),
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            CarrierDeliveredAtUtc = carrierDeliveredAtUtc,
            HandoffCreatedAtUtc = DateTime.UtcNow,
            Status = ReturnInboundHandoffStatus.PendingReceiving,
            DockDoorLocation = dockDoorLocation?.Trim(),
            Notes = notes?.Trim()
        };
    }

    /// <summary>
    /// Updates status when dock staff initiates physical scanning and verification.
    /// </summary>
    public void MarkProcessing(string? dockLocation = null)
    {
        if (Status == ReturnInboundHandoffStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot process a cancelled inbound receiving handoff.");
        }

        Status = ReturnInboundHandoffStatus.Processing;
        if (!string.IsNullOrWhiteSpace(dockLocation))
        {
            DockDoorLocation = dockLocation.Trim();
        }
    }

    /// <summary>
    /// Marks the handoff as received upon physical verification by warehouse receiving.
    /// </summary>
    public void MarkReceived(string? notes = null)
    {
        if (Status == ReturnInboundHandoffStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot mark a cancelled inbound receiving handoff as received.");
        }

        Status = ReturnInboundHandoffStatus.Received;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = string.IsNullOrWhiteSpace(Notes) ? notes.Trim() : $"{Notes}\n{notes.Trim()}";
        }
    }

    /// <summary>
    /// Cancels the inbound handoff if the delivery was voided or recorded in error.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == ReturnInboundHandoffStatus.Received)
        {
            throw new InvalidDomainStateException("Cannot cancel an inbound handoff that has already been physically received into warehouse custody.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Cancellation reason is required.");
        }

        Status = ReturnInboundHandoffStatus.Cancelled;
        Notes = string.IsNullOrWhiteSpace(Notes) ? $"Cancelled: {reason.Trim()}" : $"{Notes}\nCancelled: {reason.Trim()}";
    }
}

