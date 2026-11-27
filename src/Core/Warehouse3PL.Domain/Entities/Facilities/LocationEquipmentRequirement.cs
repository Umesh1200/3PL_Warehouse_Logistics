using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Facilities;

/// <summary>
/// Declares required material handling equipment types for accessing a physical warehouse location (MDM-2.1 A05).
/// </summary>
public class LocationEquipmentRequirement : AuditableEntity<Guid>
{
    public Guid LocationId { get; set; }
    public Guid FacilityId { get; set; }
    public WarehouseEquipmentType EquipmentType { get; set; } = WarehouseEquipmentType.Forklift;
    public bool IsMandatory { get; set; } = true;
    public long ConfigRevision { get; set; } = 1;

    public Location Location { get; set; } = null!;
    public Facility Facility { get; set; } = null!;

    public LocationEquipmentRequirement()
    {
        Id = Guid.NewGuid();
    }
}
