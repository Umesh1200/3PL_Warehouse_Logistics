using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Queries.GetPurchaseOrders;

public record GetPurchaseOrdersQuery : PagedQuery, IRequest<PagedResult<PurchaseOrderDto>>
{
    public Guid? ClientId { get; init; }
    public Guid? FacilityId { get; init; }
    public Guid? SupplierId { get; init; }
    public PurchaseOrderStatus? Status { get; init; }
}

public class GetPurchaseOrdersQueryHandler : IRequestHandler<GetPurchaseOrdersQuery, PagedResult<PurchaseOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetPurchaseOrdersQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<PurchaseOrderDto>> Handle(GetPurchaseOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = _context.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Facility)
            .Include(p => p.Supplier)
            .Include(p => p.Lines)
                .ThenInclude(l => l.Item)
            .AsQueryable();

        if (!_currentUser.IsStaff)
        {
            query = query.Where(p => p.ClientId == _currentUser.TenantId);
        }
        else if (request.ClientId.HasValue)
        {
            query = query.Where(p => p.ClientId == request.ClientId.Value);
        }

        if (request.FacilityId.HasValue)
        {
            query = query.Where(p => p.FacilityId == request.FacilityId.Value);
        }

        if (request.SupplierId.HasValue)
        {
            query = query.Where(p => p.SupplierId == request.SupplierId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(p => p.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(p =>
                p.PoNumber.ToLower().Contains(term) ||
                (p.Supplier != null && p.Supplier.SupplierName.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new PurchaseOrderDto
            {
                Id = p.Id,
                ClientId = p.ClientId,
                FacilityId = p.FacilityId,
                FacilityCode = p.Facility.FacilityCode,
                FacilityName = p.Facility.Name,
                SupplierId = p.SupplierId,
                SupplierName = p.Supplier != null ? p.Supplier.SupplierName : null,
                PoNumber = p.PoNumber,
                OrderDate = p.OrderDate,
                ExpectedDeliveryDate = p.ExpectedDeliveryDate,
                Status = p.Status,
                Currency = p.Currency,
                TotalEstimatedCost = p.TotalEstimatedCost,
                Notes = p.Notes,
                CancellationReason = p.CancellationReason,
                CancelledAt = p.CancelledAt,
                Version = p.Version,
                CreatedAt = p.CreatedAt,
                Lines = p.Lines.Select(l => new PurchaseOrderLineDto
                {
                    Id = l.Id,
                    PurchaseOrderId = l.PurchaseOrderId,
                    LineNumber = l.LineNumber,
                    ItemId = l.ItemId,
                    Sku = l.Item.Sku,
                    ItemDescription = l.Item.Description,
                    ExpectedQty = l.ExpectedQty,
                    ReceivedQty = l.ReceivedQty,
                    UnitPrice = l.UnitPrice,
                    Currency = l.Currency,
                    Status = l.Status,
                    Notes = l.Notes
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        return PagedResult<PurchaseOrderDto>.Create(items, totalCount, request.PageNumber, request.PageSize);
    }
}

