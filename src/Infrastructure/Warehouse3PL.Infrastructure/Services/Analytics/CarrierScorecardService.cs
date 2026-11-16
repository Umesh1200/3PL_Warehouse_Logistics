using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Service providing carrier performance scorecards, transit time distributions, and exception intelligence.
/// Operates strictly as a read-only analytics query service over specialized read models.
/// DB-SPEC-3PL-2026 Phase 6.4.3 Carrier Scorecards & Exception Analytics.
/// </summary>
public class CarrierScorecardService : ICarrierScorecardService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly TransportationAnalyticsAuthorization _authorization;
    private readonly IDelayAttributionService _delayAttributionService;
    private readonly ILogger<CarrierScorecardService> _logger;

    public CarrierScorecardService(
        IApplicationDbContext dbContext,
        IDelayAttributionService delayAttributionService,
        ILogger<CarrierScorecardService> logger,
        TransportationAnalyticsAuthorization authorization)
    {
        _dbContext = dbContext;
        _authorization = authorization;
        _delayAttributionService = delayAttributionService;
        _logger = logger;
    }

    public async Task<CarrierScorecardDto> GetCarrierScorecardAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        CarrierServiceLevel? serviceLevel = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
            throw new ValidationException(nameof(carrierCode), "Carrier code is required.");

        var scope = await _authorization.ResolveAsync(clientId, facilityId, cancellationToken);
        clientId = scope.ClientId;
        facilityId = scope.FacilityId;
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
        var start = startDateUtc.Date;
        var end = endDateUtc.Date.AddDays(1).AddTicks(-1);

        // 1. Query shipment-level compliance records within date window
        var complianceQuery = _dbContext.CarrierSlaComplianceRecords
            .AsNoTracking()
            .Where(r => r.CarrierCode == normalizedCarrier
                     && r.DispatchedAtUtc >= start
                     && r.DispatchedAtUtc <= end);

        if (facilityId.HasValue) complianceQuery = complianceQuery.Where(r => r.FacilityId == facilityId.Value);
        if (clientId.HasValue) complianceQuery = complianceQuery.Where(r => r.ClientId == clientId.Value);
        if (serviceLevel.HasValue) complianceQuery = complianceQuery.Where(r => r.CarrierServiceLevel == serviceLevel.Value);

        var complianceRecords = await scope.Apply(complianceQuery).ToListAsync(cancellationToken);

        // 2. Query daily performance summaries to capture aggregate volumes
        var dailyQuery = _dbContext.DailyCarrierPerformanceRecords
            .AsNoTracking()
            .Where(r => r.CarrierCode == normalizedCarrier
                     && r.DateUtc >= start.Date
                     && r.DateUtc <= end.Date
                     && r.TimeGrain == TimeGrain.Daily);

        if (facilityId.HasValue) dailyQuery = dailyQuery.Where(r => r.FacilityId == facilityId.Value);
        if (clientId.HasValue) dailyQuery = dailyQuery.Where(r => r.ClientId == clientId.Value);
        if (serviceLevel.HasValue) dailyQuery = dailyQuery.Where(r => r.CarrierServiceLevel == serviceLevel.Value);

        var dailyRecords = await scope.Apply(dailyQuery).ToListAsync(cancellationToken);

        int totalDispatched = dailyRecords.Sum(r => r.DispatchedShipmentsCount);
        if (totalDispatched == 0) totalDispatched = complianceRecords.Count;

        var deliveredRecords = complianceRecords.Where(r => r.DeliveredAtUtc.HasValue).ToList();
        int totalDelivered = deliveredRecords.Count;
        int completedOutcomes = complianceRecords.Count(r => r.DeliveredAtUtc.HasValue || r.EvaluationStatus == SlaEvaluationStatus.ExcludedFromSla);
        if (completedOutcomes == 0) completedOutcomes = totalDelivered;

        int onTimeCount = deliveredRecords.Count(r => r.IsOnTime == true);
        int firstAttemptCount = deliveredRecords.Count(r => r.IsFirstAttemptSuccess);
        int distinctExceptionsCount = complianceRecords.Count(r => r.HasDeliveryException);
        int totalExceptions = dailyRecords.Sum(r => r.DeliveryExceptionsCount);
        if (totalExceptions == 0) totalExceptions = complianceRecords.Sum(r => r.QualifyingExceptionsCount);

        // Undelivered & Overdue in-transit
        var nowUtc = DateTime.UtcNow;
        int inTransitCount = complianceRecords.Count(r => !r.DeliveredAtUtc.HasValue);
        int overdueInTransit = complianceRecords.Count(r => !r.DeliveredAtUtc.HasValue
                                                        && r.EstimatedDeliveryDateUtc.HasValue
                                                        && r.EstimatedDeliveryDateUtc.Value < nowUtc);

        // Rates (null on zero denominator)
        decimal? otdr = totalDelivered > 0 ? Math.Round((decimal)onTimeCount / totalDelivered * 100m, 2) : null;
        decimal? fadr = completedOutcomes > 0 ? Math.Round((decimal)firstAttemptCount / completedOutcomes * 100m, 2) : null;
        decimal? exRate = totalDispatched > 0 ? Math.Round((decimal)distinctExceptionsCount / totalDispatched * 100m, 2) : null;

        // Transit duration distribution from underlying observations
        var transitObservations = deliveredRecords
            .Where(r => r.ActualTransitDays.HasValue)
            .Select(r => r.ActualTransitDays!.Value)
            .OrderBy(v => v)
            .ToList();

        decimal? avgTransit = transitObservations.Count > 0 ? Math.Round(transitObservations.Average(), 2) : null;
        decimal? medianTransit = transitObservations.Count > 0 ? CalculatePercentile(transitObservations, 50m) : null;
        decimal? p75Transit = transitObservations.Count > 0 ? CalculatePercentile(transitObservations, 75m) : null;
        decimal? p90Transit = transitObservations.Count > 0 ? CalculatePercentile(transitObservations, 90m) : null;
        decimal? p95Transit = transitObservations.Count > 0 ? CalculatePercentile(transitObservations, 95m) : null;
        decimal? minTransit = transitObservations.Count > 0 ? transitObservations.Min() : null;
        decimal? maxTransit = transitObservations.Count > 0 ? transitObservations.Max() : null;
        decimal? stdDev = CalculateStandardDeviation(transitObservations);

        // Transit Time Variance (Actual Avg - Contractual Commitment Avg)
        decimal? ttv = null;
        if (deliveredRecords.Count > 0)
        {
            decimal avgContractual = (decimal)deliveredRecords.Average(r => r.ContractualTransitDays);
            if (avgTransit.HasValue)
            {
                ttv = Math.Round(avgTransit.Value - avgContractual, 2);
            }
        }

        var policyVersions = complianceRecords
            .Select(r => r.SlaPolicyVersion)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        return new CarrierScorecardDto
        {
            CarrierCode = normalizedCarrier,
            ServiceLevel = serviceLevel,
            FacilityId = facilityId,
            ClientId = clientId,
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc,
            TotalDispatchedCount = totalDispatched,
            TotalDeliveredCount = totalDelivered,
            InTransitCount = inTransitCount,
            OverdueInTransitCount = overdueInTransit,
            CompletedDeliveryOutcomesCount = completedOutcomes,
            DistinctAffectedExceptionsCount = distinctExceptionsCount,
            TotalExceptionEventsCount = totalExceptions,
            OnTimeDeliveryRatePercent = otdr,
            FirstAttemptDeliveryRatePercent = fadr,
            ExceptionRatePercent = exRate,
            AverageTransitDays = avgTransit,
            MedianTransitDays = medianTransit,
            P75TransitDays = p75Transit,
            P90TransitDays = p90Transit,
            P95TransitDays = p95Transit,
            MinTransitDays = minTransit,
            MaxTransitDays = maxTransit,
            StandardDeviationTransitDays = stdDev,
            TransitTimeVarianceDays = ttv,
            SampleSize = transitObservations.Count,
            IsStatisticallyReliable = transitObservations.Count >= 30,
            SlaPolicyVersions = policyVersions.AsReadOnly()
        };
    }

    public async Task<TransitTimeDistributionDto> GetTransitTimeDistributionAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        CarrierServiceLevel? serviceLevel = null,
        string? destinationCountry = null,
        string? destinationState = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
            throw new ValidationException(nameof(carrierCode), "Carrier code is required.");

        var scope = await _authorization.ResolveAsync(clientId, facilityId, cancellationToken);
        clientId = scope.ClientId;
        facilityId = scope.FacilityId;
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
        var start = startDateUtc.Date;
        var end = endDateUtc.Date.AddDays(1).AddTicks(-1);

        var query = _dbContext.CarrierSlaComplianceRecords
            .AsNoTracking()
            .Where(r => r.CarrierCode == normalizedCarrier
                     && r.DeliveredAtUtc.HasValue
                     && r.DeliveredAtUtc >= start
                     && r.DeliveredAtUtc <= end
                     && r.ActualTransitDays.HasValue);

        if (facilityId.HasValue) query = query.Where(r => r.FacilityId == facilityId.Value);
        if (clientId.HasValue) query = query.Where(r => r.ClientId == clientId.Value);
        if (serviceLevel.HasValue) query = query.Where(r => r.CarrierServiceLevel == serviceLevel.Value);
        if (!string.IsNullOrWhiteSpace(destinationCountry)) query = query.Where(r => r.DestinationCountry == destinationCountry.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(destinationState)) query = query.Where(r => r.DestinationStateOrRegion == destinationState.Trim().ToUpperInvariant());

        var observations = await scope.Apply(query)
            .Select(r => r.ActualTransitDays!.Value)
            .OrderBy(v => v)
            .ToListAsync(cancellationToken);

        int sampleSize = observations.Count;
        if (sampleSize == 0)
        {
            return new TransitTimeDistributionDto
            {
                CarrierCode = normalizedCarrier,
                ServiceLevel = serviceLevel,
                FacilityId = facilityId,
                ClientId = clientId,
                DestinationCountry = destinationCountry,
                DestinationStateOrRegion = destinationState,
                SampleSize = 0,
                IsStatisticallyReliable = false
            };
        }

        // Histograms
        int lessThan1 = observations.Count(v => v < 1.0m);
        int oneToTwo = observations.Count(v => v >= 1.0m && v < 2.0m);
        int twoToThree = observations.Count(v => v >= 2.0m && v < 3.0m);
        int threeToFive = observations.Count(v => v >= 3.0m && v <= 5.0m);
        int moreThanFive = observations.Count(v => v > 5.0m);

        return new TransitTimeDistributionDto
        {
            CarrierCode = normalizedCarrier,
            ServiceLevel = serviceLevel,
            FacilityId = facilityId,
            ClientId = clientId,
            DestinationCountry = destinationCountry,
            DestinationStateOrRegion = destinationState,
            SampleSize = sampleSize,
            IsStatisticallyReliable = sampleSize >= 30,
            MinDays = observations.Min(),
            MaxDays = observations.Max(),
            MeanDays = Math.Round(observations.Average(), 2),
            MedianDays = CalculatePercentile(observations, 50m),
            P75Days = CalculatePercentile(observations, 75m),
            P90Days = CalculatePercentile(observations, 90m),
            P95Days = CalculatePercentile(observations, 95m),
            StandardDeviationDays = CalculateStandardDeviation(observations),
            LessThan1DayCount = lessThan1,
            OneToTwoDaysCount = oneToTwo,
            TwoToThreeDaysCount = twoToThree,
            ThreeToFiveDaysCount = threeToFive,
            MoreThanFiveDaysCount = moreThanFive
        };
    }

    public async Task<DeliveryExceptionIntelligenceDto> GetDeliveryExceptionIntelligenceAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
            throw new ValidationException(nameof(carrierCode), "Carrier code is required.");

        var scope = await _authorization.ResolveAsync(clientId, facilityId, cancellationToken);
        clientId = scope.ClientId;
        facilityId = scope.FacilityId;
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
        var start = startDateUtc.Date;
        var end = endDateUtc.Date.AddDays(1).AddTicks(-1);

        // 1. Query compliance records to identify distinct affected shipments and resolution duration
        var complianceQuery = _dbContext.CarrierSlaComplianceRecords
            .AsNoTracking()
            .Where(r => r.CarrierCode == normalizedCarrier
                     && r.DispatchedAtUtc >= start
                     && r.DispatchedAtUtc <= end);

        if (facilityId.HasValue) complianceQuery = complianceQuery.Where(r => r.FacilityId == facilityId.Value);
        if (clientId.HasValue) complianceQuery = complianceQuery.Where(r => r.ClientId == clientId.Value);

        var complianceRecords = await scope.Apply(complianceQuery).ToListAsync(cancellationToken);

        int totalDispatched = complianceRecords.Count;
        var affectedRecords = complianceRecords.Where(r => r.HasDeliveryException).ToList();
        int distinctAffected = affectedRecords.Count;
        int repeatExceptionsCount = affectedRecords.Count(r => r.QualifyingExceptionsCount >= 2);
        int firstAttemptCorrelation = affectedRecords.Count(r => !r.IsFirstAttemptSuccess);

        decimal? exRate = totalDispatched > 0 ? Math.Round((decimal)distinctAffected / totalDispatched * 100m, 2) : null;

        // 2. Query raw delivery exceptions for category breakdown and resolution times
        var exQuery = _dbContext.ShipmentDeliveryExceptions
            .AsNoTracking()
            .Where(e => e.CarrierCode == normalizedCarrier
                     && e.OccurredAtUtc >= start
                     && e.OccurredAtUtc <= end);

        if (facilityId.HasValue) exQuery = exQuery.Where(e => e.FacilityId == facilityId.Value);
        if (clientId.HasValue) exQuery = exQuery.Where(e => e.ClientId == clientId.Value);

        var exceptions = await scope.Apply(exQuery).ToListAsync(cancellationToken);

        var categoryBreakdown = new Dictionary<CanonicalDeliveryExceptionCategory, int>();
        var attributionBreakdown = new Dictionary<DelayAttributionCategory, int>();

        var resolutionDurations = new List<decimal>();

        foreach (var ex in exceptions)
        {
            var canonical = MapExceptionTypeToCanonical(ex.ExceptionType.ToString(), ex.CarrierDescription);
            categoryBreakdown[canonical] = categoryBreakdown.GetValueOrDefault(canonical, 0) + 1;

            var attribution = _delayAttributionService.AttributeException(
                ex.ShipmentId ?? Guid.Empty,
                ex.TrackingNumber,
                normalizedCarrier,
                canonical,
                ex.CarrierExceptionCode,
                ex.CarrierDescription);

            attributionBreakdown[attribution.AttributionCategory] = attributionBreakdown.GetValueOrDefault(attribution.AttributionCategory, 0) + 1;

            if (ex.ResolvedAtUtc.HasValue)
            {
                var hours = (decimal)(ex.ResolvedAtUtc.Value - ex.OccurredAtUtc).TotalHours;
                if (hours >= 0m) resolutionDurations.Add(hours);
            }
        }

        int totalEvents = exceptions.Count > 0 ? exceptions.Count : complianceRecords.Sum(r => r.QualifyingExceptionsCount);
        decimal? avgResolution = resolutionDurations.Count > 0 ? Math.Round(resolutionDurations.Average(), 1) : null;

        return new DeliveryExceptionIntelligenceDto
        {
            CarrierCode = normalizedCarrier,
            FacilityId = facilityId,
            ClientId = clientId,
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc,
            TotalExceptionEventsCount = totalEvents,
            DistinctAffectedShipmentsCount = distinctAffected,
            ExceptionRatePercent = exRate,
            AverageResolutionHours = avgResolution,
            RepeatExceptionShipmentsCount = repeatExceptionsCount,
            FirstAttemptFailureCorrelationCount = firstAttemptCorrelation,
            CategoryBreakdown = categoryBreakdown,
            AttributionBreakdown = attributionBreakdown
        };
    }

    #region Private Statistical Helpers

    /// <summary>
    /// Calculates percentiles using linear interpolation over ordered raw shipment transit observations.
    /// Uses 0-based indexing position: k = (P / 100) * (N - 1), where 0 <= k <= N - 1.
    /// This is mathematically identical to 1-based indexing position k_1 = (P / 100) * (N - 1) + 1,
    /// adhering to the approved Phase 6.4 specification convention.
    /// </summary>
    private static decimal CalculatePercentile(List<decimal> sortedValues, decimal percentile)
    {
        if (sortedValues == null || sortedValues.Count == 0) return 0m;
        if (sortedValues.Count == 1) return sortedValues[0];

        // Linear interpolation index: k = (P / 100) * (N - 1)
        decimal k = (percentile / 100m) * (sortedValues.Count - 1);
        int lowerIndex = (int)Math.Floor(k);
        int upperIndex = (int)Math.Ceiling(k);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        decimal weight = k - lowerIndex;
        return Math.Round(sortedValues[lowerIndex] * (1m - weight) + sortedValues[upperIndex] * weight, 2);
    }

    private static decimal? CalculateStandardDeviation(List<decimal> values)
    {
        if (values == null || values.Count < 2) return null;
        decimal mean = values.Average();
        decimal sumSquares = values.Sum(v => (v - mean) * (v - mean));
        decimal sampleVariance = sumSquares / (values.Count - 1);
        return Math.Round((decimal)Math.Sqrt((double)sampleVariance), 2);
    }

    private static CanonicalDeliveryExceptionCategory MapExceptionTypeToCanonical(string exceptionType, string reason)
    {
        var combined = $"{exceptionType} {reason}".ToUpperInvariant();

        if (combined.Contains("WEATHER") || combined.Contains("SNOW") || combined.Contains("STORM") || combined.Contains("FLOOD"))
            return CanonicalDeliveryExceptionCategory.WeatherDisruption;
        if (combined.Contains("ADDRESS") || combined.Contains("STREET") || combined.Contains("APT") || combined.Contains("SUITE"))
            return CanonicalDeliveryExceptionCategory.AddressIssue;
        if (combined.Contains("CLOSED") || combined.Contains("UNAVAILABLE") || combined.Contains("NO ONE") || combined.Contains("NOT HOME"))
            return CanonicalDeliveryExceptionCategory.RecipientUnavailable;
        if (combined.Contains("DAMAGE") || combined.Contains("BROKEN") || combined.Contains("LEAK"))
            return CanonicalDeliveryExceptionCategory.DamagedInTransit;
        if (combined.Contains("MECHANICAL") || combined.Contains("SORT") || combined.Contains("DELAY") || combined.Contains("BACKLOG"))
            return CanonicalDeliveryExceptionCategory.CarrierOperationalDelay;
        if (combined.Contains("CUSTOMS") || combined.Contains("DUTY") || combined.Contains("BORDER") || combined.Contains("REGULATORY"))
            return CanonicalDeliveryExceptionCategory.CustomsOrRegulatoryHold;
        if (combined.Contains("LOST") || combined.Contains("MISSING") || combined.Contains("NO SCAN"))
            return CanonicalDeliveryExceptionCategory.LostOrMissing;

        return CanonicalDeliveryExceptionCategory.Other;
    }

    #endregion
}
