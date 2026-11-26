using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Effective-dated warehouse working schedule defining weekly rest days (e.g. Saturday-only for Nepal,
/// Saturday+Sunday for US) and daily operating / SLA cutoff hours in the facility's IANA timezone.
/// Non-overlapping active windows are guarded by PostgreSQL btree_gist exclusion constraints.
/// </summary>
public class FacilityCalendar : AuditableEntity<Guid>
{
    public Guid FacilityId { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }

    /// <summary>
    /// Canonical comma-separated System.DayOfWeek names representing weekly rest days
    /// (e.g. "Saturday" for NP, "Saturday,Sunday" for US).
    /// </summary>
    public string WeeklyRestDaysCsv { get; set; } = string.Empty;
    public TimeSpan WorkdayStartLocalTime { get; set; } = new(9, 0, 0);
    public TimeSpan WorkdayEndLocalTime { get; set; } = new(18, 0, 0);
    public TimeSpan DefaultCutoffLocalTime { get; set; } = new(17, 0, 0);
    public long ConfigRevision { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    public Facility? Facility { get; set; }

    public FacilityCalendar()
    {
        Id = Guid.NewGuid();
    }

    public static FacilityCalendar Create(
        Guid facilityId,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        string weeklyRestDaysCsv,
        TimeSpan workdayStartLocalTime,
        TimeSpan workdayEndLocalTime,
        TimeSpan defaultCutoffLocalTime,
        Guid? id = null)
    {
        if (facilityId == Guid.Empty)
        {
            throw new DomainException("FacilityId is required for FacilityCalendar.", "FACILITY_CALENDAR_MISSING_FACILITY");
        }

        if (effectiveFromUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("EffectiveFromUtc must explicitly use DateTimeKind.Utc.", "FACILITY_CALENDAR_INVALID_UTC");
        }

        if (effectiveToUtc.HasValue)
        {
            if (effectiveToUtc.Value.Kind != DateTimeKind.Utc)
            {
                throw new DomainException("EffectiveToUtc must explicitly use DateTimeKind.Utc.", "FACILITY_CALENDAR_INVALID_UTC");
            }

            if (effectiveToUtc.Value <= effectiveFromUtc)
            {
                throw new DomainException("EffectiveToUtc must be strictly after EffectiveFromUtc.", "FACILITY_CALENDAR_INVALID_WINDOW");
            }
        }

        if (workdayEndLocalTime <= workdayStartLocalTime)
        {
            throw new DomainException("WorkdayEndLocalTime must be strictly later than WorkdayStartLocalTime.", "FACILITY_CALENDAR_INVALID_HOURS");
        }

        if (defaultCutoffLocalTime < workdayStartLocalTime || defaultCutoffLocalTime > workdayEndLocalTime)
        {
            throw new DomainException(
                "DefaultCutoffLocalTime must fall within [WorkdayStartLocalTime, WorkdayEndLocalTime].",
                "FACILITY_CALENDAR_INVALID_CUTOFF");
        }

        var canonicalCsv = NormalizeWeeklyRestDaysCsv(weeklyRestDaysCsv);

        var entity = new FacilityCalendar
        {
            FacilityId = facilityId,
            EffectiveFromUtc = effectiveFromUtc,
            EffectiveToUtc = effectiveToUtc,
            WeeklyRestDaysCsv = canonicalCsv,
            WorkdayStartLocalTime = workdayStartLocalTime,
            WorkdayEndLocalTime = workdayEndLocalTime,
            DefaultCutoffLocalTime = defaultCutoffLocalTime,
            ConfigRevision = 1,
            IsActive = true
        };

        if (id.HasValue && id.Value != Guid.Empty)
        {
            entity.Id = id.Value;
        }

        return entity;
    }

    public IReadOnlySet<DayOfWeek> GetWeeklyRestDays()
        => ParseWeeklyRestDays(WeeklyRestDaysCsv);

    public bool OverlapsWindow(DateTime fromUtc, DateTime? toUtc)
    {
        var thisEnd = EffectiveToUtc ?? DateTime.MaxValue;
        var otherEnd = toUtc ?? DateTime.MaxValue;
        return EffectiveFromUtc < otherEnd && fromUtc < thisEnd;
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        ConfigRevision++;
    }

    public void CloseWindowAt(DateTime effectiveToUtc)
    {
        if (effectiveToUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("EffectiveToUtc must explicitly use DateTimeKind.Utc.", "FACILITY_CALENDAR_INVALID_UTC");
        }

        if (effectiveToUtc <= EffectiveFromUtc)
        {
            throw new DomainException("EffectiveToUtc must be strictly after EffectiveFromUtc.", "FACILITY_CALENDAR_INVALID_WINDOW");
        }

        EffectiveToUtc = effectiveToUtc;
        ConfigRevision++;
    }

    public static string NormalizeWeeklyRestDaysCsv(string weeklyRestDaysCsv)
    {
        var days = ParseWeeklyRestDays(weeklyRestDaysCsv);
        if (days.Count >= 7)
        {
            throw new DomainException(
                "FacilityCalendar must leave at least one weekly working day.",
                "FACILITY_CALENDAR_NO_WORKING_DAYS");
        }

        return string.Join(",", days.OrderBy(d => (int)d).Select(d => d.ToString()));
    }

    public static IReadOnlySet<DayOfWeek> ParseWeeklyRestDays(string? csv)
    {
        var set = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(csv))
        {
            return set;
        }

        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<DayOfWeek>(raw, ignoreCase: true, out var day) || !Enum.IsDefined(day))
            {
                throw new DomainException(
                    $"Invalid WeeklyRestDays token '{raw}'. Expected System.DayOfWeek names (e.g. Saturday, Sunday).",
                    "FACILITY_CALENDAR_INVALID_REST_DAY");
            }

            set.Add(day);
        }

        return set;
    }
}
