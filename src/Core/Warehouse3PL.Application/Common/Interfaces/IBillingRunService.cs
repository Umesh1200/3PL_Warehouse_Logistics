using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public record BillingRunRequest(
    Guid ClientId,
    Guid BillingPeriodId,
    Guid InitiatedByUserId,
    Guid? FacilityId = null,
    bool DryRun = false,
    bool ConsolidateByFacility = false,
    decimal TaxRate = 0m,
    int PaymentTermsDays = 30);

public record InvoiceSummaryDto(
    Guid InvoiceId,
    string InvoiceNumber,
    string Currency,
    Guid? FacilityId,
    decimal SubtotalAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    int LinesCount);

public record BillingRunResult(
    Guid BillingPeriodId,
    Guid ClientId,
    bool IsSuccess,
    bool DryRun,
    int InvoicesCreatedCount,
    int TotalActivitiesInvoicedCount,
    decimal TotalInvoicedAmount,
    IReadOnlyList<InvoiceSummaryDto> InvoiceSummaries,
    string? ErrorMessage = null);

/// <summary>
/// Service orchestrating automated commercial billing runs.
/// Validates periods, locks periods, groups rated activities into draft invoices per currency,
/// assigns sequential invoice numbers, marks activities as invoiced, and seals periods.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public interface IBillingRunService
{
    Task<BillingRunResult> ExecuteBillingRunAsync(BillingRunRequest request, CancellationToken cancellationToken = default);
}

