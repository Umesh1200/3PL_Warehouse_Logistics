using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

/// <summary>
/// Aggregated scorecard model tracking carrier SLA adherence, transit distributions, and tail latency.
/// DB-SPEC-3PL-2026 Phase 6.4.3 Carrier Scorecards & Exception Analytics.
/// </summary>
public record CarrierScorecardDto
{
    public string CarrierCode { get; init; } = string.Empty;
    public CarrierServiceLevel? ServiceLevel { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public DateTime StartDateUtc { get; init; }
    public DateTime EndDateUtc { get; init; }

    // Volume Counts
    public int TotalDispatchedCount { get; init; }
    public int TotalDeliveredCount { get; init; }
    public int InTransitCount { get; init; }
    public int OverdueInTransitCount { get; init; }
    public int CompletedDeliveryOutcomesCount { get; init; }
    public int DistinctAffectedExceptionsCount { get; init; }
    public int TotalExceptionEventsCount { get; init; }

    // Performance Rates (Nullable to represent undefined rate when denominator is zero)
    public decimal? OnTimeDeliveryRatePercent { get; init; }
    public decimal? FirstAttemptDeliveryRatePercent { get; init; }
    public decimal? ExceptionRatePercent { get; init; }

    // Transit Duration & Distribution Metrics (in Days)
    public decimal? AverageTransitDays { get; init; }
    public decimal? MedianTransitDays { get; init; }
    public decimal? P75TransitDays { get; init; }
    public decimal? P90TransitDays { get; init; }
    public decimal? P95TransitDays { get; init; }
    public decimal? MinTransitDays { get; init; }
    public decimal? MaxTransitDays { get; init; }
    public decimal? StandardDeviationTransitDays { get; init; }
    public decimal? TransitTimeVarianceDays { get; init; }

    // Statistical Reliability
    public int SampleSize { get; init; }
    public bool IsStatisticallyReliable { get; init; }

    // Governing Policy Versions Audit
    public IReadOnlyList<string> SlaPolicyVersions { get; init; } = [];
}

/// <summary>
/// Detailed transit-time distribution model providing percentiles and parametric dispersion metrics.
/// Percentiles are computed directly from individual shipment observations using linear interpolation
/// with zero-based indexing: k = (P / 100) * (N - 1), corresponding to 1-based k_1 = (P / 100) * (N - 1) + 1.
/// </summary>
public record TransitTimeDistributionDto
{
    public string CarrierCode { get; init; } = string.Empty;
    public CarrierServiceLevel? ServiceLevel { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public string? DestinationCountry { get; init; }
    public string? DestinationStateOrRegion { get; init; }

    public int SampleSize { get; init; }
    public bool IsStatisticallyReliable { get; init; }

    public decimal? MinDays { get; init; }
    public decimal? MaxDays { get; init; }
    public decimal? MeanDays { get; init; }
    public decimal? MedianDays { get; init; }
    public decimal? P75Days { get; init; }
    public decimal? P90Days { get; init; }
    public decimal? P95Days { get; init; }
    public decimal? StandardDeviationDays { get; init; }

    // Bucket Histogram (Days: Count)
    public int LessThan1DayCount { get; init; }
    public int OneToTwoDaysCount { get; init; }
    public int TwoToThreeDaysCount { get; init; }
    public int ThreeToFiveDaysCount { get; init; }
    public int MoreThanFiveDaysCount { get; init; }
}

/// <summary>
/// Aggregated delivery exception intelligence tracking frequency, distinct impact, and resolution metrics.
/// </summary>
public record DeliveryExceptionIntelligenceDto
{
    public string CarrierCode { get; init; } = string.Empty;
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public DateTime StartDateUtc { get; init; }
    public DateTime EndDateUtc { get; init; }

    public int TotalExceptionEventsCount { get; init; }
    public int DistinctAffectedShipmentsCount { get; init; }
    public decimal? ExceptionRatePercent { get; init; }
    public decimal? AverageResolutionHours { get; init; }

    public int RepeatExceptionShipmentsCount { get; init; }
    public int FirstAttemptFailureCorrelationCount { get; init; }

    public Dictionary<CanonicalDeliveryExceptionCategory, int> CategoryBreakdown { get; init; } = [];
    public Dictionary<DelayAttributionCategory, int> AttributionBreakdown { get; init; } = [];
}

/// <summary>
/// Root-cause delay attribution result evaluating operational responsibility and confidence.
/// </summary>
public record DelayAttributionResultDto
{
    public Guid ShipmentId { get; init; }
    public string TrackingNumber { get; init; } = string.Empty;
    public string CarrierCode { get; init; } = string.Empty;
    public CanonicalDeliveryExceptionCategory CanonicalCategory { get; init; }
    public DelayAttributionCategory AttributionCategory { get; init; }
    public AttributionConfidence Confidence { get; init; }
    public string AttributionReason { get; init; } = string.Empty;
    public bool IsCarrierResponsible { get; init; }
}

/// <summary>
/// Evidentiary completeness assessment for carrier claims readiness.
/// </summary>
public record ClaimsReadinessAssessmentDto
{
    public Guid ShipmentId { get; init; }
    public string TrackingNumber { get; init; } = string.Empty;
    public string CarrierCode { get; init; } = string.Empty;
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }

    public ClaimType ClaimType { get; init; } = ClaimType.Lost;

    public ClaimsReadinessStatus ReadinessStatus { get; init; }
    public decimal ReadinessScorePercent { get; init; }

    public bool HasHandoverEvidence { get; init; }
    public bool HasTransitScanHistory { get; init; }
    public bool HasObstructionEvidence { get; init; }
    public bool HasThresholdBreach { get; init; }
    public bool HasFreightCostEvidence { get; init; }
    public bool HasValuationEvidence { get; init; }
    public bool HasPodEvidence { get; init; }

    public IReadOnlyList<string> MissingEvidenceItems { get; init; } = [];
    public string ReviewEligibilityReason { get; init; } = string.Empty;
    public DateTime AssessedAtUtc { get; init; } = DateTime.UtcNow;
}
