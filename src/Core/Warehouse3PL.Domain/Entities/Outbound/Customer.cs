using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Client-owned recipient / customer master record.
/// Fully partitioned by ClientId (Tenant).
/// Global to all warehouse facilities of that client.
/// Phase 4.1 Order Management.
/// </summary>
public class Customer : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string NormalizedCustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public CustomerType CustomerType { get; set; } = CustomerType.RetailCustomer;
    public string? TaxRegistrationNumber { get; set; }

    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    // Default delivery address template for new order pre-fill
    public string? DefaultAddressLine1 { get; set; }
    public string? DefaultAddressLine2 { get; set; }
    public string? DefaultCity { get; set; }
    public string? DefaultStateOrProvince { get; set; }
    public string? DefaultPostalCode { get; set; }
    public string? DefaultCountryCode { get; set; }
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.PendingNormalization;

    // Structured billing address (MDM-2.2)
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingStateOrProvince { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
    public CountryNormalizationStatus BillingCountryNormalizationStatus { get; set; } = CountryNormalizationStatus.PendingNormalization;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Concurrency token for optimistic locking on customer master updates.
    /// </summary>
    public uint Version { get; set; } = 1;
    public int ConfigRevision { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public ICollection<OutboundOrder> Orders { get; set; } = [];
    public ICollection<CustomerAddress> Addresses { get; set; } = [];

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public Customer()
    {
        Id = Guid.NewGuid();
    }
}

