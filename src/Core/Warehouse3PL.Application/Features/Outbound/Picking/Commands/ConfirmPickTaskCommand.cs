using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Commands;

public record ConfirmPickTaskCommand(
    Guid PickTaskId,
    string ScannedLocationBarcode,
    string ScannedItemBarcode,
    string? ScannedLotNumber,
    string ScannedToteLpn,
    decimal Quantity,
    string? IdempotencyKey = null) : IRequest<PickConfirmationResult>;

public class ConfirmPickTaskCommandHandler : IRequestHandler<ConfirmPickTaskCommand, PickConfirmationResult>
{
    private readonly IPickExecutionService _executionService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ConfirmPickTaskCommandHandler(
        IPickExecutionService executionService,
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _executionService = executionService;
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<PickConfirmationResult> Handle(
        ConfirmPickTaskCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(ConfirmPickTaskCommand));

        var task = await _context.PickTasks
            .AsNoTracking()
            .Select(t => new { t.Id, t.FacilityId, t.AssignedUserId })
            .FirstOrDefaultAsync(t => t.Id == command.PickTaskId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PickTask), command.PickTaskId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(task.FacilityId, nameof(ConfirmPickTaskCommand));

        if (task.AssignedUserId.HasValue &&
            task.AssignedUserId.Value != _currentUser.UserId &&
            !_currentUser.IsSuperAdmin &&
            !_currentUser.HasPermission(Permissions.Outbound.TasksManage))
        {
            throw new ForbiddenException("This pick task is assigned to another operator.");
        }

        return await _executionService.ConfirmPickAsync(new ConfirmPickRequest
        {
            PickTaskId = command.PickTaskId,
            ScannedLocationBarcode = command.ScannedLocationBarcode,
            ScannedItemBarcode = command.ScannedItemBarcode,
            ScannedLotNumber = command.ScannedLotNumber,
            ScannedToteLpn = command.ScannedToteLpn,
            Quantity = command.Quantity,
            UserId = _currentUser.UserId ?? Guid.Empty,
            IdempotencyKey = command.IdempotencyKey
        }, cancellationToken);
    }
}

