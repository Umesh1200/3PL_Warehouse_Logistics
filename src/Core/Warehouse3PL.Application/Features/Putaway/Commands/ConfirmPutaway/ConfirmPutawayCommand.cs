using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Putaway.Commands.ConfirmPutaway;

public record ConfirmPutawayCommand : IRequest<PutawayTaskDto>
{
    public Guid TaskId { get; init; }
    public Guid? ConfirmedTargetLocationId { get; init; } // Optional override by operator
    public decimal? ConfirmedQuantity { get; init; }
}

public class ConfirmPutawayCommandValidator : AbstractValidator<ConfirmPutawayCommand>
{
    public ConfirmPutawayCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
    }
}

public class ConfirmPutawayCommandHandler : IRequestHandler<ConfirmPutawayCommand, PutawayTaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public ConfirmPutawayCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _operationalAuth = operationalAuth;
    }

    public async Task<PutawayTaskDto> Handle(ConfirmPutawayCommand request, CancellationToken cancellationToken)
    {
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var task = await _context.PutawayTasks
                .Include(pt => pt.Item)
                .Include(pt => pt.Lot)
                .Include(pt => pt.SourceLocation)
                .Include(pt => pt.SuggestedLocation)
                .Include(pt => pt.ActualLocation)
                .FirstOrDefaultAsync(pt => pt.Id == request.TaskId, cancellationToken);

            if (task == null)
            {
                throw new EntityNotFoundException(nameof(PutawayTask), request.TaskId);
            }

            await _operationalAuth.ValidateClientFacilityAccessAsync(
                task.ClientId, task.FacilityId, cancellationToken);

            if (task.Status == PutawayTaskStatus.Completed)
            {
                throw new ValidationException(nameof(request.TaskId), "This putaway task has already been completed.");
            }

            var targetLocationId = request.ConfirmedTargetLocationId ?? task.TargetLocationId;
            var putawayQty = request.ConfirmedQuantity ?? task.Quantity;

            if (putawayQty != task.Quantity)
            {
                throw new ValidationException(nameof(request.ConfirmedQuantity),
                    "Putaway quantity must match the server-assigned task quantity.");
            }

            if (targetLocationId != task.TargetLocationId)
            {
                var canOverride = _currentUser.IsSuperAdmin ||
                    _currentUser.HasPermission(Permissions.Inbound.PutawayManage) ||
                    _currentUser.HasPermission(Permissions.Inbound.PutawayOverride) ||
                    _currentUser.HasPermission(Permissions.Inbound.PutawayRules);
                if (!canOverride)
                {
                    throw new ForbiddenException("Current user does not have permission to override the putaway destination.");
                }
            }

            var destination = await _context.Locations.FirstOrDefaultAsync(l =>
                l.Id == targetLocationId && l.FacilityId == task.FacilityId && !l.IsDeleted,
                cancellationToken);
            if (destination == null)
            {
                throw new EntityNotFoundException(nameof(Location), targetLocationId);
            }
            if (!destination.IsActive || destination.IsLocked || destination.IsDamaged ||
                destination.IsReceiving || destination.LocationType == LocationType.StagingLane)
            {
                throw new ValidationException(nameof(request.ConfirmedTargetLocationId),
                    $"Destination location '{destination.LocationCode}' is not a suitable active storage location.");
            }

            var destinationWithZone = await _context.Locations
                .AsNoTracking()
                .Include(l => l.Zone)
                .FirstAsync(l => l.Id == destination.Id, cancellationToken);

            var sourceStockId = await _context.StockOnHand
                .AsNoTracking()
                .Where(s =>
                    s.ClientId == task.ClientId &&
                    s.FacilityId == task.FacilityId &&
                    s.ItemId == task.ItemId &&
                    s.LocationId == task.SourceLocationId &&
                    s.LotId == task.LotId)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            await using var putawayLocks = await _context.AcquirePutawayExecutionLocksAsync(
                sourceStockId, destinationWithZone.Id, cancellationToken);

            if (_context.IsRelationalDatabase && _context is DbContext efDb)
            {
                await efDb.Entry(task).ReloadAsync(cancellationToken);
                if (task.Status == PutawayTaskStatus.Completed)
                {
                    throw new ValidationException(nameof(request.TaskId), "This putaway task has already been completed.");
                }
            }

            await PutawaySafetyValidator.ValidateDestinationSafetyAndCapacityAsync(
                _context, destinationWithZone, task.Item, putawayQty, cancellationToken);

            // 1. Decrement Stock from Receiving/Source Location
            var sourceStock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == task.ClientId &&
                    s.FacilityId == task.FacilityId &&
                    s.ItemId == task.ItemId &&
                    s.LocationId == task.SourceLocationId &&
                    s.LotId == task.LotId, cancellationToken);

            if (sourceStock == null || sourceStock.QtyAvailable < putawayQty)
            {
                throw new ValidationException(nameof(request.ConfirmedQuantity),
                    $"Insufficient source stock. Required: {putawayQty}, Available: {sourceStock?.QtyAvailable ?? 0m}.");
            }

            sourceStock.QtyAvailable -= putawayQty;
            sourceStock.Version++;

            // 2. Increment Stock at Destination Bin
            var destStock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == task.ClientId &&
                    s.FacilityId == task.FacilityId &&
                    s.ItemId == task.ItemId &&
                    s.LocationId == targetLocationId &&
                    s.LotId == task.LotId, cancellationToken);

            if (destStock == null)
            {
                destStock = new StockOnHand
                {
                    ClientId = task.ClientId,
                    FacilityId = task.FacilityId,
                    ItemId = task.ItemId,
                    LocationId = targetLocationId,
                    LotId = task.LotId,
                    QtyAvailable = putawayQty,
                    Version = 1
                };
                _context.StockOnHand.Add(destStock);
            }
            else
            {
                destStock.QtyAvailable += putawayQty;
                destStock.Version++;
            }

            // 3. Mark Task Completed
            task.TargetLocationId = targetLocationId;
            task.Quantity = putawayQty;
            task.Status = PutawayTaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            task.AssignedUserId = _currentUser.UserId;

            // 4. Record Immutable Putaway Ledger Entry
            var tx = new InventoryTransaction
            {
                ClientId = task.ClientId,
                FacilityId = task.FacilityId,
                ItemId = task.ItemId,
                LotId = task.LotId,
                FromLocationId = task.SourceLocationId,
                ToLocationId = targetLocationId,
                TxType = InventoryTxType.Putaway,
                Quantity = putawayQty,
                ReferenceType = "PUTAWAY_TASK",
                ReferenceId = task.Id,
                UserId = _currentUser.UserId,
                Notes = $"Completed putaway to bin location {targetLocationId}",
                TxTimestamp = DateTime.UtcNow
            };
            _context.InventoryTransactions.Add(tx);

            await _context.SaveChangesAsync(cancellationToken);

            return new PutawayTaskDto
            {
                Id = task.Id,
                ClientId = task.ClientId,
                FacilityId = task.FacilityId,
                ItemId = task.ItemId,
                Sku = task.Item.Sku,
                ItemDescription = task.Item.Description,
                LotId = task.LotId,
                LotNumber = task.Lot?.LotNumber,
                SourceLocationId = task.SourceLocationId,
                SourceLocationCode = task.SourceLocation.LocationCode,
                TargetLocationId = task.SuggestedLocationId,
                TargetLocationCode = task.ActualLocation?.LocationCode ?? task.SuggestedLocation?.LocationCode ?? string.Empty,
                Quantity = task.Quantity,
                Status = task.Status,
                AssignedUserId = task.AssignedUserId,
                CompletedAt = task.CompletedAt,
                Notes = task.Notes
            };
        }, cancellationToken);
    }
}
