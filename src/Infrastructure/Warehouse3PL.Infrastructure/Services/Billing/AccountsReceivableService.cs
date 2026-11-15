using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Service managing the Accounts Receivable ledger:
/// payment recording, multi-invoice allocations, payment reversals, and customer statements with aging buckets.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public class AccountsReceivableService : IAccountsReceivableService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<AccountsReceivableService> _logger;

    public AccountsReceivableService(
        IApplicationDbContext dbContext,
        ILogger<AccountsReceivableService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CustomerPaymentDto> RecordPaymentAsync(
        RecordCustomerPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = await _dbContext.Clients
            .FirstOrDefaultAsync(c => c.Id == request.ClientId, cancellationToken);

        if (client == null)
        {
            throw new EntityNotFoundException(nameof(Client), request.ClientId);
        }

        var payment = CustomerPayment.Create(
            clientId: request.ClientId,
            paymentReference: request.PaymentReference,
            totalAmount: request.TotalAmount,
            method: request.PaymentMethod,
            paymentDate: request.PaymentDate ?? DateTime.UtcNow.Date,
            currency: request.Currency,
            notes: request.Notes);

        await _dbContext.CustomerPayments.AddAsync(payment, cancellationToken);

        if (request.AutoAllocate)
        {
            // Auto-allocate against open invoices chronologically (FIFO by DueDate)
            var openInvoices = await _dbContext.Invoices
                .Where(i => i.ClientId == request.ClientId &&
                            i.Currency == payment.Currency &&
                            i.OutstandingBalance > 0 &&
                            (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid || i.Status == InvoiceStatus.Disputed))
                .OrderBy(i => i.DueDate)
                .ThenBy(i => i.InvoiceDate)
                .ToListAsync(cancellationToken);

            foreach (var inv in openInvoices)
            {
                if (payment.UnallocatedAmount <= 0)
                {
                    break;
                }

                var allocAmount = decimal.Min(payment.UnallocatedAmount, inv.OutstandingBalance);
                if (allocAmount > 0)
                {
                    payment.Allocate(inv.Id, allocAmount, "Auto-allocated FIFO");
                    inv.ApplyPayment(allocAmount);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Customer payment '{PaymentReference}' recorded for Client {ClientId}. Amount: {Amount} {Currency}, Unallocated: {Unallocated}",
            payment.PaymentReference, payment.ClientId, payment.TotalAmount, payment.Currency, payment.UnallocatedAmount);

        return MapToPaymentDto(payment);
    }

    public async Task<PaymentAllocationDto> AllocatePaymentAsync(
        Guid paymentId,
        Guid invoiceId,
        decimal amount,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var payment = await _dbContext.CustomerPayments
            .Include(p => p.Allocations)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);

        if (payment == null)
        {
            throw new EntityNotFoundException(nameof(CustomerPayment), paymentId);
        }

        if (payment.IsReversed)
        {
            throw new InvalidDomainStateException("Cannot allocate funds from a reversed payment.");
        }

        var invoice = await _dbContext.Invoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

        if (invoice == null)
        {
            throw new EntityNotFoundException(nameof(Invoice), invoiceId);
        }

        if (!string.Equals(payment.Currency, invoice.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                nameof(amount),
                $"Currency mismatch: Payment currency '{payment.Currency}' does not match invoice currency '{invoice.Currency}'.");
        }

        if (amount > payment.UnallocatedAmount)
        {
            throw new InvalidDomainStateException(
                $"Allocation amount ({amount}) exceeds payment unallocated balance ({payment.UnallocatedAmount}).");
        }

        if (amount > invoice.OutstandingBalance)
        {
            throw new InvalidDomainStateException(
                $"Allocation amount ({amount}) exceeds invoice remaining outstanding balance ({invoice.OutstandingBalance}).");
        }

        var allocation = payment.Allocate(invoice.Id, amount, notes);
        invoice.ApplyPayment(amount);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payment '{PaymentRef}' allocated {Amount} to Invoice '{InvoiceNumber}'. Invoice balance: {Balance}",
            payment.PaymentReference, amount, invoice.InvoiceNumber, invoice.OutstandingBalance);

        return new PaymentAllocationDto(
            Id: allocation.Id,
            CustomerPaymentId: allocation.CustomerPaymentId,
            InvoiceId: allocation.InvoiceId,
            AllocatedAmount: allocation.AllocatedAmount,
            AllocatedAtUtc: allocation.AllocatedAtUtc,
            IsReversed: allocation.IsReversed);
    }

    public async Task<CustomerPaymentDto> ReversePaymentAsync(
        Guid paymentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var payment = await _dbContext.CustomerPayments
            .Include(p => p.Allocations)
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);

        if (payment == null)
        {
            throw new EntityNotFoundException(nameof(CustomerPayment), paymentId);
        }

        if (payment.IsReversed)
        {
            throw new InvalidDomainStateException("Customer payment is already reversed.");
        }

        // Retrieve affected invoices
        var invoiceIds = payment.Allocations
            .Where(a => !a.IsReversed)
            .Select(a => a.InvoiceId)
            .Distinct()
            .ToList();

        var invoices = await _dbContext.Invoices
            .Where(i => invoiceIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        foreach (var alloc in payment.Allocations.Where(a => !a.IsReversed))
        {
            var inv = invoices.FirstOrDefault(i => i.Id == alloc.InvoiceId);
            if (inv != null)
            {
                inv.ReversePayment(alloc.AllocatedAmount);
            }
        }

        payment.Reverse(reason);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Payment '{PaymentReference}' reversed. Reason: {Reason}", payment.PaymentReference, reason);
        return MapToPaymentDto(payment);
    }

    public async Task<CustomerStatementDto> GetCustomerStatementAsync(
        Guid clientId,
        DateTime? asOfDate = null,
        string currency = "USD",
        CancellationToken cancellationToken = default)
    {
        var client = await _dbContext.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client == null)
        {
            throw new EntityNotFoundException(nameof(Client), clientId);
        }

        var cutoffDate = asOfDate ?? DateTime.UtcNow;
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();

        // 1. Invoices
        var invoices = await _dbContext.Invoices
            .Where(i => i.ClientId == clientId &&
                        i.Currency == normalizedCurrency &&
                        i.InvoiceDate <= cutoffDate &&
                        i.Status != InvoiceStatus.Voided &&
                        i.Status != InvoiceStatus.Draft)
            .OrderBy(i => i.InvoiceDate)
            .ToListAsync(cancellationToken);

        // 2. Payments
        var payments = await _dbContext.CustomerPayments
            .Include(p => p.Allocations)
            .Where(p => p.ClientId == clientId &&
                        p.Currency == normalizedCurrency &&
                        p.PaymentDate <= cutoffDate &&
                        !p.IsReversed)
            .OrderBy(p => p.PaymentDate)
            .ToListAsync(cancellationToken);

        // 3. Credit Notes
        var creditNotes = await _dbContext.CreditNotes
            .Where(c => c.ClientId == clientId &&
                        c.Currency == normalizedCurrency &&
                        c.IssueDate <= cutoffDate &&
                        c.Status != CreditNoteStatus.Voided &&
                        c.Status != CreditNoteStatus.Draft)
            .OrderBy(c => c.IssueDate)
            .ToListAsync(cancellationToken);

        var totalInvoiced = invoices.Sum(i => i.TotalAmount);
        var totalPaid = invoices.Sum(i => i.PaidAmount);
        var totalCredited = invoices.Sum(i => i.CreditedAmount);
        var totalOutstanding = invoices.Sum(i => i.OutstandingBalance);

        // Calculate Aging Buckets
        decimal currentBucket = 0m;
        decimal days1To30Bucket = 0m;
        decimal days31To60Bucket = 0m;
        decimal days61To90Bucket = 0m;
        decimal daysOver90Bucket = 0m;

        foreach (var inv in invoices.Where(i => i.OutstandingBalance > 0))
        {
            var daysOverdue = (cutoffDate.Date - inv.DueDate.Date).Days;
            if (daysOverdue <= 0)
            {
                currentBucket += inv.OutstandingBalance;
            }
            else if (daysOverdue <= 30)
            {
                days1To30Bucket += inv.OutstandingBalance;
            }
            else if (daysOverdue <= 60)
            {
                days31To60Bucket += inv.OutstandingBalance;
            }
            else if (daysOverdue <= 90)
            {
                days61To90Bucket += inv.OutstandingBalance;
            }
            else
            {
                daysOver90Bucket += inv.OutstandingBalance;
            }
        }

        var aging = new CustomerStatementAgingDto(
            Current: decimal.Round(currentBucket, 4, MidpointRounding.AwayFromZero),
            Days1To30: decimal.Round(days1To30Bucket, 4, MidpointRounding.AwayFromZero),
            Days31To60: decimal.Round(days31To60Bucket, 4, MidpointRounding.AwayFromZero),
            Days61To90: decimal.Round(days61To90Bucket, 4, MidpointRounding.AwayFromZero),
            DaysOver90: decimal.Round(daysOver90Bucket, 4, MidpointRounding.AwayFromZero),
            TotalOutstanding: decimal.Round(totalOutstanding, 4, MidpointRounding.AwayFromZero));

        // Assemble transactions
        var transactions = new List<CustomerStatementTransactionDto>();

        foreach (var inv in invoices)
        {
            transactions.Add(new CustomerStatementTransactionDto(
                TransactionDate: inv.InvoiceDate,
                TransactionType: "Invoice",
                ReferenceNumber: inv.InvoiceNumber,
                Description: $"Invoice {inv.InvoiceNumber} (Due: {inv.DueDate:yyyy-MM-dd})",
                Amount: inv.TotalAmount,
                PaidOrApplied: inv.PaidAmount + inv.CreditedAmount,
                BalanceRemaining: inv.OutstandingBalance));
        }

        foreach (var p in payments)
        {
            transactions.Add(new CustomerStatementTransactionDto(
                TransactionDate: p.PaymentDate,
                TransactionType: "Payment",
                ReferenceNumber: p.PaymentReference,
                Description: $"Payment {p.PaymentMethod} ({p.PaymentReference})",
                Amount: -p.TotalAmount,
                PaidOrApplied: p.AllocatedAmount,
                BalanceRemaining: p.UnallocatedAmount));
        }

        foreach (var c in creditNotes)
        {
            transactions.Add(new CustomerStatementTransactionDto(
                TransactionDate: c.IssueDate,
                TransactionType: "CreditNote",
                ReferenceNumber: c.CreditNoteNumber,
                Description: $"Credit Note {c.CreditNoteNumber} ({c.Reason})",
                Amount: -c.TotalAmount,
                PaidOrApplied: c.AllocatedAmount,
                BalanceRemaining: c.UnallocatedAmount));
        }

        transactions = transactions.OrderBy(t => t.TransactionDate).ToList();

        var totalUnallocatedPayments = payments.Sum(p => p.UnallocatedAmount);
        var totalUnallocatedCreditNotes = creditNotes.Sum(c => c.UnallocatedAmount);
        var totalUnallocatedCredits = totalUnallocatedPayments + totalUnallocatedCreditNotes;
        var netCustomerBalance = totalOutstanding - totalUnallocatedCredits;

        return new CustomerStatementDto(
            ClientId: client.Id,
            ClientAccountCode: client.AccountCode,
            ClientCompanyName: client.CompanyName,
            AsOfDateUtc: cutoffDate,
            Currency: normalizedCurrency,
            TotalInvoiced: decimal.Round(totalInvoiced, 4, MidpointRounding.AwayFromZero),
            TotalPaid: decimal.Round(totalPaid, 4, MidpointRounding.AwayFromZero),
            TotalCredited: decimal.Round(totalCredited, 4, MidpointRounding.AwayFromZero),
            TotalOutstandingBalance: decimal.Round(totalOutstanding, 4, MidpointRounding.AwayFromZero),
            Aging: aging,
            Transactions: transactions,
            TotalUnallocatedCredits: decimal.Round(totalUnallocatedCredits, 4, MidpointRounding.AwayFromZero),
            NetCustomerBalance: decimal.Round(netCustomerBalance, 4, MidpointRounding.AwayFromZero));
    }

    private static CustomerPaymentDto MapToPaymentDto(CustomerPayment p)
    {
        return new CustomerPaymentDto(
            Id: p.Id,
            ClientId: p.ClientId,
            PaymentReference: p.PaymentReference,
            PaymentDate: p.PaymentDate,
            PaymentMethod: p.PaymentMethod,
            Currency: p.Currency,
            TotalAmount: p.TotalAmount,
            AllocatedAmount: p.AllocatedAmount,
            UnallocatedAmount: p.UnallocatedAmount,
            IsReversed: p.IsReversed,
            ReversalReason: p.ReversalReason,
            Allocations: p.Allocations.Select(a => new PaymentAllocationDto(
                Id: a.Id,
                CustomerPaymentId: a.CustomerPaymentId,
                InvoiceId: a.InvoiceId,
                AllocatedAmount: a.AllocatedAmount,
                AllocatedAtUtc: a.AllocatedAtUtc,
                IsReversed: a.IsReversed)).ToList());
    }
}
