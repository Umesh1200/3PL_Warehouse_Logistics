using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Features.Outbound.Shipping.DTOs;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Shipping.Queries;

public record GetBillOfLadingByIdQuery(Guid Id) : IRequest<BillOfLadingDto>;

public class GetBillOfLadingByIdQueryHandler : IRequestHandler<GetBillOfLadingByIdQuery, BillOfLadingDto>
{
    private readonly IApplicationDbContext _context;

    public GetBillOfLadingByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BillOfLadingDto> Handle(
        GetBillOfLadingByIdQuery query,
        CancellationToken cancellationToken)
    {
        var bol = await _context.BillsOfLading
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == query.Id && !b.IsDeleted, cancellationToken);

        if (bol == null)
        {
            throw new EntityNotFoundException(nameof(BillOfLading), query.Id);
        }

        return new BillOfLadingDto
        {
            Id = bol.Id,
            ClientId = bol.ClientId,
            FacilityId = bol.FacilityId,
            ShipmentId = bol.ShipmentId,
            BolNumber = bol.BolNumber,
            CarrierCode = bol.CarrierCode,
            CarrierName = bol.CarrierName,
            ServiceLevel = bol.ServiceLevel,
            TrailerNumber = bol.TrailerNumber,
            SealNumber = bol.SealNumber,
            TotalPackageCount = bol.TotalPackageCount,
            TotalGrossWeightKg = bol.TotalGrossWeightKg,
            TotalVolumeCm3 = bol.TotalVolumeCm3,
            ShipperName = bol.ShipperName,
            ShipperAddressSnapshot = bol.ShipperAddressSnapshot,
            ConsigneeName = bol.ConsigneeName,
            ConsigneeAddressSnapshot = bol.ConsigneeAddressSnapshot,
            PackageTrackingNumbers = bol.PackageTrackingNumbers,
            SpecialInstructions = bol.SpecialInstructions,
            DocumentSnapshotJson = bol.DocumentSnapshotJson,
            IssuedAtUtc = bol.IssuedAtUtc,
            IsVoided = bol.IsVoided
        };
    }
}

