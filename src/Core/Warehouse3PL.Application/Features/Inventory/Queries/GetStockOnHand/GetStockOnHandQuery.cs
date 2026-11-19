using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.Inventory.Queries.GetStockOnHand;

public record GetStockOnHandQuery : PagedQuery, IRequest<PagedResult<StockOnHandDto>>
{
    public Guid? ClientId { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? ItemId { get; init; }
    public Guid? LocationId { get; init; }
    public string? SearchSku { get; init; }
}

public class GetStockOnHandQueryHandler : IRequestHandler<GetStockOnHandQuery, PagedResult<StockOnHandDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;

    public GetStockOnHandQueryHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<StockOnHandDto>> Handle(GetStockOnHandQuery request, CancellationToken cancellationToken)
    {
        var query = _context.StockOnHand
            .AsNoTracking()
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .AsQueryable();

        if (!_currentUser.IsStaff)
        {
            var callerTenant = _currentUser.TenantId ?? _tenantProvider.TenantId;
            if (!callerTenant.HasValue || callerTenant.Value == Guid.Empty)
            {
                // Non-staff callers must have a valid tenant; fail closed if absent
                return PagedResult<StockOnHandDto>.Create([], 0, request.PageNumber, request.PageSize);
            }
            query = query.Where(s => s.ClientId == callerTenant.Value);
        }
        else if (request.ClientId.HasValue)
        {
            query = query.Where(s => s.ClientId == request.ClientId.Value);
        }

        if (request.FacilityId.HasValue)
        {
            query = query.Where(s => s.FacilityId == request.FacilityId.Value);
        }

        if (request.ItemId.HasValue)
        {
            query = query.Where(s => s.ItemId == request.ItemId.Value);
        }

        if (request.LocationId.HasValue)
        {
            query = query.Where(s => s.LocationId == request.LocationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchSku))
        {
            var search = request.SearchSku.Trim().ToUpperInvariant();
            query = query.Where(s => s.Item.Sku.Contains(search) || s.Location.LocationCode.Contains(search));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(s => s.Item.Sku)
            .ThenBy(s => s.Location.LocationCode)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new StockOnHandDto
            {
                Id = s.Id,
                ClientId = s.ClientId,
                FacilityId = s.FacilityId,
                ItemId = s.ItemId,
                Sku = s.Item.Sku,
                ItemDescription = s.Item.Description,
                LocationId = s.LocationId,
                LocationCode = s.Location.LocationCode,
                LocationBarcode = s.Location.Barcode,
                LotId = s.LotId,
                LotNumber = s.Lot != null ? s.Lot.LotNumber : null,
                ExpirationDate = s.Lot != null ? s.Lot.ExpirationDate : null,
                QtyAvailable = s.QtyAvailable,
                QtyAllocated = s.QtyAllocated,
                QtyReserved = s.QtyReserved,
                QtyQuarantined = s.QtyQuarantined,
                QtyDamaged = s.QtyDamaged,
                QtyOnHold = s.QtyOnHold,
                TotalQuantity = s.QtyAvailable + s.QtyAllocated + s.QtyReserved + s.QtyQuarantined + s.QtyDamaged + s.QtyOnHold,
                LastCountedAt = s.LastCountedAt,
                Version = s.Version
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<StockOnHandDto>(items, totalCount, request.PageNumber, request.PageSize);
    }
}

