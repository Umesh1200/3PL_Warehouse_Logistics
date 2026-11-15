using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Events.Transportation;

/// <summary>
/// Operational evidence event emitted when carrier tracking scans record intermediate transit progression.
/// </summary>
public record ShipmentTrackingUpdatedEvent : DomainEvent, ITransportationAnalyticsEvent
{
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? ShipmentId { get; init; }
    public string? TrackingNumber { get; init; }
    public string CarrierCode { get; init; } = string.Empty;
    public DateTime MilestoneUtc { get; init; }
    public string CarrierStatusCode { get; init; } = string.Empty;
    public string CarrierRawDescription { get; init; } = string.Empty;
    public string LocationCity { get; init; } = string.Empty;
    public string LocationState { get; init; } = string.Empty;
    public string LocationCountry { get; init; } = string.Empty;
    public long SequenceNumber { get; init; }
    public string SourceStreamId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = "CarrierWebhook";
}
