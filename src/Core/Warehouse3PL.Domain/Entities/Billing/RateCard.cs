using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Root commercial agreement aggregate for a 3PL Client.
/// Encompasses contract code, billing currency, default facility scope, and versioned/effective-dated schedules.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public class RateCard : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public string RateCardCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Currency { get; set; } = "USD";
    public RateCardStatus Status { get; set; } = RateCardStatus.Draft;
    public Guid? DefaultFacilityId { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility? DefaultFacility { get; set; }
    public ICollection<RateCardSchedule> Schedules { get; set; } = new List<RateCardSchedule>();

    public RateCard()
    {
        Id = Guid.NewGuid();
    }

    public static RateCard Create(
        Guid clientId,
        string rateCardCode,
        string name,
        string currency,
        Guid? defaultFacilityId = null,
        string? description = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required for RateCard.");
        }

        if (string.IsNullOrWhiteSpace(rateCardCode))
        {
            throw new ValidationException(nameof(rateCardCode), "Rate card code is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(nameof(name), "Rate card name is required.");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3)
        {
            throw new ValidationException(nameof(currency), "Currency code must be a 3-character ISO 4217 code.");
        }

        return new RateCard
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            RateCardCode = rateCardCode.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Currency = normalizedCurrency,
            Status = RateCardStatus.Draft,
            DefaultFacilityId = defaultFacilityId,
            Description = description?.Trim()
        };
    }

    /// <summary>
    /// Adds a new effective-dated commercial schedule under this rate card agreement.
    /// Validates that active schedules for the same facility/client scope do not overlap.
    /// </summary>
    public RateCardSchedule AddSchedule(
        Guid? facilityId,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        decimal? minimumMonthlySpend = null,
        string? notes = null)
    {
        // Enforce that new schedule does not overlap with existing Active schedules for the same facility scope
        var overlapping = Schedules.Where(s =>
            s.Status == RateCardStatus.Active &&
            s.FacilityId == facilityId &&
            s.OverlapsWith(effectiveFromUtc, effectiveToUtc)).ToList();

        if (overlapping.Count > 0)
        {
            throw new InvalidDomainStateException(
                $"Cannot create schedule: overlaps with existing active schedule v{overlapping[0].Version} " +
                $"({overlapping[0].EffectiveFromUtc:yyyy-MM-dd} to {overlapping[0].EffectiveToUtc:yyyy-MM-dd}) for facility scope '{facilityId}'.");
        }

        var nextVersion = Schedules.Count > 0 ? Schedules.Max(s => s.Version) + 1 : 1;

        var schedule = RateCardSchedule.Create(
            ClientId,
            Id,
            facilityId,
            nextVersion,
            effectiveFromUtc,
            effectiveToUtc,
            Currency,
            minimumMonthlySpend,
            notes);

        Schedules.Add(schedule);
        return schedule;
    }

    /// <summary>
    /// Resolves the effective schedule for an operational activity based on facility precedence:
    /// 1. Facility-specific schedule (FacilityId == facilityId) active at the timestamp.
    /// 2. Client-wide fallback schedule (FacilityId == null) active at the timestamp.
    /// Returns null if neither exists.
    /// </summary>
    public RateCardSchedule? GetEffectiveSchedule(Guid? facilityId, DateTime utcTimestamp)
    {
        if (Status != RateCardStatus.Active)
        {
            return null;
        }

        // 1. Check exact facility match if a facility was specified
        if (facilityId.HasValue)
        {
            var facilitySchedule = Schedules.FirstOrDefault(s =>
                s.FacilityId == facilityId.Value &&
                s.IsEffectiveAt(utcTimestamp));

            if (facilitySchedule != null)
            {
                return facilitySchedule;
            }
        }

        // 2. Fall back to client-wide schedule (FacilityId == null)
        var clientWideSchedule = Schedules.FirstOrDefault(s =>
            s.FacilityId == null &&
            s.IsEffectiveAt(utcTimestamp));

        return clientWideSchedule;
    }

    /// <summary>
    /// Activates this rate card agreement for live commercial rating.
    /// </summary>
    public void Activate()
    {
        if (Status == RateCardStatus.Archived)
        {
            throw new InvalidDomainStateException("Cannot activate an archived rate card.");
        }

        Status = RateCardStatus.Active;
    }

    /// <summary>
    /// Marks the rate card as superceded by a newer commercial agreement.
    /// </summary>
    public void Supercede()
    {
        Status = RateCardStatus.Superceded;
    }

    /// <summary>
    /// Archives the rate card for historical retention and compliance.
    /// </summary>
    public void Archive()
    {
        Status = RateCardStatus.Archived;
    }
}

