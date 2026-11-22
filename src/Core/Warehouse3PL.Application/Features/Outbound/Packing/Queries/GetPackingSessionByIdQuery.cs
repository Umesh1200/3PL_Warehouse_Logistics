using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Packing.DTOs;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Packing.Queries;

public record GetPackingSessionByIdQuery(Guid Id) : IRequest<PackingSessionDetailDto>;

public class GetPackingSessionByIdQueryHandler : IRequestHandler<GetPackingSessionByIdQuery, PackingSessionDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPackingSessionByIdQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PackingSessionDetailDto> Handle(
        GetPackingSessionByIdQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPackingSessionByIdQuery));

        var session = await _context.PackingSessions
            .Include(s => s.PackingStationLocation)
            .Include(s => s.OutboundOrder)
                .ThenInclude(o => o.Lines)
                    .ThenInclude(l => l.Item)
            .Include(s => s.OperatorUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == query.Id && !s.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PackingSession), query.Id);

        _outboundAuth.ValidateStaffReadFacilityAccess(session.FacilityId, nameof(GetPackingSessionByIdQuery));

        var packages = await _context.Packages
            .AsNoTracking()
            .Include(p => p.OutboundOrder)
            .Include(p => p.HandlingUnit)
            .Include(p => p.ShippingLabels)
            .Include(p => p.Lines)
            .Where(p => !p.IsDeleted &&
                        p.OutboundOrderId == session.OutboundOrderId &&
                        p.FacilityId == session.FacilityId)
            .OrderBy(p => p.CartonSequence)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        var pickedByLine = await _context.PickTasks
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.OutboundOrderId == session.OutboundOrderId)
            .GroupBy(t => t.OutboundOrderLineId)
            .Select(g => new { OrderLineId = g.Key, QtyPicked = g.Sum(x => x.QuantityPicked) })
            .ToDictionaryAsync(x => x.OrderLineId, x => x.QtyPicked, cancellationToken);

        var packedByLine = packages
            .Where(p => p.Status != PackageStatus.Cancelled)
            .SelectMany(p => p.Lines)
            .GroupBy(l => l.OutboundOrderLineId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var orderLines = (session.OutboundOrder?.Lines ?? [])
            .Where(l => !l.IsDeleted)
            .OrderBy(l => l.LineNumber)
            .Select(l =>
            {
                var qtyPacked = packedByLine.GetValueOrDefault(l.Id, 0m);
                var qtyPicked = pickedByLine.GetValueOrDefault(l.Id, 0m);
                return new PackingSessionOrderLineDto
                {
                    OrderLineId = l.Id,
                    ItemId = l.ItemId,
                    Sku = l.Item?.Sku ?? string.Empty,
                    Description = l.Item?.Description ?? string.Empty,
                    QuantityOrdered = l.QuantityOrdered,
                    QuantityAllocated = l.QuantityAllocated,
                    QuantityPicked = qtyPicked,
                    QuantityPacked = qtyPacked,
                    RemainingToPack = Math.Max(0m, l.QuantityOrdered - qtyPacked),
                    LineStatus = l.Status
                };
            })
            .ToList();

        var pickedToteHus = await _context.HandlingUnits
            .AsNoTracking()
            .Include(h => h.CurrentLocation)
            .Where(h => !h.IsDeleted &&
                        h.FacilityId == session.FacilityId &&
                        h.CurrentOutboundOrderId == session.OutboundOrderId &&
                        h.UnitType != HandlingUnitType.ShippingCarton)
            .OrderBy(h => h.LpnCode)
            .ToListAsync(cancellationToken);

        var huIds = pickedToteHus.Select(h => h.Id).ToList();
        var stockByHu = huIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _context.StockOnHand
                .AsNoTracking()
                .Where(s => s.HandlingUnitId.HasValue && huIds.Contains(s.HandlingUnitId.Value))
                .GroupBy(s => s.HandlingUnitId!.Value)
                .Select(g => new { HuId = g.Key, Qty = g.Sum(x => x.QtyAllocated + x.QtyAvailable) })
                .ToDictionaryAsync(x => x.HuId, x => x.Qty, cancellationToken);

        var pickedTotes = pickedToteHus.Select(h => new PackingToteSummaryDto
        {
            HandlingUnitId = h.Id,
            LpnCode = h.LpnCode,
            Status = h.Status,
            LocationCode = h.CurrentLocation?.LocationCode,
            TotalQuantity = stockByHu.GetValueOrDefault(h.Id, 0m)
        }).ToList();

        return new PackingSessionDetailDto
        {
            Id = session.Id,
            ClientId = session.ClientId,
            FacilityId = session.FacilityId,
            PackingStationLocationId = session.PackingStationLocationId,
            PackingStationLocationCode = session.PackingStationLocation?.LocationCode ?? string.Empty,
            OutboundOrderId = session.OutboundOrderId,
            OrderNumber = session.OutboundOrder?.OrderNumber ?? string.Empty,
            OperatorUserId = session.OperatorUserId,
            OperatorEmail = session.OperatorUser?.Email ?? string.Empty,
            Status = session.Status,
            StartedAtUtc = session.StartedAtUtc,
            CompletedAtUtc = session.CompletedAtUtc,
            ActivePackagesCount = packages.Count(p => p.Status != PackageStatus.Cancelled),
            Notes = session.Notes,
            OrderLines = orderLines,
            Packages = packages.Select(PackageMappings.ToSummaryDto).ToList(),
            PickedTotes = pickedTotes
        };
    }
}
