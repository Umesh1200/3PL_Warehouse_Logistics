using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Specific commercial accessorial billing rule under a ClientFreightAgreement.
/// Governs whether a carrier surcharge is marked up, passed through at cost, absorbed, or billed at a fixed fee.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public class ClientAccessorialRule : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid ClientFreightAgreementId { get; set; }

    public AccessorialType AccessorialType { get; set; }
    public AccessorialBillableTreatment Treatment { get; set; } = AccessorialBillableTreatment.BillableWithMarkup;

    /// <summary>
    /// Custom markup percentage for this accessorial type (if null, agreement's markup is used).
    /// </summary>
    public decimal? MarkupPercentage { get; set; }

    /// <summary>
    /// Fixed client billing fee applied when Treatment is FixedClientFee.
    /// </summary>
    public decimal? FixedFee { get; set; }

    // Navigations
    public ClientFreightAgreement ClientFreightAgreement { get; set; } = null!;

    public ClientAccessorialRule()
    {
        Id = Guid.NewGuid();
    }

    public static ClientAccessorialRule Create(
        Guid clientId,
        Guid clientFreightAgreementId,
        AccessorialType accessorialType,
        AccessorialBillableTreatment treatment,
        decimal? markupPercentage = null,
        decimal? fixedFee = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required for accessorial rule.");
        }

        if (clientFreightAgreementId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientFreightAgreementId), "Client freight agreement ID is required.");
        }

        if (markupPercentage.HasValue && markupPercentage.Value < 0)
        {
            throw new ValidationException(nameof(markupPercentage), "Markup percentage cannot be negative.");
        }

        if (fixedFee.HasValue && fixedFee.Value < 0)
        {
            throw new ValidationException(nameof(fixedFee), "Fixed fee cannot be negative.");
        }

        return new ClientAccessorialRule
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            ClientFreightAgreementId = clientFreightAgreementId,
            AccessorialType = accessorialType,
            Treatment = treatment,
            MarkupPercentage = markupPercentage,
            FixedFee = fixedFee.HasValue ? decimal.Round(fixedFee.Value, 4, MidpointRounding.AwayFromZero) : null
        };
    }

    /// <summary>
    /// Evaluates the client charge for a given carrier accessorial cost according to this rule.
    /// </summary>
    public decimal EvaluateClientCharge(decimal carrierCost, decimal? defaultAgreementMarkupPercentage)
    {
        if (carrierCost < 0)
        {
            throw new ValidationException(nameof(carrierCost), "Carrier accessorial cost cannot be negative.");
        }

        switch (Treatment)
        {
            case AccessorialBillableTreatment.PassThroughAtCost:
                return decimal.Round(carrierCost, 4, MidpointRounding.AwayFromZero);

            case AccessorialBillableTreatment.AbsorbedByWarehouse:
                return 0m;

            case AccessorialBillableTreatment.FixedClientFee:
                return decimal.Round(FixedFee ?? 0m, 4, MidpointRounding.AwayFromZero);

            case AccessorialBillableTreatment.BillableWithMarkup:
            default:
                var effectivePct = (MarkupPercentage ?? defaultAgreementMarkupPercentage ?? 0m) / 100m;
                var markup = decimal.Round(carrierCost * effectivePct, 4, MidpointRounding.AwayFromZero);
                return decimal.Round(carrierCost + markup, 4, MidpointRounding.AwayFromZero);
        }
    }
}

