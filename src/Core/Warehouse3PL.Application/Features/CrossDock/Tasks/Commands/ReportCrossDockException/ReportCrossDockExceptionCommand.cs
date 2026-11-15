using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.ReportCrossDockException;

public record ReportCrossDockExceptionCommand : IRequest<CrossDockExceptionDto>
{
    public Guid TaskId { get; init; }
    public CrossDockExceptionType Type { get; init; } = CrossDockExceptionType.Shortage;
    public decimal DiscrepantQuantity { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool ReleaseRemainingStock { get; init; } = true;
}

public class ReportCrossDockExceptionCommandValidator : AbstractValidator<ReportCrossDockExceptionCommand>
{
    public ReportCrossDockExceptionCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty().WithMessage("TaskId is required.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Reason is required.");
        RuleFor(x => x.DiscrepantQuantity).GreaterThan(0).WithMessage("DiscrepantQuantity must be greater than zero.");
    }
}

public class ReportCrossDockExceptionCommandHandler : IRequestHandler<ReportCrossDockExceptionCommand, CrossDockExceptionDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public ReportCrossDockExceptionCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<CrossDockExceptionDto> Handle(ReportCrossDockExceptionCommand request, CancellationToken cancellationToken)
    {
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var task = await _context.CrossDockTasks
                .Include(t => t.CrossDockPlan)
                .Include(t => t.CrossDockPlanLine)
                    .ThenInclude(l => l.Allocations)
                        .ThenInclude(a => a.OutboundOrderLine)
                .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken);

            if (task == null)
            {
                throw new EntityNotFoundException(nameof(CrossDockTask), request.TaskId);
            }

            task.ReportException(request.Reason);

            if (request.ReleaseRemainingStock)
            {
                var qtyToRelease = Math.Min(request.DiscrepantQuantity, task.Quantity);

                // 1. Release StockOnHand allocation back to available for putaway
                var sourceStock = await _context.StockOnHand
                    .FirstOrDefaultAsync(s =>
                        s.ClientId == task.ClientId &&
                        s.FacilityId == task.FacilityId &&
                        s.ItemId == task.ItemId &&
                        s.LocationId == task.SourceLocationId &&
                        s.LotId == task.LotId, cancellationToken);

                if (sourceStock != null)
                {
                    sourceStock.QtyAllocated = Math.Max(0, sourceStock.QtyAllocated - qtyToRelease);
                    sourceStock.QtyAvailable += qtyToRelease;
                    sourceStock.Version++;
                }

                // 2. Deallocate from outbound order line
                foreach (var alloc in task.CrossDockPlanLine.Allocations)
                {
                    alloc.OutboundOrderLine?.Deallocate(qtyToRelease);
                    alloc.Status = CrossDockAllocationStatus.Released;
                }
            }

            var exception = new CrossDockException
            {
                CrossDockPlanId = task.CrossDockPlanId,
                CrossDockTaskId = task.Id,
                Type = request.Type,
                QuantityDiscrepant = request.DiscrepantQuantity,
                Reason = request.Reason,
                ReportedByUserId = _currentUser.UserId,
                ReportedAtUtc = DateTime.UtcNow
            };

            _context.CrossDockExceptions.Add(exception);
            await _context.SaveChangesAsync(cancellationToken);

            return new CrossDockExceptionDto
            {
                Id = exception.Id,
                CrossDockPlanId = exception.CrossDockPlanId,
                CrossDockTaskId = exception.CrossDockTaskId,
                Type = exception.Type,
                QuantityDiscrepant = exception.QuantityDiscrepant,
                Reason = exception.Reason,
                ReportedByUserId = exception.ReportedByUserId,
                ReportedAtUtc = exception.ReportedAtUtc,
                IsResolved = exception.IsResolved
            };
        }, cancellationToken);
    }
}

