using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Application.Common.Security;

/// <summary>
/// DB-authoritative facility-scoped Staff authorization for MDM-2.1 Warehouse Master operations.
/// Enforces:
/// - Unauthenticated callers rejected (401)
/// - Client / non-Staff accounts rejected (403)
/// - Forged SuperAdmin / permission claims rejected via live DB User + Role + RolePermission verification (403)
/// - Conflicting X-Facility-Id header vs query/body FacilityId rejected (400)
/// - Non-SuperAdmin Staff strictly confined to their DB-assigned facility (403 on cross-facility access)
/// </summary>
public sealed class WarehouseMasterAuthorization
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public WarehouseMasterAuthorization(IApplicationDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<(Guid UserId, Guid FacilityId, bool IsSuperAdmin)> ResolveAuthorizedFacilityAsync(
        Guid? requestedFacilityId,
        CancellationToken cancellationToken,
        params string[] allowedPermissions)
    {
        var (dbUser, isSuperAdmin, dbPermissions) = await LoadAndVerifyStaffAsync(cancellationToken);

        // A05: Reject conflicting X-Facility-Id header vs query/body FacilityId
        var headerFacilityId = _currentUser.RequestedFacilityId;
        if (headerFacilityId.HasValue && headerFacilityId.Value != Guid.Empty &&
            requestedFacilityId.HasValue && requestedFacilityId.Value != Guid.Empty &&
            headerFacilityId.Value != requestedFacilityId.Value)
        {
            throw new ValidationException(
                "FacilityId",
                $"Conflicting facility scope: X-Facility-Id header '{headerFacilityId.Value}' does not match request FacilityId '{requestedFacilityId.Value}'.");
        }

        if (!isSuperAdmin && allowedPermissions.Length > 0)
        {
            bool hasDbPerm = allowedPermissions.Any(p =>
                dbPermissions.Contains(p) && _currentUser.HasPermission(p));
            if (!hasDbPerm)
            {
                throw new ForbiddenException(
                    $"Active Staff user lacks required database-verified permission ({string.Join(" or ", allowedPermissions)}).");
            }
        }

        Guid? effectiveFacilityId = requestedFacilityId is { } rf && rf != Guid.Empty
            ? rf
            : (headerFacilityId is { } hf && hf != Guid.Empty ? hf : dbUser.AssignedFacilityId);

        if (!effectiveFacilityId.HasValue || effectiveFacilityId.Value == Guid.Empty)
        {
            throw new ValidationException("FacilityId", "FacilityId is required for warehouse master operations.");
        }

        if (!isSuperAdmin)
        {
            if (!dbUser.AssignedFacilityId.HasValue || dbUser.AssignedFacilityId.Value != effectiveFacilityId.Value)
            {
                throw new ForbiddenException(
                    $"Staff user is not authorized for facility '{effectiveFacilityId.Value}'.");
            }
        }

        return (dbUser.Id, effectiveFacilityId.Value, isSuperAdmin);
    }

    public async Task<Guid> RequireGlobalHierarchyRuleAdminAsync(CancellationToken cancellationToken)
    {
        var (dbUser, isSuperAdmin, _) = await LoadAndVerifyStaffAsync(cancellationToken);
        if (!isSuperAdmin)
        {
            throw new ForbiddenException(
                "Managing global location hierarchy rules requires verified platform SuperAdmin authority.");
        }

        return dbUser.Id;
    }

    public async Task<(Guid UserId, bool IsSuperAdmin)> VerifyStaffPermissionAsync(
        CancellationToken cancellationToken,
        params string[] allowedPermissions)
    {
        var (dbUser, isSuperAdmin, dbPermissions) = await LoadAndVerifyStaffAsync(cancellationToken);
        if (!isSuperAdmin && allowedPermissions.Length > 0)
        {
            bool hasDbPerm = allowedPermissions.Any(p =>
                dbPermissions.Contains(p) && _currentUser.HasPermission(p));
            if (!hasDbPerm)
            {
                throw new ForbiddenException(
                    $"Active Staff user lacks required database-verified permission ({string.Join(" or ", allowedPermissions)}).");
            }
        }

        return (dbUser.Id, isSuperAdmin);
    }

    /// <summary>
    /// A05: Validates that a dedicated or owning Client is active and has active ClientFacilityAccess for the target facility.
    /// </summary>
    public async Task ValidateClientFacilityBindingAsync(
        Guid facilityId,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        if (clientId == Guid.Empty)
        {
            throw new ValidationException("ClientId", "ClientId cannot be empty.");
        }

        var client = await _db.Clients
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clientId && !c.IsDeleted, cancellationToken);

        if (client == null || client.Status != ClientStatus.Active)
        {
            throw new ValidationException("ClientId", $"Client '{clientId}' does not exist or is inactive.");
        }

        var hasAccess = await _db.ClientFacilityAccesses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.ClientId == clientId && a.FacilityId == facilityId && a.Status == FacilityAccessStatus.Active, cancellationToken);

        if (!hasAccess)
        {
            throw new ValidationException(
                "ClientId",
                $"Client '{client.AccountCode}' does not have active facility access to facility '{facilityId}'.");
        }
    }

    public async Task<string?> ValidateOptionalCurrencyAsync(string? currencyCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return null;
        }

        string normalized;
        try
        {
            Iso4217Currencies.Validate(currencyCode, nameof(currencyCode));
            normalized = currencyCode.Trim().ToUpperInvariant();
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException("CurrencyCode", ex.Message);
        }

        if (_db.IsRelationalDatabase || await _db.Currencies.AsNoTracking().AnyAsync(cancellationToken))
        {
            var isActive = await _db.Currencies
                .AsNoTracking()
                .AnyAsync(c => c.CurrencyCode == normalized && c.IsActive, cancellationToken);

            if (!isActive)
            {
                throw new ValidationException("CurrencyCode", $"Currency '{normalized}' is not an active CurrencyMaster record.");
            }
        }

        return normalized;
    }

    private async Task<(VerifiedStaffSnapshot User, bool IsSuperAdmin, HashSet<string> Permissions)> LoadAndVerifyStaffAsync(
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated ||
            !_currentUser.UserId.HasValue ||
            _currentUser.UserId.Value == Guid.Empty)
        {
            throw new UnauthorizedException("Authentication is required for warehouse master operations.");
        }

        if (_currentUser.UserType != UserType.Staff || !_currentUser.IsStaff)
        {
            throw new ForbiddenException("Warehouse master operations are restricted to authenticated platform Staff.");
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

        bool isDbSuperAdmin = dbRoleNames.Contains(SystemRoles.SuperAdmin);
        var dbPermissions = dbUser.Roles
            .SelectMany(r => r.Permissions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (dbUser, isDbSuperAdmin, dbPermissions);
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
