using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Authoritative Proof of Delivery (POD) evidentiary record.
/// Stores delivery confirmation metadata and secure object storage keys for signatures and photos.
/// DB-SPEC-3PL-2026 Phase 4.5 Outbound Shipment Tracking & Delivery Confirmation.
/// </summary>
public class ProofOfDelivery : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    /// <summary>
    /// Unique 1:1 association to the delivered package.
    /// </summary>
    public Guid PackageId { get; set; }
    public Guid? ShipmentId { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;

    public string? RecipientName { get; set; }
    public string? DeliveryLocationDescription { get; set; }

    public bool IsSigned { get; set; }

    // Object Storage Evidence References (Decoupled from PostgreSQL row data)
    public string? SignatureObjectKey { get; set; }
    public string? PhotoObjectKey { get; set; }
    public string? SignatureContentType { get; set; }
    public string? PhotoContentType { get; set; }
    public string? SignatureSha256 { get; set; }
    public string? PhotoSha256 { get; set; }

    // Staged storage reliability flag
    public bool IsStorageFinalized { get; set; } = true;

    // Geographic verification
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    // External identifiers and idempotency
    public string? CarrierConfirmationNumber { get; set; }
    public string? CarrierEventId { get; set; }
    public string? IdempotencyKey { get; set; }

    public PodEvidenceSource EvidenceSource { get; set; } = PodEvidenceSource.CarrierApi;

    // Source-specific operational audit fields
    public string? VerificationReason { get; set; }
    public string? ConsigneeAcknowledgmentText { get; set; }

    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime DeliveredAtUtc { get; set; }
    public bool HasExplicitDeliveredAt { get; set; }

    // Supersession and audited amendment support
    public bool IsSuperseded { get; set; } = false;
    public Guid? SupersededByPodId { get; set; }
    public string? SupersededReason { get; set; }
    public DateTime? SupersededAtUtc { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Package Package { get; set; } = null!;
    public Shipment? Shipment { get; set; }

    public ProofOfDelivery()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Validates domain rules including geographic coordinate bounds and evidence source requirements.
    /// </summary>
    public void Validate()
    {
        if (Latitude.HasValue && !Longitude.HasValue)
        {
            throw new ValidationException(nameof(Longitude), "Longitude must be specified when Latitude is provided.");
        }

        if (Longitude.HasValue && !Latitude.HasValue)
        {
            throw new ValidationException(nameof(Latitude), "Latitude must be specified when Longitude is provided.");
        }

        if (Latitude.HasValue && (Latitude.Value < -90.0m || Latitude.Value > 90.0m))
        {
            throw new ValidationException(nameof(Latitude), $"Latitude '{Latitude.Value}' is outside the valid range [-90.0, 90.0].");
        }

        if (Longitude.HasValue && (Longitude.Value < -180.0m || Longitude.Value > 180.0m))
        {
            throw new ValidationException(nameof(Longitude), $"Longitude '{Longitude.Value}' is outside the valid range [-180.0, 180.0].");
        }

        if (DeliveredAtUtc > DateTime.UtcNow.AddHours(2))
        {
            throw new ValidationException(nameof(DeliveredAtUtc), "Delivery timestamp cannot be in the future.");
        }

        if (!string.IsNullOrWhiteSpace(VerificationReason) && VerificationReason.Trim().Length > 500)
        {
            throw new ValidationException(nameof(VerificationReason), "Verification reason cannot exceed 500 characters.");
        }

        if (!string.IsNullOrWhiteSpace(ConsigneeAcknowledgmentText) && ConsigneeAcknowledgmentText.Trim().Length > 1000)
        {
            throw new ValidationException(nameof(ConsigneeAcknowledgmentText), "Consignee acknowledgment text cannot exceed 1000 characters.");
        }

        if (EvidenceSource == PodEvidenceSource.DriverHandheld)
        {
            bool hasSignature = IsSigned && !string.IsNullOrWhiteSpace(SignatureObjectKey);
            bool hasPhoto = !string.IsNullOrWhiteSpace(PhotoObjectKey);
            bool hasRecipient = !string.IsNullOrWhiteSpace(RecipientName);

            if (!hasSignature && !hasPhoto && !hasRecipient)
            {
                throw new ValidationException(nameof(EvidenceSource),
                    "Driver handheld delivery confirmation requires at least one of: signature, photo, or recipient name.");
            }
        }
    }

    /// <summary>
    /// Evaluates if an incoming submission is an idempotent duplicate of this authoritative record.
    /// </summary>
    public bool IsIdempotentDuplicate(
        string? candidateCarrierConfirmationNumber,
        string? candidateCarrierEventId,
        string? candidateIdempotencyKey,
        PodEvidenceSource candidateSource)
    {
        if (candidateSource == EvidenceSource)
        {
            // Stable carrier confirmation match
            if (!string.IsNullOrWhiteSpace(CarrierConfirmationNumber) &&
                !string.IsNullOrWhiteSpace(candidateCarrierConfirmationNumber) &&
                string.Equals(CarrierConfirmationNumber.Trim(), candidateCarrierConfirmationNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Stable carrier event ID match
            if (!string.IsNullOrWhiteSpace(CarrierEventId) &&
                !string.IsNullOrWhiteSpace(candidateCarrierEventId) &&
                string.Equals(CarrierEventId.Trim(), candidateCarrierEventId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Client/Application idempotency key match
            if (!string.IsNullOrWhiteSpace(IdempotencyKey) &&
                !string.IsNullOrWhiteSpace(candidateIdempotencyKey) &&
                string.Equals(IdempotencyKey.Trim(), candidateIdempotencyKey.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Evaluates if an incoming submission directly contradicts this authoritative record.
    /// </summary>
    public bool HasConflictWith(
        string? candidateRecipientName,
        string? candidateCarrierConfirmationNumber,
        PodEvidenceSource candidateSource)
    {
        // Contradictory carrier confirmation numbers from the same carrier
        if (!string.IsNullOrWhiteSpace(CarrierConfirmationNumber) &&
            !string.IsNullOrWhiteSpace(candidateCarrierConfirmationNumber) &&
            !string.Equals(CarrierConfirmationNumber.Trim(), candidateCarrierConfirmationNumber.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Contradictory recipient names if both are explicitly provided
        if (!string.IsNullOrWhiteSpace(RecipientName) &&
            !string.IsNullOrWhiteSpace(candidateRecipientName) &&
            !string.Equals(RecipientName.Trim(), candidateRecipientName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Explicitly supersedes this POD record with a newer audited amendment.
    /// </summary>
    public void Supersede(Guid newPodId, string reason, DateTime supersededAtUtc)
    {
        if (IsSuperseded)
        {
            throw new InvalidDomainStateException("Proof of delivery is already superseded.");
        }

        if (newPodId == Guid.Empty)
        {
            throw new ValidationException(nameof(newPodId), "New Proof of Delivery ID must be provided for supersession.");
        }

        if (newPodId == Id)
        {
            throw new ValidationException(nameof(newPodId), "A Proof of Delivery cannot supersede itself.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Supersession reason is required.");
        }

        IsSuperseded = true;
        SupersededByPodId = newPodId;
        SupersededReason = reason.Trim();
        SupersededAtUtc = supersededAtUtc;
    }
}
