using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Authoritative immutable commercial freight margin ledger record.
/// Reconciles buy-side carrier costs against sell-side client billable freight,
/// tracking gross freight margin, markup percentage, margin percentage, and post-delivery adjustments.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public class ShipmentFreightMarginRecord : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid CarrierCostRecordId { get; set; }
    public Guid? FreightAgreementId { get; set; }
    public Guid? BillableActivityId { get; set; }

    public string CarrierCode { get; set; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;
    public string? TrackingNumber { get; set; }
    public FreightPricingModel PricingModelApplied { get; set; } = FreightPricingModel.CostPlusPercentageMarkup;
    public int PackageCount { get; set; } = 1;

    // Buy-Side Carrier Financials
    public decimal CarrierBaseCost { get; set; }
    public decimal CarrierFuelSurcharge { get; set; }
    public decimal CarrierInsuranceCost { get; set; }
    public decimal CarrierAccessorialCost { get; set; }
    public decimal TotalCarrierCost { get; set; }

    // Sell-Side Client Financials
    public decimal ClientBaseFreight { get; set; }
    public decimal ClientFuelCharge { get; set; }
    public decimal ClientInsuranceCharge { get; set; }
    public decimal ClientAccessorialCharge { get; set; }
    public decimal ClientMarkupAmount { get; set; }
    public decimal ClientHandlingFee { get; set; }
    public decimal TotalClientFreight { get; set; }

    // Commercial Margin Metrics
    /// <summary>
    /// TotalClientFreight - TotalCarrierCost.
    /// </summary>
    public decimal GrossMarginAmount { get; set; }

    /// <summary>
    /// Markup % = ((Sell - Cost) / Cost) * 100.
    /// </summary>
    public decimal MarkupPercentage { get; set; }

    /// <summary>
    /// Margin % = ((Sell - Cost) / Sell) * 100.
    /// </summary>
    public decimal MarginPercentage { get; set; }

    public string Currency { get; set; } = "USD";
    public DateTime RatedAtUtc { get; set; }
    public bool IsReconciled { get; set; }
    public int AdjustmentCount { get; set; }

    // Net Adjusted Values (Updated upon invoice reconciliation or late surcharges)
    public decimal? NetAdjustedCarrierCost { get; set; }
    public decimal? NetAdjustedClientFreight { get; set; }
    public decimal? NetAdjustedMargin { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Shipment Shipment { get; set; } = null!;
    public CarrierCostRecord CarrierCostRecord { get; set; } = null!;
    public ClientFreightAgreement? FreightAgreement { get; set; }
    public BillableActivity? BillableActivity { get; set; }

    public ShipmentFreightMarginRecord()
    {
        Id = Guid.NewGuid();
    }

    public static ShipmentFreightMarginRecord Create(
        Guid clientId,
        Guid facilityId,
        Guid shipmentId,
        Guid carrierCostRecordId,
        string carrierCode,
        CarrierServiceLevel carrierServiceLevel,
        FreightPricingModel pricingModelApplied,
        int packageCount,
        decimal carrierBaseCost,
        decimal carrierFuelSurcharge,
        decimal carrierInsuranceCost,
        decimal carrierAccessorialCost,
        decimal clientBaseFreight,
        decimal clientFuelCharge,
        decimal clientInsuranceCharge,
        decimal clientAccessorialCharge,
        decimal clientMarkupAmount,
        decimal clientHandlingFee,
        string currency,
        string idempotencyKey,
        DateTime ratedAtUtc,
        Guid? freightAgreementId = null,
        Guid? billableActivityId = null,
        string? trackingNumber = null)
    {
        if (clientId == Guid.Empty) throw new ValidationException(nameof(clientId), "Client ID is required.");
        if (facilityId == Guid.Empty) throw new ValidationException(nameof(facilityId), "Facility ID is required.");
        if (shipmentId == Guid.Empty) throw new ValidationException(nameof(shipmentId), "Shipment ID is required.");
        if (carrierCostRecordId == Guid.Empty) throw new ValidationException(nameof(carrierCostRecordId), "Carrier cost record ID is required.");

        var totalCost = decimal.Round(carrierBaseCost + carrierFuelSurcharge + carrierInsuranceCost + carrierAccessorialCost, 4, MidpointRounding.AwayFromZero);
        var totalSell = decimal.Round(clientBaseFreight + clientFuelCharge + clientInsuranceCharge + clientAccessorialCharge + clientMarkupAmount + clientHandlingFee, 4, MidpointRounding.AwayFromZero);
        var margin = decimal.Round(totalSell - totalCost, 4, MidpointRounding.AwayFromZero);

        var markupPct = totalCost > 0m
            ? decimal.Round((margin / totalCost) * 100m, 2, MidpointRounding.AwayFromZero)
            : (totalSell > 0m ? 100m : 0m);

        var marginPct = totalSell > 0m
            ? decimal.Round((margin / totalSell) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        var normalizedKey = idempotencyKey?.Trim() ?? string.Empty;

        return new ShipmentFreightMarginRecord
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            ShipmentId = shipmentId,
            CarrierCostRecordId = carrierCostRecordId,
            FreightAgreementId = freightAgreementId,
            BillableActivityId = billableActivityId,
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            CarrierServiceLevel = carrierServiceLevel,
            TrackingNumber = trackingNumber?.Trim(),
            PricingModelApplied = pricingModelApplied,
            PackageCount = Math.Max(1, packageCount),
            CarrierBaseCost = decimal.Round(carrierBaseCost, 4, MidpointRounding.AwayFromZero),
            CarrierFuelSurcharge = decimal.Round(carrierFuelSurcharge, 4, MidpointRounding.AwayFromZero),
            CarrierInsuranceCost = decimal.Round(carrierInsuranceCost, 4, MidpointRounding.AwayFromZero),
            CarrierAccessorialCost = decimal.Round(carrierAccessorialCost, 4, MidpointRounding.AwayFromZero),
            TotalCarrierCost = totalCost,
            ClientBaseFreight = decimal.Round(clientBaseFreight, 4, MidpointRounding.AwayFromZero),
            ClientFuelCharge = decimal.Round(clientFuelCharge, 4, MidpointRounding.AwayFromZero),
            ClientInsuranceCharge = decimal.Round(clientInsuranceCharge, 4, MidpointRounding.AwayFromZero),
            ClientAccessorialCharge = decimal.Round(clientAccessorialCharge, 4, MidpointRounding.AwayFromZero),
            ClientMarkupAmount = decimal.Round(clientMarkupAmount, 4, MidpointRounding.AwayFromZero),
            ClientHandlingFee = decimal.Round(clientHandlingFee, 4, MidpointRounding.AwayFromZero),
            TotalClientFreight = totalSell,
            GrossMarginAmount = margin,
            MarkupPercentage = markupPct,
            MarginPercentage = marginPct,
            Currency = normalizedCurrency,
            RatedAtUtc = ratedAtUtc == default ? DateTime.UtcNow : ratedAtUtc,
            IsReconciled = false,
            AdjustmentCount = 0,
            NetAdjustedCarrierCost = totalCost,
            NetAdjustedClientFreight = totalSell,
            NetAdjustedMargin = margin,
            IdempotencyKey = normalizedKey
        };
    }

    /// <summary>
    /// Applies a post-rating or post-delivery carrier cost adjustment to this margin ledger record.
    /// Preserves original baseline snapshot while updating net adjusted metrics.
    /// </summary>
    public void ApplyAdjustment(decimal carrierCostDelta, decimal clientFreightDelta)
    {
        AdjustmentCount++;
        var currentNetCost = NetAdjustedCarrierCost ?? TotalCarrierCost;
        var currentNetSell = NetAdjustedClientFreight ?? TotalClientFreight;

        var newNetCost = decimal.Round(currentNetCost + carrierCostDelta, 4, MidpointRounding.AwayFromZero);
        var newNetSell = decimal.Round(currentNetSell + clientFreightDelta, 4, MidpointRounding.AwayFromZero);
        var newNetMargin = decimal.Round(newNetSell - newNetCost, 4, MidpointRounding.AwayFromZero);

        NetAdjustedCarrierCost = newNetCost;
        NetAdjustedClientFreight = newNetSell;
        NetAdjustedMargin = newNetMargin;
    }
}

