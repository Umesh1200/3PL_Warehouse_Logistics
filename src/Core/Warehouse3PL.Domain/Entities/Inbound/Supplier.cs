using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;

namespace Warehouse3PL.Domain.Entities.Inbound;

/// <summary>
/// Client-specific supplier/vendor master record.
/// Fully partitioned by ClientId (Tenant).
/// </summary>
public class Supplier : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public bool IsActive { get; set; } = true;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = [];
    public ICollection<AdvanceShippingNotice> Asns { get; set; } = [];

    public Supplier()
    {
        Id = Guid.NewGuid();
    }
}

