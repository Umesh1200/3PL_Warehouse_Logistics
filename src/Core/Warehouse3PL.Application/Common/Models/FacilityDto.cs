namespace Warehouse3PL.Application.Common.Models;

public record FacilityDto
{
    public Guid Id { get; init; }
    public string FacilityCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? AddressLine1 { get; init; }
    public string? City { get; init; }
    public string Timezone { get; init; } = "UTC";
    public bool IsActive { get; init; }
}

