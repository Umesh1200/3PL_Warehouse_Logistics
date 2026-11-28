using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Inbound;

/// <summary>
/// Client-specific supplier/vendor master record.
/// Fully partitioned by ClientId (Tenant).
/// </summary>
public class Supplier : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string NormalizedSupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? TaxRegistrationNumber { get; set; }
    public int? PaymentTermsDays { get; set; }
    public int? LeadTimeDays { get; set; }
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.PendingNormalization;
    public bool IsActive { get; set; } = true;
    public int ConfigRevision { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = [];
    public ICollection<AdvanceShippingNotice> Asns { get; set; } = [];
    public ICollection<SupplierContact> Contacts { get; set; } = [];

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public Supplier()
    {
        Id = Guid.NewGuid();
    }
}

