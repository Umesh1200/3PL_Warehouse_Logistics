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
        if (!_currentUser.IsAuthenticated ||
            !_currentUser.UserId.HasValue ||
            _currentUser.UserId.Value == Guid.Empty)
        {
            throw new UnauthorizedException("Authentication is required for statutory compliance operations.");
        }

        if (_currentUser.UserType != UserType.Staff || !_currentUser.IsStaff)
        {
            throw new ForbiddenException("Statutory compliance operations are strictly restricted to authenticated platform Staff.");
        }

        var userId = _currentUser.UserId.Value;

        var dbUser = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.IsDeleted,
                u.Status,
                u.UserType,
                Roles = u.UserRoles.Select(ur => new
                {
                    ur.Role.RoleName,
                    Permissions = ur.Role.RolePermissions.Select(rp => rp.PermissionKey).ToList()
                }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (dbUser == null || dbUser.IsDeleted || dbUser.Status != UserStatus.Active || dbUser.UserType != UserType.Staff)
        {
            throw new ForbiddenException("Caller is not an active database-verified Staff account.");
        }

        var dbRoleNames = dbUser.Roles
            .Select(r => r.RoleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Reject if caller claims SuperAdmin without an authoritative SuperAdmin DB role assignment.
        if (_currentUser.IsInRole(SystemRoles.SuperAdmin) && !dbRoleNames.Contains(SystemRoles.SuperAdmin))
        {
            throw new ForbiddenException("Claimed SuperAdmin role is not backed by an authoritative database role assignment.");
        }

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
}
