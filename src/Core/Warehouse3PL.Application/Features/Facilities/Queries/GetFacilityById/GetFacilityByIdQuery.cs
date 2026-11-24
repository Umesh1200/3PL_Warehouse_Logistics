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
        // 1. Platform SuperAdmin or staff callers with cross-facility read scope
        if (FacilityProjections.IsCrossFacilityStaff(_currentUser))
        {
            return await _context.Facilities
                .AsNoTracking()
                .Where(f => f.Id == request.FacilityId && !f.IsDeleted && f.IsActive)
                .Select(FacilityProjections.ToSummaryDto)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // 2. Staff without cross-facility permissions: strictly confined to assigned facility
        if (_currentUser.IsStaff)
        {
            if (_currentUser.FacilityId.HasValue && _currentUser.FacilityId.Value == request.FacilityId)
            {
                return await _context.Facilities
                    .AsNoTracking()
                    .Where(f => f.Id == request.FacilityId && !f.IsDeleted && f.IsActive)
                    .Select(FacilityProjections.ToSummaryDto)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            return null;
        }

        // 3. Client callers must have a valid TenantId and active access to this specific facility
        if (_currentUser.UserType == UserType.Client && _currentUser.TenantId.HasValue)
        {
            if (_currentUser.FacilityId.HasValue && _currentUser.FacilityId.Value != request.FacilityId)
            {
                return null;
            }

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

            return await _context.Facilities
                .AsNoTracking()
                .Where(f => f.Id == request.FacilityId && !f.IsDeleted && f.IsActive)
                .Select(FacilityProjections.ToSummaryDto)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // 4. Fail closed for Drivers, missing-tenant callers, or unauthenticated callers
        return null;
    }
}
