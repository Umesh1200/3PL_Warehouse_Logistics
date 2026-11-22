using Microsoft.AspNetCore.Authorization;

namespace Warehouse3PL.Infrastructure.Authorization;

/// <summary>
/// Requires the authenticated user to hold at least one of the specified permissions (or SuperAdmin role).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class HasAnyPermissionAttribute : AuthorizeAttribute
{
    public HasAnyPermissionAttribute(params string[] permissions)
        : base(policy: string.Join('|', permissions))
    {
        if (permissions == null || permissions.Length == 0)
        {
            throw new ArgumentException("At least one permission must be specified.", nameof(permissions));
        }
    }
}
