using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Calculation outcome when rating an activity against a RateCardLine and its volume tiers.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public record RateCalculationResult
{
    /// <summary>
    /// Calculated unit price or blended rate.
    /// </summary>
    public decimal UnitPrice { get; init; }

    /// <summary>
    /// Final calculated total amount, with any applicable minimum fee enforced.
    /// </summary>
    public decimal TotalAmount { get; init; }

    /// <summary>
    /// Quantity rated.
    /// </summary>
    public decimal Quantity { get; init; }

    /// <summary>
    /// Rate card tier matched, if applicable (single tier in AllUnits or null if Graduated/Flat).
    /// </summary>
    public Guid? MatchedTierId { get; init; }

    /// <summary>
    /// Pricing tier model used for evaluation.
    /// </summary>
    public PricingTierCalculationModel TierModel { get; init; }

    /// <summary>
    /// Indicates whether a contractual minimum charge was triggered to elevate the total.
    /// </summary>
    public bool MinimumChargeApplied { get; init; }

    /// <summary>
    /// Applicable minimum charge, if configured on the line.
    /// </summary>
    public decimal? MinimumCharge { get; init; }

    /// <summary>
    /// Human-readable audit description of how the rating was computed.
    /// </summary>
    public string CalculationSummary { get; init; } = string.Empty;
}

