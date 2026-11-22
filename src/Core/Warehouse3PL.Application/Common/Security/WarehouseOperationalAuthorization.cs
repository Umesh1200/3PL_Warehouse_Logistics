using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Common.Security;

/// <summary>
/// Operational tenant and facility scope authorization helper for warehouse inventory,
/// receiving, and putaway operations. Enforces trusted tenant and facility confinement,
/// preventing spoofed headers, foreign tenant access, and unauthorized facility access.
/// </summary>
public class WarehouseOperationalAuthorization
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public WarehouseOperationalAuthorization(IApplicationDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private bool IsVerifiedPlatformSuperAdmin =>
        _currentUser.IsSuperAdmin && _currentUser.UserType == UserType.Staff;

    /// <summary>
    /// Validates and resolves the operational tenant and facility scope for inventory and task queries.
    /// </summary>
    public async Task<OperationalQueryScope> ResolveQueryScopeAsync(
        Guid? requestedClientId,
        Guid? requestedFacilityId,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException("Authentication is required for warehouse operations.");
        }

        var targetFacility = requestedFacilityId ?? _currentUser.RequestedFacilityId;

        // 1. SuperAdmin: can access any client and any facility
        if (IsVerifiedPlatformSuperAdmin)
        {
            return new OperationalQueryScope(requestedClientId, targetFacility, null);
        }

        // 2. Client User: strictly locked to authenticated tenant
        if (_currentUser.UserType == UserType.Client)
        {
            var tenantId = _currentUser.TenantId;
            if (!tenantId.HasValue || tenantId == Guid.Empty)
            {
                throw new ForbiddenException("Authenticated client identity is required.");
            }

            if (requestedClientId.HasValue && requestedClientId.Value != tenantId.Value)
            {
                throw new ForbiddenException("Cross-client operational access is forbidden.");
            }

            var authorizedFacilityIds = await _db.ClientFacilityAccesses
                .AsNoTracking()
                .Where(a => a.ClientId == tenantId.Value && a.Status == FacilityAccessStatus.Active)
                .Select(a => a.FacilityId)
                .ToListAsync(ct);

            if (authorizedFacilityIds.Count == 0)
            {
                throw new ForbiddenException("Client has no authorized active facilities.");
            }

            if (targetFacility.HasValue)
            {
                if (!authorizedFacilityIds.Contains(targetFacility.Value))
                {
                    throw new ForbiddenException($"Client does not have access to requested facility '{targetFacility.Value}'.");
                }

                return new OperationalQueryScope(tenantId, targetFacility.Value, authorizedFacilityIds);
            }

            // Omitted facility parameter: scope to assigned facility if set & authorized, else all client facilities
            if (_currentUser.FacilityId.HasValue &&
                _currentUser.FacilityId.Value != Guid.Empty &&
                authorizedFacilityIds.Contains(_currentUser.FacilityId.Value))
            {
                return new OperationalQueryScope(tenantId, _currentUser.FacilityId.Value, authorizedFacilityIds);
            }

            return new OperationalQueryScope(tenantId, null, authorizedFacilityIds);
        }

        // 3. Staff User
        if (_currentUser.IsStaff)
        {
            var hasCrossFacility = _currentUser.HasPermission(Permissions.Analytics.MultiFacilityView) ||
                                   _currentUser.HasPermission("Analytics:CrossFacility") ||
                                   _currentUser.HasPermission("Shipments:CrossFacility") ||
                                   _currentUser.HasPermission("Financial:CrossFacility") ||
                                   _currentUser.HasPermission("Financials:CrossFacility");

            if (hasCrossFacility)
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
                throw new ForbiddenException($"Staff member is not authorized for facility '{targetFacility.Value}'. Assigned facility is '{staffFacility}'.");
            }

            return new OperationalQueryScope(requestedClientId, staffFacility, null);
        }

        // 4. Other user types (Driver, unassigned non-staff) fail closed
        throw new ForbiddenException("Unauthorized user role for warehouse operations.");
    }

    /// <summary>
    /// Validates that the current user has access to a specific facility.
    /// </summary>
    public async Task ValidateFacilityAccessAsync(Guid facilityId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException("Authentication is required.");
        }

        // 1. SuperAdmin: can access any client and any facility
        if (IsVerifiedPlatformSuperAdmin)
        {
            return;
        }

        if (_currentUser.UserType == UserType.Client)
        {
            var tenantId = _currentUser.TenantId;
            if (!tenantId.HasValue || tenantId == Guid.Empty)
            {
                throw new ForbiddenException("Authenticated client identity is required.");
            }

            var hasAccess = await _db.ClientFacilityAccesses
                .AsNoTracking()
                .AnyAsync(a => a.ClientId == tenantId.Value && a.FacilityId == facilityId && a.Status == FacilityAccessStatus.Active, ct);

            if (!hasAccess)
            {
                throw new ForbiddenException($"Client does not have access to facility '{facilityId}'.");
            }

            return;
        }

        if (_currentUser.IsStaff)
        {
            var hasCrossFacility = _currentUser.HasPermission(Permissions.Analytics.MultiFacilityView) ||
                                   _currentUser.HasPermission("Analytics:CrossFacility") ||
                                   _currentUser.HasPermission("Shipments:CrossFacility") ||
                                   _currentUser.HasPermission("Financial:CrossFacility") ||
                                   _currentUser.HasPermission("Financials:CrossFacility");

            if (hasCrossFacility)
            {
                return;
            }

            if (!_currentUser.FacilityId.HasValue || _currentUser.FacilityId.Value != facilityId)
            {
                throw new ForbiddenException($"Staff member is not authorized for facility '{facilityId}'.");
            }

            return;
        }

        throw new ForbiddenException("Unauthorized user role for facility operations.");
    }

    /// <summary>
    /// Validates a concrete client/facility pair used by warehouse mutations and detail queries.
    /// Staff are confined to their entitled facility; client identities are additionally confined
    /// to their trusted tenant, irrespective of caller-supplied request values.
    /// </summary>
    public async Task ValidateClientFacilityAccessAsync(
        Guid clientId,
        Guid facilityId,
        CancellationToken ct = default)
    {
        if (clientId == Guid.Empty || facilityId == Guid.Empty)
        {
            throw new ForbiddenException("A valid client and facility scope is required.");
        }

        await ValidateFacilityAccessAsync(facilityId, ct);

        if (_currentUser.UserType == UserType.Client)
        {
            var tenantId = _currentUser.TenantId;
            if (!tenantId.HasValue || tenantId == Guid.Empty || tenantId.Value != clientId)
            {
                throw new ForbiddenException("Cross-client operational access is forbidden.");
            }
        }
    }

    /// <summary>
    /// Validates tenant ownership and facility authorization for a receiving session.
    /// </summary>
    public async Task ValidateReceivingSessionAccessAsync(ReceivingSession session, CancellationToken ct = default)
    {
        await ValidateFacilityAccessAsync(session.FacilityId, ct);

        if (!_currentUser.IsStaff)
        {
            var tenantId = _currentUser.TenantId;
            if (!tenantId.HasValue || tenantId == Guid.Empty)
            {
                throw new ForbiddenException("Authenticated client identity is required.");
            }

            if (session.ClientId.HasValue && session.ClientId.Value != tenantId.Value)
            {
                throw new ForbiddenException("Client does not have access to this receiving session.");
            }

            if (!session.ClientId.HasValue)
            {
                throw new ForbiddenException("Client users cannot access unassigned blind receiving sessions.");
            }
        }
    }
}

public sealed record OperationalQueryScope(Guid? ClientId, Guid? FacilityId, List<Guid>? AuthorizedFacilityIds);
