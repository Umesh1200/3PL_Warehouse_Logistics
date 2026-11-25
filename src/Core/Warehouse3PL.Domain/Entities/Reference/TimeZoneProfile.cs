namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Authoritative IANA tzdb timezone registry entry.
/// Effective UTC offsets MUST always be computed dynamically via
/// <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/> for a specific UTC instant,
/// rather than persisted as static offset columns.
/// </summary>
public class TimeZoneProfile
{
    public string TimeZoneId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool ObservesDst { get; set; }
    public string? CountryCode { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Computes the exact UTC offset in minutes at the supplied UTC instant using system tzdb/ICU data.
    /// </summary>
    public int GetUtcOffsetMinutesAt(DateTime utcInstant)
    {
        var utc = utcInstant.Kind == DateTimeKind.Utc
            ? utcInstant
            : DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        return (int)tz.GetUtcOffset(utc).TotalMinutes;
    }
}
