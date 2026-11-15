using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Features.Items.Queries.GetItems;

public record GetItemsQuery : PagedQuery, IRequest<PagedResult<ItemDto>>
{
    public Guid? ClientId { get; init; }
}

public class GetItemsQueryHandler : IRequestHandler<GetItemsQuery, PagedResult<ItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ICurrentUserContext _currentUser;

    public GetItemsQueryHandler(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<ItemDto>> Handle(GetItemsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Items
            .AsNoTracking()
            .Include(i => i.Barcodes)
            .Include(i => i.PackagingUnits)
            .Where(i => !i.IsDeleted);

        // Apply tenant isolation
        var effectiveTenant = request.ClientId ?? _tenantProvider.TenantId ?? _currentUser.TenantId;
        if (!_currentUser.IsStaff && effectiveTenant.HasValue)
        {
            query = query.Where(i => i.ClientId == effectiveTenant.Value);
        }
        else if (request.ClientId.HasValue)
        {
            query = query.Where(i => i.ClientId == request.ClientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var search = request.SearchTerm.Trim().ToUpperInvariant();
            query = query.Where(i =>
                i.Sku.Contains(search) ||
                i.Description.ToUpper().Contains(search) ||
                i.Barcodes.Any(b => b.BarcodeValue.Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(i => i.Sku)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(i => new ItemDto
            {
                Id = i.Id,
                ClientId = i.ClientId,
                Sku = i.Sku,
                Description = i.Description,
                ShortName = i.ShortName,
                UnitPrice = i.UnitPrice,
                WeightKg = i.WeightKg,
                LengthCm = i.LengthCm,
                WidthCm = i.WidthCm,
                HeightCm = i.HeightCm,
                VolumeCm3 = i.VolumeCm3,
                AbcVelocity = i.AbcVelocity,
                StorageTempClass = i.StorageTempClass,
                HazardClass = i.HazardClass,
                IsLotTracked = i.IsLotTracked,
                IsSerialized = i.IsSerialized,
                RequiresExpiryDate = i.RequiresExpiryDate,
                ShelfLifeDays = i.ShelfLifeDays,
                Status = i.Status,
                Barcodes = i.Barcodes.Select(b => b.BarcodeValue).ToList(),
                PackagingUnits = i.PackagingUnits.Select(p => new PackagingUnitDto
                {
                    Id = p.Id,
                    UnitLevel = p.UnitLevel,
                    UnitName = p.UnitName,
                    ConversionQty = p.ConversionQty,
                    WeightKg = p.WeightKg,
                    VolumeCm3 = p.VolumeCm3,
                    Barcode = p.Barcode
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ItemDto>(items, totalCount, request.PageNumber, request.PageSize);
    }
}
