using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.CrossDock.Plans.Queries.GetCrossDockPlanById;

public record GetCrossDockPlanByIdQuery(Guid Id) : IRequest<CrossDockPlanDto>;

public class GetCrossDockPlanByIdQueryHandler : IRequestHandler<GetCrossDockPlanByIdQuery, CrossDockPlanDto>
{
    private readonly IApplicationDbContext _context;

    public GetCrossDockPlanByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CrossDockPlanDto> Handle(GetCrossDockPlanByIdQuery request, CancellationToken cancellationToken)
    {
        var plan = await _context.CrossDockPlans
            .AsNoTracking()
            .Include(p => p.Lines)
                .ThenInclude(l => l.Item)
            .Include(p => p.Lines)
                .ThenInclude(l => l.SourceLocation)
            .Include(p => p.Lines)
                .ThenInclude(l => l.TargetLocation)
            .Include(p => p.Lines)
                .ThenInclude(l => l.Allocations)
                    .ThenInclude(a => a.OutboundOrder)
            .Include(p => p.Tasks)
                .ThenInclude(t => t.Item)
            .Include(p => p.Tasks)
                .ThenInclude(t => t.SourceLocation)
            .Include(p => p.Tasks)
                .ThenInclude(t => t.DestinationLocation)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (plan == null)
        {
            throw new EntityNotFoundException(nameof(CrossDockPlan), request.Id);
        }

        return new CrossDockPlanDto
        {
            Id = plan.Id,
            PlanNumber = plan.PlanNumber,
            ClientId = plan.ClientId,
            FacilityId = plan.FacilityId,
            ReceivingSessionId = plan.ReceivingSessionId,
            Status = plan.Status,
            TotalPlannedQuantity = plan.TotalPlannedQuantity,
            TotalExecutedQuantity = plan.TotalExecutedQuantity,
            Notes = plan.Notes,
            CreatedAtUtc = plan.CreatedAtUtc,
            CompletedAtUtc = plan.CompletedAtUtc,
            Version = plan.Version,
            Lines = plan.Lines.Select(l => new CrossDockPlanLineDto
            {
                Id = l.Id,
                ItemId = l.ItemId,
                Sku = l.Item.Sku,
                ItemDescription = l.Item.Description,
                InboundLpn = l.InboundLpn,
                SourceLocationId = l.SourceLocationId,
                SourceLocationCode = l.SourceLocation.LocationCode,
                TargetLocationId = l.TargetLocationId,
                TargetLocationCode = l.TargetLocation.LocationCode,
                InboundAvailableQuantity = l.InboundAvailableQuantity,
                OutboundDemandQuantity = l.OutboundDemandQuantity,
                PlannedQuantity = l.PlannedQuantity,
                ExecutedQuantity = l.ExecutedQuantity,
                Status = l.Status,
                Allocations = l.Allocations.Select(a => new CrossDockAllocationDto
                {
                    Id = a.Id,
                    OutboundOrderId = a.OutboundOrderId,
                    OutboundOrderNumber = a.OutboundOrder.OrderNumber,
                    OutboundOrderLineId = a.OutboundOrderLineId,
                    AllocatedQuantity = a.AllocatedQuantity,
                    Status = a.Status,
                    AllocatedAtUtc = a.AllocatedAtUtc,
                    TransferredAtUtc = a.TransferredAtUtc
                }).ToList()
            }).ToList(),
            Tasks = plan.Tasks.Select(t => new CrossDockTaskDto
            {
                Id = t.Id,
                TaskNumber = t.TaskNumber,
                CrossDockPlanId = t.CrossDockPlanId,
                CrossDockPlanLineId = t.CrossDockPlanLineId,
                ClientId = t.ClientId,
                FacilityId = t.FacilityId,
                ItemId = t.ItemId,
                Sku = t.Item?.Sku ?? string.Empty,
                ItemDescription = t.Item?.Description ?? string.Empty,
                Lpn = t.Lpn,
                SourceLocationId = t.SourceLocationId,
                SourceLocationCode = t.SourceLocation?.LocationCode ?? string.Empty,
                DestinationLocationId = t.DestinationLocationId,
                DestinationLocationCode = t.DestinationLocation?.LocationCode ?? string.Empty,
                DestinationLocationBarcode = t.DestinationLocation?.Barcode ?? string.Empty,
                Quantity = t.Quantity,
                Status = t.Status,
                Priority = t.Priority,
                AssignedUserId = t.AssignedUserId,
                CreatedAtUtc = t.CreatedAtUtc,
                CompletedAtUtc = t.CompletedAtUtc,
                Notes = t.Notes,
                Version = t.Version
            }).ToList()
        };
    }
}

