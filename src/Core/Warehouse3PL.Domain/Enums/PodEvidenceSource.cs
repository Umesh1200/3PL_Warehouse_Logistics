namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Audit source establishing Proof of Delivery authenticity.
/// Phase 4.5 Outbound Shipment Tracking & Delivery Confirmation.
/// </summary>
public enum PodEvidenceSource
{
    CarrierApi = 1,            // Direct webhook/API delivery confirmation from carrier
    DriverHandheld = 2,        // Dedicated driver mobile app scan with GPS and signature
    ConsigneePortal = 3,       // Consignee digital receipt acknowledgment
    ManualVerification = 4     // 3PL operations verified delivery with carrier support
}

