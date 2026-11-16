using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

/// <summary>
/// Macro-level analytical freight spend summary comparing quoted, estimated, billed, and reconciled amounts.
/// Strictly distinguishes projected routing quotes from authoritative carrier invoice spend.
/// DB-SPEC-3PL-2026 Phase 6.4.4.2 Variance &amp; Accessorial Discrepancy Analytics.
/// </summary>
public record FreightCostVarianceSummaryDto
{
    public string CarrierCode { get; init; } = string.Empty;
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public DateTime StartDateUtc { get; init; }
    public DateTime EndDateUtc { get; init; }
    public DateTime ValuationTimeUtc { get; init; }

    // Volume Counts
    public int TotalShipmentsQuotedCount { get; init; }
    public int TotalShipmentsBilledCount { get; init; }
    public int TotalShipmentsReconciledCount { get; init; }

    // Financial Spend Baseline ($ USD)
    public decimal TotalQuotedAmountUsd { get; init; }
    public decimal TotalEstimatedAmountUsd { get; init; }
    public decimal TotalBilledAmountUsd { get; init; }
    public decimal TotalReconciledAmountUsd { get; init; }

    // Dollar Variances ($ USD)
    public decimal EstimatedVarianceUsd { get; init; }
    public decimal BilledVarianceUsd { get; init; }
    public decimal ReconciledVarianceUsd { get; init; }

    /// <summary>
    /// Variance % = ((TotalBilled - TotalQuoted) / TotalQuoted) * 100.
    /// Emits null when TotalQuotedAmountUsd is zero or negative.
    /// </summary>
    public decimal? VariancePercent { get; init; }
}

/// <summary>
/// Commercial freight margin and profitability analytics model.
/// Separates Estimated, Invoiced, and Realized margins so that projected margins are never conflated with realized earnings.
/// Note: RealizedGrossMarginUsd represents accrual-based reconciled billing margin, not cash treasury profit.
/// DB-SPEC-3PL-2026 Phase 6.4.4.2 Variance &amp; Accessorial Discrepancy Analytics.
/// </summary>
public record FreightProfitabilitySummaryDto
{
    public string CarrierCode { get; init; } = string.Empty;
    public CarrierServiceLevel? ServiceLevel { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public DateTime StartDateUtc { get; init; }
    public DateTime EndDateUtc { get; init; }
    public DateTime ValuationTimeUtc { get; init; }

    public int TotalShipmentsCount { get; init; }
    public decimal TotalClientRevenueUsd { get; init; }
    public decimal TotalCarrierCostUsd { get; init; }

    // Multi-Stage Freight Margins ($ USD and %)
    public decimal EstimatedGrossMarginUsd { get; init; }
    public decimal? EstimatedMarginPercent { get; init; }

    public decimal InvoicedGrossMarginUsd { get; init; }
    public decimal? InvoicedMarginPercent { get; init; }
    public decimal? InvoicedMarkupPercent { get; init; }

    public decimal RealizedGrossMarginUsd { get; init; }
    public decimal? RealizedMarginPercent { get; init; }

    // Unit Economics ($ USD per shipment)
    // Conforms to null-for-zero-denominator analytics rule: null when TotalShipmentsCount is 0.
    public decimal? AverageRevenuePerShipmentUsd { get; init; }
    public decimal? AverageCostPerShipmentUsd { get; init; }
    public decimal? AverageMarginPerShipmentUsd { get; init; }
}

/// <summary>
/// Surcharge recovery and operational cost leakage analytics model.
/// Details carrier accessorial charges, client billing recovery, and unbilled operational leakage.
/// DB-SPEC-3PL-2026 Phase 6.4.4.2 Variance &amp; Accessorial Discrepancy Analytics.
/// </summary>
public record AccessorialLeakageAnalysisDto
{
    public string CarrierCode { get; init; } = string.Empty;
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public DateTime StartDateUtc { get; init; }
    public DateTime EndDateUtc { get; init; }
    public DateTime ValuationTimeUtc { get; init; }

    public decimal TotalCarrierAccessorialsUsd { get; init; }
    public decimal TotalClientBilledAccessorialsUsd { get; init; }
    public decimal TotalNetLeakageAmountUsd { get; init; }
    public decimal TotalContractuallyAbsorbedUsd { get; init; }

    /// <summary>
    /// Recovery Rate % = (TotalClientBilledAccessorialsUsd / TotalCarrierAccessorialsUsd) * 100.
    /// Emits null when TotalCarrierAccessorialsUsd is zero or negative.
    /// </summary>
    public decimal? AccessorialRecoveryRatePercent { get; init; }

    public IReadOnlyList<AccessorialTypeBreakdownDto> BreakdownByType { get; init; } = [];
    public IReadOnlyList<AccessorialLeakageCategoryBreakdownDto> BreakdownByLeakageCategory { get; init; } = [];
}

/// <summary>
/// Granular breakdown of accessorial surcharges by specific AccessorialType.
/// </summary>
public record AccessorialTypeBreakdownDto
{
    public AccessorialType AccessorialType { get; init; }
    public decimal CarrierAmountUsd { get; init; }
    public decimal ClientBilledUsd { get; init; }
    public decimal NetLeakageUsd { get; init; }
    public decimal ContractuallyAbsorbedUsd { get; init; }
    public decimal? RecoveryRatePercent { get; init; }
    public int OccurrenceCount { get; init; }
}

/// <summary>
/// Categorical breakdown of unrecovered accessorial surcharges by root-cause leakage category.
/// </summary>
public record AccessorialLeakageCategoryBreakdownDto
{
    public AccessorialLeakageCategory Category { get; init; }
    public decimal AmountUsd { get; init; }
    public int Count { get; init; }
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Granular shipment-level discrepancy record in the controller audit queue.
/// Quarantined disputed items are flagged to ensure they are excluded from reconciled costs.
/// Tracks stable invoice-line identity and separates operational quote variances from contractual rate card variances.
/// DB-SPEC-3PL-2026 Phase 6.4.4.2 Variance &amp; Accessorial Discrepancy Analytics.
/// </summary>
public record CarrierInvoiceDiscrepancyDto
{
    public Guid ShipmentId { get; init; }
    public string ShipmentNumber { get; init; } = string.Empty;
    public string TrackingNumber { get; init; } = string.Empty;
    public string CarrierCode { get; init; } = string.Empty;

    // Stable invoice-line identity (never collapsed)
    public string CarrierInvoiceNumber { get; init; } = string.Empty;
    public IReadOnlyList<string> CarrierInvoiceNumbers { get; init; } = [];
    public Guid? CarrierCostRecordId { get; init; }
    public string InvoiceLineIdentifier { get; init; } = string.Empty;

    public decimal ExpectedCostUsd { get; init; }
    public decimal InvoicedCostUsd { get; init; }
    public decimal DiscrepancyAmountUsd { get; init; }
    public decimal? DiscrepancyPercent { get; init; }

    // Separated variance dimensions (Gate E / F)
    public decimal? OperationalQuoteVarianceUsd { get; init; }
    public decimal? ContractualRateCardVarianceUsd { get; init; }
    public decimal? InvoiceLineDiscrepancyUsd { get; init; }
    public decimal? DisputedQuarantinedAmountUsd { get; init; }

    public FreightReconciliationStatus ReconciliationStatus { get; init; }
    public DiscrepancyRootCauseCategory RootCauseCategory { get; init; }
    public string DiscrepancyReason { get; init; } = string.Empty;
    public DateTime IdentifiedAtUtc { get; init; }
    public bool IsQuarantinedFromReconciledTotals { get; init; }
}

/// <summary>
/// Contractual rate card and dimensional weight compliance analytical model.
/// Audits carrier invoice lines against contractual expected billable weight (max(scale, dim)) and rate tables.
/// Distinguishes audited shipments from indeterminate shipments with missing contractual inputs.
/// DB-SPEC-3PL-2026 Phase 6.4.4.2 Variance &amp; Accessorial Discrepancy Analytics.
/// </summary>
public record RateCardComplianceAnalysisDto
{
    public string CarrierCode { get; init; } = string.Empty;
    public Guid? FacilityId { get; init; }
    public DateTime StartDateUtc { get; init; }
    public DateTime EndDateUtc { get; init; }
    public DateTime ValuationTimeUtc { get; init; }

    public int TotalAuditedShipmentsCount { get; init; }
    public int CompliantShipmentsCount { get; init; }
    public int IndeterminateShipmentsCount { get; init; }
    public decimal? ComplianceRatePercent { get; init; }

    public int OverchargeDiscrepanciesCount { get; init; }
    public decimal TotalOverchargeAmountUsd { get; init; }

    public int UnderchargeDiscrepanciesCount { get; init; }
    public decimal TotalUnderchargeAmountUsd { get; init; }

    public int WeightAuditDiscrepanciesCount { get; init; }
    public decimal TotalWeightDriftKg { get; init; }
}
