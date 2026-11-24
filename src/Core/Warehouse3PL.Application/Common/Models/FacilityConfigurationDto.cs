namespace Warehouse3PL.Application.Common.Models;

/// <summary>
/// Privileged administrative facility detail including operational schedule and configuration payloads.
/// Restricted strictly to authorized warehouse staff holding <c>facilities:manage</c> or platform SuperAdmin.
/// </summary>
public record FacilityConfigurationDto : FacilityDto
{
    public string? OperatingHoursJson { get; init; }
    public string? ConfigurationJson { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
