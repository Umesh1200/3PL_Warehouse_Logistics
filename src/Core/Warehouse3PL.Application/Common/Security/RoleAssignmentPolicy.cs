using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Common.Security;

/// <summary>
/// Authoritative server-side role assignment and validation policy.
/// Enforced uniformly across user creation and role-assignment endpoints:
/// - ClientAdmin is strictly confined to approved client roles within its own tenant.
/// - Client and Driver users can never hold SuperAdmin or platform/warehouse-only roles.
/// - SuperAdmin grants and removals require a verified platform SuperAdmin (UserType.Staff + SuperAdmin).
/// - Fails closed on any unauthorized, disallowed, blank, or unknown role name.
/// </summary>
public static class RoleAssignmentPolicy
{
    public static readonly IReadOnlySet<string> ApprovedClientRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SystemRoles.ClientAdmin,
        SystemRoles.ClientUser
    };

    public static bool IsAllowedRoleForUserType(UserType userType, string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return false;
        }

        var normalized = roleName.Trim();
        return userType switch
        {
            UserType.Client => ApprovedClientRoles.Contains(normalized),
            UserType.Driver => string.Equals(normalized, SystemRoles.Driver, StringComparison.OrdinalIgnoreCase),
            UserType.Staff => !ApprovedClientRoles.Contains(normalized) &&
                              !string.Equals(normalized, SystemRoles.Driver, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public static async Task<List<Role>> ValidateAndResolveRolesAsync(
        IApplicationDbContext context,
        ICurrentUserContext caller,
        UserType targetUserType,
        Guid? targetClientId,
        IEnumerable<string> requestedRoleNames,
        IEnumerable<string>? existingTargetRoleNames,
        CancellationToken cancellationToken)
    {
        if (!caller.IsAuthenticated || !caller.UserId.HasValue || caller.UserId.Value == Guid.Empty)
        {
            throw new ForbiddenException("Authenticated caller identity is required for role assignment.");
        }

        var callerIsPlatformSuperAdmin = caller.UserType == UserType.Staff && caller.IsSuperAdmin;
        var callerIsStaff = caller.UserType == UserType.Staff && caller.IsStaff;

        if (!callerIsStaff)
        {
            if (!caller.TenantId.HasValue || caller.TenantId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Tenant context is required to manage client user roles.");
            }

            if (targetUserType != UserType.Client)
            {
                throw new ForbiddenException("Client administrators cannot create or manage roles for Staff or Driver accounts.");
            }

            if (!targetClientId.HasValue || targetClientId.Value != caller.TenantId.Value)
            {
                throw new ForbiddenException("Cannot manage users or roles belonging to another client tenant.");
            }
        }

        var rawList = requestedRoleNames?.ToList() ?? [];
        if (rawList.Any(string.IsNullOrWhiteSpace))
        {
            throw new ValidationException("Roles", "Role names cannot be null or whitespace.");
        }

        var normalizedRequested = rawList
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingSet = (existingTargetRoleNames ?? [])
            .Select(r => r.Trim())
            .Where(r => r.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targetCurrentlyHasSuperAdmin = existingSet.Contains(SystemRoles.SuperAdmin);
        var requestsSuperAdmin = normalizedRequested.Contains(SystemRoles.SuperAdmin);

        // Restrict SuperAdmin grants, removals, and SuperAdmin account role tampering to verified platform SuperAdmin.
        if (!callerIsPlatformSuperAdmin && (requestsSuperAdmin || targetCurrentlyHasSuperAdmin))
        {
            throw new ForbiddenException("Only an authorized platform SuperAdmin may grant, revoke, or modify SuperAdmin role assignments.");
        }

        foreach (var roleName in normalizedRequested)
        {
            if (!callerIsStaff && !ApprovedClientRoles.Contains(roleName))
            {
                throw new ForbiddenException(
                    $"Client administrators are not permitted to assign role '{roleName}'. Permitted client roles: ClientAdmin, ClientUser.");
            }

            if (!IsAllowedRoleForUserType(targetUserType, roleName))
            {
                throw new ForbiddenException(
                    $"Role '{roleName}' is incompatible with user account type '{targetUserType}'.");
            }
        }

        if (normalizedRequested.Count == 0)
        {
            return [];
        }

        var lowerNames = normalizedRequested.Select(r => r.ToLowerInvariant()).ToList();
        var dbRoles = await context.Roles
            .Where(r => lowerNames.Contains(r.RoleName.ToLower()))
            .ToListAsync(cancellationToken);

        var foundSet = dbRoles
            .Select(r => r.RoleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknownRoles = normalizedRequested
            .Where(r => !foundSet.Contains(r))
            .ToList();

        if (unknownRoles.Count > 0)
        {
            throw new ValidationException("Roles",
                $"Unknown or unconfigured role(s) requested: {string.Join(", ", unknownRoles)}.");
        }

        return dbRoles;
    }
}
