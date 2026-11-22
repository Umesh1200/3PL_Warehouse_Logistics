using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public record UserDto
{
    public Guid Id { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? AssignedFacilityId { get; init; }
    public string? CompanyName { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public UserType UserType { get; init; }
    public UserStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}

