using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record ShipmentStagingLocationDto
{
    public Guid Id { get; init; }
    public string LocationCode { get; init; } = string.Empty;
    public string Barcode { get; init; } = string.Empty;
    public LocationType LocationType { get; init; }
    public string AisleCode { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public record EligibleShipmentDriverDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public Guid? AssignedFacilityId { get; init; }
    public Guid? ClientId { get; init; }
}

public record GetShipmentStagingLocationsQuery(Guid? FacilityId = null)
    : IRequest<IReadOnlyList<ShipmentStagingLocationDto>>;

public class GetShipmentStagingLocationsQueryHandler
    : IRequestHandler<GetShipmentStagingLocationsQuery, IReadOnlyList<ShipmentStagingLocationDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetShipmentStagingLocationsQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<IReadOnlyList<ShipmentStagingLocationDto>> Handle(
        GetShipmentStagingLocationsQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetShipmentStagingLocationsQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            requestedClientId: null,
            requestedFacilityId: query.FacilityId == Guid.Empty ? null : query.FacilityId,
            allowCrossFacilityRead: false,
            allowClient: false,
            ct: cancellationToken);

        var dbQuery = _context.Locations
            .AsNoTracking()
            .Where(l => !l.IsDeleted &&
                        l.IsActive &&
                        (l.LocationType == LocationType.StagingLane || l.LocationType == LocationType.DockDoor));

        if (scope.FacilityId.HasValue)
        {
            var facilityId = scope.FacilityId.Value;
            dbQuery = dbQuery.Where(l => l.FacilityId == facilityId);
        }

        return await dbQuery
            .OrderBy(l => l.LocationType)
            .ThenBy(l => l.LocationCode)
            .Select(l => new ShipmentStagingLocationDto
            {
                Id = l.Id,
                LocationCode = l.LocationCode,
                Barcode = l.Barcode,
                LocationType = l.LocationType,
                AisleCode = l.AisleCode ?? string.Empty,
                IsActive = l.IsActive
            })
            .ToListAsync(cancellationToken);
    }
}

public record GetEligibleShipmentDriversQuery(
    Guid? FacilityId = null,
    Guid? ClientId = null) : IRequest<IReadOnlyList<EligibleShipmentDriverDto>>;

public class GetEligibleShipmentDriversQueryHandler
    : IRequestHandler<GetEligibleShipmentDriversQuery, IReadOnlyList<EligibleShipmentDriverDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetEligibleShipmentDriversQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<IReadOnlyList<EligibleShipmentDriverDto>> Handle(
        GetEligibleShipmentDriversQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetEligibleShipmentDriversQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            requestedClientId: query.ClientId == Guid.Empty ? null : query.ClientId,
            requestedFacilityId: query.FacilityId == Guid.Empty ? null : query.FacilityId,
            allowCrossFacilityRead: false,
            allowClient: false,
            ct: cancellationToken);

        var dbQuery = _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => !u.IsDeleted &&
                        u.Status == UserStatus.Active &&
                        u.UserType == UserType.Driver &&
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == SystemRoles.Driver));

        if (scope.FacilityId.HasValue)
        {
            var facilityId = scope.FacilityId.Value;
            dbQuery = dbQuery.Where(u => !u.AssignedFacilityId.HasValue || u.AssignedFacilityId.Value == facilityId);
        }

        if (scope.ClientId.HasValue)
        {
            var clientId = scope.ClientId.Value;
            dbQuery = dbQuery.Where(u => !u.ClientId.HasValue || u.ClientId.Value == clientId);
        }

        return await dbQuery
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new EligibleShipmentDriverDto
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                AssignedFacilityId = u.AssignedFacilityId,
                ClientId = u.ClientId
            })
            .ToListAsync(cancellationToken);
    }
}
