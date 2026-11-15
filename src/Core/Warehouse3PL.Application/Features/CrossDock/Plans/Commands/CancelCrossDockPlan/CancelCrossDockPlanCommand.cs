using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.CrossDock.Plans.Commands.CancelCrossDockPlan;

public record CancelCrossDockPlanCommand(Guid Id, string Reason) : IRequest<CrossDockPlanDto>;

public class CancelCrossDockPlanCommandValidator : AbstractValidator<CancelCrossDockPlanCommand>
{
    public CancelCrossDockPlanCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Plan Id is required.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Reason is required.");
    }
}

public class CancelCrossDockPlanCommandHandler : IRequestHandler<CancelCrossDockPlanCommand, CrossDockPlanDto>
{
    private readonly IApplicationDbContext _context;

    public CancelCrossDockPlanCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CrossDockPlanDto> Handle(CancelCrossDockPlanCommand request, CancellationToken cancellationToken)
    {
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var plan = await _context.CrossDockPlans
                .Include(p => p.Lines)
                    .ThenInclude(l => l.Allocations)
                        .ThenInclude(a => a.OutboundOrderLine)
                .Include(p => p.Tasks)
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (plan == null)
            {
                throw new EntityNotFoundException(nameof(CrossDockPlan), request.Id);
            }

            plan.Cancel(request.Reason);

            foreach (var line in plan.Lines)
            {
                foreach (var alloc in line.Allocations)
                {
                    if (alloc.Status == CrossDockAllocationStatus.Reserved)
                    {
                        // Release inventory allocation back to available stock
                        var sourceStock = await _context.StockOnHand
                            .FirstOrDefaultAsync(s =>
                                s.ClientId == alloc.ClientId &&
                                s.FacilityId == alloc.FacilityId &&
                                s.ItemId == alloc.ItemId &&
                                s.LocationId == line.SourceLocationId, cancellationToken);

                        if (sourceStock != null)
                        {
                            sourceStock.QtyAllocated = Math.Max(0, sourceStock.QtyAllocated - alloc.AllocatedQuantity);
                            sourceStock.QtyAvailable += alloc.AllocatedQuantity;
                            sourceStock.Version++;
                        }

                        // Deallocate from outbound order
                        alloc.OutboundOrderLine?.Deallocate(alloc.AllocatedQuantity);
                        alloc.Status = CrossDockAllocationStatus.Released;
                    }
                }
                line.Status = CrossDockLineStatus.Cancelled;
            }

            foreach (var task in plan.Tasks)
            {
                if (task.Status != CrossDockTaskStatus.Completed)
                {
                    task.Cancel(request.Reason);
                }
            }

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
                CompletedAtUtc = plan.CompletedAtUtc,
                Version = plan.Version
            };
        }, cancellationToken);
    }
}

