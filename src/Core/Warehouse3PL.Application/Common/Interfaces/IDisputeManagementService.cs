using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public record SubmitDisputeRequest(
    Guid ClientId,
    Guid InvoiceId,
    decimal DisputedAmount,
    DisputeReasonCode ReasonCode,
    string ReasonDescription,
    Guid? SubmittedByUserId = null,
    Guid? InvoiceLineId = null);

public record ReviewDisputeRequest(
    Guid DisputeId,
    Guid ReviewedByUserId,
    string ReviewNotes);

public record ResolveDisputeRequest(
    Guid DisputeId,
    Guid ResolvedByUserId,
    bool Accept,
    decimal? ApprovedCreditAmount,
    string ResolutionNotes);

public record InvoiceDisputeDto(
    Guid Id,
    Guid ClientId,
    Guid InvoiceId,
    Guid? InvoiceLineId,
    string DisputeReference,
    decimal DisputedAmount,
    DisputeStatus Status,
    DisputeReasonCode ReasonCode,
    string ReasonDescription,
    DateTime SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    string? ReviewNotes,
    DateTime? ResolvedAtUtc,
    string? ResolutionNotes,
    decimal? ApprovedCreditAmount,
    Guid? CreditNoteId);

/// <summary>
/// Service managing customer and operational invoice disputes:
/// registration, commercial review, and resolution with automatic credit note generation and invoice balance adjustment.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public interface IDisputeManagementService
{
    Task<InvoiceDisputeDto> SubmitDisputeAsync(SubmitDisputeRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceDisputeDto> ReviewDisputeAsync(ReviewDisputeRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceDisputeDto> ResolveDisputeAsync(ResolveDisputeRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceDisputeDto?> GetDisputeByIdAsync(Guid disputeId, CancellationToken cancellationToken = default);
}

