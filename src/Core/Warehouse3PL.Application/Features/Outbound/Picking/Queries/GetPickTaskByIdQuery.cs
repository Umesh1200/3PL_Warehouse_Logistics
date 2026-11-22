using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Queries;

public record GetPickTaskByIdQuery(Guid Id) : IRequest<PickTaskDto>;

public class GetPickTaskByIdQueryHandler : IRequestHandler<GetPickTaskByIdQuery, PickTaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetPickTaskByIdQueryHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PickTaskDto> Handle(GetPickTaskByIdQuery request, CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetPickTaskByIdQuery));

        var task = await _context.PickTasks
            .Include(t => t.SourceLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.OutboundOrder)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);

        if (task == null)
        {
            throw new EntityNotFoundException(nameof(PickTask), request.Id);
        }

        _outboundAuth.ValidateStaffReadFacilityAccess(task.FacilityId, nameof(GetPickTaskByIdQuery));

        return new PickTaskDto
        {
            Id = task.Id,
            PickWaveId = task.PickWaveId,
            OutboundOrderId = task.OutboundOrderId,
            OrderNumber = task.OutboundOrder?.OrderNumber ?? string.Empty,
            OutboundOrderLineId = task.OutboundOrderLineId,
            InventoryAllocationId = task.InventoryAllocationId,
            StockOnHandId = task.StockOnHandId,
            ItemId = task.ItemId,
            Sku = task.Item?.Sku ?? string.Empty,
            ItemDescription = task.Item?.Description ?? string.Empty,
            SourceLocationId = task.SourceLocationId,
            SourceLocationCode = task.SourceLocation?.LocationCode ?? string.Empty,
            DestinationLocationId = task.DestinationLocationId,
            DestinationLocationCode = task.DestinationLocation?.LocationCode,
            LotId = task.LotId,
            LotNumber = task.Lot?.LotNumber,
            ExpectedLpnCode = task.ExpectedLpnCode,
            TargetHandlingUnitId = task.TargetHandlingUnitId,
            TargetToteBarcode = task.TargetToteBarcode,
            QuantityToPick = task.QuantityToPick,
            QuantityPicked = task.QuantityPicked,
            ShortPickQuantity = task.ShortPickQuantity,
            PickSequence = task.PickSequence,
            Status = task.Status,
            ShortPickReason = task.ShortPickReason,
            AssignedUserId = task.AssignedUserId,
            StartedAtUtc = task.StartedAtUtc,
            CompletedAtUtc = task.CompletedAtUtc
        };
    }
}
