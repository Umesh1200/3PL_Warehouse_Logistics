using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Authoritative implementation of commercial RateCard management, effective dating, schedule overlap enforcement,
/// and facility override precedence resolution.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public class RateCardService : IRateCardService
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<RateCardService> _logger;

    public RateCardService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ILogger<RateCardService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RateCard> CreateRateCardAsync(
        Guid clientId,
        string rateCardCode,
        string name,
        string currency,
        Guid? defaultFacilityId = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        if (string.IsNullOrWhiteSpace(rateCardCode))
        {
            throw new ValidationException(nameof(rateCardCode), "Rate card code is required.");
        }

        var normalizedCode = rateCardCode.Trim().ToUpperInvariant();

        var codeExists = await _context.RateCards
            .AnyAsync(rc => rc.ClientId == clientId && rc.RateCardCode == normalizedCode, cancellationToken);

        if (codeExists)
        {
            throw new InvalidDomainStateException($"A rate card with code '{normalizedCode}' already exists for client '{clientId}'.");
        }

        var rateCard = RateCard.Create(clientId, normalizedCode, name, currency, defaultFacilityId, description);

        _context.RateCards.Add(rateCard);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created RateCard '{Code}' (ID: {Id}) for Client '{ClientId}'.",
            rateCard.RateCardCode, rateCard.Id, clientId);

        return rateCard;
    }

    /// <inheritdoc />
    public async Task<RateCardSchedule> AddScheduleAsync(
        Guid rateCardId,
        Guid? facilityId,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        decimal? minimumMonthlySpend = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var rateCard = await _context.RateCards
            .Include(rc => rc.Schedules)
            .FirstOrDefaultAsync(rc => rc.Id == rateCardId, cancellationToken);

        if (rateCard == null)
        {
            throw new EntityNotFoundException(nameof(RateCard), rateCardId);
        }

        ValidateTenantAccess(rateCard.ClientId);

        // Check for active schedule overlaps for the exact client and facility scope across the database
        var overlappingActive = await _context.RateCardSchedules
            .Where(s =>
                s.ClientId == rateCard.ClientId &&
                s.FacilityId == facilityId &&
                s.Status == RateCardStatus.Active &&
                s.Currency == rateCard.Currency)
            .ToListAsync(cancellationToken);

        var queryEnd = effectiveToUtc ?? DateTime.MaxValue;
        var hasOverlap = overlappingActive.Any(s =>
        {
            var sEnd = s.EffectiveToUtc ?? DateTime.MaxValue;
            return s.EffectiveFromUtc <= queryEnd && sEnd >= effectiveFromUtc;
        });

        if (hasOverlap)
        {
            throw new InvalidDomainStateException(
                $"Cannot add schedule: date interval [{effectiveFromUtc:yyyy-MM-dd} to {effectiveToUtc:yyyy-MM-dd}] " +
                $"overlaps with an existing active schedule for facility scope '{(facilityId.HasValue ? facilityId.Value.ToString() : "Client-Wide")}'.");
        }

        var schedule = rateCard.AddSchedule(facilityId, effectiveFromUtc, effectiveToUtc, minimumMonthlySpend, notes);

        _context.RateCardSchedules.Add(schedule);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added schedule v{Version} (ID: {Id}) to RateCard '{Code}'.",
            schedule.Version, schedule.Id, rateCard.RateCardCode);

        return schedule;
    }

    /// <inheritdoc />
    public async Task<RateCardLine> AddLineAsync(
        Guid scheduleId,
        BillableActivityType activityType,
        string unitOfMeasure,
        RateModelType rateModel,
        PricingTierCalculationModel tierModel,
        decimal baseRate,
        decimal? minimumCharge = null,
        IEnumerable<(int TierIndex, decimal MinQuantity, decimal? MaxQuantity, decimal UnitPrice)>? tiers = null,
        CancellationToken cancellationToken = default)
    {
        var schedule = await _context.RateCardSchedules
            .Include(s => s.Lines)
                .ThenInclude(l => l.Tiers)
            .FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken);

        if (schedule == null)
        {
            throw new EntityNotFoundException(nameof(RateCardSchedule), scheduleId);
        }

        ValidateTenantAccess(schedule.ClientId);

        var line = schedule.AddLine(activityType, unitOfMeasure, rateModel, tierModel, baseRate, minimumCharge);

        if (tiers != null)
        {
            foreach (var t in tiers)
            {
                line.AddTier(t.TierIndex, t.MinQuantity, t.MaxQuantity, t.UnitPrice);
            }
        }

        _context.RateCardLines.Add(line);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added rate line for ActivityType '{ActivityType}' to Schedule '{ScheduleId}'.",
            activityType, scheduleId);

        return line;
    }

    /// <inheritdoc />
    public async Task<RateCard> ActivateRateCardAsync(Guid rateCardId, CancellationToken cancellationToken = default)
    {
        var rateCard = await _context.RateCards.FirstOrDefaultAsync(rc => rc.Id == rateCardId, cancellationToken);
        if (rateCard == null)
        {
            throw new EntityNotFoundException(nameof(RateCard), rateCardId);
        }

        ValidateTenantAccess(rateCard.ClientId);
        rateCard.Activate();

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Activated RateCard '{Code}' (ID: {Id}).", rateCard.RateCardCode, rateCard.Id);

        return rateCard;
    }

    /// <inheritdoc />
    public async Task<RateCardSchedule> ActivateScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await _context.RateCardSchedules
            .Include(s => s.RateCard)
            .FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken);

        if (schedule == null)
        {
            throw new EntityNotFoundException(nameof(RateCardSchedule), scheduleId);
        }

        ValidateTenantAccess(schedule.ClientId);

        // Verify no overlapping active schedule exists
        var queryEnd = schedule.EffectiveToUtc ?? DateTime.MaxValue;
        var existingOverlaps = await _context.RateCardSchedules
            .Where(s =>
                s.Id != scheduleId &&
                s.ClientId == schedule.ClientId &&
                s.FacilityId == schedule.FacilityId &&
                s.Status == RateCardStatus.Active &&
                s.Currency == schedule.Currency)
            .ToListAsync(cancellationToken);

        var hasOverlap = existingOverlaps.Any(s =>
        {
            var sEnd = s.EffectiveToUtc ?? DateTime.MaxValue;
            return s.EffectiveFromUtc <= queryEnd && sEnd >= schedule.EffectiveFromUtc;
        });

        if (hasOverlap)
        {
            throw new InvalidDomainStateException(
                $"Cannot activate schedule v{schedule.Version}: overlaps with an already active schedule for the same scope.");
        }

        schedule.Activate();
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Activated Schedule v{Version} (ID: {Id}).", schedule.Version, schedule.Id);
        return schedule;
    }

    /// <inheritdoc />
    public async Task<RateCardSchedule?> GetEffectiveScheduleAsync(
        Guid clientId,
        Guid? facilityId,
        DateTime occurredAtUtc,
        string currency = "USD",
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();

        // Load candidate active schedules for the client and currency
        var activeSchedules = await _context.RateCardSchedules
            .Include(s => s.RateCard)
            .Include(s => s.Lines)
                .ThenInclude(l => l.Tiers)
            .Where(s =>
                s.ClientId == clientId &&
                s.Status == RateCardStatus.Active &&
                s.Currency == normalizedCurrency &&
                s.EffectiveFromUtc <= occurredAtUtc &&
                (!s.EffectiveToUtc.HasValue || s.EffectiveToUtc.Value >= occurredAtUtc))
            .OrderByDescending(s => s.Version)
            .ToListAsync(cancellationToken);

        // 1. Facility-specific override
        if (facilityId.HasValue)
        {
            var facilitySchedule = activeSchedules.FirstOrDefault(s => s.FacilityId == facilityId.Value);
            if (facilitySchedule != null)
            {
                return facilitySchedule;
            }
        }

        // 2. Client-wide fallback
        var clientWideSchedule = activeSchedules.FirstOrDefault(s => s.FacilityId == null);
        return clientWideSchedule;
    }

    /// <inheritdoc />
    public async Task<(RateCardSchedule? Schedule, RateCardLine? Line)> GetEffectiveRateLineAsync(
        Guid clientId,
        Guid? facilityId,
        BillableActivityType activityType,
        DateTime occurredAtUtc,
        string currency = "USD",
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();

        var activeSchedules = await _context.RateCardSchedules
            .Include(s => s.RateCard)
            .Include(s => s.Lines)
                .ThenInclude(l => l.Tiers)
            .Where(s =>
                s.ClientId == clientId &&
                s.Status == RateCardStatus.Active &&
                s.Currency == normalizedCurrency &&
                s.EffectiveFromUtc <= occurredAtUtc &&
                (!s.EffectiveToUtc.HasValue || s.EffectiveToUtc.Value >= occurredAtUtc))
            .OrderByDescending(s => s.Version)
            .ToListAsync(cancellationToken);

        // Level 1: Exact client + facility + activity match
        if (facilityId.HasValue)
        {
            var facilitySchedule = activeSchedules.FirstOrDefault(s => s.FacilityId == facilityId.Value);
            if (facilitySchedule != null)
            {
                var facilityLine = facilitySchedule.FindLine(activityType);
                if (facilityLine != null)
                {
                    return (facilitySchedule, facilityLine);
                }
            }
        }

        // Level 2: Client-wide activity rate fallback
        var clientWideSchedule = activeSchedules.FirstOrDefault(s => s.FacilityId == null);
        if (clientWideSchedule != null)
        {
            var clientLine = clientWideSchedule.FindLine(activityType);
            if (clientLine != null)
            {
                return (clientWideSchedule, clientLine);
            }
        }

        // Level 3: Missing rate configuration
        return (null, null);
    }

    private void ValidateTenantAccess(Guid clientId)
    {
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != clientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant commercial rate card operation.");
        }
    }
}

