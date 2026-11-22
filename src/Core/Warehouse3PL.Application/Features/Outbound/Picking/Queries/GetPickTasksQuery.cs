using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Queries;

public record GetPickTasksQuery : PagedQuery, IRequest<PagedResult<PickTaskDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? PickWaveId { get; init; }
    public Guid? OutboundOrderId { get; init; }
    public PickTaskStatus? Status { get; init; }
    public List<PickTaskStatus>? Statuses { get; init; }
    public Guid? AssignedUserId { get; init; }
}

public class GetPickTasksQueryHandler : IRequestHandler<GetPickTasksQuery, PagedResult<PickTaskDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPickTasksQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PagedResult<PickTaskDto>> Handle(GetPickTasksQuery request, CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPickTasksQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            request.ClientId,
            request.FacilityId,
            allowCrossFacilityRead: true,
            allowClient: false,
            cancellationToken);

        var query = _context.PickTasks
            .AsNoTracking()
            .Where(t => !t.IsDeleted);

        if (scope.ClientId.HasValue)
        {
            query = query.Where(t => t.ClientId == scope.ClientId.Value);
        }

        if (scope.FacilityId.HasValue)
        {
            query = query.Where(t => t.FacilityId == scope.FacilityId.Value);
        }

        if (request.PickWaveId.HasValue)
        {
            query = query.Where(t => t.PickWaveId == request.PickWaveId.Value);
        }

        if (request.OutboundOrderId.HasValue)
        {
            query = query.Where(t => t.OutboundOrderId == request.OutboundOrderId.Value);
        }

        if (request.AssignedUserId.HasValue)
        {
            query = query.Where(t => t.AssignedUserId == request.AssignedUserId.Value);
        }

        if (request.Statuses != null && request.Statuses.Count > 0)
        {
            query = query.Where(t => request.Statuses.Contains(t.Status));
        }
        else if (request.Status.HasValue)
        {
            query = query.Where(t => t.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(t =>
                t.OutboundOrder.OrderNumber.ToLower().Contains(term) ||
                t.Item.Sku.ToLower().Contains(term) ||
                t.SourceLocation.LocationCode.ToLower().Contains(term) ||
                (t.TargetToteBarcode != null && t.TargetToteBarcode.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageNumber = request.PageNumber > 0 ? request.PageNumber : 1;

        var items = await query
            .OrderByDescending(t => t.PickWave.Priority)
            .ThenBy(t => t.PickSequence)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip((pageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(t => t.SourceLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.OutboundOrder)
            .ToListAsync(cancellationToken);

        return PagedResult<PickTaskDto>.Create(
            items.Select(PickTaskMappings.ToDto).ToList(),
            totalCount,
            pageNumber,
            request.PageSize);
    }
}
