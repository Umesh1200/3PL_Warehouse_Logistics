using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
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

    public ConfirmPutawayCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
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

            if (task.Status == PutawayTaskStatus.Completed)
            {
                throw new ValidationException(nameof(request.TaskId), "This putaway task has already been completed.");
            }

            var targetLocationId = request.ConfirmedTargetLocationId ?? task.TargetLocationId;
            var putawayQty = request.ConfirmedQuantity ?? task.Quantity;

            // 1. Decrement Stock from Receiving/Source Location
            var sourceStock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == task.ClientId &&
                    s.FacilityId == task.FacilityId &&
                    s.ItemId == task.ItemId &&
                    s.LocationId == task.SourceLocationId &&
                    s.LotId == task.LotId, cancellationToken);

            if (sourceStock != null)
            {
                sourceStock.QtyAvailable = Math.Max(0, sourceStock.QtyAvailable - putawayQty);
                sourceStock.Version++;
            }

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
