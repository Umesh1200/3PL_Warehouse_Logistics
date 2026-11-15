using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public class RecordBillableActivityRequest
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
}

public class RecordBillableActivityResult
{
    public bool IsSuccess { get; set; }
    public bool IsDuplicate { get; set; }
    public Guid BillableActivityId { get; set; }
    public BillableActivityStatus Status { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class CreateBillingPeriodRequest
{
    public Guid ClientId { get; set; }
    public Guid? FacilityId { get; set; }
    public string PeriodCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    public BillingFrequency Frequency { get; set; } = BillingFrequency.Monthly;
    public string? Notes { get; set; }
}

public class BillableActivityDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? BillingPeriodId { get; set; }
    public BillableActivityType ActivityType { get; set; }
    public BillableActivityStatus Status { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "UNIT";
    public decimal? UnitPrice { get; set; }
    public decimal? TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public Guid? RateCardId { get; set; }
    public Guid? RateCardTierId { get; set; }
    public int? RateCardVersion { get; set; }
    public DateTime? RatedAtUtc { get; set; }
    public Guid? InvoiceId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class BillingPeriodDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid? FacilityId { get; set; }
    public string PeriodCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    public BillingPeriodStatus Status { get; set; }
    public BillingFrequency Frequency { get; set; }
    public DateTime? LockedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime? InvoicedAtUtc { get; set; }
    public int ActivityCount { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Service managing the commercial billable activity ledger, idempotency deduplication,
/// billing cycle periods, and activity-based costing intake.
/// DB-SPEC-3PL-2026 Phase 5.0 Commercial Domain &amp; Billing Foundation.
/// </summary>
public interface IBillableActivityService
{
    /// <summary>
    /// Records a billable operational activity with guaranteed idempotency deduplication.
    /// Retries or repeat calls with the same (ClientId, IdempotencyKey) safely return the existing record.
    /// </summary>
    Task<RecordBillableActivityResult> RecordActivityAsync(
        RecordBillableActivityRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically records a batch of billable operational activities.
    /// </summary>
    Task<IReadOnlyList<RecordBillableActivityResult>> RecordActivitiesBatchAsync(
        IReadOnlyList<RecordBillableActivityRequest> requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and opens a new commercial billing period for a client tenant.
    /// </summary>
    Task<BillingPeriodDto> CreateBillingPeriodAsync(
        CreateBillingPeriodRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks a billing period to freeze new activity attachment ahead of a billing run.
    /// </summary>
    Task<BillingPeriodDto> LockBillingPeriodAsync(
        Guid periodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens a locked billing period.
    /// </summary>
    Task<BillingPeriodDto> ReopenBillingPeriodAsync(
        Guid periodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a locked billing period upon rating finalization.
    /// </summary>
    Task<BillingPeriodDto> CloseBillingPeriodAsync(
        Guid periodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a billable activity by its unique identifier.
    /// </summary>
    Task<BillableActivityDto?> GetActivityByIdAsync(
        Guid activityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all billable activities linked to an operational source entity.
    /// </summary>
    Task<IReadOnlyList<BillableActivityDto>> GetActivitiesBySourceAsync(
        Guid clientId,
        string sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all unrated (PendingRating) activities for a client tenant and optional billing period.
    /// </summary>
    Task<IReadOnlyList<BillableActivityDto>> GetUnratedActivitiesAsync(
        Guid clientId,
        Guid? periodId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves billing periods for a client tenant with optional status filtering.
    /// </summary>
    Task<IReadOnlyList<BillingPeriodDto>> GetBillingPeriodsAsync(
        Guid clientId,
        BillingPeriodStatus? status = null,
        CancellationToken cancellationToken = default);
}

