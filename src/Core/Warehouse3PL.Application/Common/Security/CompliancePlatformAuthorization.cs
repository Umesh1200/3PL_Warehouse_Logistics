using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Common.Security;

/// <summary>
/// Authoritative platform Staff verification for global statutory compliance operations.
/// Rejects unauthenticated callers (401), non-Staff account types (403), and untrusted/forged
/// token claims by verifying active DB UserType, Role assignments, and RolePermissions.
/// </summary>
public sealed class CompliancePlatformAuthorization
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public CompliancePlatformAuthorization(IApplicationDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Guid> RequireVerifiedStaffPermissionAsync(
        CancellationToken cancellationToken,
        params string[] allowedPermissions)
    {
        var dbUser = await LoadVerifiedStaffUserAsync(cancellationToken);
        var dbRoleNames = dbUser.Roles
            .Select(r => r.RoleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool isDbSuperAdmin = dbRoleNames.Contains(SystemRoles.SuperAdmin);
        var dbPermissions = dbUser.Roles
            .SelectMany(r => r.Permissions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!isDbSuperAdmin)
        {
            bool hasDbPermission = allowedPermissions.Any(p =>
                dbPermissions.Contains(p) && _currentUser.HasPermission(p));

            if (!hasDbPermission)
            {
                throw new ForbiddenException(
                    $"Active Staff user lacks required database-verified permission ({string.Join(" or ", allowedPermissions)}).");
            }
        }

        return dbUser.Id;
    }

    /// <summary>
    /// Verifies active DB Staff identity and facility management authority.
    /// Non-SuperAdmin Staff without multi-facility scope are strictly restricted to their DB-assigned facility.
    /// </summary>
    public async Task<Guid> RequireVerifiedFacilityManagerAsync(
        Guid? targetFacilityId,
        CancellationToken cancellationToken)
    {
        var dbUser = await LoadVerifiedStaffUserAsync(cancellationToken);
        var dbRoleNames = dbUser.Roles
            .Select(r => r.RoleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool isDbSuperAdmin = dbRoleNames.Contains(SystemRoles.SuperAdmin);
        var dbPermissions = dbUser.Roles
            .SelectMany(r => r.Permissions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!isDbSuperAdmin)
        {
            if (!dbPermissions.Contains(Permissions.Facilities.Manage) ||
                !_currentUser.HasPermission(Permissions.Facilities.Manage))
            {
                throw new ForbiddenException(
                    $"Active Staff user lacks required database-verified permission '{Permissions.Facilities.Manage}'.");
            }

            bool isCrossFacility = dbPermissions.Contains(Permissions.Analytics.MultiFacilityView) ||
                                   dbPermissions.Contains("Analytics:CrossFacility");

            if (!isCrossFacility)
            {
                if (!targetFacilityId.HasValue)
                {
                    if (dbUser.AssignedFacilityId.HasValue)
                    {
                        throw new ForbiddenException(
                            "Facility-bound Staff cannot create new platform facilities without multi-facility permission.");
                    }
                }
                else if (!dbUser.AssignedFacilityId.HasValue || dbUser.AssignedFacilityId.Value != targetFacilityId.Value)
                {
                    throw new ForbiddenException(
                        $"Staff member is not authorized to mutate facility '{targetFacilityId.Value}'.");
                }
            }
        }

        return dbUser.Id;
    }

    private async Task<VerifiedStaffSnapshot> LoadVerifiedStaffUserAsync(CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated ||
            !_currentUser.UserId.HasValue ||
            _currentUser.UserId.Value == Guid.Empty)
        {
            throw new UnauthorizedException("Authentication is required for administrative platform operations.");
        }

        if (_currentUser.UserType != UserType.Staff || !_currentUser.IsStaff)
        {
            throw new ForbiddenException("Administrative platform operations are strictly restricted to authenticated platform Staff.");
        }

        var userId = _currentUser.UserId.Value;

        var dbUser = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new VerifiedStaffSnapshot(
                u.Id,
                u.IsDeleted,
                u.Status,
                u.UserType,
                u.AssignedFacilityId,
                u.UserRoles.Select(ur => new VerifiedStaffRole(
                    ur.Role.RoleName,
                    ur.Role.RolePermissions.Select(rp => rp.PermissionKey).ToList())).ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        if (dbUser == null || dbUser.IsDeleted || dbUser.Status != UserStatus.Active || dbUser.UserType != UserType.Staff)
        {
            throw new ForbiddenException("Caller is not an active database-verified Staff account.");
        }

        var dbRoleNames = dbUser.Roles
            .Select(r => r.RoleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_currentUser.IsInRole(SystemRoles.SuperAdmin) && !dbRoleNames.Contains(SystemRoles.SuperAdmin))
        {
            throw new ForbiddenException("Claimed SuperAdmin role is not backed by an authoritative database role assignment.");
        }

        return dbUser;
    }

    private sealed record VerifiedStaffRole(string RoleName, List<string> Permissions);

    private sealed record VerifiedStaffSnapshot(
        Guid Id,
        bool IsDeleted,
        UserStatus Status,
        UserType UserType,
        Guid? AssignedFacilityId,
        List<VerifiedStaffRole> Roles);
}
