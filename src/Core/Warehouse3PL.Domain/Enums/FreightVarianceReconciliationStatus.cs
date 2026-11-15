namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of freight spend variance reconciliation between quoted, estimated, billed, and reconciled carrier charges.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public enum FreightVarianceReconciliationStatus
{
    /// <summary>
    /// Initial baseline: quoted shipping rate captured at order routing or rate shop time.
    /// </summary>
    QuotedOnly = 1,

    /// <summary>
    /// Carrier estimated charge captured from initial dispatch or pickup manifest rate.
    /// </summary>
    Estimated = 2,

    /// <summary>
    /// Authoritative carrier invoice received and billed charges captured.
    /// </summary>
    Billed = 3,

    /// <summary>
    /// Charges reconciled and matched against quoted baseline; accessorial breakdown confirmed.
    /// </summary>
    Reconciled = 4,

    /// <summary>
    /// Discrepancy identified exceeding tolerance; routed to claims/freight audit dispute workflow.
    /// </summary>
    Disputed = 5
}
