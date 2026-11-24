using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Facilities.Queries.GetAuthorizedFacilities;

public record GetAuthorizedFacilitiesQuery : IRequest<List<FacilityDto>>;

public class GetAuthorizedFacilitiesQueryHandler : IRequestHandler<GetAuthorizedFacilitiesQuery, List<FacilityDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetAuthorizedFacilitiesQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<List<FacilityDto>> Handle(GetAuthorizedFacilitiesQuery request, CancellationToken cancellationToken)
    {
        // 1. Platform SuperAdmin or staff callers with cross-facility read scope
        if (FacilityProjections.IsCrossFacilityStaff(_currentUser))
        {
            return await _context.Facilities
                .AsNoTracking()
                .Where(f => !f.IsDeleted && f.IsActive)
                .OrderBy(f => f.FacilityCode)
                .Select(FacilityProjections.ToSummaryDto)
                .ToListAsync(cancellationToken);
        }

        // 2. Staff without cross-facility permissions: confined to assigned facility if present
        if (_currentUser.IsStaff)
        {
            if (_currentUser.FacilityId.HasValue && _currentUser.FacilityId.Value != Guid.Empty)
            {
                var assignedId = _currentUser.FacilityId.Value;
                return await _context.Facilities
                    .AsNoTracking()
                    .Where(f => !f.IsDeleted && f.IsActive && f.Id == assignedId)
                    .OrderBy(f => f.FacilityCode)
                    .Select(FacilityProjections.ToSummaryDto)
                    .ToListAsync(cancellationToken);
            }

            return [];
        }

        // 3. Client callers must have a valid TenantId; filter to active authorized facilities for that tenant
        if (_currentUser.UserType == UserType.Client && _currentUser.TenantId.HasValue)
        {
            var tenantId = _currentUser.TenantId.Value;
            var authorizedFacilityIds = _context.ClientFacilityAccesses
                .AsNoTracking()
                .Where(a => a.ClientId == tenantId && a.Status == FacilityAccessStatus.Active)
                .Select(a => a.FacilityId);

            if (_currentUser.FacilityId.HasValue && _currentUser.FacilityId.Value != Guid.Empty)
            {
                var assignedId = _currentUser.FacilityId.Value;
                authorizedFacilityIds = authorizedFacilityIds.Where(id => id == assignedId);
            }

            return await _context.Facilities
                .AsNoTracking()
                .Where(f => !f.IsDeleted && f.IsActive && authorizedFacilityIds.Contains(f.Id))
                .OrderBy(f => f.FacilityCode)
                .Select(FacilityProjections.ToSummaryDto)
                .ToListAsync(cancellationToken);
        }

        // 4. Fail closed for Drivers, missing-tenant callers, or unauthenticated callers
        return [];
    }
}
