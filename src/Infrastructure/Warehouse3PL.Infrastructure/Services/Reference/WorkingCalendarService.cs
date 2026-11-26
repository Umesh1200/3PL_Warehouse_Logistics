using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Reference;

public sealed class WorkingCalendarService : IWorkingCalendarService
{
    private readonly IApplicationDbContext _db;
    private readonly IConfigurationConsistencyService? _consistencyService;

    public WorkingCalendarService(
        IApplicationDbContext db,
        IConfigurationConsistencyService? consistencyService = null)
    {
        _db = db;
        _consistencyService = consistencyService;
    }

    public async Task<WorkingDayEvaluationResult> EvaluateWorkingDayAsync(
        Guid facilityId,
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        var facility = await _db.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == facilityId, cancellationToken)
            ?? throw new EntityNotFoundException("Facility", facilityId);

        var tz = ResolveTimeZone(facility.Timezone);
        var localMidday = localDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var targetUtc = LocalToUtcSafe(localMidday, tz);

        var activeFacilityCals = _consistencyService != null
            ? await _consistencyService.GetOrLoadVerifiedAsync(
                ConfigurationScopeType.Facility,
                facilityId.ToString("D"),
                "facility-calendars",
                (db, ct) => db.FacilityCalendars
                    .AsNoTracking()
                    .Where(fc => fc.FacilityId == facilityId && fc.IsActive)
                    .OrderByDescending(fc => fc.EffectiveFromUtc)
                    .ToListAsync(ct),
                facilityId: facilityId,
                cancellationToken: cancellationToken)
            : await _db.FacilityCalendars
                .AsNoTracking()
                .Where(fc => fc.FacilityId == facilityId && fc.IsActive)
                .OrderByDescending(fc => fc.EffectiveFromUtc)
                .ToListAsync(cancellationToken);

        var activeCal = activeFacilityCals
            .Where(fc => fc.EffectiveFromUtc <= targetUtc &&
                         (fc.EffectiveToUtc == null || targetUtc < fc.EffectiveToUtc))
            .OrderByDescending(fc => fc.EffectiveFromUtc)
            .FirstOrDefault();

        var restDays = ResolveWeeklyRestDays(activeCal, facility.CountryCode);

        var normalizedCountry = NormalizeIsoCountryFallback(facility.CountryCode);
        var facilitySub = await ResolveSubdivisionCodeAsync(normalizedCountry, facility.StateOrProvince, cancellationToken);

        IReadOnlyList<HolidayCalendar> holidays;
        if (_consistencyService != null)
        {
            var facHolidays = await _consistencyService.GetOrLoadVerifiedAsync(
                ConfigurationScopeType.Facility,
                facilityId.ToString("D"),
                "facility-holidays",
                (db, ct) => db.HolidayCalendars
                    .AsNoTracking()
                    .Where(h => h.IsActive && h.FacilityId == facilityId)
                    .ToListAsync(ct),
                facilityId: facilityId,
                cancellationToken: cancellationToken);

            var countryHolidays = await _consistencyService.GetOrLoadVerifiedAsync(
                ConfigurationScopeType.Country,
                normalizedCountry,
                "country-holidays",
                (db, ct) => db.HolidayCalendars
                    .AsNoTracking()
                    .Where(h => h.IsActive && h.CountryCode == normalizedCountry && h.FacilityId == null)
                    .ToListAsync(ct),
                cancellationToken: cancellationToken);

            holidays = facHolidays
                .Concat(countryHolidays.Where(h =>
                    h.HolidayScope == HolidayScope.National ||
                    (h.HolidayScope == HolidayScope.Regional && facilitySub != null && h.SubdivisionCode == facilitySub)))
                .Where(h => h.HolidayDate == localDate)
                .ToList();
        }
        else
        {
            holidays = await _db.HolidayCalendars
                .AsNoTracking()
                .Where(h => h.IsActive &&
                            h.HolidayDate == localDate &&
                            (h.FacilityId == facilityId ||
                             (h.CountryCode == normalizedCountry &&
                              (h.HolidayScope == HolidayScope.National ||
                               (h.HolidayScope == HolidayScope.Regional && facilitySub != null && h.SubdivisionCode == facilitySub)))))
                .ToListAsync(cancellationToken);
        }

        return EvaluateLocalDate(
            facilityId,
            tz.Id,
            localDate,
            restDays,
            activeCal?.Id,
            holidays);
    }

    public async Task<DateTime> CalculateCommittedDeliveryDateUtcAsync(
        CarrierContractualSlaPolicy policy,
        DateTime dispatchUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedDispatch = NormalizeUtcStrict(dispatchUtc);

        string tzId = string.IsNullOrWhiteSpace(policy.CutoffTimeZone) ? "UTC" : policy.CutoffTimeZone.Trim();
        if (!string.Equals(tzId, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            // Fail closed early if policy CutoffTimeZone is configured to an invalid ID
            _ = ResolveTimeZone(tzId);
        }

        string destinationCountry = NormalizeIsoCountryFallback(policy.DestinationCountry);
        string? destinationSubdivision = await ResolveSubdivisionCodeAsync(
            destinationCountry,
            policy.DestinationStateOrRegion,
            cancellationToken);

        string? originCountry = null;
        string? originSubdivision = null;
        IReadOnlyList<FacilityCalendar> originFacilityCalendars = [];

        if (policy.OriginFacilityId.HasValue && policy.OriginFacilityId.Value != Guid.Empty)
        {
            var originFacility = await _db.Facilities
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == policy.OriginFacilityId.Value, cancellationToken);

            if (originFacility != null)
            {
                // Validate configured facility timezone strictly if set
                if (!string.IsNullOrWhiteSpace(originFacility.Timezone))
                {
                    _ = ResolveTimeZone(originFacility.Timezone);
                }

                if (string.IsNullOrWhiteSpace(policy.CutoffTimeZone) ||
                    string.Equals(policy.CutoffTimeZone, "UTC", StringComparison.OrdinalIgnoreCase))
                {
                    tzId = string.IsNullOrWhiteSpace(originFacility.Timezone) ? "UTC" : originFacility.Timezone.Trim();
                }

                originCountry = NormalizeIsoCountryFallback(originFacility.CountryCode);
                originSubdivision = await ResolveSubdivisionCodeAsync(
                    originCountry,
                    originFacility.StateOrProvince,
                    cancellationToken);

                originFacilityCalendars = await _db.FacilityCalendars
                    .AsNoTracking()
                    .Where(fc => fc.FacilityId == originFacility.Id && fc.IsActive)
                    .OrderByDescending(fc => fc.EffectiveFromUtc)
                    .ToListAsync(cancellationToken);
            }
        }

        var tz = ResolveTimeZone(tzId);
        var localDispatch = TimeZoneInfo.ConvertTimeFromUtc(normalizedDispatch, tz);
        var effectiveLocalDate = localDispatch.TimeOfDay > policy.DailyCutoffTime
            ? DateOnly.FromDateTime(localDispatch.Date).AddDays(1)
            : DateOnly.FromDateTime(localDispatch.Date);

        if (policy.CalculationType == TransitDayCalculationType.CalendarDays)
        {
            var calTarget = effectiveLocalDate.AddDays(policy.TransitCommitmentDays);
            return LocalToUtcSafe(calTarget.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified), tz);
        }

        bool isCrossBorder = !string.IsNullOrEmpty(originCountry) &&
                             !string.Equals(originCountry, destinationCountry, StringComparison.OrdinalIgnoreCase);

        // Batch-load active holidays over lookahead window
        var maxDate = effectiveLocalDate.AddDays(Math.Max(60, policy.TransitCommitmentDays * 5));
        var facilityId = policy.OriginFacilityId;

        var relevantCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { destinationCountry };
        if (!string.IsNullOrEmpty(originCountry))
        {
            relevantCountries.Add(originCountry);
        }

        var candidateHolidays = await _db.HolidayCalendars
            .AsNoTracking()
            .Where(h => h.IsActive &&
                        h.HolidayDate >= effectiveLocalDate &&
                        h.HolidayDate <= maxDate &&
                        ((facilityId.HasValue && h.FacilityId == facilityId.Value) ||
                         relevantCountries.Contains(h.CountryCode)))
            .ToListAsync(cancellationToken);

        var originHolidaysByDate = candidateHolidays
            .Where(h =>
                (facilityId.HasValue && h.FacilityId == facilityId.Value) ||
                (!string.IsNullOrEmpty(originCountry) &&
                 h.CountryCode == originCountry &&
                 (h.HolidayScope == HolidayScope.National ||
                  (h.HolidayScope == HolidayScope.Regional && originSubdivision != null && h.SubdivisionCode == originSubdivision))))
            .GroupBy(h => h.HolidayDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        var destinationHolidaysByDate = candidateHolidays
            .Where(h =>
                (!isCrossBorder && facilityId.HasValue && h.FacilityId == facilityId.Value) ||
                (h.CountryCode == destinationCountry &&
                 (h.HolidayScope == HolidayScope.National ||
                  (h.HolidayScope == HolidayScope.Regional && destinationSubdivision != null && h.SubdivisionCode == destinationSubdivision))))
            .GroupBy(h => h.HolidayDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        bool IsOriginPickupWorking(DateOnly date)
        {
            if (string.IsNullOrEmpty(originCountry))
            {
                return IsDestinationTransitWorking(date);
            }

            var probeUtc = LocalToUtcSafe(date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified), tz);
            var activeCal = originFacilityCalendars.FirstOrDefault(fc =>
                fc.EffectiveFromUtc <= probeUtc &&
                (!fc.EffectiveToUtc.HasValue || probeUtc < fc.EffectiveToUtc.Value));

            var restDays = ResolveWeeklyRestDays(activeCal, originCountry);
            var dayHolidays = originHolidaysByDate.TryGetValue(date, out var list) ? list : [];

            return EvaluateLocalDate(facilityId ?? Guid.Empty, tz.Id, date, restDays, activeCal?.Id, dayHolidays).IsWorkingDay;
        }

        bool IsDestinationTransitWorking(DateOnly date)
        {
            FacilityCalendar? boundCal = null;
            if (!isCrossBorder)
            {
                var probeUtc = LocalToUtcSafe(date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified), tz);
                boundCal = originFacilityCalendars.FirstOrDefault(fc =>
                    fc.EffectiveFromUtc <= probeUtc &&
                    (!fc.EffectiveToUtc.HasValue || probeUtc < fc.EffectiveToUtc.Value));
            }

            // Cross-border transit & delivery follow destination country holidays/weekends rather than origin holidays/weekends
            var restDays = ResolveWeeklyRestDays(boundCal, destinationCountry);
            var dayHolidays = destinationHolidaysByDate.TryGetValue(date, out var list) ? list : [];

            return EvaluateLocalDate(isCrossBorder ? Guid.Empty : (facilityId ?? Guid.Empty), tz.Id, date, restDays, boundCal?.Id, dayHolidays).IsWorkingDay;
        }

        // 1. Origin carrier pickup / tender readiness follows Origin Facility calendar
        var currentDate = effectiveLocalDate;
        int safety = 0;
        while (!IsOriginPickupWorking(currentDate) && safety++ < 365)
        {
            currentDate = currentDate.AddDays(1);
        }

        // 2. Contractual business-day transit & destination delivery follow Destination calendar
        int remainingDays = policy.TransitCommitmentDays;
        while (remainingDays > 0 && safety++ < 365)
        {
            currentDate = currentDate.AddDays(1);
            if (IsDestinationTransitWorking(currentDate))
            {
                remainingDays--;
            }
        }

        return LocalToUtcSafe(currentDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified), tz);
    }

    private static WorkingDayEvaluationResult EvaluateLocalDate(
        Guid facilityId,
        string timeZoneId,
        DateOnly localDate,
        IReadOnlySet<DayOfWeek> restDays,
        Guid? facilityCalendarId,
        IReadOnlyList<HolidayCalendar> matchedHolidays)
    {
        // Deterministic precedence: Facility > Regional > National
        var governingHoliday = matchedHolidays
            .OrderByDescending(h => (int)h.HolidayScope)
            .ThenByDescending(h => h.IsWorkingDayOverride)
            .ThenBy(h => h.Id)
            .FirstOrDefault();

        if (governingHoliday != null)
        {
            if (governingHoliday.IsWorkingDayOverride)
            {
                return new WorkingDayEvaluationResult(
                    facilityId,
                    localDate,
                    localDate.DayOfWeek,
                    true,
                    $"Working-day override ({governingHoliday.HolidayScope}): {governingHoliday.Name}",
                    governingHoliday.Id,
                    governingHoliday.HolidayScope,
                    true,
                    facilityCalendarId,
                    timeZoneId);
            }

            return new WorkingDayEvaluationResult(
                facilityId,
                localDate,
                localDate.DayOfWeek,
                false,
                $"Public/facility holiday ({governingHoliday.HolidayScope}): {governingHoliday.Name}",
                governingHoliday.Id,
                governingHoliday.HolidayScope,
                false,
                facilityCalendarId,
                timeZoneId);
        }

        if (restDays.Contains(localDate.DayOfWeek))
        {
            return new WorkingDayEvaluationResult(
                facilityId,
                localDate,
                localDate.DayOfWeek,
                false,
                $"Scheduled weekly rest day ({localDate.DayOfWeek}).",
                null,
                null,
                false,
                facilityCalendarId,
                timeZoneId);
        }

        return new WorkingDayEvaluationResult(
            facilityId,
            localDate,
            localDate.DayOfWeek,
            true,
            "Regular facility working day.",
            null,
            null,
            false,
            facilityCalendarId,
            timeZoneId);
    }

    private static IReadOnlySet<DayOfWeek> ResolveWeeklyRestDays(FacilityCalendar? activeCalendar, string countryCode)
    {
        if (activeCalendar != null)
        {
            return activeCalendar.GetWeeklyRestDays();
        }

        // Country-aware fallback when no explicit FacilityCalendar row is bound:
        // Nepal observes Saturday-only weekend; standard US/other corridors observe Saturday+Sunday.
        return string.Equals(NormalizeIsoCountryFallback(countryCode), "NP", StringComparison.OrdinalIgnoreCase)
            ? new HashSet<DayOfWeek> { DayOfWeek.Saturday }
            : new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday };
    }

    private async Task<string?> ResolveSubdivisionCodeAsync(
        string countryCode,
        string? stateOrProvince,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stateOrProvince)) return null;
        var trimmed = stateOrProvince.Trim();
        var match = await _db.AdministrativeDivisions
            .AsNoTracking()
            .Where(d => d.CountryCode == countryCode &&
                        d.IsActive &&
                        (d.IsoCode == trimmed || d.Code == trimmed || d.Name == trimmed))
            .Select(d => d.IsoCode ?? d.Code)
            .FirstOrDefaultAsync(ct);
        return match?.ToUpperInvariant() ?? trimmed.ToUpperInvariant();
    }

    private static string NormalizeIsoCountryFallback(string? countryCode)
    {
        var c = (countryCode ?? string.Empty).Trim().ToUpperInvariant();
        return c switch
        {
            "USA" => "US",
            "NPL" => "NP",
            _ => c
        };
    }

    private static DateTime NormalizeUtcStrict(DateTime timestamp)
    {
        return timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => throw new DomainException(
                "Timestamp must explicitly specify DateTimeKind.Utc or offset kind; ambiguous Unspecified timestamps are rejected.",
                "INVALID_TIMESTAMP_KIND")
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        var id = timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException(
                $"Configured timezone '{id}' is not a valid IANA or system timezone.",
                "INVALID_TIMEZONE");
        }
    }

    private static DateTime LocalToUtcSafe(DateTime unspecifiedLocal, TimeZoneInfo tz)
    {
        // Guard against spring-forward invalid local wall times during DST transitions
        var probe = DateTime.SpecifyKind(unspecifiedLocal, DateTimeKind.Unspecified);
        while (tz.IsInvalidTime(probe))
        {
            probe = probe.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(probe, tz);
    }
}
