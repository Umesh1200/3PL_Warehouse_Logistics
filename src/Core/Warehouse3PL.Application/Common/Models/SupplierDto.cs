using System.Text.Json.Serialization;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class SupplierDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
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
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CountryNormalizationStatus CountryNormalizationStatus { get; set; } = CountryNormalizationStatus.PendingNormalization;
    public bool IsActive { get; set; }
    public int ConfigRevision { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public IReadOnlyList<SupplierContactDto> Contacts { get; set; } = [];
}

public record SupplierContactDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public Guid SupplierId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? RoleTitle { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsActive { get; init; } = true;
    public int ConfigRevision { get; init; } = 1;
}
