using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Aggregated read-model projection tracking facility dock door utilization, yard occupancy,
/// truck waiting duration, dock service dwell time, and gate-to-gate total turnaround time.
/// DB-SPEC-3PL-2026 Phase 6.1 Executive Control Tower &amp; Real-Time Visibility.
/// </summary>
public class FacilityDockYardSnapshot : AuditableEntity<Guid>
{
    public Guid FacilityId { get; set; }
    public DateTime DateUtc { get; set; }
    public TimeGrain TimeGrain { get; set; } = TimeGrain.Daily;

    // Physical Capacity & Utilization
    public int TotalDockDoorsCount { get; set; }
    public int OccupiedDockDoorsCount { get; set; }
    public decimal DockDoorUtilizationPercent { get; set; }

    public int TotalYardSpotsCount { get; set; }
    public int OccupiedYardSpotsCount { get; set; }
    public decimal YardOccupancyPercent { get; set; }

    // Live Vehicle Pacing Counts
    public int TrucksCheckedInCount { get; set; }
    public int TrucksDepartedCount { get; set; }
    public int TrucksWaitingInYardCount { get; set; }
    public int TrucksAtDockCount { get; set; }

    // Precision Turn-Time Accumulators & Averages
    public decimal TotalYardWaitMinutesSum { get; set; }
    public int TotalYardWaitTrucksCount { get; set; }
    public decimal AverageYardWaitTimeMinutes { get; set; }

    public decimal TotalDockServiceMinutesSum { get; set; }
    public int TotalDockServiceTrucksCount { get; set; }
    public decimal AverageDockServiceTimeMinutes { get; set; }

    public decimal TotalTurnTimeMinutesSum { get; set; }
    public int TotalDepartedTrucksCount { get; set; }
    public decimal AverageTotalTurnTimeMinutes { get; set; }

    public int ProjectionVersion { get; set; } = 1;

    public FacilityDockYardSnapshot()
    {
        Id = Guid.NewGuid();
    }

    public static FacilityDockYardSnapshot Create(
        Guid facilityId,
        DateTime dateUtc,
        TimeGrain timeGrain = TimeGrain.Daily,
        int projectionVersion = 1)
    {
        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        }

        var normalizedUtc = dateUtc.Kind == DateTimeKind.Utc ? dateUtc : DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc);

        return new FacilityDockYardSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            DateUtc = normalizedUtc,
            TimeGrain = timeGrain,
            ProjectionVersion = projectionVersion
        };
    }

    public void UpdateCapacity(int totalDoors, int occupiedDoors, int totalYardSpots, int occupiedYardSpots)
    {
        if (totalDoors < 0) throw new ValidationException(nameof(totalDoors), "Total doors cannot be negative.");
        if (occupiedDoors < 0) throw new ValidationException(nameof(occupiedDoors), "Occupied doors cannot be negative.");
        if (occupiedDoors > totalDoors && totalDoors > 0) throw new ValidationException(nameof(occupiedDoors), "Occupied doors cannot exceed total doors.");
        if (totalYardSpots < 0) throw new ValidationException(nameof(totalYardSpots), "Total yard spots cannot be negative.");
        if (occupiedYardSpots < 0) throw new ValidationException(nameof(occupiedYardSpots), "Occupied spots cannot be negative.");
        if (occupiedYardSpots > totalYardSpots && totalYardSpots > 0) throw new ValidationException(nameof(occupiedYardSpots), "Occupied spots cannot exceed total spots.");

        TotalDockDoorsCount = totalDoors;
        OccupiedDockDoorsCount = occupiedDoors;
        TotalYardSpotsCount = totalYardSpots;
        OccupiedYardSpotsCount = occupiedYardSpots;

        RecalculatePercentages();
    }

    public void RecordTruckCheckIn(int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Count cannot be negative.");
        TrucksCheckedInCount += count;
        TrucksWaitingInYardCount += count;
    }

    public void RecordTruckSpottedAtDock(decimal yardWaitMinutes, int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Count cannot be negative.");
        if (yardWaitMinutes < 0m) throw new ValidationException(nameof(yardWaitMinutes), "Wait minutes cannot be negative.");

        if (TrucksWaitingInYardCount >= count)
        {
            TrucksWaitingInYardCount -= count;
        }
        TrucksAtDockCount += count;

        TotalYardWaitMinutesSum += yardWaitMinutes;
        TotalYardWaitTrucksCount += count;
        AverageYardWaitTimeMinutes = TotalYardWaitTrucksCount > 0
            ? Math.Round(TotalYardWaitMinutesSum / TotalYardWaitTrucksCount, 2)
            : 0m;
    }

    public void RecordDockReleased(decimal dockServiceMinutes, int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Count cannot be negative.");
        if (dockServiceMinutes < 0m) throw new ValidationException(nameof(dockServiceMinutes), "Service minutes cannot be negative.");

        if (TrucksAtDockCount >= count)
        {
            TrucksAtDockCount -= count;
        }

        TotalDockServiceMinutesSum += dockServiceMinutes;
        TotalDockServiceTrucksCount += count;
        AverageDockServiceTimeMinutes = TotalDockServiceTrucksCount > 0
            ? Math.Round(TotalDockServiceMinutesSum / TotalDockServiceTrucksCount, 2)
            : 0m;
    }

    public void RecordTruckDeparture(decimal totalTurnTimeMinutes, int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Count cannot be negative.");
        if (totalTurnTimeMinutes < 0m) throw new ValidationException(nameof(totalTurnTimeMinutes), "Turn time cannot be negative.");

        TrucksDepartedCount += count;
        TotalTurnTimeMinutesSum += totalTurnTimeMinutes;
        TotalDepartedTrucksCount += count;
        AverageTotalTurnTimeMinutes = TotalDepartedTrucksCount > 0
            ? Math.Round(TotalTurnTimeMinutesSum / TotalDepartedTrucksCount, 2)
            : 0m;
    }

    public void RecalculatePercentages()
    {
        DockDoorUtilizationPercent = TotalDockDoorsCount > 0
            ? Math.Round((decimal)OccupiedDockDoorsCount / TotalDockDoorsCount * 100m, 2)
            : 0m;

        YardOccupancyPercent = TotalYardSpotsCount > 0
            ? Math.Round((decimal)OccupiedYardSpotsCount / TotalYardSpotsCount * 100m, 2)
            : 0m;

        if (TotalYardWaitTrucksCount > 0)
        {
            AverageYardWaitTimeMinutes = Math.Round(TotalYardWaitMinutesSum / TotalYardWaitTrucksCount, 2);
        }

        if (TotalDockServiceTrucksCount > 0)
        {
            AverageDockServiceTimeMinutes = Math.Round(TotalDockServiceMinutesSum / TotalDockServiceTrucksCount, 2);
        }

        if (TotalDepartedTrucksCount > 0)
        {
            AverageTotalTurnTimeMinutes = Math.Round(TotalTurnTimeMinutesSum / TotalDepartedTrucksCount, 2);
        }
    }
}

