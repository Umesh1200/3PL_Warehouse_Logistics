namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of an individual SKU relocation recommendation within a slotting batch.
/// </summary>
public enum SlottingRecommendationStatus
{
    /// <summary>
    /// Pending supervisory review.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Formally approved for operational relocation.
    /// </summary>
    Approved = 2,

    /// <summary>
    /// Rejected due to operational disruption or physical constraints.
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// Skipped or deferred by operator.
    /// </summary>
    Skipped = 4,

    /// <summary>
    /// Physical relocation completed in warehouse.
    /// </summary>
    Executed = 5
}
