namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational lifecycle status for carrier delivery exceptions.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public enum DeliveryExceptionStatus
{
    /// <summary>Exception reported/ingested; awaiting operational triage.</summary>
    Open = 1,

    /// <summary>Operations or customer service actively investigating with carrier or customer.</summary>
    UnderReview = 2,

    /// <summary>Carrier delivery re-attempt has been scheduled and acknowledged.</summary>
    ScheduledForReattempt = 3,

    /// <summary>Awaiting consignee action or instructions (e.g., gate access code, corrected address).</summary>
    PendingCustomerAction = 4,

    /// <summary>Escalated to client merchant for Return-To-Sender authorization.</summary>
    PendingReturnAuthorization = 5,

    /// <summary>Exception operational workflow successfully resolved.</summary>
    Resolved = 6,

    /// <summary>Exception closed by initiating an authorized Return-To-Sender shipment.</summary>
    ReturnToSenderInitiated = 7,

    /// <summary>Exception rescinded or voided (e.g., carrier false alarm, erroneous reporting).</summary>
    Voided = 8
}

