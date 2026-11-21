using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Putaway.Tasks.Queries.GetPutawayTasks;

public record GetPutawayTasksQuery : PagedQuery, IRequest<PagedResult<PutawayTaskDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public PutawayTaskStatus? Status { get; init; }
    public List<PutawayTaskStatus>? Statuses { get; init; }
    public Guid? ItemId { get; init; }
}

public class GetPutawayTasksQueryHandler : IRequestHandler<GetPutawayTasksQuery, PagedResult<PutawayTaskDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public GetPutawayTasksQueryHandler(
        IApplicationDbContext context,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _operationalAuth = operationalAuth;
    }

    public async Task<PagedResult<PutawayTaskDto>> Handle(GetPutawayTasksQuery request, CancellationToken cancellationToken)
    {
        var scope = await _operationalAuth.ResolveQueryScopeAsync(request.ClientId, request.FacilityId, cancellationToken);

        var query = _context.PutawayTasks
            .AsNoTracking()
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.SourceLocation)
            .Include(t => t.SuggestedLocation)
            .Include(t => t.ActualLocation)
            .AsQueryable();

        if (scope.ClientId.HasValue)
        {
            query = query.Where(t => t.ClientId == scope.ClientId.Value);
        }

        if (scope.FacilityId.HasValue)
        {
            query = query.Where(t => t.FacilityId == scope.FacilityId.Value);
        }
        else if (scope.AuthorizedFacilityIds != null)
        {
            query = query.Where(t => scope.AuthorizedFacilityIds.Contains(t.FacilityId));
        }

        if (request.Statuses != null && request.Statuses.Count > 0)
        {
            query = query.Where(t => request.Statuses.Contains(t.Status));
        }
        else if (request.Status.HasValue)
        {
            query = query.Where(t => t.Status == request.Status.Value);
        }

        if (request.ItemId.HasValue)
        {
            query = query.Where(t => t.ItemId == request.ItemId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(t =>
                (t.LpnCode != null && t.LpnCode.ToLower().Contains(term)) ||
                t.Item.Sku.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAtUtc)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new PutawayTaskDto
            {
                Id = t.Id,
                FacilityId = t.FacilityId,
                ClientId = t.ClientId,
                ItemId = t.ItemId,
                Sku = t.Item.Sku,
                ItemDescription = t.Item.Description,
                LotId = t.LotId,
                LotNumber = t.Lot != null ? t.Lot.LotNumber : null,
                LpnId = t.LpnId,
                LpnCode = t.LpnCode,
                Quantity = t.Quantity,
                SourceLocationId = t.SourceLocationId,
                SourceLocationCode = t.SourceLocation.LocationCode,
                SuggestedLocationId = t.SuggestedLocationId,
                SuggestedLocationCode = t.SuggestedLocation.LocationCode,
                SuggestedLocationBarcode = t.SuggestedLocation.Barcode,
                ActualLocationId = t.ActualLocationId,
                ActualLocationCode = t.ActualLocation != null ? t.ActualLocation.LocationCode : null,
                Status = t.Status,
                Priority = t.Priority,
                AssignedUserId = t.AssignedUserId,
                CreatedAtUtc = t.CreatedAtUtc,
                CompletedAtUtc = t.CompletedAtUtc,
                ExceptionReason = t.ExceptionReason,
                Version = t.Version
            })
            .ToListAsync(cancellationToken);

        return PagedResult<PutawayTaskDto>.Create(items, totalCount, request.PageNumber, request.PageSize);
    }
}
