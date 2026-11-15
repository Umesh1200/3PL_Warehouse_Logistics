namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Business and operational reasons for authorizing a Return-To-Sender (RTS) shipment.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public enum ReturnReason
{
    /// <summary>Consignee explicitly refused to accept delivery of the shipment.</summary>
    CustomerRefused = 1,

    /// <summary>Recipient address is undeliverable, nonexistent, or recipient has moved.</summary>
    UndeliverableAddress = 2,

    /// <summary>Merchandise visibly damaged or destroyed during outbound transit.</summary>
    DamagedInTransit = 3,

    /// <summary>Customer or client merchant cancelled the order while in transit.</summary>
    CustomerCancelled = 4,

    /// <summary>Package was held at carrier depot but remained unclaimed past the hold deadline.</summary>
    UnclaimedAtDepot = 5,

    /// <summary>Carrier delivery attempt limit exceeded without successful delivery.</summary>
    CarrierReturnPolicyExceeded = 6,

    /// <summary>Shipment misrouted or lost in transit, subsequently recovered and routed back.</summary>
    LostAndRecovered = 7,

    /// <summary>Other authorized return reason.</summary>
    Other = 99
}

