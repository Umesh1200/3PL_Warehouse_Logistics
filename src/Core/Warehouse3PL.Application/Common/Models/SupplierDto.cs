namespace Warehouse3PL.Application.Common.Models;

public class SupplierDto
{
    public Guid Id { get; set; }
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
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public Warehouse3PL.Domain.Enums.CountryNormalizationStatus CountryNormalizationStatus { get; set; } = Warehouse3PL.Domain.Enums.CountryNormalizationStatus.PendingNormalization;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

