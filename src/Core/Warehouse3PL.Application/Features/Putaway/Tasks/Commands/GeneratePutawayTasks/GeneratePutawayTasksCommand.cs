using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

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

    public GeneratePutawayTasksCommandHandler(
        IApplicationDbContext context,
        IPutawayLocationSelector locationSelector)
    {
        _context = context;
        _locationSelector = locationSelector;
    }

    public async Task<List<PutawayTaskDto>> Handle(GeneratePutawayTasksCommand request, CancellationToken cancellationToken)
    {
        // 1. Find all sound receipt lines that reside in a staging location
        var query = _context.ReceiptLines
            .Include(l => l.ReceivingSession)
            .Include(l => l.Item)
            .Include(l => l.StagingLocation)
            .Where(l => l.ReceivingSession.FacilityId == request.FacilityId &&
                        l.StagingLocationId != null &&
                        l.Condition == ReceiptConditionStatus.Sound &&
                        l.QuantityReceived > 0);

        if (request.ClientId.HasValue)
        {
            query = query.Where(l => l.ReceivingSession.ClientId == request.ClientId.Value || l.Item.ClientId == request.ClientId.Value);
        }

        var candidateLines = await query.ToListAsync(cancellationToken);

        // 2. Identify LPNs / ReceiptLines that already have active or completed tasks
        var existingTasks = await _context.PutawayTasks
            .AsNoTracking()
            .Where(t => t.FacilityId == request.FacilityId &&
                        (t.Status != PutawayTaskStatus.Cancelled))
            .ToListAsync(cancellationToken);

        var existingLpnIds = existingTasks.Where(t => t.LpnId.HasValue).Select(t => t.LpnId!.Value).ToHashSet();
        var existingLpnCodes = existingTasks.Where(t => !string.IsNullOrEmpty(t.LpnCode)).Select(t => t.LpnCode!).ToHashSet();

        var unassignedLines = candidateLines
            .Where(l => !existingLpnIds.Contains(l.Id) && (string.IsNullOrEmpty(l.Lpn) || !existingLpnCodes.Contains(l.Lpn)))
            .ToList();

        var generatedTasks = new List<PutawayTask>();

        foreach (var line in unassignedLines)
        {
            var clientId = line.ReceivingSession.ClientId ?? line.Item.ClientId;
            var facilityId = line.ReceivingSession.FacilityId;

            // Resolve Lot if LotNumber is present
            Guid? lotId = null;
            if (!string.IsNullOrWhiteSpace(line.LotNumber))
            {
                var lot = await _context.InventoryLots
                    .FirstOrDefaultAsync(l => l.ClientId == clientId && l.ItemId == line.ItemId && l.LotNumber == line.LotNumber, cancellationToken);
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

            generatedTasks.Add(task);
            _context.PutawayTasks.Add(task);
        }

        if (generatedTasks.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return generatedTasks.Select(t => new PutawayTaskDto
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
        }).ToList();
    }
}
