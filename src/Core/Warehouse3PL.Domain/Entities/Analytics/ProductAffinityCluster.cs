using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Co-location cluster representing a group of mutually high-affinity products
/// recommended to be stored in close spatial proximity (adjacent bays, same aisle/zone).
/// Phase 6.3.7.3 Product Affinity & Co-Locational Slotting Rules.
/// </summary>
public class ProductAffinityCluster : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    /// <summary>
    /// Unique identifier for the affinity mining batch run that created this cluster.
    /// </summary>
    public Guid? MiningBatchId { get; set; }

    /// <summary>
    /// Unique cluster reference code (e.g. "PAC-FAC01-001").
    /// </summary>
    public string ClusterCode { get; set; } = string.Empty;

    /// <summary>
    /// Anchor SKU identifier (item with highest internal affinity connectivity or pick frequency).
    /// </summary>
    public Guid PrimaryAnchorItemId { get; set; }
    public string PrimaryAnchorSku { get; set; } = string.Empty;

    /// <summary>
    /// Number of member SKUs in this cluster.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Average pairwise lift among members in this cluster.
    /// </summary>
    public decimal AverageLift { get; set; }

    /// <summary>
    /// Maximum pairwise lift observed within this cluster.
    /// </summary>
    public decimal MaxLift { get; set; }

    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public DateTime CalculatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ProductAffinityClusterMember> Members { get; set; } = [];

    public ProductAffinityCluster()
    {
        Id = Guid.NewGuid();
    }
}
