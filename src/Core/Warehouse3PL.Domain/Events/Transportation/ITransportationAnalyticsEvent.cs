namespace Warehouse3PL.Domain.Events.Transportation;

/// <summary>
/// Common contract for all transportation analytics operational evidence events.
/// Implements facility and client tenancy, source stream identity, monotonic sequencing, and optional shipment associations.
/// </summary>
public interface ITransportationAnalyticsEvent
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
    Guid FacilityId { get; }
    Guid ClientId { get; }
    Guid? ShipmentId { get; }
    string? TrackingNumber { get; }
    string CarrierCode { get; }
    long SequenceNumber { get; }
    string SourceStreamId { get; }
    string SourceSystem { get; }
}
