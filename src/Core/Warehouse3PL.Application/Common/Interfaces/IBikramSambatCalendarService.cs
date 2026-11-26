using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// DB-backed Nepal Bikram Sambat (BS) calendar converter and Nepal statutory fiscal-year resolver.
/// Resolves all dates strictly from seeded authoritative <see cref="Domain.Entities.Reference.BikramSambatCalendarMonth"/> rows.
/// </summary>
public interface IBikramSambatCalendarService
{
    Task<BikramSambatDateResult> ConvertToBikramSambatAsync(DateOnly gregorianDate, CancellationToken cancellationToken = default);

    Task<BikramSambatDateResult> ConvertInstantInTimeZoneToBikramSambatAsync(
        DateTime utcInstant,
        string timeZoneId,
        CancellationToken cancellationToken = default);

    Task<DateOnly> ConvertToGregorianAsync(int bsYear, int bsMonth, int bsDay, CancellationToken cancellationToken = default);

    Task<string> ResolveNepaliFiscalYearAsync(DateOnly gregorianDate, CancellationToken cancellationToken = default);

    Task<DateOnly> ResolveVatReturnDueDateAsync(int bsYear, int bsMonth, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BikramSambatCalendarMonthDto>> GetCalendarMonthsAsync(
        int? bsYear = null,
        CancellationToken cancellationToken = default);
}
