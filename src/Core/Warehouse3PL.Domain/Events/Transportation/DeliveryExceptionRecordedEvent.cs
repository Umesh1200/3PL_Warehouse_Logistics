using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Events.Transportation;

/// <summary>
/// Operational evidence event emitted when a delivery exception or delay scan occurs.
/// Normalizes external carrier codes into canonical domain exception categories.
/// </summary>
public record DeliveryExceptionRecordedEvent : DomainEvent, ITransportationAnalyticsEvent
{
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? ShipmentId { get; init; }
    public string? TrackingNumber { get; init; }
    public string CarrierCode { get; init; } = string.Empty;
    public DateTime ExceptionTimestampUtc { get; init; }
    public CanonicalDeliveryExceptionCategory CanonicalCategory { get; init; } = CanonicalDeliveryExceptionCategory.Other;
    public string RawCarrierCode { get; init; } = string.Empty;
    public string RawCarrierDescription { get; init; } = string.Empty;
    public bool IsDisruptive { get; init; } = true;
    public long SequenceNumber { get; init; }
    public string SourceStreamId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = "CarrierWebhook";
}
