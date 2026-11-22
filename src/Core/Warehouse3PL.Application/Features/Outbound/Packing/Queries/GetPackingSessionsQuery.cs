using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Packing.DTOs;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Queries;

public record GetPackingSessionsQuery : PagedQuery, IRequest<PagedResult<PackingSessionSummaryDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public PackingSessionStatus? Status { get; init; }
    public Guid? OutboundOrderId { get; init; }
    public Guid? PackingStationLocationId { get; init; }
}

public class GetPackingSessionsQueryHandler : IRequestHandler<GetPackingSessionsQuery, PagedResult<PackingSessionSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPackingSessionsQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PagedResult<PackingSessionSummaryDto>> Handle(
        GetPackingSessionsQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPackingSessionsQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            query.ClientId,
            query.FacilityId,
            allowCrossFacilityRead: true,
            allowClient: false,
            cancellationToken);

        var dbQuery = _context.PackingSessions
            .AsNoTracking()
            .Where(s => !s.IsDeleted);

        if (scope.ClientId.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.ClientId == scope.ClientId.Value);
        }

        if (scope.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.FacilityId == scope.FacilityId.Value);
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.Status == query.Status.Value);
        }

        if (query.OutboundOrderId.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.OutboundOrderId == query.OutboundOrderId.Value);
        }

        if (query.PackingStationLocationId.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.PackingStationLocationId == query.PackingStationLocationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(s =>
                s.OutboundOrder.OrderNumber.ToLower().Contains(term) ||
                s.PackingStationLocation.LocationCode.ToLower().Contains(term) ||
                s.OperatorUser.Email.ToLower().Contains(term));
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var pageNumber = query.PageNumber > 0 ? query.PageNumber : 1;

        var sessions = await dbQuery
            .OrderByDescending(s => s.StartedAtUtc)
            .ThenBy(s => s.Id)
            .Skip((pageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(s => s.PackingStationLocation)
            .Include(s => s.OutboundOrder)
            .Include(s => s.OperatorUser)
            .ToListAsync(cancellationToken);

        var orderIds = sessions.Select(s => s.OutboundOrderId).Distinct().ToList();
        var activePackageCounts = orderIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _context.Packages
                .AsNoTracking()
                .Where(p => !p.IsDeleted &&
                            orderIds.Contains(p.OutboundOrderId) &&
                            p.Status != PackageStatus.Cancelled)
                .GroupBy(p => p.OutboundOrderId)
                .Select(g => new { OrderId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OrderId, x => x.Count, cancellationToken);

        var items = sessions.Select(s => new PackingSessionSummaryDto
        {
            Id = s.Id,
            ClientId = s.ClientId,
            FacilityId = s.FacilityId,
            PackingStationLocationId = s.PackingStationLocationId,
            PackingStationLocationCode = s.PackingStationLocation?.LocationCode ?? string.Empty,
            OutboundOrderId = s.OutboundOrderId,
            OrderNumber = s.OutboundOrder?.OrderNumber ?? string.Empty,
            OperatorUserId = s.OperatorUserId,
            OperatorEmail = s.OperatorUser?.Email ?? string.Empty,
            Status = s.Status,
            StartedAtUtc = s.StartedAtUtc,
            CompletedAtUtc = s.CompletedAtUtc,
            ActivePackagesCount = activePackageCounts.GetValueOrDefault(s.OutboundOrderId, 0),
            Notes = s.Notes
        }).ToList();

        return PagedResult<PackingSessionSummaryDto>.Create(items, totalCount, pageNumber, query.PageSize);
    }
}
