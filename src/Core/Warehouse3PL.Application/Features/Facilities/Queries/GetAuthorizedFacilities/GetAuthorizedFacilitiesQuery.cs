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
        var query = _context.Facilities
            .AsNoTracking()
            .Where(f => !f.IsDeleted && f.IsActive);

        // If client user with specific tenant ID, restrict to facilities authorized for that tenant
        if (!_currentUser.IsSuperAdmin && !_currentUser.IsStaff && _currentUser.TenantId.HasValue)
        {
            var tenantId = _currentUser.TenantId.Value;
            var authorizedFacilityIds = _context.ClientFacilityAccesses
                .AsNoTracking()
                .Where(a => a.ClientId == tenantId && a.Status == FacilityAccessStatus.Active)
                .Select(a => a.FacilityId);

            query = query.Where(f => authorizedFacilityIds.Contains(f.Id));
        }

        var facilities = await query
            .OrderBy(f => f.FacilityCode)
            .Select(f => new FacilityDto
            {
                Id = f.Id,
                FacilityCode = f.FacilityCode,
                Name = f.Name,
                AddressLine1 = f.AddressLine1,
                City = f.City,
                Timezone = f.Timezone,
                IsActive = f.IsActive
            })
            .ToListAsync(cancellationToken);

        return facilities;
    }
}
