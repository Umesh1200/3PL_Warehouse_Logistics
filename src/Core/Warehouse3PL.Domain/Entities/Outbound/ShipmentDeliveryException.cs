using System.Security.Cryptography;
using System.Text;
using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Operational carrier transit delivery exception record.
/// Tracks delivery anomalies, failed attempts, and operational resolution workflows.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public class ShipmentDeliveryException : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid PackageId { get; set; }
    public Guid? ShipmentId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? ReturnShipmentId { get; set; }
    public Guid? ReturnAuthorizationId { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;

    public DeliveryExceptionType ExceptionType { get; set; }
    public DeliveryExceptionStatus Status { get; set; } = DeliveryExceptionStatus.Open;
    public DeliveryExceptionSeverity Severity { get; set; } = DeliveryExceptionSeverity.Medium;

    public string CarrierExceptionCode { get; set; } = string.Empty;
    public string CarrierDescription { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public int AttemptNumber { get; set; } = 1;
    public int ReattemptCount { get; set; }
    public DateTime? ScheduledReattemptDateUtc { get; set; }
    public int ContactAttemptsCount { get; set; }
    public string? CorrectedAddress { get; set; }
    public string? HoldDepotLocation { get; set; }

    public DeliveryExceptionResolution Resolution { get; set; } = DeliveryExceptionResolution.PendingCarrierReattempt;
    public string? ResolutionNotes { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }

    // Ingestion Metadata & Deduplication (Sub-Step 3B.2)
    public DeliveryExceptionSource Source { get; set; } = DeliveryExceptionSource.CarrierWebhook;
    public Guid? ReportedByUserId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? CarrierEventId { get; set; }
    public string? RawPayload { get; set; }
    public string? EventFingerprint { get; set; }

    public bool IsResolved => ResolvedAtUtc.HasValue || Status == DeliveryExceptionStatus.Resolved || Status == DeliveryExceptionStatus.ReturnToSenderInitiated || Status == DeliveryExceptionStatus.Voided;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Package Package { get; set; } = null!;
    public Shipment? Shipment { get; set; }
    public OutboundOrder? Order { get; set; }
    public ReturnShipment? ReturnShipment { get; set; }
    public ReturnAuthorization? ReturnAuthorization { get; set; }
    public User? ResolvedByUser { get; set; }
    public User? ReportedByUser { get; set; }
    public ICollection<DeliveryExceptionContactLog> ContactLogs { get; set; } = [];

    public ShipmentDeliveryException()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Validates relationships between package, shipment, and order.
    /// </summary>
    public void ValidateRelationships(Package package, Shipment? shipment, OutboundOrder? order)
    {
        if (package == null)
        {
            throw new ValidationException(nameof(PackageId), "Associated package cannot be null.");
        }

        if (package.ClientId != ClientId)
        {
            throw new ValidationException(nameof(ClientId), "Package tenant does not match exception tenant.");
        }

        if (package.FacilityId != FacilityId)
        {
            throw new ValidationException(nameof(FacilityId), "Package facility does not match exception facility.");
        }

        if (order != null)
        {
            if (package.OutboundOrderId != order.Id)
            {
                throw new ValidationException(nameof(OrderId), $"Package '{package.PackageNumber}' does not belong to order '{order.OrderNumber}'.");
            }

            if (order.ClientId != ClientId)
            {
                throw new ValidationException(nameof(ClientId), $"Order '{order.OrderNumber}' belongs to a different client tenant.");
            }
        }

        if (shipment != null)
        {
            if (package.ShipmentId != shipment.Id)
            {
                throw new ValidationException(nameof(ShipmentId), $"Package '{package.PackageNumber}' does not belong to shipment '{shipment.ShipmentNumber}'.");
            }

            if (shipment.ClientId != ClientId)
            {
                throw new ValidationException(nameof(ClientId), $"Shipment '{shipment.ShipmentNumber}' belongs to a different client tenant.");
            }
        }
    }

    /// <summary>
    /// Schedules a confirmed re-attempt with the carrier.
    /// </summary>
    public void ScheduleReattempt(DateTime scheduledDateUtc, string notes, Guid userId)
    {
        if (Status == DeliveryExceptionStatus.Resolved)
        {
            throw new InvalidDomainStateException("A resolved exception cannot be scheduled for another reattempt without an explicit reopening operation.");
        }

        if (Status == DeliveryExceptionStatus.Voided)
        {
            throw new InvalidDomainStateException("A voided exception cannot be scheduled for reattempt.");
        }

        if (Status == DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            throw new InvalidDomainStateException("Cannot schedule reattempt on an exception with an active return-to-sender workflow.");
        }

        if (Status == DeliveryExceptionStatus.ScheduledForReattempt && ScheduledReattemptDateUtc.HasValue)
        {
            throw new InvalidDomainStateException("A delivery reattempt is already actively scheduled for this exception. Reschedule or cancel the existing reattempt first.");
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new ValidationException(nameof(notes), "Notes are required when scheduling a delivery re-attempt.");
        }

        if (scheduledDateUtc < DateTime.UtcNow.AddMinutes(-15))
        {
            throw new ValidationException(nameof(scheduledDateUtc), "Scheduled reattempt date cannot be in the past.");
        }

        if (scheduledDateUtc > DateTime.UtcNow.AddDays(30))
        {
            throw new ValidationException(nameof(scheduledDateUtc), "Scheduled reattempt date cannot be more than 30 days in the future.");
        }

        ReattemptCount++;
        ScheduledReattemptDateUtc = scheduledDateUtc;
        ResolutionNotes = notes.Trim();
        Status = DeliveryExceptionStatus.ScheduledForReattempt;
        Resolution = DeliveryExceptionResolution.RedeliveryScheduled;
    }

    /// <summary>
    /// Cancels an actively scheduled delivery reattempt.
    /// </summary>
    public void CancelReattempt(string reason, Guid userId)
    {
        if (Status != DeliveryExceptionStatus.ScheduledForReattempt || !ScheduledReattemptDateUtc.HasValue)
        {
            throw new InvalidDomainStateException("Cannot cancel reattempt when no reattempt is actively scheduled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Cancellation reason is required.");
        }

        ScheduledReattemptDateUtc = null;
        Status = DeliveryExceptionStatus.UnderReview;
        Resolution = DeliveryExceptionResolution.PendingCarrierReattempt;
        ResolutionNotes = $"Reattempt cancelled: {reason.Trim()} (Prior notes: {ResolutionNotes})";
    }

    /// <summary>
    /// Reschedules an existing delivery reattempt with an updated date.
    /// </summary>
    public void RescheduleReattempt(DateTime newDateUtc, string reason, Guid userId)
    {
        if (Status != DeliveryExceptionStatus.ScheduledForReattempt || !ScheduledReattemptDateUtc.HasValue)
        {
            throw new InvalidDomainStateException("Cannot reschedule when no reattempt is actively scheduled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Rescheduling reason is required.");
        }

        if (newDateUtc < DateTime.UtcNow.AddMinutes(-15))
        {
            throw new ValidationException(nameof(newDateUtc), "New reattempt date cannot be in the past.");
        }

        if (newDateUtc > DateTime.UtcNow.AddDays(30))
        {
            throw new ValidationException(nameof(newDateUtc), "New reattempt date cannot be more than 30 days in the future.");
        }

        var priorDate = ScheduledReattemptDateUtc.Value;
        ScheduledReattemptDateUtc = newDateUtc;
        ReattemptCount++;
        ResolutionNotes = $"Rescheduled from {priorDate:yyyy-MM-dd} to {newDateUtc:yyyy-MM-dd}: {reason.Trim()} (Prior notes: {ResolutionNotes})";
    }

    /// <summary>
    /// Records an operational attempt to contact the customer or consignee for delivery instructions.
    /// </summary>
    public void RecordCustomerContact(string notes, Guid userId)
    {
        if (Status == DeliveryExceptionStatus.Resolved || Status == DeliveryExceptionStatus.Voided || Status == DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            throw new InvalidDomainStateException("Cannot record customer contact on a closed or voided exception.");
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new ValidationException(nameof(notes), "Notes are required when recording a customer contact attempt.");
        }

        ContactAttemptsCount++;
        ResolutionNotes = notes.Trim();
        if (Status == DeliveryExceptionStatus.Open)
        {
            Status = DeliveryExceptionStatus.PendingCustomerAction;
        }
        Resolution = DeliveryExceptionResolution.CustomerContacted;
    }

    /// <summary>
    /// Updates and corrects consignee delivery address for retransmission to carrier.
    /// </summary>
    public void UpdateAddress(string correctedAddress, string notes, Guid userId)
    {
        if (Status == DeliveryExceptionStatus.Resolved || Status == DeliveryExceptionStatus.Voided || Status == DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            throw new InvalidDomainStateException("Cannot update address on a closed or voided exception.");
        }

        if (string.IsNullOrWhiteSpace(correctedAddress))
        {
            throw new ValidationException(nameof(correctedAddress), "Corrected address cannot be empty.");
        }

        CorrectedAddress = correctedAddress.Trim();
        ResolutionNotes = notes?.Trim();
        Status = DeliveryExceptionStatus.UnderReview;
        Resolution = DeliveryExceptionResolution.AddressCorrected;
    }

    /// <summary>
    /// Redirects package to carrier depot or access point for customer pickup.
    /// </summary>
    public void RedirectToDepot(string depotLocation, string notes, Guid userId)
    {
        if (Status == DeliveryExceptionStatus.Resolved || Status == DeliveryExceptionStatus.Voided || Status == DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            throw new InvalidDomainStateException("Cannot redirect package on a closed or voided exception.");
        }

        if (string.IsNullOrWhiteSpace(depotLocation))
        {
            throw new ValidationException(nameof(depotLocation), "Depot location cannot be empty.");
        }

        HoldDepotLocation = depotLocation.Trim();
        ResolutionNotes = notes?.Trim();
        Status = DeliveryExceptionStatus.UnderReview;
        Resolution = DeliveryExceptionResolution.HoldAtCarrierDepot;
    }

    /// <summary>
    /// Authorizes a Return-To-Sender workflow, linking the created ReturnShipment.
    /// </summary>
    public void AuthorizeReturnToSender(Guid returnShipmentId, string notes, Guid userId)
    {
        AuthorizeReturnToSender(Guid.NewGuid(), returnShipmentId, notes, userId);
    }

    /// <summary>
    /// Authorizes a Return-To-Sender workflow, linking the ReturnAuthorization record and optional initial ReturnShipment.
    /// </summary>
    public void AuthorizeReturnToSender(Guid returnAuthorizationId, Guid? returnShipmentId, string notes, Guid userId)
    {
        if (Status == DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            throw new InvalidDomainStateException("Return authorization cannot be issued twice.");
        }

        if (Status == DeliveryExceptionStatus.Voided)
        {
            throw new InvalidDomainStateException("A voided exception cannot be authorized for return.");
        }

        if (Status == DeliveryExceptionStatus.Resolved)
        {
            throw new InvalidDomainStateException("An already resolved exception cannot be authorized for return.");
        }

        if (returnAuthorizationId == Guid.Empty)
        {
            throw new ValidationException(nameof(returnAuthorizationId), "A valid ReturnAuthorizationId is required.");
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new ValidationException(nameof(notes), "Authorization notes are required when authorizing Return-To-Sender.");
        }

        ReturnAuthorizationId = returnAuthorizationId;
        ReturnShipmentId = returnShipmentId;
        Resolution = DeliveryExceptionResolution.ReturnToSenderAuthorized;
        Status = DeliveryExceptionStatus.ReturnToSenderInitiated;
        ResolutionNotes = notes.Trim();
        ResolvedByUserId = userId;
        ResolvedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Reverts return authorization on the delivery exception when authorization is cancelled.
    /// </summary>
    public void CancelReturnAuthorization(string reason, Guid userId)
    {
        if (Status != DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            throw new InvalidDomainStateException("Cannot cancel return authorization for an exception that is not in ReturnToSenderInitiated status.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Cancellation reason is required.");
        }

        Status = DeliveryExceptionStatus.UnderReview;
        Resolution = DeliveryExceptionResolution.PendingCarrierReattempt;
        ResolutionNotes = $"Return authorization cancelled: {reason.Trim()} (Prior notes: {ResolutionNotes})";
        ResolvedAtUtc = null;
        ResolvedByUserId = null;
        ReturnAuthorizationId = null;
        ReturnShipmentId = null;
    }

    /// <summary>
    /// Reverts return authorization on the delivery exception when authorization expires.
    /// </summary>
    public void ExpireReturnAuthorization(string reason)
    {
        if (Status != DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            throw new InvalidDomainStateException("Cannot expire return authorization for an exception that is not in ReturnToSenderInitiated status.");
        }

        Status = DeliveryExceptionStatus.UnderReview;
        Resolution = DeliveryExceptionResolution.PendingCarrierReattempt;
        ResolutionNotes = $"Return authorization expired: {reason.Trim()} (Prior notes: {ResolutionNotes})";
        ResolvedAtUtc = null;
        ResolvedByUserId = null;
        ReturnAuthorizationId = null;
        ReturnShipmentId = null;
    }

    /// <summary>
    /// Resolves the delivery exception.
    /// </summary>
    public void Resolve(DeliveryExceptionResolution resolution, string notes, Guid resolvedByUserId)
    {
        if (Status == DeliveryExceptionStatus.Voided)
        {
            throw new InvalidDomainStateException("A voided exception cannot be resolved.");
        }

        if (Status == DeliveryExceptionStatus.Resolved)
        {
            throw new InvalidDomainStateException("Delivery exception is already resolved.");
        }

        if (Status == DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            throw new InvalidDomainStateException("An exception cannot be resolved while a required return workflow remains incomplete.");
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new ValidationException(nameof(notes), "Resolution notes are required when resolving a delivery exception.");
        }

        Resolution = resolution;
        ResolutionNotes = notes.Trim();
        ResolvedByUserId = resolvedByUserId;
        ResolvedAtUtc = DateTime.UtcNow;
        Status = DeliveryExceptionStatus.Resolved;
    }

    /// <summary>
    /// Voids or rescinds an erroneously entered exception.
    /// </summary>
    public void Void(string reason, Guid userId)
    {
        if (Status == DeliveryExceptionStatus.Resolved)
        {
            throw new InvalidDomainStateException("Cannot void an already resolved exception.");
        }

        if (Status == DeliveryExceptionStatus.ReturnToSenderInitiated)
        {
            throw new InvalidDomainStateException("Cannot void an exception with an active return-to-sender workflow.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason is required to void a delivery exception.");
        }

        Resolution = DeliveryExceptionResolution.CarrierErrorRescinded;
        ResolutionNotes = reason.Trim();
        ResolvedByUserId = userId;
        ResolvedAtUtc = DateTime.UtcNow;
        Status = DeliveryExceptionStatus.Voided;
    }

    /// <summary>
    /// Explicitly reopens a resolved or voided delivery exception for renewed operational handling.
    /// </summary>
    public void Reopen(string reason, Guid userId)
    {
        if (Status != DeliveryExceptionStatus.Resolved && Status != DeliveryExceptionStatus.Voided)
        {
            throw new InvalidDomainStateException("Only resolved or voided exceptions can be reopened.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason is required to reopen a delivery exception.");
        }

        Status = DeliveryExceptionStatus.Open;
        Resolution = DeliveryExceptionResolution.PendingCarrierReattempt;
        ResolutionNotes = $"Reopened: {reason.Trim()} (Previous: {ResolutionNotes})";
        ResolvedAtUtc = null;
        ResolvedByUserId = null;
    }

    /// <summary>
    /// Computes deterministic SHA-256 fingerprint for carrier exception deduplication.
    /// </summary>
    public static string ComputeEventFingerprint(Guid clientId, string carrierCode, string trackingNumber, string? carrierEventId, string carrierExceptionCode, DateTime occurredAtUtc)
    {
        string raw;
        if (!string.IsNullOrWhiteSpace(carrierEventId))
        {
            raw = $"{clientId:D}:{carrierCode.Trim().ToUpperInvariant()}:{trackingNumber.Trim().ToUpperInvariant()}:ID:{carrierEventId.Trim()}";
        }
        else
        {
            raw = $"{clientId:D}:{carrierCode.Trim().ToUpperInvariant()}:{trackingNumber.Trim().ToUpperInvariant()}:{carrierExceptionCode.Trim().ToUpperInvariant()}:{occurredAtUtc.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Derives default severity based on delivery exception type.
    /// </summary>
    public static DeliveryExceptionSeverity DetermineDefaultSeverity(DeliveryExceptionType type)
    {
        return type switch
        {
            DeliveryExceptionType.PackageDamagedInTransit or DeliveryExceptionType.LostInTransit => DeliveryExceptionSeverity.Critical,
            DeliveryExceptionType.CustomerRefused or DeliveryExceptionType.AccessRestricted => DeliveryExceptionSeverity.High,
            DeliveryExceptionType.CarrierOperationalDelay or DeliveryExceptionType.WeatherOrNaturalDisaster => DeliveryExceptionSeverity.Low,
            _ => DeliveryExceptionSeverity.Medium
        };
    }
}
