using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.CycleCounts.Commands.RecordCycleCountItems;

public record CountObservationDto(Guid ItemCountId, decimal CountedQty);

public record RecordCycleCountItemsCommand(Guid CycleCountId, List<CountObservationDto> Observations) : IRequest<bool>;

public class RecordCycleCountItemsCommandValidator : AbstractValidator<RecordCycleCountItemsCommand>
{
    public RecordCycleCountItemsCommandValidator()
    {
        RuleFor(x => x.CycleCountId).NotEmpty();
        RuleFor(x => x.Observations).NotEmpty();
    }
}

public class RecordCycleCountItemsCommandHandler : IRequestHandler<RecordCycleCountItemsCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public RecordCycleCountItemsCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(RecordCycleCountItemsCommand request, CancellationToken cancellationToken)
    {
        var count = await _context.CycleCounts
            .Include(cc => cc.Items)
            .FirstOrDefaultAsync(cc => cc.Id == request.CycleCountId, cancellationToken);

        if (count == null)
        {
            throw new EntityNotFoundException(nameof(CycleCount), request.CycleCountId);
        }

        var itemMap = count.Items.ToDictionary(i => i.Id);

        foreach (var obs in request.Observations)
        {
            if (itemMap.TryGetValue(obs.ItemCountId, out var item))
            {
                item.CountedQty = obs.CountedQty;
                item.CounterUserId = _currentUser.UserId;
                item.CountedAt = DateTime.UtcNow;
            }
        }

        // If all items counted, move to PendingApproval
        if (count.Items.All(i => i.CountedQty.HasValue))
        {
            count.Status = CycleCountStatus.PendingApproval;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

