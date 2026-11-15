using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.Locations.Queries.GetLocationTree;

public record GetLocationTreeQuery(Guid FacilityId, Guid? ZoneId = null) : IRequest<List<LocationTreeNodeDto>>;

public class GetLocationTreeQueryHandler : IRequestHandler<GetLocationTreeQuery, List<LocationTreeNodeDto>>
{
    private readonly IApplicationDbContext _context;

    public GetLocationTreeQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<LocationTreeNodeDto>> Handle(GetLocationTreeQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Locations
            .AsNoTracking()
            .Where(l => l.FacilityId == request.FacilityId && !l.IsDeleted);

        if (request.ZoneId.HasValue)
        {
            query = query.Where(l => l.ZoneId == request.ZoneId.Value);
        }

        var allLocations = await query.ToListAsync(cancellationToken);

        // Convert to tree nodes
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
                Barcode = l.Barcode,
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
                IsActive = l.IsActive,
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

