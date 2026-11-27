namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Authoritative administrative lifecycle status of a warehouse location (MDM-2.1 A01).
/// Strictly separated from operational locks (<see cref="LocationLockType"/>) and physical damage (<c>IsDamaged</c>):
/// administrative activation NEVER implicitly releases quarantine, maintenance, or cycle-count locks.
/// </summary>
public enum LocationStatus
{
    Active = 1,
    Inactive = 2,
    Decommissioned = 3
}

/// <summary>
/// Operational role for a physical warehouse workstation/station location profile.
/// </summary>
public enum WarehouseStationRole
{
    Receiving = 1,
    Packing = 2,
    Dispatch = 3,
    QualityInspection = 4,
    Returns = 5
}

/// <summary>
/// Operational lifecycle status for warehouse material handling equipment (MHE).
/// </summary>
public enum WarehouseEquipmentStatus
{
    Active = 1,
    Maintenance = 2,
    OutOfService = 3,
    Retired = 4
}
