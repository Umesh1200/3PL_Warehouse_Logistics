namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Classification of carrier cost adjustments assessed after initial rating or delivery.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public enum CarrierCostAdjustmentType
{
    /// <summary>
    /// Surcharge assessed post-delivery (e.g., address correction, residential reclassification).
    /// </summary>
    LateAccessorial = 1,

    /// <summary>
    /// Re-weigh or volumetric audit adjustment assessed by carrier sorting hub.
    /// </summary>
    WeightDiscrepancy = 2,

    /// <summary>
    /// Carrier invoice rate correction or tariff audit adjustment.
    /// </summary>
    RateCorrection = 3,

    /// <summary>
    /// Credit issued by carrier following billing dispute resolution.
    /// </summary>
    DisputeCredit = 4
}

