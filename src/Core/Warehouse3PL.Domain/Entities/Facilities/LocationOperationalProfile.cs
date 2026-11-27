using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Facilities;

/// <summary>
/// Operational station capability profile for receiving, packing, dispatch, inspection, or returns locations (MDM-2.1 A05).
/// </summary>
public class LocationOperationalProfile : AuditableEntity<Guid>
{
    public Guid LocationId
    {
        get => Id;
        set => Id = value;
    }

    public Guid FacilityId { get; set; }
    public WarehouseStationRole StationRole { get; set; } = WarehouseStationRole.Packing;
    public bool SupportsScale { get; set; } = false;
    public bool SupportsLabelPrinter { get; set; } = false;
    public bool SupportsConveyor { get; set; } = false;
    public int MaxConcurrentSessions { get; set; } = 1;
    public Guid? DefaultStagingLocationId { get; set; }
    public long ConfigRevision { get; set; } = 1;

    public Location Location { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Location? DefaultStagingLocation { get; set; }
}
