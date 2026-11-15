namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational and analytical lifecycle state of a transportation freight charge.
/// Governs carrier invoice auditing, discrepancy management, and settlement.
/// DB-SPEC-3PL-2026 Phase 6.4.4 Freight Cost Variance & Accessorial Analytics.
/// </summary>
public enum FreightReconciliationStatus
{
    /// <summary>
    /// Initial rate quote calculated during order routing or label generation.
    /// </summary>
    Estimated = 1,

    /// <summary>
    /// Confirmed charge billed on an authoritative carrier electronic invoice statement.
    /// </summary>
    Invoiced = 2,

    /// <summary>
    /// Mathematically audited, compliant with contractual rate cards, and approved for settlement.
    /// </summary>
    Reconciled = 3,

    /// <summary>
    /// Discrepancy, rate overcharge, or unauthorized accessorial surcharge filed with carrier.
    /// Quarantined from reconciled cost totals.
    /// </summary>
    Disputed = 4,

    /// <summary>
    /// Disputed carrier charge rejected by carrier and approved as unrecoverable operational loss.
    /// </summary>
    WrittenOff = 5
}
