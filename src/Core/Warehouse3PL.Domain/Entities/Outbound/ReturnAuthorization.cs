using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Operational entity representing an explicit business and operational authorization for a Return-To-Sender (RTS) workflow.
/// Decouples operational authorization decisions from reverse shipping execution and warehouse receiving.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4: Return-to-Sender Authorization, Return Shipment &amp; Tracking.
/// </summary>
public class ReturnAuthorization : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid OriginFacilityId { get; set; }
    public Guid DestinationFacilityId { get; set; }

    public string AuthorizationNumber { get; set; } = string.Empty;
    public string? RmaNumber { get; set; }

    public Guid OrderId { get; set; }
    public Guid? OriginalShipmentId { get; set; }
    public Guid PackageId { get; set; }
    public Guid? DeliveryExceptionId { get; set; }
    public Guid? ReturnShipmentId { get; set; }

    public ReturnReason Reason { get; set; } = ReturnReason.Other;
    public ReturnAuthorizationStatus Status { get; set; } = ReturnAuthorizationStatus.Authorized;

    public Guid AuthorizedByUserId { get; set; }
    public DateTime AuthorizedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? InTransitAtUtc { get; set; }
    public DateTime? DeliveredAtFacilityAtUtc { get; set; }
    public Guid? InboundHandoffId { get; set; }

    public string AuthorizationNotes { get; set; } = string.Empty;

    public string? CancellationReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility OriginFacility { get; set; } = null!;
    public Facility DestinationFacility { get; set; } = null!;
    public OutboundOrder Order { get; set; } = null!;
    public Shipment? OriginalShipment { get; set; }
    public Package Package { get; set; } = null!;
    public ShipmentDeliveryException? DeliveryException { get; set; }
    public ReturnShipment? ReturnShipment { get; set; }
    public ReturnInboundHandoff? InboundHandoff { get; set; }
    public User AuthorizedByUser { get; set; } = null!;
    public User? CancelledByUser { get; set; }

    public ReturnAuthorization()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Factory method to construct and initialize a valid ReturnAuthorization.
    /// </summary>
    public static ReturnAuthorization Create(
        Guid clientId,
        Guid originFacilityId,
        Guid destinationFacilityId,
        Guid orderId,
        Guid? originalShipmentId,
        Guid packageId,
        Guid? deliveryExceptionId,
        string authorizationNumber,
        ReturnReason reason,
        string notes,
        Guid authorizedByUserId,
        string? rmaNumber = null,
        DateTime? expiresAtUtc = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required.");
        }

        if (originFacilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(originFacilityId), "Origin facility ID is required.");
        }

        if (destinationFacilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(destinationFacilityId), "Destination facility ID is required.");
        }

        if (orderId == Guid.Empty)
        {
            throw new ValidationException(nameof(orderId), "Outbound order ID is required.");
        }

        if (packageId == Guid.Empty)
        {
            throw new ValidationException(nameof(packageId), "Package ID is required.");
        }

        if (string.IsNullOrWhiteSpace(authorizationNumber))
        {
            throw new ValidationException(nameof(authorizationNumber), "Authorization number is required.");
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new ValidationException(nameof(notes), "Authorization notes are required.");
        }

        if (authorizedByUserId == Guid.Empty)
        {
            throw new ValidationException(nameof(authorizedByUserId), "Authorizing user ID is required.");
        }

        if (expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTime.UtcNow)
        {
            throw new ValidationException(nameof(expiresAtUtc), "Authorization expiry timestamp cannot be in the past.");
        }

        return new ReturnAuthorization
        {
            ClientId = clientId,
            OriginFacilityId = originFacilityId,
            DestinationFacilityId = destinationFacilityId,
            OrderId = orderId,
            OriginalShipmentId = originalShipmentId,
            PackageId = packageId,
            DeliveryExceptionId = deliveryExceptionId,
            AuthorizationNumber = authorizationNumber.Trim().ToUpperInvariant(),
            RmaNumber = string.IsNullOrWhiteSpace(rmaNumber) ? null : rmaNumber.Trim().ToUpperInvariant(),
            Reason = reason,
            Status = ReturnAuthorizationStatus.Authorized,
            AuthorizedByUserId = authorizedByUserId,
            AuthorizedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            AuthorizationNotes = notes.Trim()
        };
    }

    /// <summary>
    /// Validates domain relationships between authorization, package, shipment, order, and facilities.
    /// </summary>
    public void ValidateRelationships(Package package, Shipment? shipment, OutboundOrder? order, Facility? destinationFacility)
    {
        if (package == null)
        {
            throw new ValidationException(nameof(PackageId), "Associated package cannot be null.");
        }

        if (package.ClientId != ClientId)
        {
            throw new ValidationException(nameof(ClientId), "Package tenant does not match authorization tenant.");
        }

        if (package.Id != PackageId)
        {
            throw new ValidationException(nameof(PackageId), "Supplied package does not match authorization package ID.");
        }

        if (order != null)
        {
            if (package.OutboundOrderId != order.Id || order.Id != OrderId)
            {
                throw new ValidationException(nameof(OrderId), $"Package '{package.PackageNumber}' does not belong to order '{order.OrderNumber}'.");
            }

            if (order.ClientId != ClientId)
            {
                throw new ValidationException(nameof(ClientId), "Order tenant does not match authorization tenant.");
            }
        }

        if (shipment != null)
        {
            if (package.ShipmentId.HasValue && package.ShipmentId.Value != shipment.Id)
            {
                throw new ValidationException(nameof(OriginalShipmentId), $"Package '{package.PackageNumber}' does not belong to shipment '{shipment.ShipmentNumber}'.");
            }

            if (shipment.ClientId != ClientId)
            {
                throw new ValidationException(nameof(ClientId), "Shipment tenant does not match authorization tenant.");
            }
        }

        if (destinationFacility != null && destinationFacility.Id != DestinationFacilityId)
        {
            throw new ValidationException(nameof(DestinationFacilityId), "Destination facility mismatch.");
        }
    }

    /// <summary>
    /// Links a created reverse shipment aggregate to this return authorization.
    /// </summary>
    public void LinkReturnShipment(Guid returnShipmentId)
    {
        if (Status == ReturnAuthorizationStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot link a reverse shipment to a cancelled return authorization.");
        }

        if (Status == ReturnAuthorizationStatus.Expired)
        {
            throw new InvalidDomainStateException("Cannot link a reverse shipment to an expired return authorization.");
        }

        if (returnShipmentId == Guid.Empty)
        {
            throw new ValidationException(nameof(returnShipmentId), "A valid return shipment ID is required.");
        }

        ReturnShipmentId = returnShipmentId;
        Status = ReturnAuthorizationStatus.ShipmentCreated;
    }

    /// <summary>
    /// Transitions status to InTransit when reverse carrier accepts the package into custody.
    /// InTransit timestamp is recorded monotonically.
    /// </summary>
    public void RecordInTransit(DateTime? occurredAtUtc = null)
    {
        if (Status == ReturnAuthorizationStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot transition a cancelled return authorization to InTransit.");
        }

        if (Status == ReturnAuthorizationStatus.Expired)
        {
            throw new InvalidDomainStateException("Cannot transition an expired return authorization to InTransit.");
        }

        if (Status == ReturnAuthorizationStatus.Authorized)
        {
            throw new InvalidDomainStateException("Cannot transition authorization to InTransit when no return shipment has been created.");
        }

        if (!InTransitAtUtc.HasValue && occurredAtUtc.HasValue)
        {
            InTransitAtUtc = occurredAtUtc.Value;
        }

        // Monotonic transition: only advance from ShipmentCreated to InTransit
        if (Status == ReturnAuthorizationStatus.ShipmentCreated)
        {
            Status = ReturnAuthorizationStatus.InTransit;
        }
    }

    /// <summary>
    /// Records physical carrier delivery to the destination warehouse receiving dock.
    /// Delivery timestamp is recorded monotonically.
    /// </summary>
    public void RecordDeliveredAtFacility(DateTime? deliveredAtUtc = null)
    {
        if (Status == ReturnAuthorizationStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot record delivery for a cancelled return authorization.");
        }

        if (Status == ReturnAuthorizationStatus.Authorized)
        {
            throw new InvalidDomainStateException("Cannot mark authorization as delivered when no return shipment has been created or in custody.");
        }

        if (!InTransitAtUtc.HasValue && deliveredAtUtc.HasValue)
        {
            InTransitAtUtc = deliveredAtUtc.Value;
        }

        if (!DeliveredAtFacilityAtUtc.HasValue && deliveredAtUtc.HasValue)
        {
            DeliveredAtFacilityAtUtc = deliveredAtUtc.Value;
        }

        if (Status != ReturnAuthorizationStatus.Completed)
        {
            Status = ReturnAuthorizationStatus.DeliveredAtFacility;
        }
    }

    /// <summary>
    /// Completes the authorization workflow after warehouse receiving handoff has been established.
    /// </summary>
    public void Complete(string? notes = null)
    {
        if (Status == ReturnAuthorizationStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot complete a cancelled return authorization.");
        }

        Status = ReturnAuthorizationStatus.Completed;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            AuthorizationNotes = $"{AuthorizationNotes}\n[Completed]: {notes.Trim()}";
        }
    }

    /// <summary>
    /// Cancels the return authorization prior to reverse transit execution.
    /// </summary>
    public void Cancel(string reason, Guid cancelledByUserId)
    {
        if (Status == ReturnAuthorizationStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Return authorization is already cancelled.");
        }

        if (Status == ReturnAuthorizationStatus.InTransit ||
            Status == ReturnAuthorizationStatus.DeliveredAtFacility ||
            Status == ReturnAuthorizationStatus.Completed)
        {
            throw new InvalidDomainStateException("Cannot cancel a return authorization once the package is in reverse transit, delivered, or completed.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Cancellation reason is required.");
        }

        if (cancelledByUserId == Guid.Empty)
        {
            throw new ValidationException(nameof(cancelledByUserId), "Cancelled by user ID is required.");
        }

        Status = ReturnAuthorizationStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledAtUtc = DateTime.UtcNow;
        CancelledByUserId = cancelledByUserId;
    }

    /// <summary>
    /// Marks the return authorization as expired if authorization window elapsed before carrier execution.
    /// </summary>
    public void Expire()
    {
        if (Status == ReturnAuthorizationStatus.Cancelled ||
            Status == ReturnAuthorizationStatus.InTransit ||
            Status == ReturnAuthorizationStatus.DeliveredAtFacility ||
            Status == ReturnAuthorizationStatus.Completed)
        {
            throw new InvalidDomainStateException($"Cannot expire a return authorization that is currently {Status}.");
        }

        Status = ReturnAuthorizationStatus.Expired;
    }
}

