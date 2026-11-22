using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Picking;

/// <summary>
/// Authoritative transactional execution engine for warehouse directed RF picking.
/// Validates barcode scans, performs atomic source-to-tote inventory movements under allocated state,
/// generates immutable Pick ledger transactions, and processes short-pick discrepancies with cycle count integration.
/// DB-SPEC-3PL-2026 Phase 4.3 Step 3.
/// </summary>
public class PickExecutionService : IPickExecutionService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PickExecutionService> _logger;

    public PickExecutionService(
        IApplicationDbContext context,
        ILogger<PickExecutionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PickConfirmationResult> ConfirmPickAsync(
        ConfirmPickRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        return await _context.ExecuteInTransactionAsync(async () =>
        {
            // 1. Load PickTask with required relational graph
            var task = await _context.PickTasks
                .Include(t => t.SourceLocation)
                .Include(t => t.Item)
                    .ThenInclude(i => i.Barcodes)
                .Include(t => t.Lot)
                .Include(t => t.InventoryAllocation)
                .Include(t => t.PickWave)
                    .ThenInclude(w => w.Tasks)
                .Include(t => t.OutboundOrder)
                .FirstOrDefaultAsync(t => t.Id == request.PickTaskId, cancellationToken);

            if (task == null)
            {
                throw new EntityNotFoundException(nameof(PickTask), request.PickTaskId);
            }

            var originalStatus = task.Status;
            var originalAssignedUserId = task.AssignedUserId;
            var requiresOverrideAudit = ValidateExecutionOwnership(
                task,
                request.RequireAssignedOwnership,
                request.AllowSupervisorOverride,
                request.UserId,
                "execute pick confirmation");

            // 2. Idempotency & Terminal State Safeguard
            if (task.Status == PickTaskStatus.Completed || task.Status == PickTaskStatus.ShortPicked)
            {
                if (task.QuantityPicked == request.Quantity &&
                    string.Equals(task.TargetToteBarcode, request.ScannedToteLpn.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Idempotent retry detected for PickTask '{TaskId}'. Returning existing confirmation.",
                        task.Id);

                    return new PickConfirmationResult
                    {
                        IsSuccess = true,
                        PickTaskId = task.Id,
                        TaskStatus = task.Status,
                        QuantityPicked = task.QuantityPicked,
                        ShortPickQuantity = task.ShortPickQuantity,
                        DestinationStockId = task.InventoryAllocation?.PickedStockOnHandId ?? Guid.Empty,
                        DestinationHandlingUnitId = task.TargetHandlingUnitId ?? Guid.Empty,
                        DestinationToteBarcode = task.TargetToteBarcode ?? string.Empty,
                        WaveCompleted = task.PickWave?.Status == PickWaveStatus.Completed,
                        OrderCompleted = task.OutboundOrder?.Status == OutboundOrderStatus.Completed,
                        Message = "Pick already confirmed (idempotent response)."
                    };
                }

                throw new InvalidDomainStateException(
                    $"Cannot confirm pick for task '{task.Id}' because it is already in terminal status '{task.Status}'.");
            }

            if (task.Status == PickTaskStatus.Cancelled)
            {
                throw new InvalidDomainStateException($"Cannot confirm pick for cancelled task '{task.Id}'.");
            }

            ValidateReleasedWaveForExecution(task);

            return await ExecuteConfirmPickCoreAsync(
                task,
                request,
                persistAndFinalize: true,
                requiresOverrideAudit,
                originalStatus,
                originalAssignedUserId,
                cancellationToken);
        }, cancellationToken);
    }

    public async Task<ShortPickExecutionResult> ReportShortPickAsync(
        ReportShortPickRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        return await _context.ExecuteInTransactionAsync(async () =>
        {
            var task = await _context.PickTasks
                .Include(t => t.SourceLocation)
                .Include(t => t.Item)
                    .ThenInclude(i => i.Barcodes)
                .Include(t => t.Lot)
                .Include(t => t.InventoryAllocation)
                .Include(t => t.PickWave)
                    .ThenInclude(w => w.Tasks)
                .Include(t => t.OutboundOrder)
                .FirstOrDefaultAsync(t => t.Id == request.PickTaskId, cancellationToken);

            if (task == null)
            {
                throw new EntityNotFoundException(nameof(PickTask), request.PickTaskId);
            }

            var originalStatus = task.Status;
            var originalAssignedUserId = task.AssignedUserId;
            var requiresOverrideAudit = ValidateExecutionOwnership(
                task,
                request.RequireAssignedOwnership,
                request.AllowSupervisorOverride,
                request.UserId,
                "report a short-pick");

            if (task.Status == PickTaskStatus.Completed || task.Status == PickTaskStatus.ShortPicked)
            {
                throw new InvalidDomainStateException(
                    $"Cannot report short-pick for task '{task.Id}' because it is already in terminal status '{task.Status}'.");
            }

            if (task.Status == PickTaskStatus.Cancelled)
            {
                throw new InvalidDomainStateException($"Cannot report short-pick for cancelled task '{task.Id}'.");
            }

            ValidateReleasedWaveForExecution(task);

            var pickedQty = request.QuantityPicked;
            if (pickedQty < 0)
            {
                throw new ValidationException(nameof(request.QuantityPicked), "Picked quantity cannot be negative.");
            }

            if (pickedQty >= task.QuantityToPick)
            {
                throw new ValidationException(nameof(request.QuantityPicked),
                    $"Short-pick quantity {pickedQty} must be strictly less than task demand {task.QuantityToPick}.");
            }

            var discrepancyQty = task.QuantityToPick - pickedQty;

            // 1. If partial quantity was found, physically pick it inside the SAME outer transaction without committing early
            if (pickedQty > 0)
            {
                if (string.IsNullOrWhiteSpace(request.ScannedToteLpn))
                {
                    throw new ValidationException(nameof(request.ScannedToteLpn),
                        "Target tote LPN is required when confirming partial picked quantity on short pick.");
                }

                await ExecuteConfirmPickCoreAsync(
                    task,
                    new ConfirmPickRequest
                    {
                        PickTaskId = task.Id,
                        ScannedLocationBarcode = request.ScannedLocationBarcode ?? task.SourceLocation.Barcode,
                        ScannedItemBarcode = request.ScannedItemBarcode ?? task.Item.Sku,
                        ScannedToteLpn = request.ScannedToteLpn,
                        Quantity = pickedQty,
                        UserId = request.UserId,
                        IdempotencyKey = request.IdempotencyKey,
                        RequireAssignedOwnership = false,
                        AllowSupervisorOverride = false
                    },
                    persistAndFinalize: false,
                    requiresOverrideAudit: false,
                    originalStatus,
                    originalAssignedUserId,
                    cancellationToken);
            }

            // 2. Adjust remaining reservation on source allocation for the shortfall
            var allocation = task.InventoryAllocation;
            if (allocation != null && allocation.RemainingToPickQuantity > 0)
            {
                var qtyToRelease = Math.Min(discrepancyQty, allocation.RemainingToPickQuantity);
                allocation.Release(qtyToRelease, request.UserId, $"Short-pick: {request.Reason} - {request.Notes}");

                // Release from source bin stock
                var sourceStock = await _context.StockOnHand.FirstOrDefaultAsync(s => s.Id == task.StockOnHandId, cancellationToken);
                if (sourceStock != null)
                {
                    sourceStock.QtyAllocated = Math.Max(0m, sourceStock.QtyAllocated - qtyToRelease);
                    if (pickedQty == 0)
                    {
                        sourceStock.Version++;
                    }
                }
            }

            // 3. Automated Cycle Count Trigger for affected location
            var cycleCount = new CycleCount
            {
                Id = Guid.NewGuid(),
                FacilityId = task.FacilityId,
                ZoneId = task.SourceLocation.ZoneId,
                CountNumber = $"CC-SP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
                CountType = CycleCountType.AdHoc,
                Status = CycleCountStatus.Scheduled,
                ScheduledDate = DateTime.UtcNow,
                Notes = $"Automated cycle count triggered by short pick on Task '{task.Id}' at '{task.SourceLocation.LocationCode}'. " +
                        $"Discrepancy: {discrepancyQty} units of SKU '{task.Item.Sku}'. Reason: {request.Reason}. {request.Notes}"
            };

            var ccItem = new CycleCountItem
            {
                Id = Guid.NewGuid(),
                CycleCountId = cycleCount.Id,
                LocationId = task.SourceLocationId,
                ItemId = task.ItemId,
                LotId = task.LotId,
                ExpectedQty = 0m // Ad-hoc investigation
            };
            cycleCount.Items.Add(ccItem);
            _context.CycleCounts.Add(cycleCount);

            // 4. Conditional LocationLock based on discrepancy severity
            bool shouldLock = request.Reason == ShortPickReason.BinEmpty ||
                              request.Reason == ShortPickReason.WrongItemInBin ||
                              request.Reason == ShortPickReason.LotExpired ||
                              discrepancyQty >= 10m;

            if (shouldLock)
            {
                var locLock = new LocationLock
                {
                    Id = Guid.NewGuid(),
                    LocationId = task.SourceLocationId,
                    LockType = LocationLockType.CycleCount,
                    ReasonCode = request.Reason.ToString(),
                    Notes = $"Operational hold placed due to {request.Reason} discrepancy of {discrepancyQty} units during task '{task.Id}'.",
                    LockedByUserId = request.UserId,
                    LockedAt = DateTime.UtcNow
                };
                task.SourceLocation.IsLocked = true;
                _context.LocationLocks.Add(locLock);
            }

            // 5. Create PickException
            var exception = new PickException
            {
                Id = Guid.NewGuid(),
                ClientId = task.ClientId,
                FacilityId = task.FacilityId,
                PickTaskId = task.Id,
                OutboundOrderId = task.OutboundOrderId,
                OutboundOrderLineId = task.OutboundOrderLineId,
                LocationId = task.SourceLocationId,
                ItemId = task.ItemId,
                LotId = task.LotId,
                ExpectedQuantity = task.QuantityToPick,
                ActualQuantityPicked = pickedQty,
                Reason = request.Reason,
                Notes = request.Notes,
                ReportedByUserId = request.UserId,
                ReportedAtUtc = DateTime.UtcNow,
                GeneratedCycleCountId = cycleCount.Id,
                LocationLockApplied = shouldLock
            };
            _context.PickExceptions.Add(exception);

            // 6. Update Task Status
            task.ReportShortPick(pickedQty, request.Reason, request.Notes, request.UserId);
            task.Version = unchecked(task.Version + 1u);

            if (requiresOverrideAudit)
            {
                AddSupervisorOverrideAudit(
                    task,
                    originalStatus,
                    originalAssignedUserId,
                    request.UserId,
                    request.OverrideReason,
                    "short-pick");
            }

            // 7. Check Wave Completion
            var wave = task.PickWave;
            var allTasksFinished = wave.Tasks.All(t => t.Status == PickTaskStatus.Completed ||
                                                      t.Status == PickTaskStatus.ShortPicked ||
                                                      t.Status == PickTaskStatus.Cancelled);
            if (allTasksFinished)
            {
                wave.Status = PickWaveStatus.Completed;
                wave.CompletedAtUtc = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Short-pick recorded for Task '{TaskId}': {Picked}/{Requested} units ({Reason}). CycleCount: '{CCNum}', Locked: {Locked}",
                task.Id, pickedQty, task.QuantityToPick, request.Reason, cycleCount.CountNumber, shouldLock);

            return new ShortPickExecutionResult
            {
                IsSuccess = true,
                PickTaskId = task.Id,
                TaskStatus = task.Status,
                QuantityPicked = pickedQty,
                DiscrepancyQuantity = discrepancyQty,
                PickExceptionId = exception.Id,
                GeneratedCycleCountId = cycleCount.Id,
                LocationLockApplied = shouldLock,
                Message = $"Short-pick recorded ({request.Reason}). Discrepancy of {discrepancyQty} units reported."
            };
        }, cancellationToken);
    }

    private async Task<PickConfirmationResult> ExecuteConfirmPickCoreAsync(
        PickTask task,
        ConfirmPickRequest request,
        bool persistAndFinalize,
        bool requiresOverrideAudit,
        PickTaskStatus originalStatus,
        Guid? originalAssignedUserId,
        CancellationToken cancellationToken)
    {
        // Scan Validation — Location
        if (string.IsNullOrWhiteSpace(request.ScannedLocationBarcode))
        {
            throw new ValidationException(nameof(request.ScannedLocationBarcode), "Scanned location barcode is required.");
        }

        var locBarcode = request.ScannedLocationBarcode.Trim();
        var locMatch = string.Equals(locBarcode, task.SourceLocation.Barcode, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(locBarcode, task.SourceLocation.LocationCode, StringComparison.OrdinalIgnoreCase);

        if (!locMatch)
        {
            throw new ValidationException(nameof(request.ScannedLocationBarcode),
                $"Scanned location barcode '{locBarcode}' does not match expected location '{task.SourceLocation.LocationCode}' (Barcode: '{task.SourceLocation.Barcode}').");
        }

        if (task.SourceLocation.IsLocked || task.SourceLocation.IsDamaged || !task.SourceLocation.IsActive)
        {
            throw new ValidationException(nameof(request.ScannedLocationBarcode),
                $"Source location '{task.SourceLocation.LocationCode}' is currently locked, damaged, or inactive.");
        }

        // Scan Validation — Item / SKU
        if (string.IsNullOrWhiteSpace(request.ScannedItemBarcode))
        {
            throw new ValidationException(nameof(request.ScannedItemBarcode), "Scanned item barcode is required.");
        }

        var itemBarcode = request.ScannedItemBarcode.Trim();
        var itemMatch = string.Equals(itemBarcode, task.Item.Sku, StringComparison.OrdinalIgnoreCase) ||
                        task.Item.Barcodes.Any(b => string.Equals(b.BarcodeValue, itemBarcode, StringComparison.OrdinalIgnoreCase));

        if (!itemMatch)
        {
            throw new ValidationException(nameof(request.ScannedItemBarcode),
                $"Scanned item barcode '{itemBarcode}' does not match expected SKU '{task.Item.Sku}'.");
        }

        // Scan Validation — Lot
        if (task.LotId.HasValue || task.Item.IsLotTracked)
        {
            if (task.Lot != null && !string.IsNullOrWhiteSpace(request.ScannedLotNumber))
            {
                if (!string.Equals(task.Lot.LotNumber.Trim(), request.ScannedLotNumber.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException(nameof(request.ScannedLotNumber),
                        $"Scanned lot number '{request.ScannedLotNumber}' does not match expected lot '{task.Lot.LotNumber}'.");
                }
            }
        }

        // Scan Validation — Target Tote / Handling Unit
        if (string.IsNullOrWhiteSpace(request.ScannedToteLpn))
        {
            throw new ValidationException(nameof(request.ScannedToteLpn), "Target tote / handling unit LPN is required.");
        }

        var toteLpn = request.ScannedToteLpn.Trim();
        var tote = await _context.HandlingUnits
            .FirstOrDefaultAsync(h => h.FacilityId == task.FacilityId &&
                                      h.LpnCode == toteLpn &&
                                      !h.IsDeleted, cancellationToken);

        if (tote == null)
        {
            throw new ValidationException(nameof(request.ScannedToteLpn),
                $"Handling unit with LPN '{toteLpn}' was not found in facility.");
        }

        if (tote.Status == HandlingUnitStatus.Damaged)
        {
            throw new ValidationException(nameof(request.ScannedToteLpn),
                $"Target tote '{toteLpn}' is Damaged and cannot accept picked inventory.");
        }

        if (tote.CurrentPickWaveId.HasValue && tote.CurrentPickWaveId.Value != task.PickWaveId)
        {
            throw new ValidationException(nameof(request.ScannedToteLpn),
                $"Target tote '{toteLpn}' is already assigned to a different pick wave.");
        }

        if (tote.Status == HandlingUnitStatus.Available)
        {
            tote.AssignToWave(task.PickWaveId, task.OutboundOrderId);
        }

        // Quantity Validation
        if (request.Quantity <= 0)
        {
            throw new ValidationException(nameof(request.Quantity), "Picked quantity must be greater than zero.");
        }

        var remainingTaskQty = task.QuantityToPick - task.QuantityPicked;
        if (request.Quantity > remainingTaskQty)
        {
            throw new ValidationException(nameof(request.Quantity),
                $"Picked quantity {request.Quantity} exceeds remaining task demand {remainingTaskQty}.");
        }

        var allocation = task.InventoryAllocation;
        if (allocation == null)
        {
            throw new DomainException($"Pick task '{task.Id}' has no associated inventory allocation.");
        }

        if (request.Quantity > allocation.RemainingToPickQuantity)
        {
            throw new ValidationException(nameof(request.Quantity),
                $"Picked quantity {request.Quantity} exceeds allocation remaining unpicked quantity {allocation.RemainingToPickQuantity}.");
        }

        // Atomic Physical Stock Movement & Conservation
        var sourceStock = await _context.StockOnHand
            .FirstOrDefaultAsync(s => s.Id == task.StockOnHandId, cancellationToken);

        if (sourceStock == null)
        {
            throw new DomainException($"Source stock record '{task.StockOnHandId}' was not found.");
        }

        if (sourceStock.QtyAllocated < request.Quantity)
        {
            throw new DomainException(
                $"Source stock '{task.StockOnHandId}' has insufficient allocated quantity ({sourceStock.QtyAllocated}) " +
                $"to satisfy pick confirmation of {request.Quantity} units.");
        }

        sourceStock.QtyAllocated -= request.Quantity;
        sourceStock.Version++;

        var destStock = await _context.StockOnHand
            .FirstOrDefaultAsync(s => s.HandlingUnitId == tote.Id &&
                                      s.ItemId == task.ItemId &&
                                      s.LotId == task.LotId, cancellationToken);

        if (destStock != null)
        {
            destStock.QtyAllocated += request.Quantity;
            destStock.Version++;
        }
        else
        {
            destStock = new StockOnHand
            {
                Id = Guid.NewGuid(),
                ClientId = task.ClientId,
                FacilityId = task.FacilityId,
                ItemId = task.ItemId,
                LocationId = tote.CurrentLocationId ?? task.DestinationLocationId ?? task.SourceLocationId,
                LotId = task.LotId,
                HandlingUnitId = tote.Id,
                LpnCode = tote.LpnCode,
                QtyAvailable = 0m,
                QtyAllocated = request.Quantity,
                ReceivedAtUtc = DateTime.UtcNow,
                Version = 1
            };
            _context.StockOnHand.Add(destStock);
        }

        // Update Allocation and Task
        allocation.RecordPick(request.Quantity, destStock.Id, tote.Id, tote.LpnCode);
        task.ConfirmPick(request.Quantity, tote.LpnCode, tote.Id, request.UserId);

        // Record Immutable Inventory Transaction Ledger
        var tx = new InventoryTransaction
        {
            ClientId = task.ClientId,
            FacilityId = task.FacilityId,
            ItemId = task.ItemId,
            LotId = task.LotId,
            FromLocationId = task.SourceLocationId,
            ToLocationId = destStock.LocationId,
            TxType = InventoryTxType.Pick,
            Quantity = request.Quantity,
            ReferenceType = "PickTask",
            ReferenceId = task.Id,
            UserId = request.UserId,
            Notes = $"Pick confirmed: Task '{task.Id}', Wave '{task.PickWave.WaveNumber}', Tote '{tote.LpnCode}'",
            TxTimestamp = DateTime.UtcNow
        };
        _context.InventoryTransactions.Add(tx);

        if (persistAndFinalize)
        {
            task.Version = unchecked(task.Version + 1u);

            if (requiresOverrideAudit)
            {
                AddSupervisorOverrideAudit(
                    task,
                    originalStatus,
                    originalAssignedUserId,
                    request.UserId,
                    request.OverrideReason,
                    "pick confirmation");
            }

            var wave = task.PickWave;
            var allTasksFinished = wave.Tasks.All(t => t.Status == PickTaskStatus.Completed ||
                                                      t.Status == PickTaskStatus.ShortPicked ||
                                                      t.Status == PickTaskStatus.Cancelled);

            if (allTasksFinished)
            {
                wave.Status = PickWaveStatus.Completed;
                wave.CompletedAtUtc = DateTime.UtcNow;
            }
            else if (wave.Status == PickWaveStatus.Released)
            {
                wave.Status = PickWaveStatus.InProgress;
                wave.StartedAtUtc ??= DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Pick confirmed for Task '{TaskId}': {Qty} units into Tote '{Tote}'. TxId: {TxId}, WaveStatus: {WaveStatus}",
                task.Id, request.Quantity, tote.LpnCode, tx.Id, wave.Status);
        }

        return new PickConfirmationResult
        {
            IsSuccess = true,
            PickTaskId = task.Id,
            TaskStatus = task.Status,
            QuantityPicked = task.QuantityPicked,
            ShortPickQuantity = task.ShortPickQuantity,
            DestinationStockId = destStock.Id,
            DestinationHandlingUnitId = tote.Id,
            DestinationToteBarcode = tote.LpnCode,
            InventoryTransactionId = tx.Id,
            WaveCompleted = task.PickWave.Status == PickWaveStatus.Completed,
            OrderCompleted = task.OutboundOrder?.Status == OutboundOrderStatus.Completed,
            Message = $"Successfully picked {request.Quantity} units into tote '{tote.LpnCode}'."
        };
    }

    private static bool ValidateExecutionOwnership(
        PickTask task,
        bool requireAssignedOwnership,
        bool allowSupervisorOverride,
        Guid userId,
        string operationLabel)
    {
        if (!requireAssignedOwnership)
        {
            return false;
        }

        if (userId == Guid.Empty)
        {
            throw new ForbiddenException($"Authenticated staff operator identity is required to {operationLabel}.");
        }

        if (task.AssignedUserId.HasValue && task.AssignedUserId.Value == userId)
        {
            return false;
        }

        if (!allowSupervisorOverride)
        {
            throw new ForbiddenException(task.AssignedUserId.HasValue
                ? "This pick task is assigned to another operator."
                : $"Pick task must be explicitly claimed by the operator before {operationLabel}.");
        }

        return true;
    }

    private static void ValidateReleasedWaveForExecution(PickTask task)
    {
        if (task.PickWave == null ||
            task.PickWave.IsDeleted ||
            (task.PickWave.Status != PickWaveStatus.Released && task.PickWave.Status != PickWaveStatus.InProgress))
        {
            throw new InvalidDomainStateException(
                $"Pick task '{task.Id}' belongs to wave with status '{task.PickWave?.Status}' and cannot be executed until the wave is Released or InProgress.");
        }
    }

    private void AddSupervisorOverrideAudit(
        PickTask task,
        PickTaskStatus fromStatus,
        Guid? priorAssignedUserId,
        Guid supervisorUserId,
        string? overrideReason,
        string operationLabel)
    {
        var baseReason = string.IsNullOrWhiteSpace(overrideReason)
            ? $"Task-management override {operationLabel} by supervisor '{supervisorUserId}'"
            : overrideReason.Trim();

        _context.PickTaskStatusHistories.Add(new PickTaskStatusHistory
        {
            PickTaskId = task.Id,
            FromStatus = fromStatus,
            ToStatus = task.Status,
            Reason = $"{baseReason} (prior assignee: '{priorAssignedUserId?.ToString() ?? "unassigned"}').",
            ChangedByUserId = supervisorUserId,
            ChangedAtUtc = DateTime.UtcNow
        });
    }
}

