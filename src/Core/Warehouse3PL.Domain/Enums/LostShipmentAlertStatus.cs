namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of an advisory lost-in-transit (LIT) threshold alert.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public enum LostShipmentAlertStatus
{
    /// <summary>
    /// Alert active: shipment has exceeded tracking scan or transit commitment thresholds without milestone progression.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Alert resolved: shipment resumed movement and was successfully delivered.
    /// </summary>
    ResolvedDelivered = 2,

    /// <summary>
    /// Advisory claim initiated: carrier lost parcel claim filed through authorized claims workflow.
    /// </summary>
    ClaimInitiated = 3,

    /// <summary>
    /// Alert dismissed: exception manually acknowledged by logistics coordinator with verified cause.
    /// </summary>
    Dismissed = 4
}
