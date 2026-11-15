using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Aggregated read model summarizing freight spend drift, accessorial charges, and quote-versus-billed variances.
/// Distinguishes between estimated variance, billed invoice variance, and fully reconciled charges.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public class CarrierFreightCostSummary : AuditableEntity<Guid>
{
    public string CarrierCode { get; set; } = string.Empty;
    public Guid FacilityId { get; set; }
    public Guid? ClientId { get; set; }

    public DateTime DateUtc { get; set; }
    public TimeGrain TimeGrain { get; set; } = TimeGrain.Daily;
    public string Currency { get; set; } = "USD";

    // Shipment Counts
    public int TotalShipmentsQuotedCount { get; set; }
    public int TotalShipmentsBilledCount { get; set; }
    public int TotalShipmentsReconciledCount { get; set; }

    // Quoted Baseline (from initial order routing / rate shop)
    public decimal TotalQuotedBaseFreightUsd { get; set; }
    public decimal TotalQuotedAccessorialsUsd { get; set; }
    public decimal TotalQuotedAmountUsd { get; set; }

    // Estimated Freight (from carrier manifest / dispatch scan)
    public decimal TotalEstimatedAmountUsd { get; set; }

    // Authoritative Billed Freight (from carrier invoice)
    public decimal TotalActualBaseFreightUsd { get; set; }
    public decimal TotalActualAccessorialsUsd { get; set; }
    public decimal TotalBilledAmountUsd { get; set; }

    // Surcharge Breakdown (Normalized in USD)
    public decimal FuelSurchargeUsd { get; set; }
    public decimal ResidentialSurchargeUsd { get; set; }
    public decimal LiftgateFeeUsd { get; set; }
    public decimal AddressCorrectionFeeUsd { get; set; }
    public decimal OtherAdjustmentsUsd { get; set; }

    // Variance Metrics (Actual Billed - Quoted Baseline)
    public decimal EstimatedVarianceUsd { get; set; }
    public decimal BilledVarianceUsd { get; set; }
    public decimal ReconciledVarianceUsd { get; set; }
    public decimal? AccessorialDensityRatio { get; set; }

    public CarrierFreightCostSummary()
    {
        Id = Guid.NewGuid();
    }

    public static CarrierFreightCostSummary Create(
        string carrierCode,
        Guid facilityId,
        DateTime dateUtc,
        TimeGrain timeGrain = TimeGrain.Daily,
        string currency = "USD",
        Guid? clientId = null)
    {
        if (string.IsNullOrWhiteSpace(carrierCode)) throw new ValidationException(nameof(carrierCode), "Carrier code is required.");
        if (facilityId == Guid.Empty) throw new ValidationException(nameof(facilityId), "Facility ID is required.");

        return new CarrierFreightCostSummary
        {
            Id = Guid.NewGuid(),
            CarrierCode = carrierCode.Trim().ToUpperInvariant(),
            FacilityId = facilityId,
            ClientId = clientId,
            DateUtc = dateUtc.Kind == DateTimeKind.Utc ? dateUtc : DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc),
            TimeGrain = timeGrain,
            Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant()
        };
    }

    public void RecordQuote(decimal baseFreight, decimal accessorials)
    {
        TotalShipmentsQuotedCount++;
        TotalQuotedBaseFreightUsd += baseFreight;
        TotalQuotedAccessorialsUsd += accessorials;
        TotalQuotedAmountUsd += (baseFreight + accessorials);
        Recalculate();
    }

    public void RecordEstimate(decimal estimatedTotal)
    {
        TotalEstimatedAmountUsd += estimatedTotal;
        Recalculate();
    }

    public void RecordBilledCharge(
        decimal baseFreight,
        decimal accessorials,
        decimal fuelSurcharge = 0m,
        decimal residential = 0m,
        decimal liftgate = 0m,
        decimal addressCorrection = 0m,
        decimal other = 0m)
    {
        TotalShipmentsBilledCount++;
        TotalActualBaseFreightUsd += baseFreight;
        TotalActualAccessorialsUsd += accessorials;
        decimal totalBilled = baseFreight + accessorials;
        TotalBilledAmountUsd += totalBilled;

        FuelSurchargeUsd += fuelSurcharge;
        ResidentialSurchargeUsd += residential;
        LiftgateFeeUsd += liftgate;
        AddressCorrectionFeeUsd += addressCorrection;
        OtherAdjustmentsUsd += other;

        Recalculate();
    }

    public void RecordReconciliation(decimal reconciledAmount)
    {
        TotalShipmentsReconciledCount++;
        ReconciledVarianceUsd = TotalBilledAmountUsd - TotalQuotedAmountUsd;
    }

    public void Recalculate()
    {
        EstimatedVarianceUsd = TotalEstimatedAmountUsd - TotalQuotedAmountUsd;
        BilledVarianceUsd = TotalBilledAmountUsd - TotalQuotedAmountUsd;

        AccessorialDensityRatio = TotalShipmentsBilledCount > 0
            ? Math.Round(TotalActualAccessorialsUsd / TotalShipmentsBilledCount, 2)
            : (TotalShipmentsQuotedCount > 0 ? Math.Round(TotalQuotedAccessorialsUsd / TotalShipmentsQuotedCount, 2) : null);
    }
}
