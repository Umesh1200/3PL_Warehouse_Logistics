using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.ConfirmCrossDockTask;

public record ConfirmCrossDockTaskCommand : IRequest<CrossDockTaskDto>
{
    public Guid TaskId { get; init; }
    public string ScannedLpn { get; init; } = string.Empty;
    public string ScannedDestinationBarcode { get; init; } = string.Empty;
    public bool AllowOverride { get; init; } = false;
}

public class ConfirmCrossDockTaskCommandValidator : AbstractValidator<ConfirmCrossDockTaskCommand>
{
    public ConfirmCrossDockTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty().WithMessage("TaskId is required.");
        RuleFor(x => x.ScannedLpn).NotEmpty().WithMessage("ScannedLpn is required for dual-scan verification.");
        RuleFor(x => x.ScannedDestinationBarcode).NotEmpty().WithMessage("ScannedDestinationBarcode is required.");
    }
}

public class ConfirmCrossDockTaskCommandHandler : IRequestHandler<ConfirmCrossDockTaskCommand, CrossDockTaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public ConfirmCrossDockTaskCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<CrossDockTaskDto> Handle(ConfirmCrossDockTaskCommand request, CancellationToken cancellationToken)
    {
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var task = await _context.CrossDockTasks
                .Include(t => t.CrossDockPlan)
                .Include(t => t.CrossDockPlanLine)
                    .ThenInclude(l => l.Allocations)
                .Include(t => t.Item)
                .Include(t => t.Lot)
                .Include(t => t.SourceLocation)
                .Include(t => t.DestinationLocation)
                .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken);

            if (task == null)
            {
                throw new EntityNotFoundException(nameof(CrossDockTask), request.TaskId);
            }

            if (task.Status == CrossDockTaskStatus.Completed)
            {
                throw new ValidationException(nameof(request.TaskId), "This cross-dock task has already been completed.");
            }

            // 1. Dual-Verification Step 1: Inbound LPN scan match
            if (!string.IsNullOrWhiteSpace(task.Lpn))
            {
                if (!task.Lpn.Trim().Equals(request.ScannedLpn.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException(
                        nameof(request.ScannedLpn),
                        $"Scanned LPN '{request.ScannedLpn}' does not match cross-dock task LPN '{task.Lpn}'.");
                }
            }

            // 2. Dual-Verification Step 2: Destination location barcode scan match
            var scannedDest = await _context.Locations
                .FirstOrDefaultAsync(l =>
                    l.FacilityId == task.FacilityId &&
                    !l.IsDeleted &&
                    (l.Barcode == request.ScannedDestinationBarcode || l.LocationCode == request.ScannedDestinationBarcode),
                    cancellationToken);

            if (scannedDest == null)
            {
                throw new ValidationException(
                    nameof(request.ScannedDestinationBarcode),
                    $"Destination location barcode '{request.ScannedDestinationBarcode}' is not recognized in this facility.");
            }

            if (scannedDest.Id != task.DestinationLocationId && !request.AllowOverride)
            {
                throw new ValidationException(
                    nameof(request.ScannedDestinationBarcode),
                    $"Scanned location barcode '{request.ScannedDestinationBarcode}' does not match planned destination '{task.DestinationLocation.LocationCode}'. Override not authorized.");
            }

            // MDM21-R03/R04: Lock cross-dock destination and verify active, unlocked, undamaged status
            await using var cdLock = await _context.AcquirePutawayExecutionLocksAsync(
                null, scannedDest.Id, cancellationToken);

            var actualDestLocation = await _context.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == scannedDest.Id &&
                                          l.FacilityId == task.FacilityId &&
                                          !l.IsDeleted &&
                                          l.IsActive &&
                                          l.Status == LocationStatus.Active &&
                                          !l.IsLocked &&
                                          !l.IsDamaged,
                    cancellationToken)
                ?? throw new ValidationException(
                    nameof(request.ScannedDestinationBarcode),
                    $"Cross-dock destination location '{scannedDest.LocationCode}' is inactive, locked, or damaged.");

            // 3. Atomic Ledger Relocation: Decrement QtyAllocated at Source Staging
            var sourceStock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == task.ClientId &&
                    s.FacilityId == task.FacilityId &&
                    s.ItemId == task.ItemId &&
                    s.LocationId == task.SourceLocationId &&
                    s.LotId == task.LotId, cancellationToken);

            if (sourceStock != null)
            {
                sourceStock.QtyAllocated = Math.Max(0, sourceStock.QtyAllocated - task.Quantity);
                sourceStock.Version++;
            }

            // 4. Atomic Ledger Relocation: Increment QtyAllocated at Outbound Staging Destination
            var destStock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == task.ClientId &&
                    s.FacilityId == task.FacilityId &&
                    s.ItemId == task.ItemId &&
                    s.LocationId == actualDestLocation.Id &&
                    s.LotId == task.LotId, cancellationToken);

            if (destStock == null)
            {
                destStock = new StockOnHand
                {
                    ClientId = task.ClientId,
                    FacilityId = task.FacilityId,
                    ItemId = task.ItemId,
                    LocationId = actualDestLocation.Id,
                    LotId = task.LotId,
                    QtyAvailable = 0,
                    QtyAllocated = task.Quantity,
                    Version = 1
                };
                _context.StockOnHand.Add(destStock);
            }
            else
            {
                destStock.QtyAllocated += task.Quantity;
                destStock.Version++;
            }

            // 5. Immutable Ledger Transaction
            var tx = new InventoryTransaction
            {
                ClientId = task.ClientId,
                FacilityId = task.FacilityId,
                ItemId = task.ItemId,
                LotId = task.LotId,
                FromLocationId = task.SourceLocationId,
                ToLocationId = actualDestLocation.Id,
                TxType = InventoryTxType.Transfer,
                Quantity = task.Quantity,
                ReferenceType = "CROSS_DOCK_TASK",
                ReferenceId = task.Id,
                UserId = _currentUser.UserId,
                Notes = $"Cross-dock flow-through transfer of LPN '{task.Lpn}' to '{actualDestLocation.LocationCode}'",
                TxTimestamp = DateTime.UtcNow
            };
            _context.InventoryTransactions.Add(tx);

            // 6. Complete task and update plan line & allocations
            task.ConfirmExecution(request.ScannedLpn, request.ScannedDestinationBarcode, actualDestLocation.Id, _currentUser.UserId ?? Guid.Empty);

            foreach (var alloc in task.CrossDockPlanLine.Allocations)
            {
                alloc.Status = CrossDockAllocationStatus.Transferred;
                alloc.TransferredAtUtc = DateTime.UtcNow;
            }

            task.CrossDockPlanLine.ExecutedQuantity += task.Quantity;
            if (task.CrossDockPlanLine.ExecutedQuantity >= task.CrossDockPlanLine.PlannedQuantity)
            {
                task.CrossDockPlanLine.Status = CrossDockLineStatus.Completed;
            }

            task.CrossDockPlan.TotalExecutedQuantity += task.Quantity;
            if (task.CrossDockPlan.TotalExecutedQuantity >= task.CrossDockPlan.TotalPlannedQuantity)
            {
                task.CrossDockPlan.Complete();
            }

            await _context.SaveChangesAsync(cancellationToken);

            return new CrossDockTaskDto
            {
                Id = task.Id,
                TaskNumber = task.TaskNumber,
                CrossDockPlanId = task.CrossDockPlanId,
                CrossDockPlanLineId = task.CrossDockPlanLineId,
                ClientId = task.ClientId,
                FacilityId = task.FacilityId,
                ItemId = task.ItemId,
                Sku = task.Item.Sku,
                ItemDescription = task.Item.Description,
                LotId = task.LotId,
                LotNumber = task.Lot?.LotNumber,
                Lpn = task.Lpn,
                SourceLocationId = task.SourceLocationId,
                SourceLocationCode = task.SourceLocation.LocationCode,
                DestinationLocationId = task.DestinationLocationId,
                DestinationLocationCode = task.DestinationLocation.LocationCode,
                DestinationLocationBarcode = task.DestinationLocation.Barcode,
                ActualDestinationLocationId = task.ActualDestinationLocationId,
                ActualDestinationLocationCode = actualDestLocation.LocationCode,
                Quantity = task.Quantity,
                Status = task.Status,
                Priority = task.Priority,
                AssignedUserId = task.AssignedUserId,
                CreatedAtUtc = task.CreatedAtUtc,
                CompletedAtUtc = task.CompletedAtUtc,
                Notes = task.Notes,
                Version = task.Version
            };
        }, cancellationToken);
    }
}

