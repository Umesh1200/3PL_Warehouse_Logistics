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

namespace Warehouse3PL.Application.Features.Inbound.Asns.Commands.CreateAsn;

public record CreateAsnCommand : IRequest<AdvanceShippingNoticeDto>
{
    public Guid? ClientId { get; init; }
    public Guid FacilityId { get; init; }
    public Guid? PurchaseOrderId { get; init; }
    public string AsnNumber { get; init; } = string.Empty;
    public Guid? SupplierId { get; init; }
    public string? SupplierName { get; init; }
    public string? CarrierName { get; init; }
    public string? TrackingReference { get; init; }
    public string? TrailerOrContainerNumber { get; init; }
    public DateTime? EstimatedArrival { get; init; }
    public string? Notes { get; init; }
    public List<CreateAsnLineItemDto> Lines { get; init; } = [];
}

public record CreateAsnLineItemDto
{
    public Guid? PurchaseOrderLineId { get; init; }
    public Guid ItemId { get; init; }
    public decimal ExpectedQty { get; init; }
    public string? ExpectedLotNumber { get; init; }
    public DateTime? ManufactureDate { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public Guid? PackagingUnitId { get; init; }
    public string? Notes { get; init; }
}

public class CreateAsnCommandValidator : AbstractValidator<CreateAsnCommand>
{
    public CreateAsnCommandValidator()
    {
        RuleFor(x => x.FacilityId)
            .NotEmpty().WithMessage("Destination facility is required.");

        RuleFor(x => x.AsnNumber)
            .NotEmpty().WithMessage("ASN number is required.")
            .MaximumLength(50).WithMessage("ASN number must not exceed 50 characters.");

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("An ASN must contain at least one line item.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId)
                .NotEmpty().WithMessage("Item ID is required for each line.");

            line.RuleFor(l => l.ExpectedQty)
                .GreaterThan(0).WithMessage("Expected quantity must be greater than zero.");
        });
    }
}

public class CreateAsnCommandHandler : IRequestHandler<CreateAsnCommand, AdvanceShippingNoticeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public CreateAsnCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<AdvanceShippingNoticeDto> Handle(CreateAsnCommand request, CancellationToken cancellationToken)
    {
        var targetClientId = _currentUser.IsStaff && request.ClientId.HasValue
            ? request.ClientId.Value
            : _currentUser.TenantId ?? request.ClientId ?? Guid.Empty;

        if (targetClientId == Guid.Empty)
        {
            throw new ValidationException(nameof(request.ClientId), "A valid ClientId is required to register an ASN.");
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

        // 2. Verify ASN Number uniqueness within tenant
        var asnNumberNormalized = request.AsnNumber.Trim();
        var asnExists = await _context.AdvanceShippingNotices
            .AnyAsync(a => a.ClientId == targetClientId && a.AsnNumber == asnNumberNormalized, cancellationToken);

        if (asnExists)
        {
            throw new ValidationException(nameof(request.AsnNumber), $"ASN '{asnNumberNormalized}' already exists for this client.");
        }

        // 3. Verify Supplier (if provided) belongs to the client and is active
        Supplier? supplier = null;
        if (request.SupplierId.HasValue)
        {
            supplier = await _context.Suppliers
                .FirstOrDefaultAsync(s => s.Id == request.SupplierId.Value && s.ClientId == targetClientId && !s.IsDeleted, cancellationToken);

            if (supplier == null)
            {
                throw new ValidationException(nameof(request.SupplierId), $"Supplier '{request.SupplierId}' does not belong to this client or does not exist.");
            }

            if (!supplier.IsActive)
            {
                throw new DomainException(
                    $"Supplier '{supplier.SupplierCode}' is inactive and cannot be used for new ASNs.",
                    "SUPPLIER_INACTIVE");
            }
        }

        // 4. Verify Linked Purchase Order (if specified)
        PurchaseOrder? po = null;
        Dictionary<Guid, PurchaseOrderLine> poLines = [];
        if (request.PurchaseOrderId.HasValue)
        {
            po = await _context.PurchaseOrders
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == request.PurchaseOrderId.Value, cancellationToken);

            if (po == null)
            {
                throw new EntityNotFoundException(nameof(PurchaseOrder), request.PurchaseOrderId.Value);
            }

            if (po.ClientId != targetClientId)
            {
                throw new ForbiddenException("Cannot link an ASN to a purchase order belonging to another client.");
            }

            if (po.FacilityId != request.FacilityId)
            {
                throw new ValidationException(nameof(request.FacilityId), "The destination facility of the ASN must match the facility of the linked Purchase Order.");
            }

            if (po.Status != PurchaseOrderStatus.Submitted && po.Status != PurchaseOrderStatus.PartiallyReceived)
            {
                throw new ValidationException(nameof(request.PurchaseOrderId), $"Cannot dispatch an ASN against Purchase Order '{po.PoNumber}' because it is in '{po.Status}' status. PO must be Submitted or PartiallyReceived.");
            }

            poLines = po.Lines.ToDictionary(l => l.Id);
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

            // Cross-tenant SKU guard
            if (item.ClientId != targetClientId)
            {
                throw new ForbiddenException($"Item '{item.Sku}' does not belong to client '{targetClientId}'. Cross-tenant item injection is strictly prohibited.");
            }

            // Line integrity against PO line if linked
            if (lineReq.PurchaseOrderLineId.HasValue)
            {
                if (po == null)
                {
                    throw new ValidationException(nameof(lineReq.PurchaseOrderLineId), "Cannot specify PurchaseOrderLineId when ASN is not linked to a Purchase Order.");
                }

                if (!poLines.TryGetValue(lineReq.PurchaseOrderLineId.Value, out var poLine))
                {
                    throw new ValidationException(nameof(lineReq.PurchaseOrderLineId), $"PO Line '{lineReq.PurchaseOrderLineId.Value}' does not belong to linked Purchase Order '{po.PoNumber}'.");
                }

                if (poLine.ItemId != lineReq.ItemId)
                {
                    throw new ValidationException(nameof(lineReq.ItemId), $"Item mismatch: ASN line references item '{item.Sku}', but linked PO line expects '{poLine.ItemId}'.");
                }
            }

            // Expiry and lot tracking validation
            if (item.RequiresExpiryDate && !lineReq.ExpiryDate.HasValue)
            {
                throw new ValidationException(nameof(lineReq.ExpiryDate), $"Item '{item.Sku}' requires an expiration date.");
            }

            if (item.IsLotTracked && string.IsNullOrWhiteSpace(lineReq.ExpectedLotNumber))
            {
                throw new ValidationException(nameof(lineReq.ExpectedLotNumber), $"Item '{item.Sku}' is lot-tracked and requires an expected lot/batch number.");
            }
        }

        // 6. Build ASN Entity
        var asn = new AdvanceShippingNotice
        {
            ClientId = targetClientId,
            FacilityId = request.FacilityId,
            PurchaseOrderId = request.PurchaseOrderId,
            AsnNumber = asnNumberNormalized,
            SupplierId = request.SupplierId ?? po?.SupplierId,
            SupplierName = request.SupplierName?.Trim() ?? supplier?.SupplierName,
            CarrierName = request.CarrierName?.Trim(),
            TrackingReference = request.TrackingReference?.Trim(),
            TrailerOrContainerNumber = request.TrailerOrContainerNumber?.Trim(),
            EstimatedArrival = request.EstimatedArrival,
            Status = AsnStatus.Draft,
            Notes = request.Notes?.Trim(),
            Version = 1
        };

        foreach (var lineReq in request.Lines)
        {
            asn.Lines.Add(new AsnLineItem
            {
                PurchaseOrderLineId = lineReq.PurchaseOrderLineId,
                ItemId = lineReq.ItemId,
                ExpectedQty = lineReq.ExpectedQty,
                ReceivedQty = 0m, // Received quantity starts at 0 and is populated exclusively by receiving in Phase 3.3
                ExpectedLotNumber = lineReq.ExpectedLotNumber?.Trim(),
                ManufactureDate = lineReq.ManufactureDate,
                ExpiryDate = lineReq.ExpiryDate,
                PackagingUnitId = lineReq.PackagingUnitId,
                Notes = lineReq.Notes?.Trim()
            });
        }

        _context.AdvanceShippingNotices.Add(asn);
        await _context.SaveChangesAsync(cancellationToken);

        return new AdvanceShippingNoticeDto
        {
            Id = asn.Id,
            ClientId = asn.ClientId,
            FacilityId = asn.FacilityId,
            FacilityCode = facility.FacilityCode,
            FacilityName = facility.Name,
            PurchaseOrderId = asn.PurchaseOrderId,
            PoNumber = po?.PoNumber,
            AsnNumber = asn.AsnNumber,
            SupplierId = asn.SupplierId,
            SupplierName = asn.SupplierName,
            CarrierName = asn.CarrierName,
            TrackingReference = asn.TrackingReference,
            TrailerOrContainerNumber = asn.TrailerOrContainerNumber,
            EstimatedArrival = asn.EstimatedArrival,
            ActualArrival = asn.ActualArrival,
            Status = asn.Status,
            Notes = asn.Notes,
            Version = asn.Version,
            CreatedAt = asn.CreatedAt,
            Lines = asn.Lines.Select(l => new AsnLineItemDto
            {
                Id = l.Id,
                AsnId = l.AsnId,
                PurchaseOrderLineId = l.PurchaseOrderLineId,
                PoLineNumber = l.PurchaseOrderLineId.HasValue && poLines.TryGetValue(l.PurchaseOrderLineId.Value, out var pl) ? pl.LineNumber : null,
                ItemId = l.ItemId,
                Sku = items[l.ItemId].Sku,
                ItemDescription = items[l.ItemId].Description,
                ExpectedQty = l.ExpectedQty,
                ReceivedQty = l.ReceivedQty,
                ExpectedLotNumber = l.ExpectedLotNumber,
                ManufactureDate = l.ManufactureDate,
                ExpiryDate = l.ExpiryDate,
                PackagingUnitId = l.PackagingUnitId,
                Notes = l.Notes
            }).ToList()
        };
    }
}
