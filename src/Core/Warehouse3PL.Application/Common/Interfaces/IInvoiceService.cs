using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public record InvoiceLineDto(
    Guid Id,
    Guid InvoiceId,
    int LineNumber,
    InvoiceChargeCategory ChargeCategory,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitPrice,
    decimal TotalAmount,
    Guid? BillableActivityId,
    BillableActivityType? ActivityType,
    Guid? FacilityId,
    string? MetadataJson);

public record InvoiceDto(
    Guid Id,
    Guid ClientId,
    Guid? FacilityId,
    Guid BillingPeriodId,
    string InvoiceNumber,
    InvoiceStatus Status,
    DateTime InvoiceDate,
    DateTime DueDate,
    int PaymentTermsDays,
    string Currency,
    decimal SubtotalAmount,
    decimal TaxRate,
    decimal TaxAmount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal CreditedAmount,
    decimal OutstandingBalance,
    DateTime? ApprovedAtUtc,
    Guid? ApprovedByUserId,
    DateTime? IssuedAtUtc,
    Guid? IssuedByUserId,
    DateTime? VoidedAtUtc,
    Guid? VoidedByUserId,
    string? VoidReason,
    Guid? ReplacementInvoiceId,
    string? Notes,
    IReadOnlyList<InvoiceLineDto> Lines);

public record CreditNoteLineDto(
    Guid? OriginalInvoiceLineId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalAmount);

public record CreateCreditNoteRequest(
    Guid ClientId,
    Guid OriginalInvoiceId,
    string Reason,
    IReadOnlyList<CreditNoteLineDto> Lines,
    DateTime? IssueDate = null,
    Guid? DisputeId = null);

public record CreditNoteDto(
    Guid Id,
    Guid ClientId,
    Guid OriginalInvoiceId,
    Guid? DisputeId,
    string CreditNoteNumber,
    CreditNoteStatus Status,
    DateTime IssueDate,
    string Currency,
    decimal TotalAmount,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    string Reason,
    string? VoidReason);

/// <summary>
/// Service governing the lifecycle of commercial invoices and credit notes:
/// approvals, issuance, immutability, voiding, and credit note emissions.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public interface IInvoiceService
{
    Task<InvoiceDto?> GetInvoiceByIdAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<InvoiceDto> ApproveInvoiceAsync(Guid invoiceId, Guid approvedByUserId, CancellationToken cancellationToken = default);
    Task<InvoiceDto> IssueInvoiceAsync(Guid invoiceId, Guid issuedByUserId, CancellationToken cancellationToken = default);
    Task<InvoiceDto> VoidInvoiceAsync(Guid invoiceId, Guid voidedByUserId, string reason, Guid? replacementInvoiceId = null, CancellationToken cancellationToken = default);

    Task<CreditNoteDto> CreateCreditNoteAsync(CreateCreditNoteRequest request, CancellationToken cancellationToken = default);
    Task<CreditNoteDto> IssueCreditNoteAsync(Guid creditNoteId, CancellationToken cancellationToken = default);
    Task<CreditNoteDto> ApplyCreditNoteToInvoiceAsync(Guid creditNoteId, Guid invoiceId, decimal amount, CancellationToken cancellationToken = default);
}

