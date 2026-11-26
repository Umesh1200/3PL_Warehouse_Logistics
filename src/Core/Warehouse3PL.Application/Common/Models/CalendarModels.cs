using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public sealed record BikramSambatCalendarMonthDto(
    int BsYear,
    int BsMonth,
    string MonthNameEn,
    string MonthNameNe,
    int DaysInMonth,
    DateOnly GregorianStartDate,
    DateOnly GregorianEndDate,
    string FiscalYearLabel,
    string SourceCitation,
    int SeedVersion);

public sealed record BikramSambatDateResult(
    DateOnly GregorianDate,
    int BsYear,
    int BsMonth,
    int BsDay,
    string MonthNameEn,
    string MonthNameNe,
    string FormattedBsDate,
    string FiscalYearLabel,
    string? TimeZoneId,
    string SourceCitation);

public sealed record HolidayCalendarDto(
    Guid Id,
    string CountryCode,
    string? SubdivisionCode,
    Guid? FacilityId,
    DateOnly HolidayDate,
    string Name,
    HolidayScope HolidayScope,
    bool IsWorkingDayOverride,
    long ConfigRevision,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record FacilityCalendarDto(
    Guid Id,
    Guid FacilityId,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    string WeeklyRestDaysCsv,
    IReadOnlyList<DayOfWeek> WeeklyRestDays,
    TimeSpan WorkdayStartLocalTime,
    TimeSpan WorkdayEndLocalTime,
    TimeSpan DefaultCutoffLocalTime,
    long ConfigRevision,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record WorkingDayEvaluationResult(
    Guid FacilityId,
    DateOnly LocalDate,
    DayOfWeek DayOfWeek,
    bool IsWorkingDay,
    string Reason,
    Guid? AppliedHolidayId,
    HolidayScope? AppliedHolidayScope,
    bool IsWorkingDayOverride,
    Guid? FacilityCalendarId,
    string TimeZoneId);

public static class CalendarMappings
{
    public static BikramSambatCalendarMonthDto ToDto(this BikramSambatCalendarMonth m) => new(
        m.BsYear,
        m.BsMonth,
        m.MonthNameEn,
        m.MonthNameNe,
        m.DaysInMonth,
        m.GregorianStartDate,
        m.GregorianEndDate,
        m.FiscalYearLabel,
        m.SourceCitation,
        m.SeedVersion);

    public static HolidayCalendarDto ToDto(this HolidayCalendar h) => new(
        h.Id,
        h.CountryCode,
        h.SubdivisionCode,
        h.FacilityId,
        h.HolidayDate,
        h.Name,
        h.HolidayScope,
        h.IsWorkingDayOverride,
        h.ConfigRevision,
        h.IsActive,
        h.CreatedAt,
        h.UpdatedAt);

    public static FacilityCalendarDto ToDto(this FacilityCalendar fc) => new(
        fc.Id,
        fc.FacilityId,
        fc.EffectiveFromUtc,
        fc.EffectiveToUtc,
        fc.WeeklyRestDaysCsv,
        fc.GetWeeklyRestDays().OrderBy(d => (int)d).ToList(),
        fc.WorkdayStartLocalTime,
        fc.WorkdayEndLocalTime,
        fc.DefaultCutoffLocalTime,
        fc.ConfigRevision,
        fc.IsActive,
        fc.CreatedAt,
        fc.UpdatedAt);
}
