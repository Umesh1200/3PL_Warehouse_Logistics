using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Service providing authoritative financial reconciliation between buy-side carrier costs and sell-side client freight.
/// Strictly enforces anti-double counting, signed adjustment accounting, multi-package conservation, and tenant isolation.
/// DB-SPEC-3PL-2026 Phase 6.4.4.1 Buy-Side &amp; Sell-Side Financial Reconciliation.
/// </summary>
public interface IFreightReconciliationService
{
    /// <summary>
    /// Reconciles buy-side carrier expenses and sell-side client billable freight for a specific shipment.
    /// Enforces tenant and facility authorization at the service boundary.
    /// </summary>
    Task<ReconciledShipmentFinancialsDto> GetShipmentFinancialReconciliationAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries reconciled financials for a batch of shipments within a date range.
    /// Parameters facilityId and clientId act as query filters restricted to authenticated permissions.
    /// </summary>
    Task<ReconciliationBatchSummaryDto> GetReconciledShipmentFinancialsBatchAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        string? carrierCode = null,
        Guid? facilityId = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates master shipment transportation costs across individual packages by weight ratio,
    /// strictly conserving the total sum including residual cent rounding adjustments.
    /// </summary>
    IReadOnlyList<PackageCostAllocationDto> AllocateShipmentCostsToPackages(
        decimal totalCarrierCost,
        decimal totalClientFreight,
        IReadOnlyList<(Guid PackageId, string PackageNumber, decimal WeightKg)> packages);

    /// <summary>
    /// Performs reproducible currency normalization to USD, preserving exchange rate and source evidence.
    /// </summary>
    CurrencyConversionEvidence NormalizeCurrency(
        decimal amount,
        string sourceCurrency,
        string targetCurrency,
        DateTime effectiveDateUtc,
        decimal? exchangeRate = null,
        string? rateSource = null);
}
