using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Effective-dated commercial schedule under a Client RateCard agreement.
/// Supports facility-specific overrides (FacilityId != null) vs client-wide defaults (FacilityId == null),
/// version progression, minimum monthly spend commitments, and contractual line items.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public class RateCardSchedule : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid RateCardId { get; set; }
    public Guid? FacilityId { get; set; }

    /// <summary>
    /// Sequential version number for this schedule scope (1, 2, 3...).
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// UTC timestamp from which this schedule is contractually effective (inclusive).
    /// </summary>
    public DateTime EffectiveFromUtc { get; set; }

    /// <summary>
    /// UTC timestamp until which this schedule is contractually effective (inclusive).
    /// If null, the schedule is currently open-ended.
    /// </summary>
    public DateTime? EffectiveToUtc { get; set; }

    public RateCardStatus Status { get; set; } = RateCardStatus.Draft;

    /// <summary>
    /// Contractual minimum monthly commercial spend commitment, if agreed.
    /// </summary>
    public decimal? MinimumMonthlySpend { get; set; }

    public string Currency { get; set; } = "USD";
    public string? Notes { get; set; }

    // Navigations
    public RateCard RateCard { get; set; } = null!;
    public Facility? Facility { get; set; }
    public ICollection<RateCardLine> Lines { get; set; } = new List<RateCardLine>();

    public RateCardSchedule()
    {
        Id = Guid.NewGuid();
    }

    public static RateCardSchedule Create(
        Guid clientId,
        Guid rateCardId,
        Guid? facilityId,
        int version,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        string currency,
        decimal? minimumMonthlySpend = null,
        string? notes = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required for rate card schedule.");
        }

        if (rateCardId == Guid.Empty)
        {
            throw new ValidationException(nameof(rateCardId), "Rate card ID is required.");
        }

        if (version <= 0)
        {
            throw new ValidationException(nameof(version), "Schedule version must be a positive integer.");
        }

        if (effectiveToUtc.HasValue && effectiveToUtc.Value < effectiveFromUtc)
        {
            throw new ValidationException(nameof(effectiveToUtc), $"EffectiveToUtc ({effectiveToUtc.Value:O}) cannot precede EffectiveFromUtc ({effectiveFromUtc:O}).");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3)
        {
            throw new ValidationException(nameof(currency), "Currency code must be a 3-character ISO 4217 code.");
        }

        if (minimumMonthlySpend.HasValue && minimumMonthlySpend.Value < 0)
        {
            throw new ValidationException(nameof(minimumMonthlySpend), "Minimum monthly spend cannot be negative.");
        }

        return new RateCardSchedule
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            RateCardId = rateCardId,
            FacilityId = facilityId,
            Version = version,
            EffectiveFromUtc = effectiveFromUtc,
            EffectiveToUtc = effectiveToUtc,
            Status = RateCardStatus.Draft,
            Currency = normalizedCurrency,
            MinimumMonthlySpend = minimumMonthlySpend.HasValue ? decimal.Round(minimumMonthlySpend.Value, 4, MidpointRounding.AwayFromZero) : null,
            Notes = notes?.Trim()
        };
    }

    /// <summary>
    /// Determines whether this schedule is active and effective for a given UTC timestamp.
    /// </summary>
    public bool IsEffectiveAt(DateTime utcTimestamp)
    {
        if (Status != RateCardStatus.Active)
        {
            return false;
        }

        if (utcTimestamp < EffectiveFromUtc)
        {
            return false;
        }

        if (EffectiveToUtc.HasValue && utcTimestamp > EffectiveToUtc.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks whether this schedule overlaps with the specified date interval.
    /// </summary>
    public bool OverlapsWith(DateTime fromUtc, DateTime? toUtc)
    {
        var scheduleEnd = EffectiveToUtc ?? DateTime.MaxValue;
        var queryEnd = toUtc ?? DateTime.MaxValue;

        return EffectiveFromUtc <= queryEnd && scheduleEnd >= fromUtc;
    }

    /// <summary>
    /// Adds a billable activity pricing line to this schedule.
    /// </summary>
    public RateCardLine AddLine(
        BillableActivityType activityType,
        string unitOfMeasure,
        RateModelType rateModel,
        PricingTierCalculationModel tierModel,
        decimal baseRate,
        decimal? minimumCharge = null)
    {
        if (Lines.Any(l => l.ActivityType == activityType))
        {
            throw new InvalidDomainStateException($"A rate line for activity type '{activityType}' already exists in schedule v{Version}.");
        }

        var line = RateCardLine.Create(ClientId, Id, activityType, unitOfMeasure, rateModel, tierModel, baseRate, minimumCharge);
        Lines.Add(line);
        return line;
    }

    /// <summary>
    /// Finds the rate line for the requested activity type.
    /// </summary>
    public RateCardLine? FindLine(BillableActivityType activityType)
    {
        return Lines.FirstOrDefault(l => l.ActivityType == activityType);
    }

    /// <summary>
    /// Activates this schedule for rating operations.
    /// </summary>
    public void Activate()
    {
        if (Status == RateCardStatus.Archived)
        {
            throw new InvalidDomainStateException("Cannot activate an archived rate card schedule.");
        }

        Status = RateCardStatus.Active;
    }

    /// <summary>
    /// Expires this schedule, setting EffectiveToUtc to the designated expiration timestamp.
    /// </summary>
    public void Expire(DateTime expiredAtUtc)
    {
        if (expiredAtUtc < EffectiveFromUtc)
        {
            throw new ValidationException(nameof(expiredAtUtc), "Expiration date cannot precede EffectiveFromUtc.");
        }

        EffectiveToUtc = expiredAtUtc;
    }

    /// <summary>
    /// Marks the schedule as superceded by a newer revision.
    /// </summary>
    public void Supercede(DateTime? supercededAtUtc = null)
    {
        if (supercededAtUtc.HasValue && supercededAtUtc.Value >= EffectiveFromUtc)
        {
            EffectiveToUtc = supercededAtUtc.Value;
        }

        Status = RateCardStatus.Superceded;
    }

    /// <summary>
    /// Archives the schedule for historical retention.
    /// </summary>
    public void Archive()
    {
        Status = RateCardStatus.Archived;
    }
}

