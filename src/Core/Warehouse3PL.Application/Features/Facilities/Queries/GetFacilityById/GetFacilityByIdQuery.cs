using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
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
        // 1. Explicitly allow SuperAdmin or staff callers with multi-facility view (global facility access)
        if (_currentUser.IsSuperAdmin ||
            _currentUser.HasPermission(Permissions.Analytics.MultiFacilityView) ||
            _currentUser.HasPermission("Analytics:CrossFacility") ||
            _currentUser.HasPermission("Shipments:CrossFacility") ||
            _currentUser.HasPermission("Financial:CrossFacility") ||
            _currentUser.HasPermission("Financials:CrossFacility"))
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

        // 2. Staff without cross-facility permissions: strictly confined to assigned facility
        if (_currentUser.IsStaff)
        {
            if (_currentUser.FacilityId.HasValue && _currentUser.FacilityId.Value == request.FacilityId)
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

            // Unassigned staff or staff attempting to access another facility fail closed
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

        // 4. Deny missing-tenant callers, unsupported nonstaff identities, or unauthenticated callers
        return null;
    }
}
