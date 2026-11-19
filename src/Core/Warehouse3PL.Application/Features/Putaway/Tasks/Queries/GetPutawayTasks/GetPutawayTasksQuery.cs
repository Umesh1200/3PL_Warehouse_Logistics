using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Putaway.Tasks.Queries.GetPutawayTasks;

public record GetPutawayTasksQuery : PagedQuery, IRequest<PagedResult<PutawayTaskDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public PutawayTaskStatus? Status { get; init; }
    public Guid? ItemId { get; init; }
}

public class GetPutawayTasksQueryHandler : IRequestHandler<GetPutawayTasksQuery, PagedResult<PutawayTaskDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly ITenantProvider _tenantProvider;

    public GetPutawayTasksQueryHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        ITenantProvider tenantProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _tenantProvider = tenantProvider;
    }

    public async Task<PagedResult<PutawayTaskDto>> Handle(GetPutawayTasksQuery request, CancellationToken cancellationToken)
    {
        var query = _context.PutawayTasks
            .AsNoTracking()
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.SourceLocation)
            .Include(t => t.SuggestedLocation)
            .Include(t => t.ActualLocation)
            .AsQueryable();

        if (!_currentUser.IsStaff)
        {
            var callerTenant = _currentUser.TenantId ?? _tenantProvider.TenantId;
            if (!callerTenant.HasValue || callerTenant.Value == Guid.Empty)
            {
                return PagedResult<PutawayTaskDto>.Create([], 0, request.PageNumber, request.PageSize);
            }
            query = query.Where(t => t.ClientId == callerTenant.Value);
        }
        else if (request.ClientId.HasValue)
        {
            query = query.Where(t => t.ClientId == request.ClientId.Value);
        }

        if (request.FacilityId.HasValue)
        {
            query = query.Where(t => t.FacilityId == request.FacilityId.Value);
        }

        if (request.Status.HasValue)
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
                t.Item.Sku.ToLower().Contains(term) ||
                (t.LpnCode != null && t.LpnCode.ToLower().Contains(term)) ||
                t.SourceLocation.LocationCode.ToLower().Contains(term) ||
                t.SuggestedLocation.LocationCode.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var tasks = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = tasks.Select(t => new PutawayTaskDto
        {
            Id = t.Id,
            ClientId = t.ClientId,
            FacilityId = t.FacilityId,
            ItemId = t.ItemId,
            Sku = t.Item?.Sku ?? string.Empty,
            ItemDescription = t.Item?.Description ?? string.Empty,
            LotId = t.LotId,
            LotNumber = t.Lot?.LotNumber,
            LpnId = t.LpnId,
            LpnCode = t.LpnCode,
            SourceLocationId = t.SourceLocationId,
            SourceLocationCode = t.SourceLocation?.LocationCode ?? string.Empty,
            SuggestedLocationId = t.SuggestedLocationId,
            SuggestedLocationCode = t.SuggestedLocation?.LocationCode ?? string.Empty,
            SuggestedLocationBarcode = t.SuggestedLocation?.Barcode ?? string.Empty,
            ActualLocationId = t.ActualLocationId,
            ActualLocationCode = t.ActualLocation?.LocationCode,
            Quantity = t.Quantity,
            Status = t.Status,
            Priority = t.Priority,
            AssignedUserId = t.AssignedUserId,
            CreatedAtUtc = t.CreatedAtUtc,
            CompletedAtUtc = t.CompletedAtUtc,
            ExceptionReason = t.ExceptionReason,
            Notes = t.Notes,
            Version = t.Version
        }).ToList();

        return PagedResult<PutawayTaskDto>.Create(items, totalCount, request.PageNumber, request.PageSize);
    }
}
