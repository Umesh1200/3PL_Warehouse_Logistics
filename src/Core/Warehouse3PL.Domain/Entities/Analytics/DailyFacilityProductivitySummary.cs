using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Aggregated read model projection tracking facility-level operational throughput, labor pacing, and fulfillment velocity.
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation & KPI Engine.
/// </summary>
public class DailyFacilityProductivitySummary : AuditableEntity<Guid>
{
    public Guid FacilityId { get; set; }
    public DateTime DateUtc { get; set; }
    public TimeGrain TimeGrain { get; set; } = TimeGrain.Daily;

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
    public decimal TotalPickHours { get; set; }
    public decimal TotalPutawayHours { get; set; }
    public decimal UnitsPerLaborHour { get; set; }
    public decimal PutawayPerLaborHour { get; set; }

    // Fulfillment Velocity & SLA
    public decimal AverageFulfillmentCycleTimeHours { get; set; }
    public int TotalCycleTimeOrdersCount { get; set; }
    public decimal TotalCycleTimeHoursSum { get; set; }
    public int EligibleOrdersCutoffCount { get; set; }
    public int SameDayDispatchedCutoffCount { get; set; }
    public decimal SameDayDispatchAdherencePercent { get; set; }

    public DailyFacilityProductivitySummary()
    {
        Id = Guid.NewGuid();
    }

    public static DailyFacilityProductivitySummary Create(
        Guid facilityId,
        DateTime dateUtc,
        TimeGrain timeGrain = TimeGrain.Daily)
    {
        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        }

        return new DailyFacilityProductivitySummary
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            DateUtc = dateUtc.Kind == DateTimeKind.Utc ? dateUtc : DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc),
            TimeGrain = timeGrain,
            OrdersReceivedCount = 0,
            OrdersAllocatedCount = 0,
            LinesPickedCount = 0,
            UnitsPickedCount = 0,
            CartonsPackedCount = 0,
            OrdersDispatchedCount = 0,
            PackagesDispatchedCount = 0,
            InboundPalletsReceivedCount = 0,
            InboundPutawayCompletedCount = 0,
            ReturnsInspectedCount = 0,
            ActiveWorkerHeadcount = 0,
            TotalLaborHours = 0m,
            TotalPickHours = 0m,
            TotalPutawayHours = 0m,
            UnitsPerLaborHour = 0m,
            PutawayPerLaborHour = 0m,
            AverageFulfillmentCycleTimeHours = 0m,
            TotalCycleTimeOrdersCount = 0,
            TotalCycleTimeHoursSum = 0m,
            EligibleOrdersCutoffCount = 0,
            SameDayDispatchedCutoffCount = 0,
            SameDayDispatchAdherencePercent = 0m
        };
    }

    public void RecordOrderReceived(int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Received count cannot be negative.");
        OrdersReceivedCount += count;
    }

    public void RecordOrderAllocated(int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Allocated count cannot be negative.");
        OrdersAllocatedCount += count;
    }

    public void RecordPickCompleted(int lines, int units, decimal pickDurationHours = 0m)
    {
        if (lines < 0) throw new ValidationException(nameof(lines), "Lines picked cannot be negative.");
        if (units < 0) throw new ValidationException(nameof(units), "Units picked cannot be negative.");
        if (pickDurationHours < 0m) throw new ValidationException(nameof(pickDurationHours), "Pick duration cannot be negative.");

        LinesPickedCount += lines;
        UnitsPickedCount += units;
        TotalPickHours += pickDurationHours;
        TotalLaborHours += pickDurationHours;
        RecalculateRates();
    }

    public void RecordPackCompleted(int cartons)
    {
        if (cartons < 0) throw new ValidationException(nameof(cartons), "Packed cartons cannot be negative.");
        CartonsPackedCount += cartons;
    }

    public void RecordOrderDispatched(
        int packages = 1,
        decimal cycleTimeHours = 0m,
        bool eligibleCutoff = false,
        bool sameDayDispatched = false)
    {
        if (packages < 0) throw new ValidationException(nameof(packages), "Dispatched packages cannot be negative.");
        if (cycleTimeHours < 0m) throw new ValidationException(nameof(cycleTimeHours), "Cycle time cannot be negative.");

        OrdersDispatchedCount++;
        PackagesDispatchedCount += packages;

        if (cycleTimeHours > 0m)
        {
            TotalCycleTimeOrdersCount++;
            TotalCycleTimeHoursSum += cycleTimeHours;
            AverageFulfillmentCycleTimeHours = Math.Round(TotalCycleTimeHoursSum / TotalCycleTimeOrdersCount, 2);
        }

        if (eligibleCutoff)
        {
            EligibleOrdersCutoffCount++;
            if (sameDayDispatched)
            {
                SameDayDispatchedCutoffCount++;
            }

            SameDayDispatchAdherencePercent = EligibleOrdersCutoffCount > 0
                ? Math.Round((decimal)SameDayDispatchedCutoffCount / EligibleOrdersCutoffCount * 100m, 2)
                : 0m;
        }
    }

    public void RecordInboundReceived(int pallets = 1)
    {
        if (pallets < 0) throw new ValidationException(nameof(pallets), "Pallets received cannot be negative.");
        InboundPalletsReceivedCount += pallets;
    }

    public void RecordPutawayCompleted(int pallets = 1, decimal putawayDurationHours = 0m)
    {
        if (pallets < 0) throw new ValidationException(nameof(pallets), "Pallets putaway cannot be negative.");
        if (putawayDurationHours < 0m) throw new ValidationException(nameof(putawayDurationHours), "Putaway duration cannot be negative.");

        InboundPutawayCompletedCount += pallets;
        TotalPutawayHours += putawayDurationHours;
        TotalLaborHours += putawayDurationHours;
        RecalculateRates();
    }

    public void RecordReturnInspected(int items = 1)
    {
        if (items < 0) throw new ValidationException(nameof(items), "Inspected return count cannot be negative.");
        ReturnsInspectedCount += items;
    }

    public void UpdateLaborPacing(int workerHeadcount, decimal additionalLaborHours)
    {
        if (workerHeadcount < 0) throw new ValidationException(nameof(workerHeadcount), "Worker headcount cannot be negative.");
        if (additionalLaborHours < 0m) throw new ValidationException(nameof(additionalLaborHours), "Labor hours cannot be negative.");

        if (workerHeadcount > ActiveWorkerHeadcount)
        {
            ActiveWorkerHeadcount = workerHeadcount;
        }

        TotalLaborHours += additionalLaborHours;
        RecalculateRates();
    }

    public void RecalculateRates()
    {
        UnitsPerLaborHour = TotalPickHours > 0m
            ? Math.Round((decimal)UnitsPickedCount / TotalPickHours, 2)
            : (TotalLaborHours > 0m ? Math.Round((decimal)UnitsPickedCount / TotalLaborHours, 2) : 0m);

        PutawayPerLaborHour = TotalPutawayHours > 0m
            ? Math.Round((decimal)InboundPutawayCompletedCount / TotalPutawayHours, 2)
            : 0m;

        if (TotalCycleTimeOrdersCount > 0)
        {
            AverageFulfillmentCycleTimeHours = Math.Round(TotalCycleTimeHoursSum / TotalCycleTimeOrdersCount, 2);
        }

        if (EligibleOrdersCutoffCount > 0)
        {
            SameDayDispatchAdherencePercent = Math.Round((decimal)SameDayDispatchedCutoffCount / EligibleOrdersCutoffCount * 100m, 2);
        }
    }
}

