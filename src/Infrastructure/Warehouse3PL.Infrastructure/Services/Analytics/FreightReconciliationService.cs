using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Authoritative financial reconciliation analytics service reconciling buy-side and sell-side freight spend.
/// Strictly enforces anti-double counting, signed adjustment accounting, multi-package conservation, and fail-closed authorization.
/// DB-SPEC-3PL-2026 Phase 6.4.4.1 Buy-Side &amp; Sell-Side Financial Reconciliation.
/// </summary>
public class FreightReconciliationService : IFreightReconciliationService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IFxRateProvider _fxRateProvider;
    private readonly ILogger<FreightReconciliationService> _logger;

    public FreightReconciliationService(
        IApplicationDbContext dbContext,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUserContext,
        IFxRateProvider fxRateProvider,
        ILogger<FreightReconciliationService> logger)
    {
        _dbContext = dbContext;
        _tenantProvider = tenantProvider;
        _currentUserContext = currentUserContext;
        _fxRateProvider = fxRateProvider ?? throw new ArgumentNullException(nameof(fxRateProvider));
        _logger = logger;
    }

    public async Task<ReconciledShipmentFinancialsDto> GetShipmentFinancialReconciliationAsync(
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments
            .AsNoTracking()
            .Include(s => s.Packages)
            .FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);

        if (shipment == null)
        {
            throw new EntityNotFoundException(nameof(Shipment), shipmentId);
        }

        // 1. Enforce Fail-Closed Tenant and Facility Authorization
        EnforceShipmentAuthorization(shipment);

        // 2. Canonical Buy-Side Cost Retrieval & Deduplication
        var costRecords = await _dbContext.CarrierCostRecords
            .AsNoTracking()
            .Include(c => c.Adjustments)
            .Include(c => c.AccessorialCharges)
            .Where(c => c.ShipmentId == shipmentId)
            .OrderBy(c => c.CostAssessedAtUtc)
            .ToListAsync(cancellationToken);

        var conversionAuditTrail = new List<CurrencyConversionEvidence>();

        // 2A. Quoted Baseline: Select latest Estimated quote
        var quotedRecord = costRecords
            .Where(c => c.Status == CarrierCostStatus.Estimated)
            .OrderByDescending(c => c.CostAssessedAtUtc)
            .FirstOrDefault();

        decimal quotedCarrierCostUsd = 0m;
        if (quotedRecord != null)
        {
            var qConv = NormalizeCurrency(
                quotedRecord.TotalCarrierCost,
                quotedRecord.Currency,
                "USD",
                quotedRecord.CostAssessedAtUtc);
            quotedCarrierCostUsd = qConv.ConvertedAmountUsd;
            conversionAuditTrail.Add(qConv);
        }

        // 2B. Tender Estimate: Select latest ActualConfirmed estimate
        var confirmedRecord = costRecords
            .Where(c => c.Status == CarrierCostStatus.ActualConfirmed)
            .OrderByDescending(c => c.CostAssessedAtUtc)
            .FirstOrDefault();

        decimal estimatedCarrierCostUsd = 0m;
        if (confirmedRecord != null)
        {
            var eConv = NormalizeCurrency(
                confirmedRecord.TotalCarrierCost,
                confirmedRecord.Currency,
                "USD",
                confirmedRecord.CostAssessedAtUtc);
            estimatedCarrierCostUsd = eConv.ConvertedAmountUsd;
            conversionAuditTrail.Add(eConv);
        }
        else if (quotedRecord != null)
        {
            estimatedCarrierCostUsd = quotedCarrierCostUsd;
        }

        // 2C. Authoritative Invoiced Charges:
        // Canonical Charge-Line Identity Deduplication:
        // Prevents identical duplicate carrier invoice charges from inflating buy-side totals.
        var rawInvoicedRecords = costRecords
            .Where(c => c.Status == CarrierCostStatus.Invoiced
                     || c.Status == CarrierCostStatus.Reconciled
                     || c.Status == CarrierCostStatus.Disputed)
            .ToList();

        var invoicedRecords = rawInvoicedRecords
            .GroupBy(c => !string.IsNullOrWhiteSpace(c.IdempotencyKey)
                ? $"KEY:{c.IdempotencyKey.Trim().ToUpperInvariant()}"
                : (!string.IsNullOrWhiteSpace(c.CarrierInvoiceNumber)
                    ? $"INV:{c.CarrierCode.Trim().ToUpperInvariant()}:{c.CarrierInvoiceNumber.Trim().ToUpperInvariant()}:{c.TrackingNumber?.Trim().ToUpperInvariant()}:{c.PackageId}"
                    : $"ID:{c.Id}"))
            .Select(g => g.OrderByDescending(c => c.CostAssessedAtUtc).First())
            .ToList();

        decimal invoicedBaseAmountUsd = 0m;
        decimal invoicedAccessorialsAmountUsd = 0m;
        decimal totalAdjustmentsAmountUsd = 0m;
        var adjustmentsAudit = new List<CarrierAdjustmentAuditDto>();
        var carrierInvoiceNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        FreightReconciliationStatus buySideStatus;

        if (invoicedRecords.Count > 0)
        {
            foreach (var cost in invoicedRecords)
            {
                if (!string.IsNullOrWhiteSpace(cost.CarrierInvoiceNumber))
                {
                    carrierInvoiceNumbers.Add(cost.CarrierInvoiceNumber.Trim());
                }

                // Currency Normalization for individual base charge
                var baseConv = NormalizeCurrency(cost.BaseTransportationCost, cost.Currency, "USD", cost.CostAssessedAtUtc);
                invoicedBaseAmountUsd += baseConv.ConvertedAmountUsd;
                conversionAuditTrail.Add(baseConv);

                // Accessorial & Surcharge Accounting:
                // Fuel and Insurance are explicit non-base surcharges on CarrierCostRecord.
                // Detailed accessorial items in AccessorialCharges (e.g. liftgate, detention) are aggregated in addition to fuel & insurance.
                decimal detailedAccessorials = cost.AccessorialCharges.Count > 0
                    ? cost.AccessorialCharges.Sum(a => a.CarrierAmount)
                    : cost.AccessorialCost;

                decimal totalNonBaseSurcharges = cost.FuelSurcharge + cost.InsuranceCost + detailedAccessorials;

                var accConv = NormalizeCurrency(totalNonBaseSurcharges, cost.Currency, "USD", cost.CostAssessedAtUtc);
                invoicedAccessorialsAmountUsd += accConv.ConvertedAmountUsd;
                conversionAuditTrail.Add(accConv);
            }

            // Detect Idempotency Collisions:
            // If adjustments share an IdempotencyKey but differ in financial content (amounts, type, currency),
            // throw an exception rather than silently choosing one.
            var adjustmentsWithKeys = invoicedRecords
                .SelectMany(c => c.Adjustments.Select(adj => new { Adjustment = adj, ParentCost = c }))
                .Where(x => !string.IsNullOrWhiteSpace(x.Adjustment.IdempotencyKey))
                .GroupBy(x => x.Adjustment.IdempotencyKey.Trim().ToUpperInvariant())
                .ToList();

            foreach (var group in adjustmentsWithKeys)
            {
                var first = group.First();
                bool hasConflict = group.Any(item =>
                    item.Adjustment.CarrierCostDelta != first.Adjustment.CarrierCostDelta ||
                    item.Adjustment.AdjustmentType != first.Adjustment.AdjustmentType ||
                    item.Adjustment.OriginalCarrierCost != first.Adjustment.OriginalCarrierCost ||
                    item.Adjustment.AdjustedCarrierCost != first.Adjustment.AdjustedCarrierCost ||
                    !string.Equals(item.Adjustment.Currency ?? item.ParentCost.Currency, first.Adjustment.Currency ?? first.ParentCost.Currency, StringComparison.OrdinalIgnoreCase));

                if (hasConflict)
                {
                    throw new InvalidOperationException(
                        $"Idempotency collision detected for adjustment key '{group.Key}'. Multiple adjustments share this key but contain conflicting financial values.");
                }
            }

            // Deduplicate adjustments globally across all invoiced records for this shipment
            var allAdjustments = invoicedRecords
                .SelectMany(c => c.Adjustments.Select(adj => new { Adjustment = adj, ParentCost = c }))
                .GroupBy(x => !string.IsNullOrWhiteSpace(x.Adjustment.IdempotencyKey)
                    ? $"IDEM:{x.Adjustment.IdempotencyKey.Trim().ToUpperInvariant()}"
                    : $"KEY:{x.Adjustment.CarrierCostRecordId}:{x.Adjustment.AdjustmentType}:{x.Adjustment.OriginalCarrierCost}:{x.Adjustment.AdjustedCarrierCost}:{x.Adjustment.AdjustedAtUtc:O}")
                .Select(g => g.OrderByDescending(x => x.Adjustment.AdjustedAtUtc).First())
                .ToList();

            foreach (var item in allAdjustments)
            {
                var adj = item.Adjustment;
                var cost = item.ParentCost;

                // Currency Normalization for individual adjustments using parent currency and adjustment date
                var adjCurrency = string.IsNullOrWhiteSpace(adj.Currency) ? cost.Currency : adj.Currency;
                var adjEffectiveDate = adj.AdjustedAtUtc == default ? cost.CostAssessedAtUtc : adj.AdjustedAtUtc;
                var adjConv = NormalizeCurrency(adj.CarrierCostDelta, adjCurrency, "USD", adjEffectiveDate);

                totalAdjustmentsAmountUsd += adjConv.ConvertedAmountUsd;
                conversionAuditTrail.Add(adjConv);

                if (!string.IsNullOrWhiteSpace(adj.CarrierInvoiceNumber))
                {
                    carrierInvoiceNumbers.Add(adj.CarrierInvoiceNumber.Trim());
                }

                adjustmentsAudit.Add(new CarrierAdjustmentAuditDto
                {
                    AdjustmentId = adj.Id,
                    CarrierInvoiceNumber = adj.CarrierInvoiceNumber,
                    AdjustmentType = adj.AdjustmentType,
                    OriginalCost = adj.OriginalCarrierCost,
                    AdjustedCost = adj.AdjustedCarrierCost,
                    CostDelta = adj.CarrierCostDelta,
                    Reason = adj.Reason,
                    AdjustedAtUtc = adj.AdjustedAtUtc
                });
            }

            if (invoicedRecords.Any(c => c.Status == CarrierCostStatus.Disputed))
            {
                buySideStatus = FreightReconciliationStatus.Disputed;
            }
            else if (invoicedRecords.All(c => c.Status == CarrierCostStatus.Reconciled))
            {
                buySideStatus = FreightReconciliationStatus.Reconciled;
            }
            else
            {
                buySideStatus = FreightReconciliationStatus.Invoiced;
            }
        }
        else
        {
            // CRITICAL: Un-invoiced shipment with only quotes/tender estimates must strictly report 0.00m invoiced amounts.
            // Estimates must NEVER enter invoiced base, accessorials, adjustments, or net invoiced totals.
            invoicedBaseAmountUsd = 0m;
            invoicedAccessorialsAmountUsd = 0m;
            totalAdjustmentsAmountUsd = 0m;
            buySideStatus = FreightReconciliationStatus.Estimated;
        }

        decimal netInvoicedCarrierCostUsd = invoicedBaseAmountUsd + invoicedAccessorialsAmountUsd + totalAdjustmentsAmountUsd;
        decimal? reconciledCarrierCostUsd = (buySideStatus == FreightReconciliationStatus.Reconciled && invoicedRecords.Count > 0)
            ? netInvoicedCarrierCostUsd
            : null;

        // 3. Canonical Sell-Side Freight Retrieval
        // Filter strictly to authoritative qualifying invoice statuses: Issued, Paid, PartiallyPaid, Disputed, Approved.
        // Excludes Voided and Draft invoices, and excludes lines without an invoice.
        var marginRecord = await _dbContext.ShipmentFreightMarginRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ShipmentId == shipmentId, cancellationToken);

        var activityIds = await _dbContext.BillableActivities
            .AsNoTracking()
            .Where(b => b.SourceId == shipmentId)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var rawInvoiceLines = await _dbContext.InvoiceLines
            .AsNoTracking()
            .Include(l => l.Invoice)
            .Include(l => l.BillableActivity)
            .Where(l => l.ChargeCategory == InvoiceChargeCategory.Freight
                     && ((l.BillableActivityId.HasValue && activityIds.Contains(l.BillableActivityId.Value))
                         || (l.BillableActivity != null && l.BillableActivity.SourceId == shipmentId)))
            .ToListAsync(cancellationToken);

        // Authoritative qualifying invoice statuses
        var qualifyingInvoiceLines = rawInvoiceLines
            .Where(l => l.Invoice != null && (
                l.Invoice.Status == InvoiceStatus.Issued ||
                l.Invoice.Status == InvoiceStatus.Paid ||
                l.Invoice.Status == InvoiceStatus.PartiallyPaid ||
                l.Invoice.Status == InvoiceStatus.Disputed ||
                l.Invoice.Status == InvoiceStatus.Approved))
            .ToList();

        // Enforce Sell-Side Line Uniqueness & Multi-Line Freight Conservation:
        // Group by distinct InvoiceLine.Id to ensure:
        // A) Multiple BillableActivity records referencing the same InvoiceLine are aggregated exactly ONCE.
        // B) Multiple distinct legitimate freight charges (Base, Fuel, Accessorial) are all conserved.
        var distinctInvoiceLines = qualifyingInvoiceLines
            .GroupBy(l => l.Id)
            .Select(g => g.First())
            .ToList();

        decimal invoicedClientFreightUsd = 0m;
        var clientInvoiceNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        FreightReconciliationStatus sellSideStatus;

        if (distinctInvoiceLines.Count > 0)
        {
            foreach (var l in distinctInvoiceLines)
            {
                if (l.Invoice != null && !string.IsNullOrWhiteSpace(l.Invoice.InvoiceNumber))
                {
                    clientInvoiceNumbers.Add(l.Invoice.InvoiceNumber.Trim());
                }

                // Currency normalization for sell-side invoice lines
                var lineCurrency = l.Invoice?.Currency ?? l.BillableActivity?.Currency;
                if (string.IsNullOrWhiteSpace(lineCurrency))
                {
                    throw new InvalidOperationException($"Invoice line '{l.Id}' has missing currency code.");
                }
                var lineDate = l.Invoice?.InvoiceDate ?? (shipment.DispatchedAtUtc ?? DateTime.UtcNow);
                var lineConv = NormalizeCurrency(l.TotalAmount, lineCurrency, "USD", lineDate);
                invoicedClientFreightUsd += lineConv.ConvertedAmountUsd;
                conversionAuditTrail.Add(lineConv);
            }

            if (distinctInvoiceLines.Any(l => l.Invoice != null && l.Invoice.Status == InvoiceStatus.Disputed))
            {
                sellSideStatus = FreightReconciliationStatus.Disputed;
            }
            else if (marginRecord?.IsReconciled == true || distinctInvoiceLines.All(l => l.Invoice != null && l.Invoice.Status == InvoiceStatus.Paid))
            {
                sellSideStatus = FreightReconciliationStatus.Reconciled;
            }
            else
            {
                sellSideStatus = FreightReconciliationStatus.Invoiced;
            }
        }
        else
        {
            // CRITICAL: When NO qualifying invoice lines exist, invoiced client freight is strictly 0.00m.
            // A ShipmentFreightMarginRecord is an operational estimate/commercial baseline, NEVER an authoritative client invoice.
            invoicedClientFreightUsd = 0.00m;
            sellSideStatus = FreightReconciliationStatus.Estimated;
        }

        // Resolve estimated client freight:
        // Distinguish "no estimate exists" (marginRecord == null) from "estimate exists and is zero" (marginRecord.TotalClientFreight == 0m)
        decimal estimatedClientFreightUsd;
        if (marginRecord != null)
        {
            if (string.IsNullOrWhiteSpace(marginRecord.Currency))
            {
                throw new InvalidOperationException($"ShipmentFreightMarginRecord '{marginRecord.Id}' has missing currency code.");
            }

            var mConv = NormalizeCurrency(
                marginRecord.TotalClientFreight,
                marginRecord.Currency,
                "USD",
                marginRecord.RatedAtUtc);
            estimatedClientFreightUsd = mConv.ConvertedAmountUsd;
            conversionAuditTrail.Add(mConv);
        }
        else
        {
            // No prior estimate record exists: fall back to invoiced client freight
            estimatedClientFreightUsd = invoicedClientFreightUsd;
        }

        decimal? reconciledClientFreightUsd = (sellSideStatus == FreightReconciliationStatus.Reconciled && distinctInvoiceLines.Count > 0)
            ? invoicedClientFreightUsd
            : null;

        // 4. Deterministic Multi-Package Allocation
        var packageList = shipment.Packages
            .Select(p => (
                PackageId: p.Id,
                PackageNumber: p.PackageNumber,
                WeightKg: p.GrossWeightKg > 0m ? p.GrossWeightKg : (p.NetWeightKg > 0m ? p.NetWeightKg : 0m)))
            .ToList();

        var packageAllocations = AllocateShipmentCostsToPackages(
            netInvoicedCarrierCostUsd,
            invoicedClientFreightUsd,
            packageList);

        // 5. Build Comprehensive Audit DTO
        var primaryEvidence = conversionAuditTrail.LastOrDefault();
        var trackingNumber = shipment.Packages.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.TrackingNumber))?.TrackingNumber ?? string.Empty;

        return new ReconciledShipmentFinancialsDto
        {
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            TrackingNumber = trackingNumber,
            CarrierCode = shipment.CarrierCode,
            CarrierServiceLevel = shipment.CarrierServiceLevel,
            FacilityId = shipment.FacilityId,
            ClientId = shipment.ClientId,
            DispatchedAtUtc = shipment.DispatchedAtUtc,
            RatedAtUtc = marginRecord?.RatedAtUtc,
            QuotedCarrierCostUsd = quotedCarrierCostUsd,
            EstimatedCarrierCostUsd = estimatedCarrierCostUsd,
            InvoicedCarrierBaseCostUsd = invoicedBaseAmountUsd,
            InvoicedCarrierAccessorialsUsd = invoicedAccessorialsAmountUsd,
            TotalCarrierAdjustmentsUsd = totalAdjustmentsAmountUsd,
            NetInvoicedCarrierCostUsd = netInvoicedCarrierCostUsd,
            ReconciledCarrierCostUsd = reconciledCarrierCostUsd,
            BuySideReconciliationStatus = buySideStatus,
            EstimatedClientFreightUsd = estimatedClientFreightUsd,
            InvoicedClientFreightUsd = invoicedClientFreightUsd,
            ReconciledClientFreightUsd = reconciledClientFreightUsd,
            SellSideReconciliationStatus = sellSideStatus,
            PackageAllocations = packageAllocations,
            CarrierInvoiceNumbers = carrierInvoiceNumbers.OrderBy(n => n).ToList().AsReadOnly(),
            ClientInvoiceNumbers = clientInvoiceNumbers.OrderBy(n => n).ToList().AsReadOnly(),
            AdjustmentAuditTrail = adjustmentsAudit.OrderBy(a => a.AdjustedAtUtc).ToList().AsReadOnly(),
            ConversionEvidence = primaryEvidence,
            ConversionEvidenceAuditTrail = conversionAuditTrail.AsReadOnly()
        };
    }

    public async Task<ReconciliationBatchSummaryDto> GetReconciledShipmentFinancialsBatchAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        string? carrierCode = null,
        Guid? facilityId = null,
        Guid? clientId = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Boundary Authorization Enforcement (Fail-Closed)
        EnforceFilterAuthorization(clientId, facilityId, out var effectiveClientId, out var effectiveFacilityId, out var clientAuthorizedFacilities);

        var start = startDateUtc.Date;
        var end = endDateUtc.Date.AddDays(1).AddTicks(-1);

        // 2. Query distinct Shipments (Prevents Duplicate Aggregation)
        // CarrierFreightCostSummary is an analytical read model and is NEVER joined into financial totals.
        var shipmentQuery = _dbContext.Shipments
            .AsNoTracking()
            .Where(s => s.DispatchedAtUtc >= start && s.DispatchedAtUtc <= end);

        if (!string.IsNullOrWhiteSpace(carrierCode))
        {
            var normalizedCarrier = carrierCode.Trim().ToUpperInvariant();
            shipmentQuery = shipmentQuery.Where(s => s.CarrierCode == normalizedCarrier);
        }

        if (effectiveFacilityId.HasValue)
        {
            shipmentQuery = shipmentQuery.Where(s => s.FacilityId == effectiveFacilityId.Value);
        }
        else if (clientAuthorizedFacilities != null && clientAuthorizedFacilities.Count > 0)
        {
            // Restrict client with no specific facility parameter to only their permitted facilities
            shipmentQuery = shipmentQuery.Where(s => clientAuthorizedFacilities.Contains(s.FacilityId));
        }

        if (effectiveClientId.HasValue)
        {
            shipmentQuery = shipmentQuery.Where(s => s.ClientId == effectiveClientId.Value);
        }

        var shipmentIds = await shipmentQuery
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var shipmentDtos = new List<ReconciledShipmentFinancialsDto>(shipmentIds.Count);
        foreach (var sId in shipmentIds)
        {
            var recon = await GetShipmentFinancialReconciliationAsync(sId, cancellationToken);
            shipmentDtos.Add(recon);
        }

        // 3. Batch Aggregation
        int totalShipments = shipmentDtos.Count;
        decimal totalQuoted = shipmentDtos.Sum(s => s.QuotedCarrierCostUsd);
        decimal totalEstimated = shipmentDtos.Sum(s => s.EstimatedCarrierCostUsd);
        decimal totalInvoicedCarrier = shipmentDtos.Sum(s => s.InvoicedCarrierBaseCostUsd + s.InvoicedCarrierAccessorialsUsd);
        decimal totalNetInvoicedCarrier = shipmentDtos.Sum(s => s.NetInvoicedCarrierCostUsd);
        decimal totalReconciledCarrier = shipmentDtos.Sum(s => s.ReconciledCarrierCostUsd ?? 0m);

        decimal totalInvoicedClient = shipmentDtos.Sum(s => s.InvoicedClientFreightUsd);
        decimal totalReconciledClient = shipmentDtos.Sum(s => s.ReconciledClientFreightUsd ?? 0m);

        int totalAdjustmentsCount = shipmentDtos.Sum(s => s.AdjustmentAuditTrail.Count);
        decimal totalAdjustmentsAmount = shipmentDtos.Sum(s => s.TotalCarrierAdjustmentsUsd);

        int disputedCount = shipmentDtos.Count(s =>
            s.BuySideReconciliationStatus == FreightReconciliationStatus.Disputed ||
            s.SellSideReconciliationStatus == FreightReconciliationStatus.Disputed);

        decimal disputedAmount = shipmentDtos
            .Where(s => s.BuySideReconciliationStatus == FreightReconciliationStatus.Disputed ||
                        s.SellSideReconciliationStatus == FreightReconciliationStatus.Disputed)
            .Sum(s => s.NetInvoicedCarrierCostUsd);

        return new ReconciliationBatchSummaryDto
        {
            TotalShipmentsCount = totalShipments,
            TotalQuotedAmountUsd = totalQuoted,
            TotalEstimatedAmountUsd = totalEstimated,
            TotalInvoicedCarrierCostUsd = totalInvoicedCarrier,
            TotalNetInvoicedCarrierCostUsd = totalNetInvoicedCarrier,
            TotalReconciledCarrierCostUsd = totalReconciledCarrier,
            TotalInvoicedClientFreightUsd = totalInvoicedClient,
            TotalReconciledClientFreightUsd = totalReconciledClient,
            TotalAdjustmentsCount = totalAdjustmentsCount,
            TotalAdjustmentsAmountUsd = totalAdjustmentsAmount,
            DisputedShipmentsCount = disputedCount,
            DisputedAmountUsd = disputedAmount,
            Shipments = shipmentDtos.AsReadOnly()
        };
    }

    public IReadOnlyList<PackageCostAllocationDto> AllocateShipmentCostsToPackages(
        decimal totalCarrierCost,
        decimal totalClientFreight,
        IReadOnlyList<(Guid PackageId, string PackageNumber, decimal WeightKg)> packages)
    {
        if (packages == null || packages.Count == 0)
        {
            return Array.Empty<PackageCostAllocationDto>();
        }

        // Deduplicate packages by PackageId if duplicate identifiers are passed
        var distinctPackages = packages
            .GroupBy(p => p.PackageId)
            .Select(g => g.First())
            .ToList();

        // Validate weights: negative weights are treated as 0 (unweighed)
        var sanitizedPackages = distinctPackages
            .Select(p => (
                p.PackageId,
                PackageNumber: string.IsNullOrWhiteSpace(p.PackageNumber) ? $"PKG-{p.PackageId:N}"[..10].ToUpperInvariant() : p.PackageNumber.Trim(),
                WeightKg: p.WeightKg > 0m ? p.WeightKg : 0m))
            .ToList();

        bool hasMissingOrZeroWeights = sanitizedPackages.Any(p => p.WeightKg <= 0m);
        decimal totalWeight = sanitizedPackages.Sum(p => p.WeightKg);
        bool isFallbackEqualAllocation = totalWeight <= 0m || hasMissingOrZeroWeights;

        if (sanitizedPackages.Count == 1)
        {
            return new List<PackageCostAllocationDto>
            {
                new PackageCostAllocationDto
                {
                    PackageId = sanitizedPackages[0].PackageId,
                    PackageNumber = sanitizedPackages[0].PackageNumber,
                    WeightKg = sanitizedPackages[0].WeightKg,
                    WeightRatio = 1.0m,
                    AllocatedCarrierCostUsd = totalCarrierCost,
                    AllocatedClientFreightUsd = totalClientFreight,
                    IsFallbackEqualAllocationApplied = sanitizedPackages[0].WeightKg <= 0m
                }
            };
        }

        decimal effectiveTotalWeight = isFallbackEqualAllocation ? sanitizedPackages.Count : totalWeight;

        var allocations = new List<PackageCostAllocationDto>(sanitizedPackages.Count);
        decimal runningCarrierSum = 0m;
        decimal runningClientSum = 0m;

        for (int i = 0; i < sanitizedPackages.Count; i++)
        {
            var p = sanitizedPackages[i];
            decimal effectiveWeight = isFallbackEqualAllocation ? 1.0m : p.WeightKg;
            decimal ratio = Math.Round(effectiveWeight / effectiveTotalWeight, 4, MidpointRounding.AwayFromZero);
            decimal carrierShare = Math.Round(totalCarrierCost * (effectiveWeight / effectiveTotalWeight), 2, MidpointRounding.AwayFromZero);
            decimal clientShare = Math.Round(totalClientFreight * (effectiveWeight / effectiveTotalWeight), 2, MidpointRounding.AwayFromZero);

            runningCarrierSum += carrierShare;
            runningClientSum += clientShare;

            allocations.Add(new PackageCostAllocationDto
            {
                PackageId = p.PackageId,
                PackageNumber = p.PackageNumber,
                WeightKg = p.WeightKg,
                WeightRatio = ratio,
                AllocatedCarrierCostUsd = carrierShare,
                AllocatedClientFreightUsd = clientShare,
                IsFallbackEqualAllocationApplied = isFallbackEqualAllocation
            });
        }

        // Exact Cent Conservation Rule:
        // Adjust for any rounding residuals (CarrierCostResidual and ClientFreightResidual)
        // by attributing the single cent residual to the heaviest package.
        // Deterministic Tie-Breaker: Order by WeightKg desc, PackageNumber asc, PackageId asc.
        decimal carrierResidual = totalCarrierCost - runningCarrierSum;
        decimal clientResidual = totalClientFreight - runningClientSum;

        if (carrierResidual != 0m || clientResidual != 0m)
        {
            var primaryPackage = allocations
                .OrderByDescending(a => a.WeightKg)
                .ThenBy(a => a.PackageNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.PackageId)
                .First();

            int targetIndex = allocations.IndexOf(primaryPackage);
            allocations[targetIndex] = primaryPackage with
            {
                AllocatedCarrierCostUsd = primaryPackage.AllocatedCarrierCostUsd + carrierResidual,
                AllocatedClientFreightUsd = primaryPackage.AllocatedClientFreightUsd + clientResidual
            };
        }

        return allocations.AsReadOnly();
    }

    public CurrencyConversionEvidence NormalizeCurrency(
        decimal amount,
        string sourceCurrency,
        string targetCurrency,
        DateTime effectiveDateUtc,
        decimal? exchangeRate = null,
        string? rateSource = null)
    {
        if (string.IsNullOrWhiteSpace(sourceCurrency))
        {
            throw new ArgumentException("Source currency code cannot be null or whitespace.", nameof(sourceCurrency));
        }

        if (string.IsNullOrWhiteSpace(targetCurrency))
        {
            throw new ArgumentException("Target currency code cannot be null or whitespace.", nameof(targetCurrency));
        }

        var src = sourceCurrency.Trim().ToUpperInvariant();
        var tgt = targetCurrency.Trim().ToUpperInvariant();

        var utcDate = effectiveDateUtc.Kind == DateTimeKind.Local ? effectiveDateUtc.ToUniversalTime() : effectiveDateUtc;

        if (string.Equals(src, tgt, StringComparison.OrdinalIgnoreCase))
        {
            return new CurrencyConversionEvidence(
                originalCurrency: src,
                originalAmount: amount,
                exchangeRate: 1.0m,
                effectiveDateUtc: utcDate,
                rateSource: rateSource ?? "IdentityRate",
                rateVersion: "Identity-1.0",
                targetCurrency: tgt,
                roundingDecimals: 4,
                roundingMode: MidpointRounding.AwayFromZero,
                benchmarkSeriesId: "IDENTITY",
                integrityHash: "IDENTITY",
                publishedAtUtc: utcDate);
        }

        if (exchangeRate.HasValue)
        {
            return new CurrencyConversionEvidence(
                originalCurrency: src,
                originalAmount: amount,
                exchangeRate: exchangeRate.Value,
                effectiveDateUtc: utcDate,
                rateSource: rateSource ?? "ExplicitProvided",
                rateVersion: "Explicit-1.0",
                targetCurrency: tgt,
                roundingDecimals: 4,
                roundingMode: MidpointRounding.AwayFromZero,
                benchmarkSeriesId: "EXPLICIT",
                integrityHash: "EXPLICIT",
                publishedAtUtc: utcDate);
        }

        decimal rate = _fxRateProvider.GetHistoricalExchangeRate(
            src,
            tgt,
            utcDate,
            out var source,
            out var version,
            out var seriesId,
            out var hash,
            out var publishedAt);

        return new CurrencyConversionEvidence(
            originalCurrency: src,
            originalAmount: amount,
            exchangeRate: rate,
            effectiveDateUtc: utcDate,
            rateSource: rateSource ?? source,
            rateVersion: version,
            targetCurrency: tgt,
            roundingDecimals: 4,
            roundingMode: MidpointRounding.AwayFromZero,
            benchmarkSeriesId: seriesId,
            integrityHash: hash,
            publishedAtUtc: publishedAt);
    }

    #region Private Authorization & Verification Helpers

    private void EnforceShipmentAuthorization(Shipment shipment)
    {
        if (!_currentUserContext.IsAuthenticated)
        {
            throw new UnauthorizedException("Authentication required to access freight financial records.");
        }

        if (_currentUserContext.IsSuperAdmin)
        {
            return;
        }

        if (!_currentUserContext.IsStaff)
        {
            EnforceClientShipmentAuthorization(shipment);
            return;
        }

        // Staff Users: Fail-closed requirement
        // Staff user MUST have cross-facility permission OR an assigned facility matching the shipment.
        bool hasCrossFacilityPermission = _currentUserContext.HasPermission("Analytics:CrossFacility")
                                       || _currentUserContext.HasPermission("Financial:CrossFacility");

        if (hasCrossFacilityPermission)
        {
            return;
        }

        var staffFacilityId = _currentUserContext.FacilityId;
        if (!staffFacilityId.HasValue || staffFacilityId.Value == Guid.Empty)
        {
            throw new ForbiddenException("Access denied. Staff user has no assigned facility and lacks cross-facility permission.");
        }

        if (staffFacilityId.Value != shipment.FacilityId)
        {
            throw new ForbiddenException($"Access denied. Shipment '{shipment.ShipmentNumber}' belongs to facility '{shipment.FacilityId}', which is outside staff assigned facility '{staffFacilityId.Value}'.");
        }
    }

    private void EnforceClientShipmentAuthorization(Shipment shipment)
    {
        var userTenantId = _currentUserContext.TenantId ?? _tenantProvider.TenantId;
        if (!userTenantId.HasValue || userTenantId.Value == Guid.Empty || userTenantId.Value != shipment.ClientId)
        {
            throw new ForbiddenException($"Access denied. Shipment '{shipment.ShipmentNumber}' belongs to tenant '{shipment.ClientId}'.");
        }

        // Fail-Closed: Always verify that the client has an active facility assignment in ClientFacilityAccess
        var hasActiveFacilityAccess = _dbContext.ClientFacilityAccesses
            .Any(cfa => cfa.ClientId == userTenantId.Value
                     && cfa.FacilityId == shipment.FacilityId
                     && cfa.Status == FacilityAccessStatus.Active);

        if (!hasActiveFacilityAccess)
        {
            throw new ForbiddenException($"Access denied. Client '{userTenantId.Value}' does not have active authorized access to facility '{shipment.FacilityId}'.");
        }

        // If client user context carries a specific assigned facility, enforce that it matches the shipment facility
        var userFacilityId = _currentUserContext.FacilityId;
        if (userFacilityId.HasValue && userFacilityId.Value != Guid.Empty && userFacilityId.Value != shipment.FacilityId)
        {
            throw new ForbiddenException($"Access denied. Shipment '{shipment.ShipmentNumber}' belongs to facility '{shipment.FacilityId}', outside user assigned facility '{userFacilityId.Value}'.");
        }
    }

    private void EnforceFilterAuthorization(
        Guid? requestedClientId,
        Guid? requestedFacilityId,
        out Guid? effectiveClientId,
        out Guid? effectiveFacilityId,
        out List<Guid>? clientAuthorizedFacilities)
    {
        clientAuthorizedFacilities = null;

        // 1. Unauthenticated callers are strictly DENIED access
        if (!_currentUserContext.IsAuthenticated)
        {
            throw new UnauthorizedException("Authentication required to access freight financial records.");
        }

        // 2. SuperAdmin: Unrestricted across all tenants and facilities
        if (_currentUserContext.IsSuperAdmin)
        {
            effectiveClientId = requestedClientId;
            effectiveFacilityId = requestedFacilityId;
            return;
        }

        // 3. Staff Users: Fail-closed requirement
        if (_currentUserContext.IsStaff)
        {
            effectiveClientId = requestedClientId;

            bool hasCrossFacilityPermission = _currentUserContext.HasPermission("Analytics:CrossFacility")
                                           || _currentUserContext.HasPermission("Financial:CrossFacility");

            if (hasCrossFacilityPermission)
            {
                effectiveFacilityId = requestedFacilityId;
                return;
            }

            var staffFacilityId = _currentUserContext.FacilityId;
            if (!staffFacilityId.HasValue || staffFacilityId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Access denied. Staff user has no assigned facility and lacks cross-facility permission.");
            }

            if (requestedFacilityId.HasValue && requestedFacilityId.Value != staffFacilityId.Value)
            {
                throw new ForbiddenException($"Access denied. Staff user is restricted to assigned facility '{staffFacilityId.Value}'.");
            }

            effectiveFacilityId = staffFacilityId.Value;
            return;
        }

        // 4. Client Users: Strictly locked to their own TenantId and permitted facilities
        var userTenantId = _currentUserContext.TenantId ?? _tenantProvider.TenantId;
        if (!userTenantId.HasValue || userTenantId.Value == Guid.Empty)
        {
            throw new ForbiddenException("Client user context missing authenticated tenant identity.");
        }

        if (requestedClientId.HasValue && requestedClientId.Value != userTenantId.Value)
        {
            throw new ForbiddenException($"Access denied. You cannot access freight financial records for client tenant '{requestedClientId.Value}'.");
        }
        effectiveClientId = userTenantId.Value;

        // Retrieve all currently ACTIVE facility assignments for this client
        var authorizedFacilityIds = _dbContext.ClientFacilityAccesses
            .Where(cfa => cfa.ClientId == userTenantId.Value && cfa.Status == FacilityAccessStatus.Active)
            .Select(cfa => cfa.FacilityId)
            .ToList();

        if (authorizedFacilityIds.Count == 0)
        {
            throw new ForbiddenException($"Access denied. Client tenant '{userTenantId.Value}' has no active facility access assignments.");
        }

        var userFacilityId = _currentUserContext.FacilityId;
        if (userFacilityId.HasValue && userFacilityId.Value != Guid.Empty)
        {
            // Verify the facility claim in the token is still actively assigned!
            if (!authorizedFacilityIds.Contains(userFacilityId.Value))
            {
                throw new ForbiddenException($"Access denied. Facility claim '{userFacilityId.Value}' is not currently active for client '{userTenantId.Value}'.");
            }

            if (requestedFacilityId.HasValue && requestedFacilityId.Value != userFacilityId.Value)
            {
                throw new ForbiddenException($"Access denied. You cannot access freight financial records for facility '{requestedFacilityId.Value}'.");
            }
            effectiveFacilityId = userFacilityId.Value;
            return;
        }

        // Client has no specific single facility in token: verify against active assignments
        if (requestedFacilityId.HasValue)
        {
            if (!authorizedFacilityIds.Contains(requestedFacilityId.Value))
            {
                throw new ForbiddenException($"Access denied. Client '{userTenantId.Value}' does not have authorized access to facility '{requestedFacilityId.Value}'.");
            }
            effectiveFacilityId = requestedFacilityId.Value;
        }
        else
        {
            effectiveFacilityId = null;
            clientAuthorizedFacilities = authorizedFacilityIds;
        }
    }

    #endregion
}
