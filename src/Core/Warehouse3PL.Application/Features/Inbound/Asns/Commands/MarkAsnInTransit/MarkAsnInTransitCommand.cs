using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.Asns.Commands.MarkAsnInTransit;

public record MarkAsnInTransitCommand(Guid Id) : IRequest<AdvanceShippingNoticeDto>;

public class MarkAsnInTransitCommandHandler : IRequestHandler<MarkAsnInTransitCommand, AdvanceShippingNoticeDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public MarkAsnInTransitCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<AdvanceShippingNoticeDto> Handle(MarkAsnInTransitCommand request, CancellationToken cancellationToken)
    {
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var asn = await _context.AdvanceShippingNotices
                .Include(a => a.Facility)
                .Include(a => a.PurchaseOrder)
                .Include(a => a.Lines)
                    .ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

            if (asn == null)
            {
                throw new EntityNotFoundException(nameof(AdvanceShippingNotice), request.Id);
            }

            if (!_currentUser.IsStaff && asn.ClientId != _currentUser.TenantId)
            {
                throw new ForbiddenException("You do not have access to this ASN.");
            }

            // Facility access check
            if (!_currentUser.IsStaff)
            {
                var hasAccess = await _context.ClientFacilityAccesses
                    .AnyAsync(a => a.ClientId == asn.ClientId &&
                                   a.FacilityId == asn.FacilityId &&
                                   a.Status == FacilityAccessStatus.Active, cancellationToken);

                if (!hasAccess)
                {
                    throw new ForbiddenException($"Client does not have active authorized access to facility '{asn.Facility.FacilityCode}'.");
                }
            }

            // Explicit domain method
            asn.MarkInTransit();

            await _context.SaveChangesAsync(cancellationToken);

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
                Version = asn.Version,
                CreatedAt = asn.CreatedAt,
                Lines = asn.Lines.Select(l => new AsnLineItemDto
                {
                    Id = l.Id,
                    AsnId = l.AsnId,
                    PurchaseOrderLineId = l.PurchaseOrderLineId,
                    ItemId = l.ItemId,
                    Sku = l.Item?.Sku ?? string.Empty,
                    ItemDescription = l.Item?.Description ?? string.Empty,
                    ExpectedQty = l.ExpectedQty,
                    ReceivedQty = l.ReceivedQty,
                    ExpectedLotNumber = l.ExpectedLotNumber,
                    ManufactureDate = l.ManufactureDate,
                    ExpiryDate = l.ExpiryDate,
                    PackagingUnitId = l.PackagingUnitId,
                    Notes = l.Notes
                }).ToList()
            };
        }, cancellationToken);
    }
}

