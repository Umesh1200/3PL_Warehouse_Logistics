using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Events.Transportation;

/// <summary>
/// Operational evidence event emitted when cryptographic or signed Proof of Delivery documentation is captured.
/// </summary>
public record ProofOfDeliveryRecordedEvent : DomainEvent, ITransportationAnalyticsEvent
{
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? ShipmentId { get; init; }
    public string? TrackingNumber { get; init; }
    public string CarrierCode { get; init; } = string.Empty;
    public DateTime PodVerifiedAtUtc { get; init; }
    public string DocumentUri { get; init; } = string.Empty;
    public string VerificationStatus { get; init; } = "Verified";
    public long SequenceNumber { get; init; }
    public string SourceStreamId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = "CarrierPod";
}
