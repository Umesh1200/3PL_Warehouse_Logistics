using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Audit trail for shipment lifecycle, package membership amendments, staging, loading, and dispatch events.
/// DB-SPEC-3PL-2026 Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public class ShipmentAuditLog : BaseEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid? PackageId { get; set; }

    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Shipment Shipment { get; set; } = null!;
    public Package? Package { get; set; }

    public ShipmentAuditLog()
    {
        Id = Guid.NewGuid();
    }
}

