using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Security;

/// <summary>
/// Audit trail capturing automated reconciliation of privileged/platform roles
/// removed from non-staff accounts (Client / Driver).
/// </summary>
public class RoleReconciliationAuditLog : BaseEntity<Guid>
{
    public Guid UserId { get; set; }
    public UserType UserType { get; set; }
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime ReconciledAtUtc { get; set; } = DateTime.UtcNow;

    public RoleReconciliationAuditLog()
    {
        Id = Guid.NewGuid();
    }
}
