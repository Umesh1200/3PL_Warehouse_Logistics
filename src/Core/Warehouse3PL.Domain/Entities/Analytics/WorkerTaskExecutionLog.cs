using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Fine-grained execution log capturing task-level standard vs. actual labor duration and variance.
/// DB-SPEC-3PL-2026 Phase 6.2 Labor, Productivity & Workforce Pacing Analytics.
/// </summary>
public class WorkerTaskExecutionLog : AuditableEntity<Guid>
{
    public Guid TaskId { get; set; }
    public WarehouseLaborActivityType ActivityType { get; set; }
    public Guid WorkerUserId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid? ZoneId { get; set; }
    public string Department { get; set; } = string.Empty;

    public int UnitsProcessed { get; set; }
    public int LinesProcessed { get; set; }
    public int CartonsProcessed { get; set; }
    public int PalletsProcessed { get; set; }
    public decimal TravelDistanceMeters { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }

    public decimal ActualDurationSeconds { get; set; }
    public decimal StandardDurationSeconds { get; set; }
    public Guid? StandardProfileId { get; set; }
    public int StandardProfileVersion { get; set; } = 1;

    /// <summary>
    /// Variance in seconds: ActualDurationSeconds - StandardDurationSeconds.
    /// Negative indicates ahead of standard (favorable); positive indicates behind standard (unfavorable).
    /// </summary>
    public decimal VarianceSeconds { get; set; }

    /// <summary>
    /// Labor Efficiency % = (StandardDurationSeconds / ActualDurationSeconds) * 100.
    /// </summary>
    public decimal EfficiencyPercent { get; set; }

    public LaborComplexityFactor Complexity { get; set; } = LaborComplexityFactor.Standard;
    public WarehouseEquipmentType Equipment { get; set; } = WarehouseEquipmentType.ManualCart;

    public decimal HourlyWageRate { get; set; }
    public decimal CalculatedLaborCost { get; set; }

    public WorkerTaskExecutionLog()
    {
        Id = Guid.NewGuid();
    }

    public static WorkerTaskExecutionLog Create(
        Guid taskId,
        WarehouseLaborActivityType activityType,
        Guid workerUserId,
        Guid facilityId,
        string department,
        int units,
        int lines,
        int cartons,
        int pallets,
        DateTime startedAtUtc,
        DateTime completedAtUtc,
        decimal standardDurationSeconds,
        decimal hourlyWageRate = 0m,
        LaborComplexityFactor complexity = LaborComplexityFactor.Standard,
        WarehouseEquipmentType equipment = WarehouseEquipmentType.ManualCart,
        Guid? zoneId = null,
        decimal travelDistanceMeters = 0m,
        Guid? standardProfileId = null,
        int standardProfileVersion = 1)
    {
        if (taskId == Guid.Empty || workerUserId == Guid.Empty || facilityId == Guid.Empty)
            throw new InvalidDomainStateException("TaskId, WorkerUserId, and FacilityId are required.");

        if (startedAtUtc == default || completedAtUtc == default)
            throw new InvalidDomainStateException("Task started and completed timestamps must be valid UTC timestamps.");

        if (completedAtUtc < startedAtUtc)
            throw new InvalidDomainStateException("CompletedAtUtc cannot be earlier than StartedAtUtc.");

        var rawActualSeconds = (decimal)(completedAtUtc - startedAtUtc).TotalSeconds;
        var actualSeconds = Math.Max(1.0m, Math.Round(rawActualSeconds, 2, MidpointRounding.AwayFromZero));
        var standardSeconds = Math.Max(1.0m, Math.Round(standardDurationSeconds, 2, MidpointRounding.AwayFromZero));

        var variance = Math.Round(actualSeconds - standardSeconds, 2, MidpointRounding.AwayFromZero);
        var efficiency = Math.Round((standardSeconds / actualSeconds) * 100m, 2, MidpointRounding.AwayFromZero);
        var laborCost = Math.Round((actualSeconds / 3600m) * Math.Max(0m, hourlyWageRate), 4, MidpointRounding.AwayFromZero);

        return new WorkerTaskExecutionLog
        {
            TaskId = taskId,
            ActivityType = activityType,
            WorkerUserId = workerUserId,
            FacilityId = facilityId,
            ZoneId = zoneId,
            Department = string.IsNullOrWhiteSpace(department) ? activityType.ToString() : department.Trim(),
            UnitsProcessed = Math.Max(0, units),
            LinesProcessed = Math.Max(0, lines),
            CartonsProcessed = Math.Max(0, cartons),
            PalletsProcessed = Math.Max(0, pallets),
            TravelDistanceMeters = Math.Max(0m, travelDistanceMeters),
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            ActualDurationSeconds = actualSeconds,
            StandardDurationSeconds = standardSeconds,
            StandardProfileId = standardProfileId,
            StandardProfileVersion = Math.Max(1, standardProfileVersion),
            VarianceSeconds = variance,
            EfficiencyPercent = efficiency,
            Complexity = complexity,
            Equipment = equipment,
            HourlyWageRate = Math.Max(0m, hourlyWageRate),
            CalculatedLaborCost = laborCost
        };
    }
}

