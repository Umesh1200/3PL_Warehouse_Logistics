namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational classification of carrier freight claims.
/// Distinguishes evidentiary prerequisites between lost parcels, damaged goods, and delivery delays.
/// DB-SPEC-3PL-2026 Phase 6.4.3 Carrier Scorecards & Exception Analytics.
/// </summary>
public enum ClaimType
{
    /// <summary>
    /// Parcel unlocated in carrier network; requires inactivity threshold breach and absence of POD.
    /// </summary>
    Lost = 1,

    /// <summary>
    /// Parcel delivered with physical damage or destruction; requires damage exception or POD damage notation.
    /// </summary>
    Damaged = 2,

    /// <summary>
    /// Guaranteed delivery service level commitment breached; requires delivery timestamp past EDD.
    /// </summary>
    Delayed = 3,

    /// <summary>
    /// Miscellaneous transit claims, shortages, or billing disputes.
    /// </summary>
    Other = 99
}
