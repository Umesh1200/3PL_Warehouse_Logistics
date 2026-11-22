using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Queries;

public record GetPickWavesQuery(
    Guid FacilityId,
    PickWaveStatus? Status = null,
    PickWaveType? WaveType = null,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<PagedResult<PickWaveDto>>;

public class GetPickWavesQueryHandler : IRequestHandler<GetPickWavesQuery, PagedResult<PickWaveDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPickWavesQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PagedResult<PickWaveDto>> Handle(
        GetPickWavesQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPickWavesQuery));
        var requestedFacility = query.FacilityId == Guid.Empty ? (Guid?)null : query.FacilityId;
        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            null,
            requestedFacility,
            allowCrossFacilityRead: true,
            allowClient: false,
            cancellationToken);

        var dbQuery = _context.PickWaves
            .Where(w => !w.IsDeleted);

        if (scope.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(w => w.FacilityId == scope.FacilityId.Value);
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(w => w.Status == query.Status.Value);
        }

        if (query.WaveType.HasValue)
        {
            dbQuery = dbQuery.Where(w => w.WaveType == query.WaveType.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var pageNumber = query.PageNumber > 0 ? query.PageNumber : 1;
        var pageSize = query.PageSize > 0 ? query.PageSize : 20;

        var waves = await dbQuery
            .OrderByDescending(w => w.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Include(w => w.WaveOrders)
            .Include(w => w.Tasks)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var dtos = waves.Select(w => new PickWaveDto
        {
            Id = w.Id,
            ClientId = w.ClientId,
            FacilityId = w.FacilityId,
            WaveNumber = w.WaveNumber,
            WaveType = w.WaveType,
            Status = w.Status,
            Priority = w.Priority,
            ZoneId = w.ZoneId,
            TempClass = w.TempClass,
            AssignedToUserId = w.AssignedToUserId,
            ReleasedAtUtc = w.ReleasedAtUtc,
            StartedAtUtc = w.StartedAtUtc,
            CompletedAtUtc = w.CompletedAtUtc,
            CreatedAt = w.CreatedAt,
            OrdersCount = w.WaveOrders.Count,
            TasksCount = w.Tasks.Count,
            TotalQuantityToPick = w.Tasks.Sum(t => t.QuantityToPick),
            TotalQuantityPicked = w.Tasks.Sum(t => t.QuantityPicked)
        }).ToList();

        return PagedResult<PickWaveDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}

