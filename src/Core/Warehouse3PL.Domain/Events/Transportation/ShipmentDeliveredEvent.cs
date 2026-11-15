using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Events.Transportation;

/// <summary>
/// Operational evidence event emitted upon final successful delivery completion.
/// </summary>
public record ShipmentDeliveredEvent : DomainEvent, ITransportationAnalyticsEvent
{
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? ShipmentId { get; init; }
    public string? TrackingNumber { get; init; }
    public string CarrierCode { get; init; } = string.Empty;
    public DateTime DeliveredAtUtc { get; init; }
    public string RecipientSignatureName { get; init; } = string.Empty;
    public string DeliveryLocationType { get; init; } = string.Empty;
    public long SequenceNumber { get; init; }
    public string SourceStreamId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = "CarrierWebhook";
}
