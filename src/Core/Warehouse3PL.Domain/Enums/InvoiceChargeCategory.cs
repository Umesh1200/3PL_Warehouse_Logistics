namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Top-level commercial charge categories presented on 3PL client invoices.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public enum InvoiceChargeCategory
{
    /// <summary>
    /// Recurring warehouse storage footprint (Pallet-day, Carton-day, CBM-day, minimum spend).
    /// </summary>
    Storage = 1,

    /// <summary>
    /// Physical inbound, outbound, and reverse logistics handling operations.
    /// </summary>
    Handling = 2,

    /// <summary>
    /// Carrier freight transportation, fuel surcharges, accessorials, and freight markups.
    /// </summary>
    Freight = 3,

    /// <summary>
    /// Value-added services (kitting, assembly, special labeling, re-bagging).
    /// </summary>
    Vas = 4,

    /// <summary>
    /// Manual accounting adjustments, credits, debits, dispute resolutions, and contractual allowances.
    /// </summary>
    Adjustment = 5
}

