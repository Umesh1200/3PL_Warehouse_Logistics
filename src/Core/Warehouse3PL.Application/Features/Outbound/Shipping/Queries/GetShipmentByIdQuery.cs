using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Shipping.DTOs;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record GetShipmentByIdQuery(Guid Id) : IRequest<ShipmentDto>;

public class GetShipmentByIdQueryHandler : IRequestHandler<GetShipmentByIdQuery, ShipmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetShipmentByIdQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<ShipmentDto> Handle(
        GetShipmentByIdQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetShipmentByIdQuery));

        var shipment = await _context.Shipments
            .Include(s => s.Client)
            .Include(s => s.StagingLocation)
            .Include(s => s.Packages)
                .ThenInclude(p => p.OutboundOrder)
            .Include(s => s.Packages)
                .ThenInclude(p => p.CurrentLocation)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == query.Id && !s.IsDeleted, cancellationToken);

        if (shipment == null)
        {
            throw new EntityNotFoundException(nameof(Shipment), query.Id);
        }

        _outboundAuth.ValidateStaffReadFacilityAccess(shipment.FacilityId, nameof(GetShipmentByIdQuery));

        var activeBolId = await _context.BillsOfLading
            .AsNoTracking()
            .Where(b => b.ShipmentId == shipment.Id && !b.IsVoided && !b.IsDeleted)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new ShipmentDto
        {
            Id = shipment.Id,
            ClientId = shipment.ClientId,
            ClientName = shipment.Client?.CompanyName,
            FacilityId = shipment.FacilityId,
            ShipmentNumber = shipment.ShipmentNumber,
            CarrierCode = shipment.CarrierCode,
            CarrierServiceLevel = shipment.CarrierServiceLevel,
            Status = shipment.Status,
            StagingLocationId = shipment.StagingLocationId,
            StagingLocationCode = shipment.StagingLocation?.LocationCode,
            AssignedDriverUserId = shipment.AssignedDriverUserId,
            CustodyStatus = shipment.CustodyStatus,
            TrailerNumber = shipment.TrailerNumber,
            SealNumber = shipment.SealNumber,
            BillOfLadingId = activeBolId,
            BillOfLadingNumber = shipment.BillOfLadingNumber,
            TotalPackagesCount = shipment.TotalPackagesCount,
            TotalWeightKg = shipment.TotalWeightKg,
            TotalVolumeCm3 = shipment.TotalVolumeCm3,
            DispatchedAtUtc = shipment.DispatchedAtUtc,
            Packages = shipment.Packages.Where(p => !p.IsDeleted).Select(p => new ShipmentPackageSummaryDto
            {
                PackageId = p.Id,
                PackageNumber = p.PackageNumber,
                TrackingNumber = p.TrackingNumber,
                CarrierCode = p.CarrierCode,
                GrossWeightKg = p.GrossWeightKg,
                Status = p.Status,
                OutboundOrderId = p.OutboundOrderId,
                OrderNumber = p.OutboundOrder?.OrderNumber ?? string.Empty,
                CurrentLocationCode = p.CurrentLocation?.LocationCode
            }).ToList()
        };
    }
}

