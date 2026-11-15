namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of a commercial 3PL billing invoice.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>
    /// Initial generated state during billing runs; open for accounting review and adjustment.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// Reviewed and approved by commercial/billing controller; ready for client distribution.
    /// </summary>
    Approved = 2,

    /// <summary>
    /// Formally dispatched/issued to the client; immutable financial document.
    /// </summary>
    Issued = 3,

    /// <summary>
    /// In full settlement; outstanding balance is zero.
    /// </summary>
    Paid = 4,

    /// <summary>
    /// Partial payment or credit allocation applied; outstanding balance remains greater than zero.
    /// </summary>
    PartiallyPaid = 5,

    /// <summary>
    /// Customer or finance team has raised an active dispute against one or more lines.
    /// </summary>
    Disputed = 6,

    /// <summary>
    /// Cancelled or voided prior to payment, or after complete payment reversal.
    /// </summary>
    Voided = 7
}

