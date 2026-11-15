using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Daily aggregated read model tracking worker-level productivity, unconflated rates, utilization, and labor efficiency.
/// DB-SPEC-3PL-2026 Phase 6.2 Labor, Productivity & Workforce Pacing Analytics.
/// </summary>
public class DailyWorkerProductivitySummary : AuditableEntity<Guid>
{
    public Guid WorkerUserId { get; set; }
    public Guid FacilityId { get; set; }
    public string Department { get; set; } = string.Empty;
    public DateTime DateUtc { get; set; }
    public TimeGrain TimeGrain { get; set; } = TimeGrain.Daily;

    // Shift & Duration Breakdown (Minutes)
    public decimal TotalShiftMinutes { get; set; } = 480m; // Default 8-hour shift
    public decimal ProductiveWorkMinutes { get; set; }
    public decimal IdleMinutes { get; set; }
    public decimal ScheduledBreakMinutes { get; set; } = 30m;

    // Completed Throughput Counts
    public int TasksCompleted { get; set; }
    public int UnitsCompleted { get; set; }
    public int LinesCompleted { get; set; }
    public int CartonsCompleted { get; set; }
    public int PalletsCompleted { get; set; }

    // Direct Labor Hours
    public decimal ActualLaborHours { get; set; }
    public decimal StandardLaborHours { get; set; }

    // Workforce Metrics & Pacing
    public decimal LaborEfficiencyPercent { get; set; }
    public decimal LaborUtilizationPercent { get; set; }
    public decimal UnitsPerHour { get; set; }
    public decimal LinesPerHour { get; set; }
    public decimal CartonsPerHour { get; set; }
    public decimal TasksPerHour { get; set; }
    public LaborPerformanceTier PerformanceTier { get; set; } = LaborPerformanceTier.Underperforming;

    // Direct Wage & Activity Costing
    public decimal HourlyWageRate { get; set; }
    public decimal TotalLaborCost { get; set; }
    public decimal LaborCostPerUnit { get; set; }
    public decimal LaborCostPerTask { get; set; }

    /// <summary>
    /// High-water mark of completed task intervals for preventing overlapping task duration double-counting.
    /// </summary>
    public DateTime? LastTaskCompletedAtUtc { get; set; }

    public DailyWorkerProductivitySummary()
    {
        Id = Guid.NewGuid();
    }

    public static DailyWorkerProductivitySummary Create(
        Guid workerUserId,
        Guid facilityId,
        string department,
        DateTime dateUtc,
        decimal totalShiftMinutes = 480m,
        decimal scheduledBreakMinutes = 30m,
        decimal hourlyWageRate = 0m)
    {
        if (workerUserId == Guid.Empty || facilityId == Guid.Empty)
            throw new InvalidDomainStateException("WorkerUserId and FacilityId are required.");

        var summary = new DailyWorkerProductivitySummary
        {
            WorkerUserId = workerUserId,
            FacilityId = facilityId,
            Department = string.IsNullOrWhiteSpace(department) ? "General" : department.Trim(),
            DateUtc = dateUtc.Date,
            TimeGrain = TimeGrain.Daily,
            TotalShiftMinutes = Math.Max(0m, totalShiftMinutes),
            ScheduledBreakMinutes = Math.Max(0m, scheduledBreakMinutes),
            HourlyWageRate = Math.Max(0m, hourlyWageRate)
        };

        summary.RecalculateMetrics();
        return summary;
    }

    /// <summary>
    /// Records a completed task execution into the daily worker summary and recalculates all rates and tiers.
    /// Includes overlapping interval protection: clips any already-accounted active duration.
    /// </summary>
    public void RecordTaskExecution(
        int units,
        int lines,
        int cartons,
        int pallets,
        decimal actualSeconds,
        decimal standardSeconds,
        decimal? hourlyWageRate = null,
        DateTime? startedAtUtc = null,
        DateTime? completedAtUtc = null)
    {
        TasksCompleted++;
        UnitsCompleted += Math.Max(0, units);
        LinesCompleted += Math.Max(0, lines);
        CartonsCompleted += Math.Max(0, cartons);
        PalletsCompleted += Math.Max(0, pallets);

        // Overlapping interval protection
        decimal effectiveActualSeconds = actualSeconds;
        if (startedAtUtc.HasValue && completedAtUtc.HasValue && LastTaskCompletedAtUtc.HasValue)
        {
            if (completedAtUtc.Value <= LastTaskCompletedAtUtc.Value)
            {
                // Fully contained inside an already-credited interval
                effectiveActualSeconds = 0m;
            }
            else if (startedAtUtc.Value < LastTaskCompletedAtUtc.Value)
            {
                // Partially overlapping: credit only non-overlapping tail
                effectiveActualSeconds = (decimal)(completedAtUtc.Value - LastTaskCompletedAtUtc.Value).TotalSeconds;
            }

            if (completedAtUtc.Value > LastTaskCompletedAtUtc.Value)
            {
                LastTaskCompletedAtUtc = completedAtUtc.Value;
            }
        }
        else if (completedAtUtc.HasValue)
        {
            LastTaskCompletedAtUtc = completedAtUtc.Value;
        }

        var actualMins = Math.Max(0m, effectiveActualSeconds) / 60m;
        ProductiveWorkMinutes += actualMins;

        var stdHours = Math.Max(0m, standardSeconds) / 3600m;
        StandardLaborHours += stdHours;

        if (hourlyWageRate.HasValue && hourlyWageRate.Value > 0m)
        {
            HourlyWageRate = hourlyWageRate.Value;
        }

        RecalculateMetrics();
    }

    /// <summary>
    /// Records an idle or non-productive interval into the daily worker summary.
    /// </summary>
    public void RecordIdleInterval(decimal durationMinutes, LaborIdleTimeReason reason)
    {
        if (durationMinutes <= 0m) return;

        if (reason == LaborIdleTimeReason.ScheduledBreak)
        {
            ScheduledBreakMinutes += durationMinutes;
        }
        else
        {
            IdleMinutes += durationMinutes;
        }

        RecalculateMetrics();
    }

    /// <summary>
    /// Adjusts shift parameters (e.g. overtime or shortened shift).
    /// </summary>
    public void SetShiftDuration(decimal totalShiftMinutes, decimal scheduledBreakMinutes)
    {
        TotalShiftMinutes = Math.Max(0m, totalShiftMinutes);
        ScheduledBreakMinutes = Math.Max(0m, scheduledBreakMinutes);
        RecalculateMetrics();
    }

    /// <summary>
    /// Dynamically evaluates all unconflated throughput rates, efficiency %, utilization %, and performance tier.
    /// </summary>
    public void RecalculateMetrics()
    {
        var rawActualHours = ProductiveWorkMinutes / 60m;
        var rawStdHours = StandardLaborHours;

        // Efficiency % = (StandardLaborHours / ActualLaborHours) * 100
        if (rawActualHours > 0m)
        {
            LaborEfficiencyPercent = Math.Round((rawStdHours / rawActualHours) * 100m, 2, MidpointRounding.AwayFromZero);
            UnitsPerHour = Math.Round(UnitsCompleted / rawActualHours, 2, MidpointRounding.AwayFromZero);
            LinesPerHour = Math.Round(LinesCompleted / rawActualHours, 2, MidpointRounding.AwayFromZero);
            CartonsPerHour = Math.Round(CartonsCompleted / rawActualHours, 2, MidpointRounding.AwayFromZero);
            TasksPerHour = Math.Round(TasksCompleted / rawActualHours, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            LaborEfficiencyPercent = 0m;
            UnitsPerHour = 0m;
            LinesPerHour = 0m;
            CartonsPerHour = 0m;
            TasksPerHour = 0m;
        }

        ActualLaborHours = Math.Round(rawActualHours, 4, MidpointRounding.AwayFromZero);
        StandardLaborHours = Math.Round(rawStdHours, 4, MidpointRounding.AwayFromZero);

        // Utilization % = ProductiveWorkMinutes / (TotalShiftMinutes - ScheduledBreakMinutes) * 100
        var netAvailableShiftMinutes = Math.Max(0m, TotalShiftMinutes - ScheduledBreakMinutes);
        if (netAvailableShiftMinutes > 0m)
        {
            LaborUtilizationPercent = Math.Round((ProductiveWorkMinutes / netAvailableShiftMinutes) * 100m, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            LaborUtilizationPercent = 0m;
        }

        // Performance Tier
        if (LaborEfficiencyPercent >= 120.0m)
        {
            PerformanceTier = LaborPerformanceTier.Exceptional;
        }
        else if (LaborEfficiencyPercent >= 95.0m)
        {
            PerformanceTier = LaborPerformanceTier.Standard;
        }
        else if (LaborEfficiencyPercent >= 80.0m)
        {
            PerformanceTier = LaborPerformanceTier.Developing;
        }
        else
        {
            PerformanceTier = LaborPerformanceTier.Underperforming;
        }

        // Financials
        if (HourlyWageRate > 0m)
        {
            TotalLaborCost = Math.Round(ActualLaborHours * HourlyWageRate, 2, MidpointRounding.AwayFromZero);
            LaborCostPerUnit = UnitsCompleted > 0 ? Math.Round(TotalLaborCost / UnitsCompleted, 4, MidpointRounding.AwayFromZero) : 0m;
            LaborCostPerTask = TasksCompleted > 0 ? Math.Round(TotalLaborCost / TasksCompleted, 4, MidpointRounding.AwayFromZero) : 0m;
        }
        else
        {
            TotalLaborCost = 0m;
            LaborCostPerUnit = 0m;
            LaborCostPerTask = 0m;
        }
    }
}
