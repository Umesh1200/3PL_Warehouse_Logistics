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
        // 1. Explicitly allow authorized staff and superadmin cases (global facility access)
        if (_currentUser.IsSuperAdmin || _currentUser.IsStaff)
        {
            return await _context.Facilities
                .AsNoTracking()
                .Where(f => !f.IsDeleted && f.IsActive)
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
        }

        // 2. Client callers must have a valid TenantId; filter to active authorized facilities for that tenant
        if (_currentUser.UserType == UserType.Client && _currentUser.TenantId.HasValue)
        {
            var tenantId = _currentUser.TenantId.Value;
            var authorizedFacilityIds = _context.ClientFacilityAccesses
                .AsNoTracking()
                .Where(a => a.ClientId == tenantId && a.Status == FacilityAccessStatus.Active)
                .Select(a => a.FacilityId);

            return await _context.Facilities
                .AsNoTracking()
                .Where(f => !f.IsDeleted && f.IsActive && authorizedFacilityIds.Contains(f.Id))
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
        }

        // 3. Deny missing-tenant callers, unsupported nonstaff identities (e.g. Driver), or unauthenticated callers (Fail closed)
        return [];
    }
}
