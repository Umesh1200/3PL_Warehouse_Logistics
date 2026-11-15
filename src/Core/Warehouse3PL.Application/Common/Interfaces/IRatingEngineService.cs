using Warehouse3PL.Domain.Entities.Billing;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Result summary of a batch activity rating execution run.
/// </summary>
public class RatingBatchResult
{
    public int TotalActivitiesFound { get; set; }
    public int SuccessfullyRatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int MissingRateCount { get; set; }
    public decimal TotalRatedAmount { get; set; }
    public List<Guid> RatedActivityIds { get; set; } = new();
    public List<MissingRateDetail> MissingRateDetails { get; set; } = new();
}

/// <summary>
/// Diagnostic details when a billable activity could not be rated due to missing commercial agreement lines.
/// </summary>
public class MissingRateDetail
{
    public Guid ActivityId { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public Guid FacilityId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Core rating engine interface for evaluating pending billable activities against contractual client rate cards.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public interface IRatingEngineService
{
    /// <summary>
    /// Rates a single pending billable activity against the authoritative client rate card schedule.
    /// Preserves immutable rating snapshot and transitions activity to Rated status.
    /// Throws DomainException if missing schedule/line or currency mismatch occurs.
    /// </summary>
    Task<BillableActivity> RateActivityAsync(
        Guid activityId,
        Guid? ratedByUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates and rates pending billable activities in batch for a given client, optional facility, or billing period.
    /// Safely skips already rated activities, logs missing rates, and commits successful ratings atomically.
    /// </summary>
    Task<RatingBatchResult> RateActivitiesBatchAsync(
        Guid clientId,
        Guid? facilityId = null,
        Guid? billingPeriodId = null,
        Guid? ratedByUserId = null,
        CancellationToken cancellationToken = default);
}

