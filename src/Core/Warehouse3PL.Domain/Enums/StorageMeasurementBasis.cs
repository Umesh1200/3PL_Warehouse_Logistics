namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Metric basis for calculating recurring warehouse holding and inventory storage fees.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public enum StorageMeasurementBasis
{
    /// <summary>
    /// Storage fee calculated per billable pallet position per day.
    /// </summary>
    PalletDay = 1,

    /// <summary>
    /// Storage fee calculated per billable carton/case per day.
    /// </summary>
    CartonDay = 2,

    /// <summary>
    /// Storage fee calculated per cubic meter (CBM) volume per day.
    /// </summary>
    CbmDay = 3
}

