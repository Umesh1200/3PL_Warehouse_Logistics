namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of a 3PL client commercial rate card or schedule agreement.
/// DB-SPEC-3PL-2026 Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// </summary>
public enum RateCardStatus
{
    /// <summary>
    /// Agreement or schedule is being drafted and is not yet available for live rating.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// Agreement or schedule is actively effective and used by the rating engine.
    /// </summary>
    Active = 2,

    /// <summary>
    /// Replaced by a newer version or revised commercial schedule.
    /// </summary>
    Superceded = 3,

    /// <summary>
    /// Decommissioned or archived agreement, retained strictly for audit and historical invoice traceability.
    /// </summary>
    Archived = 4
}

