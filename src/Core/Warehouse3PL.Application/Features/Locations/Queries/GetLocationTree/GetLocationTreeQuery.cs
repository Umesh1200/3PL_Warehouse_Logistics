using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;

namespace Warehouse3PL.Application.Features.Locations.Queries.GetLocationTree;

public record GetLocationTreeQuery(Guid? FacilityId, Guid? ZoneId = null) : IRequest<List<LocationTreeNodeDto>>;

public class GetLocationTreeQueryHandler : IRequestHandler<GetLocationTreeQuery, List<LocationTreeNodeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseMasterAuthorization _auth;

    public GetLocationTreeQueryHandler(IApplicationDbContext context, WarehouseMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<List<LocationTreeNodeDto>> Handle(GetLocationTreeQuery request, CancellationToken cancellationToken)
    {
        var (_, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsView,
            Permissions.Facilities.View);

        var query = _context.Locations
            .AsNoTracking()
            .Where(l => l.FacilityId == facilityId && !l.IsDeleted);

        if (request.ZoneId.HasValue)
        {
            query = query.Where(l => l.ZoneId == request.ZoneId.Value);
        }

        var allLocations = await query
            .OrderBy(l => l.HierarchyLevel)
            .ThenBy(l => l.LocationCode)
            .ToListAsync(cancellationToken);

        var nodeMap = allLocations.ToDictionary(
            l => l.Id,
            l => new LocationTreeNodeDto
            {
                Id = l.Id,
                FacilityId = l.FacilityId,
                ZoneId = l.ZoneId,
                ParentLocationId = l.ParentLocationId,
                LocationType = l.LocationType,
                LocationCode = l.LocationCode,
                LocationName = l.LocationName,
                Barcode = l.Barcode,
                HierarchyLevel = l.HierarchyLevel,
                HierarchyPath = l.HierarchyPath,
                Remarks = l.Remarks,
                ConfigRevision = l.ConfigRevision,
                AisleCode = l.AisleCode,
                RackCode = l.RackCode,
                ShelfLevel = l.ShelfLevel,
                BinNumber = l.BinNumber,
                PickSequence = l.PickSequence,
                LengthCm = l.LengthCm,
                WidthCm = l.WidthCm,
                HeightCm = l.HeightCm,
                MaxVolumeCm3 = l.MaxVolumeCm3,
                MaxWeightKg = l.MaxWeightKg,
                Status = l.Status,
                IsActive = l.IsActive,
                IsStorable = l.IsStorable,
                IsPickable = l.IsPickable,
                IsReceiving = l.IsReceiving,
                IsDamaged = l.IsDamaged,
                IsLocked = l.IsLocked
            });

        var roots = new List<LocationTreeNodeDto>();

        foreach (var node in nodeMap.Values)
        {
            if (node.ParentLocationId.HasValue && nodeMap.TryGetValue(node.ParentLocationId.Value, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return roots;
    }
}
