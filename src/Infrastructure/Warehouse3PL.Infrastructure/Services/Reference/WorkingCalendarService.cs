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

    public WorkingCalendarService(IApplicationDbContext db)
    {
        _db = db;
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

        var activeCal = await _db.FacilityCalendars
            .AsNoTracking()
            .Where(fc => fc.FacilityId == facilityId &&
                         fc.IsActive &&
                         fc.EffectiveFromUtc <= targetUtc &&
                         (fc.EffectiveToUtc == null || targetUtc < fc.EffectiveToUtc))
            .OrderByDescending(fc => fc.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var restDays = ResolveWeeklyRestDays(activeCal, facility.CountryCode);

        var normalizedCountry = NormalizeIsoCountryFallback(facility.CountryCode);
        var facilitySub = await ResolveSubdivisionCodeAsync(normalizedCountry, facility.StateOrProvince, cancellationToken);

        var holidays = await _db.HolidayCalendars
            .AsNoTracking()
            .Where(h => h.IsActive &&
                        h.HolidayDate == localDate &&
                        (h.FacilityId == facilityId ||
                         (h.CountryCode == normalizedCountry &&
                          (h.HolidayScope == HolidayScope.National ||
                           (h.HolidayScope == HolidayScope.Regional && facilitySub != null && h.SubdivisionCode == facilitySub)))))
            .ToListAsync(cancellationToken);

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
        var normalizedDispatch = dispatchUtc.Kind == DateTimeKind.Utc
            ? dispatchUtc
            : DateTime.SpecifyKind(dispatchUtc, DateTimeKind.Utc);

        string tzId = string.IsNullOrWhiteSpace(policy.CutoffTimeZone) ? "UTC" : policy.CutoffTimeZone.Trim();
        string countryCode = NormalizeIsoCountryFallback(policy.DestinationCountry);
        string? subdivisionCode = null;
        IReadOnlyList<FacilityCalendar> facilityCalendars = [];

        if (policy.OriginFacilityId.HasValue && policy.OriginFacilityId.Value != Guid.Empty)
        {
            var facility = await _db.Facilities
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == policy.OriginFacilityId.Value, cancellationToken);

            if (facility != null)
            {
                if (string.IsNullOrWhiteSpace(policy.CutoffTimeZone) ||
                    string.Equals(policy.CutoffTimeZone, "UTC", StringComparison.OrdinalIgnoreCase))
                {
                    tzId = string.IsNullOrWhiteSpace(facility.Timezone) ? "UTC" : facility.Timezone;
                }

                countryCode = NormalizeIsoCountryFallback(facility.CountryCode);
                subdivisionCode = await ResolveSubdivisionCodeAsync(countryCode, facility.StateOrProvince, cancellationToken);

                facilityCalendars = await _db.FacilityCalendars
                    .AsNoTracking()
                    .Where(fc => fc.FacilityId == facility.Id && fc.IsActive)
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

        // Batch-load active holidays over generous lookahead window (max 120 calendar days)
        var maxDate = effectiveLocalDate.AddDays(Math.Max(60, policy.TransitCommitmentDays * 5));
        var facilityId = policy.OriginFacilityId;

        var holidays = await _db.HolidayCalendars
            .AsNoTracking()
            .Where(h => h.IsActive &&
                        h.HolidayDate >= effectiveLocalDate &&
                        h.HolidayDate <= maxDate &&
                        ((facilityId.HasValue && h.FacilityId == facilityId.Value) ||
                         (h.CountryCode == countryCode &&
                          (h.HolidayScope == HolidayScope.National ||
                           (h.HolidayScope == HolidayScope.Regional && subdivisionCode != null && h.SubdivisionCode == subdivisionCode)))))
            .ToListAsync(cancellationToken);

        var holidayLookup = holidays.GroupBy(h => h.HolidayDate).ToDictionary(g => g.Key, g => g.ToList());

        bool IsDateWorking(DateOnly date)
        {
            var probeUtc = LocalToUtcSafe(date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified), tz);
            var activeCal = facilityCalendars.FirstOrDefault(fc =>
                fc.EffectiveFromUtc <= probeUtc &&
                (!fc.EffectiveToUtc.HasValue || probeUtc < fc.EffectiveToUtc.Value));

            var restDays = ResolveWeeklyRestDays(activeCal, countryCode);
            var dayHolidays = holidayLookup.TryGetValue(date, out var list) ? list : [];

            return EvaluateLocalDate(facilityId ?? Guid.Empty, tz.Id, date, restDays, activeCal?.Id, dayHolidays).IsWorkingDay;
        }

        var currentDate = effectiveLocalDate;
        int safety = 0;
        while (!IsDateWorking(currentDate) && safety++ < 365)
        {
            currentDate = currentDate.AddDays(1);
        }

        int remainingDays = policy.TransitCommitmentDays;
        while (remainingDays > 0 && safety++ < 365)
        {
            currentDate = currentDate.AddDays(1);
            if (IsDateWorking(currentDate))
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

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
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
