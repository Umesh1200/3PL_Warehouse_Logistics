using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Security;

/// <summary>
/// User role assignment.
/// </summary>
public class UserRole : BaseEntity<long>
{
    public Guid UserId { get; set; }
    public int RoleId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public Guid? AssignedBy { get; set; }

    // Navigations
    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}

