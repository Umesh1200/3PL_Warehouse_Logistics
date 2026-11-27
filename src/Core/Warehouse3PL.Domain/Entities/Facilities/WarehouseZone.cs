using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Facilities;

/// <summary>
/// Operational division inside a warehouse facility (e.g. Bulk, Pick Face, Cold Storage, Staging).
/// </summary>
public class WarehouseZone : AuditableEntity<Guid>, ISoftDeletable
{
    public Guid FacilityId { get; set; }
    public string ZoneCode { get; set; } = string.Empty;
    public string NormalizedZoneCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public WarehouseZoneType ZoneType { get; set; } = WarehouseZoneType.BulkStorage;
    public decimal? TempMinC { get; set; }
    public decimal? TempMaxC { get; set; }
    public Guid? DedicatedClientId { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
    public long ConfigRevision { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Facility Facility { get; set; } = null!;
    public Client? DedicatedClient { get; set; }
    public ICollection<Location> Locations { get; set; } = [];

    public WarehouseZone()
    {
        Id = Guid.NewGuid();
    }

    public static string NormalizeCode(string code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();
}

