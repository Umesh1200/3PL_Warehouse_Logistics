using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Security;

/// <summary>
/// User entity for Staff, Client users, and Drivers.
/// ClientId is null for platform STAFF, and set for CLIENT / DRIVER tenants.
/// </summary>
public class User : AuditableEntity<Guid>, ISoftDeletable
{
    public Guid? ClientId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public UserType UserType { get; set; } = UserType.Client;
    public UserStatus Status { get; set; } = UserStatus.Active;

    // Refresh token
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client? Client { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = [];

    public User()
    {
        Id = Guid.NewGuid();
    }
}

