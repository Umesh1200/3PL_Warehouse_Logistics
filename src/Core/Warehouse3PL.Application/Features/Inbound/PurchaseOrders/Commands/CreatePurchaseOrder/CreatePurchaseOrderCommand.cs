using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Commands.CreatePurchaseOrder;

public record CreatePurchaseOrderCommand : IRequest<PurchaseOrderDto>
{
    public Guid? ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid? SupplierId { get; init; }
    public string PoNumber { get; init; } = string.Empty;
    public DateTime? OrderDate { get; init; }
    public DateTime? ExpectedDeliveryDate { get; init; }
    public string Currency { get; init; } = "USD";
    public string? Notes { get; init; }
    public List<CreatePoLineItemDto> Lines { get; init; } = [];
}

public record CreatePoLineItemDto
{
    public int LineNumber { get; init; }
    public Guid ItemId { get; init; }
    public decimal ExpectedQty { get; init; }
    public decimal UnitPrice { get; init; }
    public string? Currency { get; init; }
    public string? Notes { get; init; }
}

public class CreatePurchaseOrderCommandValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.FacilityId)
            .NotEmpty().WithMessage("Destination facility is required.");

        RuleFor(x => x.PoNumber)
            .NotEmpty().WithMessage("PO number is required.")
            .MaximumLength(50).WithMessage("PO number must not exceed 50 characters.");

        RuleFor(x => x.Currency)
            .NotEmpty().Length(3).WithMessage("Currency code must be 3 characters.");

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("A purchase order must contain at least one line item.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.LineNumber)
                .GreaterThan(0).WithMessage("Line number must be positive.");

            line.RuleFor(l => l.ItemId)
                .NotEmpty().WithMessage("Item ID is required for each line.");

            line.RuleFor(l => l.ExpectedQty)
                .GreaterThan(0).WithMessage("Expected quantity must be greater than zero.");

            line.RuleFor(l => l.UnitPrice)
                .GreaterThanOrEqualTo(0).WithMessage("Unit price cannot be negative.");
        });
    }
}

public class CreatePurchaseOrderCommandHandler : IRequestHandler<CreatePurchaseOrderCommand, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public CreatePurchaseOrderCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PurchaseOrderDto> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var targetClientId = _currentUser.IsStaff && request.ClientId.HasValue
            ? request.ClientId.Value
            : _currentUser.TenantId ?? request.ClientId ?? Guid.Empty;

        if (targetClientId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.ClientId), "A valid ClientId is required to create a purchase order.");
        }

        // 1. Verify Facility and Active Client-Facility Access
        var facility = await _context.Facilities
            .FirstOrDefaultAsync(f => f.Id == request.FacilityId, cancellationToken);
        if (facility == null)
        {
            throw new EntityNotFoundException(nameof(Facility), request.FacilityId);
        }

        if (!_currentUser.IsStaff)
        {
            var hasAccess = await _context.ClientFacilityAccesses
                .AnyAsync(a => a.ClientId == targetClientId &&
                               a.FacilityId == request.FacilityId &&
                               a.Status == FacilityAccessStatus.Active, cancellationToken);

            if (!hasAccess)
            {
                throw new ForbiddenException($"Client does not have active authorized access to facility '{facility.FacilityCode}'.");
            }
        }

        // 2. Verify Supplier (if provided) belongs to the client
        Supplier? supplier = null;
        if (request.SupplierId.HasValue)
        {
            supplier = await _context.Suppliers
                .FirstOrDefaultAsync(s => s.Id == request.SupplierId.Value && s.ClientId == targetClientId, cancellationToken);

            if (supplier == null)
            {
                throw new ValidationException(nameof(request.SupplierId), $"Supplier '{request.SupplierId}' does not belong to this client or does not exist.");
            }
        }

        // 3. Verify PO Number uniqueness within tenant
        var poNumberNormalized = request.PoNumber.Trim();
        var poExists = await _context.PurchaseOrders
            .AnyAsync(po => po.ClientId == targetClientId && po.PoNumber == poNumberNormalized, cancellationToken);

        if (poExists)
        {
            throw new ValidationException(nameof(request.PoNumber), $"Purchase order '{poNumberNormalized}' already exists for this client.");
        }

        // 4. Verify unique line numbers in request
        var duplicateLineNums = request.Lines
            .GroupBy(l => l.LineNumber)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateLineNums.Count > 0)
        {
            throw new ValidationException(nameof(request.Lines), $"Duplicate line numbers detected: {string.Join(", ", duplicateLineNums)}.");
        }

        // 5. Verify Item integrity and tenant ownership for every line item
        var itemIds = request.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await _context.Items
            .IgnoreQueryFilters()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        foreach (var lineReq in request.Lines)
        {
            if (!items.TryGetValue(lineReq.ItemId, out var item))
            {
                throw new EntityNotFoundException(nameof(Item), lineReq.ItemId);
            }

            // CRITICAL TENANT INTEGRITY: Prevent cross-tenant SKU injection
            if (item.ClientId != targetClientId)
            {
                throw new ForbiddenException($"Item '{item.Sku}' does not belong to client '{targetClientId}'. Cross-tenant item injection is strictly prohibited.");
            }
        }

        // 6. Build Purchase Order Entity
        var purchaseOrder = new PurchaseOrder
        {
            ClientId = targetClientId,
            FacilityId = request.FacilityId,
            SupplierId = request.SupplierId,
            PoNumber = poNumberNormalized,
            OrderDate = request.OrderDate ?? DateTime.UtcNow,
            ExpectedDeliveryDate = request.ExpectedDeliveryDate,
            Status = PurchaseOrderStatus.Draft,
            Currency = request.Currency.ToUpperInvariant(),
            Notes = request.Notes?.Trim(),
            Version = 1
        };

        decimal totalCost = 0m;
        foreach (var lineReq in request.Lines.OrderBy(l => l.LineNumber))
        {
            var lineCost = lineReq.ExpectedQty * lineReq.UnitPrice;
            totalCost += lineCost;

            purchaseOrder.Lines.Add(new PurchaseOrderLine
            {
                LineNumber = lineReq.LineNumber,
                ItemId = lineReq.ItemId,
                ExpectedQty = lineReq.ExpectedQty,
                ReceivedQty = 0m,
                UnitPrice = lineReq.UnitPrice,
                Currency = (lineReq.Currency ?? request.Currency).ToUpperInvariant(),
                Status = PurchaseOrderLineStatus.Open,
                Notes = lineReq.Notes?.Trim()
            });
        }

        purchaseOrder.TotalEstimatedCost = totalCost;

        _context.PurchaseOrders.Add(purchaseOrder);
        await _context.SaveChangesAsync(cancellationToken);

        return new PurchaseOrderDto
        {
            Id = purchaseOrder.Id,
            ClientId = purchaseOrder.ClientId,
            FacilityId = purchaseOrder.FacilityId,
            FacilityCode = facility.FacilityCode,
            FacilityName = facility.Name,
            SupplierId = purchaseOrder.SupplierId,
            SupplierName = supplier?.SupplierName,
            PoNumber = purchaseOrder.PoNumber,
            OrderDate = purchaseOrder.OrderDate,
            ExpectedDeliveryDate = purchaseOrder.ExpectedDeliveryDate,
            Status = purchaseOrder.Status,
            Currency = purchaseOrder.Currency,
            TotalEstimatedCost = purchaseOrder.TotalEstimatedCost,
            Notes = purchaseOrder.Notes,
            Version = purchaseOrder.Version,
            CreatedAt = purchaseOrder.CreatedAt,
            Lines = purchaseOrder.Lines.Select(l => new PurchaseOrderLineDto
            {
                Id = l.Id,
                PurchaseOrderId = l.PurchaseOrderId,
                LineNumber = l.LineNumber,
                ItemId = l.ItemId,
                Sku = items[l.ItemId].Sku,
                ItemDescription = items[l.ItemId].Description,
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
