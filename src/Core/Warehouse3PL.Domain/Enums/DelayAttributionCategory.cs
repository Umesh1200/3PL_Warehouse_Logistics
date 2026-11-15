namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational responsibility classification for carrier delivery exceptions and transit delays.
/// Distinguishes carrier-controlled delays from external, consignee, warehouse, or regulatory holds.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public enum DelayAttributionCategory
{
    /// <summary>
    /// Delay caused by carrier network issues (e.g. missorts, equipment breakdown, hub backlogs, lost package, transit damage).
    /// </summary>
    CarrierFault = 1,

    /// <summary>
    /// Delay caused by natural disasters, severe weather, road closures, or external force majeure disruptions.
    /// </summary>
    ExternalDisruption = 2,

    /// <summary>
    /// Delay caused by consignee actions, incorrect/incomplete address, recipient unavailable, business closed, or customer-directed hold.
    /// </summary>
    CustomerOrRecipient = 3,

    /// <summary>
    /// Delay caused by warehouse fulfillment delays, late dock tender, or dispatch after carrier daily cutoff time.
    /// </summary>
    FacilityOrigin = 4,

    /// <summary>
    /// Delay caused by international border customs holds, quarantine inspections, or regulatory queries.
    /// </summary>
    RegulatoryOrCustoms = 5,

    /// <summary>
    /// Insufficient evidence or ambiguous carrier scan codes; responsibility unassigned.
    /// </summary>
    Unknown = 6
}
