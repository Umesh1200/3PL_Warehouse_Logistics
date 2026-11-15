using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class IngestReturnTrackingRequest
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
    public string? DestinationFacilityCode { get; set; }
    public string? DockDoorLocation { get; set; }
    public string? RawPayloadJson { get; set; }
    public Guid? TenantIdOverride { get; set; }
}

public class IngestReturnTrackingResult
{
    public bool IsSuccess { get; set; }
    public bool IsDuplicate { get; set; }
    public Guid? ReturnTrackingEventId { get; set; }
    public Guid ReturnShipmentId { get; set; }
    public Guid ReturnAuthorizationId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public ReturnShipmentStatus ShipmentStatus { get; set; }
    public ReturnAuthorizationStatus AuthorizationStatus { get; set; }
    public DateTime? CarrierHandoverAtUtc { get; set; }
    public DateTime? DeliveredAtWarehouseUtc { get; set; }
    public Guid? InboundHandoffId { get; set; }
    public string? InboundHandoffReference { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ReturnInboundHandoffDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid DestinationFacilityId { get; set; }
    public string DestinationFacilityName { get; set; } = string.Empty;
    public Guid ReturnShipmentId { get; set; }
    public Guid ReturnAuthorizationId { get; set; }
    public Guid OriginalPackageId { get; set; }
    public Guid OriginalOrderId { get; set; }
    public string HandoffReference { get; set; } = string.Empty;
    public string ReturnRmaNumber { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string CarrierCode { get; set; } = string.Empty;
    public ReturnInboundHandoffStatus Status { get; set; }
    public DateTime CarrierDeliveredAtUtc { get; set; }
    public DateTime HandoffCreatedAtUtc { get; set; }
    public string? DockDoorLocation { get; set; }
    public string? Notes { get; set; }
}

public class ReturnTrackingTimelineEventDto
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public ReturnShipmentStatus Status { get; set; }
    public string? CarrierStatusCode { get; set; }
    public string? CarrierStatusDescription { get; set; }
    public string? Location { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
}

/// <summary>
/// Service managing the ingestion of reverse carrier tracking milestones,
/// carrier custody handover, warehouse arrival, and inbound receiving handoffs.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4: Return-to-Sender Authorization, Return Shipment &amp; Tracking.
/// </summary>
public interface IReturnTrackingService
{
    /// <summary>
    /// Ingests a carrier reverse tracking milestone, normalizes status, verifies custody handover,
    /// and generates an inbound receiving handoff upon carrier dock delivery.
    /// Operates idempotently and monotonically.
    /// </summary>
    Task<IngestReturnTrackingResult> IngestReturnTrackingEventAsync(
        IngestReturnTrackingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the inbound receiving handoff associated with a return shipment.
    /// </summary>
    Task<ReturnInboundHandoffDto?> GetInboundHandoffByReturnShipmentIdAsync(
        Guid returnShipmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the inbound receiving handoff associated with a return authorization.
    /// </summary>
    Task<ReturnInboundHandoffDto?> GetInboundHandoffByAuthorizationIdAsync(
        Guid returnAuthorizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the chronological audit history of reverse tracking events for a return shipment.
    /// </summary>
    Task<IReadOnlyList<ReturnTrackingTimelineEventDto>> GetReturnTrackingHistoryAsync(
        Guid returnShipmentId,
        CancellationToken cancellationToken = default);
}

