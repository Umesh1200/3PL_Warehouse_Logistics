using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Allocation record associating a customer payment to a specific invoice.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class PaymentAllocation : AuditableEntity<Guid>
{
    public Guid CustomerPaymentId { get; set; }
    public Guid InvoiceId { get; set; }

    public decimal AllocatedAmount { get; set; }
    public DateTime AllocatedAtUtc { get; set; }
    public bool IsReversed { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
    public string? Notes { get; set; }

    // Navigations
    public CustomerPayment CustomerPayment { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;

    public PaymentAllocation()
    {
        Id = Guid.NewGuid();
    }

    public static PaymentAllocation Create(
        Guid customerPaymentId,
        Guid invoiceId,
        decimal amount,
        DateTime? allocatedAtUtc = null,
        string? notes = null)
    {
        if (customerPaymentId == Guid.Empty)
        {
            throw new ValidationException(nameof(customerPaymentId), "Customer payment ID is required.");
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ValidationException(nameof(invoiceId), "Invoice ID is required.");
        }

        if (amount <= 0)
        {
            throw new ValidationException(nameof(amount), "Allocated amount must be positive.");
        }

        return new PaymentAllocation
        {
            Id = Guid.NewGuid(),
            CustomerPaymentId = customerPaymentId,
            InvoiceId = invoiceId,
            AllocatedAmount = decimal.Round(amount, 4, MidpointRounding.AwayFromZero),
            AllocatedAtUtc = allocatedAtUtc ?? DateTime.UtcNow,
            IsReversed = false,
            Notes = notes?.Trim()
        };
    }

    public void Reverse()
    {
        if (IsReversed)
        {
            throw new InvalidDomainStateException("Payment allocation is already reversed.");
        }

        IsReversed = true;
        ReversedAtUtc = DateTime.UtcNow;
    }
}

