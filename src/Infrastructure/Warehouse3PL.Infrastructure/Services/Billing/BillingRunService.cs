using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Service orchestrating automated commercial billing runs.
/// Groups rated activities into invoices per currency and facility, assigns sequential numbers,
/// marks activities as invoiced, and seals the billing period.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class BillingRunService : IBillingRunService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<BillingRunService> _logger;

    public BillingRunService(
        IApplicationDbContext dbContext,
        ILogger<BillingRunService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<BillingRunResult> ExecuteBillingRunAsync(
        BillingRunRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Initiating commercial billing run for Client {ClientId}, Period {BillingPeriodId}, DryRun: {DryRun}",
            request.ClientId, request.BillingPeriodId, request.DryRun);

        var period = await _dbContext.BillingPeriods
            .Include(p => p.Client)
            .FirstOrDefaultAsync(p => p.Id == request.BillingPeriodId && p.ClientId == request.ClientId, cancellationToken);

        if (period == null)
        {
            throw new EntityNotFoundException(nameof(BillingPeriod), request.BillingPeriodId);
        }

        if (period.Status == BillingPeriodStatus.Closed || period.Status == BillingPeriodStatus.Invoiced)
        {
            throw new InvalidDomainStateException(
                $"Billing period '{period.PeriodCode}' is already '{period.Status}'. Cannot run billing on a sealed or invoiced period.");
        }

        // 1. Detect unrated activities in the billing period
        var unratedQuery = _dbContext.BillableActivities
            .Where(a => a.ClientId == request.ClientId &&
                        (a.BillingPeriodId == request.BillingPeriodId ||
                         (a.OccurredAtUtc >= period.StartDateUtc && a.OccurredAtUtc <= period.EndDateUtc)) &&
                        a.Status == BillableActivityStatus.PendingRating);

        if (request.FacilityId.HasValue)
        {
            unratedQuery = unratedQuery.Where(a => a.FacilityId == request.FacilityId.Value);
        }

        var unratedCount = await unratedQuery.CountAsync(cancellationToken);
        if (unratedCount > 0)
        {
            throw new InvalidDomainStateException(
                $"Cannot execute billing run for period '{period.PeriodCode}'. Found {unratedCount} unrated billable activities with status '{BillableActivityStatus.PendingRating}'. All activities must be rated or excluded prior to billing run.");
        }

        // 2. Query eligible rated and waived activities
        var eligibleQuery = _dbContext.BillableActivities
            .Where(a => a.ClientId == request.ClientId &&
                        (a.BillingPeriodId == request.BillingPeriodId ||
                         (a.OccurredAtUtc >= period.StartDateUtc && a.OccurredAtUtc <= period.EndDateUtc)) &&
                        (a.Status == BillableActivityStatus.Rated || a.Status == BillableActivityStatus.Waived) &&
                        a.InvoiceId == null);

        if (request.FacilityId.HasValue)
        {
            eligibleQuery = eligibleQuery.Where(a => a.FacilityId == request.FacilityId.Value);
        }

        var eligibleActivities = await eligibleQuery
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        if (eligibleActivities.Count == 0)
        {
            _logger.LogInformation("No uninvoiced billable activities found for period '{PeriodCode}'.", period.PeriodCode);
            return new BillingRunResult(
                BillingPeriodId: period.Id,
                ClientId: request.ClientId,
                IsSuccess: true,
                DryRun: request.DryRun,
                InvoicesCreatedCount: 0,
                TotalActivitiesInvoicedCount: 0,
                TotalInvoicedAmount: 0m,
                InvoiceSummaries: Array.Empty<InvoiceSummaryDto>());
        }

        // 3. Concurrency-safe sequence tracking
        var fiscalYear = period.EndDateUtc.Year;
        var sequence = await _dbContext.InvoiceSequences
            .FirstOrDefaultAsync(s => s.ClientId == request.ClientId && s.FiscalYear == fiscalYear && s.Prefix == "INV", cancellationToken);

        if (sequence == null)
        {
            sequence = InvoiceSequence.Create(request.ClientId, fiscalYear, "INV");
            if (!request.DryRun)
            {
                await _dbContext.InvoiceSequences.AddAsync(sequence, cancellationToken);
            }
        }

        var clientCode = !string.IsNullOrWhiteSpace(period.Client?.AccountCode)
            ? period.Client.AccountCode
            : "CLI";

        // 4. Group activities by Currency and (if not consolidated) Facility
        var activityGroups = eligibleActivities
            .GroupBy(a => new
            {
                Currency = string.IsNullOrWhiteSpace(a.Currency) ? "USD" : a.Currency.Trim().ToUpperInvariant(),
                FacilityId = request.ConsolidateByFacility ? (Guid?)null : a.FacilityId
            })
            .ToList();

        var createdInvoices = new List<Invoice>();
        var summaries = new List<InvoiceSummaryDto>();
        var now = DateTime.UtcNow;

        foreach (var group in activityGroups)
        {
            var invoiceNumber = sequence.NextNumber(clientCode);
            var invoice = Invoice.Create(
                clientId: request.ClientId,
                billingPeriodId: period.Id,
                invoiceNumber: invoiceNumber,
                invoiceDate: now.Date,
                paymentTermsDays: request.PaymentTermsDays,
                currency: group.Key.Currency,
                facilityId: group.Key.FacilityId,
                taxRate: request.TaxRate,
                notes: $"Billing run for period {period.PeriodCode} ({period.StartDateUtc:yyyy-MM-dd} to {period.EndDateUtc:yyyy-MM-dd})");

            int lineNumber = 1;
            foreach (var act in group)
            {
                var chargeCategory = ResolveChargeCategory(act.ActivityType);
                var unitPrice = act.UnitPrice ?? 0m;
                var totalAmount = act.TotalAmount ?? 0m;

                var line = InvoiceLine.Create(
                    lineNumber: lineNumber++,
                    category: chargeCategory,
                    description: string.IsNullOrWhiteSpace(act.Description) ? $"{act.ActivityType} ({act.SourceReference})" : act.Description,
                    quantity: act.Quantity,
                    unitOfMeasure: act.UnitOfMeasure,
                    unitPrice: unitPrice,
                    totalAmount: totalAmount,
                    billableActivityId: act.Id,
                    activityType: act.ActivityType,
                    facilityId: act.FacilityId,
                    metadataJson: act.MetadataJson);

                invoice.AddLine(line);

                if (!request.DryRun)
                {
                    act.MarkInvoiced(invoice.Id, line.Id, now);
                    if (act.BillingPeriodId == null)
                    {
                        act.AssignBillingPeriod(period.Id);
                    }
                }
            }

            createdInvoices.Add(invoice);
            summaries.Add(new InvoiceSummaryDto(
                InvoiceId: invoice.Id,
                InvoiceNumber: invoice.InvoiceNumber,
                Currency: invoice.Currency,
                FacilityId: invoice.FacilityId,
                SubtotalAmount: invoice.SubtotalAmount,
                TaxAmount: invoice.TaxAmount,
                TotalAmount: invoice.TotalAmount,
                LinesCount: invoice.Lines.Count));
        }

        // 5. Commit state if not dry-run
        if (!request.DryRun)
        {
            if (period.Status == BillingPeriodStatus.Open)
            {
                period.Lock(now);
            }
            period.Close(now);
            period.MarkInvoiced(now);

            await _dbContext.Invoices.AddRangeAsync(createdInvoices, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var totalInvoiced = summaries.Sum(s => s.TotalAmount);
        var totalActivities = eligibleActivities.Count;

        _logger.LogInformation(
            "Commercial billing run completed. Invoices: {Count}, Activities: {Activities}, Total: {Total} (DryRun: {DryRun})",
            createdInvoices.Count, totalActivities, totalInvoiced, request.DryRun);

        return new BillingRunResult(
            BillingPeriodId: period.Id,
            ClientId: request.ClientId,
            IsSuccess: true,
            DryRun: request.DryRun,
            InvoicesCreatedCount: createdInvoices.Count,
            TotalActivitiesInvoicedCount: totalActivities,
            TotalInvoicedAmount: totalInvoiced,
            InvoiceSummaries: summaries);
    }

    private static InvoiceChargeCategory ResolveChargeCategory(BillableActivityType type)
    {
        return type switch
        {
            BillableActivityType.StoragePalletDay or
            BillableActivityType.StorageCartonDay or
            BillableActivityType.StorageCbmDay => InvoiceChargeCategory.Storage,

            BillableActivityType.InboundReceiving or
            BillableActivityType.InboundInspection or
            BillableActivityType.Putaway or
            BillableActivityType.CrossDock or
            BillableActivityType.OrderProcessing or
            BillableActivityType.PickLine or
            BillableActivityType.PickUnit or
            BillableActivityType.PackingCarton or
            BillableActivityType.PackagingMaterial or
            BillableActivityType.ShippingHandling or
            BillableActivityType.ReturnProcessing or
            BillableActivityType.ReturnInspection or
            BillableActivityType.Restocking => InvoiceChargeCategory.Handling,

            BillableActivityType.CarrierFreight => InvoiceChargeCategory.Freight,

            BillableActivityType.SpecialHandling or
            BillableActivityType.KittingAndAssembly or
            BillableActivityType.CustomVas => InvoiceChargeCategory.Vas,

            _ => InvoiceChargeCategory.Handling
        };
    }
}
