namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Communication channel utilized when contacting a customer/consignee regarding a delivery exception.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public enum CustomerContactMethod
{
    PhoneCall = 1,
    Sms = 2,
    Email = 3,
    CustomerPortal = 4,
    CarrierMessaging = 5,
    InPerson = 6
}

