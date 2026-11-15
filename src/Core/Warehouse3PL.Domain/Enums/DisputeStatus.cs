namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of an invoice dispute raised by a client or internal billing reviewer.
/// DB-SPEC-3PL-2026 Phase 5.5 Invoicing Engine &amp; AR Ledger.
/// </summary>
public enum DisputeStatus
{
    /// <summary>
    /// Dispute registered and awaiting review.
    /// </summary>
    Submitted = 1,

    /// <summary>
    /// Actively under commercial or operational review.
    /// </summary>
    UnderReview = 2,

    /// <summary>
    /// Dispute validated and accepted; credit note emission pending or authorized.
    /// </summary>
    Accepted = 3,

    /// <summary>
    /// Dispute investigated and rejected with documented justification.
    /// </summary>
    Rejected = 4,

    /// <summary>
    /// Dispute formally concluded and finalized (credit applied or balance restored).
    /// </summary>
    Resolved = 5
}

