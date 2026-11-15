using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Aggregated read model projection tracking carrier SLA delivery performance, transit times, and exception rates.
/// Sourced from Phase 4.4 and Phase 4.5 carrier tracking, proof of delivery, and exception ledgers.
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation & KPI Engine.
/// </summary>
public class DailyCarrierPerformanceRecord : AuditableEntity<Guid>
{
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;
    public Guid FacilityId { get; set; }
    public Guid? ClientId { get; set; }
    public DateTime DateUtc { get; set; }
    public TimeGrain TimeGrain { get; set; } = TimeGrain.Daily;

    // Shipment Volume & Status
    public int DispatchedShipmentsCount { get; set; }
    public int DeliveredShipmentsCount { get; set; }
    public int InTransitShipmentsCount { get; set; }
    public int OverdueInTransitCount { get; set; }
    public int CompletedDeliveryOutcomesCount { get; set; }

    // Delivery SLA Performance
    public int OnTimeDeliveredCount { get; set; }
    public int LateDeliveredCount { get; set; }
    public int FirstAttemptDeliveredCount { get; set; }
    public int DeliveryExceptionsCount { get; set; }

    // Rate Metrics (Nullable to represent explicit unavailable/undefined state when denominator is zero)
    public decimal? OnTimeDeliveryRatePercent { get; set; }
    public decimal? FirstAttemptDeliveryRatePercent { get; set; }
    public decimal? DeliveryExceptionRatePercent { get; set; }

    // Transit Time
    public decimal TotalTransitDaysSum { get; set; }
    public decimal? AverageTransitDays { get; set; }

    public DailyCarrierPerformanceRecord()
    {
        Id = Guid.NewGuid();
    }

    public static DailyCarrierPerformanceRecord Create(
        string carrierCode,
        CarrierServiceLevel serviceLevel,
        Guid facilityId,
        DateTime dateUtc,
        TimeGrain timeGrain = TimeGrain.Daily,
        Guid? clientId = null)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            throw new ValidationException(nameof(carrierCode), "Carrier code is required.");
        }

        if (facilityId == Guid.Empty)
        {
            throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        }

        return new DailyCarrierPerformanceRecord
        {
            Id = Guid.NewGuid(),
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            CarrierServiceLevel = serviceLevel,
            FacilityId = facilityId,
            ClientId = clientId,
            DateUtc = dateUtc.Kind == DateTimeKind.Utc ? dateUtc : DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc),
            TimeGrain = timeGrain,
            DispatchedShipmentsCount = 0,
            DeliveredShipmentsCount = 0,
            InTransitShipmentsCount = 0,
            OverdueInTransitCount = 0,
            CompletedDeliveryOutcomesCount = 0,
            OnTimeDeliveredCount = 0,
            LateDeliveredCount = 0,
            FirstAttemptDeliveredCount = 0,
            DeliveryExceptionsCount = 0,
            OnTimeDeliveryRatePercent = null,
            FirstAttemptDeliveryRatePercent = null,
            DeliveryExceptionRatePercent = null,
            TotalTransitDaysSum = 0m,
            AverageTransitDays = null
        };
    }

    public void RecordDispatch(int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Dispatch count cannot be negative.");
        DispatchedShipmentsCount += count;
        InTransitShipmentsCount += count;
        RecalculateRates();
    }

    public void RecordDelivery(bool isOnTime, bool isFirstAttempt, decimal transitDays)
    {
        if (transitDays < 0m) throw new ValidationException(nameof(transitDays), "Transit days cannot be negative.");

        DeliveredShipmentsCount++;
        CompletedDeliveryOutcomesCount++;
        if (InTransitShipmentsCount > 0)
        {
            InTransitShipmentsCount--;
        }

        if (isOnTime)
        {
            OnTimeDeliveredCount++;
        }
        else
        {
            LateDeliveredCount++;
        }

        if (isFirstAttempt)
        {
            FirstAttemptDeliveredCount++;
        }

        TotalTransitDaysSum += transitDays;
        RecalculateRates();
    }

    public void RecordException(int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Exception count cannot be negative.");
        DeliveryExceptionsCount += count;
        RecalculateRates();
    }

    public void RecalculateRates()
    {
        // 1. OTDR (%): strictly over delivered shipments; returns null if no deliveries
        OnTimeDeliveryRatePercent = DeliveredShipmentsCount > 0
            ? Math.Round((decimal)OnTimeDeliveredCount / DeliveredShipmentsCount * 100m, 2)
            : null;

        // 2. FADR (%): strictly over completed delivery outcomes (delivered + returned); returns null if no completed outcomes
        FirstAttemptDeliveryRatePercent = CompletedDeliveryOutcomesCount > 0
            ? Math.Round((decimal)FirstAttemptDeliveredCount / CompletedDeliveryOutcomesCount * 100m, 2)
            : null;

        // 3. Exception Rate (%): over dispatched shipments; returns null if no dispatches
        DeliveryExceptionRatePercent = DispatchedShipmentsCount > 0
            ? Math.Round((decimal)DeliveryExceptionsCount / DispatchedShipmentsCount * 100m, 2)
            : null;

        // 4. Average Transit Days: over delivered shipments; returns null if no deliveries
        AverageTransitDays = DeliveredShipmentsCount > 0
            ? Math.Round(TotalTransitDaysSum / DeliveredShipmentsCount, 2)
            : null;
    }
}

