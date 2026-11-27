using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Facilities;

/// <summary>
/// Configurable parent-to-child location hierarchy transition rule (MDM-2.1 A02 & A04).
/// Facility-specific rules (FacilityId != null) take precedence over global default rules (FacilityId == null).
/// Null ParentLocationType defines a permitted root location type.
/// </summary>
public class LocationHierarchyRule : AuditableEntity<Guid>
{
    public Guid? FacilityId { get; set; }
    public LocationType? ParentLocationType { get; set; }
    public LocationType ChildLocationType { get; set; }
    public int MaxDepth { get; set; } = 6;
    public bool IsStorableLeaf { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public long ConfigRevision { get; set; } = 1;

    public Facility? Facility { get; set; }

    public LocationHierarchyRule()
    {
        Id = Guid.NewGuid();
    }
}
