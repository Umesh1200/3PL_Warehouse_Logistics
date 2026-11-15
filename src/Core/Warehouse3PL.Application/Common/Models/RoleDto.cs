using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public record RoleDto
{
    public int Id { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsSystemRole { get; init; }
    public IReadOnlyList<RolePermissionDto> Permissions { get; init; } = [];
}

public record RolePermissionDto
{
    public long Id { get; init; }
    public string PermissionKey { get; init; } = string.Empty;
    public AccessLevel AccessLevel { get; init; }
}

