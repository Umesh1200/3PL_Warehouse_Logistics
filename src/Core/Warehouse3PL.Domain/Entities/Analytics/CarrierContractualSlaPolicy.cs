using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Versioned contractual Service Level Agreement (SLA) policy defining transit commitments for a carrier.
/// Supports lane-specific, regional, and client-specific overrides with effective dating.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public class CarrierContractualSlaPolicy : AuditableEntity<Guid>
{
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;

    /// <summary>
    /// Optional client ID for client-specific contractual SLA rate and transit agreements.
    /// If null, this policy serves as the default facility-wide agreement for the carrier.
    /// </summary>
    public Guid? ClientId { get; set; }

    /// <summary>
    /// Optional origin facility ID for facility-specific pickup and fulfillment commitments.
    /// </summary>
    public Guid? OriginFacilityId { get; set; }
    public string OriginRegion { get; set; } = string.Empty;

    // Destination Geographic Scope
    public string DestinationCountry { get; set; } = string.Empty;
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.PendingNormalization;
    public string DestinationStateOrRegion { get; set; } = string.Empty;
    public string DestinationPostalCodePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Contractual transit commitment in days.
    /// </summary>
    public int TransitCommitmentDays { get; set; }

    /// <summary>
    /// Specifies whether transit duration is counted across calendar days or business days.
    /// </summary>
    public TransitDayCalculationType CalculationType { get; set; } = TransitDayCalculationType.BusinessDays;

    /// <summary>
    /// Daily carrier tender/dispatch cutoff time in local warehouse/cutoff timezone.
    /// Shipments dispatched after this cutoff time roll their transit clock to the next operating day.
    /// </summary>
    public TimeSpan DailyCutoffTime { get; set; } = new TimeSpan(17, 0, 0); // 5:00 PM local
    public string CutoffTimeZone { get; set; } = "UTC";

    // Effective Dating & Versioning
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public string SlaPolicyVersion { get; set; } = "1.0.0";
    public bool IsActive { get; set; } = true;
    public string Notes { get; set; } = string.Empty;

    public CarrierContractualSlaPolicy()
    {
        Id = Guid.NewGuid();
    }

    public static CarrierContractualSlaPolicy Create(
        string carrierCode,
        CarrierServiceLevel serviceLevel,
        int transitCommitmentDays,
        string slaPolicyVersion = "1.0.0",
        TransitDayCalculationType calculationType = TransitDayCalculationType.BusinessDays,
        Guid? clientId = null,
        Guid? originFacilityId = null,
        string originRegion = "",
        string destinationCountry = "",
        string destinationStateOrRegion = "",
        string destinationPostalCodePrefix = "",
        TimeSpan? dailyCutoffTime = null,
        string cutoffTimeZone = "UTC",
        DateTime? effectiveFromUtc = null,
        DateTime? effectiveToUtc = null)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
            throw new ValidationException(nameof(carrierCode), "Carrier code is required.");
        if (transitCommitmentDays <= 0)
            throw new ValidationException(nameof(transitCommitmentDays), "Transit commitment days must be greater than zero.");
        if (string.IsNullOrWhiteSpace(slaPolicyVersion))
            throw new ValidationException(nameof(slaPolicyVersion), "SLA policy version is required.");

        var fromUtc = effectiveFromUtc ?? DateTime.UtcNow.Date;

        return new CarrierContractualSlaPolicy
        {
            Id = Guid.NewGuid(),
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            CarrierServiceLevel = serviceLevel,
            TransitCommitmentDays = transitCommitmentDays,
            SlaPolicyVersion = slaPolicyVersion.Trim(),
            CalculationType = calculationType,
            ClientId = clientId,
            OriginFacilityId = originFacilityId,
            OriginRegion = originRegion?.Trim().ToUpperInvariant() ?? string.Empty,
            DestinationCountry = string.IsNullOrWhiteSpace(destinationCountry) ? string.Empty : destinationCountry.Trim().ToUpperInvariant(),
            CountryNormalizationStatus = CountryNormalizationStatus.PendingNormalization,
            DestinationStateOrRegion = destinationStateOrRegion?.Trim().ToUpperInvariant() ?? string.Empty,
            DestinationPostalCodePrefix = destinationPostalCodePrefix?.Trim() ?? string.Empty,
            DailyCutoffTime = dailyCutoffTime ?? new TimeSpan(17, 0, 0),
            CutoffTimeZone = string.IsNullOrWhiteSpace(cutoffTimeZone) ? "UTC" : cutoffTimeZone.Trim(),
            EffectiveFromUtc = fromUtc.Kind == DateTimeKind.Utc ? fromUtc : DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
            EffectiveToUtc = effectiveToUtc.HasValue ? (effectiveToUtc.Value.Kind == DateTimeKind.Utc ? effectiveToUtc : DateTime.SpecifyKind(effectiveToUtc.Value, DateTimeKind.Utc)) : null,
            IsActive = true
        };
    }

    /// <summary>
    /// Calculates the committed contractual Estimated Delivery Date (EDD) given an operational dispatch timestamp.
    /// Accounts for daily cutoff time and business vs calendar day rules.
    /// </summary>
    public DateTime CalculateCommittedDeliveryDate(DateTime dispatchUtc)
    {
        var normalizedDispatch = dispatchUtc.Kind == DateTimeKind.Utc ? dispatchUtc : DateTime.SpecifyKind(dispatchUtc, DateTimeKind.Utc);
        var effectiveDispatchDate = normalizedDispatch.TimeOfDay > DailyCutoffTime
            ? normalizedDispatch.Date.AddDays(1)
            : normalizedDispatch.Date;

        if (CalculationType == TransitDayCalculationType.CalendarDays)
        {
            return effectiveDispatchDate.AddDays(TransitCommitmentDays).Date.AddHours(23).AddMinutes(59).AddSeconds(59);
        }

        // Business days calculation (skipping Saturday and Sunday)
        var currentDate = effectiveDispatchDate;
        // If effective dispatch falls on weekend, advance to next Monday
        while (currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday)
        {
            currentDate = currentDate.AddDays(1);
        }

        int remainingDays = TransitCommitmentDays;
        while (remainingDays > 0)
        {
            currentDate = currentDate.AddDays(1);
            if (currentDate.DayOfWeek != DayOfWeek.Saturday && currentDate.DayOfWeek != DayOfWeek.Sunday)
            {
                remainingDays--;
            }
        }

        return currentDate.Date.AddHours(23).AddMinutes(59).AddSeconds(59);
    }
}
