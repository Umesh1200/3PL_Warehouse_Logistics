using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Security;

/// <summary>
/// Security role definition.
/// </summary>
public class Role : AuditableEntity<int>
{
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }

    // Navigations
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
    public ICollection<UserRole> UserRoles { get; set; } = [];
}

