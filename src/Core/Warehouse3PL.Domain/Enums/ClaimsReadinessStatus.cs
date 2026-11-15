namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of evidentiary readiness for carrier freight loss, damage, or SLA breach claims.
/// Reflects documentation completeness; does not initiate legal loss or billing debits.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public enum ClaimsReadinessStatus
{
    /// <summary>
    /// Missing critical handover proof, tracking scans, or goods valuation.
    /// </summary>
    InsufficientEvidence = 1,

    /// <summary>
    /// Partial evidence gathered; awaiting scan timeout or invoice reconciliation.
    /// </summary>
    EvidenceCollecting = 2,

    /// <summary>
    /// All necessary evidence compiled and ready for human claims adjustor review.
    /// </summary>
    ReadyForReview = 3,

    /// <summary>
    /// Claim has been submitted to carrier network portal.
    /// </summary>
    Submitted = 4,

    /// <summary>
    /// Claim has been resolved with carrier (paid, denied, or withdrawn).
    /// </summary>
    Resolved = 5
}
