using System.Globalization;
using System.Text.RegularExpressions;
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

        // 1. Authoritative Batch Financial Reconciliation Baseline
        var batch = await _reconciliationService.GetReconciledShipmentFinancialsBatchAsync(
            startDateUtc,
            endDateUtc,
            normalizedCarrier,
            effectiveFacilityId,
            effectiveClientId,
            valuationTime,
            cancellationToken);

        int totalQuotedCount = batch.Shipments.Count(s => s.QuotedCarrierCostUsd > 0m);
        int totalBilledCount = batch.Shipments.Count(s => s.NetInvoicedCarrierCostUsd > 0m);
        int totalReconciledCount = batch.Shipments.Count(s => s.ReconciledCarrierCostUsd.HasValue);

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
            TotalShipmentsQuotedCount = totalQuotedCount,
            TotalShipmentsBilledCount = totalBilledCount,
            TotalShipmentsReconciledCount = totalReconciledCount,
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

        var shipments = batch.Shipments;
        if (serviceLevel.HasValue)
        {
            shipments = shipments.Where(s => s.CarrierServiceLevel == serviceLevel.Value).ToList();
        }

        int totalShipments = shipments.Count;

        // 1. Estimated Gross Margin
        decimal totalEstClient = shipments.Sum(s => s.EstimatedClientFreightUsd);
        decimal totalEstCarrier = shipments.Sum(s => s.EstimatedCarrierCostUsd);
        decimal estimatedMarginUsd = totalEstClient - totalEstCarrier;
        decimal? estimatedMarginPercent = totalEstClient > 0m
            ? Math.Round((estimatedMarginUsd / totalEstClient) * 100m, 2, MidpointRounding.AwayFromZero)
            : null;

        // 2. Invoiced Gross Margin
        decimal totalClientRevenue = shipments.Sum(s => s.InvoicedClientFreightUsd);
        decimal totalCarrierCost = shipments.Sum(s => s.NetInvoicedCarrierCostUsd);
        decimal invoicedMarginUsd = totalClientRevenue - totalCarrierCost;

        decimal? invoicedMarginPercent = totalClientRevenue > 0m
            ? Math.Round((invoicedMarginUsd / totalClientRevenue) * 100m, 2, MidpointRounding.AwayFromZero)
            : (totalClientRevenue < 0m
                ? Math.Round((invoicedMarginUsd / Math.Abs(totalClientRevenue)) * 100m, 2, MidpointRounding.AwayFromZero)
                : (decimal?)(totalCarrierCost > 0m ? -100.00m : null));

        if (totalClientRevenue > 0m && invoicedMarginUsd < 0m)
        {
            invoicedMarginPercent = Math.Round((invoicedMarginUsd / totalClientRevenue) * 100m, 2, MidpointRounding.AwayFromZero);
        }

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

        // Unit Economics (Conforms strictly to established null-for-zero-denominator analytics rule)
        decimal? avgRevenue = totalShipments > 0
            ? Math.Round(totalClientRevenue / totalShipments, 2, MidpointRounding.AwayFromZero)
            : null;

        decimal? avgCost = totalShipments > 0
            ? Math.Round(totalCarrierCost / totalShipments, 2, MidpointRounding.AwayFromZero)
            : null;

        decimal? avgMargin = totalShipments > 0
            ? Math.Round(invoicedMarginUsd / totalShipments, 2, MidpointRounding.AwayFromZero)
            : null;

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

        // 1. Query shipments in reporting period
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

        // 2. Query discrete carrier accessorial charges constrained by valuation cutoff and valid lifecycle states (Gate B)
        var accessorialCharges = await _dbContext.CarrierAccessorialCharges
            .AsNoTracking()
            .Include(a => a.CarrierCostRecord)
            .Where(a => shipmentIds.Contains(a.ShipmentId)
                     && a.AssessedAtUtc <= valuationTime
                     && a.CarrierCostRecord != null
                     && (a.CarrierCostRecord.Status == CarrierCostStatus.Invoiced
                         || a.CarrierCostRecord.Status == CarrierCostStatus.Reconciled
                         || a.CarrierCostRecord.Status == CarrierCostStatus.Disputed
                         || a.CarrierCostRecord.Status == CarrierCostStatus.ActualConfirmed)
                     && a.CarrierCostRecord.CostAssessedAtUtc <= valuationTime)
            .ToListAsync(cancellationToken);

        // 3. Query authoritative qualifying Phase 5 client invoice lines (Gate A)
        var chargeIds = accessorialCharges.Select(a => a.Id).ToHashSet();
        var billableActivities = await _dbContext.BillableActivities
            .AsNoTracking()
            .Where(b => b.SourceId != Guid.Empty && chargeIds.Contains(b.SourceId)
                     && b.OccurredAtUtc <= valuationTime
                     && b.CreatedAt <= valuationTime)
            .ToListAsync(cancellationToken);

        var activityById = billableActivities.ToDictionary(b => b.Id);
        var activityIds = activityById.Keys.ToHashSet();

        var qualifyingInvoiceLines = await _dbContext.InvoiceLines
            .AsNoTracking()
            .Include(l => l.Invoice)
            .Include(l => l.BillableActivity)
            .Where(l => l.ChargeCategory == InvoiceChargeCategory.Freight
                     && l.Invoice != null
                     && l.Invoice.InvoiceDate <= valuationTime
                     && l.CreatedAt <= valuationTime
                     && (l.Invoice.Status == InvoiceStatus.Issued
                         || l.Invoice.Status == InvoiceStatus.Paid
                         || l.Invoice.Status == InvoiceStatus.PartiallyPaid
                         || l.Invoice.Status == InvoiceStatus.Disputed
                         || l.Invoice.Status == InvoiceStatus.Approved)
                     && ((l.BillableActivityId.HasValue && activityIds.Contains(l.BillableActivityId.Value))
                         || (l.BillableActivity != null && chargeIds.Contains(l.BillableActivity.SourceId))))
            .ToListAsync(cancellationToken);

        // Deduplicate invoice lines by identity
        var deduplicatedLines = qualifyingInvoiceLines
            .GroupBy(l => l.Id)
            .Select(g => g.First())
            .ToList();

        // Deterministic mapping to authoritative BillableActivity, ensuring SourceId matches charge.Id (Finding 5)
        var linesWithActivity = deduplicatedLines
            .Select(l =>
            {
                var act = l.BillableActivity;
                if (act == null && l.BillableActivityId.HasValue && activityById.TryGetValue(l.BillableActivityId.Value, out var resolvedAct))
                {
                    act = resolvedAct;
                }
                return new { Line = l, Activity = act };
            })
            .Where(x => x.Activity != null && chargeIds.Contains(x.Activity.SourceId))
            .ToList();

        var linesByChargeId = linesWithActivity
            .ToLookup(x => x.Activity!.SourceId, x => x);

        var allocatedInvoiceLineIds = new HashSet<Guid>();
        var allocatedBillableActivityIds = new HashSet<Guid>();
        decimal totalCarrierAccessorials = 0m;
        decimal totalClientBilledAccessorials = 0m;
        decimal totalNetLeakage = 0m;
        decimal totalContractuallyAbsorbed = 0m;
        decimal totalDisputedQuarantined = 0m;

        var typeBreakdowns = new Dictionary<AccessorialType, (decimal CarrierUsd, decimal ClientUsd, decimal LeakageUsd, decimal AbsorbedUsd, int Count)>();
        var categoryBreakdowns = new Dictionary<AccessorialLeakageCategory, (decimal AmountUsd, int Count)>();

        foreach (var charge in accessorialCharges)
        {
            var carrierConv = _reconciliationService.NormalizeCurrency(
                charge.CarrierAmount,
                charge.Currency,
                "USD",
                charge.AssessedAtUtc,
                valuationTime);
            decimal carrierUsd = carrierConv.ConvertedAmountUsd;

            // Authoritative sell-side billed amount from qualifying Phase 5 InvoiceLine records (Gate A & Finding 5)
            // charge.ClientAmount is strictly ignored as proof of billing.
            // Surcharge client recovery is resolved strictly through explicit charge-to-line linkage,
            // explicitly verifying BillableActivity.SourceId == charge.Id and preventing duplicate allocation.
            decimal clientBilledUsd = 0m;

            var directItems = linesByChargeId[charge.Id]
                .Where(x => !allocatedInvoiceLineIds.Contains(x.Line.Id) && (x.Activity == null || !allocatedBillableActivityIds.Contains(x.Activity.Id)))
                .OrderByDescending(x => x.Line.Invoice != null ? x.Line.Invoice.InvoiceDate : x.Line.CreatedAt)
                .ThenByDescending(x => x.Line.CreatedAt)
                .ToList();

            foreach (var item in directItems)
            {
                var line = item.Line;
                var act = item.Activity;

                // Explicit predicate verification: BillableActivity.SourceId must strictly equal charge.Id (Finding 5)
                if (act == null || act.SourceId != charge.Id)
                {
                    continue;
                }

                // Strictly prevent duplicate allocation of the same invoice line or billable activity across charges or corrected invoices
                if (!allocatedInvoiceLineIds.Add(line.Id) || !allocatedBillableActivityIds.Add(act.Id))
                {
                    continue;
                }

                var lineConv = _reconciliationService.NormalizeCurrency(
                    line.TotalAmount,
                    "USD",
                    "USD",
                    line.Invoice?.InvoiceDate ?? charge.AssessedAtUtc,
                    valuationTime);
                clientBilledUsd += lineConv.ConvertedAmountUsd;
            }

            totalCarrierAccessorials += carrierUsd;
            totalClientBilledAccessorials += clientBilledUsd;

            decimal itemLeakage = 0m;
            decimal itemAbsorbed = 0m;
            AccessorialLeakageCategory category;

            // Dispute status evaluated FIRST to quarantine disputed charges from leakage & client under-recovery (Finding 5)
            bool isDisputed = charge.CarrierCostRecord?.Status == CarrierCostStatus.Disputed;

            if (isDisputed)
            {
                category = AccessorialLeakageCategory.CarrierOvercharge;
                itemLeakage = 0m;
                totalDisputedQuarantined += carrierUsd;
            }
            else if (charge.BillableTreatment == AccessorialBillableTreatment.AbsorbedByWarehouse)
            {
                category = AccessorialLeakageCategory.AbsorbedByContract;
                itemAbsorbed = carrierUsd;
                totalContractuallyAbsorbed += itemAbsorbed;
            }
            else if (carrierUsd > 0m && clientBilledUsd == 0m)
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
            else if (carrierUsd > clientBilledUsd && clientBilledUsd > 0m)
            {
                itemLeakage = carrierUsd - clientBilledUsd;
                totalNetLeakage += itemLeakage;
                category = AccessorialLeakageCategory.ClientUnderRecovery;
            }
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
                currentType.ClientUsd + clientBilledUsd,
                currentType.LeakageUsd + itemLeakage,
                currentType.AbsorbedUsd + itemAbsorbed,
                currentType.Count + 1);

            // Accumulate by AccessorialLeakageCategory
            if (itemLeakage > 0m || itemAbsorbed > 0m || isDisputed)
            {
                decimal categoryAmount = isDisputed ? carrierUsd : (itemLeakage > 0m ? itemLeakage : itemAbsorbed);
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
            TotalDisputedQuarantinedUsd = totalDisputedQuarantined,
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

        var shipmentIds = batch.Shipments.Select(s => s.ShipmentId).ToList();

        // Query all carrier cost records with adjustments for these shipments (constrained by valuation cutoff)
        var allCostRecords = await _dbContext.CarrierCostRecords
            .AsNoTracking()
            .Include(c => c.Adjustments)
            .Where(c => shipmentIds.Contains(c.ShipmentId) && c.CostAssessedAtUtc <= valuationTime)
            .ToListAsync(cancellationToken);

        var costRecordsByShipment = allCostRecords.ToLookup(c => c.ShipmentId);

        var start = startDateUtc.Date;
        var end = endDateUtc.Date.AddDays(1).AddTicks(-1);

        // Query active client freight agreements constrained by historical valuation evidence (Findings 2 & 3)
        var agreements = await _dbContext.ClientFreightAgreements
            .AsNoTracking()
            .Where(a => a.IsActive && (a.CarrierCode == null || a.CarrierCode == normalizedCarrier)
                     && a.EffectiveFromUtc <= end
                     && (!a.EffectiveToUtc.HasValue || a.EffectiveToUtc.Value >= start)
                     && a.CreatedAt <= valuationTime)
            .ToListAsync(cancellationToken);

        // Query active commercial rate card schedules constrained by active parent rate card and historical valuation evidence
        var rateCardSchedules = await _dbContext.RateCardSchedules
            .AsNoTracking()
            .Include(s => s.RateCard)
            .Include(s => s.Lines)
                .ThenInclude(l => l.Tiers)
            .Where(s => s.Status == RateCardStatus.Active
                     && s.RateCard != null && s.RateCard.Status == RateCardStatus.Active
                     && s.RateCard.ClientId == s.ClientId
                     && (!effectiveFacilityId.HasValue || s.FacilityId == effectiveFacilityId.Value || s.FacilityId == null)
                     && s.CreatedAt <= valuationTime
                     && s.RateCard.CreatedAt <= valuationTime
                     && s.EffectiveFromUtc <= end
                     && (!s.EffectiveToUtc.HasValue || s.EffectiveToUtc.Value >= start))
            .ToListAsync(cancellationToken);

        foreach (var s in batch.Shipments)
        {
            if (statusFilter.HasValue && s.BuySideReconciliationStatus != statusFilter.Value)
            {
                continue;
            }

            var shipmentCosts = costRecordsByShipment[s.ShipmentId].ToList();
            var invoicedRecords = shipmentCosts
                .Where(c => c.Status == CarrierCostStatus.Invoiced
                         || c.Status == CarrierCostStatus.Reconciled
                         || c.Status == CarrierCostStatus.Disputed)
                .ToList();

            var shipmentDate = s.DispatchedAtUtc ?? valuationTime;

            // Deterministic Agreement Resolution: Specific carrier & facility match precedes broader match; reject ambiguous conflicts (Findings 2 & 6)
            var candidateAgreements = agreements
                .Where(a =>
                    a.ClientId == s.ClientId &&
                    a.CreatedAt <= valuationTime &&
                    a.EffectiveFromUtc <= shipmentDate &&
                    (!a.EffectiveToUtc.HasValue || a.EffectiveToUtc.Value >= shipmentDate) &&
                    (a.CarrierCode == null || a.CarrierCode == normalizedCarrier) &&
                    (!a.FacilityId.HasValue || a.FacilityId.Value == s.FacilityId) &&
                    (!a.CarrierServiceLevel.HasValue || a.CarrierServiceLevel.Value == s.CarrierServiceLevel))
                .OrderByDescending(a => a.CarrierCode != null)
                .ThenByDescending(a => a.FacilityId.HasValue)
                .ThenByDescending(a => a.CarrierServiceLevel.HasValue)
                .ThenByDescending(a => a.EffectiveFromUtc)
                .ThenByDescending(a => a.CreatedAt)
                .ToList();

            ClientFreightAgreement? agreement = null;
            if (candidateAgreements.Count > 0)
            {
                var topAgreement = candidateAgreements[0];
                bool hasConflict = candidateAgreements.Skip(1).Any(other =>
                    (other.CarrierCode != null) == (topAgreement.CarrierCode != null) &&
                    other.FacilityId.HasValue == topAgreement.FacilityId.HasValue &&
                    other.CarrierServiceLevel.HasValue == topAgreement.CarrierServiceLevel.HasValue &&
                    other.FixedFreightRate != topAgreement.FixedFreightRate);

                if (!hasConflict)
                {
                    agreement = topAgreement;
                }
            }

            // Deterministic Schedule Resolution following RateCard.GetEffectiveSchedule (Finding 6)
            var candidateSchedules = rateCardSchedules
                .Where(rc =>
                    rc.ClientId == s.ClientId &&
                    rc.CreatedAt <= valuationTime &&
                    rc.RateCard != null && rc.RateCard.Status == RateCardStatus.Active &&
                    rc.RateCard.CreatedAt <= valuationTime &&
                    rc.EffectiveFromUtc <= shipmentDate &&
                    (!rc.EffectiveToUtc.HasValue || rc.EffectiveToUtc.Value >= shipmentDate) &&
                    (!rc.FacilityId.HasValue || rc.FacilityId.Value == s.FacilityId))
                .ToList();

            RateCardSchedule? schedule = candidateSchedules
                .Where(rc => rc.FacilityId == s.FacilityId)
                .OrderByDescending(rc => rc.Version)
                .ThenByDescending(rc => rc.EffectiveFromUtc)
                .FirstOrDefault()
                ?? candidateSchedules
                .Where(rc => rc.FacilityId == null)
                .OrderByDescending(rc => rc.Version)
                .ThenByDescending(rc => rc.EffectiveFromUtc)
                .FirstOrDefault();

            decimal? contractualRateUsd = null;
            if (agreement?.FixedFreightRate.HasValue == true && agreement.FixedFreightRate.Value > 0m)
            {
                var contractConv = _reconciliationService.NormalizeCurrency(
                    agreement.FixedFreightRate.Value,
                    agreement.Currency,
                    "USD",
                    s.DispatchedAtUtc ?? valuationTime,
                    valuationTime);
                contractualRateUsd = contractConv.ConvertedAmountUsd;
            }
            else if (schedule != null)
            {
                var freightLine = schedule.FindLine(BillableActivityType.CarrierFreight);
                if (freightLine != null && freightLine.BaseRate > 0m)
                {
                    var ratingResult = freightLine.CalculateRating(1.0m);
                    var schedConv = _reconciliationService.NormalizeCurrency(
                        ratingResult.TotalAmount,
                        schedule.Currency ?? "USD",
                        "USD",
                        s.DispatchedAtUtc ?? valuationTime,
                        valuationTime);
                    contractualRateUsd = schedConv.ConvertedAmountUsd;
                }
            }

            if (invoicedRecords.Count > 0)
            {
                // Process each invoice record / line individually to ensure stable identity and prevent collapsing (Gate E / F)
                foreach (var costRecord in invoicedRecords)
                {
                    decimal recordBaseUsd = _reconciliationService.NormalizeCurrency(
                        costRecord.BaseTransportationCost,
                        costRecord.Currency,
                        "USD",
                        costRecord.CostAssessedAtUtc,
                        valuationTime).ConvertedAmountUsd;

                    decimal recordSurchargesUsd = _reconciliationService.NormalizeCurrency(
                        costRecord.FuelSurcharge + costRecord.InsuranceCost + costRecord.AccessorialCost,
                        costRecord.Currency,
                        "USD",
                        costRecord.CostAssessedAtUtc,
                        valuationTime).ConvertedAmountUsd;

                    decimal recordAdjustmentsUsd = 0m;
                    foreach (var adj in costRecord.Adjustments.Where(a => a.AdjustedAtUtc <= valuationTime))
                    {
                        var adjConv = _reconciliationService.NormalizeCurrency(
                            adj.CarrierCostDelta,
                            adj.Currency ?? costRecord.Currency,
                            "USD",
                            adj.AdjustedAtUtc,
                            valuationTime);
                        recordAdjustmentsUsd += adjConv.ConvertedAmountUsd;
                    }

                    decimal recordInvoicedTotalUsd = recordBaseUsd + recordSurchargesUsd + recordAdjustmentsUsd;

                    // Operational Quote Variance: Invoiced vs Quoted / Estimated
                    decimal quoteBaseline = s.QuotedCarrierCostUsd > 0m
                        ? s.QuotedCarrierCostUsd
                        : (s.EstimatedCarrierCostUsd > 0m ? s.EstimatedCarrierCostUsd : recordInvoicedTotalUsd);

                    decimal operationalQuoteVariance = recordInvoicedTotalUsd - quoteBaseline;

                    // Contractual Rate Card Variance: Invoiced vs Contractual Rate
                    decimal? contractualRateVariance = contractualRateUsd.HasValue
                        ? (recordInvoicedTotalUsd - contractualRateUsd.Value)
                        : null;

                    // Expected baseline: Contractual rate if available, otherwise operational quote/estimate
                    decimal expectedCost = contractualRateUsd ?? quoteBaseline;
                    decimal discrepancy = recordInvoicedTotalUsd - expectedCost;

                    bool isDisputed = costRecord.Status == CarrierCostStatus.Disputed || s.BuySideReconciliationStatus == FreightReconciliationStatus.Disputed;

                    if (Math.Abs(discrepancy) >= minThreshold || isDisputed || costRecord.Adjustments.Count > 0 || Math.Abs(operationalQuoteVariance) >= minThreshold)
                    {
                        decimal? discrepancyPercent = expectedCost > 0m
                            ? Math.Round((discrepancy / expectedCost) * 100m, 2, MidpointRounding.AwayFromZero)
                            : null;

                        var (rootCause, reason) = ClassifyDiscrepancy(s, costRecord, expectedCost, recordInvoicedTotalUsd, discrepancy, contractualRateVariance);

                        var invoiceNum = !string.IsNullOrWhiteSpace(costRecord.CarrierInvoiceNumber)
                            ? costRecord.CarrierInvoiceNumber.Trim()
                            : (s.CarrierInvoiceNumbers.FirstOrDefault() ?? string.Empty);

                        var lineIdentifier = $"{invoiceNum}:{costRecord.CarrierCode}:{costRecord.Id}";

                        queue.Add(new CarrierInvoiceDiscrepancyDto
                        {
                            ShipmentId = s.ShipmentId,
                            ShipmentNumber = s.ShipmentNumber,
                            TrackingNumber = costRecord.TrackingNumber ?? s.TrackingNumber,
                            CarrierCode = s.CarrierCode,
                            CarrierInvoiceNumber = invoiceNum,
                            CarrierInvoiceNumbers = s.CarrierInvoiceNumbers,
                            CarrierCostRecordId = costRecord.Id,
                            InvoiceLineIdentifier = lineIdentifier,
                            ExpectedCostUsd = expectedCost,
                            InvoicedCostUsd = recordInvoicedTotalUsd,
                            DiscrepancyAmountUsd = discrepancy,
                            DiscrepancyPercent = discrepancyPercent,
                            OperationalQuoteVarianceUsd = operationalQuoteVariance,
                            ContractualRateCardVarianceUsd = contractualRateVariance,
                            InvoiceLineDiscrepancyUsd = discrepancy,
                            DisputedQuarantinedAmountUsd = isDisputed ? recordInvoicedTotalUsd : null,
                            ReconciliationStatus = isDisputed ? FreightReconciliationStatus.Disputed : s.BuySideReconciliationStatus,
                            RootCauseCategory = rootCause,
                            DiscrepancyReason = reason,
                            IdentifiedAtUtc = costRecord.CostAssessedAtUtc,
                            IsQuarantinedFromReconciledTotals = isDisputed
                        });
                    }
                }
            }
            else
            {
                // Un-invoiced shipment with estimated/quoted spend
                decimal expectedCost = s.QuotedCarrierCostUsd > 0m ? s.QuotedCarrierCostUsd : s.EstimatedCarrierCostUsd;
                decimal invoicedCost = s.NetInvoicedCarrierCostUsd;
                decimal discrepancy = invoicedCost - expectedCost;

                if (Math.Abs(discrepancy) >= minThreshold)
                {
                    queue.Add(new CarrierInvoiceDiscrepancyDto
                    {
                        ShipmentId = s.ShipmentId,
                        ShipmentNumber = s.ShipmentNumber,
                        TrackingNumber = s.TrackingNumber,
                        CarrierCode = s.CarrierCode,
                        CarrierInvoiceNumber = string.Empty,
                        CarrierInvoiceNumbers = s.CarrierInvoiceNumbers,
                        CarrierCostRecordId = null,
                        InvoiceLineIdentifier = $"ESTIMATED:{s.ShipmentNumber}",
                        ExpectedCostUsd = expectedCost,
                        InvoicedCostUsd = invoicedCost,
                        DiscrepancyAmountUsd = discrepancy,
                        DiscrepancyPercent = expectedCost > 0m ? Math.Round((discrepancy / expectedCost) * 100m, 2) : null,
                        OperationalQuoteVarianceUsd = discrepancy,
                        ContractualRateCardVarianceUsd = null,
                        InvoiceLineDiscrepancyUsd = discrepancy,
                        DisputedQuarantinedAmountUsd = null,
                        ReconciliationStatus = s.BuySideReconciliationStatus,
                        RootCauseCategory = DiscrepancyRootCauseCategory.Other,
                        DiscrepancyReason = "Un-invoiced shipment pending carrier invoice statement.",
                        IdentifiedAtUtc = s.DispatchedAtUtc ?? DateTime.UtcNow,
                        IsQuarantinedFromReconciledTotals = false
                    });
                }
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

        // Retrieve shipments with packages in date range
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

        // Retrieve carrier cost records constrained by valuation cutoff
        var costRecords = await _dbContext.CarrierCostRecords
            .AsNoTracking()
            .Include(c => c.Adjustments)
            .Where(c => shipmentIds.Contains(c.ShipmentId) && c.CostAssessedAtUtc <= valuationTime &&
                       (c.Status == CarrierCostStatus.Invoiced || c.Status == CarrierCostStatus.Reconciled || c.Status == CarrierCostStatus.Disputed))
            .ToListAsync(cancellationToken);

        var costRecordsByShipment = costRecords.ToLookup(c => c.ShipmentId);

        // Query active carrier agreements for this carrier constrained by historical valuation evidence (Findings 2 & 3)
        var agreements = await _dbContext.ClientFreightAgreements
            .AsNoTracking()
            .Where(a => a.IsActive && (a.CarrierCode == null || a.CarrierCode == normalizedCarrier)
                     && a.EffectiveFromUtc <= end
                     && (!a.EffectiveToUtc.HasValue || a.EffectiveToUtc.Value >= start)
                     && a.CreatedAt <= valuationTime)
            .ToListAsync(cancellationToken);

        // Query active commercial rate card schedules constrained by active parent rate card and historical valuation evidence (Findings 2, 3 & 6)
        var rateCardSchedules = await _dbContext.RateCardSchedules
            .AsNoTracking()
            .Include(s => s.RateCard)
            .Include(s => s.Lines)
                .ThenInclude(l => l.Tiers)
            .Where(s => s.Status == RateCardStatus.Active
                     && s.RateCard != null && s.RateCard.Status == RateCardStatus.Active
                     && s.RateCard.ClientId == s.ClientId
                     && (!effectiveFacilityId.HasValue || s.FacilityId == effectiveFacilityId.Value || s.FacilityId == null)
                     && s.CreatedAt <= valuationTime
                     && s.RateCard.CreatedAt <= valuationTime
                     && s.EffectiveFromUtc <= end
                     && (!s.EffectiveToUtc.HasValue || s.EffectiveToUtc.Value >= start))
            .ToListAsync(cancellationToken);

        int totalAudited = 0;
        int indeterminateCount = 0;
        int compliantCount = 0;
        int overchargeCount = 0;
        decimal totalOvercharge = 0m;
        int underchargeCount = 0;
        decimal totalUndercharge = 0m;
        int weightAuditCount = 0;
        decimal totalWeightDriftKg = 0m;

        foreach (var shipment in shipments)
        {
            var shipmentCosts = costRecordsByShipment[shipment.Id].ToList();
            if (shipmentCosts.Count == 0) continue;

            var shipmentDate = shipment.DispatchedAtUtc ?? valuationTime;

            // Deterministic Agreement Resolution: Specific carrier & facility match precedes broader match; reject ambiguous conflicts (Findings 2 & 6)
            var candidateAgreements = agreements
                .Where(a =>
                    a.ClientId == shipment.ClientId &&
                    a.CreatedAt <= valuationTime &&
                    a.EffectiveFromUtc <= shipmentDate &&
                    (!a.EffectiveToUtc.HasValue || a.EffectiveToUtc.Value >= shipmentDate) &&
                    (a.CarrierCode == null || a.CarrierCode == normalizedCarrier) &&
                    (!a.FacilityId.HasValue || a.FacilityId.Value == shipment.FacilityId) &&
                    (!a.CarrierServiceLevel.HasValue || a.CarrierServiceLevel.Value == shipment.CarrierServiceLevel || (shipmentCosts.Count > 0 && a.CarrierServiceLevel.Value == shipmentCosts[0].CarrierServiceLevel)))
                .OrderByDescending(a => a.CarrierCode != null)
                .ThenByDescending(a => a.FacilityId.HasValue)
                .ThenByDescending(a => a.CarrierServiceLevel.HasValue)
                .ThenByDescending(a => a.EffectiveFromUtc)
                .ThenByDescending(a => a.CreatedAt)
                .ToList();

            ClientFreightAgreement? agreement = null;
            if (candidateAgreements.Count > 0)
            {
                var topAgreement = candidateAgreements[0];
                bool hasConflict = candidateAgreements.Skip(1).Any(other =>
                    (other.CarrierCode != null) == (topAgreement.CarrierCode != null) &&
                    other.FacilityId.HasValue == topAgreement.FacilityId.HasValue &&
                    other.CarrierServiceLevel.HasValue == topAgreement.CarrierServiceLevel.HasValue &&
                    other.FixedFreightRate != topAgreement.FixedFreightRate);

                if (!hasConflict)
                {
                    agreement = topAgreement;
                }
            }

            // Deterministic Schedule Resolution following RateCard.GetEffectiveSchedule: facility-specific schedule takes precedence (Finding 6)
            var candidateSchedules = rateCardSchedules
                .Where(rc =>
                    rc.ClientId == shipment.ClientId &&
                    rc.CreatedAt <= valuationTime &&
                    rc.RateCard != null && rc.RateCard.Status == RateCardStatus.Active &&
                    rc.RateCard.CreatedAt <= valuationTime &&
                    rc.EffectiveFromUtc <= shipmentDate &&
                    (!rc.EffectiveToUtc.HasValue || rc.EffectiveToUtc.Value >= shipmentDate) &&
                    (!rc.FacilityId.HasValue || rc.FacilityId.Value == shipment.FacilityId))
                .ToList();

            RateCardSchedule? schedule = candidateSchedules
                .Where(rc => rc.FacilityId == shipment.FacilityId)
                .OrderByDescending(rc => rc.Version)
                .ThenByDescending(rc => rc.EffectiveFromUtc)
                .FirstOrDefault()
                ?? candidateSchedules
                .Where(rc => rc.FacilityId == null)
                .OrderByDescending(rc => rc.Version)
                .ThenByDescending(rc => rc.EffectiveFromUtc)
                .FirstOrDefault();

            // 2. Resolve Contractual Weight Increment (Gate D & Finding 4)
            decimal weightIncrement = ResolveContractualWeightIncrement(agreement);

            // 3. Compute Expected Billable Weight W_expected = RoundToIncrement(max(Scale, Dim))
            decimal expectedBillableWeight = CalculateExpectedBillableWeight(shipment.Packages, weightIncrement);

            // 4. Resolve Contractual Expected Base Price & Weight Dependency (Findings 1 & 2)
            bool hasContractualRate = TryResolveContractualExpectedBaseCost(
                shipment,
                agreement,
                schedule,
                expectedBillableWeight,
                out decimal contractualBaseCost,
                out string contractCurrency,
                out bool isWeightDependent);

            // Gate D requirement: If required contractual rate card inputs are missing, return indeterminate rather than declaring compliance
            if (!hasContractualRate)
            {
                indeterminateCount++;
                continue;
            }

            // 5. Weight Audit Discrepancies: Compare against verified carrier-billed weight
            bool hasWeightAdjustment = false;
            bool hasBilledWeight = false;
            decimal billedWeightKg = 0m;

            foreach (var cost in shipmentCosts)
            {
                var weightAdj = cost.Adjustments.FirstOrDefault(a => a.AdjustmentType == CarrierCostAdjustmentType.WeightDiscrepancy && a.AdjustedAtUtc <= valuationTime);
                if (weightAdj != null)
                {
                    hasWeightAdjustment = true;
                }

                if (TryResolveCarrierBilledWeight(weightAdj, cost, out decimal w))
                {
                    hasBilledWeight = true;
                    billedWeightKg = w;
                }
            }

            // Finding 1: If the applicable rate card is weight-dependent (per-kg or tiered brackets) OR if a carrier weight discrepancy was assessed,
            // verified carrier-billed weight evidence is mandatory. Missing billed weight makes the shipment indeterminate, not compliant!
            if ((isWeightDependent || hasWeightAdjustment) && !hasBilledWeight)
            {
                indeterminateCount++;
                continue;
            }

            totalAudited++;

            bool hasWeightDiscrepancy = false;
            if (hasBilledWeight)
            {
                decimal drift = billedWeightKg - expectedBillableWeight;
                if (drift > 0.05m)
                {
                    hasWeightDiscrepancy = true;
                    weightAuditCount++;
                    totalWeightDriftKg += drift;
                }
            }

            // 6. Base Rate Overcharge / Undercharge Audit
            var expectedConv = _reconciliationService.NormalizeCurrency(
                contractualBaseCost,
                contractCurrency,
                "USD",
                shipment.DispatchedAtUtc ?? valuationTime,
                valuationTime);
            decimal expectedBaseUsd = expectedConv.ConvertedAmountUsd;

            // Explicitly aggregate base transportation cost across all legitimate carrier invoice records for this shipment (Gate D)
            decimal invoicedBaseUsd = 0m;
            foreach (var cost in shipmentCosts)
            {
                var baseConv = _reconciliationService.NormalizeCurrency(
                    cost.BaseTransportationCost,
                    cost.Currency,
                    "USD",
                    cost.CostAssessedAtUtc,
                    valuationTime);
                invoicedBaseUsd += baseConv.ConvertedAmountUsd;
            }

            decimal delta = invoicedBaseUsd - expectedBaseUsd;
            decimal absDelta = Math.Abs(delta);
            // 1% contractual tolerance
            decimal tolerance = Math.Max(0.05m, expectedBaseUsd * 0.01m);

            if (absDelta <= tolerance && !hasWeightDiscrepancy)
            {
                compliantCount++;
            }
            else if (delta > 0m || hasWeightDiscrepancy)
            {
                overchargeCount++;
                totalOvercharge += Math.Max(0m, delta);
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
            IndeterminateShipmentsCount = indeterminateCount,
            ComplianceRatePercent = complianceRate,
            OverchargeDiscrepanciesCount = overchargeCount,
            TotalOverchargeAmountUsd = totalOvercharge,
            UnderchargeDiscrepanciesCount = underchargeCount,
            TotalUnderchargeAmountUsd = totalUndercharge,
            WeightAuditDiscrepanciesCount = weightAuditCount,
            TotalWeightDriftKg = totalWeightDriftKg
        };
    }

    #region Helper & Authorization Methods

    private static decimal ResolveContractualWeightIncrement(ClientFreightAgreement? agreement)
    {
        if (agreement?.WeightRoundingIncrementKg.HasValue == true && agreement.WeightRoundingIncrementKg.Value > 0m)
        {
            return agreement.WeightRoundingIncrementKg.Value;
        }

        // Standard contractual carrier weight increment defaults to 0.5 kg (Finding 4: free-text regex parsing removed)
        return 0.5m;
    }

    private static decimal CalculateExpectedBillableWeight(IEnumerable<Package> packages, decimal increment)
    {
        if (increment <= 0m) increment = 0.5m;
        decimal totalExpected = 0m;

        foreach (var p in packages)
        {
            decimal scaleWeight = p.GrossWeightKg > 0m ? p.GrossWeightKg : p.NetWeightKg;
            decimal dimWeight = p.VolumetricWeightKg > 0m
                ? p.VolumetricWeightKg
                : ((p.LengthCm > 0m && p.WidthCm > 0m && p.HeightCm > 0m)
                    ? (p.LengthCm * p.WidthCm * p.HeightCm) / (p.VolumetricDivisor > 0m ? p.VolumetricDivisor : 5000m)
                    : 0m);

            decimal rawWeight = Math.Max(scaleWeight, dimWeight);
            if (rawWeight > 0m)
            {
                decimal rounded = Math.Ceiling(rawWeight / increment) * increment;
                totalExpected += rounded;
            }
        }

        return Math.Round(totalExpected, 2, MidpointRounding.AwayFromZero);
    }

    private static bool TryResolveCarrierBilledWeight(
        CarrierCostAdjustment? adjustment,
        CarrierCostRecord costRecord,
        out decimal billedWeightKg)
    {
        billedWeightKg = 0m;

        // Structured carrier-billed weight evidence (Gate D)
        if (adjustment?.BilledWeightKg.HasValue == true && adjustment.BilledWeightKg.Value > 0m)
        {
            billedWeightKg = adjustment.BilledWeightKg.Value;
            return true;
        }

        if (costRecord.BilledWeightKg.HasValue && costRecord.BilledWeightKg.Value > 0m)
        {
            billedWeightKg = costRecord.BilledWeightKg.Value;
            return true;
        }

        return false;
    }

    private static bool TryResolveContractualExpectedBaseCost(
        Shipment shipment,
        ClientFreightAgreement? agreement,
        RateCardSchedule? rateCardSchedule,
        decimal expectedBillableWeight,
        out decimal contractualBaseCost,
        out string currency,
        out bool isWeightDependent)
    {
        contractualBaseCost = 0m;
        currency = "USD";
        isWeightDependent = false;

        // 1. Check RateCardSchedule (commercial rate card line rating via CalculateRating)
        if (rateCardSchedule != null)
        {
            var freightLine = rateCardSchedule.FindLine(BillableActivityType.CarrierFreight);
            if (freightLine != null)
            {
                currency = rateCardSchedule.Currency ?? "USD";
                isWeightDependent = string.Equals(freightLine.UnitOfMeasure, "KG", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(freightLine.UnitOfMeasure, "LB", StringComparison.OrdinalIgnoreCase)
                                 || freightLine.TierModel != PricingTierCalculationModel.FlatRate
                                 || freightLine.Tiers.Count > 0;
                var ratingResult = freightLine.CalculateRating(expectedBillableWeight);
                contractualBaseCost = ratingResult.TotalAmount;
                return true;
            }
        }

        // 2. Check ClientFreightAgreement (fixed contractual rate)
        if (agreement != null)
        {
            currency = agreement.Currency ?? "USD";
            if (agreement.FixedFreightRate.HasValue && agreement.FixedFreightRate.Value > 0m)
            {
                contractualBaseCost = agreement.FixedFreightRate.Value;
                isWeightDependent = false;
                return true;
            }
        }

        return false;
    }

    private static (DiscrepancyRootCauseCategory RootCause, string Reason) ClassifyDiscrepancy(
        ReconciledShipmentFinancialsDto s,
        CarrierCostRecord costRecord,
        decimal expectedCost,
        decimal invoicedCost,
        decimal discrepancy,
        decimal? contractualRateVariance)
    {
        if (costRecord.Adjustments.Any(a => a.AdjustmentType == CarrierCostAdjustmentType.WeightDiscrepancy) ||
            s.AdjustmentAuditTrail.Any(a => a.AdjustmentType == CarrierCostAdjustmentType.WeightDiscrepancy))
        {
            return (DiscrepancyRootCauseCategory.DimensionalWeightDrift,
                $"Carrier re-weighed parcel exceeding expected billable weight (variance: ${discrepancy:N2}).");
        }

        if (contractualRateVariance.HasValue && contractualRateVariance.Value > 0.05m)
        {
            return (DiscrepancyRootCauseCategory.BaseRateOvercharge,
                $"Carrier base rate exceeds contractual rate card by ${contractualRateVariance.Value:N2}.");
        }

        if (costRecord.AccessorialCost > 0m || s.InvoicedCarrierAccessorialsUsd > 0m)
        {
            decimal accessorialAmount = costRecord.AccessorialCost > 0m ? costRecord.AccessorialCost : s.InvoicedCarrierAccessorialsUsd;
            return (DiscrepancyRootCauseCategory.UncontractedAccessorial,
                $"Carrier assessed unexpected accessorial surcharges (${accessorialAmount:N2}).");
        }

        if (costRecord.Adjustments.Any(a => a.AdjustmentType == CarrierCostAdjustmentType.LateAccessorial) ||
            s.AdjustmentAuditTrail.Any(a => a.AdjustmentType == CarrierCostAdjustmentType.LateAccessorial))
        {
            return (DiscrepancyRootCauseCategory.LateAdjustment,
                $"Carrier assessed post-delivery late adjustment (${discrepancy:N2}).");
        }

        if (costRecord.Adjustments.Any(a => a.AdjustmentType == CarrierCostAdjustmentType.RateCorrection) ||
            s.AdjustmentAuditTrail.Any(a => a.AdjustmentType == CarrierCostAdjustmentType.RateCorrection))
        {
            return (DiscrepancyRootCauseCategory.BaseRateOvercharge,
                $"Carrier invoice rate adjustment applied (${discrepancy:N2}).");
        }

        if (Math.Abs(discrepancy) > 0m)
        {
            return (DiscrepancyRootCauseCategory.Other,
                $"Variance between carrier invoice (${invoicedCost:N2}) and expected baseline (${expectedCost:N2}).");
        }

        return (DiscrepancyRootCauseCategory.Other, "Carrier billing matches expected baseline.");
    }

    private static string GetCategoryDescription(AccessorialLeakageCategory category) => category switch
    {
        AccessorialLeakageCategory.AbsorbedByContract => "Carrier surcharge contractually absorbed by 3PL warehouse under client agreement.",
        AccessorialLeakageCategory.UnbilledOperationalCharge => "Billable accessorial charge omitted from client invoice lines (operational leakage).",
        AccessorialLeakageCategory.AddressCorrectionPenalty => "Carrier address correction fee not recovered from client billing.",
        AccessorialLeakageCategory.FuelIndexingDrift => "Carrier fuel surcharge drift exceeding client fuel billing recovery.",
        AccessorialLeakageCategory.DimensionalWeightAdjustment => "Volumetric weight re-rating penalty by carrier not recovered from client.",
        AccessorialLeakageCategory.CarrierOvercharge => "Erroneous carrier surcharge identified for dispute and quarantined from client billing.",
        AccessorialLeakageCategory.ClientUnderRecovery => "Client billing schedule shortfall relative to carrier accessorial actual cost.",
        _ => "Standard accessorial variance within acceptable margin tolerance."
    };

    private void EnforceFilterAuthorization(
        Guid? requestedClientId,
        Guid? requestedFacilityId,
        out Guid? effectiveClientId,
        out Guid? effectiveFacilityId,
        out HashSet<Guid>? clientAuthorizedFacilities)
    {
        clientAuthorizedFacilities = null;

        if (_currentUserContext.IsSuperAdmin)
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
