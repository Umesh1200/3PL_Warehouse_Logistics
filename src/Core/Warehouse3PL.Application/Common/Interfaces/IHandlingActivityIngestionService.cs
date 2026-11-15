using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Operational activity ingestion request payload.
/// </summary>
public class IngestHandlingActivityRequest
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public BillableActivityType ActivityType { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "UNIT";
    public DateTime OccurredAtUtc { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? BillingPeriodId { get; set; }
    public string? MetadataJson { get; set; }
    public bool AutoRate { get; set; } = true;
}

/// <summary>
/// Comprehensive result of a handling activity ingestion operation.
/// </summary>
public class IngestHandlingActivityResult
{
    public bool IsSuccess { get; set; }
    public bool IsDuplicate { get; set; }
    public bool IsRated { get; set; }
    public Guid BillableActivityId { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public BillableActivityType ActivityType { get; set; }
    public BillableActivityStatus Status { get; set; }
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "UNIT";
    public decimal? UnitPrice { get; set; }
    public decimal? TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public Guid? RateCardId { get; set; }
    public Guid? RateCardScheduleId { get; set; }
    public Guid? RateCardTierId { get; set; }
    public int? RateCardVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Service contract for ingesting operational warehouse, fulfillment, reverse-logistics, and VAS events
/// into immutable commercial BillableActivity records with deterministic idempotency and immediate auto-rating.
/// DB-SPEC-3PL-2026 Phase 5.3 Activity-Based Handling Charges &amp; Operational Ingestion Engine.
/// </summary>
public interface IHandlingActivityIngestionService
{
    /// <summary>
    /// Ingests a generic handling activity into the billable activity ledger with idempotency deduplication and optional auto-rating.
    /// </summary>
    Task<IngestHandlingActivityResult> IngestActivityAsync(
        IngestHandlingActivityRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically ingests a batch of handling activities into the billable activity ledger.
    /// </summary>
    Task<IReadOnlyList<IngestHandlingActivityResult>> IngestBatchAsync(
        IReadOnlyList<IngestHandlingActivityRequest> requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests an inbound receiving activity (per pallet, carton, or unit).
    /// Idempotency Key: INB-RCV-{receiptLineId}
    /// </summary>
    Task<IngestHandlingActivityResult> IngestInboundReceiptAsync(
        Guid clientId,
        Guid facilityId,
        Guid receiptLineId,
        string poNumber,
        decimal quantity,
        string unitOfMeasure,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests an inbound QA inspection activity.
    /// Idempotency Key: INB-INSP-{inspectionId}
    /// </summary>
    Task<IngestHandlingActivityResult> IngestInboundInspectionAsync(
        Guid clientId,
        Guid facilityId,
        Guid inspectionId,
        string reference,
        decimal sampleQuantity,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests a putaway task execution activity.
    /// Idempotency Key: PUTAWAY-{putawayTaskId}
    /// </summary>
    Task<IngestHandlingActivityResult> IngestPutawayTaskAsync(
        Guid clientId,
        Guid facilityId,
        Guid putawayTaskId,
        string taskReference,
        decimal quantity,
        string unitOfMeasure,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests a cross-dock task execution activity.
    /// Idempotency Key: CROSSDOCK-{crossDockTaskId}
    /// </summary>
    Task<IngestHandlingActivityResult> IngestCrossDockExecutionAsync(
        Guid clientId,
        Guid facilityId,
        Guid crossDockTaskId,
        string reference,
        decimal quantity,
        string unitOfMeasure,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests an outbound base order fulfillment fee.
    /// Idempotency Key: OUT-ORD-{orderId}
    /// </summary>
    Task<IngestHandlingActivityResult> IngestOrderFulfillmentAsync(
        Guid clientId,
        Guid facilityId,
        Guid orderId,
        string orderNumber,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests outbound pick execution charges. Emits both a PickLine activity (per line picked)
    /// and a PickUnit activity (for total units/pieces picked).
    /// Idempotency Keys: PICK-LINE-{pickTaskId} and PICK-UNIT-{pickTaskId}
    /// </summary>
    Task<IReadOnlyList<IngestHandlingActivityResult>> IngestPickExecutionAsync(
        Guid clientId,
        Guid facilityId,
        Guid pickTaskId,
        string taskNumber,
        decimal pickedQuantity,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests outbound package packing charges. Emits a PackingCarton activity,
    /// and optionally a PackagingMaterial consumption activity if packaging material is specified.
    /// Idempotency Keys: PACK-BOX-{packageId} and PACK-MAT-{packageId}-{packagingMaterialId}
    /// </summary>
    Task<IReadOnlyList<IngestHandlingActivityResult>> IngestPackingSessionAsync(
        Guid clientId,
        Guid facilityId,
        Guid packageId,
        string packageNumber,
        Guid? packagingMaterialId,
        string? materialCode,
        decimal materialQuantity,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests outbound shipping handling / staging and dispatch activity.
    /// Idempotency Key: SHIP-DISP-{shipmentId}
    /// </summary>
    Task<IngestHandlingActivityResult> IngestShipmentDispatchAsync(
        Guid clientId,
        Guid facilityId,
        Guid shipmentId,
        string shipmentNumber,
        decimal packageCount,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests reverse-logistics return processing charges.
    /// Emits ReturnProcessing (reverse receipt), and optionally ReturnInspection and Restocking activities.
    /// Idempotency Keys: RET-RCV-{returnLineId}, RET-INSP-{returnLineId}, RET-RESTOCK-{returnLineId}
    /// </summary>
    Task<IReadOnlyList<IngestHandlingActivityResult>> IngestReturnProcessingAsync(
        Guid clientId,
        Guid facilityId,
        Guid returnLineId,
        string rmaNumber,
        decimal quantity,
        bool isInspected,
        bool isRestocked,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests value-added service (VAS) activities such as kitting, assembly, or special handling.
    /// Idempotency Key: VAS-{vasType}-{sourceId}-{reference}
    /// </summary>
    Task<IngestHandlingActivityResult> IngestVasActivityAsync(
        Guid clientId,
        Guid facilityId,
        BillableActivityType vasType,
        Guid sourceId,
        string sourceReference,
        decimal quantity,
        string unitOfMeasure,
        string description,
        DateTime occurredAtUtc,
        bool autoRate = true,
        CancellationToken cancellationToken = default);
}

