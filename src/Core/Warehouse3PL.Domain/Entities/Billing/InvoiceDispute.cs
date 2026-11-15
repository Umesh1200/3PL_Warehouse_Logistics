using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Domain entity representing a customer or internal commercial dispute against an invoice or invoice line.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class InvoiceDispute : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid? InvoiceLineId { get; set; }

    public string DisputeReference { get; set; } = string.Empty;
    public decimal DisputedAmount { get; set; }

    public DisputeStatus Status { get; set; } = DisputeStatus.Submitted;
    public DisputeReasonCode ReasonCode { get; set; }
    public string ReasonDescription { get; set; } = string.Empty;

    // Submission Auditing
    public DateTime SubmittedAtUtc { get; set; }
    public Guid? SubmittedByUserId { get; set; }

    // Review Auditing
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewNotes { get; set; }

    // Resolution Auditing
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNotes { get; set; }
    public decimal? ApprovedCreditAmount { get; set; }
    public Guid? CreditNoteId { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;
    public InvoiceLine? InvoiceLine { get; set; }
    public CreditNote? CreditNote { get; set; }

    public InvoiceDispute()
    {
        Id = Guid.NewGuid();
    }

    public static InvoiceDispute Create(
        Guid clientId,
        Guid invoiceId,
        string disputeReference,
        decimal disputedAmount,
        DisputeReasonCode reasonCode,
        string reasonDescription,
        Guid? submittedByUserId = null,
        Guid? invoiceLineId = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client tenant ID is required.");
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ValidationException(nameof(invoiceId), "Invoice ID is required.");
        }

        if (string.IsNullOrWhiteSpace(disputeReference))
        {
            throw new ValidationException(nameof(disputeReference), "Dispute reference number is required.");
        }

        if (disputedAmount <= 0)
        {
            throw new ValidationException(nameof(disputedAmount), "Disputed amount must be strictly positive.");
        }

        if (string.IsNullOrWhiteSpace(reasonDescription))
        {
            throw new ValidationException(nameof(reasonDescription), "Dispute reason description is required.");
        }

        return new InvoiceDispute
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            InvoiceId = invoiceId,
            InvoiceLineId = invoiceLineId,
            DisputeReference = disputeReference.Trim().ToUpperInvariant(),
            DisputedAmount = decimal.Round(disputedAmount, 4, MidpointRounding.AwayFromZero),
            Status = DisputeStatus.Submitted,
            ReasonCode = reasonCode,
            ReasonDescription = reasonDescription.Trim(),
            SubmittedAtUtc = DateTime.UtcNow,
            SubmittedByUserId = submittedByUserId
        };
    }

    public void Review(Guid reviewerUserId, string notes)
    {
        if (Status != DisputeStatus.Submitted && Status != DisputeStatus.UnderReview)
        {
            throw new InvalidDomainStateException($"Cannot mark dispute as under review from '{Status}' status.");
        }

        Status = DisputeStatus.UnderReview;
        ReviewedAtUtc = DateTime.UtcNow;
        ReviewedByUserId = reviewerUserId;
        ReviewNotes = notes?.Trim();
    }

    public void Accept(Guid resolverUserId, decimal approvedCreditAmount, string notes, Guid creditNoteId)
    {
        if (Status != DisputeStatus.Submitted && Status != DisputeStatus.UnderReview)
        {
            throw new InvalidDomainStateException($"Cannot accept dispute from '{Status}' status.");
        }

        if (approvedCreditAmount <= 0)
        {
            throw new ValidationException(nameof(approvedCreditAmount), "Approved credit amount must be positive.");
        }

        if (approvedCreditAmount > DisputedAmount)
        {
            throw new InvalidDomainStateException($"Approved credit ({approvedCreditAmount}) cannot exceed disputed amount ({DisputedAmount}).");
        }

        if (creditNoteId == Guid.Empty)
        {
            throw new ValidationException(nameof(creditNoteId), "Associated credit note ID is required when accepting a dispute.");
        }

        Status = DisputeStatus.Accepted;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedByUserId = resolverUserId;
        ApprovedCreditAmount = decimal.Round(approvedCreditAmount, 4, MidpointRounding.AwayFromZero);
        CreditNoteId = creditNoteId;
        ResolutionNotes = notes?.Trim();
    }

    public void Reject(Guid resolverUserId, string notes)
    {
        if (Status != DisputeStatus.Submitted && Status != DisputeStatus.UnderReview)
        {
            throw new InvalidDomainStateException($"Cannot reject dispute from '{Status}' status.");
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new ValidationException(nameof(notes), "Documented resolution notes are required when rejecting a dispute.");
        }

        Status = DisputeStatus.Rejected;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedByUserId = resolverUserId;
        ResolutionNotes = notes.Trim();
    }

    public void MarkResolved()
    {
        if (Status != DisputeStatus.Accepted && Status != DisputeStatus.Rejected)
        {
            throw new InvalidDomainStateException($"Cannot resolve dispute until it has been Accepted or Rejected. Current status: '{Status}'.");
        }

        Status = DisputeStatus.Resolved;
    }
}

