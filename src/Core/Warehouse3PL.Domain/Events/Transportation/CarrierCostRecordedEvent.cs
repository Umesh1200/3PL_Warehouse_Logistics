using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Events.Transportation;

/// <summary>
/// Operational evidence event capturing quoted base freight and initial estimated charges from dispatch manifest.
/// Distinct from tracking milestone evidence.
/// </summary>
public record CarrierCostRecordedEvent : DomainEvent, ITransportationAnalyticsEvent
{
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? ShipmentId { get; init; }
    public string? TrackingNumber { get; init; }
    public string CarrierCode { get; init; } = string.Empty;
    public decimal QuotedBaseFreightUsd { get; init; }
    public decimal QuotedAccessorialsUsd { get; init; }
    public decimal EstimatedAmountUsd { get; init; }
    public string Currency { get; init; } = "USD";
    public long SequenceNumber { get; init; }
    public string SourceStreamId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = "RateEngine";
}
