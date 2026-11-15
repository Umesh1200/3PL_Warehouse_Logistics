namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational resolution lifecycle for carrier delivery exceptions.
/// Phase 4.5 Outbound Shipment Tracking &amp; Delivery Confirmation (Step 3B).
/// </summary>
public enum DeliveryExceptionResolution
{
    PendingCarrierReattempt = 1,   // Carrier will re-attempt automatically next business day
    CustomerContacted = 2,         // Customer service contacted consignee for instructions
    AddressCorrected = 3,          // Updated address submitted to carrier
    HoldAtCarrierDepot = 4,        // Package redirected to carrier depot for customer pickup
    ReturnToSenderRequested = 5,   // Return to origin warehouse facility requested
    ClaimInitiated = 6,            // Lost/damaged freight insurance claim initiated
    Resolved = 7,                  // Exception successfully resolved and delivered
    RedeliveryScheduled = 8,       // Explicit carrier re-delivery date confirmed
    ReturnToSenderAuthorized = 9,  // Return-to-sender officially authorized by client
    PackageDestroyedAbandoned = 10,// Hazardous or damaged parcel destroyed with authorization
    CarrierErrorRescinded = 11     // Carrier confirmed scan was in error
}
