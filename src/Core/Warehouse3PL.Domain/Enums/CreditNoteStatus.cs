namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of a commercial credit note.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public enum CreditNoteStatus
{
    /// <summary>
    /// Initial drafting state prior to financial authorization.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// Formally authorized and issued to the client.
    /// </summary>
    Issued = 2,

    /// <summary>
    /// Fully applied against an invoice or AR balance.
    /// </summary>
    Applied = 3,

    /// <summary>
    /// Cancelled or voided.
    /// </summary>
    Voided = 4
}

