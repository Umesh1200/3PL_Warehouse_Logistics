namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Configurable hierarchical location types.
/// Supports arbitrary depth topologies (e.g. Facility -> Zone -> Aisle -> Rack -> Shelf -> Bin).
/// </summary>
public enum LocationType
{
    Facility = 1,
    Zone = 2,
    Aisle = 3,
    Rack = 4,
    ShelfLevel = 5,
    Bin = 6,
    DockDoor = 7,
    StagingLane = 8,
    BulkFloor = 9,
    Mezzanine = 10,
    Cantilever = 11,
    PackingStation = 12
}

