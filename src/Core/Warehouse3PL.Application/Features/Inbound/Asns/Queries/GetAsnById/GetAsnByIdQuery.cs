using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.Asns.Queries.GetAsnById;

public record GetAsnByIdQuery(Guid Id) : IRequest<AdvanceShippingNoticeDto>;

public class GetAsnByIdQueryHandler : IRequestHandler<GetAsnByIdQuery, AdvanceShippingNoticeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetAsnByIdQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<AdvanceShippingNoticeDto> Handle(GetAsnByIdQuery request, CancellationToken cancellationToken)
    {
        var asn = await _context.AdvanceShippingNotices
            .AsNoTracking()
            .Include(a => a.Facility)
            .Include(a => a.PurchaseOrder)
            .Include(a => a.Lines)
                .ThenInclude(l => l.Item)
            .Include(a => a.Lines)
                .ThenInclude(l => l.PackagingUnit)
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

        if (asn == null)
        {
            throw new EntityNotFoundException(nameof(AdvanceShippingNotice), request.Id);
        }

        if (!_currentUser.IsStaff && asn.ClientId != _currentUser.TenantId)
        {
            throw new ForbiddenException("You do not have access to this ASN.");
        }

        return new AdvanceShippingNoticeDto
        {
            Id = asn.Id,
            ClientId = asn.ClientId,
            FacilityId = asn.FacilityId,
            FacilityCode = asn.Facility.FacilityCode,
            FacilityName = asn.Facility.Name,
            PurchaseOrderId = asn.PurchaseOrderId,
            PoNumber = asn.PurchaseOrder?.PoNumber,
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
            CancellationReason = asn.CancellationReason,
            CancelledAt = asn.CancelledAt,
            Version = asn.Version,
            CreatedAt = asn.CreatedAt,
            Lines = asn.Lines.Select(l => new AsnLineItemDto
            {
                Id = l.Id,
                AsnId = l.AsnId,
                PurchaseOrderLineId = l.PurchaseOrderLineId,
                ItemId = l.ItemId,
                Sku = l.Item.Sku,
                ItemDescription = l.Item.Description,
                ExpectedQty = l.ExpectedQty,
                ReceivedQty = l.ReceivedQty,
                ExpectedLotNumber = l.ExpectedLotNumber,
                ManufactureDate = l.ManufactureDate,
                ExpiryDate = l.ExpiryDate,
                PackagingUnitId = l.PackagingUnitId,
                PackagingUnitName = l.PackagingUnit?.UnitName,
                Notes = l.Notes
            }).ToList()
        };
    }
}

