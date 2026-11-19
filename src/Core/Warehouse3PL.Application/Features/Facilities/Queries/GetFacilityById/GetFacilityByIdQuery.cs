using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Facilities.Queries.GetFacilityById;

public record GetFacilityByIdQuery(Guid FacilityId) : IRequest<FacilityDto?>;

public class GetFacilityByIdQueryHandler : IRequestHandler<GetFacilityByIdQuery, FacilityDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetFacilityByIdQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<FacilityDto?> Handle(GetFacilityByIdQuery request, CancellationToken cancellationToken)
    {
        // 1. Explicitly allow authorized staff and superadmin cases
        if (_currentUser.IsSuperAdmin || _currentUser.IsStaff)
        {
            var facility = await _context.Facilities
                .AsNoTracking()
                .Where(f => f.Id == request.FacilityId && !f.IsDeleted && f.IsActive)
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
                .FirstOrDefaultAsync(cancellationToken);

            return facility;
        }

        // 2. Client callers must have a valid TenantId and active access to this specific facility
        if (_currentUser.UserType == UserType.Client && _currentUser.TenantId.HasValue)
        {
            var tenantId = _currentUser.TenantId.Value;
            var hasAccess = await _context.ClientFacilityAccesses
                .AsNoTracking()
                .AnyAsync(a => a.ClientId == tenantId &&
                               a.FacilityId == request.FacilityId &&
                               a.Status == FacilityAccessStatus.Active,
                          cancellationToken);

            if (!hasAccess)
            {
                return null;
            }

            var facility = await _context.Facilities
                .AsNoTracking()
                .Where(f => f.Id == request.FacilityId && !f.IsDeleted && f.IsActive)
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
                .FirstOrDefaultAsync(cancellationToken);

            return facility;
        }

        // 3. Deny missing-tenant callers, unsupported nonstaff identities, or unauthenticated callers
        return null;
    }
}
