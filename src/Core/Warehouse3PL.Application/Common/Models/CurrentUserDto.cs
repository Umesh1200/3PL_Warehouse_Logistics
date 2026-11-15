using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public record CurrentUserDto
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public UserType UserType { get; init; }
    public Guid? TenantId { get; init; }
    public string? CompanyName { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

