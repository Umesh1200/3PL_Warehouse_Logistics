using Microsoft.AspNetCore.Authorization;

namespace Warehouse3PL.Infrastructure.Authorization;

public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public IReadOnlyList<string> Permissions { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
        var split = permission.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Permissions = split.Length > 0 ? split : [permission];
    }
}
