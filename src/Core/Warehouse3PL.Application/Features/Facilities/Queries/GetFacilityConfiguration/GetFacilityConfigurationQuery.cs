using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Facilities.Queries.GetFacilityConfiguration;

public record GetFacilityConfigurationQuery(Guid FacilityId) : IRequest<FacilityConfigurationDto?>;

/// <summary>
/// Returns privileged facility configuration (<c>OperatingHoursJson</c>, <c>ConfigurationJson</c>).
/// Fails closed for Clients, Drivers, unauthenticated callers, Staff lacking <c>facilities:manage</c>,
/// and non-SuperAdmin Staff targeting a facility outside their assigned boundary.
/// </summary>
public class GetFacilityConfigurationQueryHandler : IRequestHandler<GetFacilityConfigurationQuery, FacilityConfigurationDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetFacilityConfigurationQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<FacilityConfigurationDto?> Handle(GetFacilityConfigurationQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedException("Authentication is required to access facility configuration.");
        }

        // Clients and Drivers are never allowed to read privileged facility configuration payloads.
        if (_currentUser.UserType != UserType.Staff)
        {
            throw new ForbiddenException("Privileged facility configuration is restricted to authorized warehouse staff.");
        }

        bool isVerifiedPlatformSuperAdmin = _currentUser.IsSuperAdmin && _currentUser.UserType == UserType.Staff;
        if (!isVerifiedPlatformSuperAdmin && !_currentUser.HasPermission(Permissions.Facilities.Manage))
        {
            throw new ForbiddenException($"Permission '{Permissions.Facilities.Manage}' is required to view privileged facility configuration.");
        }

        if (!isVerifiedPlatformSuperAdmin &&
            (!_currentUser.FacilityId.HasValue || _currentUser.FacilityId.Value != request.FacilityId))
        {
            throw new ForbiddenException($"Staff member is not authorized to view configuration for facility '{request.FacilityId}'.");
        }

        return await _context.Facilities
            .AsNoTracking()
            .Where(f => f.Id == request.FacilityId && !f.IsDeleted && f.IsActive)
            .Select(FacilityProjections.ToConfigurationDto)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
