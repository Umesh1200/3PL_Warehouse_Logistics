using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Authoritative buy-side contractual rate agreement with a freight carrier.
/// Establishes commercial buy-side pricing benchmarks, per-unit/per-weight rating rules,
/// tiered volume brackets, dimensional-weight divisors, and minimum charges.
/// DB-SPEC-3PL-2026 Phase 6.4.4 Transportation &amp; Carrier SLA Analytics.
/// </summary>
public class CarrierRateAgreement : AuditableEntity<Guid>
{
    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel? CarrierServiceLevel { get; set; }
    public string AgreementCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional origin facility scope. If null, applies network-wide across all facilities.
    /// </summary>
    public Guid? FacilityId { get; set; }

    /// <summary>
    /// Optional client scope for client-dedicated or client-subsidized carrier contract accounts.
    /// If null, applies as the default 3PL master carrier agreement.
    /// </summary>
    public Guid? ClientId { get; set; }

    /// <summary>
    /// Optional geographic zone or lane definition (e.g., "Zone 2", "ORD-DFW").
    /// </summary>
    public string? ZoneOrLane { get; set; }

    /// <summary>
    /// Contractual rating model: PerUnit (weight/count) or FlatFee.
    /// </summary>
    public RateModelType RateModel { get; set; } = RateModelType.PerUnit;

    /// <summary>
    /// Tier calculation model (FlatRate, AllUnits, Graduated).
    /// </summary>
    public PricingTierCalculationModel TierModel { get; set; } = PricingTierCalculationModel.FlatRate;

    /// <summary>
    /// Base contractual unit or flat rate.
    /// </summary>
    public decimal BaseRate { get; set; }

    /// <summary>
    /// Contractual unit of measure: "KG", "LB", or "UNIT".
    /// </summary>
    public string UnitOfMeasure { get; set; } = "KG";

    /// <summary>
    /// Contractual minimum fee enforced per shipment.
    /// </summary>
    public decimal? MinimumCharge { get; set; }

    /// <summary>
    /// Contractual dimensional weight volumetric divisor (e.g. 5000 for metric cm^3/kg, 139 for imperial in^3/lb).
    /// </summary>
    public decimal? DimensionalWeightDivisor { get; set; }

    /// <summary>
    /// Contractual billable weight rounding step (e.g. 0.5 kg or 1.0 lb).
    /// </summary>
    public decimal? WeightRoundingIncrementKg { get; set; }

    public string Currency { get; set; } = "USD";
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public string? Notes { get; set; }

    public ICollection<CarrierRateTier> Tiers { get; set; } = new List<CarrierRateTier>();

    public CarrierRateAgreement()
    {
        Id = Guid.NewGuid();
    }

    public static CarrierRateAgreement Create(
        string carrierCode,
        string agreementCode,
        string name,
        decimal baseRate,
        string currency = "USD",
        string unitOfMeasure = "KG",
        RateModelType rateModel = RateModelType.PerUnit,
        PricingTierCalculationModel tierModel = PricingTierCalculationModel.FlatRate,
        DateTime? effectiveFromUtc = null,
        DateTime? effectiveToUtc = null,
        CarrierServiceLevel? serviceLevel = null,
        Guid? facilityId = null,
        Guid? clientId = null,
        string? zoneOrLane = null,
        decimal? minimumCharge = null,
        decimal? dimensionalWeightDivisor = null,
        decimal? weightRoundingIncrementKg = null,
        int version = 1)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            throw new ValidationException(nameof(carrierCode), "Carrier code is required for carrier rate agreement.");
        }
        if (string.IsNullOrWhiteSpace(agreementCode))
        {
            throw new ValidationException(nameof(agreementCode), "Agreement code is required.");
        }
        if (baseRate < 0m)
        {
            throw new ValidationException(nameof(baseRate), "Base rate cannot be negative.");
        }
        if (minimumCharge.HasValue && minimumCharge.Value < 0m)
        {
            throw new ValidationException(nameof(minimumCharge), "Minimum charge cannot be negative.");
        }
        if (weightRoundingIncrementKg.HasValue && weightRoundingIncrementKg.Value <= 0m)
        {
            throw new ValidationException(nameof(weightRoundingIncrementKg), "Weight rounding increment must be strictly positive.");
        }
        if (dimensionalWeightDivisor.HasValue && dimensionalWeightDivisor.Value <= 0m)
        {
            throw new ValidationException(nameof(dimensionalWeightDivisor), "Dimensional weight divisor must be strictly positive.");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        var normalizedUom = string.IsNullOrWhiteSpace(unitOfMeasure) ? "KG" : unitOfMeasure.Trim().ToUpperInvariant();

        return new CarrierRateAgreement
        {
            Id = Guid.NewGuid(),
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            AgreementCode = agreementCode.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            BaseRate = baseRate,
            Currency = normalizedCurrency,
            UnitOfMeasure = normalizedUom,
            RateModel = rateModel,
            TierModel = tierModel,
            EffectiveFromUtc = effectiveFromUtc ?? DateTime.UtcNow,
            EffectiveToUtc = effectiveToUtc,
            CarrierServiceLevel = serviceLevel,
            FacilityId = facilityId,
            ClientId = clientId,
            ZoneOrLane = zoneOrLane?.Trim(),
            MinimumCharge = minimumCharge,
            DimensionalWeightDivisor = dimensionalWeightDivisor,
            WeightRoundingIncrementKg = weightRoundingIncrementKg,
            IsActive = true,
            Version = version
        };
    }

    public void AddTier(int tierNumber, decimal lowerBound, decimal? upperBound, decimal rate)
    {
        if (rate < 0m) throw new ValidationException(nameof(rate), "Tier rate cannot be negative.");
        if (lowerBound < 0m) throw new ValidationException(nameof(lowerBound), "Lower bound cannot be negative.");
        if (upperBound.HasValue && upperBound.Value <= lowerBound)
        {
            throw new ValidationException(nameof(upperBound), "Upper bound must be greater than lower bound.");
        }

        Tiers.Add(new CarrierRateTier
        {
            Id = Guid.NewGuid(),
            CarrierRateAgreementId = Id,
            TierNumber = tierNumber,
            LowerBound = lowerBound,
            UpperBound = upperBound,
            Rate = rate
        });
    }

    /// <summary>
    /// Rates the expected billable weight (in kg) against this carrier agreement.
    /// Handles automatic conversion to LB if contractual unit of measure is LB (1 kg = 2.20462262 lb).
    /// </summary>
    public RateCalculationResult CalculateRating(decimal expectedBillableWeightKg)
    {
        if (expectedBillableWeightKg < 0m)
        {
            throw new ValidationException(nameof(expectedBillableWeightKg), "Weight cannot be negative.");
        }

        decimal ratedQuantity = string.Equals(UnitOfMeasure, "LB", StringComparison.OrdinalIgnoreCase)
            ? decimal.Round(expectedBillableWeightKg * 2.20462262m, 4, MidpointRounding.AwayFromZero)
            : expectedBillableWeightKg;

        if (RateModel == RateModelType.FlatFee)
        {
            decimal total = BaseRate;
            bool minApplied = false;
            if (MinimumCharge.HasValue && total < MinimumCharge.Value)
            {
                total = MinimumCharge.Value;
                minApplied = true;
            }
            return new RateCalculationResult
            {
                Quantity = 1m,
                UnitPrice = BaseRate,
                TotalAmount = total,
                TierModel = PricingTierCalculationModel.FlatRate,
                MinimumChargeApplied = minApplied,
                MinimumCharge = MinimumCharge
            };
        }

        if (Tiers.Count == 0 || TierModel == PricingTierCalculationModel.FlatRate)
        {
            decimal rawTotal = decimal.Round(ratedQuantity * BaseRate, 4, MidpointRounding.AwayFromZero);
            decimal total = MinimumCharge.HasValue && rawTotal < MinimumCharge.Value ? MinimumCharge.Value : rawTotal;
            bool minApplied = MinimumCharge.HasValue && total == MinimumCharge.Value;
            return new RateCalculationResult
            {
                Quantity = ratedQuantity,
                UnitPrice = BaseRate,
                TotalAmount = total,
                TierModel = PricingTierCalculationModel.FlatRate,
                MinimumChargeApplied = minApplied,
                MinimumCharge = MinimumCharge
            };
        }

        var sortedTiers = Tiers.OrderBy(t => t.TierNumber).ToList();
        if (TierModel == PricingTierCalculationModel.AllUnits)
        {
            var matchedTier = sortedTiers.FirstOrDefault(t =>
                ratedQuantity >= t.LowerBound &&
                (!t.UpperBound.HasValue || ratedQuantity <= t.UpperBound.Value));

            decimal unitRate = matchedTier != null ? matchedTier.Rate : BaseRate;
            decimal rawTotal = decimal.Round(ratedQuantity * unitRate, 4, MidpointRounding.AwayFromZero);
            decimal total = MinimumCharge.HasValue && rawTotal < MinimumCharge.Value ? MinimumCharge.Value : rawTotal;
            bool minApplied = MinimumCharge.HasValue && total == MinimumCharge.Value;
            return new RateCalculationResult
            {
                Quantity = ratedQuantity,
                UnitPrice = unitRate,
                TotalAmount = total,
                TierModel = PricingTierCalculationModel.AllUnits,
                MinimumChargeApplied = minApplied,
                MinimumCharge = MinimumCharge
            };
        }
        else // Graduated
        {
            decimal accumulated = 0m;
            decimal remaining = ratedQuantity;

            foreach (var tier in sortedTiers)
            {
                if (remaining <= 0m) break;
                decimal tierSpan = tier.UpperBound.HasValue ? tier.UpperBound.Value - tier.LowerBound : remaining;
                decimal applicable = Math.Min(remaining, tierSpan);
                accumulated += applicable * tier.Rate;
                remaining -= applicable;
            }

            if (remaining > 0m)
            {
                accumulated += remaining * BaseRate;
            }

            accumulated = decimal.Round(accumulated, 4, MidpointRounding.AwayFromZero);
            decimal total = MinimumCharge.HasValue && accumulated < MinimumCharge.Value ? MinimumCharge.Value : accumulated;
            bool minApplied = MinimumCharge.HasValue && total == MinimumCharge.Value;
            return new RateCalculationResult
            {
                Quantity = ratedQuantity,
                UnitPrice = BaseRate,
                TotalAmount = total,
                TierModel = PricingTierCalculationModel.Graduated,
                MinimumChargeApplied = minApplied,
                MinimumCharge = MinimumCharge
            };
        }
    }
}
