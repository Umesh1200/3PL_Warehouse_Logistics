using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Queries.GetPurchaseOrderById;

public record GetPurchaseOrderByIdQuery(Guid Id) : IRequest<PurchaseOrderDto>;

public class GetPurchaseOrderByIdQueryHandler : IRequestHandler<GetPurchaseOrderByIdQuery, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetPurchaseOrderByIdQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PurchaseOrderDto> Handle(GetPurchaseOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var po = await _context.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Facility)
            .Include(p => p.Supplier)
            .Include(p => p.Lines)
                .ThenInclude(l => l.Item)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (po == null)
        {
            throw new EntityNotFoundException(nameof(PurchaseOrder), request.Id);
        }

        if (!_currentUser.IsStaff && po.ClientId != _currentUser.TenantId)
        {
            throw new ForbiddenException("You do not have access to this purchase order.");
        }

        return new PurchaseOrderDto
        {
            Id = po.Id,
            ClientId = po.ClientId,
            FacilityId = po.FacilityId,
            FacilityCode = po.Facility.FacilityCode,
            FacilityName = po.Facility.Name,
            SupplierId = po.SupplierId,
            SupplierName = po.Supplier?.SupplierName,
            PoNumber = po.PoNumber,
            OrderDate = po.OrderDate,
            ExpectedDeliveryDate = po.ExpectedDeliveryDate,
            Status = po.Status,
            Currency = po.Currency,
            TotalEstimatedCost = po.TotalEstimatedCost,
            Notes = po.Notes,
            CancellationReason = po.CancellationReason,
            CancelledAt = po.CancelledAt,
            Version = po.Version,
            CreatedAt = po.CreatedAt,
            Lines = po.Lines.OrderBy(l => l.LineNumber).Select(l => new PurchaseOrderLineDto
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
        };
    }
}

