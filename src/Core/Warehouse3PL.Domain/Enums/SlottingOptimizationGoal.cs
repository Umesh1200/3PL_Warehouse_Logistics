namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Primary and composite optimization goals for warehouse slotting policies.
/// </summary>
public enum SlottingOptimizationGoal
{
    /// <summary>
    /// Minimize total travel distance from pick faces to packing/dock areas.
    /// </summary>
    MinimizeTravelDistance = 1,

    /// <summary>
    /// Maximize pick density by grouping high-frequency co-picked items in adjacent bays.
    /// </summary>
    MaximizePickDensity = 2,

    /// <summary>
    /// Prioritize ergonomic golden zone (waist-to-shoulder height) for high-velocity SKUs.
    /// </summary>
    ErgonomicGoldenZone = 3,

    /// <summary>
    /// Balance order picking workload across multiple aisles to reduce warehouse aisle congestion.
    /// </summary>
    BalanceWorkload = 4,

    /// <summary>
    /// Maximize volumetric cubic fill and storage space utilization.
    /// </summary>
    MaximizeCubeFill = 5,

    /// <summary>
    /// Configurable weighted composite scoring combining distance, ergonomics, density, and cube fill.
    /// </summary>
    WeightedComposite = 6
}
