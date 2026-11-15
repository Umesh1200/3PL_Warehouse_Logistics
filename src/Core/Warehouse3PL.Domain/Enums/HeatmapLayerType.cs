namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Bitwise flag enum representing independent warehouse visualization heatmap layers.
/// Invariant: Layers are independently activatable and non-mutually exclusive.
/// DB-SPEC-3PL-2026 Phase 6.3.6 Dynamic Warehouse Heatmaps.
/// </summary>
[Flags]
public enum HeatmapLayerType
{
    None = 0,

    /// <summary>
    /// Volumetric cubic-meter utilization percentage (uncapped, can exceed 100%).
    /// </summary>
    CubeUtilization = 1,

    /// <summary>
    /// Pallet position occupancy percentage (uncapped, strictly independent of cube fill).
    /// </summary>
    PalletOccupancy = 2,

    /// <summary>
    /// SKU movement velocity density (FastMoving, MediumMoving, SlowMoving, NonMoving, DeadStock).
    /// </summary>
    MovementVelocity = 4,

    /// <summary>
    /// Inventory age and expiration risk (Current, Slow, Dormant, DeadStock, NearExpiry, Expired).
    /// </summary>
    AgingAndExpiry = 8,

    /// <summary>
    /// Over-capacity locations exceeding physical or usable cubic limits.
    /// </summary>
    Overcapacity = 16,

    /// <summary>
    /// Operational status isolation for blocked, damaged, or quarantined locations.
    /// </summary>
    BlockedLocations = 32,

    /// <summary>
    /// Default composite overview combining cube fill, over-capacity, and blocked alerts.
    /// </summary>
    StandardOverview = CubeUtilization | Overcapacity | BlockedLocations,

    /// <summary>
    /// Comprehensive multi-layer visualization enabling all analytics overlays.
    /// </summary>
    AllLayers = CubeUtilization | PalletOccupancy | MovementVelocity | AgingAndExpiry | Overcapacity | BlockedLocations
}
