using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Service contract for managing 3PL commercial rate cards, versioned schedules, pricing lines, and volume tiers.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public interface IRateCardService
{
    /// <summary>
    /// Creates a new commercial agreement rate card for a client.
    /// </summary>
    Task<RateCard> CreateRateCardAsync(
        Guid clientId,
        string rateCardCode,
        string name,
        string currency,
        Guid? defaultFacilityId = null,
        string? description = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a versioned, effective-dated commercial schedule under an existing rate card.
    /// Validates non-overlapping active schedules for the same client and facility scope.
    /// </summary>
    Task<RateCardSchedule> AddScheduleAsync(
        Guid rateCardId,
        Guid? facilityId,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        decimal? minimumMonthlySpend = null,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a billable activity pricing line with optional volume tiers to a schedule.
    /// </summary>
    Task<RateCardLine> AddLineAsync(
        Guid scheduleId,
        BillableActivityType activityType,
        string unitOfMeasure,
        RateModelType rateModel,
        PricingTierCalculationModel tierModel,
        decimal baseRate,
        decimal? minimumCharge = null,
        IEnumerable<(int TierIndex, decimal MinQuantity, decimal? MaxQuantity, decimal UnitPrice)>? tiers = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates a rate card for live commercial operations.
    /// </summary>
    Task<RateCard> ActivateRateCardAsync(Guid rateCardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates a schedule, ensuring no active overlap exists for the client/facility scope.
    /// </summary>
    Task<RateCardSchedule> ActivateScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the effective schedule for a client and facility at a specific UTC timestamp.
    /// Precedence: Facility-specific override schedule -> Client-wide default schedule -> null.
    /// </summary>
    Task<RateCardSchedule?> GetEffectiveScheduleAsync(
        Guid clientId,
        Guid? facilityId,
        DateTime occurredAtUtc,
        string currency = "USD",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the applicable pricing line and schedule for an activity based on facility precedence:
    /// 1. Line on exact facility-specific schedule.
    /// 2. Line on client-wide default schedule.
    /// 3. Returns null if no matching active schedule or line exists.
    /// </summary>
    Task<(RateCardSchedule? Schedule, RateCardLine? Line)> GetEffectiveRateLineAsync(
        Guid clientId,
        Guid? facilityId,
        BillableActivityType activityType,
        DateTime occurredAtUtc,
        string currency = "USD",
        CancellationToken cancellationToken = default);
}

