using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Commands;

public record AssignPickTaskCommand(
    Guid PickTaskId,
    Guid OperatorUserId) : IRequest<PickTaskDto>;

public class AssignPickTaskCommandHandler : IRequestHandler<AssignPickTaskCommand, PickTaskDto>
{
    private readonly IApplicationDbContext _context;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public AssignPickTaskCommandHandler(
        IApplicationDbContext context,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _outboundAuth = outboundAuth;
    }

    public async Task<PickTaskDto> Handle(
        AssignPickTaskCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(AssignPickTaskCommand));

        var task = await _context.PickTasks
            .Include(t => t.SourceLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.OutboundOrder)
            .FirstOrDefaultAsync(t => t.Id == command.PickTaskId && !t.IsDeleted, cancellationToken);

        if (task == null)
        {
            throw new EntityNotFoundException(nameof(task), command.PickTaskId);
        }

        _outboundAuth.ValidateStaffMutationFacilityAccess(task.FacilityId, nameof(AssignPickTaskCommand));

        var targetUser = await _context.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.UserType, u.Status, u.IsDeleted })
            .FirstOrDefaultAsync(u => u.Id == command.OperatorUserId && !u.IsDeleted, cancellationToken);

        if (targetUser == null ||
            targetUser.UserType != Domain.Enums.UserType.Staff ||
            targetUser.Status != Domain.Enums.UserStatus.Active)
        {
            throw new ValidationException(nameof(command.OperatorUserId),
                "Target operator must be an active warehouse staff user.");
        }

        task.Assign(command.OperatorUserId);
        task.Version = unchecked(task.Version + 1u);
        await _context.SaveChangesAsync(cancellationToken);

        return PickTaskMappings.ToDto(task);
    }
}

