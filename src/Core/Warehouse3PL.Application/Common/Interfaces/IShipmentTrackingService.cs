using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class IngestTrackingEventRequest
{
    public string CarrierCode { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string? CarrierEventId { get; set; }
    public string CarrierEventCode { get; set; } = string.Empty;
    public string CarrierDescription { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? EventCity { get; set; }
    public string? EventStateOrProvince { get; set; }
    public string? EventPostalCode { get; set; }
    public string? EventCountry { get; set; }
    public string? RawPayloadJson { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class IngestTrackingEventResult
{
    public bool IsSuccess { get; set; }
    public bool IsDuplicate { get; set; }
    public Guid? TrackingEventId { get; set; }
    public Guid PackageId { get; set; }
    public Guid? ShipmentId { get; set; }
    public Guid OutboundOrderId { get; set; }
    public TrackingMilestoneStatus NormalizedMilestone { get; set; }
    public TrackingMilestoneStatus PackageTrackingStatus { get; set; }
    public OrderDeliveryStatus OrderDeliveryStatus { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SyncShipmentTrackingRequest
{
    public Guid ShipmentId { get; set; }
    public Guid? UserId { get; set; }
}

public class SyncShipmentTrackingResult
{
    public bool IsSuccess { get; set; }
    public Guid ShipmentId { get; set; }
    public int PackagesEvaluated { get; set; }
    public int NewEventsIngested { get; set; }
    public int DuplicateEventsSkipped { get; set; }
    public TrackingMilestoneStatus ShipmentDeliveryStatus { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class EvaluateOrderDeliveryRollupResult
{
    public Guid OutboundOrderId { get; set; }
    public OrderDeliveryStatus DeliveryStatus { get; set; }
    public int TotalPackages { get; set; }
    public int DeliveredPackages { get; set; }
    public int InTransitPackages { get; set; }
    public int ExceptionPackages { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
}

public class TrackingTimelineEventDto
{
    public Guid Id { get; set; }
    public TrackingMilestoneStatus MilestoneStatus { get; set; }
    public string CarrierEventCode { get; set; } = string.Empty;
    public string CarrierDescription { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
}

public class PackageTrackingTimelineDto
{
    public Guid PackageId { get; set; }
    public string PackageNumber { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public TrackingMilestoneStatus CurrentTrackingStatus { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public List<TrackingTimelineEventDto> Events { get; set; } = [];
}

public interface IShipmentTrackingService
{
    Task<IngestTrackingEventResult> IngestTrackingEventAsync(
        IngestTrackingEventRequest request,
        CancellationToken cancellationToken = default);

    Task<SyncShipmentTrackingResult> SyncShipmentTrackingAsync(
        SyncShipmentTrackingRequest request,
        CancellationToken cancellationToken = default);

    Task<EvaluateOrderDeliveryRollupResult> EvaluateOrderDeliveryRollupAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<PackageTrackingTimelineDto?> GetPackageTrackingTimelineAsync(
        Guid packageId,
        CancellationToken cancellationToken = default);

    Task<EvaluateOrderDeliveryRollupResult?> GetOrderDeliveryStatusAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);
}

