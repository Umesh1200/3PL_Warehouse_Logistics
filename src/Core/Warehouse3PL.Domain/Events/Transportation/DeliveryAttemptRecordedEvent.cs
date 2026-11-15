using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Events.Transportation;

/// <summary>
/// Operational evidence event emitted when a carrier records a physical delivery attempt.
/// Subsequent attempts increment the attempt counter without altering the original first-attempt outcome.
/// </summary>
public record DeliveryAttemptRecordedEvent : DomainEvent, ITransportationAnalyticsEvent
{
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? ShipmentId { get; init; }
    public string? TrackingNumber { get; init; }
    public string CarrierCode { get; init; } = string.Empty;
    public int AttemptNumber { get; init; }
    public DateTime AttemptTimestampUtc { get; init; }
    public bool WasSuccessful { get; init; }
    public string CarrierReasonCode { get; init; } = string.Empty;
    public long SequenceNumber { get; init; }
    public string SourceStreamId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = "CarrierWebhook";
}
