using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Shipping.DTOs;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record GetShipmentsQuery : PagedQuery, IRequest<PagedResult<ShipmentDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public ShipmentStatus? Status { get; init; }
    public List<ShipmentStatus>? Statuses { get; init; }
    public string? CarrierCode { get; init; }
    public DateTime? FromDateUtc { get; init; }
    public DateTime? ToDateUtc { get; init; }
}

public class GetShipmentsQueryHandler : IRequestHandler<GetShipmentsQuery, PagedResult<ShipmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetShipmentsQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PagedResult<ShipmentDto>> Handle(
        GetShipmentsQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetShipmentsQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            query.ClientId,
            query.FacilityId,
            allowCrossFacilityRead: true,
            allowClient: false,
            cancellationToken);

        var dbQuery = _context.Shipments
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

        if (query.Statuses != null && query.Statuses.Count > 0)
        {
            dbQuery = dbQuery.Where(s => query.Statuses.Contains(s.Status));
        }
        else if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.CarrierCode))
        {
            var carrier = query.CarrierCode.Trim().ToUpperInvariant();
            dbQuery = dbQuery.Where(s => s.CarrierCode.ToUpper() == carrier);
        }

        if (query.FromDateUtc.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.CreatedAt >= query.FromDateUtc.Value);
        }

        if (query.ToDateUtc.HasValue)
        {
            dbQuery = dbQuery.Where(s => s.CreatedAt <= query.ToDateUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(s =>
                s.ShipmentNumber.ToLower().Contains(term) ||
                s.CarrierCode.ToLower().Contains(term) ||
                (s.BillOfLadingNumber != null && s.BillOfLadingNumber.ToLower().Contains(term)) ||
                (s.TrailerNumber != null && s.TrailerNumber.ToLower().Contains(term)));
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var pageNumber = query.PageNumber > 0 ? query.PageNumber : 1;

        var shipments = await dbQuery
            .OrderByDescending(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .Skip((pageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Include(s => s.Client)
            .Include(s => s.StagingLocation)
            .Include(s => s.Packages)
                .ThenInclude(p => p.OutboundOrder)
            .Include(s => s.Packages)
                .ThenInclude(p => p.CurrentLocation)
            .ToListAsync(cancellationToken);

        var shipmentIds = shipments.Select(s => s.Id).ToList();
        var bolByShipment = shipmentIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : (await _context.BillsOfLading
                .AsNoTracking()
                .Where(b => shipmentIds.Contains(b.ShipmentId) && !b.IsVoided && !b.IsDeleted)
                .OrderByDescending(b => b.IssuedAtUtc)
                .Select(b => new { b.ShipmentId, b.Id })
                .ToListAsync(cancellationToken))
                .GroupBy(b => b.ShipmentId)
                .ToDictionary(g => g.Key, g => g.First().Id);

        var items = shipments.Select(s => new ShipmentDto
        {
            Id = s.Id,
            ClientId = s.ClientId,
            ClientName = s.Client?.CompanyName,
            FacilityId = s.FacilityId,
            ShipmentNumber = s.ShipmentNumber,
            CarrierCode = s.CarrierCode,
            CarrierServiceLevel = s.CarrierServiceLevel,
            Status = s.Status,
            StagingLocationId = s.StagingLocationId,
            StagingLocationCode = s.StagingLocation?.LocationCode,
            AssignedDriverUserId = s.AssignedDriverUserId,
            CustodyStatus = s.CustodyStatus,
            TrailerNumber = s.TrailerNumber,
            SealNumber = s.SealNumber,
            BillOfLadingId = bolByShipment.TryGetValue(s.Id, out var bolId) ? bolId : null,
            BillOfLadingNumber = s.BillOfLadingNumber,
            TotalPackagesCount = s.TotalPackagesCount,
            TotalWeightKg = s.TotalWeightKg,
            TotalVolumeCm3 = s.TotalVolumeCm3,
            DispatchedAtUtc = s.DispatchedAtUtc,
            Packages = s.Packages.Where(p => !p.IsDeleted).Select(p => new ShipmentPackageSummaryDto
            {
                PackageId = p.Id,
                PackageNumber = p.PackageNumber,
                TrackingNumber = p.TrackingNumber,
                CarrierCode = p.CarrierCode,
                GrossWeightKg = p.GrossWeightKg,
                Status = p.Status,
                OutboundOrderId = p.OutboundOrderId,
                OrderNumber = p.OutboundOrder?.OrderNumber ?? string.Empty,
                CurrentLocationCode = p.CurrentLocation?.LocationCode
            }).ToList()
        }).ToList();

        return PagedResult<ShipmentDto>.Create(items, totalCount, pageNumber, query.PageSize);
    }
}
