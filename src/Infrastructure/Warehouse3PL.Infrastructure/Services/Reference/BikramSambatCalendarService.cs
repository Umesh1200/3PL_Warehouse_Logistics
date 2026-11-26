using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Reference;

public sealed class BikramSambatCalendarService : IBikramSambatCalendarService
{
    private readonly IApplicationDbContext _db;

    public BikramSambatCalendarService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<BikramSambatDateResult> ConvertToBikramSambatAsync(
        DateOnly gregorianDate,
        CancellationToken cancellationToken = default)
    {
        var month = await _db.BikramSambatCalendarMonths
            .AsNoTracking()
            .Where(m => m.GregorianStartDate <= gregorianDate && m.GregorianEndDate >= gregorianDate)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(
                $"Gregorian date '{gregorianDate:yyyy-MM-dd}' is outside the seeded Bikram Sambat catalog ({BikramSambatCalendarMonth.MinSupportedBsYear}–{BikramSambatCalendarMonth.MaxSupportedBsYear} BS).",
                "BS_DATE_OUT_OF_RANGE");

        var bsDay = gregorianDate.DayNumber - month.GregorianStartDate.DayNumber + 1;
        return new BikramSambatDateResult(
            gregorianDate,
            month.BsYear,
            month.BsMonth,
            bsDay,
            month.MonthNameEn,
            month.MonthNameNe,
            $"{month.BsYear:D4}-{month.BsMonth:D2}-{bsDay:D2}",
            month.FiscalYearLabel,
            null,
            month.SourceCitation);
    }

    public async Task<BikramSambatDateResult> ConvertInstantInTimeZoneToBikramSambatAsync(
        DateTime utcInstant,
        string timeZoneId,
        CancellationToken cancellationToken = default)
    {
        if (utcInstant.Kind == DateTimeKind.Unspecified)
        {
            throw new DomainException(
                "Timestamp kind cannot be Unspecified when resolving Bikram Sambat date across IANA timezone boundaries.",
                "BS_UNSPECIFIED_TIMESTAMP_KIND");
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new DomainException("TimeZoneId is required.", "INVALID_TIMEZONE");
        }

        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException($"Unknown or invalid IANA timezone '{timeZoneId}'.", "INVALID_TIMEZONE");
        }

        var normalizedUtc = utcInstant.Kind == DateTimeKind.Utc
            ? utcInstant
            : utcInstant.ToUniversalTime();
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, tz);
        var localDate = DateOnly.FromDateTime(localTime);

        var res = await ConvertToBikramSambatAsync(localDate, cancellationToken);
        return res with { TimeZoneId = tz.Id };
    }

    public async Task<DateOnly> ConvertToGregorianAsync(
        int bsYear,
        int bsMonth,
        int bsDay,
        CancellationToken cancellationToken = default)
    {
        if (bsYear < BikramSambatCalendarMonth.MinSupportedBsYear ||
            bsYear > BikramSambatCalendarMonth.MaxSupportedBsYear)
        {
            throw new DomainException(
                $"BsYear {bsYear} is outside supported range ({BikramSambatCalendarMonth.MinSupportedBsYear}–{BikramSambatCalendarMonth.MaxSupportedBsYear}).",
                "BS_DATE_OUT_OF_RANGE");
        }

        if (bsMonth < 1 || bsMonth > 12 || bsDay < 1)
        {
            throw new DomainException($"Invalid Bikram Sambat date {bsYear}-{bsMonth:D2}-{bsDay:D2}.", "BS_INVALID_DATE");
        }

        var month = await _db.BikramSambatCalendarMonths
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.BsYear == bsYear && m.BsMonth == bsMonth, cancellationToken)
            ?? throw new DomainException(
                $"Bikram Sambat month {bsYear}-{bsMonth:D2} not found in seeded catalog.",
                "BS_DATE_OUT_OF_RANGE");

        if (bsDay > month.DaysInMonth)
        {
            throw new DomainException(
                $"BsDay {bsDay} exceeds maximum {month.DaysInMonth} days for {month.MonthNameEn} {bsYear}.",
                "BS_INVALID_DAY");
        }

        return month.GregorianStartDate.AddDays(bsDay - 1);
    }

    public async Task<string> ResolveNepaliFiscalYearAsync(
        DateOnly gregorianDate,
        CancellationToken cancellationToken = default)
    {
        var converted = await ConvertToBikramSambatAsync(gregorianDate, cancellationToken);
        return converted.FiscalYearLabel;
    }

    public async Task<DateOnly> ResolveVatReturnDueDateAsync(
        int bsYear,
        int bsMonth,
        CancellationToken cancellationToken = default)
    {
        if (bsMonth < 1 || bsMonth > 12)
        {
            throw new DomainException($"Invalid BsMonth '{bsMonth}'.", "BS_INVALID_MONTH");
        }

        int dueBsYear = bsMonth == 12 ? bsYear + 1 : bsYear;
        int dueBsMonth = bsMonth == 12 ? 1 : bsMonth + 1;
        return await ConvertToGregorianAsync(dueBsYear, dueBsMonth, 25, cancellationToken);
    }

    public async Task<IReadOnlyList<BikramSambatCalendarMonthDto>> GetCalendarMonthsAsync(
        int? bsYear = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.BikramSambatCalendarMonths.AsNoTracking();
        if (bsYear.HasValue)
        {
            query = query.Where(m => m.BsYear == bsYear.Value);
        }

        var rows = await query
            .OrderBy(m => m.BsYear)
            .ThenBy(m => m.BsMonth)
            .ToListAsync(cancellationToken);

        return rows.Select(m => m.ToDto()).ToList();
    }
}
