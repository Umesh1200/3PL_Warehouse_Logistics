using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Structured billing and delivery addresses owned by a Customer master record.
/// Historical OutboundOrder/ShippingAddress snapshots remain immutable when master addresses change.
/// </summary>
public class CustomerAddress : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid CustomerId { get; set; }

    public CustomerAddressType AddressType { get; set; } = CustomerAddressType.Delivery;
    public string? Label { get; set; }
    public string? RecipientName { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? StateOrProvince { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.PendingNormalization;
    public string? Phone { get; set; }
    public string? DeliveryInstructions { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int ConfigRevision { get; set; } = 1;

    // ISoftDeletable
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigation properties
    public Customer Customer { get; set; } = null!;
}
