using Warehouse3PL.Domain.Common;

namespace Warehouse3PL.Domain.Entities.Security;

/// <summary>
/// Contact point for a 3PL Client.
/// </summary>
public class ClientContact : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? RoleTitle { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsBillingContact { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public int ConfigRevision { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigation
    public Client Client { get; set; } = null!;

    public ClientContact()
    {
        Id = Guid.NewGuid();
    }
}

