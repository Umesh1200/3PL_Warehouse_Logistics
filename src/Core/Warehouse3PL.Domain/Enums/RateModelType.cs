namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Metric model defining how billable activity rates are structured.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public enum RateModelType
{
    /// <summary>
    /// Rate charged per discrete item or piece (e.g. piece pick, unit receiving).
    /// </summary>
    PerUnit = 1,

    /// <summary>
    /// Rate charged per outbound or inbound order (e.g. order base fulfillment fee).
    /// </summary>
    PerOrder = 2,

    /// <summary>
    /// Rate charged per order line picked or processed.
    /// </summary>
    PerLine = 3,

    /// <summary>
    /// Rate charged per pallet (e.g. pallet putaway, pallet storage).
    /// </summary>
    PerPallet = 4,

    /// <summary>
    /// Rate charged per carton or package (e.g. carton pack, carton handling).
    /// </summary>
    PerCarton = 5,

    /// <summary>
    /// Rate charged per cubic meter per day for volumetric storage.
    /// </summary>
    PerCbmDay = 6,

    /// <summary>
    /// Percentage markup or fee based on carrier freight cost.
    /// </summary>
    PercentageOfFreight = 7,

    /// <summary>
    /// Fixed flat charge for service or transaction regardless of quantity.
    /// </summary>
    FlatFee = 8
}

