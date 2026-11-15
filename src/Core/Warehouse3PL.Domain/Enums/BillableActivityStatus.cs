namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of a recorded commercial billable activity.
/// DB-SPEC-3PL-2026 Phase 5.0 Billing Foundation.
/// </summary>
public enum BillableActivityStatus
{
    /// <summary>Activity captured from operational event; pending rating with contract rate card.</summary>
    PendingRating = 1,

    /// <summary>Activity priced and rated; ready for inclusion in next billing cycle run.</summary>
    Rated = 2,

    /// <summary>Activity billed and locked on an approved/issued commercial invoice.</summary>
    Invoiced = 3,

    /// <summary>Operational activity was voided, shorted, or cancelled; excluded from billing.</summary>
    Cancelled = 4,

    /// <summary>Commercial waiver or courtesy credit applied; rated at zero charge with audit record.</summary>
    Waived = 5
}

