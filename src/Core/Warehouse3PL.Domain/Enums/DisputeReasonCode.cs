namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Root cause classification code for invoice disputes.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public enum DisputeReasonCode
{
    PricingError = 1,
    QuantityDiscrepancy = 2,
    DamagedGoods = 3,
    LateDelivery = 4,
    DuplicateCharge = 5,
    UnauthorizedAccessorial = 6,
    ServiceDispute = 7,
    Other = 99
}

