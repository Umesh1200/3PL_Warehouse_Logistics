namespace Warehouse3PL.Application.Common.Models;

/// <summary>
/// Reader-safe facility summary exposed through <c>facilities:view</c>.
/// Excludes privileged operational settings and raw configuration JSON.
/// </summary>
public record FacilityDto
{
    public Guid Id { get; init; }
    public string FacilityCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? StateOrProvince { get; init; }
    public string? PostalCode { get; init; }
    public string CountryCode { get; init; } = string.Empty;
    public string Timezone { get; init; } = "UTC";
    public Guid? DefaultLegalEntityId { get; init; }
    public string? DefaultCurrencyCode { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    public decimal? TotalAreaSqM { get; init; }
    public int DockDoorCount { get; init; }
    public bool IsActive { get; init; }
}
