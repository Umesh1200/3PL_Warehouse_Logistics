using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ConfirmPutawayTask;

public record ConfirmPutawayTaskCommand : IRequest<PutawayTaskDto>
{
    public Guid TaskId { get; init; }

    /// <summary>
    /// Physical scan of the container / pallet LPN barcode.
    /// </summary>
    public string ScannedLpn { get; init; } = string.Empty;

    /// <summary>
    /// Physical scan of the destination bin location barcode tag.
    /// </summary>
    public string ScannedLocationBarcode { get; init; } = string.Empty;

    /// <summary>
    /// Whether operator is explicitly authorized to override the suggested bin.
    /// </summary>
    public bool AllowOverride { get; init; } = false;
}

public class ConfirmPutawayTaskCommandValidator : AbstractValidator<ConfirmPutawayTaskCommand>
{
    public ConfirmPutawayTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty().WithMessage("TaskId is required.");
        RuleFor(x => x.ScannedLpn).NotEmpty().WithMessage("ScannedLpn is required for dual-scan verification.");
        RuleFor(x => x.ScannedLocationBarcode).NotEmpty().WithMessage("ScannedLocationBarcode is required for dual-scan verification.");
    }
}

public class ConfirmPutawayTaskCommandHandler : IRequestHandler<ConfirmPutawayTaskCommand, PutawayTaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public ConfirmPutawayTaskCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PutawayTaskDto> Handle(ConfirmPutawayTaskCommand request, CancellationToken cancellationToken)
    {
        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var task = await _context.PutawayTasks
                .Include(t => t.Item)
                .Include(t => t.Lot)
                .Include(t => t.SourceLocation)
                .Include(t => t.SuggestedLocation)
                .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken);

            if (task == null)
            {
                throw new EntityNotFoundException(nameof(PutawayTask), request.TaskId);
            }

            if (task.Status == PutawayTaskStatus.Completed)
            {
                throw new ValidationException(nameof(request.TaskId), "This putaway task has already been completed.");
            }

            // 1. Dual-Verification Step 1: LPN container barcode scan validation
            if (!string.IsNullOrWhiteSpace(task.LpnCode))
            {
                if (!task.LpnCode.Trim().Equals(request.ScannedLpn.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException(
                        nameof(request.ScannedLpn),
                        $"Scanned LPN barcode '{request.ScannedLpn}' does not match task LPN '{task.LpnCode}'.");
                }
            }

            // 2. Dual-Verification Step 2: Destination location barcode scan validation
            var scannedLoc = await _context.Locations
                .Include(l => l.Zone)
                .FirstOrDefaultAsync(l =>
                    l.FacilityId == task.FacilityId &&
                    !l.IsDeleted &&
                    (l.Barcode == request.ScannedLocationBarcode || l.LocationCode == request.ScannedLocationBarcode),
                    cancellationToken);

            if (scannedLoc == null)
            {
                throw new ValidationException(
                    nameof(request.ScannedLocationBarcode),
                    $"Location barcode '{request.ScannedLocationBarcode}' is not recognized in this facility.");
            }

            if (scannedLoc.Id != task.SuggestedLocationId && !request.AllowOverride)
            {
                throw new ValidationException(
                    nameof(request.ScannedLocationBarcode),
                    $"Scanned location barcode '{request.ScannedLocationBarcode}' ({scannedLoc.LocationCode}) does not match suggested bin '{task.SuggestedLocation.LocationCode}'. Bin override not authorized.");
            }

            var actualLocation = scannedLoc;

            // 3. Atomic Ledger Relocation: Decrement stock from STAGING_LANE
            var sourceStock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == task.ClientId &&
                    s.FacilityId == task.FacilityId &&
                    s.ItemId == task.ItemId &&
                    s.LocationId == task.SourceLocationId &&
                    s.LotId == task.LotId, cancellationToken);

            if (sourceStock != null)
            {
                sourceStock.QtyAvailable = Math.Max(0, sourceStock.QtyAvailable - task.Quantity);
                sourceStock.Version++;
            }

            // 4. Atomic Ledger Relocation: Increment stock at Target Bin
            var destStock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == task.ClientId &&
                    s.FacilityId == task.FacilityId &&
                    s.ItemId == task.ItemId &&
                    s.LocationId == actualLocation.Id &&
                    s.LotId == task.LotId, cancellationToken);

            if (destStock == null)
            {
                destStock = new StockOnHand
                {
                    ClientId = task.ClientId,
                    FacilityId = task.FacilityId,
                    ItemId = task.ItemId,
                    LocationId = actualLocation.Id,
                    LotId = task.LotId,
                    QtyAvailable = task.Quantity,
                    Version = 1
                };
                _context.StockOnHand.Add(destStock);
            }
            else
            {
                destStock.QtyAvailable += task.Quantity;
                destStock.Version++;
            }

            // 5. Complete task
            task.Complete(actualLocation.Id, _currentUser.UserId ?? Guid.Empty);

            // 6. Record append-only immutable Transfer transaction in Ledger
            var tx = new InventoryTransaction
            {
                ClientId = task.ClientId,
                FacilityId = task.FacilityId,
                ItemId = task.ItemId,
                LotId = task.LotId,
                FromLocationId = task.SourceLocationId,
                ToLocationId = actualLocation.Id,
                TxType = InventoryTxType.Transfer,
                Quantity = task.Quantity,
                ReferenceType = "PUTAWAY_TASK",
                ReferenceId = task.Id,
                UserId = _currentUser.UserId,
                Notes = $"Putaway dual-scan confirmed for LPN '{task.LpnCode}' into bin '{actualLocation.LocationCode}'.",
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
                LpnId = task.LpnId,
                LpnCode = task.LpnCode,
                SourceLocationId = task.SourceLocationId,
                SourceLocationCode = task.SourceLocation.LocationCode,
                SuggestedLocationId = task.SuggestedLocationId,
                SuggestedLocationCode = task.SuggestedLocation.LocationCode,
                SuggestedLocationBarcode = task.SuggestedLocation.Barcode,
                ActualLocationId = task.ActualLocationId,
                ActualLocationCode = actualLocation.LocationCode,
                Quantity = task.Quantity,
                Status = task.Status,
                Priority = task.Priority,
                AssignedUserId = task.AssignedUserId,
                CreatedAtUtc = task.CreatedAtUtc,
                CompletedAtUtc = task.CompletedAtUtc,
                ExceptionReason = task.ExceptionReason,
                Notes = task.Notes,
                Version = task.Version
            };
        }, cancellationToken);
    }
}
