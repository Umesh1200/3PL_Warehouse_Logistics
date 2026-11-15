using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Security;

/// <summary>
/// Mapping of roles to fine-grained permission keys and access levels.
/// </summary>
public class RolePermission : AuditableEntity<long>
{
    public int RoleId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public AccessLevel AccessLevel { get; set; } = AccessLevel.Read;

    // Navigation
    public Role Role { get; set; } = null!;
}

