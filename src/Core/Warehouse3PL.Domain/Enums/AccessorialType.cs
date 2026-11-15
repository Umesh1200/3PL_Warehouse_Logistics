namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Categorization of specialized carrier accessorial services and surcharges.
/// DB-SPEC-3PL-2026 Phase 5.4 Shipping Margin, Accessorial Charges &amp; Carrier Markup Engine.
/// </summary>
public enum AccessorialType
{
    ResidentialDelivery = 1,
    LiftgateService = 2,
    SaturdayDelivery = 3,
    AddressCorrection = 4,
    InsideDelivery = 5,
    OversizeOverweight = 6,
    DangerousGoodsHazmat = 7,
    ReDelivery = 8,
    FuelSurcharge = 9,
    DeclaredValueInsurance = 10,
    RemoteAreaSurcharge = 11,
    Other = 99
}

