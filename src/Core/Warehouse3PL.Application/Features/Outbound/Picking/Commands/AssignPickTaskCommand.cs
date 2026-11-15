using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Commands;

public record AssignPickTaskCommand(
    Guid PickTaskId,
    Guid OperatorUserId) : IRequest<PickTaskDto>;

public class AssignPickTaskCommandHandler : IRequestHandler<AssignPickTaskCommand, PickTaskDto>
{
    private readonly IApplicationDbContext _context;

    public AssignPickTaskCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PickTaskDto> Handle(
        AssignPickTaskCommand command,
        CancellationToken cancellationToken)
    {
        var task = await _context.PickTasks
            .Include(t => t.SourceLocation)
            .Include(t => t.Item)
            .FirstOrDefaultAsync(t => t.Id == command.PickTaskId, cancellationToken);

        if (task == null)
        {
            throw new EntityNotFoundException(nameof(task), command.PickTaskId);
        }

        task.Assign(command.OperatorUserId);
        await _context.SaveChangesAsync(cancellationToken);

        return new PickTaskDto
        {
            Id = task.Id,
            PickWaveId = task.PickWaveId,
            OutboundOrderId = task.OutboundOrderId,
            OutboundOrderLineId = task.OutboundOrderLineId,
            InventoryAllocationId = task.InventoryAllocationId,
            StockOnHandId = task.StockOnHandId,
            ItemId = task.ItemId,
            Sku = task.Item?.Sku ?? string.Empty,
            SourceLocationId = task.SourceLocationId,
            SourceLocationCode = task.SourceLocation?.LocationCode ?? string.Empty,
            QuantityToPick = task.QuantityToPick,
            QuantityPicked = task.QuantityPicked,
            ShortPickQuantity = task.ShortPickQuantity,
            PickSequence = task.PickSequence,
            Status = task.Status,
            AssignedUserId = task.AssignedUserId
        };
    }
}

