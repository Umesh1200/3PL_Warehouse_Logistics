using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Commercial pricing rule for a specific BillableActivityType within a RateCardSchedule.
/// Defines pricing model, base rates, minimum charges, and volume tier structures.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public class RateCardLine : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid RateCardScheduleId { get; set; }

    public BillableActivityType ActivityType { get; set; }
    public string UnitOfMeasure { get; set; } = "UNIT";
    public RateModelType RateModel { get; set; } = RateModelType.PerUnit;
    public PricingTierCalculationModel TierModel { get; set; } = PricingTierCalculationModel.FlatRate;

    /// <summary>
    /// Base unit rate or flat rate applied when no tiers exist or for FlatRate model.
    /// </summary>
    public decimal BaseRate { get; set; }

    /// <summary>
    /// Contractual minimum fee enforced per activity event.
    /// If calculated total is below this floor, total is elevated to MinimumCharge.
    /// </summary>
    public decimal? MinimumCharge { get; set; }

    // Navigations
    public RateCardSchedule RateCardSchedule { get; set; } = null!;
    public ICollection<RateCardTier> Tiers { get; set; } = new List<RateCardTier>();

    public RateCardLine()
    {
        Id = Guid.NewGuid();
    }

    public static RateCardLine Create(
        Guid clientId,
        Guid rateCardScheduleId,
        BillableActivityType activityType,
        string unitOfMeasure,
        RateModelType rateModel,
        PricingTierCalculationModel tierModel,
        decimal baseRate,
        decimal? minimumCharge = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required for rate card line.");
        }

        if (rateCardScheduleId == Guid.Empty)
        {
            throw new ValidationException(nameof(rateCardScheduleId), "Rate card schedule ID is required.");
        }

        if (string.IsNullOrWhiteSpace(unitOfMeasure))
        {
            throw new ValidationException(nameof(unitOfMeasure), "Unit of measure is required.");
        }

        if (baseRate < 0)
        {
            throw new ValidationException(nameof(baseRate), "Base rate cannot be negative.");
        }

        if (minimumCharge.HasValue && minimumCharge.Value < 0)
        {
            throw new ValidationException(nameof(minimumCharge), "Minimum charge cannot be negative.");
        }

        return new RateCardLine
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            RateCardScheduleId = rateCardScheduleId,
            ActivityType = activityType,
            UnitOfMeasure = unitOfMeasure.Trim().ToUpperInvariant(),
            RateModel = rateModel,
            TierModel = tierModel,
            BaseRate = decimal.Round(baseRate, 4, MidpointRounding.AwayFromZero),
            MinimumCharge = minimumCharge.HasValue ? decimal.Round(minimumCharge.Value, 4, MidpointRounding.AwayFromZero) : null
        };
    }

    /// <summary>
    /// Adds a volume pricing tier to this rate line.
    /// </summary>
    public RateCardTier AddTier(int tierIndex, decimal minQuantity, decimal? maxQuantity, decimal unitPrice)
    {
        var tier = RateCardTier.Create(ClientId, Id, tierIndex, minQuantity, maxQuantity, unitPrice);
        Tiers.Add(tier);
        return tier;
    }

    /// <summary>
    /// Evaluates contractual pricing for the given activity quantity according to TierModel, BaseRate, and MinimumCharge.
    /// </summary>
    public RateCalculationResult CalculateRating(decimal quantity)
    {
        if (quantity < 0)
        {
            throw new ValidationException(nameof(quantity), "Quantity to rate cannot be negative.");
        }

        if (Tiers.Count == 0 || TierModel == PricingTierCalculationModel.FlatRate)
        {
            return CalculateFlatRate(quantity);
        }

        return TierModel switch
        {
            PricingTierCalculationModel.AllUnits => CalculateAllUnits(quantity),
            PricingTierCalculationModel.Graduated => CalculateGraduated(quantity),
            _ => CalculateFlatRate(quantity)
        };
    }

    private RateCalculationResult CalculateFlatRate(decimal quantity)
    {
        var rawTotal = decimal.Round(quantity * BaseRate, 4, MidpointRounding.AwayFromZero);
        var finalTotal = rawTotal;
        var minApplied = false;

        if (MinimumCharge.HasValue && finalTotal < MinimumCharge.Value)
        {
            finalTotal = MinimumCharge.Value;
            minApplied = true;
        }

        var effectiveUnitPrice = quantity > 0
            ? decimal.Round(finalTotal / quantity, 4, MidpointRounding.AwayFromZero)
            : BaseRate;

        return new RateCalculationResult
        {
            Quantity = quantity,
            UnitPrice = effectiveUnitPrice,
            TotalAmount = finalTotal,
            MatchedTierId = null,
            TierModel = PricingTierCalculationModel.FlatRate,
            MinimumChargeApplied = minApplied,
            MinimumCharge = MinimumCharge,
            CalculationSummary = minApplied
                ? $"Flat rate @ {BaseRate:F4}/unit = {rawTotal:F2}; elevated to minimum charge {MinimumCharge:F2}"
                : $"Flat rate @ {BaseRate:F4}/unit across {quantity} {UnitOfMeasure} = {finalTotal:F2}"
        };
    }

    private RateCalculationResult CalculateAllUnits(decimal quantity)
    {
        var sortedTiers = Tiers.OrderBy(t => t.TierIndex).ThenBy(t => t.TierMinQuantity).ToList();
        RateCardTier? matchedTier = null;

        if (quantity == 0)
        {
            matchedTier = sortedTiers.FirstOrDefault();
        }
        else
        {
            // Find tier where quantity falls within [TierMinQuantity, TierMaxQuantity]
            matchedTier = sortedTiers.FirstOrDefault(t =>
                quantity >= t.TierMinQuantity && (!t.TierMaxQuantity.HasValue || quantity <= t.TierMaxQuantity.Value));

            // If quantity exceeds all defined tiers, select highest tier bracket
            if (matchedTier == null && sortedTiers.Count > 0)
            {
                matchedTier = sortedTiers.Last();
            }
        }

        var appliedUnitPrice = matchedTier != null ? matchedTier.UnitPrice : BaseRate;
        var rawTotal = decimal.Round(quantity * appliedUnitPrice, 4, MidpointRounding.AwayFromZero);
        var finalTotal = rawTotal;
        var minApplied = false;

        if (MinimumCharge.HasValue && finalTotal < MinimumCharge.Value)
        {
            finalTotal = MinimumCharge.Value;
            minApplied = true;
        }

        var effectiveUnitPrice = quantity > 0
            ? decimal.Round(finalTotal / quantity, 4, MidpointRounding.AwayFromZero)
            : appliedUnitPrice;

        return new RateCalculationResult
        {
            Quantity = quantity,
            UnitPrice = effectiveUnitPrice,
            TotalAmount = finalTotal,
            MatchedTierId = matchedTier?.Id,
            TierModel = PricingTierCalculationModel.AllUnits,
            MinimumChargeApplied = minApplied,
            MinimumCharge = MinimumCharge,
            CalculationSummary = minApplied
                ? $"All-Units (Tier {matchedTier?.TierIndex ?? 0} achieved @ {appliedUnitPrice:F4}) = {rawTotal:F2}; elevated to minimum charge {MinimumCharge:F2}"
                : $"All-Units (Tier {matchedTier?.TierIndex ?? 0} achieved @ {appliedUnitPrice:F4}) across all {quantity} units = {finalTotal:F2}"
        };
    }

    private RateCalculationResult CalculateGraduated(decimal quantity)
    {
        var sortedTiers = Tiers.OrderBy(t => t.TierIndex).ThenBy(t => t.TierMinQuantity).ToList();
        var rawTotal = 0m;
        var previousBracketMax = 0m;
        RateCardTier? highestAchievedTier = null;

        for (int i = 0; i < sortedTiers.Count; i++)
        {
            var tier = sortedTiers[i];
            var bracketMax = tier.TierMaxQuantity ?? decimal.MaxValue;

            if (quantity > previousBracketMax)
            {
                var sliceQty = Math.Min(quantity, bracketMax) - previousBracketMax;
                if (sliceQty > 0)
                {
                    rawTotal += decimal.Round(sliceQty * tier.UnitPrice, 4, MidpointRounding.AwayFromZero);
                    highestAchievedTier = tier;
                }
            }

            previousBracketMax = bracketMax;
        }

        rawTotal = decimal.Round(rawTotal, 4, MidpointRounding.AwayFromZero);
        var finalTotal = rawTotal;
        var minApplied = false;

        if (MinimumCharge.HasValue && finalTotal < MinimumCharge.Value)
        {
            finalTotal = MinimumCharge.Value;
            minApplied = true;
        }

        var blendedUnitPrice = quantity > 0
            ? decimal.Round(finalTotal / quantity, 4, MidpointRounding.AwayFromZero)
            : (sortedTiers.FirstOrDefault()?.UnitPrice ?? BaseRate);

        return new RateCalculationResult
        {
            Quantity = quantity,
            UnitPrice = blendedUnitPrice,
            TotalAmount = finalTotal,
            MatchedTierId = highestAchievedTier?.Id,
            TierModel = PricingTierCalculationModel.Graduated,
            MinimumChargeApplied = minApplied,
            MinimumCharge = MinimumCharge,
            CalculationSummary = minApplied
                ? $"Graduated brackets = {rawTotal:F2}; elevated to minimum charge {MinimumCharge:F2}"
                : $"Graduated bracket rating across {quantity} units = {finalTotal:F2} (blended unit price: {blendedUnitPrice:F4})"
        };
    }
}
