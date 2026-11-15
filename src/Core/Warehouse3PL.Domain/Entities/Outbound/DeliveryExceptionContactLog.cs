using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Historical operational audit log for customer/consignee contact attempts on delivery exceptions.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public class DeliveryExceptionContactLog : AuditableEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid ExceptionId { get; set; }
    public Guid PackageId { get; set; }

    public CustomerContactMethod ContactMethod { get; set; }
    public CustomerContactOutcome Outcome { get; set; }

    public DateTime ContactedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid StaffUserId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? ResolutionAction { get; set; }

    // Navigations
    public ShipmentDeliveryException Exception { get; set; } = null!;
    public User StaffUser { get; set; } = null!;

    public DeliveryExceptionContactLog()
    {
        Id = Guid.NewGuid();
    }
}

