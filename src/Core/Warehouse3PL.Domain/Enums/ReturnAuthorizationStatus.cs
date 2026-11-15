namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational lifecycle status of a Return-To-Sender (RTS) operational authorization.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4: Return-to-Sender Authorization, Return Shipment &amp; Tracking.
/// </summary>
public enum ReturnAuthorizationStatus
{
    /// <summary>Return authorized by staff/client; awaiting reverse shipment creation and carrier booking.</summary>
    Authorized = 1,

    /// <summary>Reverse shipment created and linked to carrier return tracking number.</summary>
    ShipmentCreated = 2,

    /// <summary>Carrier has accepted the return parcel into reverse transit custody.</summary>
    InTransit = 3,

    /// <summary>Carrier delivered return parcel to warehouse receiving dock.</summary>
    DeliveredAtFacility = 4,

    /// <summary>Downstream warehouse receiving handoff created and processed.</summary>
    Completed = 5,

    /// <summary>Return authorization cancelled by staff prior to carrier handover.</summary>
    Cancelled = 6,

    /// <summary>Return authorization expired due to inactivity or elapsed authorization horizon.</summary>
    Expired = 7
}

