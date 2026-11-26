using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Snapshot of physical destination address and carrier delivery instructions for an OutboundOrder.
/// Maintained separately from Customer master to ensure historical shipment destination immutability.
/// Phase 4.1 Order Management.
/// </summary>
public class ShippingAddress : BaseEntity<Guid>, ISoftDeletable
{
    public Guid OutboundOrderId { get; set; }

    public string RecipientName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string StateOrProvince { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "USA";
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.Normalized;
    public string? PhoneNumber { get; set; }
    public string? DeliveryInstructions { get; set; }

    // Amendment audit tracking
    public bool IsAmended { get; set; } = false;
    public DateTime? AmendedAtUtc { get; set; }
    public Guid? AmendedByUserId { get; set; }
    public string? AmendmentReason { get; set; }
    public string? OriginalAddressSnapshot { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigation
    public OutboundOrder OutboundOrder { get; set; } = null!;

    public ShippingAddress()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Explicitly amends destination details prior to warehouse floor fulfillment.
    /// Preserves a snapshot of the prior address for auditability.
    /// </summary>
    public void Amend(
        string recipientName,
        string? companyName,
        string addressLine1,
        string? addressLine2,
        string city,
        string stateOrProvince,
        string postalCode,
        string countryCode,
        string? phoneNumber,
        string? deliveryInstructions,
        Guid amendedByUserId,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(OriginalAddressSnapshot))
        {
            OriginalAddressSnapshot = $"{RecipientName}, {CompanyName ?? ""}, {AddressLine1}, {AddressLine2 ?? ""}, {City}, {StateOrProvince} {PostalCode}, {CountryCode}".Trim();
        }

        RecipientName = recipientName;
        CompanyName = companyName;
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        City = city;
        StateOrProvince = stateOrProvince;
        PostalCode = postalCode;
        CountryCode = countryCode;
        PhoneNumber = phoneNumber;
        DeliveryInstructions = deliveryInstructions;

        IsAmended = true;
        AmendedAtUtc = DateTime.UtcNow;
        AmendedByUserId = amendedByUserId;
        AmendmentReason = reason;
    }

    public string ToFormattedSingleLine()
    {
        var line2 = string.IsNullOrWhiteSpace(AddressLine2) ? "" : $" {AddressLine2},";
        return $"{RecipientName}, {AddressLine1},{line2} {City}, {StateOrProvince} {PostalCode}, {CountryCode}";
    }
}

