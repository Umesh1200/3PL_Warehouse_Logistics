using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Individual SKU membership within a product affinity co-location cluster.
/// Phase 6.3.7.3 Product Affinity & Co-Locational Slotting Rules.
/// </summary>
public class ProductAffinityClusterMember : BaseEntity<Guid>
{
    public Guid ClusterId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// Whether this item is the designated primary anchor of the cluster.
    /// </summary>
    public bool IsAnchor { get; set; }

    /// <summary>
    /// Number of pairwise high-affinity connections this item maintains within the cluster.
    /// </summary>
    public int WithinClusterDegree { get; set; }

    /// <summary>
    /// Average lift of this item with other cluster members.
    /// </summary>
    public decimal AverageWithinClusterLift { get; set; }

    public ProductAffinityCluster Cluster { get; set; } = null!;

    public ProductAffinityClusterMember()
    {
        Id = Guid.NewGuid();
    }
}
