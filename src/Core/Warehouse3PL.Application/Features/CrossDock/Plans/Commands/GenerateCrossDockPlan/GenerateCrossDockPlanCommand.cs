using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.CrossDock.Plans.Commands.GenerateCrossDockPlan;

public record GenerateCrossDockPlanCommand : IRequest<CrossDockPlanDto>
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? ReceivingSessionId { get; init; }
    public Guid? TargetOutboundLocationId { get; init; }
    public string? Notes { get; init; }
}

public class GenerateCrossDockPlanCommandValidator : AbstractValidator<GenerateCrossDockPlanCommand>
{
    public GenerateCrossDockPlanCommandValidator()
    {
        RuleFor(x => x.FacilityId).NotEmpty().WithMessage("FacilityId is required.");
    }
}

public class GenerateCrossDockPlanCommandHandler : IRequestHandler<GenerateCrossDockPlanCommand, CrossDockPlanDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICrossDockEngine _crossDockEngine;

    public GenerateCrossDockPlanCommandHandler(
        IApplicationDbContext context,
        ICrossDockEngine crossDockEngine)
    {
        _context = context;
        _crossDockEngine = crossDockEngine;
    }

    public async Task<CrossDockPlanDto> Handle(GenerateCrossDockPlanCommand request, CancellationToken cancellationToken)
    {
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var opportunities = await _crossDockEngine.DetectOpportunitiesAsync(
                request.FacilityId,
                request.ClientId,
                request.ReceivingSessionId,
                cancellationToken);

            if (opportunities.Count == 0)
            {
                throw new ValidationException(nameof(request.FacilityId), "No eligible cross-dock opportunities found for the specified criteria.");
            }

            // Determine Target Outbound Staging Location
            Location? targetLocation = null;
            if (request.TargetOutboundLocationId.HasValue)
            {
                targetLocation = await _context.Locations
                    .FirstOrDefaultAsync(l => l.Id == request.TargetOutboundLocationId.Value && l.FacilityId == request.FacilityId, cancellationToken);
            }

            if (targetLocation == null)
            {
                // Fall back to first available staging lane or outbound dock door
                targetLocation = await _context.Locations
                    .FirstOrDefaultAsync(l =>
                        l.FacilityId == request.FacilityId &&
                        !l.IsDeleted &&
                        l.IsActive &&
                        (l.LocationType == LocationType.StagingLane || l.LocationType == LocationType.DockDoor),
                        cancellationToken);

                if (targetLocation == null)
                {
                    throw new ValidationException(nameof(request.TargetOutboundLocationId), "No outbound staging or dock location found in this facility.");
                }
            }

            var planNumber = $"XDK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
            var resolvedClientId = request.ClientId ?? opportunities[0].MatchedOutboundOrderId; // will resolve per line

            // Group by primary client if multiple
            var primaryClientId = request.ClientId.HasValue
                ? request.ClientId.Value
                : await _context.OutboundOrders
                    .Where(o => o.Id == opportunities[0].MatchedOutboundOrderId)
                    .Select(o => o.ClientId)
                    .FirstAsync(cancellationToken);

            var plan = new CrossDockPlan
            {
                ClientId = primaryClientId,
                FacilityId = request.FacilityId,
                ReceivingSessionId = request.ReceivingSessionId,
                PlanNumber = planNumber,
                Status = CrossDockPlanStatus.Approved,
                Notes = request.Notes ?? "Auto-generated from matching inbound freight against unfulfilled demand.",
                CreatedAtUtc = DateTime.UtcNow
            };

            decimal totalPlannedQty = 0;

            foreach (var opp in opportunities)
            {
                var orderLine = await _context.OutboundOrderLines
                    .Include(l => l.OutboundOrder)
                    .FirstOrDefaultAsync(l => l.Id == opp.MatchedOutboundOrderLineId, cancellationToken);

                if (orderLine == null || orderLine.RemainingDemand <= 0)
                {
                    continue;
                }

                // Verify and reserve stock in StockOnHand at source location
                var sourceStock = await _context.StockOnHand
                    .FirstOrDefaultAsync(s =>
                        s.FacilityId == request.FacilityId &&
                        s.LocationId == opp.SourceStagingLocationId &&
                        s.ItemId == opp.ItemId, cancellationToken);

                if (sourceStock == null || sourceStock.QtyAvailable <= 0)
                {
                    continue;
                }

                var allocatableQty = Math.Min(opp.RecommendedCrossDockQty, sourceStock.QtyAvailable);
                allocatableQty = Math.Min(allocatableQty, orderLine.RemainingDemand);

                if (allocatableQty <= 0)
                {
                    continue;
                }

                // 1. Invariant: Atomic reservation prevents stock from being put away
                sourceStock.QtyAvailable -= allocatableQty;
                sourceStock.QtyAllocated += allocatableQty;
                sourceStock.Version++;

                // 2. Outbound demand reservation
                orderLine.Allocate(allocatableQty);

                // 3. Create Plan Line
                var planLine = new CrossDockPlanLine
                {
                    CrossDockPlanId = plan.Id,
                    ItemId = opp.ItemId,
                    ReceiptLineId = opp.ReceiptLineId,
                    InboundLpn = opp.InboundLpn,
                    SourceLocationId = opp.SourceStagingLocationId,
                    TargetLocationId = targetLocation.Id,
                    InboundAvailableQuantity = opp.InboundAvailableQty,
                    OutboundDemandQuantity = opp.OrderDemandedQty,
                    PlannedQuantity = allocatableQty,
                    Status = CrossDockLineStatus.Planned
                };

                // 4. Create Allocation Link
                var allocation = new CrossDockAllocation
                {
                    CrossDockPlanLineId = planLine.Id,
                    ClientId = orderLine.OutboundOrder.ClientId,
                    FacilityId = request.FacilityId,
                    ItemId = opp.ItemId,
                    InboundLpn = opp.InboundLpn,
                    OutboundOrderId = orderLine.OutboundOrderId,
                    OutboundOrderLineId = orderLine.Id,
                    AllocatedQuantity = allocatableQty,
                    Status = CrossDockAllocationStatus.Reserved,
                    AllocatedAtUtc = DateTime.UtcNow
                };
                planLine.Allocations.Add(allocation);

                // 5. Create Directed Cross-Dock Task
                var priority = CrossDockPriority.Normal;
                if (opp.StorageTempClass == StorageTempClass.Frozen || opp.StorageTempClass == StorageTempClass.Refrigerated)
                {
                    priority = CrossDockPriority.CriticalColdChain;
                }
                else if (orderLine.OutboundOrder.Priority >= 2)
                {
                    priority = CrossDockPriority.Urgent;
                }

                var task = new CrossDockTask
                {
                    CrossDockPlanId = plan.Id,
                    CrossDockPlanLineId = planLine.Id,
                    ClientId = orderLine.OutboundOrder.ClientId,
                    FacilityId = request.FacilityId,
                    ItemId = opp.ItemId,
                    TaskNumber = $"XDK-TSK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
                    Lpn = opp.InboundLpn,
                    SourceLocationId = opp.SourceStagingLocationId,
                    DestinationLocationId = targetLocation.Id,
                    Quantity = allocatableQty,
                    Status = CrossDockTaskStatus.Pending,
                    Priority = priority,
                    CreatedAtUtc = DateTime.UtcNow,
                    Notes = $"Direct from receiving to outbound staging for Order {orderLine.OutboundOrder.OrderNumber}"
                };

                plan.Lines.Add(planLine);
                plan.Tasks.Add(task);
                totalPlannedQty += allocatableQty;
            }

            plan.TotalPlannedQuantity = totalPlannedQty;
            _context.CrossDockPlans.Add(plan);

            await _context.SaveChangesAsync(cancellationToken);

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
                Version = plan.Version,
                Lines = plan.Lines.Select(l => new CrossDockPlanLineDto
                {
                    Id = l.Id,
                    ItemId = l.ItemId,
                    InboundLpn = l.InboundLpn,
                    SourceLocationId = l.SourceLocationId,
                    TargetLocationId = l.TargetLocationId,
                    InboundAvailableQuantity = l.InboundAvailableQuantity,
                    OutboundDemandQuantity = l.OutboundDemandQuantity,
                    PlannedQuantity = l.PlannedQuantity,
                    Status = l.Status,
                    Allocations = l.Allocations.Select(a => new CrossDockAllocationDto
                    {
                        Id = a.Id,
                        OutboundOrderId = a.OutboundOrderId,
                        OutboundOrderLineId = a.OutboundOrderLineId,
                        AllocatedQuantity = a.AllocatedQuantity,
                        Status = a.Status,
                        AllocatedAtUtc = a.AllocatedAtUtc
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
                    Lpn = t.Lpn,
                    SourceLocationId = t.SourceLocationId,
                    DestinationLocationId = t.DestinationLocationId,
                    Quantity = t.Quantity,
                    Status = t.Status,
                    Priority = t.Priority,
                    CreatedAtUtc = t.CreatedAtUtc,
                    Notes = t.Notes,
                    Version = t.Version
                }).ToList()
            };
        }, cancellationToken);
    }
}
