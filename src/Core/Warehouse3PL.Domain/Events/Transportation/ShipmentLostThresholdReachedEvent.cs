using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Events.Transportation;

/// <summary>
/// Operational evidence event emitted when a shipment exceeds tracking inactivity thresholds without delivery.
/// Advisory alarm only; does not initiate legal loss claims.
/// </summary>
public record ShipmentLostThresholdReachedEvent : DomainEvent, ITransportationAnalyticsEvent
{
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public Guid? ShipmentId { get; init; }
    public string? TrackingNumber { get; init; }
    public string CarrierCode { get; init; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; init; } = CarrierServiceLevel.StandardGround;
    public DateTime TriggeredAtUtc { get; init; }
    public decimal HoursWithoutScanMovement { get; init; }
    public decimal ThresholdHours { get; init; }
    public long SequenceNumber { get; init; }
    public string SourceStreamId { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = "AnalyticsAlarm";
}
