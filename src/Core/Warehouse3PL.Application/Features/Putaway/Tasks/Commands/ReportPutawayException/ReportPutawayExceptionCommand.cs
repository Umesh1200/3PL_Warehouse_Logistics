using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ReportPutawayException;

public record ReportPutawayExceptionCommand : IRequest<PutawayTaskDto>
{
    public Guid TaskId { get; init; }
    public string ExceptionReason { get; init; } = string.Empty;
    public bool AutoReRoute { get; init; } = true;
}

public class ReportPutawayExceptionCommandValidator : AbstractValidator<ReportPutawayExceptionCommand>
{
    public ReportPutawayExceptionCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty().WithMessage("TaskId is required.");
        RuleFor(x => x.ExceptionReason).NotEmpty().WithMessage("ExceptionReason is required.");
    }
}

public class ReportPutawayExceptionCommandHandler : IRequestHandler<ReportPutawayExceptionCommand, PutawayTaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPutawayLocationSelector _locationSelector;

    public ReportPutawayExceptionCommandHandler(
        IApplicationDbContext context,
        IPutawayLocationSelector locationSelector)
    {
        _context = context;
        _locationSelector = locationSelector;
    }

    public async Task<PutawayTaskDto> Handle(ReportPutawayExceptionCommand request, CancellationToken cancellationToken)
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

        // 1. Report Exception
        task.ReportException(request.ExceptionReason);

        // 2. Dynamic Re-routing
        if (request.AutoReRoute)
        {
            var blockedLocationId = task.SuggestedLocationId;
            var alternativeLocation = await _locationSelector.SelectOptimalLocationAsync(
                task.ClientId,
                task.FacilityId,
                task.ItemId,
                task.Quantity,
                task.LotId,
                [blockedLocationId],
                cancellationToken);

            if (alternativeLocation != null)
            {
                task.ReRoute(
                    alternativeLocation.Id,
                    $"Dynamic reroute: '{request.ExceptionReason}'. Alternate bin allocated: {alternativeLocation.LocationCode}");
                task.SuggestedLocation = alternativeLocation;
            }
        }

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
            SuggestedLocationCode = task.SuggestedLocation?.LocationCode ?? string.Empty,
            SuggestedLocationBarcode = task.SuggestedLocation?.Barcode ?? string.Empty,
            ActualLocationId = task.ActualLocationId,
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
    }
}

