using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Commands;

public record GeneratePickWaveCommand(PickWaveGenerationRequest Request) : IRequest<PickWaveDto>;

public class GeneratePickWaveCommandHandler : IRequestHandler<GeneratePickWaveCommand, PickWaveDto>
{
    private readonly IPickWaveGenerator _waveGenerator;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GeneratePickWaveCommandHandler(
        IPickWaveGenerator waveGenerator,
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _waveGenerator = waveGenerator;
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<PickWaveDto> Handle(
        GeneratePickWaveCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Request;
        _outboundAuth.ValidateStaffMutationFacilityAccess(request.FacilityId, nameof(GeneratePickWaveCommand));

        // Execute wave planning and task generation (read-only calculation & graph construction)
        var result = await _waveGenerator.GenerateWaveAsync(request, cancellationToken);
        var wave = result.Wave;

        // Transactionally persist wave, wave orders, and tasks
        await _context.ExecuteInTransactionAsync(async () =>
        {
            _context.PickWaves.Add(wave);

            foreach (var wo in wave.WaveOrders)
            {
                _context.PickWaveOrders.Add(wo);
            }

            foreach (var task in result.Tasks)
            {
                _context.PickTasks.Add(task);
            }

            await _context.SaveChangesAsync(cancellationToken);
            return wave.Id;
        }, cancellationToken);

        // Map to DTO
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
            EstimatedTravelMeters = result.Route.EstimatedTravelMeters,
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
                QuantityToPick = t.QuantityToPick,
                QuantityPicked = t.QuantityPicked,
                ShortPickQuantity = t.ShortPickQuantity,
                PickSequence = t.PickSequence,
                Status = t.Status,
                ShortPickReason = t.ShortPickReason
            }).ToList()
        };
    }
}

