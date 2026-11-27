using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Domain.Entities.Facilities;

/// <summary>
/// Operational storage compatibility & capacity profile for a storable <see cref="Location"/> (MDM-2.1 A05 & A06).
/// Only exposes restrictions with real active execution enforcement in Putaway & Stock Placement;
/// unified thermal/hazmat overrides remain deferred to MDM-2.4.
/// </summary>
public class LocationStorageProfile : AuditableEntity<Guid>
{
    public Guid LocationId
    {
        get => Id;
        set => Id = value;
    }

    public Guid FacilityId { get; set; }
    public Guid? DedicatedClientId { get; set; }
    public int? MaxHandlingUnits { get; set; }
    public bool AllowMixedSku { get; set; } = true;
    public bool AllowMixedLot { get; set; } = true;
    public bool IsPutawayAllowed { get; set; } = true;
    public long ConfigRevision { get; set; } = 1;

    public Location Location { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Client? DedicatedClient { get; set; }
}
