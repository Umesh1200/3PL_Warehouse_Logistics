namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Shipping service level requested for outbound transportation.
/// Phase 4.1 Order Management.
/// </summary>
public enum CarrierServiceLevel
{
    StandardGround = 1,
    NextDayAir = 2,
    SecondDayAir = 3,
    PriorityOvernight = 4,
    FreightLTL = 5,
    FreightFTL = 6,
    CustomerPickup = 7
}

