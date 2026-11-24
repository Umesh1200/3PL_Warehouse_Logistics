using System.Text.Json.Serialization;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Authoritative Proof of Delivery submission, validation, and staged evidence ingestion service.
/// Coordinates multi-channel evidence ingestion, staged object storage, idempotency, and audited supersession.
/// DB-SPEC-3PL-2026 Phase 4.5 Shipment Tracking & Delivery Confirmation (Step 3A).
/// </summary>
public interface IProofOfDeliveryService
{
    /// <summary>
    /// Submits and ingests a Proof of Delivery record with staged evidence upload, validation, and persistence.
    /// </summary>
    Task<SubmitProofOfDeliveryResult> SubmitProofOfDeliveryAsync(
        SubmitProofOfDeliveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Audited supersession workflow: replaces an existing active POD with a new validated POD.
    /// Preserves original evidence and links historical audit trail.
    /// </summary>
    Task<SubmitProofOfDeliveryResult> SupersedeProofOfDeliveryAsync(
        SupersedeProofOfDeliveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves active or specific POD with short-lived pre-signed download URLs for authorized users.
    /// </summary>
    Task<ProofOfDeliveryDto?> GetProofOfDeliveryByPackageIdAsync(
        Guid packageId,
        TimeSpan? urlExpiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a POD by its primary ID.
    /// </summary>
    Task<ProofOfDeliveryDto?> GetProofOfDeliveryByIdAsync(
        Guid podId,
        TimeSpan? urlExpiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles unfinalized pending POD records and purges orphaned staged objects.
    /// </summary>
    Task<int> ReconcilePendingStorageAsync(
        TimeSpan olderThan,
        CancellationToken cancellationToken = default);
}

public class SubmitProofOfDeliveryRequest
{
    public Guid PackageId { get; set; }
    public Guid? TenantIdOverride { get; set; }

    public string? RecipientName { get; set; }
    public string? DeliveryLocationDescription { get; set; }
    public bool IsSigned { get; set; }

    // Evidence binary streams (staged outside DB transactions)
    public Stream? SignatureStream { get; set; }
    public string? SignatureContentType { get; set; }

    public Stream? PhotoStream { get; set; }
    public string? PhotoContentType { get; set; }

    // Geographic verification
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    // External identifiers & idempotency
    public string? CarrierConfirmationNumber { get; set; }
    public string? CarrierEventId { get; set; }
    public string? IdempotencyKey { get; set; }

    public PodEvidenceSource EvidenceSource { get; set; } = PodEvidenceSource.DriverHandheld;

    private DateTime _deliveredAtUtc = DateTime.UtcNow;
    private bool _hasExplicitDeliveredAtUtc;

    public DateTime DeliveredAtUtc
    {
        get => _deliveredAtUtc;
        set
        {
            _deliveredAtUtc = value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
            _hasExplicitDeliveredAtUtc = true;
        }
    }

    [JsonIgnore]
    public bool HasExplicitDeliveredAtUtc
    {
        get => _hasExplicitDeliveredAtUtc;
        set => _hasExplicitDeliveredAtUtc = value;
    }

    // Source-specific audit fields
    public string? VerificationReason { get; set; }
    public string? ConsigneeAcknowledgmentText { get; set; }

    [JsonIgnore]
    public bool IsInteractiveSubmission { get; set; }

    [JsonIgnore]
    public Func<CancellationToken, Task>? BeforeCommitCustodyValidationAsync { get; set; }
}

public class SubmitProofOfDeliveryResult
{
    public bool IsSuccess { get; set; }
    public bool IsDuplicate { get; set; }
    public Guid ProofOfDeliveryId { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public string? RecipientName { get; set; }
    public string? DeliveryLocationDescription { get; set; }
    public bool IsSigned { get; set; }
    public PodEvidenceSource EvidenceSource { get; set; }
    public string? VerificationReason { get; set; }
    public string? ConsigneeAcknowledgmentText { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public DateTime DeliveredAtUtc { get; set; }
    public bool HasExplicitDeliveredAt { get; set; }
    public bool IsStorageFinalized { get; set; }
    public string? SignatureSha256 { get; set; }
    public string? PhotoSha256 { get; set; }
    public string? PreSignedSignatureUrl { get; set; }
    public string? PreSignedPhotoUrl { get; set; }
    public DateTime? PreSignedUrlsExpireAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SupersedeProofOfDeliveryRequest
{
    public Guid ExistingPodId { get; set; }
    public SubmitProofOfDeliveryRequest ReplacementSubmission { get; set; } = null!;
    public string Reason { get; set; } = string.Empty;
}

public class ProofOfDeliveryDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public Guid? ShipmentId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public string? RecipientName { get; set; }
    public string? DeliveryLocationDescription { get; set; }
    public bool IsSigned { get; set; }
    public bool IsStorageFinalized { get; set; }
    public string? PreSignedSignatureUrl { get; set; }
    public string? PreSignedPhotoUrl { get; set; }
    public string? SignatureSha256 { get; set; }
    public string? PhotoSha256 { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? CarrierConfirmationNumber { get; set; }
    public string? CarrierEventId { get; set; }
    public string? IdempotencyKey { get; set; }
    public PodEvidenceSource EvidenceSource { get; set; }
    public string? VerificationReason { get; set; }
    public string? ConsigneeAcknowledgmentText { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public DateTime DeliveredAtUtc { get; set; }
    public bool HasExplicitDeliveredAt { get; set; }
    public bool IsSuperseded { get; set; }
    public Guid? SupersededByPodId { get; set; }
    public string? SupersededReason { get; set; }
    public DateTime? SupersededAtUtc { get; set; }
    public DateTime? PreSignedUrlsExpireAtUtc { get; set; }
}

