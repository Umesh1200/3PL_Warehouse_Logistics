using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Aggregated read model summarizing transportation transit times, P95 duration, and on-time percentages by shipping lane.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public class LaneTransitSummary : AuditableEntity<Guid>
{
    public Guid OriginFacilityId { get; set; }
    public string OriginRegion { get; set; } = string.Empty;

    public string DestinationCountry { get; set; } = "US";
    public string DestinationStateOrRegion { get; set; } = string.Empty;

    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;

    public DateTime DateUtc { get; set; }
    public TimeGrain TimeGrain { get; set; } = TimeGrain.Daily;

    // Volume
    public int DispatchedShipmentsCount { get; set; }
    public int DeliveredShipmentsCount { get; set; }

    // Transit Statistics (in Days)
    public decimal TotalTransitDaysSum { get; set; }
    public decimal? AverageTransitDays { get; set; }
    public decimal MinTransitDays { get; set; }
    public decimal MaxTransitDays { get; set; }
    public decimal P95TransitDays { get; set; }

    // SLA Adherence
    public int OnTimeDeliveredCount { get; set; }
    public decimal? OnTimeDeliveryRatePercent { get; set; }

    public LaneTransitSummary()
    {
        Id = Guid.NewGuid();
    }

    public static LaneTransitSummary Create(
        Guid originFacilityId,
        string destinationCountry,
        string destinationStateOrRegion,
        string carrierCode,
        CarrierServiceLevel serviceLevel,
        DateTime dateUtc,
        TimeGrain timeGrain = TimeGrain.Daily,
        string originRegion = "")
    {
        if (originFacilityId == Guid.Empty) throw new ValidationException(nameof(originFacilityId), "Origin facility ID is required.");
        if (string.IsNullOrWhiteSpace(carrierCode)) throw new ValidationException(nameof(carrierCode), "Carrier code is required.");

        return new LaneTransitSummary
        {
            Id = Guid.NewGuid(),
            OriginFacilityId = originFacilityId,
            OriginRegion = originRegion?.Trim().ToUpperInvariant() ?? string.Empty,
            DestinationCountry = string.IsNullOrWhiteSpace(destinationCountry) ? "US" : destinationCountry.Trim().ToUpperInvariant(),
            DestinationStateOrRegion = destinationStateOrRegion?.Trim().ToUpperInvariant() ?? string.Empty,
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            CarrierServiceLevel = serviceLevel,
            DateUtc = dateUtc.Kind == DateTimeKind.Utc ? dateUtc : DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc),
            TimeGrain = timeGrain,
            DispatchedShipmentsCount = 0,
            DeliveredShipmentsCount = 0,
            TotalTransitDaysSum = 0m,
            AverageTransitDays = null,
            MinTransitDays = 0m,
            MaxTransitDays = 0m,
            P95TransitDays = 0m,
            OnTimeDeliveredCount = 0,
            OnTimeDeliveryRatePercent = null
        };
    }

    public void RecordDispatch(int count = 1)
    {
        if (count < 0) throw new ValidationException(nameof(count), "Dispatch count cannot be negative.");
        DispatchedShipmentsCount += count;
    }

    public void RecordDelivery(decimal transitDays, bool isOnTime)
    {
        if (transitDays < 0m) throw new ValidationException(nameof(transitDays), "Transit days cannot be negative.");

        DeliveredShipmentsCount++;
        TotalTransitDaysSum += transitDays;

        if (DeliveredShipmentsCount == 1)
        {
            MinTransitDays = transitDays;
            MaxTransitDays = transitDays;
        }
        else
        {
            if (transitDays < MinTransitDays) MinTransitDays = transitDays;
            if (transitDays > MaxTransitDays) MaxTransitDays = transitDays;
        }

        if (isOnTime)
        {
            OnTimeDeliveredCount++;
        }

        Recalculate();
    }

    public void Recalculate()
    {
        if (DeliveredShipmentsCount > 0)
        {
            AverageTransitDays = Math.Round(TotalTransitDaysSum / DeliveredShipmentsCount, 2);
            OnTimeDeliveryRatePercent = Math.Round((decimal)OnTimeDeliveredCount / DeliveredShipmentsCount * 100m, 2);
            // P95 estimation
            P95TransitDays = Math.Round(MaxTransitDays * 0.95m + (AverageTransitDays.Value * 0.05m), 2);
        }
        else
        {
            AverageTransitDays = null;
            OnTimeDeliveryRatePercent = null;
            P95TransitDays = 0m;
        }
    }
}
