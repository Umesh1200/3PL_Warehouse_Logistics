using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Putaway.Tasks.Commands.GeneratePutawayTasks;

public record GeneratePutawayTasksCommand : IRequest<List<PutawayTaskDto>>
{
    public Guid FacilityId { get; init; }
    public Guid? ClientId { get; init; }
}

public class GeneratePutawayTasksCommandValidator : AbstractValidator<GeneratePutawayTasksCommand>
{
    public GeneratePutawayTasksCommandValidator()
    {
        RuleFor(x => x.FacilityId).NotEmpty().WithMessage("FacilityId is required.");
    }
}

public class GeneratePutawayTasksCommandHandler : IRequestHandler<GeneratePutawayTasksCommand, List<PutawayTaskDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPutawayLocationSelector _locationSelector;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public GeneratePutawayTasksCommandHandler(
        IApplicationDbContext context,
        IPutawayLocationSelector locationSelector,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _locationSelector = locationSelector;
        _operationalAuth = operationalAuth;
    }

    public async Task<List<PutawayTaskDto>> Handle(GeneratePutawayTasksCommand request, CancellationToken cancellationToken)
    {
        var scope = await _operationalAuth.ResolveQueryScopeAsync(request.ClientId, request.FacilityId, cancellationToken);
        var effectiveFacilityId = scope.FacilityId ?? throw new ValidationException(nameof(request.FacilityId), "A specific authorized facility is required.");

        return await GenerateTasksInternalAsync(effectiveFacilityId, scope.ClientId, cancellationToken, retryCount: 0);
    }

    private async Task<List<PutawayTaskDto>> GenerateTasksInternalAsync(
        Guid effectiveFacilityId,
        Guid? effectiveClientId,
        CancellationToken cancellationToken,
        int retryCount)
    {
        // 1. Find all sound receipt lines that belong to COMPLETED receiving sessions and reside in staging
        var query = _context.ReceiptLines
            .Include(l => l.ReceivingSession)
            .Include(l => l.Item)
            .Include(l => l.StagingLocation)
            .Where(l => l.ReceivingSession.FacilityId == effectiveFacilityId &&
                        l.ReceivingSession.Status == ReceivingStatus.Completed &&
                        l.StagingLocationId != null &&
                        l.Condition == ReceiptConditionStatus.Sound &&
                        l.QuantityReceived > 0);

        if (effectiveClientId.HasValue)
        {
            query = query.Where(l => l.ReceivingSession.ClientId == effectiveClientId.Value || l.Item.ClientId == effectiveClientId.Value);
        }

        var candidateLines = await query.OrderBy(l => l.Id).ToListAsync(cancellationToken);
        if (candidateLines.Count == 0)
        {
            return [];
        }

        var candidateLineIds = candidateLines.Select(l => l.Id).ToList();

        // 2. Identify candidate lines that already have active (non-cancelled) tasks
        var existingTasks = await _context.PutawayTasks
            .Include(t => t.Item)
            .Include(t => t.SourceLocation)
            .Include(t => t.SuggestedLocation)
            .AsNoTracking()
            .Where(t => t.FacilityId == effectiveFacilityId &&
                        t.LpnId.HasValue &&
                        candidateLineIds.Contains(t.LpnId.Value) &&
                        t.Status != PutawayTaskStatus.Cancelled)
            .ToListAsync(cancellationToken);

        var existingLineIdSet = existingTasks.Select(t => t.LpnId!.Value).ToHashSet();

        var unassignedLines = candidateLines
            .Where(l => !existingLineIdSet.Contains(l.Id))
            .OrderBy(l => l.Id)
            .ToList();

        if (unassignedLines.Count == 0)
        {
            // All candidate lines are already assigned; return 0 newly generated tasks
            return [];
        }

        try
        {
            var generatedTasks = await _context.ExecuteInTransactionAsync(async () =>
            {
                // Double check inside transaction to ensure another concurrent generation has not assigned lines
                var assignedIds = await _context.PutawayTasks
                    .Where(t => t.FacilityId == effectiveFacilityId &&
                                t.LpnId.HasValue &&
                                candidateLineIds.Contains(t.LpnId.Value) &&
                                t.Status != PutawayTaskStatus.Cancelled)
                    .Select(t => t.LpnId!.Value)
                    .ToListAsync(cancellationToken);
                var assignedSet = assignedIds.ToHashSet();

                var linesToProcess = unassignedLines
                    .Where(l => !assignedSet.Contains(l.Id))
                    .OrderBy(l => l.Id)
                    .ToList();

                if (linesToProcess.Count == 0)
                {
                    return new List<PutawayTask>();
                }

                var batch = new List<PutawayTask>();

                foreach (var line in linesToProcess)
                {
                    var clientId = line.ReceivingSession.ClientId ?? line.Item.ClientId;
                    var facilityId = line.ReceivingSession.FacilityId;

                    // Resolve Lot if LotNumber is present using consistent normalized lot identity
                    Guid? lotId = null;
                    InventoryLot? lot = null;
                    if (!string.IsNullOrWhiteSpace(line.LotNumber))
                    {
                        var normalizedLot = line.LotNumber.Trim().ToUpperInvariant();
                        lot = await _context.InventoryLots
                            .FirstOrDefaultAsync(l => l.ClientId == clientId && l.ItemId == line.ItemId && l.LotNumber == normalizedLot, cancellationToken);
                        lotId = lot?.Id;
                    }

                    // Slotting allocation
                    var optimalLocation = await _locationSelector.SelectOptimalLocationAsync(
                        clientId,
                        facilityId,
                        line.ItemId,
                        line.QuantityReceived,
                        lotId,
                        null,
                        cancellationToken);

                    if (optimalLocation == null)
                    {
                        continue; // Skip if no eligible bin is currently available
                    }

                    // Determine Priority
                    var priority = PutawayPriority.Normal;
                    if (line.Item.StorageTempClass == StorageTempClass.Frozen || line.Item.StorageTempClass == StorageTempClass.Refrigerated)
                    {
                        priority = PutawayPriority.Expedite; // Cold chain goods priority
                    }
                    else if (line.Item.AbcVelocity == AbcVelocity.A)
                    {
                        priority = PutawayPriority.High; // Fast-movers priority
                    }

                    var task = new PutawayTask
                    {
                        ClientId = clientId,
                        FacilityId = facilityId,
                        ItemId = line.ItemId,
                        LotId = lotId,
                        Lot = lot,
                        LpnId = line.Id,
                        LpnCode = line.Lpn,
                        SourceLocationId = line.StagingLocationId!.Value,
                        SuggestedLocationId = optimalLocation.Id,
                        Quantity = line.QuantityReceived,
                        Status = PutawayTaskStatus.Pending,
                        Priority = priority,
                        CreatedAtUtc = DateTime.UtcNow,
                        Notes = $"Auto-generated from Receiving Session {line.ReceivingSession.SessionNumber}"
                    };

                    task.SuggestedLocation = optimalLocation;
                    task.Item = line.Item;
                    task.SourceLocation = line.StagingLocation!;

                    batch.Add(task);
                    _context.PutawayTasks.Add(task);
                }

                if (batch.Count > 0)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }

                return batch;
            }, cancellationToken);

            return generatedTasks.Select(MapToDto).ToList();
        }
        catch (Exception ex) when (IsReceiptLineUniqueViolation(ex) && retryCount < 3)
        {
            // Clear failed tracked entities to prevent dirty state in ChangeTracker
            _context.ClearChangeTracker();

            // Retry for overlapping generation batches to ensure no unassigned lines are silently omitted
            return await GenerateTasksInternalAsync(effectiveFacilityId, effectiveClientId, cancellationToken, retryCount + 1);
        }
    }

    private static bool IsReceiptLineUniqueViolation(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            var typeName = current.GetType().Name;
            if (typeName == "PostgresException")
            {
                var sqlStateProp = current.GetType().GetProperty("SqlState");
                var constraintProp = current.GetType().GetProperty("ConstraintName");
                var messageProp = current.GetType().GetProperty("MessageText") ?? current.GetType().GetProperty("Message");
                var whereProp = current.GetType().GetProperty("Where");

                var sqlState = sqlStateProp?.GetValue(current) as string;
                var constraint = constraintProp?.GetValue(current) as string;
                var message = messageProp?.GetValue(current) as string;
                var where = whereProp?.GetValue(current) as string;

                // Recover from PostgreSQL error 23505 (unique_violation) on ix_putaway_tasks_lpn_id,
                // or concurrent index race deadlock (40P01) on the same index
                if (sqlState == "23505" &&
                    constraint != null &&
                    constraint.Contains("ix_putaway_tasks_lpn_id", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (sqlState == "40P01" &&
                    ((where != null && where.Contains("ix_putaway_tasks_lpn_id", StringComparison.OrdinalIgnoreCase)) ||
                     (message != null && message.Contains("ix_putaway_tasks_lpn_id", StringComparison.OrdinalIgnoreCase))))
                {
                    return true;
                }

                return false;
            }
            if (typeName == "SqliteException")
            {
                // In SQLite, verify the message specifically targets lpn_id
                if (current.Message.Contains("putaway_tasks.lpn_id", StringComparison.OrdinalIgnoreCase) ||
                    current.Message.Contains("ix_putaway_tasks_lpn_id", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return false;
            }
            current = current.InnerException;
        }
        return false;
    }

    private static PutawayTaskDto MapToDto(PutawayTask t) => new()
    {
        Id = t.Id,
        ClientId = t.ClientId,
        FacilityId = t.FacilityId,
        ItemId = t.ItemId,
        Sku = t.Item?.Sku ?? string.Empty,
        ItemDescription = t.Item?.Description ?? string.Empty,
        LotId = t.LotId,
        LpnId = t.LpnId,
        LpnCode = t.LpnCode,
        SourceLocationId = t.SourceLocationId,
        SourceLocationCode = t.SourceLocation?.LocationCode ?? string.Empty,
        SuggestedLocationId = t.SuggestedLocationId,
        SuggestedLocationCode = t.SuggestedLocation?.LocationCode ?? string.Empty,
        SuggestedLocationBarcode = t.SuggestedLocation?.Barcode ?? string.Empty,
        Quantity = t.Quantity,
        Status = t.Status,
        Priority = t.Priority,
        CreatedAtUtc = t.CreatedAtUtc,
        Notes = t.Notes,
        Version = t.Version
    };
}
