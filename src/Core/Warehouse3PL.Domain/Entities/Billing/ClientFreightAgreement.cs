using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Contractual commercial freight pricing and carrier markup agreement for a client tenant.
/// Governs client-billable shipping calculations, margin models, and accessorial billing rules.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public class ClientFreightAgreement : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid? FacilityId { get; set; }

    public string AgreementCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional carrier code scope (null applies to all carriers unless overridden).
    /// </summary>
    public string? CarrierCode { get; set; }

    /// <summary>
    /// Optional carrier service level scope (null applies to all service levels).
    /// </summary>
    public CarrierServiceLevel? CarrierServiceLevel { get; set; }

    public FreightPricingModel PricingModel { get; set; } = FreightPricingModel.CostPlusPercentageMarkup;

    /// <summary>
    /// Percentage markup or target margin percentage (e.g. 15.00 for 15%).
    /// </summary>
    public decimal? MarkupPercentage { get; set; }

    /// <summary>
    /// Flat handling fee added per shipment.
    /// </summary>
    public decimal? FlatFeePerShipment { get; set; }

    /// <summary>
    /// Flat handling fee added per package in the shipment.
    /// </summary>
    public decimal? FlatFeePerPackage { get; set; }

    /// <summary>
    /// Fixed contractual shipping rate (used for FixedContractualRate model).
    /// </summary>
    public decimal? FixedFreightRate { get; set; }

    /// <summary>
    /// Minimum markup dollar floor applied to the calculated markup.
    /// </summary>
    public decimal? MinimumMarkupAmount { get; set; }

    /// <summary>
    /// Maximum markup dollar ceiling applied to the calculated markup.
    /// </summary>
    public decimal? MaximumMarkupAmount { get; set; }

    /// <summary>
    /// Default billing treatment for carrier accessorial surcharges.
    /// </summary>
    public AccessorialBillableTreatment DefaultAccessorialTreatment { get; set; } = AccessorialBillableTreatment.BillableWithMarkup;

    public string Currency { get; set; } = "USD";
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility? Facility { get; set; }
    public ICollection<ClientAccessorialRule> AccessorialRules { get; set; } = new List<ClientAccessorialRule>();

    public ClientFreightAgreement()
    {
        Id = Guid.NewGuid();
    }

    public static ClientFreightAgreement Create(
        Guid clientId,
        string agreementCode,
        string name,
        FreightPricingModel pricingModel,
        string currency,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null,
        Guid? facilityId = null,
        string? carrierCode = null,
        CarrierServiceLevel? carrierServiceLevel = null,
        decimal? markupPercentage = null,
        decimal? flatFeePerShipment = null,
        decimal? flatFeePerPackage = null,
        decimal? fixedFreightRate = null,
        decimal? minimumMarkupAmount = null,
        decimal? maximumMarkupAmount = null,
        AccessorialBillableTreatment defaultAccessorialTreatment = AccessorialBillableTreatment.BillableWithMarkup,
        string? notes = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required for freight agreement.");
        }

        if (string.IsNullOrWhiteSpace(agreementCode))
        {
            throw new ValidationException(nameof(agreementCode), "Agreement code is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(nameof(name), "Agreement name is required.");
        }

        if (effectiveToUtc.HasValue && effectiveToUtc.Value <= effectiveFromUtc)
        {
            throw new ValidationException(nameof(effectiveToUtc), "Effective To date must be after Effective From date.");
        }

        if (markupPercentage.HasValue && markupPercentage.Value < 0)
        {
            throw new ValidationException(nameof(markupPercentage), "Markup percentage cannot be negative.");
        }

        if (pricingModel == FreightPricingModel.CostPlusMarginPercentage && markupPercentage.HasValue && markupPercentage.Value >= 100)
        {
            throw new ValidationException(nameof(markupPercentage), "Target margin percentage must be strictly less than 100%.");
        }

        if (minimumMarkupAmount.HasValue && minimumMarkupAmount.Value < 0)
        {
            throw new ValidationException(nameof(minimumMarkupAmount), "Minimum markup amount cannot be negative.");
        }

        if (maximumMarkupAmount.HasValue && maximumMarkupAmount.Value < 0)
        {
            throw new ValidationException(nameof(maximumMarkupAmount), "Maximum markup amount cannot be negative.");
        }

        if (minimumMarkupAmount.HasValue && maximumMarkupAmount.HasValue && maximumMarkupAmount.Value < minimumMarkupAmount.Value)
        {
            throw new ValidationException(nameof(maximumMarkupAmount), "Maximum markup amount cannot be less than minimum markup amount.");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();

        return new ClientFreightAgreement
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            AgreementCode = agreementCode.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            CarrierCode = carrierCode?.Trim().ToUpperInvariant(),
            CarrierServiceLevel = carrierServiceLevel,
            PricingModel = pricingModel,
            MarkupPercentage = markupPercentage,
            FlatFeePerShipment = flatFeePerShipment,
            FlatFeePerPackage = flatFeePerPackage,
            FixedFreightRate = fixedFreightRate,
            MinimumMarkupAmount = minimumMarkupAmount,
            MaximumMarkupAmount = maximumMarkupAmount,
            DefaultAccessorialTreatment = defaultAccessorialTreatment,
            Currency = normalizedCurrency,
            EffectiveFromUtc = effectiveFromUtc,
            EffectiveToUtc = effectiveToUtc,
            IsActive = true,
            Notes = notes
        };
    }

    /// <summary>
    /// Adds or replaces an accessorial override rule for this freight agreement.
    /// </summary>
    public ClientAccessorialRule AddAccessorialRule(
        AccessorialType accessorialType,
        AccessorialBillableTreatment treatment,
        decimal? markupPercentage = null,
        decimal? fixedFee = null)
    {
        var existing = AccessorialRules.FirstOrDefault(r => r.AccessorialType == accessorialType);
        if (existing != null)
        {
            AccessorialRules.Remove(existing);
        }

        var rule = ClientAccessorialRule.Create(ClientId, Id, accessorialType, treatment, markupPercentage, fixedFee);
        AccessorialRules.Add(rule);
        return rule;
    }

    /// <summary>
    /// Evaluates client freight charge and gross markup on a given carrier cost according to the contractual pricing model.
    /// </summary>
    public (decimal ClientFreight, decimal MarkupAmount, decimal HandlingFee) CalculatePricing(
        decimal totalCarrierCost,
        int packageCount = 1)
    {
        if (totalCarrierCost < 0)
        {
            throw new ValidationException(nameof(totalCarrierCost), "Carrier cost cannot be negative.");
        }

        var packages = Math.Max(1, packageCount);
        decimal clientFreight = 0m;
        decimal rawMarkup = 0m;
        decimal handlingFee = 0m;

        switch (PricingModel)
        {
            case FreightPricingModel.CostPlusPercentageMarkup:
                // Markup % = ((Sell - Cost) / Cost) * 100
                // Sell = Cost * (1 + Markup% / 100)
                var pct = (MarkupPercentage ?? 0m) / 100m;
                rawMarkup = decimal.Round(totalCarrierCost * pct, 4, MidpointRounding.AwayFromZero);
                rawMarkup = ApplyMarkupLimits(rawMarkup);
                clientFreight = decimal.Round(totalCarrierCost + rawMarkup, 4, MidpointRounding.AwayFromZero);
                break;

            case FreightPricingModel.CostPlusMarginPercentage:
                // Target Margin % = ((Sell - Cost) / Sell) * 100
                // Sell = Cost / (1 - Margin% / 100)
                var marginPct = (MarkupPercentage ?? 0m) / 100m;
                if (marginPct >= 1m)
                {
                    marginPct = 0.9999m;
                }
                var targetSell = totalCarrierCost / (1m - marginPct);
                rawMarkup = decimal.Round(targetSell - totalCarrierCost, 4, MidpointRounding.AwayFromZero);
                rawMarkup = ApplyMarkupLimits(rawMarkup);
                clientFreight = decimal.Round(totalCarrierCost + rawMarkup, 4, MidpointRounding.AwayFromZero);
                break;

            case FreightPricingModel.FlatFeePerShipment:
                handlingFee = decimal.Round(FlatFeePerShipment ?? 0m, 4, MidpointRounding.AwayFromZero);
                rawMarkup = 0m;
                clientFreight = decimal.Round(totalCarrierCost + handlingFee, 4, MidpointRounding.AwayFromZero);
                break;

            case FreightPricingModel.FlatFeePerPackage:
                var feePerPkg = FlatFeePerPackage ?? 0m;
                handlingFee = decimal.Round(feePerPkg * packages, 4, MidpointRounding.AwayFromZero);
                rawMarkup = 0m;
                clientFreight = decimal.Round(totalCarrierCost + handlingFee, 4, MidpointRounding.AwayFromZero);
                break;

            case FreightPricingModel.FixedContractualRate:
                clientFreight = decimal.Round(FixedFreightRate ?? 0m, 4, MidpointRounding.AwayFromZero);
                rawMarkup = decimal.Round(clientFreight - totalCarrierCost, 4, MidpointRounding.AwayFromZero);
                break;

            case FreightPricingModel.PassThroughCostOnly:
            default:
                rawMarkup = 0m;
                clientFreight = totalCarrierCost;
                break;
        }

        return (clientFreight, rawMarkup, handlingFee);
    }

    private decimal ApplyMarkupLimits(decimal rawMarkup)
    {
        var limited = rawMarkup;

        if (MinimumMarkupAmount.HasValue && limited < MinimumMarkupAmount.Value)
        {
            limited = MinimumMarkupAmount.Value;
        }

        if (MaximumMarkupAmount.HasValue && limited > MaximumMarkupAmount.Value)
        {
            limited = MaximumMarkupAmount.Value;
        }

        return decimal.Round(limited, 4, MidpointRounding.AwayFromZero);
    }
}
