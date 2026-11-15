using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Domain aggregate root for a commercial client invoice.
/// Unifies storage, handling, freight, and value-added charges into an authoritative, auditable financial document.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class Invoice : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid? FacilityId { get; set; }
    public Guid BillingPeriodId { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public int PaymentTermsDays { get; set; } = 30;
    public string Currency { get; set; } = "USD";

    // Financial Totals
    public decimal SubtotalAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

    // Settlement & Accounts Receivable Tracking
    public decimal PaidAmount { get; set; }
    public decimal CreditedAmount { get; set; }
    public decimal OutstandingBalance { get; set; }

    // Lifecycle Auditing
    public DateTime? ApprovedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public Guid? IssuedByUserId { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
    public Guid? ReplacementInvoiceId { get; set; }

    public string? Notes { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility? Facility { get; set; }
    public BillingPeriod BillingPeriod { get; set; } = null!;
    public Invoice? ReplacementInvoice { get; set; }
    public ICollection<InvoiceLine> Lines { get; set; } = [];
    public ICollection<PaymentAllocation> PaymentAllocations { get; set; } = [];
    public ICollection<InvoiceDispute> Disputes { get; set; } = [];
    public ICollection<CreditNote> CreditNotes { get; set; } = [];

    public Invoice()
    {
        Id = Guid.NewGuid();
    }

    public static Invoice Create(
        Guid clientId,
        Guid billingPeriodId,
        string invoiceNumber,
        DateTime invoiceDate,
        int paymentTermsDays = 30,
        string currency = "USD",
        Guid? facilityId = null,
        decimal taxRate = 0m,
        string? notes = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client tenant ID is required.");
        }

        if (billingPeriodId == Guid.Empty)
        {
            throw new ValidationException(nameof(billingPeriodId), "Billing period ID is required.");
        }

        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            throw new ValidationException(nameof(invoiceNumber), "Invoice number is required.");
        }

        if (paymentTermsDays < 0)
        {
            throw new ValidationException(nameof(paymentTermsDays), "Payment terms days cannot be negative.");
        }

        if (taxRate < 0)
        {
            throw new ValidationException(nameof(taxRate), "Tax rate cannot be negative.");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3)
        {
            throw new ValidationException(nameof(currency), "Currency code must be a 3-character ISO 4217 code.");
        }

        var normalizedInvoiceDate = invoiceDate == default ? DateTime.UtcNow.Date : invoiceDate.Date;
        var dueDate = normalizedInvoiceDate.AddDays(paymentTermsDays);

        return new Invoice
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            BillingPeriodId = billingPeriodId,
            InvoiceNumber = invoiceNumber.Trim().ToUpperInvariant(),
            Status = InvoiceStatus.Draft,
            InvoiceDate = normalizedInvoiceDate,
            DueDate = dueDate,
            PaymentTermsDays = paymentTermsDays,
            Currency = normalizedCurrency,
            TaxRate = decimal.Round(taxRate, 4, MidpointRounding.AwayFromZero),
            Notes = notes?.Trim()
        };
    }

    /// <summary>
    /// Adds a line item to the draft invoice and recalculates financial totals.
    /// </summary>
    public void AddLine(InvoiceLine line)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidDomainStateException($"Cannot add lines to an invoice in '{Status}' status. Lines may only be added in Draft status.");
        }

        line.InvoiceId = Id;
        Lines.Add(line);
        CalculateTotals();
    }

    /// <summary>
    /// Recalculates subtotal, tax, total, and outstanding balance based on current lines and allocations.
    /// Outstanding Balance = Total Amount - Paid Amount - Credited Amount.
    /// </summary>
    public void CalculateTotals()
    {
        SubtotalAmount = decimal.Round(Lines.Sum(l => l.TotalAmount), 4, MidpointRounding.AwayFromZero);
        TaxAmount = decimal.Round(SubtotalAmount * TaxRate, 4, MidpointRounding.AwayFromZero);
        TotalAmount = SubtotalAmount + TaxAmount;

        var netSettled = PaidAmount + CreditedAmount;
        OutstandingBalance = decimal.Max(0m, TotalAmount - netSettled);
    }

    /// <summary>
    /// Approves the draft invoice for distribution.
    /// </summary>
    public void Approve(Guid approvedByUserId, DateTime? approvedAtUtc = null)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidDomainStateException($"Cannot approve an invoice in '{Status}' status. Only Draft invoices can be approved.");
        }

        if (Lines.Count == 0)
        {
            throw new InvalidDomainStateException("Cannot approve an invoice with zero lines.");
        }

        CalculateTotals();

        Status = InvoiceStatus.Approved;
        ApprovedAtUtc = approvedAtUtc ?? DateTime.UtcNow;
        ApprovedByUserId = approvedByUserId;
    }

    /// <summary>
    /// Issues the approved invoice, establishing it as an immutable commercial and financial obligation.
    /// </summary>
    public void Issue(Guid issuedByUserId, DateTime? issuedAtUtc = null)
    {
        if (Status != InvoiceStatus.Approved)
        {
            throw new InvalidDomainStateException($"Cannot issue an invoice in '{Status}' status. Invoice must be Approved before issuance.");
        }

        Status = InvoiceStatus.Issued;
        IssuedAtUtc = issuedAtUtc ?? DateTime.UtcNow;
        IssuedByUserId = issuedByUserId;
    }

    /// <summary>
    /// Applies a customer cash/transfer payment allocation to the invoice.
    /// </summary>
    public void ApplyPayment(decimal amount)
    {
        if (Status != InvoiceStatus.Issued && Status != InvoiceStatus.PartiallyPaid && Status != InvoiceStatus.Disputed)
        {
            throw new InvalidDomainStateException($"Cannot apply payment to an invoice in '{Status}' status. Invoice must be Issued, PartiallyPaid, or Disputed.");
        }

        if (amount <= 0)
        {
            throw new ValidationException(nameof(amount), "Payment allocation amount must be strictly positive.");
        }

        if (amount > OutstandingBalance)
        {
            throw new InvalidDomainStateException($"Payment amount ({amount}) exceeds remaining invoice outstanding balance ({OutstandingBalance}).");
        }

        PaidAmount = decimal.Round(PaidAmount + amount, 4, MidpointRounding.AwayFromZero);
        CalculateTotals();

        if (OutstandingBalance == 0m)
        {
            Status = InvoiceStatus.Paid;
        }
        else
        {
            Status = InvoiceStatus.PartiallyPaid;
        }
    }

    /// <summary>
    /// Reverses a previously allocated payment (e.g. NSF check, bank chargeback, correction).
    /// </summary>
    public void ReversePayment(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ValidationException(nameof(amount), "Payment reversal amount must be strictly positive.");
        }

        if (amount > PaidAmount)
        {
            throw new InvalidDomainStateException($"Reversal amount ({amount}) exceeds cumulative paid amount ({PaidAmount}).");
        }

        PaidAmount = decimal.Round(PaidAmount - amount, 4, MidpointRounding.AwayFromZero);
        CalculateTotals();

        if (OutstandingBalance == 0m)
        {
            Status = InvoiceStatus.Paid;
        }
        else if (PaidAmount > 0 || CreditedAmount > 0)
        {
            Status = InvoiceStatus.PartiallyPaid;
        }
        else
        {
            Status = InvoiceStatus.Issued;
        }
    }

    /// <summary>
    /// Applies a formal commercial credit note allocation to the invoice.
    /// </summary>
    public void ApplyCredit(decimal amount)
    {
        if (Status != InvoiceStatus.Issued && Status != InvoiceStatus.PartiallyPaid && Status != InvoiceStatus.Disputed)
        {
            throw new InvalidDomainStateException($"Cannot apply credit to an invoice in '{Status}' status.");
        }

        if (amount <= 0)
        {
            throw new ValidationException(nameof(amount), "Credit allocation amount must be positive.");
        }

        if (amount > OutstandingBalance)
        {
            throw new InvalidDomainStateException($"Credit amount ({amount}) exceeds remaining outstanding balance ({OutstandingBalance}).");
        }

        CreditedAmount = decimal.Round(CreditedAmount + amount, 4, MidpointRounding.AwayFromZero);
        CalculateTotals();

        if (OutstandingBalance == 0m)
        {
            Status = InvoiceStatus.Paid;
        }
        else
        {
            Status = InvoiceStatus.PartiallyPaid;
        }
    }

    /// <summary>
    /// Reverses an applied credit allocation.
    /// </summary>
    public void ReverseCredit(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ValidationException(nameof(amount), "Credit reversal amount must be positive.");
        }

        if (amount > CreditedAmount)
        {
            throw new InvalidDomainStateException($"Reversal credit amount ({amount}) exceeds cumulative credited amount ({CreditedAmount}).");
        }

        CreditedAmount = decimal.Round(CreditedAmount - amount, 4, MidpointRounding.AwayFromZero);
        CalculateTotals();

        if (OutstandingBalance == 0m)
        {
            Status = InvoiceStatus.Paid;
        }
        else if (PaidAmount > 0 || CreditedAmount > 0)
        {
            Status = InvoiceStatus.PartiallyPaid;
        }
        else
        {
            Status = InvoiceStatus.Issued;
        }
    }

    /// <summary>
    /// Marks the invoice as disputed by customer or billing operations.
    /// </summary>
    public void MarkDisputed(string reason)
    {
        if (Status == InvoiceStatus.Draft || Status == InvoiceStatus.Voided)
        {
            throw new InvalidDomainStateException($"Cannot mark an invoice in '{Status}' status as disputed.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Dispute reason is required.");
        }

        Status = InvoiceStatus.Disputed;
    }

    /// <summary>
    /// Resolves active disputes and restores the invoice's lifecycle status based on settled balance.
    /// </summary>
    public void ResolveDispute()
    {
        if (Status != InvoiceStatus.Disputed)
        {
            return;
        }

        CalculateTotals();

        if (OutstandingBalance == 0m)
        {
            Status = InvoiceStatus.Paid;
        }
        else if (PaidAmount > 0 || CreditedAmount > 0)
        {
            Status = InvoiceStatus.PartiallyPaid;
        }
        else
        {
            Status = InvoiceStatus.Issued;
        }
    }

    /// <summary>
    /// Voids the invoice if no active payments remain allocated.
    /// Preserves original line items, void authorization, and optional link to replacement invoice.
    /// </summary>
    public void Void(Guid voidedByUserId, string reason, Guid? replacementInvoiceId = null)
    {
        if (Status == InvoiceStatus.Paid)
        {
            throw new InvalidDomainStateException("Cannot void an invoice that has been fully paid. Payments must be reversed first.");
        }

        if (PaidAmount > 0)
        {
            throw new InvalidDomainStateException("Cannot void an invoice with active payment allocations. Payments must be reversed first.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Void reason is required.");
        }

        Status = InvoiceStatus.Voided;
        VoidedAtUtc = DateTime.UtcNow;
        VoidedByUserId = voidedByUserId;
        VoidReason = reason.Trim();
        ReplacementInvoiceId = replacementInvoiceId;
    }
}

