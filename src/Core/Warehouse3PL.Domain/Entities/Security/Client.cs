using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Security;

/// <summary>
/// Root Tenant entity: represents a 3PL logistics client company.
/// Per DB-SPEC-3PL-2026: clients.client_id is the primary multi-tenancy anchor
/// propagating across SKUs, ledger, orders, and rate cards.
/// </summary>
public class Client : AuditableEntity<Guid>, ISoftDeletable
{
    public string CompanyName { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public ClientStatus Status { get; set; } = ClientStatus.Onboarding;
    public SlaTier SlaTier { get; set; } = SlaTier.Standard;
    public string Currency { get; set; } = "USD";

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public ICollection<ClientContact> Contacts { get; set; } = [];
    public ICollection<User> Users { get; set; } = [];
    public ICollection<ClientFacilityAccess> FacilityAccesses { get; set; } = [];

    public Client()
    {
        Id = Guid.NewGuid();
    }
}

