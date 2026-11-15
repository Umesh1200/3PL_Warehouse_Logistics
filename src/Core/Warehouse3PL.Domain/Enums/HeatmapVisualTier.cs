namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Discrete visualization alert tiers for warehouse heatmap cells.
/// DB-SPEC-3PL-2026 Phase 6.3.6 Dynamic Warehouse Heatmaps.
/// </summary>
public enum HeatmapVisualTier
{
    /// <summary>
    /// Location is empty / unallocated (0% fill).
    /// </summary>
    Empty = 0,

    /// <summary>
    /// Location operating within normal/optimal operating parameters.
    /// </summary>
    Optimal = 1,

    /// <summary>
    /// Location has elevated fill, slow movement, or early warning condition.
    /// </summary>
    Elevated = 2,

    /// <summary>
    /// Location has high utilization or requires operational attention.
    /// </summary>
    High = 3,

    /// <summary>
    /// Critical condition: Over-capacity (>100%), Expired lots, or severe bottleneck.
    /// </summary>
    Critical = 4,

    /// <summary>
    /// Location is physically or administratively blocked / locked.
    /// </summary>
    Blocked = 5
}
