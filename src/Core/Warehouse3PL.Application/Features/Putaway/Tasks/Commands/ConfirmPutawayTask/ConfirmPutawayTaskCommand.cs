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

namespace Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ConfirmPutawayTask;

public record ConfirmPutawayTaskCommand : IRequest<PutawayTaskDto>
{
    public Guid TaskId { get; init; }

    /// <summary>
    /// Scanned container or pallet LPN tag from mobile RF device.
    /// </summary>
    public string ScannedLpn { get; init; } = string.Empty;

    /// <summary>
    /// Scanned target location barcode from rack/bin label.
    /// </summary>
    public string ScannedLocationBarcode { get; init; } = string.Empty;

    /// <summary>
    /// Set true when a warehouse supervisor or authorized lead explicitly overrides the suggested bin.
    /// </summary>
    public bool AllowOverride { get; init; } = false;
}

public class ConfirmPutawayTaskCommandValidator : AbstractValidator<ConfirmPutawayTaskCommand>
{
    public ConfirmPutawayTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty().WithMessage("TaskId is required.");
        RuleFor(x => x.ScannedLpn).NotEmpty().WithMessage("Scanned LPN is required.");
        RuleFor(x => x.ScannedLocationBarcode).NotEmpty().WithMessage("Scanned location barcode is required.");
    }
}

public class ConfirmPutawayTaskCommandHandler : IRequestHandler<ConfirmPutawayTaskCommand, PutawayTaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public ConfirmPutawayTaskCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _operationalAuth = operationalAuth;
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

            // Facility and Tenant Access Validation
            await _operationalAuth.ValidateClientFacilityAccessAsync(
                task.ClientId, task.FacilityId, cancellationToken);

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

            if (scannedLoc.Id != task.SuggestedLocationId)
            {
                if (!request.AllowOverride)
                {
                    throw new ValidationException(
                        nameof(request.ScannedLocationBarcode),
                        $"Scanned location barcode '{request.ScannedLocationBarcode}' ({scannedLoc.LocationCode}) does not match suggested bin '{task.SuggestedLocation?.LocationCode}'. Bin override not authorized.");
                }

                // Server-authorized override check
                var hasOverridePerm = _currentUser.IsSuperAdmin ||
                    _currentUser.HasPermission(Permissions.Inbound.PutawayManage) ||
                    _currentUser.HasPermission(Permissions.Inbound.PutawayOverride) ||
                    _currentUser.HasPermission(Permissions.Inbound.PutawayRules);

                if (!hasOverridePerm)
                {
                    throw new ForbiddenException("Current user does not have permission to override suggested putaway location.");
                }
            }

            // Validate destination suitability
            if (!scannedLoc.IsActive)
            {
                throw new ValidationException(nameof(request.ScannedLocationBarcode), $"Destination location '{scannedLoc.LocationCode}' is inactive.");
            }

            if (scannedLoc.IsDeleted)
            {
                throw new ValidationException(nameof(request.ScannedLocationBarcode), $"Destination location '{scannedLoc.LocationCode}' has been deleted.");
            }

            if (scannedLoc.IsLocked)
            {
                throw new ValidationException(nameof(request.ScannedLocationBarcode), $"Destination location '{scannedLoc.LocationCode}' is currently locked.");
            }

            if (scannedLoc.IsDamaged)
            {
                throw new ValidationException(nameof(request.ScannedLocationBarcode), $"Destination location '{scannedLoc.LocationCode}' is marked as damaged and cannot accept inventory.");
            }

            if (scannedLoc.IsReceiving || scannedLoc.LocationType == LocationType.StagingLane)
            {
                throw new ValidationException(nameof(request.ScannedLocationBarcode), $"Destination location '{scannedLoc.LocationCode}' is a receiving staging area. Putaway destination must be a storage location.");
            }

            if (scannedLoc.FacilityId != task.FacilityId)
            {
                throw new ValidationException(nameof(request.ScannedLocationBarcode), $"Destination location '{scannedLoc.LocationCode}' belongs to another facility.");
            }

            var actualLocation = scannedLoc;

            // 3. Inventory Conservation: Verify and decrement stock from source staging location
            var sourceStock = await _context.StockOnHand
                .FirstOrDefaultAsync(s =>
                    s.ClientId == task.ClientId &&
                    s.FacilityId == task.FacilityId &&
                    s.ItemId == task.ItemId &&
                    s.LocationId == task.SourceLocationId &&
                    s.LotId == task.LotId, cancellationToken);

            if (sourceStock == null || sourceStock.QtyAvailable < task.Quantity)
            {
                throw new ValidationException(
                    nameof(task.Quantity),
                    $"Insufficient stock at source staging location '{task.SourceLocation?.LocationCode ?? task.SourceLocationId.ToString()}'. " +
                    $"Required: {task.Quantity}, Available: {sourceStock?.QtyAvailable ?? 0m}. Putaway confirmation aborted to conserve inventory.");
            }

            sourceStock.QtyAvailable -= task.Quantity;
            sourceStock.Version++;

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
            var ledgerTx = new InventoryTransaction
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
                UserId = _currentUser.UserId ?? Guid.Empty,
                TxTimestamp = DateTime.UtcNow,
                Notes = $"Directed Putaway Dual-Scan Confirmed: Task {task.Id} (LPN: {task.LpnCode}, Dest: {actualLocation.LocationCode})"
            };
            _context.InventoryTransactions.Add(ledgerTx);

            await _context.SaveChangesAsync(cancellationToken);

            return new PutawayTaskDto
            {
                Id = task.Id,
                FacilityId = task.FacilityId,
                ClientId = task.ClientId,
                ItemId = task.ItemId,
                Sku = task.Item?.Sku ?? string.Empty,
                ItemDescription = task.Item?.Description ?? string.Empty,
                LotId = task.LotId,
                LotNumber = task.Lot?.LotNumber,
                LpnId = task.LpnId,
                LpnCode = task.LpnCode,
                Quantity = task.Quantity,
                SourceLocationId = task.SourceLocationId,
                SourceLocationCode = task.SourceLocation?.LocationCode ?? string.Empty,
                SuggestedLocationId = task.SuggestedLocationId,
                SuggestedLocationCode = task.SuggestedLocation?.LocationCode ?? string.Empty,
                ActualLocationId = task.ActualLocationId,
                ActualLocationCode = actualLocation.LocationCode,
                Status = task.Status,
                Priority = task.Priority,
                AssignedUserId = task.AssignedUserId,
                CreatedAtUtc = task.CreatedAtUtc,
                CompletedAtUtc = task.CompletedAtUtc,
                ExceptionReason = task.ExceptionReason,
                Version = task.Version
            };
        }, cancellationToken);
    }
}
