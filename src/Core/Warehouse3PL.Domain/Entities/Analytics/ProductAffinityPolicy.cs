using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Configurable policy parameters for market-basket product affinity mining and co-locational slotting rules.
/// Phase 6.3.7.3 Product Affinity & Co-Locational Slotting Rules.
/// </summary>
public class ProductAffinityPolicy : AuditableEntity<Guid>
{
    public Guid? FacilityId { get; set; }
    public Guid? ClientId { get; set; }

    public string Name { get; set; } = "Standard Product Affinity Policy";
    public string? Description { get; set; }

    /// <summary>
    /// Default observation window in days (e.g. 30, 60, 90, 180, 365). Default is 90 days.
    /// </summary>
    public int AnalysisWindowDays { get; set; } = 90;

    /// <summary>
    /// Minimum total qualifying orders required in the observation window before mining executes.
    /// Default is 30 orders. Low-volume samples fail closed as statistically insufficient.
    /// </summary>
    public int MinOrderCount { get; set; } = 30;

    /// <summary>
    /// Minimum co-occurrence count (orders containing both item A and item B) required to consider a pair.
    /// Default is 3 orders. Rejects single-order noise and rare-pair anomalies.
    /// </summary>
    public int MinCoOccurrenceCount { get; set; } = 3;

    /// <summary>
    /// Minimum pair support threshold: C(A, B) / N. Default is 0.010 (1.0%).
    /// </summary>
    public decimal MinSupport { get; set; } = 0.0100m;

    /// <summary>
    /// Minimum confidence threshold: max(conf(A -> B), conf(B -> A)). Default is 0.100 (10.0%).
    /// </summary>
    public decimal MinConfidence { get; set; } = 0.1000m;

    /// <summary>
    /// Minimum lift threshold required for positive association (must be > 1.0). Default is 1.100.
    /// </summary>
    public decimal MinLift { get; set; } = 1.1000m;

    /// <summary>
    /// Minimum lift required to merge items into an affinity co-location cluster. Default is 1.250.
    /// </summary>
    public decimal MinClusterLift { get; set; } = 1.2500m;

    /// <summary>
    /// Maximum number of SKUs that can belong to a single co-location cluster. Default is 8.
    /// Prevents sprawling clusters that exceed physical zone or aisle capacity.
    /// </summary>
    public int MaxClusterSize { get; set; } = 8;

    /// <summary>
    /// Scoring weight for product co-location affinity in the candidate location suitability equation (0.0 to 1.0).
    /// Default is 0.15.
    /// </summary>
    public decimal WeightProductAffinity { get; set; } = 0.15m;

    /// <summary>
    /// When true, allows unassigned facility orders (FacilityId == Guid.Empty) to contribute to facility mining statistics.
    /// Default is false to strictly prevent cross-facility demand contamination.
    /// </summary>
    public bool IncludeUnassignedFacilityOrders { get; set; } = false;

    /// <summary>
    /// Optional seasonal start date for time-window segmentation.
    /// </summary>
    public DateTime? SeasonalStartDateUtc { get; set; }

    /// <summary>
    /// Optional seasonal end date for time-window segmentation.
    /// </summary>
    public DateTime? SeasonalEndDateUtc { get; set; }

    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;

    public ProductAffinityPolicy()
    {
        Id = Guid.NewGuid();
    }
}
