namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Standardized canonical classification taxonomy for carrier delivery exceptions and disruptions.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public enum CanonicalDeliveryExceptionCategory
{
    /// <summary>
    /// Disruptions caused by severe weather events, natural disasters, or environmental blockages.
    /// </summary>
    WeatherDisruption = 1,

    /// <summary>
    /// Incomplete, incorrect, or unlocatable delivery address.
    /// </summary>
    AddressIssue = 2,

    /// <summary>
    /// Recipient was not present, business closed, or required signature unavailable.
    /// </summary>
    RecipientUnavailable = 3,

    /// <summary>
    /// Package or merchandise was crushed, leaking, or structurally damaged in carrier transit.
    /// </summary>
    DamagedInTransit = 4,

    /// <summary>
    /// Carrier hub delay, mechanical failure, missed sort, or operational backlog.
    /// </summary>
    CarrierOperationalDelay = 5,

    /// <summary>
    /// Government customs hold, inspection delay, or regulatory import/export quarantine.
    /// </summary>
    CustomsOrRegulatoryHold = 6,

    /// <summary>
    /// Package is missing from carrier manifest, tracking silent, or declared lost in transit.
    /// </summary>
    LostOrMissing = 7,

    /// <summary>
    /// Other unclassified or generic carrier exception description.
    /// </summary>
    Other = 8
}
