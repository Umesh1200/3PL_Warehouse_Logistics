namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of an authorized Return-To-Sender (RTS) shipment.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public enum ReturnShipmentStatus
{
    /// <summary>Return authorized; return shipping label generated and awaiting carrier custody.</summary>
    Authorized = 1,

    /// <summary>Carrier has accepted the return parcel into custody and is transporting it back.</summary>
    InTransitToWarehouse = 2,

    /// <summary>Return shipment is out on vehicle for final delivery to the warehouse dock.</summary>
    OutForDeliveryToWarehouse = 3,

    /// <summary>Carrier has delivered the return package at the destination warehouse dock.</summary>
    DeliveredAtWarehouse = 4,

    /// <summary>Warehouse receiving and QA inspection completed; inventory disposition assigned.</summary>
    ReceivedAndInspected = 5,

    /// <summary>Return authorization cancelled prior to carrier custody handover.</summary>
    Cancelled = 6,

    /// <summary>Return parcel was lost by the carrier during reverse transit.</summary>
    LostInReturn = 7
}

