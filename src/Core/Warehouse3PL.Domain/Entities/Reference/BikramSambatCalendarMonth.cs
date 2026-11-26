using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Immutable authoritative Nepal Bikram Sambat (BS) calendar month lookup table (2075 BS–2090 BS).
/// Because BS month lengths (29–32 days) follow astronomical calculations rather than fixed rules,
/// exact Gregorian date ranges and Shrawan 1 fiscal year transitions are stored per month.
/// </summary>
public class BikramSambatCalendarMonth
{
    public const int MinSupportedBsYear = 2075;
    public const int MaxSupportedBsYear = 2090;

    public int BsYear { get; set; }
    public int BsMonth { get; set; }
    public string MonthNameEn { get; set; } = string.Empty;
    public string MonthNameNe { get; set; } = string.Empty;
    public int DaysInMonth { get; set; }
    public DateOnly GregorianStartDate { get; set; }
    public DateOnly GregorianEndDate { get; set; }

    /// <summary>
    /// Nepal fiscal year label (e.g. "2083/84"). Fiscal year advances on Shrawan 1 (BsMonth == 4).
    /// </summary>
    public string FiscalYearLabel { get; set; } = string.Empty;
    public string SourceCitation { get; set; } = string.Empty;
    public int SeedVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static BikramSambatCalendarMonth Create(
        int bsYear,
        int bsMonth,
        string monthNameEn,
        string monthNameNe,
        int daysInMonth,
        DateOnly gregorianStartDate,
        DateOnly gregorianEndDate,
        string sourceCitation,
        int seedVersion = 1)
    {
        if (bsYear < MinSupportedBsYear || bsYear > MaxSupportedBsYear)
        {
            throw new DomainException(
                $"BsYear {bsYear} is outside supported Bikram Sambat catalog range ({MinSupportedBsYear}–{MaxSupportedBsYear}).",
                "BS_YEAR_OUT_OF_RANGE");
        }

        if (bsMonth < 1 || bsMonth > 12)
        {
            throw new DomainException($"BsMonth {bsMonth} must be between 1 and 12.", "BS_INVALID_MONTH");
        }

        if (daysInMonth < 29 || daysInMonth > 32)
        {
            throw new DomainException($"DaysInMonth {daysInMonth} must be between 29 and 32.", "BS_INVALID_DAYS_IN_MONTH");
        }

        if (gregorianStartDate.AddDays(daysInMonth - 1) != gregorianEndDate)
        {
            throw new DomainException(
                $"Gregorian span [{gregorianStartDate:yyyy-MM-dd}..{gregorianEndDate:yyyy-MM-dd}] does not equal {daysInMonth} days.",
                "BS_SPAN_MISMATCH");
        }

        if (string.IsNullOrWhiteSpace(monthNameEn) || string.IsNullOrWhiteSpace(monthNameNe))
        {
            throw new DomainException("MonthNameEn and MonthNameNe are required.", "BS_MISSING_MONTH_NAME");
        }

        if (string.IsNullOrWhiteSpace(sourceCitation))
        {
            throw new DomainException("SourceCitation is required for BikramSambatCalendarMonth entries.", "BS_MISSING_SOURCE_CITATION");
        }

        return new BikramSambatCalendarMonth
        {
            BsYear = bsYear,
            BsMonth = bsMonth,
            MonthNameEn = monthNameEn.Trim(),
            MonthNameNe = monthNameNe.Trim(),
            DaysInMonth = daysInMonth,
            GregorianStartDate = gregorianStartDate,
            GregorianEndDate = gregorianEndDate,
            FiscalYearLabel = ComputeFiscalYearLabel(bsYear, bsMonth),
            SourceCitation = sourceCitation.Trim(),
            SeedVersion = seedVersion,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static string ComputeFiscalYearLabel(int bsYear, int bsMonth)
    {
        // Nepal statutory fiscal year starts on Shrawan 1 (BsMonth == 4).
        // Months 1..3 (Baishakh..Ashadh) remain in the fiscal year started the preceding Shrawan.
        int startYear = bsMonth >= 4 ? bsYear : bsYear - 1;
        int endTwoDigits = (startYear + 1) % 100;
        return $"{startYear}/{endTwoDigits:D2}";
    }
}
