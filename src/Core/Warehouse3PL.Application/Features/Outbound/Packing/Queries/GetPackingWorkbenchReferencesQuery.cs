using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Queries;

public record PackingStationLocationDto
{
    public Guid Id { get; init; }
    public string LocationCode { get; init; } = string.Empty;
    public string Barcode { get; init; } = string.Empty;
    public string AisleCode { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public record PackagingMaterialDto
{
    public Guid Id { get; init; }
    public Guid? ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public PackagingMaterialType MaterialType { get; init; }
    public decimal LengthCm { get; init; }
    public decimal WidthCm { get; init; }
    public decimal HeightCm { get; init; }
    public decimal MaxWeightKg { get; init; }
    public decimal TareWeightKg { get; init; }
    public decimal? CostPerUnit { get; init; }
}

public record PackableOrderSummaryDto
{
    public Guid Id { get; init; }
    public Guid ClientId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public OutboundOrderStatus Status { get; init; }
    public int Priority { get; init; }
    public CarrierServiceLevel ServiceLevel { get; init; }
    public decimal TotalAllocatedUnits { get; init; }
    public decimal TotalPickedUnits { get; init; }
    public decimal TotalPackedUnits { get; init; }
}

public record GetPackingStationsQuery(Guid? FacilityId = null)
    : IRequest<IReadOnlyList<PackingStationLocationDto>>;

public class GetPackingStationsQueryHandler
    : IRequestHandler<GetPackingStationsQuery, IReadOnlyList<PackingStationLocationDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPackingStationsQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<IReadOnlyList<PackingStationLocationDto>> Handle(
        GetPackingStationsQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPackingStationsQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            requestedClientId: null,
            requestedFacilityId: query.FacilityId == Guid.Empty ? null : query.FacilityId,
            allowCrossFacilityRead: false,
            allowClient: false,
            ct: cancellationToken);

        var dbQuery = _context.Locations
            .AsNoTracking()
            .Where(l => !l.IsDeleted &&
                        l.IsActive &&
                        l.LocationType == LocationType.PackingStation);

        if (scope.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(l => l.FacilityId == scope.FacilityId.Value);
        }

        return await dbQuery
            .OrderBy(l => l.LocationCode)
            .Select(l => new PackingStationLocationDto
            {
                Id = l.Id,
                LocationCode = l.LocationCode,
                Barcode = l.Barcode,
                AisleCode = l.AisleCode ?? string.Empty,
                IsActive = l.IsActive
            })
            .ToListAsync(cancellationToken);
    }
}

public record GetPackagingMaterialsQuery(
    Guid? FacilityId = null,
    Guid? ClientId = null) : IRequest<IReadOnlyList<PackagingMaterialDto>>;

public class GetPackagingMaterialsQueryHandler
    : IRequestHandler<GetPackagingMaterialsQuery, IReadOnlyList<PackagingMaterialDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPackagingMaterialsQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<IReadOnlyList<PackagingMaterialDto>> Handle(
        GetPackagingMaterialsQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPackagingMaterialsQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            requestedClientId: query.ClientId == Guid.Empty ? null : query.ClientId,
            requestedFacilityId: query.FacilityId == Guid.Empty ? null : query.FacilityId,
            allowCrossFacilityRead: false,
            allowClient: false,
            ct: cancellationToken);

        var dbQuery = _context.PackagingMaterials
            .AsNoTracking()
            .Where(m => !m.IsDeleted && m.IsActive);

        if (scope.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(m => m.FacilityId == scope.FacilityId.Value);
        }

        if (scope.ClientId.HasValue)
        {
            var cid = scope.ClientId.Value;
            dbQuery = dbQuery.Where(m => m.ClientId == null || m.ClientId == cid);
        }

        return await dbQuery
            .OrderBy(m => m.LengthCm * m.WidthCm * m.HeightCm)
            .ThenBy(m => m.Code)
            .Select(m => new PackagingMaterialDto
            {
                Id = m.Id,
                ClientId = m.ClientId,
                FacilityId = m.FacilityId,
                Code = m.Code,
                Name = m.Name,
                MaterialType = m.MaterialType,
                LengthCm = m.LengthCm,
                WidthCm = m.WidthCm,
                HeightCm = m.HeightCm,
                MaxWeightKg = m.MaxWeightKg,
                TareWeightKg = m.TareWeightKg,
                CostPerUnit = m.CostPerUnit
            })
            .ToListAsync(cancellationToken);
    }
}

public record GetPackableOrdersQuery : PagedQuery, IRequest<PagedResult<PackableOrderSummaryDto>>
{
    public Guid? FacilityId { get; init; }
}

public class GetPackableOrdersQueryHandler
    : IRequestHandler<GetPackableOrdersQuery, PagedResult<PackableOrderSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPackableOrdersQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PagedResult<PackableOrderSummaryDto>> Handle(
        GetPackableOrdersQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPackableOrdersQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            requestedClientId: null,
            requestedFacilityId: query.FacilityId == Guid.Empty ? null : query.FacilityId,
            allowCrossFacilityRead: false,
            allowClient: false,
            ct: cancellationToken);

        var dbQuery = _context.OutboundOrders
            .AsNoTracking()
            .Where(o => !o.IsDeleted &&
                        (o.Status == OutboundOrderStatus.Allocated ||
                         o.Status == OutboundOrderStatus.PartiallyAllocated ||
                         o.Status == OutboundOrderStatus.InProgress));

        if (scope.FacilityId.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.FacilityId == scope.FacilityId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim().ToLower();
            dbQuery = dbQuery.Where(o =>
                o.OrderNumber.ToLower().Contains(term) ||
                o.CustomerName.ToLower().Contains(term));
        }

        var pageNumber = query.PageNumber > 0 ? query.PageNumber : 1;
        var pageSize = query.PageSize is > 0 and <= 100 ? query.PageSize : 50;
        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var orders = await dbQuery
            .OrderByDescending(o => o.Priority)
            .ThenBy(o => o.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Include(o => o.Lines)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();

        var pickedByOrder = orderIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _context.PickTasks
                .AsNoTracking()
                .Where(t => !t.IsDeleted && orderIds.Contains(t.OutboundOrderId))
                .GroupBy(t => t.OutboundOrderId)
                .Select(g => new { OrderId = g.Key, Qty = g.Sum(x => x.QuantityPicked) })
                .ToDictionaryAsync(x => x.OrderId, x => x.Qty, cancellationToken);

        var packedByOrder = orderIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _context.PackageLines
                .AsNoTracking()
                .Where(pl => !pl.Package.IsDeleted &&
                             pl.Package.Status != PackageStatus.Cancelled &&
                             orderIds.Contains(pl.Package.OutboundOrderId))
                .GroupBy(pl => pl.Package.OutboundOrderId)
                .Select(g => new { OrderId = g.Key, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.OrderId, x => x.Qty, cancellationToken);

        var items = orders.Select(o => new PackableOrderSummaryDto
        {
            Id = o.Id,
            ClientId = o.ClientId,
            OrderNumber = o.OrderNumber,
            CustomerName = o.CustomerName,
            Status = o.Status,
            Priority = o.Priority,
            ServiceLevel = o.ServiceLevel,
            TotalAllocatedUnits = o.Lines.Where(l => !l.IsDeleted).Sum(l => l.QuantityAllocated),
            TotalPickedUnits = pickedByOrder.GetValueOrDefault(o.Id, 0m),
            TotalPackedUnits = packedByOrder.GetValueOrDefault(o.Id, 0m)
        }).ToList();

        return new PagedResult<PackableOrderSummaryDto>(items, totalCount, pageNumber, pageSize);
    }
}
