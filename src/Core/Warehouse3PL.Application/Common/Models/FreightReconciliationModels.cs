using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Application.Common.Models;

/// <summary>
/// Reconciled shipment financial analytical model establishing buy-side and sell-side amounts.
/// Strict Invariant: Original charge plus signed adjustments equals net charge.
/// DB-SPEC-3PL-2026 Phase 6.4.4.1 Buy-Side & Sell-Side Financial Reconciliation.
/// </summary>
public record ReconciledShipmentFinancialsDto
{
    public Guid ShipmentId { get; init; }
    public string ShipmentNumber { get; init; } = string.Empty;
    public string TrackingNumber { get; init; } = string.Empty;
    public string CarrierCode { get; init; } = string.Empty;
    public CarrierServiceLevel CarrierServiceLevel { get; init; } = CarrierServiceLevel.StandardGround;
    public Guid FacilityId { get; init; }
    public Guid ClientId { get; init; }
    public DateTime? DispatchedAtUtc { get; init; }
    public DateTime? RatedAtUtc { get; init; }

    // Buy-Side Financial Evidence
    public decimal QuotedCarrierCostUsd { get; init; }
    public decimal EstimatedCarrierCostUsd { get; init; }
    public decimal InvoicedCarrierBaseCostUsd { get; init; }
    public decimal InvoicedCarrierAccessorialsUsd { get; init; }
    public decimal TotalCarrierAdjustmentsUsd { get; init; }
    public decimal NetInvoicedCarrierCostUsd { get; init; }
    public decimal? ReconciledCarrierCostUsd { get; init; }
    public FreightReconciliationStatus BuySideReconciliationStatus { get; init; } = FreightReconciliationStatus.Estimated;

    // Sell-Side Financial Evidence
    public decimal EstimatedClientFreightUsd { get; init; }
    public decimal InvoicedClientFreightUsd { get; init; }
    public decimal? ReconciledClientFreightUsd { get; init; }
    public FreightReconciliationStatus SellSideReconciliationStatus { get; init; } = FreightReconciliationStatus.Estimated;

    // Multi-Package Allocation
    public IReadOnlyList<PackageCostAllocationDto> PackageAllocations { get; init; } = [];

    // Lineage & Traceability
    public IReadOnlyList<string> CarrierInvoiceNumbers { get; init; } = [];
    public IReadOnlyList<string> ClientInvoiceNumbers { get; init; } = [];
    public IReadOnlyList<CarrierAdjustmentAuditDto> AdjustmentAuditTrail { get; init; } = [];
    public CurrencyConversionEvidence? ConversionEvidence { get; init; }
    public IReadOnlyList<CurrencyConversionEvidence> ConversionEvidenceAuditTrail { get; init; } = [];
}

/// <summary>
/// Package-level cost allocation conserving total shipment cost down to exact cent.
/// </summary>
public record PackageCostAllocationDto
{
    public Guid PackageId { get; init; }
    public string PackageNumber { get; init; } = string.Empty;
    public decimal WeightKg { get; init; }
    public decimal WeightRatio { get; init; }
    public decimal AllocatedCarrierCostUsd { get; init; }
    public decimal AllocatedClientFreightUsd { get; init; }
    public bool IsFallbackEqualAllocationApplied { get; init; }
}

/// <summary>
/// Audit line item representing an immutable carrier invoice adjustment or credit note.
/// </summary>
public record CarrierAdjustmentAuditDto
{
    public Guid AdjustmentId { get; init; }
    public string CarrierInvoiceNumber { get; init; } = string.Empty;
    public CarrierCostAdjustmentType AdjustmentType { get; init; }
    public decimal OriginalCost { get; init; }
    public decimal AdjustedCost { get; init; }
    public decimal CostDelta { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime AdjustedAtUtc { get; init; }
}

/// <summary>
/// Aggregated financial reconciliation summary across an audited shipment batch.
/// </summary>
public record ReconciliationBatchSummaryDto
{
    public int TotalShipmentsCount { get; init; }
    public decimal TotalQuotedAmountUsd { get; init; }
    public decimal TotalEstimatedAmountUsd { get; init; }
    public decimal TotalInvoicedCarrierCostUsd { get; init; }
    public decimal TotalNetInvoicedCarrierCostUsd { get; init; }
    public decimal TotalReconciledCarrierCostUsd { get; init; }
    public decimal TotalInvoicedClientFreightUsd { get; init; }
    public decimal TotalReconciledClientFreightUsd { get; init; }
    public int TotalAdjustmentsCount { get; init; }
    public decimal TotalAdjustmentsAmountUsd { get; init; }
    public int DisputedShipmentsCount { get; init; }
    public decimal DisputedAmountUsd { get; init; }
    public IReadOnlyList<ReconciledShipmentFinancialsDto> Shipments { get; init; } = [];
}
