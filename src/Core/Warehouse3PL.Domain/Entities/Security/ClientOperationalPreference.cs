using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;

namespace Warehouse3PL.Domain.Entities.Security;

/// <summary>
/// Structured operational preferences for a Client, supporting explicit Client-level defaults
/// (FacilityId = null) and optional Facility-specific overrides (FacilityId != null).
/// Intentionally excludes commercial rate cards, contracts, and billing SLA policies.
/// </summary>
public class ClientOperationalPreference : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid? FacilityId { get; set; }

    public Guid? DefaultReceivingZoneId { get; set; }
    public Guid? DefaultShippingZoneId { get; set; }

    public bool RequireAsnBeforeReceiving { get; set; }
    public bool AllowPartialShipments { get; set; } = true;
    public bool RequireQcInspectionOnReturns { get; set; }
    public bool AllowLotMixingInLocation { get; set; }
    public int DefaultOrderPriority { get; set; } = 1;
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
    public int ConfigRevision { get; set; } = 1;

    // Navigation properties
    public Client Client { get; set; } = null!;
    public Facility? Facility { get; set; }
    public WarehouseZone? DefaultReceivingZone { get; set; }
    public WarehouseZone? DefaultShippingZone { get; set; }
}
