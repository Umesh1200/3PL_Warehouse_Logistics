using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Mined pairwise market-basket association rule between two SKUs in a warehouse facility.
/// Invariants:
/// 1. Canonical Ordering: ItemAId is lexicographically less than ItemBId to eliminate duplicate reversed pairs.
/// 2. Tenant & Facility Isolation: Strictly partitioned by ClientId and FacilityId.
/// 3. Noise Rejection: Statistical significance requires passing minimum sample size, co-occurrence, support, and lift gates.
/// Phase 6.3.7.3 Product Affinity & Co-Locational Slotting Rules.
/// </summary>
public class ProductAffinityPair : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    /// <summary>
    /// Unique identifier for the affinity mining batch run.
    /// </summary>
    public Guid? MiningBatchId { get; set; }

    /// <summary>
    /// Primary SKU in canonical pair order (ItemAId.CompareTo(ItemBId) < 0).
    /// </summary>
    public Guid ItemAId { get; set; }
    public string ItemASku { get; set; } = string.Empty;

    /// <summary>
    /// Secondary SKU in canonical pair order.
    /// </summary>
    public Guid ItemBId { get; set; }
    public string ItemBSku { get; set; } = string.Empty;

    /// <summary>
    /// Total number of qualifying orders evaluated in the observation window (N).
    /// </summary>
    public int TotalOrdersEvaluated { get; set; }

    /// <summary>
    /// Distinct order frequency of Item A: C(A).
    /// </summary>
    public int ItemAOrderCount { get; set; }

    /// <summary>
    /// Distinct order frequency of Item B: C(B).
    /// </summary>
    public int ItemBOrderCount { get; set; }

    /// <summary>
    /// Co-occurrence order count containing both Item A and Item B: C(A, B).
    /// </summary>
    public int CoOccurrenceCount { get; set; }

    /// <summary>
    /// Pair joint support: supp(A, B) = C(A, B) / N.
    /// </summary>
    public decimal Support { get; set; }

    /// <summary>
    /// Marginal support for Item A: supp(A) = C(A) / N.
    /// </summary>
    public decimal ItemASupport { get; set; }

    /// <summary>
    /// Marginal support for Item B: supp(B) = C(B) / N.
    /// </summary>
    public decimal ItemBSupport { get; set; }

    /// <summary>
    /// Directional confidence A -> B: conf(A -> B) = C(A, B) / C(A).
    /// </summary>
    public decimal ConfidenceAtoB { get; set; }

    /// <summary>
    /// Directional confidence B -> A: conf(B -> A) = C(A, B) / C(B).
    /// </summary>
    public decimal ConfidenceBtoA { get; set; }

    /// <summary>
    /// Symmetric maximum confidence: max(conf(A -> B), conf(B -> A)).
    /// </summary>
    public decimal MaxConfidence { get; set; }

    /// <summary>
    /// Kulczynski measure: 0.5 * (conf(A -> B) + conf(B -> A)).
    /// Null-invariant symmetric association metric.
    /// </summary>
    public decimal KulczynskiMeasure { get; set; }

    /// <summary>
    /// Lift ratio: supp(A, B) / (supp(A) * supp(B)) = (C(A, B) * N) / (C(A) * C(B)).
    /// Values > 1.0 indicate positive affinity beyond random chance.
    /// </summary>
    public decimal Lift { get; set; }

    /// <summary>
    /// Pearson's chi-square test statistic for independence with 1 degree of freedom (with Yates' correction if cells < 5).
    /// Values >= 3.841 indicate statistical significance at p < 0.05.
    /// </summary>
    public decimal? ChiSquareStatistic { get; set; }

    /// <summary>
    /// Minimum expected cell frequency across the 2x2 contingency table.
    /// Values < 5.0 trigger Yates' continuity correction; values < 1.0 invalidate asymptotic chi-square approximation.
    /// </summary>
    public decimal? MinExpectedCellFrequency { get; set; }

    /// <summary>
    /// Indicates whether Yates' continuity correction was applied due to small cell counts (< 5.0).
    /// </summary>
    public bool HasYatesCorrection { get; set; }

    /// <summary>
    /// Two-tailed p-value for the chi-square statistic (df = 1).
    /// </summary>
    public decimal? PValue { get; set; }

    /// <summary>
    /// Whether the pair passes all sample size, co-occurrence, support, and lift reliability gates.
    /// </summary>
    public bool IsStatisticallySignificant { get; set; }

    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public DateTime CalculatedAtUtc { get; set; } = DateTime.UtcNow;

    public ProductAffinityPair()
    {
        Id = Guid.NewGuid();
    }
}
