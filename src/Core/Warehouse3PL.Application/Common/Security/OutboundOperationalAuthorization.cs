using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Common.Security;

/// <summary>
/// Authoritative tenant, facility, user-type, and task-ownership authorization helper
/// for Phase 7.4 Outbound, Packing, Shipment Consolidation, and Shipping workflows.
/// Keeps Phase 7.3 <see cref="WarehouseOperationalAuthorization"/> untouched while enforcing:
/// - Strict Staff-only physical execution (rejecting ClientAdmin and Driver even with legacy claims)
/// - Strict SuperAdmin semantics (only SystemRoles.SuperAdmin bypasses facility/permission scoping)
/// - Clean separation between cross-facility read views and single-facility operational mutations
/// - Approved 401/403/404 status mapping for concealed foreign-tenant resources
/// </summary>
public sealed class OutboundOperationalAuthorization
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ITenantProvider _tenantProvider;

    public OutboundOperationalAuthorization(
        IApplicationDbContext db,
        ICurrentUserContext currentUser,
        ITenantProvider tenantProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _tenantProvider = tenantProvider;
    }

    public void EnsureAuthenticated()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException("Authentication is required for outbound operations.");
        }
    }

    /// <summary>
    /// Rejects non-permitted user identity types (such as Driver) with 403 Forbidden prior to tenant-filtered DB lookups.
    /// </summary>
    public void EnsureIdentityAllowed(bool allowClient, string operationDescription)
    {
        EnsureAuthenticated();

        if (_currentUser.IsSuperAdmin || _currentUser.UserType == UserType.Staff)
        {
            return;
        }

        if (_currentUser.UserType == UserType.Client && allowClient)
        {
            return;
        }

        throw new ForbiddenException(
            $"Operation '{operationDescription}' is not permitted for user identity type.");
    }

    /// <summary>
    /// Ensures the caller is strictly authorized warehouse staff or SuperAdmin.
    /// Rejects UserType.Client (including legacy ClientAdmin claims) and UserType.Driver with 403 Forbidden.
    /// </summary>
    public void EnsureStaffOperationalIdentity(string operationDescription)
    {
        EnsureAuthenticated();

        if (_currentUser.IsSuperAdmin)
        {
            return;
        }

        if (_currentUser.UserType != UserType.Staff)
        {
            throw new ForbiddenException(
                $"Operation '{operationDescription}' is restricted to authorized warehouse staff identities.");
        }
    }

    /// <summary>
    /// Enforces single-facility confinement for operational mutations.
    /// Read-only multi-facility permissions (e.g. analytics:multi-facility:view) NEVER grant cross-facility writes.
    /// </summary>
    public void ValidateStaffMutationFacilityAccess(Guid facilityId, string operationDescription)
    {
        EnsureStaffOperationalIdentity(operationDescription);

        if (_currentUser.IsSuperAdmin)
        {
            return;
        }

        if (facilityId == Guid.Empty)
        {
            throw new ForbiddenException("A valid facility scope is required for outbound mutations.");
        }

        if (!_currentUser.FacilityId.HasValue || _currentUser.FacilityId.Value == Guid.Empty)
        {
            throw new ForbiddenException("Staff member has no assigned facility for operational mutations.");
        }

        if (_currentUser.FacilityId.Value != facilityId)
        {
            throw new ForbiddenException(
                $"Staff member is not authorized to mutate records in facility '{facilityId}'.");
        }

        if (_currentUser.RequestedFacilityId.HasValue &&
            _currentUser.RequestedFacilityId.Value != _currentUser.FacilityId.Value)
        {
            throw new ForbiddenException(
                $"Requested facility header '{_currentUser.RequestedFacilityId.Value}' does not match assigned facility.");
        }
    }

    /// <summary>
    /// Validates read access to a facility for warehouse staff.
    /// Cross-facility reads are permitted only when holding Permissions.Analytics.MultiFacilityView.
    /// </summary>
    public void ValidateStaffReadFacilityAccess(Guid facilityId, string operationDescription)
    {
        EnsureStaffOperationalIdentity(operationDescription);

        if (_currentUser.IsSuperAdmin || _currentUser.HasPermission(Permissions.Analytics.MultiFacilityView))
        {
            return;
        }

        if (!_currentUser.FacilityId.HasValue || _currentUser.FacilityId.Value == Guid.Empty)
        {
            throw new ForbiddenException("Staff member has no assigned facility and lacks multi-facility read permission.");
        }

        if (_currentUser.FacilityId.Value != facilityId)
        {
            throw new ForbiddenException($"Staff member is not authorized for facility '{facilityId}'.");
        }
    }

    /// <summary>
    /// Resolves tenant and facility filters for outbound queries supporting both Staff and Client views.
    /// </summary>
    public async Task<OperationalQueryScope> ResolveOutboundQueryScopeAsync(
        Guid? requestedClientId,
        Guid? requestedFacilityId,
        bool allowCrossFacilityRead,
        bool allowClient,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();

        var targetFacility = requestedFacilityId ?? _currentUser.RequestedFacilityId;

        if (_currentUser.IsSuperAdmin)
        {
            return new OperationalQueryScope(requestedClientId, targetFacility, null);
        }

        if (_currentUser.UserType == UserType.Client)
        {
            if (!allowClient)
            {
                throw new ForbiddenException("Client users are not authorized to perform this warehouse operation.");
            }

            var tenantId = ResolveValidatedClientTenantId(requestedClientId);
            var authorizedFacilityIds = await GetActiveClientFacilitiesAsync(tenantId, ct);

            if (targetFacility.HasValue && !authorizedFacilityIds.Contains(targetFacility.Value))
            {
                throw new ForbiddenException($"Client does not have authorized access to facility '{targetFacility.Value}'.");
            }

            return new OperationalQueryScope(tenantId, targetFacility, authorizedFacilityIds);
        }

        if (_currentUser.UserType == UserType.Staff)
        {
            if (allowCrossFacilityRead && _currentUser.HasPermission(Permissions.Analytics.MultiFacilityView))
            {
                return new OperationalQueryScope(requestedClientId, targetFacility, null);
            }

            if (!_currentUser.FacilityId.HasValue || _currentUser.FacilityId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Staff member has no assigned facility and lacks cross-facility permission.");
            }

            var staffFacility = _currentUser.FacilityId.Value;
            if (targetFacility.HasValue && targetFacility.Value != staffFacility)
            {
                throw new ForbiddenException(
                    $"Staff member is not authorized for facility '{targetFacility.Value}'. Assigned facility is '{staffFacility}'.");
            }

            return new OperationalQueryScope(requestedClientId, staffFacility, null);
        }

        throw new ForbiddenException("User identity type is not authorized for outbound operations.");
    }

    /// <summary>
    /// Validates access on a single existing outbound entity (order/package) supporting approved 404 concealment for foreign tenants.
    /// </summary>
    public async Task ValidateEntityReadOrClientMutationAsync(
        string entityName,
        Guid entityId,
        Guid entityClientId,
        Guid entityFacilityId,
        bool allowClient,
        bool isMutation,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();

        if (_currentUser.IsSuperAdmin)
        {
            return;
        }

        if (_currentUser.UserType == UserType.Client)
        {
            if (!allowClient)
            {
                throw new ForbiddenException($"Client users cannot execute '{entityName}' warehouse operations.");
            }

            var tenantId = ResolveValidatedClientTenantId(null);

            if (entityClientId != tenantId)
            {
                // Conceal existence of foreign tenant records
                throw new EntityNotFoundException(entityName, entityId);
            }

            await EnsureClientHasActiveFacilityAccessAsync(tenantId, entityFacilityId, ct);
            return;
        }

        if (_currentUser.UserType == UserType.Staff)
        {
            if (isMutation)
            {
                ValidateStaffMutationFacilityAccess(entityFacilityId, entityName);
            }
            else
            {
                ValidateStaffReadFacilityAccess(entityFacilityId, entityName);
            }
            return;
        }

        throw new ForbiddenException("User identity type is not authorized for outbound operations.");
    }

    /// <summary>
    /// Validates client facility access against active ClientFacilityAccess records.
    /// </summary>
    public async Task EnsureClientHasActiveFacilityAccessAsync(Guid clientId, Guid facilityId, CancellationToken ct = default)
    {
        var hasAccess = await _db.ClientFacilityAccesses
            .AsNoTracking()
            .AnyAsync(a => a.ClientId == clientId &&
                           a.FacilityId == facilityId &&
                           a.Status == FacilityAccessStatus.Active, ct);

        if (!hasAccess)
        {
            throw new ForbiddenException($"Client '{clientId}' does not have active authorized access to facility '{facilityId}'.");
        }
    }

    public Guid ResolveValidatedClientTenantId(Guid? requestedClientId)
    {
        var claimTenant = _currentUser.TenantId;
        if (!claimTenant.HasValue || claimTenant.Value == Guid.Empty)
        {
            throw new ForbiddenException("Authenticated client tenant identity is required.");
        }

        if (_tenantProvider.TenantId.HasValue && _tenantProvider.TenantId.Value != claimTenant.Value)
        {
            throw new ForbiddenException("Cross-client tenant header spoofing is forbidden.");
        }

        if (requestedClientId.HasValue && requestedClientId.Value != claimTenant.Value)
        {
            throw new ForbiddenException("Cross-client operational access is forbidden.");
        }

        return claimTenant.Value;
    }

    public async Task<List<Guid>> GetActiveClientFacilitiesAsync(Guid tenantId, CancellationToken ct)
    {
        var facilities = await _db.ClientFacilityAccesses
            .AsNoTracking()
            .Where(a => a.ClientId == tenantId && a.Status == FacilityAccessStatus.Active)
            .Select(a => a.FacilityId)
            .ToListAsync(ct);

        if (facilities.Count == 0)
        {
            throw new ForbiddenException("Client has no authorized active facilities.");
        }

        return facilities;
    }
}
