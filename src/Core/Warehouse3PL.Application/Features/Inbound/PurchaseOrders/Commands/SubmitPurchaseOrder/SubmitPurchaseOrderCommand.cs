using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Commands.SubmitPurchaseOrder;

public record SubmitPurchaseOrderCommand(Guid Id) : IRequest<PurchaseOrderDto>;

public class SubmitPurchaseOrderCommandHandler : IRequestHandler<SubmitPurchaseOrderCommand, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public SubmitPurchaseOrderCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PurchaseOrderDto> Handle(SubmitPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var po = await _context.PurchaseOrders
                .Include(p => p.Facility)
                .Include(p => p.Supplier)
                .Include(p => p.Lines)
                    .ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (po == null)
            {
                throw new EntityNotFoundException(nameof(PurchaseOrder), request.Id);
            }

            // Tenant isolation
            if (!_currentUser.IsStaff && po.ClientId != _currentUser.TenantId)
            {
                throw new ForbiddenException("You do not have access to this purchase order.");
            }

            // Active facility access check
            if (!_currentUser.IsStaff)
            {
                var hasAccess = await _context.ClientFacilityAccesses
                    .AnyAsync(a => a.ClientId == po.ClientId &&
                                   a.FacilityId == po.FacilityId &&
                                   a.Status == FacilityAccessStatus.Active, cancellationToken);

                if (!hasAccess)
                {
                    throw new ForbiddenException($"Client does not have active authorized access to facility '{po.Facility.FacilityCode}'.");
                }
            }

            // Explicit domain lifecycle method
            po.Submit();

            await _context.SaveChangesAsync(cancellationToken);

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
                Version = po.Version,
                CreatedAt = po.CreatedAt,
                Lines = po.Lines.Select(l => new PurchaseOrderLineDto
                {
                    Id = l.Id,
                    PurchaseOrderId = l.PurchaseOrderId,
                    LineNumber = l.LineNumber,
                    ItemId = l.ItemId,
                    Sku = l.Item?.Sku ?? string.Empty,
                    ItemDescription = l.Item?.Description ?? string.Empty,
                    ExpectedQty = l.ExpectedQty,
                    ReceivedQty = l.ReceivedQty,
                    UnitPrice = l.UnitPrice,
                    Currency = l.Currency,
                    Status = l.Status,
                    Notes = l.Notes
                }).ToList()
            };
        }, cancellationToken);
    }
}

