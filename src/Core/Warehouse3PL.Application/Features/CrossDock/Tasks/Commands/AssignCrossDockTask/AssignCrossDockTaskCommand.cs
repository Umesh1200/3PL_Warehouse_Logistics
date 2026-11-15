using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.AssignCrossDockTask;

public record AssignCrossDockTaskCommand : IRequest<CrossDockTaskDto>
{
    public Guid TaskId { get; init; }
    public Guid? UserId { get; init; }
}

public class AssignCrossDockTaskCommandValidator : AbstractValidator<AssignCrossDockTaskCommand>
{
    public AssignCrossDockTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty().WithMessage("TaskId is required.");
    }
}

public class AssignCrossDockTaskCommandHandler : IRequestHandler<AssignCrossDockTaskCommand, CrossDockTaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public AssignCrossDockTaskCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<CrossDockTaskDto> Handle(AssignCrossDockTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _context.CrossDockTasks
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.SourceLocation)
            .Include(t => t.DestinationLocation)
            .FirstOrDefaultAsync(t => t.Id == request.TaskId, cancellationToken);

        if (task == null)
        {
            throw new EntityNotFoundException(nameof(CrossDockTask), request.TaskId);
        }

        var targetUserId = request.UserId ?? _currentUser.UserId ?? Guid.Empty;
        task.AssignToWorker(targetUserId);

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
            Quantity = task.Quantity,
            Status = task.Status,
            Priority = task.Priority,
            AssignedUserId = task.AssignedUserId,
            CreatedAtUtc = task.CreatedAtUtc,
            Notes = task.Notes,
            Version = task.Version
        };
    }
}

