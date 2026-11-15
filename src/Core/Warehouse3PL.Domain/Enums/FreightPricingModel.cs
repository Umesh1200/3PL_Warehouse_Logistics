namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Contractual commercial freight pricing model applied to carrier transportation costs.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public enum FreightPricingModel
{
    /// <summary>
    /// Sell = CarrierCost * (1 + MarkupPercentage / 100).
    /// </summary>
    CostPlusPercentageMarkup = 1,

    /// <summary>
    /// Target gross margin percentage: Sell = CarrierCost / (1 - MarginPercentage / 100).
    /// </summary>
    CostPlusMarginPercentage = 2,

    /// <summary>
    /// Sell = CarrierCost + FlatFeePerShipment.
    /// </summary>
    FlatFeePerShipment = 3,

    /// <summary>
    /// Sell = CarrierCost + (PackageCount * FlatFeePerPackage).
    /// </summary>
    FlatFeePerPackage = 4,

    /// <summary>
    /// Fixed contractual rate regardless of underlying carrier cost.
    /// </summary>
    FixedContractualRate = 5,

    /// <summary>
    /// Zero markup pass-through: Sell = CarrierCost.
    /// </summary>
    PassThroughCostOnly = 6
}

