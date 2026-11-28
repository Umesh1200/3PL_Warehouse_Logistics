using System.Text.Json.Serialization;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public record CustomerDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CustomerType CustomerType { get; init; } = CustomerType.RetailCustomer;
    public string? TaxRegistrationNumber { get; init; }
    public string? ContactName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }

    public string? DefaultAddressLine1 { get; init; }
    public string? DefaultAddressLine2 { get; init; }
    public string? DefaultCity { get; init; }
    public string? DefaultStateOrProvince { get; init; }
    public string? DefaultPostalCode { get; init; }
    public string? DefaultCountryCode { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CountryNormalizationStatus CountryNormalizationStatus { get; init; } = CountryNormalizationStatus.PendingNormalization;

    public string? BillingAddressLine1 { get; init; }
    public string? BillingAddressLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingStateOrProvince { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CountryNormalizationStatus BillingCountryNormalizationStatus { get; init; } = CountryNormalizationStatus.PendingNormalization;

    public bool IsActive { get; init; }
    public int ConfigRevision { get; init; } = 1;
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<CustomerAddressDto> Addresses { get; init; } = [];
}

public record CustomerAddressDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public Guid CustomerId { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CustomerAddressType AddressType { get; init; } = CustomerAddressType.Delivery;
    public string? Label { get; init; }
    public string? RecipientName { get; init; }
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? StateOrProvince { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CountryNormalizationStatus CountryNormalizationStatus { get; init; } = CountryNormalizationStatus.PendingNormalization;
    public string? Phone { get; init; }
    public string? DeliveryInstructions { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;
    public int ConfigRevision { get; init; } = 1;
}
