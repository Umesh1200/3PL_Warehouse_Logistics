using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Advisory relocation recommendation proposing the movement of an SKU
/// from its current location to a more suitable physical storage location.
/// Phase 6.3.7 ABC-Based Intelligent Warehouse Slotting Optimization.
/// </summary>
public class SlottingRecommendation : AuditableEntity<Guid>
{
    public Guid BatchId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemSku { get; set; } = string.Empty;
    public Guid ClientId { get; set; }

    public Guid CurrentLocationId { get; set; }
    public string CurrentLocationCode { get; set; } = string.Empty;

    public Guid ProposedLocationId { get; set; }
    public string ProposedLocationCode { get; set; } = string.Empty;

    public string SourceZoneCode { get; set; } = string.Empty;
    public string DestinationZoneCode { get; set; } = string.Empty;

    public string SkuVelocity { get; set; } = "Medium";
    public string AbcClass { get; set; } = "B";
    public string VelocityTrend { get; set; } = "Stable";
    public string ReasonCode { get; set; } = "OptimizeStoragePlacement";

    /// <summary>
    /// Overall placement suitability score (0 to 100).
    /// </summary>
    public decimal SuitabilityScore { get; set; }

    /// <summary>
    /// Ergonomic placement score (0 to 100) reflecting golden zone waist-to-shoulder suitability.
    /// </summary>
    public decimal ErgonomicScore { get; set; }

    /// <summary>
    /// ABC tier placement affinity score (0 to 100).
    /// </summary>
    public decimal AbcAffinityScore { get; set; }

    /// <summary>
    /// Velocity pattern and trend affinity score (0 to 100).
    /// </summary>
    public decimal VelocityAffinityScore { get; set; }

    /// <summary>
    /// Picking volume density and dock proximity score (0 to 100).
    /// </summary>
    public decimal PickDensityScore { get; set; }

    /// <summary>
    /// Cumulative batch weight consumed at proposed destination after this proposal (kg).
    /// </summary>
    public decimal CumulativeWeightConsumedKg { get; set; }

    /// <summary>
    /// Cumulative batch volume consumed at proposed destination after this proposal (cm3).
    /// </summary>
    public decimal CumulativeVolumeConsumedCm3 { get; set; }

    /// <summary>
    /// Projected reduction in pick travel distance per pick operation (in meters).
    /// </summary>
    public decimal TravelDistanceDeltaMeters { get; set; }

    /// <summary>
    /// Estimated labor time required to execute the physical relocation.
    /// </summary>
    public decimal EstimatedMoveLaborMinutes { get; set; }

    /// <summary>
    /// Estimated net monthly financial savings factoring in labor cost of moving vs pick travel savings.
    /// </summary>
    public decimal EstimatedMonthlyNetSavingsUsd { get; set; }

    // Phase 6.3.7.4 Transparent Economics & Multi-Factor Prioritization
    public decimal GrossMonthlySavingsUsd { get; set; }
    public decimal HandlingCostUsd { get; set; }
    public decimal MovementCostUsd { get; set; }
    public decimal DisruptionCostUsd { get; set; }
    public decimal TotalRelocationCostUsd { get; set; }
    public decimal UrgencyScore { get; set; }
    public string? UrgencyReason { get; set; }
    public decimal PriorityScore { get; set; }
    public string DisruptionLevel { get; set; } = "Low";
    public bool IsOperationallyFeasible { get; set; } = true;

    /// <summary>
    /// Flag indicating if any hard physical or regulatory constraint was breached.
    /// A recommendation with hard constraint violations can never be approved.
    /// </summary>
    public bool IsHardConstraintViolated { get; set; }

    public string? ViolatedConstraintsSummary { get; set; }

    /// <summary>
    /// Indicates whether the SKU velocity projection used for placement was stale or missing.
    /// </summary>
    public bool IsProjectionStaleOrMissing { get; set; }

    /// <summary>
    /// Audit trace of the classification source: "AuthoritativeProjection", "CatalogFallback (StaleProjection)", or "CatalogFallback (MissingProjection)".
    /// </summary>
    public string ClassificationSource { get; set; } = "AuthoritativeProjection";

    /// <summary>
    /// Timestamp when the underlying velocity projection was calculated.
    /// </summary>
    public DateTime? ProjectionCalculatedAtUtc { get; set; }

    /// <summary>
    /// Age in days of the projection at simulation time.
    /// </summary>
    public int? ProjectionAgeDays { get; set; }

    public SlottingRecommendationStatus Status { get; set; } = SlottingRecommendationStatus.Pending;
    public string? RejectionReason { get; set; }

    // Navigations
    public SlottingRecommendationBatch Batch { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public Location CurrentLocation { get; set; } = null!;
    public Location ProposedLocation { get; set; } = null!;

    public SlottingRecommendation()
    {
        Id = Guid.NewGuid();
    }
}
