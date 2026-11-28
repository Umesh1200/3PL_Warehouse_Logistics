using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Security;

/// <summary>
/// Authorizes a client tenant to operate within specific physical facilities.
/// </summary>
public class ClientFacilityAccess : AuditableEntity<int>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;
    public FacilityAccessStatus Status { get; set; } = FacilityAccessStatus.Active;
    public bool PrimaryOperatingFacility { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? Remarks { get; set; }
    public int ConfigRevision { get; set; } = 1;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
}

