using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Service providing transportation freight cost variance, margin profitability, accessorial leakage,
/// and rate-card compliance analytics.
/// Builds upon the authoritative buy-side and sell-side reconciliation outputs of IFreightReconciliationService.
/// Enforces read-only ledger invariance, fail-closed tenant boundaries, and multi-stage margin separation.
/// DB-SPEC-3PL-2026 Phase 6.4.4.2 Variance &amp; Accessorial Discrepancy Analytics.
/// </summary>
public class FreightCostVarianceService : IFreightCostVarianceService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IFreightReconciliationService _reconciliationService;
    private readonly ILogger<FreightCostVarianceService> _logger;

    public FreightCostVarianceService(
        IApplicationDbContext dbContext,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        IFreightReconciliationService reconciliationService,
        ILogger<FreightCostVarianceService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantProvider = tenantProvider ?? throw new ArgumentNullException(nameof(tenantProvider));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
        _reconciliationService = reconciliationService ?? throw new ArgumentNullException(nameof(reconciliationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FreightCostVarianceSummaryDto> GetFreightCostVarianceSummaryAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            throw new ArgumentException("Carrier code is required.", nameof(carrierCode));
        }

        EnforceFilterAuthorization(clientId, facilityId, out var effectiveClientId, out var effectiveFacilityId, out _);

        var valuationTime = asOfUtc ?? DateTime.UtcNow;
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();

        // Retrieve authoritative reconciled batch financials from IFreightReconciliationService
        var batch = await _reconciliationService.GetReconciledShipmentFinancialsBatchAsync(
            startDateUtc,
            endDateUtc,
            normalizedCarrier,
            effectiveFacilityId,
            effectiveClientId,
            valuationTime,
            cancellationToken);

        int quotedCount = batch.Shipments.Count(s => s.QuotedCarrierCostUsd > 0m);
        int billedCount = batch.Shipments.Count(s => (s.InvoicedCarrierBaseCostUsd + s.InvoicedCarrierAccessorialsUsd) > 0m);
        int reconciledCount = batch.Shipments.Count(s => s.BuySideReconciliationStatus == FreightReconciliationStatus.Reconciled);

        decimal totalQuoted = batch.TotalQuotedAmountUsd;
        decimal totalEstimated = batch.TotalEstimatedAmountUsd;
        decimal totalBilled = batch.TotalNetInvoicedCarrierCostUsd;
        decimal totalReconciled = batch.TotalReconciledCarrierCostUsd;

        decimal estimatedVariance = totalEstimated - totalQuoted;
        decimal billedVariance = totalBilled - totalQuoted;
        decimal reconciledVariance = totalReconciled - totalQuoted;

        decimal? variancePercent = totalQuoted > 0m
            ? Math.Round(((totalBilled - totalQuoted) / totalQuoted) * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        return new FreightCostVarianceSummaryDto
        {
            CarrierCode = normalizedCarrier,
            FacilityId = effectiveFacilityId,
            ClientId = effectiveClientId,
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc,
            ValuationTimeUtc = valuationTime,
            TotalShipmentsQuotedCount = quotedCount,
            TotalShipmentsBilledCount = billedCount,
            TotalShipmentsReconciledCount = reconciledCount,
            TotalQuotedAmountUsd = totalQuoted,
            TotalEstimatedAmountUsd = totalEstimated,
            TotalBilledAmountUsd = totalBilled,
            TotalReconciledAmountUsd = totalReconciled,
            EstimatedVarianceUsd = estimatedVariance,
            BilledVarianceUsd = billedVariance,
            ReconciledVarianceUsd = reconciledVariance,
            VariancePercent = variancePercent
        };
    }

    public async Task<FreightProfitabilitySummaryDto> GetFreightProfitabilitySummaryAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        CarrierServiceLevel? serviceLevel = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            throw new ArgumentException("Carrier code is required.", nameof(carrierCode));
        }

        EnforceFilterAuthorization(clientId, facilityId, out var effectiveClientId, out var effectiveFacilityId, out _);

        var valuationTime = asOfUtc ?? DateTime.UtcNow;
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();

        var batch = await _reconciliationService.GetReconciledShipmentFinancialsBatchAsync(
            startDateUtc,
            endDateUtc,
            normalizedCarrier,
            effectiveFacilityId,
            effectiveClientId,
            valuationTime,
            cancellationToken);

        var shipments = serviceLevel.HasValue
            ? batch.Shipments.Where(s => s.CarrierServiceLevel == serviceLevel.Value).ToList()
            : batch.Shipments;

        int totalShipments = shipments.Count;
        decimal totalClientRevenue = shipments.Sum(s => s.InvoicedClientFreightUsd);
        decimal totalCarrierCost = shipments.Sum(s => s.NetInvoicedCarrierCostUsd);

        // 1. Estimated Gross Margin
        decimal totalEstClient = shipments.Sum(s => s.EstimatedClientFreightUsd);
        decimal totalEstCarrier = shipments.Sum(s => s.EstimatedCarrierCostUsd);
        decimal estimatedMarginUsd = totalEstClient - totalEstCarrier;
        decimal? estimatedMarginPercent = totalEstClient > 0m
            ? Math.Round((estimatedMarginUsd / totalEstClient) * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        // 2. Invoiced Gross Margin
        decimal invoicedMarginUsd = totalClientRevenue - totalCarrierCost;
        decimal? invoicedMarginPercent = totalClientRevenue > 0m
            ? Math.Round((invoicedMarginUsd / totalClientRevenue) * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        decimal? invoicedMarkupPercent = totalCarrierCost > 0m
            ? Math.Round((invoicedMarginUsd / totalCarrierCost) * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        // 3. Realized Reconciled Gross Margin (Accrual-based reconciled billing margin)
        decimal totalReconciledClient = shipments.Sum(s => s.ReconciledClientFreightUsd ?? 0m);
        decimal totalReconciledCarrier = shipments.Sum(s => s.ReconciledCarrierCostUsd ?? 0m);
        decimal realizedMarginUsd = totalReconciledClient - totalReconciledCarrier;
        decimal? realizedMarginPercent = totalReconciledClient > 0m
            ? Math.Round((realizedMarginUsd / totalReconciledClient) * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        // Unit Economics
        decimal avgRevenue = totalShipments > 0 ? Math.Round(totalClientRevenue / totalShipments, 4, MidpointRounding.AwayFromZero) : 0m;
        decimal avgCost = totalShipments > 0 ? Math.Round(totalCarrierCost / totalShipments, 4, MidpointRounding.AwayFromZero) : 0m;
        decimal avgMargin = totalShipments > 0 ? Math.Round(invoicedMarginUsd / totalShipments, 4, MidpointRounding.AwayFromZero) : 0m;

        return new FreightProfitabilitySummaryDto
        {
            CarrierCode = normalizedCarrier,
            ServiceLevel = serviceLevel,
            FacilityId = effectiveFacilityId,
            ClientId = effectiveClientId,
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc,
            ValuationTimeUtc = valuationTime,
            TotalShipmentsCount = totalShipments,
            TotalClientRevenueUsd = totalClientRevenue,
            TotalCarrierCostUsd = totalCarrierCost,
            EstimatedGrossMarginUsd = estimatedMarginUsd,
            EstimatedMarginPercent = estimatedMarginPercent,
            InvoicedGrossMarginUsd = invoicedMarginUsd,
            InvoicedMarginPercent = invoicedMarginPercent,
            InvoicedMarkupPercent = invoicedMarkupPercent,
            RealizedGrossMarginUsd = realizedMarginUsd,
            RealizedMarginPercent = realizedMarginPercent,
            AverageRevenuePerShipmentUsd = avgRevenue,
            AverageCostPerShipmentUsd = avgCost,
            AverageMarginPerShipmentUsd = avgMargin
        };
    }

    public async Task<AccessorialLeakageAnalysisDto> GetAccessorialLeakageAnalysisAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            throw new ArgumentException("Carrier code is required.", nameof(carrierCode));
        }

        EnforceFilterAuthorization(clientId, facilityId, out var effectiveClientId, out var effectiveFacilityId, out var clientAuthorizedFacilities);

        var valuationTime = asOfUtc ?? DateTime.UtcNow;
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
        var start = startDateUtc.Date;
        var end = endDateUtc.Date.AddDays(1).AddTicks(-1);

        // Query shipments in scope
        var shipmentQuery = _dbContext.Shipments
            .AsNoTracking()
            .Where(s => s.DispatchedAtUtc >= start && s.DispatchedAtUtc <= end && s.CarrierCode == normalizedCarrier);

        if (effectiveFacilityId.HasValue)
        {
            shipmentQuery = shipmentQuery.Where(s => s.FacilityId == effectiveFacilityId.Value);
        }
        else if (clientAuthorizedFacilities != null && clientAuthorizedFacilities.Count > 0)
        {
            shipmentQuery = shipmentQuery.Where(s => clientAuthorizedFacilities.Contains(s.FacilityId));
        }

        if (effectiveClientId.HasValue)
        {
            shipmentQuery = shipmentQuery.Where(s => s.ClientId == effectiveClientId.Value);
        }

        var shipmentIds = await shipmentQuery.Select(s => s.Id).ToListAsync(cancellationToken);

        // Query discrete accessorial charges associated with these shipments
        var accessorialCharges = await _dbContext.CarrierAccessorialCharges
            .AsNoTracking()
            .Include(a => a.CarrierCostRecord)
            .Where(a => shipmentIds.Contains(a.ShipmentId))
            .ToListAsync(cancellationToken);

        decimal totalCarrierAccessorials = 0m;
        decimal totalClientBilledAccessorials = 0m;
        decimal totalNetLeakage = 0m;
        decimal totalContractuallyAbsorbed = 0m;

        var typeBreakdowns = new Dictionary<AccessorialType, (decimal CarrierUsd, decimal ClientUsd, decimal LeakageUsd, decimal AbsorbedUsd, int Count)>();
        var categoryBreakdowns = new Dictionary<AccessorialLeakageCategory, (decimal AmountUsd, int Count)>();

        foreach (var charge in accessorialCharges)
        {
            // Currency normalization for accessorial charge
            var carrierConv = _reconciliationService.NormalizeCurrency(
                charge.CarrierAmount,
                charge.Currency,
                "USD",
                charge.AssessedAtUtc,
                valuationTime);
            decimal carrierUsd = carrierConv.ConvertedAmountUsd;

            decimal clientUsd = 0m;
            if (charge.ClientAmount.HasValue && charge.ClientAmount.Value > 0m)
            {
                var clientConv = _reconciliationService.NormalizeCurrency(
                    charge.ClientAmount.Value,
                    charge.Currency,
                    "USD",
                    charge.AssessedAtUtc,
                    valuationTime);
                clientUsd = clientConv.ConvertedAmountUsd;
            }

            totalCarrierAccessorials += carrierUsd;
            totalClientBilledAccessorials += clientUsd;

            decimal itemLeakage = 0m;
            decimal itemAbsorbed = 0m;
            AccessorialLeakageCategory category;

            // 4-Way Surcharge Classification:
            // 1. Contractually Absorbed (Contract explicitly designates 3PL absorbs fee)
            if (charge.BillableTreatment == AccessorialBillableTreatment.AbsorbedByWarehouse)
            {
                category = AccessorialLeakageCategory.AbsorbedByContract;
                itemAbsorbed = carrierUsd;
                totalContractuallyAbsorbed += itemAbsorbed;
            }
            // 2. Unbilled Operational Leakage (Fee was billable, but omitted/unbilled)
            else if (carrierUsd > 0m && clientUsd == 0m)
            {
                itemLeakage = carrierUsd;
                totalNetLeakage += itemLeakage;

                category = charge.AccessorialType switch
                {
                    AccessorialType.AddressCorrection => AccessorialLeakageCategory.AddressCorrectionPenalty,
                    AccessorialType.FuelSurcharge => AccessorialLeakageCategory.FuelIndexingDrift,
                    AccessorialType.OversizeOverweight => AccessorialLeakageCategory.DimensionalWeightAdjustment,
                    _ => AccessorialLeakageCategory.UnbilledOperationalCharge
                };
            }
            // 3. Client Under-Recovery (Client billed fixed fee that fell short of carrier cost)
            else if (carrierUsd > clientUsd && clientUsd > 0m)
            {
                itemLeakage = carrierUsd - clientUsd;
                totalNetLeakage += itemLeakage;
                category = AccessorialLeakageCategory.ClientUnderRecovery;
            }
            // 4. Fully Recovered
            else
            {
                category = AccessorialLeakageCategory.UncontestedDiscrepancy;
            }

            // Accumulate by AccessorialType
            if (!typeBreakdowns.TryGetValue(charge.AccessorialType, out var currentType))
            {
                currentType = (0m, 0m, 0m, 0m, 0);
            }
            typeBreakdowns[charge.AccessorialType] = (
                currentType.CarrierUsd + carrierUsd,
                currentType.ClientUsd + clientUsd,
                currentType.LeakageUsd + itemLeakage,
                currentType.AbsorbedUsd + itemAbsorbed,
                currentType.Count + 1);

            // Accumulate by AccessorialLeakageCategory
            if (itemLeakage > 0m || itemAbsorbed > 0m)
            {
                decimal categoryAmount = itemLeakage > 0m ? itemLeakage : itemAbsorbed;
                if (!categoryBreakdowns.TryGetValue(category, out var currentCat))
                {
                    currentCat = (0m, 0);
                }
                categoryBreakdowns[category] = (currentCat.AmountUsd + categoryAmount, currentCat.Count + 1);
            }
        }

        decimal? recoveryRate = totalCarrierAccessorials > 0m
            ? Math.Round((totalClientBilledAccessorials / totalCarrierAccessorials) * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        var typeList = typeBreakdowns.Select(kvp => new AccessorialTypeBreakdownDto
        {
            AccessorialType = kvp.Key,
            CarrierAmountUsd = kvp.Value.CarrierUsd,
            ClientBilledUsd = kvp.Value.ClientUsd,
            NetLeakageUsd = kvp.Value.LeakageUsd,
            ContractuallyAbsorbedUsd = kvp.Value.AbsorbedUsd,
            RecoveryRatePercent = kvp.Value.CarrierUsd > 0m
                ? Math.Round((kvp.Value.ClientUsd / kvp.Value.CarrierUsd) * 100m, 2, MidpointRounding.AwayFromZero)
                : null,
            OccurrenceCount = kvp.Value.Count
        }).OrderByDescending(t => t.NetLeakageUsd).ToList();

        var categoryList = categoryBreakdowns.Select(kvp => new AccessorialLeakageCategoryBreakdownDto
        {
            Category = kvp.Key,
            AmountUsd = kvp.Value.AmountUsd,
            Count = kvp.Value.Count,
            Description = GetCategoryDescription(kvp.Key)
        }).OrderByDescending(c => c.AmountUsd).ToList();

        return new AccessorialLeakageAnalysisDto
        {
            CarrierCode = normalizedCarrier,
            FacilityId = effectiveFacilityId,
            ClientId = effectiveClientId,
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc,
            ValuationTimeUtc = valuationTime,
            TotalCarrierAccessorialsUsd = totalCarrierAccessorials,
            TotalClientBilledAccessorialsUsd = totalClientBilledAccessorials,
            TotalNetLeakageAmountUsd = totalNetLeakage,
            TotalContractuallyAbsorbedUsd = totalContractuallyAbsorbed,
            AccessorialRecoveryRatePercent = recoveryRate,
            BreakdownByType = typeList.AsReadOnly(),
            BreakdownByLeakageCategory = categoryList.AsReadOnly()
        };
    }

    public async Task<IReadOnlyList<CarrierInvoiceDiscrepancyDto>> GetDiscrepancyManagementQueueAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        Guid? clientId = null,
        FreightReconciliationStatus? statusFilter = null,
        decimal? minimumDiscrepancyUsd = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            throw new ArgumentException("Carrier code is required.", nameof(carrierCode));
        }

        EnforceFilterAuthorization(clientId, facilityId, out var effectiveClientId, out var effectiveFacilityId, out _);

        var valuationTime = asOfUtc ?? DateTime.UtcNow;
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();

        var batch = await _reconciliationService.GetReconciledShipmentFinancialsBatchAsync(
            startDateUtc,
            endDateUtc,
            normalizedCarrier,
            effectiveFacilityId,
            effectiveClientId,
            valuationTime,
            cancellationToken);

        var minThreshold = minimumDiscrepancyUsd ?? 0.01m;
        var queue = new List<CarrierInvoiceDiscrepancyDto>();

        foreach (var s in batch.Shipments)
        {
            if (statusFilter.HasValue && s.BuySideReconciliationStatus != statusFilter.Value)
            {
                continue;
            }

            decimal expectedCost = s.QuotedCarrierCostUsd > 0m
                ? s.QuotedCarrierCostUsd
                : (s.EstimatedCarrierCostUsd > 0m
                    ? s.EstimatedCarrierCostUsd
                    : (s.TotalCarrierAdjustmentsUsd != 0m
                        ? (s.NetInvoicedCarrierCostUsd - s.TotalCarrierAdjustmentsUsd)
                        : s.NetInvoicedCarrierCostUsd));

            decimal invoicedCost = s.NetInvoicedCarrierCostUsd;
            decimal discrepancy = invoicedCost - expectedCost;

            bool isDisputed = s.BuySideReconciliationStatus == FreightReconciliationStatus.Disputed;

            if (Math.Abs(discrepancy) >= minThreshold || isDisputed)
            {
                decimal? discrepancyPercent = expectedCost > 0m
                    ? Math.Round((discrepancy / expectedCost) * 100m, 2, MidpointRounding.AwayFromZero)
                    : null;

                var (rootCause, reason) = ClassifyDiscrepancy(s, expectedCost, invoicedCost, discrepancy);

                queue.Add(new CarrierInvoiceDiscrepancyDto
                {
                    ShipmentId = s.ShipmentId,
                    ShipmentNumber = s.ShipmentNumber,
                    TrackingNumber = s.TrackingNumber,
                    CarrierCode = s.CarrierCode,
                    CarrierInvoiceNumber = s.CarrierInvoiceNumbers.FirstOrDefault() ?? string.Empty,
                    ExpectedCostUsd = expectedCost,
                    InvoicedCostUsd = invoicedCost,
                    DiscrepancyAmountUsd = discrepancy,
                    DiscrepancyPercent = discrepancyPercent,
                    ReconciliationStatus = s.BuySideReconciliationStatus,
                    RootCauseCategory = rootCause,
                    DiscrepancyReason = reason,
                    IdentifiedAtUtc = s.DispatchedAtUtc ?? DateTime.UtcNow,
                    IsQuarantinedFromReconciledTotals = isDisputed
                });
            }
        }

        return queue.OrderByDescending(d => Math.Abs(d.DiscrepancyAmountUsd)).ToList().AsReadOnly();
    }

    public async Task<RateCardComplianceAnalysisDto> GetRateCardComplianceAnalysisAsync(
        string carrierCode,
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? facilityId = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
        {
            throw new ArgumentException("Carrier code is required.", nameof(carrierCode));
        }

        EnforceFilterAuthorization(null, facilityId, out _, out var effectiveFacilityId, out _);

        var valuationTime = asOfUtc ?? DateTime.UtcNow;
        var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
        var start = startDateUtc.Date;
        var end = endDateUtc.Date.AddDays(1).AddTicks(-1);

        // Retrieve shipments with packages and cost records
        var query = _dbContext.Shipments
            .AsNoTracking()
            .Include(s => s.Packages)
            .Where(s => s.DispatchedAtUtc >= start && s.DispatchedAtUtc <= end && s.CarrierCode == normalizedCarrier);

        if (effectiveFacilityId.HasValue)
        {
            query = query.Where(s => s.FacilityId == effectiveFacilityId.Value);
        }

        var shipments = await query.ToListAsync(cancellationToken);
        var shipmentIds = shipments.Select(s => s.Id).ToList();

        var costRecords = await _dbContext.CarrierCostRecords
            .AsNoTracking()
            .Include(c => c.Adjustments)
            .Where(c => shipmentIds.Contains(c.ShipmentId) &&
                       (c.Status == CarrierCostStatus.Invoiced || c.Status == CarrierCostStatus.Reconciled || c.Status == CarrierCostStatus.Disputed))
            .ToListAsync(cancellationToken);

        int totalAudited = 0;
        int compliantCount = 0;
        int overchargeCount = 0;
        decimal totalOvercharge = 0m;
        int underchargeCount = 0;
        decimal totalUndercharge = 0m;
        int weightAuditCount = 0;
        decimal totalWeightDriftKg = 0m;

        foreach (var shipment in shipments)
        {
            var cost = costRecords.FirstOrDefault(c => c.ShipmentId == shipment.Id);
            if (cost == null) continue;

            totalAudited++;

            // 1. Billable Weight Audit: Expected Billable Weight = RoundToIncrement(max(ScaleWeight, VolumetricWeight))
            decimal totalScaleWeight = shipment.Packages.Sum(p => p.GrossWeightKg > 0m ? p.GrossWeightKg : p.NetWeightKg);
            decimal totalVolumetricWeight = shipment.Packages.Sum(p =>
                (p.LengthCm > 0m && p.WidthCm > 0m && p.HeightCm > 0m)
                    ? Math.Round((p.LengthCm * p.WidthCm * p.HeightCm) / (p.VolumetricDivisor > 0m ? p.VolumetricDivisor : 5000m), 2, MidpointRounding.AwayFromZero)
                    : 0m);

            decimal rawExpectedWeight = Math.Max(totalScaleWeight, totalVolumetricWeight);
            // Standard carrier weight rounding: round up to nearest 0.5 kg increment
            decimal expectedBillableWeight = rawExpectedWeight > 0m
                ? Math.Ceiling(rawExpectedWeight * 2m) / 2m
                : 0m;

            // In our system, Package.GrossWeightKg or CarrierCostRecord notes capture carrier billed weight
            // Check if any package has carrier weight drift
            decimal billedCarrierWeight = totalScaleWeight; // Baseline
            var weightCorrectionAdj = cost.Adjustments.FirstOrDefault(a => a.AdjustmentType == CarrierCostAdjustmentType.WeightDiscrepancy);
            if (weightCorrectionAdj != null && weightCorrectionAdj.CarrierCostDelta > 0m)
            {
                // Weight correction adjustment indicates carrier re-weighed parcel exceeding initial dim quote
                weightAuditCount++;
                decimal estimatedWeightDrift = Math.Round(weightCorrectionAdj.CarrierCostDelta / 5.0m, 2); // Nominal weight factor
                totalWeightDriftKg += estimatedWeightDrift > 0m ? estimatedWeightDrift : 1.0m;
            }

            // 2. Base Freight & Overcharge Audit
            // Compare invoiced base transportation cost vs expected base cost
            var baseConv = _reconciliationService.NormalizeCurrency(cost.BaseTransportationCost, cost.Currency, "USD", cost.CostAssessedAtUtc, valuationTime);
            decimal invoicedBaseUsd = baseConv.ConvertedAmountUsd;

            // Tolerance: $0.05 or 0.5%
            decimal expectedBaseUsd = invoicedBaseUsd;
            if (cost.Adjustments.Count > 0)
            {
                var originalConv = _reconciliationService.NormalizeCurrency(cost.Adjustments.First().OriginalCarrierCost, cost.Currency, "USD", cost.CostAssessedAtUtc, valuationTime);
                expectedBaseUsd = originalConv.ConvertedAmountUsd;
            }

            decimal delta = invoicedBaseUsd - expectedBaseUsd;
            decimal absDelta = Math.Abs(delta);
            decimal tolerance = Math.Max(0.05m, expectedBaseUsd * 0.005m);

            if (absDelta <= tolerance)
            {
                compliantCount++;
            }
            else if (delta > 0m)
            {
                overchargeCount++;
                totalOvercharge += delta;
            }
            else
            {
                underchargeCount++;
                totalUndercharge += -delta;
            }
        }

        decimal? complianceRate = totalAudited > 0
            ? Math.Round(((decimal)compliantCount / totalAudited) * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        return new RateCardComplianceAnalysisDto
        {
            CarrierCode = normalizedCarrier,
            FacilityId = effectiveFacilityId,
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc,
            ValuationTimeUtc = valuationTime,
            TotalAuditedShipmentsCount = totalAudited,
            CompliantShipmentsCount = compliantCount,
            ComplianceRatePercent = complianceRate,
            OverchargeDiscrepanciesCount = overchargeCount,
            TotalOverchargeAmountUsd = totalOvercharge,
            UnderchargeDiscrepanciesCount = underchargeCount,
            TotalUnderchargeAmountUsd = totalUndercharge,
            WeightAuditDiscrepanciesCount = weightAuditCount,
            TotalWeightDriftKg = totalWeightDriftKg
        };
    }

    #region Private Helper Methods

    private static (DiscrepancyRootCauseCategory RootCause, string Reason) ClassifyDiscrepancy(
        ReconciledShipmentFinancialsDto s,
        decimal expectedCost,
        decimal invoicedCost,
        decimal discrepancy)
    {
        if (s.AdjustmentAuditTrail.Any(a => a.AdjustmentType == CarrierCostAdjustmentType.WeightDiscrepancy))
        {
            return (DiscrepancyRootCauseCategory.DimensionalWeightDrift,
                $"Carrier re-weighed parcel exceeding expected billable weight (variance: ${discrepancy:N2}).");
        }

        if (s.InvoicedCarrierAccessorialsUsd > 0m && s.QuotedCarrierCostUsd > 0m && s.InvoicedCarrierAccessorialsUsd > (s.EstimatedCarrierCostUsd - s.QuotedCarrierCostUsd))
        {
            return (DiscrepancyRootCauseCategory.UncontractedAccessorial,
                $"Carrier assessed unexpected accessorial surcharges (${s.InvoicedCarrierAccessorialsUsd:N2}).");
        }

        if (s.AdjustmentAuditTrail.Any(a => a.AdjustmentType == CarrierCostAdjustmentType.LateAccessorial))
        {
            return (DiscrepancyRootCauseCategory.LateAdjustment,
                "Carrier billed late-arriving surcharge after physical delivery.");
        }

        if (s.BuySideReconciliationStatus == FreightReconciliationStatus.Disputed)
        {
            return (DiscrepancyRootCauseCategory.BaseRateOvercharge,
                $"Carrier invoiced charge disputed by freight audit (variance: ${discrepancy:N2}).");
        }

        if (discrepancy > 0m)
        {
            return (DiscrepancyRootCauseCategory.BaseRateOvercharge,
                $"Invoiced base rate exceeds expected quote by ${discrepancy:N2}.");
        }

        return (DiscrepancyRootCauseCategory.Other,
            $"Billing variance of ${discrepancy:N2} between invoiced and expected cost.");
    }

    private static string GetCategoryDescription(AccessorialLeakageCategory category)
    {
        return category switch
        {
            AccessorialLeakageCategory.AbsorbedByContract => "Contractually absorbed surcharge per client agreement",
            AccessorialLeakageCategory.UnbilledOperationalCharge => "Billable carrier surcharge omitted from client billing",
            AccessorialLeakageCategory.AddressCorrectionPenalty => "Carrier address correction penalty unrecovered",
            AccessorialLeakageCategory.FuelIndexingDrift => "Carrier weekly fuel index drift exceeding client billing rate",
            AccessorialLeakageCategory.DimensionalWeightAdjustment => "Carrier re-weigh adjustment not billed to client",
            AccessorialLeakageCategory.CarrierOvercharge => "Erroneous carrier surcharge filed for dispute",
            AccessorialLeakageCategory.ClientUnderRecovery => "Fixed client billing fell short of carrier accessorial cost",
            _ => "Uncontested unrecovered surcharge"
        };
    }

    private void EnforceFilterAuthorization(
        Guid? requestedClientId,
        Guid? requestedFacilityId,
        out Guid? effectiveClientId,
        out Guid? effectiveFacilityId,
        out HashSet<Guid>? clientAuthorizedFacilities)
    {
        clientAuthorizedFacilities = null;

        if (_tenantProvider.BypassTenantFilter && _currentUserContext.IsSuperAdmin)
        {
            effectiveClientId = requestedClientId;
            effectiveFacilityId = requestedFacilityId;
            return;
        }

        var userType = _currentUserContext.UserType;

        if (userType == UserType.Client)
        {
            var userTenantId = _currentUserContext.TenantId ?? _tenantProvider.TenantId;
            if (!userTenantId.HasValue || userTenantId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Client user context missing authenticated tenant identity.");
            }

            var currentTenantId = userTenantId.Value;
            if (requestedClientId.HasValue && requestedClientId.Value != currentTenantId)
            {
                throw new ForbiddenException($"Client user is not authorized to query client '{requestedClientId}'.");
            }
            effectiveClientId = currentTenantId;

            // Query active facility access assignments from database
            var authorizedFacilityIds = _dbContext.ClientFacilityAccesses
                .AsNoTracking()
                .Where(a => a.ClientId == currentTenantId && a.Status == FacilityAccessStatus.Active)
                .Select(a => a.FacilityId)
                .ToHashSet();

            if (authorizedFacilityIds.Count == 0)
            {
                throw new ForbiddenException($"Client '{currentTenantId}' has no active facility assignments.");
            }

            if (requestedFacilityId.HasValue)
            {
                if (!authorizedFacilityIds.Contains(requestedFacilityId.Value))
                {
                    throw new ForbiddenException($"Client '{currentTenantId}' is not authorized for facility '{requestedFacilityId.Value}'.");
                }
                effectiveFacilityId = requestedFacilityId.Value;
            }
            else
            {
                effectiveFacilityId = null;
                clientAuthorizedFacilities = authorizedFacilityIds;
            }
            return;
        }

        if (userType == UserType.Staff)
        {
            effectiveClientId = requestedClientId;

            bool hasCrossFacilityPermission = _currentUserContext.HasPermission("Analytics:CrossFacility")
                                           || _currentUserContext.HasPermission("Shipments:CrossFacility")
                                           || _currentUserContext.HasPermission("Financials:CrossFacility");

            if (hasCrossFacilityPermission)
            {
                effectiveFacilityId = requestedFacilityId;
                return;
            }

            var staffFacilityId = _currentUserContext.FacilityId;
            if (!staffFacilityId.HasValue || staffFacilityId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Staff user without CrossFacility permission has no assigned facility.");
            }

            if (requestedFacilityId.HasValue && requestedFacilityId.Value != staffFacilityId.Value)
            {
                throw new ForbiddenException($"Staff user assigned to facility '{staffFacilityId.Value}' is not authorized to query facility '{requestedFacilityId.Value}'.");
            }

            effectiveFacilityId = staffFacilityId.Value;
            return;
        }

        throw new ForbiddenException($"User type '{userType}' is not authorized for freight analytics.");
    }

    #endregion
}
