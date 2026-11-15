namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status of a warehouse inbound dock receiving handoff for an authorized Return-To-Sender (RTS) package.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4: Return-to-Sender Authorization, Return Shipment &amp; Tracking.
/// </summary>
public enum ReturnInboundHandoffStatus
{
    /// <summary>Handoff created upon carrier dock delivery; awaiting physical receiving scan by warehouse dock staff.</summary>
    PendingReceiving = 1,

    /// <summary>Physical receiving in progress at warehouse dock.</summary>
    Processing = 2,

    /// <summary>Goods received and accepted at warehouse facility; routed to inspection/disposition.</summary>
    Received = 3,

    /// <summary>Receiving handoff cancelled or rejected prior to physical receipt.</summary>
    Cancelled = 4
}

