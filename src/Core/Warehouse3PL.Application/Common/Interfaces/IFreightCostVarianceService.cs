using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Service providing transportation freight cost variance, margin profitability, accessorial leakage,
/// and rate-card compliance analytics.
/// Builds upon the authoritative buy-side and sell-side reconciliation outputs of IFreightReconciliationService.
/// DB-SPEC-3PL-2026 Phase 6.4.4.2 Variance &amp; Accessorial Discrepancy Analytics.
/// </summary>
public interface IFreightCostVarianceService
{
    /// <summary>
    /// Computes macro-level freight spend variance across quoted baseline, dispatch estimates, invoiced spend, and reconciled costs.
    /// </summary>
    Task<FreightCostVarianceSummaryDto> GetFreightCostVarianceSummaryAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes multi-stage commercial freight margins (Estimated, Invoiced, Realized) and unit economics.
    /// </summary>
    Task<FreightProfitabilitySummaryDto> GetFreightProfitabilitySummaryAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        CarrierServiceLevel? serviceLevel = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes carrier accessorial surcharges, recovery rates, contractually absorbed fees, and unbilled operational leakage.
    /// </summary>
    Task<AccessorialLeakageAnalysisDto> GetAccessorialLeakageAnalysisAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the shipment-level discrepancy review queue for freight audit controllers, quarantining disputed costs.
    /// </summary>
    Task<IReadOnlyList<CarrierInvoiceDiscrepancyDto>> GetDiscrepancyManagementQueueAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        FreightReconciliationStatus? statusFilter = null,
        decimal? minimumDiscrepancyUsd = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Audits carrier invoice compliance against contractual rate card tables and expected billable weight (max(scale, dim)).
    /// </summary>
    Task<RateCardComplianceAnalysisDto> GetRateCardComplianceAnalysisAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);
}
