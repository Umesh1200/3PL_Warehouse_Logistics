using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Outbound;
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

    /// <summary>
    /// Enforces authoritative courier delivery execution access for a package:
    /// - Always rejects UserType.Client (including ClientAdmin) with 403 Forbidden.
    /// - Enforces Permissions.Dispatch.ExecuteDelivery for Staff and Driver.
    /// - Confines non-SuperAdmin Staff to their single assigned facility (never honoring analytics:multi-facility:view).
    /// - Confines UserType.Driver strictly to Dispatched shipments where Shipment.AssignedDriverUserId == caller
    ///   and the active immutable ShipmentCustodyTransfer is bound to the caller's user identity.
    /// </summary>
    public async Task ValidateCourierPackageAccessAsync(Package package, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException("Authentication is required for courier delivery operations.");
        }

        if (_currentUser.UserType == UserType.Client)
        {
            throw new ForbiddenException("Client accounts cannot execute courier delivery or custody operations.");
        }

        if (IsVerifiedPlatformSuperAdmin)
        {
            return;
        }

        if (!_currentUser.HasPermission(Permissions.Dispatch.ExecuteDelivery))
        {
            throw new ForbiddenException("Missing 'dispatch:execute-delivery' permission for courier delivery execution.");
        }

        if (_currentUser.UserType == UserType.Staff)
        {
            if (package.FacilityId == Guid.Empty)
            {
                throw new ForbiddenException("A valid facility scope is required for courier delivery operations.");
            }

            if (!_currentUser.FacilityId.HasValue || _currentUser.FacilityId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Staff member has no assigned facility for courier delivery execution.");
            }

            if (_currentUser.FacilityId.Value != package.FacilityId)
            {
                throw new ForbiddenException($"Staff member is not authorized for package facility '{package.FacilityId}'.");
            }

            if (_currentUser.RequestedFacilityId.HasValue &&
                _currentUser.RequestedFacilityId.Value != _currentUser.FacilityId.Value)
            {
                throw new ForbiddenException(
                    $"Requested facility header '{_currentUser.RequestedFacilityId.Value}' does not match assigned facility.");
            }

            return;
        }

        if (_currentUser.UserType == UserType.Driver)
        {
            if (!_currentUser.UserId.HasValue || _currentUser.UserId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Authenticated driver user identity is required.");
            }

            if (!package.ShipmentId.HasValue || package.ShipmentId.Value == Guid.Empty)
            {
                throw new ForbiddenException($"Package '{package.PackageNumber}' is not assigned to a dispatched shipment.");
            }

            var driverUserId = _currentUser.UserId.Value;

            var shipment = await _db.Shipments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.Id == package.ShipmentId.Value && !s.IsDeleted)
                .Select(s => new
                {
                    s.Id,
                    s.ClientId,
                    s.FacilityId,
                    s.Status,
                    s.AssignedDriverUserId
                })
                .FirstOrDefaultAsync(ct);

            if (shipment == null ||
                shipment.ClientId != package.ClientId ||
                shipment.FacilityId != package.FacilityId)
            {
                throw new ForbiddenException($"Driver custody verification failed for package '{package.PackageNumber}'.");
            }

            if (shipment.Status != ShipmentStatus.Dispatched)
            {
                throw new ForbiddenException(
                    $"Courier execution requires shipment status 'Dispatched' (current: '{shipment.Status}').");
            }

            if (!shipment.AssignedDriverUserId.HasValue || shipment.AssignedDriverUserId.Value != driverUserId)
            {
                throw new ForbiddenException(
                    $"Driver '{driverUserId}' is not currently assigned to shipment '{shipment.Id}'.");
            }

            var latestCustodyTransfer = await _db.ShipmentCustodyTransfers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.ShipmentId == shipment.Id &&
                            t.ClientId == shipment.ClientId &&
                            t.FacilityId == shipment.FacilityId)
                .OrderByDescending(t => t.TransferredAtUtc)
                .ThenByDescending(t => t.Id)
                .Select(t => new { t.Id, t.AssignedDriverUserId })
                .FirstOrDefaultAsync(ct);

            if (latestCustodyTransfer == null ||
                !latestCustodyTransfer.AssignedDriverUserId.HasValue ||
                latestCustodyTransfer.AssignedDriverUserId.Value != driverUserId)
            {
                throw new ForbiddenException(
                    $"Driver '{driverUserId}' does not hold verified active custody transfer for shipment '{shipment.Id}'.");
            }

            return;
        }

        throw new ForbiddenException("User identity type is not authorized for courier delivery operations.");
    }

    /// <summary>
    /// Loads the target package (rejecting unauthenticated callers with 401 and Client identities with 403)
    /// and executes <see cref="ValidateCourierPackageAccessAsync"/> before any downstream side effects.
    /// </summary>
    public async Task<Package> ValidateCourierPackageAccessByIdAsync(Guid packageId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException("Authentication is required for courier delivery operations.");
        }

        if (_currentUser.UserType == UserType.Client)
        {
            throw new ForbiddenException("Client accounts cannot execute courier delivery or custody operations.");
        }

        if (packageId == Guid.Empty)
        {
            throw new Warehouse3PL.Domain.Exceptions.ValidationException(nameof(packageId), "Package ID is required.");
        }

        var package = await _db.Packages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(p => p.Shipment)
            .FirstOrDefaultAsync(p => p.Id == packageId && !p.IsDeleted, ct)
            ?? throw new EntityNotFoundException(nameof(Package), packageId);

        await ValidateCourierPackageAccessAsync(package, ct);
        return package;
    }
}

public sealed record OperationalQueryScope(Guid? ClientId, Guid? FacilityId, List<Guid>? AuthorizedFacilityIds);
