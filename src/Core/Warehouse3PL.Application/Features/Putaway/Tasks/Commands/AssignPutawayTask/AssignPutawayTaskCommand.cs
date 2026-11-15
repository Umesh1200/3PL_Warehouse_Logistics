using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Putaway.Tasks.Commands.AssignPutawayTask;

public record AssignPutawayTaskCommand : IRequest<PutawayTaskDto>
{
    public Guid TaskId { get; init; }
    public Guid? UserId { get; init; }
}

public class AssignPutawayTaskCommandValidator : AbstractValidator<AssignPutawayTaskCommand>
{
    public AssignPutawayTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty().WithMessage("TaskId is required.");
    }
}

public class AssignPutawayTaskCommandHandler : IRequestHandler<AssignPutawayTaskCommand, PutawayTaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public AssignPutawayTaskCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PutawayTaskDto> Handle(AssignPutawayTaskCommand request, CancellationToken cancellationToken)
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

        var targetUserId = request.UserId ?? _currentUser.UserId ?? Guid.Empty;
        task.AssignToWorker(targetUserId);

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
