using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Packing.DTOs;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record GetEligibleShipmentPackagesQuery : PagedQuery, IRequest<PagedResult<PackageSummaryDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public string? CarrierCode { get; init; }
}

public class GetEligibleShipmentPackagesQueryHandler : IRequestHandler<GetEligibleShipmentPackagesQuery, PagedResult<PackageSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetEligibleShipmentPackagesQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PagedResult<PackageSummaryDto>> Handle(
        GetEligibleShipmentPackagesQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetEligibleShipmentPackagesQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            query.ClientId,
            query.FacilityId,
            allowCrossFacilityRead: true,
            allowClient: false,
            cancellationToken);

        var dbQuery = _context.Packages
            .AsNoTracking()
            .Where(p => !p.IsDeleted &&
                        p.ShipmentId == null &&
                        (p.Status == PackageStatus.Packed || p.Status == PackageStatus.LabelPrinted));

        if (scope.ClientId.HasValue)
        {
            dbQuery = dbQuery.Where(p => p.ClientId == scope.ClientId.Value);
        }

        if (scope.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(p => p.FacilityId == scope.FacilityId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.CarrierCode))
        {
            var carrier = query.CarrierCode.Trim().ToUpperInvariant();
            dbQuery = dbQuery.Where(p => p.CarrierCode != null && p.CarrierCode.ToUpper() == carrier);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(p =>
                p.PackageNumber.ToLower().Contains(term) ||
                (p.TrackingNumber != null && p.TrackingNumber.ToLower().Contains(term)) ||
                p.OutboundOrder.OrderNumber.ToLower().Contains(term));
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var pageNumber = query.PageNumber > 0 ? query.PageNumber : 1;

        var packages = await dbQuery
            .OrderByDescending(p => p.PackedAtUtc ?? p.CreatedAt)
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
