using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Domain aggregate root for a commercial credit note.
/// Emitted for price corrections, disputed charges, or volume rebates without mutating issued invoices.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class CreditNote : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid OriginalInvoiceId { get; set; }
    public Guid? DisputeId { get; set; }

    public string CreditNoteNumber { get; set; } = string.Empty;
    public CreditNoteStatus Status { get; set; } = CreditNoteStatus.Draft;

    public DateTime IssueDate { get; set; }
    public string Currency { get; set; } = "USD";

    public decimal TotalAmount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal UnallocatedAmount { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string? VoidReason { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Invoice OriginalInvoice { get; set; } = null!;
    public InvoiceDispute? Dispute { get; set; }
    public ICollection<CreditNoteLine> Lines { get; set; } = [];

    public CreditNote()
    {
        Id = Guid.NewGuid();
    }

    public static CreditNote Create(
        Guid clientId,
        Guid originalInvoiceId,
        string creditNoteNumber,
        DateTime issueDate,
        string currency,
        string reason,
        Guid? disputeId = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required.");
        }

        if (originalInvoiceId == Guid.Empty)
        {
            throw new ValidationException(nameof(originalInvoiceId), "Original invoice ID is required.");
        }

        if (string.IsNullOrWhiteSpace(creditNoteNumber))
        {
            throw new ValidationException(nameof(creditNoteNumber), "Credit note number is required.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Credit note reason is required.");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();

        return new CreditNote
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OriginalInvoiceId = originalInvoiceId,
            DisputeId = disputeId,
            CreditNoteNumber = creditNoteNumber.Trim().ToUpperInvariant(),
            Status = CreditNoteStatus.Draft,
            IssueDate = issueDate == default ? DateTime.UtcNow.Date : issueDate.Date,
            Currency = normalizedCurrency,
            Reason = reason.Trim()
        };
    }

    public void AddLine(CreditNoteLine line)
    {
        if (Status != CreditNoteStatus.Draft)
        {
            throw new InvalidDomainStateException($"Cannot add lines to a credit note in '{Status}' status.");
        }

        line.CreditNoteId = Id;
        Lines.Add(line);
        TotalAmount = decimal.Round(Lines.Sum(l => l.TotalAmount), 4, MidpointRounding.AwayFromZero);
        UnallocatedAmount = TotalAmount - AllocatedAmount;
    }

    public void Issue()
    {
        if (Status != CreditNoteStatus.Draft)
        {
            throw new InvalidDomainStateException($"Cannot issue a credit note in '{Status}' status.");
        }

        if (Lines.Count == 0 || TotalAmount <= 0)
        {
            throw new InvalidDomainStateException("Credit note must contain at least one line with positive amount.");
        }

        Status = CreditNoteStatus.Issued;
        UnallocatedAmount = TotalAmount - AllocatedAmount;
    }

    public void Apply(decimal amount)
    {
        if (Status != CreditNoteStatus.Issued && Status != CreditNoteStatus.Applied)
        {
            throw new InvalidDomainStateException($"Cannot apply credit note in '{Status}' status.");
        }

        if (amount <= 0)
        {
            throw new ValidationException(nameof(amount), "Application amount must be positive.");
        }

        if (amount > UnallocatedAmount)
        {
            throw new InvalidDomainStateException($"Amount ({amount}) exceeds credit note unallocated balance ({UnallocatedAmount}).");
        }

        AllocatedAmount = decimal.Round(AllocatedAmount + amount, 4, MidpointRounding.AwayFromZero);
        UnallocatedAmount = decimal.Round(TotalAmount - AllocatedAmount, 4, MidpointRounding.AwayFromZero);

        if (UnallocatedAmount == 0m)
        {
            Status = CreditNoteStatus.Applied;
        }
    }

    public void Void(string reason)
    {
        if (AllocatedAmount > 0)
        {
            throw new InvalidDomainStateException("Cannot void a credit note that has already been applied. Allocations must be reversed first.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Void reason is required.");
        }

        Status = CreditNoteStatus.Voided;
        VoidReason = reason.Trim();
    }
}

