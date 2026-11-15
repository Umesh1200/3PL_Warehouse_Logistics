using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Billing;

/// <summary>
/// Domain aggregate root for settled Accounts Receivable customer payments.
/// Tracks settlement reference, payment method, allocated amounts, and unallocated credits.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class CustomerPayment : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }

    public string PaymentReference { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string Currency { get; set; } = "USD";

    public decimal TotalAmount { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal UnallocatedAmount { get; set; }

    public bool IsReversed { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
    public string? ReversalReason { get; set; }

    public string? Notes { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public ICollection<PaymentAllocation> Allocations { get; set; } = [];

    public CustomerPayment()
    {
        Id = Guid.NewGuid();
    }

    public static CustomerPayment Create(
        Guid clientId,
        string paymentReference,
        decimal totalAmount,
        PaymentMethod method,
        DateTime paymentDate,
        string currency = "USD",
        string? notes = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException(nameof(clientId), "Client ID is required.");
        }

        if (string.IsNullOrWhiteSpace(paymentReference))
        {
            throw new ValidationException(nameof(paymentReference), "Payment reference (check/wire/trace number) is required.");
        }

        if (totalAmount <= 0)
        {
            throw new ValidationException(nameof(totalAmount), "Payment total amount must be strictly positive.");
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();

        return new CustomerPayment
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            PaymentReference = paymentReference.Trim(),
            TotalAmount = decimal.Round(totalAmount, 4, MidpointRounding.AwayFromZero),
            AllocatedAmount = 0m,
            UnallocatedAmount = decimal.Round(totalAmount, 4, MidpointRounding.AwayFromZero),
            PaymentMethod = method,
            PaymentDate = paymentDate == default ? DateTime.UtcNow.Date : paymentDate.Date,
            Currency = normalizedCurrency,
            IsReversed = false,
            Notes = notes?.Trim()
        };
    }

    public PaymentAllocation Allocate(Guid invoiceId, decimal amount, string? notes = null)
    {
        if (IsReversed)
        {
            throw new InvalidDomainStateException("Cannot allocate funds from a reversed payment.");
        }

        if (amount <= 0)
        {
            throw new ValidationException(nameof(amount), "Allocation amount must be positive.");
        }

        if (amount > UnallocatedAmount)
        {
            throw new InvalidDomainStateException($"Allocation amount ({amount}) exceeds available unallocated payment balance ({UnallocatedAmount}).");
        }

        var allocation = PaymentAllocation.Create(Id, invoiceId, amount, DateTime.UtcNow, notes);
        Allocations.Add(allocation);

        AllocatedAmount = decimal.Round(AllocatedAmount + amount, 4, MidpointRounding.AwayFromZero);
        UnallocatedAmount = decimal.Round(TotalAmount - AllocatedAmount, 4, MidpointRounding.AwayFromZero);

        return allocation;
    }

    public void Reverse(string reason)
    {
        if (IsReversed)
        {
            throw new InvalidDomainStateException("Payment is already reversed.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "Reversal reason is required.");
        }

        IsReversed = true;
        ReversedAtUtc = DateTime.UtcNow;
        ReversalReason = reason.Trim();

        foreach (var alloc in Allocations.Where(a => !a.IsReversed))
        {
            alloc.Reverse();
        }

        AllocatedAmount = 0m;
        UnallocatedAmount = 0m;
    }
}

