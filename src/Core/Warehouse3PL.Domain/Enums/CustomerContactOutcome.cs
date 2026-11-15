namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational outcome of an outbound consignee contact attempt for delivery exception resolution.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public enum CustomerContactOutcome
{
    /// <summary>Customer reached; agreed to carrier re-attempt schedule.</summary>
    ReachedAgreedToReattempt = 1,

    /// <summary>Customer reached; provided corrected address or access details.</summary>
    ReachedAddressCorrected = 2,

    /// <summary>Customer reached; confirmed refusal of delivery.</summary>
    ReachedRefusedDelivery = 3,

    /// <summary>Consignee requested hold at carrier depot or access point for self-pickup.</summary>
    ConsigneeRequestedDepotPickup = 4,

    /// <summary>Voicemail message left with instructions.</summary>
    LeftVoicemail = 5,

    /// <summary>Call placed but no answer; no voicemail facility.</summary>
    NoAnswer = 6,

    /// <summary>Phone number disconnected, invalid, or email bounced.</summary>
    InvalidContactDetails = 7,

    /// <summary>Message delivered; customer service awaiting callback.</summary>
    AwaitingCustomerCallback = 8
}

