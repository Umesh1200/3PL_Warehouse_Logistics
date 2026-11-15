using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Authoritative calculation and commercial rating engine.
/// Evaluates PendingRating billable activities against contractual rate cards, volume tiers,
/// facility overrides, and contractual spend commitments, applying an immutable commercial snapshot.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public class RatingEngineService : IRatingEngineService
{
    private readonly IApplicationDbContext _context;
    private readonly IRateCardService _rateCardService;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<RatingEngineService> _logger;

    public RatingEngineService(
        IApplicationDbContext context,
        IRateCardService rateCardService,
        ITenantProvider tenantProvider,
        ILogger<RatingEngineService> logger)
    {
        _context = context;
        _rateCardService = rateCardService;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BillableActivity> RateActivityAsync(
        Guid activityId,
        Guid? ratedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        var activity = await _context.BillableActivities
            .FirstOrDefaultAsync(a => a.Id == activityId, cancellationToken);

        if (activity == null)
        {
            throw new EntityNotFoundException(nameof(BillableActivity), activityId);
        }

        ValidateTenantAccess(activity.ClientId);

        // Idempotency: If activity is already Rated, return existing immutable snapshot without re-rating
        if (activity.Status == BillableActivityStatus.Rated)
        {
            _logger.LogInformation("Activity '{ActivityId}' is already rated. Preserving historical pricing snapshot.", activityId);
            return activity;
        }

        if (activity.Status == BillableActivityStatus.Invoiced)
        {
            throw new InvalidDomainStateException("Cannot rate an activity that has already been invoiced.");
        }

        if (activity.Status == BillableActivityStatus.Cancelled)
        {
            throw new InvalidDomainStateException("Cannot rate a cancelled billable activity.");
        }

        // Precedence Resolution: Exact Facility Schedule Line -> Client-Wide Fallback Line -> Missing Rate
        var (schedule, line) = await _rateCardService.GetEffectiveRateLineAsync(
            activity.ClientId,
            activity.FacilityId,
            activity.ActivityType,
            activity.OccurredAtUtc,
            activity.Currency,
            cancellationToken);

        if (schedule == null || line == null)
        {
            _logger.LogWarning(
                "No contractual rate found for Client '{ClientId}', Facility '{FacilityId}', Activity '{ActivityType}' at '{OccurredAtUtc:O}'. Activity remains PendingRating.",
                activity.ClientId, activity.FacilityId, activity.ActivityType, activity.OccurredAtUtc);

            throw new InvalidDomainStateException(
                $"No applicable active commercial rate card line found for ActivityType '{activity.ActivityType}' " +
                $"at Facility '{activity.FacilityId}' effective for '{activity.OccurredAtUtc:yyyy-MM-dd}'. Activity remains PendingRating.");
        }

        // Strict Currency Guard
        if (!string.Equals(schedule.Currency, activity.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(nameof(activity.Currency),
                $"Currency mismatch: Billable activity currency '{activity.Currency}' does not match rate card agreement currency '{schedule.Currency}'.");
        }

        // Evaluate Volume Tier Calculation (Graduated, AllUnits, or FlatRate)
        var ratingResult = line.CalculateRating(activity.Quantity);

        // Apply Immutable Pricing Snapshot
        activity.Rate(
            unitPrice: ratingResult.UnitPrice,
            totalAmount: ratingResult.TotalAmount,
            currency: schedule.Currency,
            rateCardId: schedule.RateCardId,
            rateCardTierId: ratingResult.MatchedTierId,
            rateCardVersion: schedule.Version,
            ratedByUserId: ratedByUserId,
            ratedAtUtc: DateTime.UtcNow,
            rateCardScheduleId: schedule.Id);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Successfully rated Activity '{ActivityId}' ({ActivityType}, Qty: {Qty}) -> UnitPrice: {UnitPrice:F4}, Total: {Total:F2} {Currency} via Schedule v{Version}.",
            activity.Id, activity.ActivityType, activity.Quantity, activity.UnitPrice, activity.TotalAmount, activity.Currency, schedule.Version);

        return activity;
    }

    /// <inheritdoc />
    public async Task<RatingBatchResult> RateActivitiesBatchAsync(
        Guid clientId,
        Guid? facilityId = null,
        Guid? billingPeriodId = null,
        Guid? ratedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        var query = _context.BillableActivities
            .Where(a => a.ClientId == clientId && a.Status == BillableActivityStatus.PendingRating);

        if (facilityId.HasValue)
        {
            query = query.Where(a => a.FacilityId == facilityId.Value);
        }

        if (billingPeriodId.HasValue)
        {
            query = query.Where(a => a.BillingPeriodId == billingPeriodId.Value);
        }

        var pendingActivities = await query
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        var result = new RatingBatchResult
        {
            TotalActivitiesFound = pendingActivities.Count
        };

        if (pendingActivities.Count == 0)
        {
            return result;
        }

        foreach (var activity in pendingActivities)
        {
            // Idempotent double-check
            if (activity.Status != BillableActivityStatus.PendingRating)
            {
                result.SkippedCount++;
                continue;
            }

            var (schedule, line) = await _rateCardService.GetEffectiveRateLineAsync(
                activity.ClientId,
                activity.FacilityId,
                activity.ActivityType,
                activity.OccurredAtUtc,
                activity.Currency,
                cancellationToken);

            if (schedule == null || line == null)
            {
                result.MissingRateCount++;
                result.MissingRateDetails.Add(new MissingRateDetail
                {
                    ActivityId = activity.Id,
                    ActivityType = activity.ActivityType.ToString(),
                    FacilityId = activity.FacilityId,
                    OccurredAtUtc = activity.OccurredAtUtc,
                    Reason = $"No effective commercial rate found for {activity.ActivityType} at facility {activity.FacilityId} on {activity.OccurredAtUtc:yyyy-MM-dd}."
                });
                continue;
            }

            if (!string.Equals(schedule.Currency, activity.Currency, StringComparison.OrdinalIgnoreCase))
            {
                result.MissingRateCount++;
                result.MissingRateDetails.Add(new MissingRateDetail
                {
                    ActivityId = activity.Id,
                    ActivityType = activity.ActivityType.ToString(),
                    FacilityId = activity.FacilityId,
                    OccurredAtUtc = activity.OccurredAtUtc,
                    Reason = $"Currency mismatch: Activity currency '{activity.Currency}' vs RateCard currency '{schedule.Currency}'."
                });
                continue;
            }

            var ratingResult = line.CalculateRating(activity.Quantity);

            activity.Rate(
                unitPrice: ratingResult.UnitPrice,
                totalAmount: ratingResult.TotalAmount,
                currency: schedule.Currency,
                rateCardId: schedule.RateCardId,
                rateCardTierId: ratingResult.MatchedTierId,
                rateCardVersion: schedule.Version,
                ratedByUserId: ratedByUserId,
                ratedAtUtc: DateTime.UtcNow,
                rateCardScheduleId: schedule.Id);

            result.SuccessfullyRatedCount++;
            result.TotalRatedAmount += ratingResult.TotalAmount;
            result.RatedActivityIds.Add(activity.Id);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Batch rating complete for Client '{ClientId}'. Found: {Found}, Rated: {Rated}, Missing: {Missing}, Total: {Total:F2}.",
            clientId, result.TotalActivitiesFound, result.SuccessfullyRatedCount, result.MissingRateCount, result.TotalRatedAmount);

        return result;
    }

    private void ValidateTenantAccess(Guid clientId)
    {
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != clientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant commercial rating operation.");
        }
    }
}

