using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Packing.DTOs;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Queries;

public record GetPackingPackagesQuery : PagedQuery, IRequest<PagedResult<PackageSummaryDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? PackingSessionId { get; init; }
    public Guid? OutboundOrderId { get; init; }
    public PackageStatus? Status { get; init; }
}

public class GetPackingPackagesQueryHandler : IRequestHandler<GetPackingPackagesQuery, PagedResult<PackageSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPackingPackagesQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PagedResult<PackageSummaryDto>> Handle(
        GetPackingPackagesQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPackingPackagesQuery));

        Guid? sessionOrderId = query.OutboundOrderId;
        Guid? sessionFacilityId = query.FacilityId;

        if (query.PackingSessionId.HasValue)
        {
            var session = await _context.PackingSessions
                .AsNoTracking()
                .Where(s => s.Id == query.PackingSessionId.Value && !s.IsDeleted)
                .Select(s => new { s.Id, s.FacilityId, s.OutboundOrderId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException(nameof(PackingSession), query.PackingSessionId.Value);

            _outboundAuth.ValidateStaffReadFacilityAccess(session.FacilityId, nameof(GetPackingPackagesQuery));
            sessionOrderId = session.OutboundOrderId;
            sessionFacilityId = session.FacilityId;
        }

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            query.ClientId,
            sessionFacilityId,
            allowCrossFacilityRead: true,
            allowClient: false,
            cancellationToken);

        var dbQuery = _context.Packages
            .AsNoTracking()
            .Where(p => !p.IsDeleted);

        if (scope.ClientId.HasValue)
        {
            dbQuery = dbQuery.Where(p => p.ClientId == scope.ClientId.Value);
        }

        if (scope.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(p => p.FacilityId == scope.FacilityId.Value);
        }

        if (sessionOrderId.HasValue)
        {
            dbQuery = dbQuery.Where(p => p.OutboundOrderId == sessionOrderId.Value);
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(p => p.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(p =>
                p.PackageNumber.ToLower().Contains(term) ||
                (p.TrackingNumber != null && p.TrackingNumber.ToLower().Contains(term)) ||
                p.OutboundOrder.OrderNumber.ToLower().Contains(term) ||
                p.HandlingUnit.LpnCode.ToLower().Contains(term));
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var pageNumber = query.PageNumber > 0 ? query.PageNumber : 1;

        var packages = await dbQuery
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.CartonSequence)
            .ThenBy(p => p.Id)
            .Skip((pageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(p => p.OutboundOrder)
            .Include(p => p.HandlingUnit)
            .Include(p => p.ShippingLabels)
            .ToListAsync(cancellationToken);

        return PagedResult<PackageSummaryDto>.Create(
            packages.Select(PackageMappings.ToSummaryDto).ToList(),
            totalCount,
            pageNumber,
            query.PageSize);
    }
}
