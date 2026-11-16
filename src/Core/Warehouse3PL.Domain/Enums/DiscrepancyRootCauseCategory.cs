namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Root-cause classification for carrier invoice discrepancies between expected and invoiced costs.
/// DB-SPEC-3PL-2026 Phase 6.4.4.2 Variance &amp; Accessorial Discrepancy Analytics.
/// </summary>
public enum DiscrepancyRootCauseCategory
{
    /// <summary>
    /// Carrier billed weight exceeds contractual expected billable weight (scale vs dimensional weight).
    /// </summary>
    DimensionalWeightDrift = 1,

    /// <summary>
    /// Carrier invoiced base freight rate exceeds contractual rate card table.
    /// </summary>
    BaseRateOvercharge = 2,

    /// <summary>
    /// Carrier assessed unexpected or uncontracted accessorial surcharges.
    /// </summary>
    UncontractedAccessorial = 3,

    /// <summary>
    /// Carrier duplicate invoice line or idempotency collision detected.
    /// </summary>
    DuplicateBilling = 4,

    /// <summary>
    /// Late-arriving surcharge or carrier post-delivery adjustment.
    /// </summary>
    LateAdjustment = 5,

    /// <summary>
    /// Unclassified carrier billing variance.
    /// </summary>
    Other = 99
}
