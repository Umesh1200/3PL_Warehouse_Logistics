using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Features.Outbound.Shipping.DTOs;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record GetShipmentByIdQuery(Guid Id) : IRequest<ShipmentDto>;

public class GetShipmentByIdQueryHandler : IRequestHandler<GetShipmentByIdQuery, ShipmentDto>
{
    private readonly IApplicationDbContext _context;

    public GetShipmentByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ShipmentDto> Handle(
        GetShipmentByIdQuery query,
        CancellationToken cancellationToken)
    {
        var shipment = await _context.Shipments
            .Include(s => s.Packages)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == query.Id && !s.IsDeleted, cancellationToken);

        if (shipment == null)
        {
            throw new EntityNotFoundException(nameof(Shipment), query.Id);
        }

        return new ShipmentDto
        {
            Id = shipment.Id,
            ClientId = shipment.ClientId,
            FacilityId = shipment.FacilityId,
            ShipmentNumber = shipment.ShipmentNumber,
            CarrierCode = shipment.CarrierCode,
            CarrierServiceLevel = shipment.CarrierServiceLevel,
            Status = shipment.Status,
            TrailerNumber = shipment.TrailerNumber,
            SealNumber = shipment.SealNumber,
            BillOfLadingNumber = shipment.BillOfLadingNumber,
            TotalPackagesCount = shipment.TotalPackagesCount,
            TotalWeightKg = shipment.TotalWeightKg,
            TotalVolumeCm3 = shipment.TotalVolumeCm3,
            DispatchedAtUtc = shipment.DispatchedAtUtc,
            Packages = shipment.Packages.Select(p => new ShipmentPackageSummaryDto
            {
                PackageId = p.Id,
                PackageNumber = p.PackageNumber,
                TrackingNumber = p.TrackingNumber,
                GrossWeightKg = p.GrossWeightKg,
                Status = p.Status,
                OutboundOrderId = p.OutboundOrderId
            }).ToList()
        };
    }
}

