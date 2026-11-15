using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Aggregated read model summarizing delivery exception frequency and resolution status by carrier and category.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public class CarrierExceptionSummary : AuditableEntity<Guid>
{
    public string CarrierCode { get; set; } = string.Empty;
    public Guid FacilityId { get; set; }
    public Guid? ClientId { get; set; }

    public DateTime DateUtc { get; set; }
    public TimeGrain TimeGrain { get; set; } = TimeGrain.Daily;

    public CanonicalDeliveryExceptionCategory ExceptionCategory { get; set; } = CanonicalDeliveryExceptionCategory.Other;

    // Counts
    public int TotalExceptionsCount { get; set; }
    public int ResolvedDeliveredCount { get; set; }
    public int PendingResolutionCount { get; set; }
    public int ReturnedToOriginCount { get; set; }
    public int LostClaimRecommendedCount { get; set; }

    public CarrierExceptionSummary()
    {
        Id = Guid.NewGuid();
    }

    public static CarrierExceptionSummary Create(
        string carrierCode,
        Guid facilityId,
        CanonicalDeliveryExceptionCategory exceptionCategory,
        DateTime dateUtc,
        TimeGrain timeGrain = TimeGrain.Daily,
        Guid? clientId = null)
    {
        if (string.IsNullOrWhiteSpace(carrierCode)) throw new ValidationException(nameof(carrierCode), "Carrier code is required.");
        if (facilityId == Guid.Empty) throw new ValidationException(nameof(facilityId), "Facility ID is required.");

        return new CarrierExceptionSummary
        {
            Id = Guid.NewGuid(),
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            FacilityId = facilityId,
            ClientId = clientId,
            ExceptionCategory = exceptionCategory,
            DateUtc = dateUtc.Kind == DateTimeKind.Utc ? dateUtc : DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc),
            TimeGrain = timeGrain,
            TotalExceptionsCount = 0,
            ResolvedDeliveredCount = 0,
            PendingResolutionCount = 0,
            ReturnedToOriginCount = 0,
            LostClaimRecommendedCount = 0
        };
    }

    public void RecordException(bool isPending = true, bool isLostAlarm = false)
    {
        TotalExceptionsCount++;
        if (isPending) PendingResolutionCount++;
        if (isLostAlarm) LostClaimRecommendedCount++;
    }

    public void RecordResolution(bool wasDelivered, bool wasReturned)
    {
        if (PendingResolutionCount > 0) PendingResolutionCount--;
        if (wasDelivered) ResolvedDeliveredCount++;
        if (wasReturned) ReturnedToOriginCount++;
    }
}
