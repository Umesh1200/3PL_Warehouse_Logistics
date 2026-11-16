using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Authoritative buy-side contractual rate agreement with a freight carrier.
/// Establishes commercial buy-side pricing benchmarks, per-unit/per-weight rating rules,
/// tiered volume brackets, dimensional-weight divisors, and minimum charges.
/// DB-SPEC-3PL-2026 Phase 6.4.4 Transportation &amp; Carrier SLA Analytics.
/// </summary>
public class CarrierRateAgreement : AuditableEntity<Guid>, ISoftDeletable
{
    private static readonly HashSet<string> SupportedUnitsOfMeasure = new(StringComparer.OrdinalIgnoreCase)
    {
        "KG", "LB", "UNIT", "PIECE", "SHIPMENT"
    };

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

    // Explicit contract routing selectors. All populated selectors must match every package.
    // A zone/lane label alone is not geographic evidence.
    public string? DestinationCountryCode { get; set; }
    public string? DestinationStateOrProvince { get; set; }
    public string? DestinationPostalCodePrefix { get; set; }
    public string? DestinationFacilityCode { get; set; }

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

    private string _unitOfMeasure = "KG";

    /// <summary>
    /// Contractual unit of measure: "KG", "LB", "UNIT", "PIECE", or "SHIPMENT".
    /// </summary>
    public string UnitOfMeasure
    {
        get => _unitOfMeasure;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !SupportedUnitsOfMeasure.Contains(value.Trim()))
            {
                throw new ValidationException(nameof(UnitOfMeasure), $"Unsupported unit of measure '{value}'. Supported units are KG, LB, UNIT, PIECE, SHIPMENT.");
            }
            _unitOfMeasure = value.Trim().ToUpperInvariant();
        }
    }

    /// <summary>
    /// Contractual minimum fee enforced per shipment.
    /// </summary>
    public decimal? MinimumCharge { get; set; }

    /// <summary>
    /// Contractual dimensional weight volumetric divisor (e.g. 5000 for metric cm^3/kg, 139 for imperial in^3/lb).
    /// </summary>
    public decimal? DimensionalWeightDivisor { get; set; }

    private decimal? _weightRoundingIncrement;

    /// <summary>
    /// Contractual billable weight rounding step in the agreement's unit of measure (e.g. 0.5 kg or 1.0 lb).
    /// </summary>
    public decimal? WeightRoundingIncrement
    {
        get => _weightRoundingIncrement;
        set
        {
            if (value.HasValue && value.Value <= 0m)
            {
                throw new ValidationException(nameof(WeightRoundingIncrement), "Weight rounding increment must be strictly positive (> 0).");
            }
            _weightRoundingIncrement = value.HasValue ? decimal.Round(value.Value, 4, MidpointRounding.AwayFromZero) : null;
        }
    }

    /// <summary>
    /// Synchronized alias for WeightRoundingIncrement (backward compatibility with earlier schema).
    /// </summary>
    public decimal? WeightRoundingIncrementKg
    {
        get => WeightRoundingIncrement;
        set => WeightRoundingIncrement = value;
    }

    public string Currency { get; set; } = "USD";
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public string? Notes { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Relational Foreign Key Navigations (Finding 5)
    public Client? Client { get; set; }
    public Facility? Facility { get; set; }

    public ICollection<CarrierRateTier> Tiers { get; set; } = new List<CarrierRateTier>();

    public CarrierRateAgreement()
    {
        Id = Guid.NewGuid();
    }

    public static bool IsSupportedUnitOfMeasure(string? uom)
    {
        return !string.IsNullOrWhiteSpace(uom) && SupportedUnitsOfMeasure.Contains(uom.Trim());
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

        if (!SupportedUnitsOfMeasure.Contains(normalizedUom))
        {
            throw new ValidationException(nameof(unitOfMeasure), $"Unsupported unit of measure '{unitOfMeasure}'. Supported units are KG, LB, UNIT, PIECE, SHIPMENT.");
        }

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
            WeightRoundingIncrement = weightRoundingIncrementKg,
            IsActive = true,
            Version = version
        };
    }

    /// <summary>
    /// Adds a volume pricing bracket tier to this carrier agreement with strict domain boundary checks.
    /// </summary>
    public CarrierRateTier AddTier(int tierNumber, decimal lowerBound, decimal? upperBound, decimal rate)
    {
        if (tierNumber <= 0)
        {
            throw new ValidationException(nameof(tierNumber), "Tier number must be strictly positive (> 0).");
        }
        if (rate < 0m)
        {
            throw new ValidationException(nameof(rate), "Tier rate cannot be negative.");
        }
        if (lowerBound < 0m)
        {
            throw new ValidationException(nameof(lowerBound), "Lower bound cannot be negative.");
        }
        if (upperBound.HasValue && upperBound.Value <= lowerBound)
        {
            throw new ValidationException(nameof(upperBound), "Upper bound must be strictly greater than lower bound.");
        }
        if (Tiers.Any(t => t.TierNumber == tierNumber))
        {
            throw new ValidationException(nameof(tierNumber), $"Duplicate tier number '{tierNumber}'.");
        }

        var tier = new CarrierRateTier
        {
            Id = Guid.NewGuid(),
            CarrierRateAgreementId = Id,
            TierNumber = tierNumber,
            LowerBound = lowerBound,
            UpperBound = upperBound,
            Rate = rate,
            CarrierRateAgreement = this
        };

        Tiers.Add(tier);
        return tier;
    }

    /// <summary>
    /// Validates that tiers form an ordered, non-overlapping contiguous bracket structure without gaps.
    /// </summary>
    public static void ValidateTierStructure(IReadOnlyList<CarrierRateTier> sortedTiers)
    {
        if (sortedTiers.Count == 0) return;

        if (sortedTiers[0].LowerBound != 0m)
        {
            throw new ValidationException(nameof(CarrierRateTier.LowerBound), "First tier bracket must start at 0.");
        }

        for (int i = 1; i < sortedTiers.Count; i++)
        {
            var prev = sortedTiers[i - 1];
            var curr = sortedTiers[i];

            if (!prev.UpperBound.HasValue)
            {
                throw new ValidationException(nameof(CarrierRateTier.UpperBound), $"Tier {prev.TierNumber} is unbounded; non-terminal tier cannot have unbounded upper limit.");
            }

            if (curr.LowerBound < prev.UpperBound.Value)
            {
                throw new ValidationException(nameof(CarrierRateTier.LowerBound), $"Tier {curr.TierNumber} overlaps with Tier {prev.TierNumber}.");
            }

            if (curr.LowerBound - prev.UpperBound.Value > 0.01m)
            {
                throw new ValidationException(nameof(CarrierRateTier.LowerBound), $"Tier {curr.TierNumber} has a gap following Tier {prev.TierNumber}.");
            }
        }
    }

    /// <summary>
    /// Rates the given billable quantity against this carrier agreement.
    /// By default (isQuantityAlreadyInContractUom = false), when UnitOfMeasure is "LB", the input quantity
    /// is assumed to be in kilograms and is converted to pounds before rating.
    /// Set isQuantityAlreadyInContractUom = true when the caller has already converted the quantity to the
    /// contractual unit (e.g., the analytics service after calling CalculateExpectedBillableWeight).
    /// If valuationTime is provided, enforces historical cutoff: excludes tiers created after valuationTime,
    /// and rejects modified tiers (throws ValidationException to mark indeterminate).
    /// </summary>
    public RateCalculationResult CalculateRating(decimal billableQuantity, DateTime? valuationTime = null, bool isQuantityAlreadyInContractUom = false)
    {
        if (billableQuantity < 0m)
        {
            throw new ValidationException(nameof(billableQuantity), "Quantity to rate cannot be negative.");
        }

        if (!SupportedUnitsOfMeasure.Contains(UnitOfMeasure))
        {
            throw new ValidationException(nameof(UnitOfMeasure), $"Unsupported unit of measure '{UnitOfMeasure}'.");
        }

        // Convert kg → lb when UOM is LB and quantity is not already in contractual units (Finding 3)
        if (!isQuantityAlreadyInContractUom && string.Equals(UnitOfMeasure, "LB", StringComparison.OrdinalIgnoreCase))
        {
            billableQuantity = decimal.Round(billableQuantity * 2.20462262m, 4, MidpointRounding.AwayFromZero);
        }

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
                TotalAmount = decimal.Round(total, 2, MidpointRounding.AwayFromZero),
                TierModel = PricingTierCalculationModel.FlatRate,
                MinimumChargeApplied = minApplied,
                MinimumCharge = MinimumCharge
            };
        }

        // Apply historical valuation cutoff to rate tiers (Finding 1)
        List<CarrierRateTier> activeTiers;
        if (valuationTime.HasValue)
        {
            // If any tier in agreement was modified after valuation cutoff, historical terms are untrusted -> indeterminate
            if (Tiers.Any(t => t.CreatedAt <= valuationTime.Value && t.UpdatedAt.HasValue && t.UpdatedAt.Value > valuationTime.Value))
            {
                throw new ValidationException("CarrierRateTier", "Carrier rate tier was modified after valuation cutoff timestamp, rendering historical rating indeterminate.");
            }

            // Include only tiers created at or before valuation cutoff
            activeTiers = Tiers
                .Where(t => t.CreatedAt <= valuationTime.Value)
                .OrderBy(t => t.TierNumber)
                .ToList();
        }
        else
        {
            activeTiers = Tiers.OrderBy(t => t.TierNumber).ToList();
        }

        if (activeTiers.Count == 0 || TierModel == PricingTierCalculationModel.FlatRate)
        {
            decimal rawTotal = decimal.Round(billableQuantity * BaseRate, 4, MidpointRounding.AwayFromZero);
            decimal total = MinimumCharge.HasValue && rawTotal < MinimumCharge.Value ? MinimumCharge.Value : rawTotal;
            bool minApplied = MinimumCharge.HasValue && total == MinimumCharge.Value;
            return new RateCalculationResult
            {
                Quantity = billableQuantity,
                UnitPrice = BaseRate,
                TotalAmount = decimal.Round(total, 2, MidpointRounding.AwayFromZero),
                TierModel = PricingTierCalculationModel.FlatRate,
                MinimumChargeApplied = minApplied,
                MinimumCharge = MinimumCharge
            };
        }

        // Validate tier structure for gaps and overlaps (Finding 6)
        ValidateTierStructure(activeTiers);

        if (TierModel == PricingTierCalculationModel.AllUnits)
        {
            CarrierRateTier? matchedTier = null;

            if (billableQuantity == 0m)
            {
                matchedTier = activeTiers.FirstOrDefault();
            }
            else
            {
                // Bracket convention: [LowerBound, UpperBound]
                matchedTier = activeTiers.FirstOrDefault(t =>
                    billableQuantity >= t.LowerBound &&
                    (!t.UpperBound.HasValue || billableQuantity <= t.UpperBound.Value));

                if (matchedTier == null && activeTiers.Count > 0)
                {
                    matchedTier = activeTiers.Last();
                }
            }

            decimal unitRate = matchedTier != null ? matchedTier.Rate : BaseRate;
            decimal rawTotal = decimal.Round(billableQuantity * unitRate, 4, MidpointRounding.AwayFromZero);
            decimal total = MinimumCharge.HasValue && rawTotal < MinimumCharge.Value ? MinimumCharge.Value : rawTotal;
            bool minApplied = MinimumCharge.HasValue && total == MinimumCharge.Value;

            return new RateCalculationResult
            {
                Quantity = billableQuantity,
                UnitPrice = unitRate,
                TotalAmount = decimal.Round(total, 2, MidpointRounding.AwayFromZero),
                TierModel = PricingTierCalculationModel.AllUnits,
                MinimumChargeApplied = minApplied,
                MinimumCharge = MinimumCharge
            };
        }
        else // Graduated bracket rating
        {
            decimal accumulated = 0m;
            decimal previousBracketMax = 0m;

            for (int i = 0; i < activeTiers.Count; i++)
            {
                var tier = activeTiers[i];
                var bracketMax = tier.UpperBound ?? decimal.MaxValue;

                if (billableQuantity > previousBracketMax)
                {
                    var sliceQty = Math.Min(billableQuantity, bracketMax) - previousBracketMax;
                    if (sliceQty > 0)
                    {
                        accumulated += decimal.Round(sliceQty * tier.Rate, 4, MidpointRounding.AwayFromZero);
                    }
                }

                previousBracketMax = bracketMax;
            }

            // Excess beyond last bounded tier rates at BaseRate
            if (billableQuantity > previousBracketMax && activeTiers.Count > 0 && activeTiers.Last().UpperBound.HasValue)
            {
                var excess = billableQuantity - previousBracketMax;
                accumulated += decimal.Round(excess * BaseRate, 4, MidpointRounding.AwayFromZero);
            }

            accumulated = decimal.Round(accumulated, 4, MidpointRounding.AwayFromZero);
            decimal total = MinimumCharge.HasValue && accumulated < MinimumCharge.Value ? MinimumCharge.Value : accumulated;
            bool minApplied = MinimumCharge.HasValue && total == MinimumCharge.Value;

            return new RateCalculationResult
            {
                Quantity = billableQuantity,
                UnitPrice = BaseRate,
                TotalAmount = decimal.Round(total, 2, MidpointRounding.AwayFromZero),
                TierModel = PricingTierCalculationModel.Graduated,
                MinimumChargeApplied = minApplied,
                MinimumCharge = MinimumCharge
            };
        }
    }
}
