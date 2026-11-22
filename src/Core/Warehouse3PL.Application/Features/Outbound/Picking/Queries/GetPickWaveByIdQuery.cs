using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Queries;

public record GetPickWaveByIdQuery(Guid Id) : IRequest<PickWaveDto>;

public class GetPickWaveByIdQueryHandler : IRequestHandler<GetPickWaveByIdQuery, PickWaveDto>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPickWaveByIdQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PickWaveDto> Handle(
        GetPickWaveByIdQuery query,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPickWaveByIdQuery));

        var wave = await _context.PickWaves
            .Include(w => w.WaveOrders)
                .ThenInclude(wo => wo.OutboundOrder)
            .Include(w => w.Tasks)
                .ThenInclude(t => t.Item)
            .Include(w => w.Tasks)
                .ThenInclude(t => t.SourceLocation)
            .Include(w => w.Tasks)
                .ThenInclude(t => t.DestinationLocation)
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == query.Id, cancellationToken);

        if (wave == null)
        {
            throw new EntityNotFoundException(nameof(wave), query.Id);
        }

        _outboundAuth.ValidateStaffReadFacilityAccess(wave.FacilityId, nameof(GetPickWaveByIdQuery));

        return new PickWaveDto
        {
            Id = wave.Id,
            ClientId = wave.ClientId,
            FacilityId = wave.FacilityId,
            WaveNumber = wave.WaveNumber,
            WaveType = wave.WaveType,
            Status = wave.Status,
            Priority = wave.Priority,
            ZoneId = wave.ZoneId,
            TempClass = wave.TempClass,
            AssignedToUserId = wave.AssignedToUserId,
            ReleasedAtUtc = wave.ReleasedAtUtc,
            StartedAtUtc = wave.StartedAtUtc,
            CompletedAtUtc = wave.CompletedAtUtc,
            CreatedAt = wave.CreatedAt,
            OrdersCount = wave.WaveOrders.Count,
            TasksCount = wave.Tasks.Count,
            TotalQuantityToPick = wave.Tasks.Sum(t => t.QuantityToPick),
            TotalQuantityPicked = wave.Tasks.Sum(t => t.QuantityPicked),
            WaveOrders = wave.WaveOrders.Select(wo => new PickWaveOrderDto
            {
                Id = wo.Id,
                OutboundOrderId = wo.OutboundOrderId,
                OrderNumber = wo.OutboundOrder?.OrderNumber ?? string.Empty,
                CustomerName = wo.OutboundOrder?.CustomerName ?? string.Empty,
                Priority = wo.OutboundOrder?.Priority ?? 1,
                SequenceNumber = wo.SequenceNumber,
                OrderStatus = wo.OutboundOrder?.Status ?? Domain.Enums.OutboundOrderStatus.Released
            }).ToList(),
            Tasks = wave.Tasks.OrderBy(t => t.PickSequence).Select(t => new PickTaskDto
            {
                Id = t.Id,
                PickWaveId = t.PickWaveId,
                OutboundOrderId = t.OutboundOrderId,
                OrderNumber = t.OutboundOrder?.OrderNumber ?? string.Empty,
                OutboundOrderLineId = t.OutboundOrderLineId,
                InventoryAllocationId = t.InventoryAllocationId,
                StockOnHandId = t.StockOnHandId,
                ItemId = t.ItemId,
                Sku = t.Item?.Sku ?? string.Empty,
                ItemDescription = t.Item?.Description ?? string.Empty,
                SourceLocationId = t.SourceLocationId,
                SourceLocationCode = t.SourceLocation?.LocationCode ?? string.Empty,
                DestinationLocationId = t.DestinationLocationId,
                DestinationLocationCode = t.DestinationLocation?.LocationCode,
                LotId = t.LotId,
                ExpectedLpnCode = t.ExpectedLpnCode,
                TargetHandlingUnitId = t.TargetHandlingUnitId,
                TargetToteBarcode = t.TargetToteBarcode,
                QuantityToPick = t.QuantityToPick,
                QuantityPicked = t.QuantityPicked,
                ShortPickQuantity = t.ShortPickQuantity,
                PickSequence = t.PickSequence,
                Status = t.Status,
                ShortPickReason = t.ShortPickReason,
                AssignedUserId = t.AssignedUserId,
                StartedAtUtc = t.StartedAtUtc,
                CompletedAtUtc = t.CompletedAtUtc
            }).ToList()
        };
    }
}
