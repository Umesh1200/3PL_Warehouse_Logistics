namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational and accounting status of a carrier transportation cost record.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public enum CarrierCostStatus
{
    /// <summary>
    /// Initial carrier rate quote obtained during label generation.
    /// </summary>
    Estimated = 1,

    /// <summary>
    /// Confirmed carrier shipping cost following physical carrier custody handover.
    /// </summary>
    ActualConfirmed = 2,

    /// <summary>
    /// Invoiced by carrier on periodic carrier billing statement.
    /// </summary>
    Invoiced = 3,

    /// <summary>
    /// Matched and mathematically reconciled with carrier invoice.
    /// </summary>
    Reconciled = 4,

    /// <summary>
    /// Carrier invoice disputed due to billing, weight, or service discrepancies.
    /// </summary>
    Disputed = 5
}

