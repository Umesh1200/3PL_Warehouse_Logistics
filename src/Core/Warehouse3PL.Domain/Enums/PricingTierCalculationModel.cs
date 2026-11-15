namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Calculation methodology applied when evaluating volume tiers on a commercial rate card line.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public enum PricingTierCalculationModel
{
    /// <summary>
    /// Marginal bracketed pricing: each portion of volume receives the price of its corresponding bracket tier.
    /// Example: 1-500 @ $0.40, 501-1000 @ $0.35. A quantity of 750 is charged: (500 * $0.40) + (250 * $0.35).
    /// </summary>
    Graduated = 1,

    /// <summary>
    /// Retroactive cliff pricing: once total volume qualifies for an achieved tier, the entire eligible volume receives that achieved tier rate.
    /// Example: 1-500 @ $0.40, 501-1000 @ $0.35. A quantity of 750 is charged: 750 * $0.35.
    /// </summary>
    AllUnits = 2,

    /// <summary>
    /// Flat rate pricing: a constant base rate applied linearly across all quantities or as a single fixed fee.
    /// </summary>
    FlatRate = 3
}

