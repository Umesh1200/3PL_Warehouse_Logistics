using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Aggregated read-model projection tracking hourly warehouse fulfillment throughput and labor pacing.
/// Bucketed by hour (HH:00:00 to HH:59:59 UTC).
/// DB-SPEC-3PL-2026 Phase 6.1 Executive Control Tower &amp; Real-Time Visibility.
/// </summary>
public class HourlyFacilityThroughputSnapshot : AuditableEntity<Guid>
{
    public Guid FacilityId { get; set; }
    public DateTime HourTimestampUtc { get; set; }

    // Un-conflated Throughput Milestones
    public int OrdersReceivedCount { get; set; }
    public int OrdersAllocatedCount { get; set; }
    public int LinesPickedCount { get; set; }
    public int UnitsPickedCount { get; set; }
    public int CartonsPackedCount { get; set; }
    public int OrdersDispatchedCount { get; set; }
    public int PackagesDispatchedCount { get; set; }
    public int InboundPalletsReceivedCount { get; set; }
    public int InboundPutawayCompletedCount { get; set; }
    public int ReturnsInspectedCount { get; set; }

    // Direct Labor & Pacing
    public int ActiveWorkerHeadcount { get; set; }
    public decimal TotalLaborHours { get; set; }
    public decimal PickDurationHours { get; set; }
    public decimal PutawayDurationHours { get; set; }
    public decimal UnitsPerLaborHour { get; set; }
    public decimal PutawayPerLaborHour { get; set; }

    // Cycle Time & Cutoff Velocity
    public decimal AverageCycleTimeHours { get; set; }
    public int TotalCycleOrdersCount { get; set; }
    public decimal TotalCycleHoursSum { get; set; }
    public int EligibleCutoffCount { get; set; }
    public int SameDayDispatchedCount { get; set; }
    public decimal SameDayDispatchAdherencePercent { get; set; }

    public int ProjectionVersion { get; set; } = 1;

    public HourlyFacilityThroughputSnapshot()
    {
        Id = Guid.NewGuid();
    }

    public static HourlyFacilityThroughputSnapshot Create(
        Guid facilityId,
        DateTime timestampUtc,
        int projectionVersion = 1)
    {
        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        }

        var normalizedUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        var hourBucket = new DateTime(normalizedUtc.Year, normalizedUtc.Month, normalizedUtc.Day, normalizedUtc.Hour, 0, 0, DateTimeKind.Utc);

        return new HourlyFacilityThroughputSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            HourTimestampUtc = hourBucket,
            ProjectionVersion = projectionVersion
        };
    }

    public void RecordOrderReceived(int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Count cannot be negative.");
        OrdersReceivedCount += count;
    }

    public void RecordOrderAllocated(int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Count cannot be negative.");
        OrdersAllocatedCount += count;
    }

    public void RecordPickCompleted(int lines, int units, decimal durationHours = 0m)
    {
        if (lines < 0) throw new ValidationException(nameof(lines), "Lines cannot be negative.");
        if (units < 0) throw new ValidationException(nameof(units), "Units cannot be negative.");
        if (durationHours < 0m) throw new ValidationException(nameof(durationHours), "Duration cannot be negative.");

        LinesPickedCount += lines;
        UnitsPickedCount += units;
        PickDurationHours += durationHours;
        TotalLaborHours += durationHours;
        RecalculateRates();
    }

    public void RecordPackCompleted(int cartons)
    {
        if (cartons < 0) throw new ValidationException(nameof(cartons), "Cartons cannot be negative.");
        CartonsPackedCount += cartons;
    }

    public void RecordOrderDispatched(
        int packages = 1,
        decimal cycleTimeHours = 0m,
        bool eligibleCutoff = false,
        bool sameDayDispatched = false)
    {
        if (packages < 0) throw new ValidationException(nameof(packages), "Packages cannot be negative.");
        if (cycleTimeHours < 0m) throw new ValidationException(nameof(cycleTimeHours), "Cycle time cannot be negative.");

        OrdersDispatchedCount++;
        PackagesDispatchedCount += packages;

        if (cycleTimeHours > 0m)
        {
            TotalCycleOrdersCount++;
            TotalCycleHoursSum += cycleTimeHours;
            AverageCycleTimeHours = Math.Round(TotalCycleHoursSum / TotalCycleOrdersCount, 2);
        }

        if (eligibleCutoff)
        {
            EligibleCutoffCount++;
            if (sameDayDispatched)
            {
                SameDayDispatchedCount++;
            }
            SameDayDispatchAdherencePercent = Math.Round((decimal)SameDayDispatchedCount / EligibleCutoffCount * 100m, 2);
        }
    }

    public void RecordInboundReceived(int pallets = 1)
    {
        if (pallets < 0) throw new ValidationException(nameof(pallets), "Pallets cannot be negative.");
        InboundPalletsReceivedCount += pallets;
    }

    public void RecordPutawayCompleted(int pallets = 1, decimal durationHours = 0m)
    {
        if (pallets < 0) throw new ValidationException(nameof(pallets), "Pallets cannot be negative.");
        if (durationHours < 0m) throw new ValidationException(nameof(durationHours), "Duration cannot be negative.");

        InboundPutawayCompletedCount += pallets;
        PutawayDurationHours += durationHours;
        TotalLaborHours += durationHours;
        RecalculateRates();
    }

    public void RecordReturnInspected(int items = 1)
    {
        if (items < 0) throw new ValidationException(nameof(items), "Items cannot be negative.");
        ReturnsInspectedCount += items;
    }

    public void UpdateLaborPacing(int workerHeadcount, decimal additionalLaborHours)
    {
        if (workerHeadcount < 0) throw new ValidationException(nameof(workerHeadcount), "Headcount cannot be negative.");
        if (additionalLaborHours < 0m) throw new ValidationException(nameof(additionalLaborHours), "Hours cannot be negative.");

        if (workerHeadcount > ActiveWorkerHeadcount)
        {
            ActiveWorkerHeadcount = workerHeadcount;
        }

        TotalLaborHours += additionalLaborHours;
        RecalculateRates();
    }

    public void RecalculateRates()
    {
        UnitsPerLaborHour = PickDurationHours > 0m
            ? Math.Round((decimal)UnitsPickedCount / PickDurationHours, 2)
            : (TotalLaborHours > 0m ? Math.Round((decimal)UnitsPickedCount / TotalLaborHours, 2) : 0m);

        PutawayPerLaborHour = PutawayDurationHours > 0m
            ? Math.Round((decimal)InboundPutawayCompletedCount / PutawayDurationHours, 2)
            : 0m;

        if (TotalCycleOrdersCount > 0)
        {
            AverageCycleTimeHours = Math.Round(TotalCycleHoursSum / TotalCycleOrdersCount, 2);
        }

        if (EligibleCutoffCount > 0)
        {
            SameDayDispatchAdherencePercent = Math.Round((decimal)SameDayDispatchedCount / EligibleCutoffCount * 100m, 2);
        }
    }
}

