namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Settled payment methods recognized in the Accounts Receivable ledger.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public enum PaymentMethod
{
    WireTransfer = 1,
    Ach = 2,
    CreditCard = 3,
    Check = 4,
    ElectronicFundsTransfer = 5,
    Other = 99
}

