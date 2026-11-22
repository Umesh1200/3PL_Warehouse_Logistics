using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Authentication;

/// <summary>
/// Authoritative JWT validation check verifying:
/// - Active, non-deleted user state
/// - Strict parity between signed JWT user_type and DB UserType
/// - Incompatible privileged role rejection & immediate rejection of revoked DB roles
/// - Client tenant_id parity
/// - Staff facility_id parity with active DB Users.AssignedFacilityId
/// </summary>
public static class JwtStaffFacilityValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var userIdStr = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.Principal?.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdStr, out var userId) || userId == Guid.Empty)
        {
            context.Fail("Invalid token subject identity.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<IApplicationDbContext>();
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.IsDeleted,
                u.Status,
                u.UserType,
                u.ClientId,
                u.AssignedFacilityId,
                RoleNames = u.UserRoles.Select(ur => ur.Role.RoleName).ToList()
            })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (user == null || user.IsDeleted || user.Status != UserStatus.Active)
        {
            context.Fail("User account is inactive, suspended, or deleted.");
            return;
        }

        // Enforce strict signed user_type parity against authoritative DB UserType
        var tokenUserTypeClaim = context.Principal?.FindFirst("user_type")?.Value
            ?? context.Principal?.FindFirst("UserType")?.Value;

        if (!Enum.TryParse<UserType>(tokenUserTypeClaim, ignoreCase: true, out var tokenUserType) ||
            tokenUserType != user.UserType)
        {
            context.Fail("Token user_type does not match authoritative database account type.");
            return;
        }

        // Extract all signed role claims across ClaimTypes.Role / roles / role
        var tokenRoles = context.Principal!
            .FindAll(ClaimTypes.Role)
            .Concat(context.Principal.FindAll("roles"))
            .Concat(context.Principal.FindAll("role"))
            .Select(c => c.Value.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Reject incompatible privileged/platform role claims for account type and reject revoked DB roles immediately
        var dbRolesSet = user.RoleNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tokenRole in tokenRoles)
        {
            if (!RoleAssignmentPolicy.IsAllowedRoleForUserType(user.UserType, tokenRole))
            {
                context.Fail($"Token contains incompatible role '{tokenRole}' for account type '{user.UserType}'.");
                return;
            }

            if (!dbRolesSet.Contains(tokenRole))
            {
                context.Fail($"Token role '{tokenRole}' has been revoked or is not currently assigned.");
                return;
            }
        }

        if (user.UserType == UserType.Client)
        {
            var tenantClaim = context.Principal.FindFirst("tenant_id")?.Value
                ?? context.Principal.FindFirst("client_id")?.Value;
            if (!Guid.TryParse(tenantClaim, out var tokenTenantId) ||
                !user.ClientId.HasValue ||
                tokenTenantId != user.ClientId.Value)
            {
                context.Fail("Client token tenant scope does not match authoritative database tenant.");
                return;
            }
            return;
        }

        if (user.UserType != UserType.Staff)
        {
            return;
        }

        var facilityClaim = context.Principal.FindFirst("facility_id")?.Value;
        Guid? tokenFacilityId = null;
        if (!string.IsNullOrWhiteSpace(facilityClaim))
        {
            if (!Guid.TryParse(facilityClaim, out var parsedFacilityId) || parsedFacilityId == Guid.Empty)
            {
                context.Fail("Invalid staff facility claim.");
                return;
            }
            tokenFacilityId = parsedFacilityId;
        }

        if (tokenFacilityId != user.AssignedFacilityId)
        {
            context.Fail("Staff facility assignment has changed; token facility scope is stale.");
            return;
        }

        if (user.AssignedFacilityId.HasValue)
        {
            var facilityIsActive = await db.Facilities
                .AsNoTracking()
                .AnyAsync(f => f.Id == user.AssignedFacilityId.Value && !f.IsDeleted && f.IsActive,
                    context.HttpContext.RequestAborted);

            if (!facilityIsActive)
            {
                context.Fail("Assigned staff facility is inactive or no longer exists.");
            }
        }
    }
}
