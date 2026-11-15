using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Commands;

public record ReleasePickWaveCommand(Guid PickWaveId) : IRequest<PickWaveDto>;

public class ReleasePickWaveCommandHandler : IRequestHandler<ReleasePickWaveCommand, PickWaveDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public ReleasePickWaveCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PickWaveDto> Handle(
        ReleasePickWaveCommand command,
        CancellationToken cancellationToken)
    {
        var wave = await _context.PickWaves
            .Include(w => w.WaveOrders)
                .ThenInclude(wo => wo.OutboundOrder)
            .Include(w => w.Tasks)
                .ThenInclude(t => t.Item)
            .Include(w => w.Tasks)
                .ThenInclude(t => t.SourceLocation)
            .FirstOrDefaultAsync(w => w.Id == command.PickWaveId, cancellationToken);

        if (wave == null)
        {
            throw new EntityNotFoundException(nameof(wave), command.PickWaveId);
        }

        wave.Release(_currentUser.UserId ?? Guid.Empty);
        await _context.SaveChangesAsync(cancellationToken);

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
            Tasks = wave.Tasks.Select(t => new PickTaskDto
            {
                Id = t.Id,
                PickWaveId = t.PickWaveId,
                OutboundOrderId = t.OutboundOrderId,
                OutboundOrderLineId = t.OutboundOrderLineId,
                InventoryAllocationId = t.InventoryAllocationId,
                StockOnHandId = t.StockOnHandId,
                ItemId = t.ItemId,
                Sku = t.Item?.Sku ?? string.Empty,
                SourceLocationId = t.SourceLocationId,
                SourceLocationCode = t.SourceLocation?.LocationCode ?? string.Empty,
                QuantityToPick = t.QuantityToPick,
                QuantityPicked = t.QuantityPicked,
                ShortPickQuantity = t.ShortPickQuantity,
                PickSequence = t.PickSequence,
                Status = t.Status
            }).ToList()
        };
    }
}
