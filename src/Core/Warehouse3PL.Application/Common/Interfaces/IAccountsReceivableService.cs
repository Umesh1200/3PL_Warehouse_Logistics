using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public record RecordCustomerPaymentRequest(
    Guid ClientId,
    string PaymentReference,
    decimal TotalAmount,
    PaymentMethod PaymentMethod,
    DateTime? PaymentDate = null,
    string Currency = "USD",
    string? Notes = null,
    bool AutoAllocate = false);

public record PaymentAllocationDto(
    Guid Id,
    Guid CustomerPaymentId,
    Guid InvoiceId,
    decimal AllocatedAmount,
    DateTime AllocatedAtUtc,
    bool IsReversed);

public record CustomerPaymentDto(
    Guid Id,
    Guid ClientId,
    string PaymentReference,
    DateTime PaymentDate,
    PaymentMethod PaymentMethod,
    string Currency,
    decimal TotalAmount,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    bool IsReversed,
    string? ReversalReason,
    IReadOnlyList<PaymentAllocationDto> Allocations);

public record CustomerStatementAgingDto(
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal DaysOver90,
    decimal TotalOutstanding);

public record CustomerStatementTransactionDto(
    DateTime TransactionDate,
    string TransactionType,
    string ReferenceNumber,
    string Description,
    decimal Amount,
    decimal PaidOrApplied,
    decimal BalanceRemaining);

public record CustomerStatementDto(
    Guid ClientId,
    string ClientAccountCode,
    string ClientCompanyName,
    DateTime AsOfDateUtc,
    string Currency,
    decimal TotalInvoiced,
    decimal TotalPaid,
    decimal TotalCredited,
    decimal TotalOutstandingBalance,
    CustomerStatementAgingDto Aging,
    IReadOnlyList<CustomerStatementTransactionDto> Transactions,
    decimal TotalUnallocatedCredits = 0m,
    decimal NetCustomerBalance = 0m);

/// <summary>
/// Service managing the Accounts Receivable ledger:
/// payment recording, multi-invoice allocations, payment reversals, and customer statements with aging buckets.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public interface IAccountsReceivableService
{
    Task<CustomerPaymentDto> RecordPaymentAsync(RecordCustomerPaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentAllocationDto> AllocatePaymentAsync(Guid paymentId, Guid invoiceId, decimal amount, string? notes = null, CancellationToken cancellationToken = default);
    Task<CustomerPaymentDto> ReversePaymentAsync(Guid paymentId, string reason, CancellationToken cancellationToken = default);
    Task<CustomerStatementDto> GetCustomerStatementAsync(Guid clientId, DateTime? asOfDate = null, string currency = "USD", CancellationToken cancellationToken = default);
}

