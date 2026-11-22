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
            .Select(t => new { t.Id, t.FacilityId, t.AssignedUserId, t.Status })
            .FirstOrDefaultAsync(t => t.Id == command.PickTaskId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PickTask), command.PickTaskId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(task.FacilityId, nameof(ConfirmPickTaskCommand));

        var callerUserId = _currentUser.UserId;
        var isSupervisorOverride = _currentUser.IsSuperAdmin || _currentUser.HasPermission(Permissions.Outbound.TasksManage);

        if (!isSupervisorOverride)
        {
            if (!callerUserId.HasValue || callerUserId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Authenticated staff operator identity is required to execute pick confirmation.");
            }

            if (!task.AssignedUserId.HasValue)
            {
                throw new ForbiddenException("Pick task must be explicitly claimed by the operator before executing confirmation.");
            }

            if (task.AssignedUserId.Value != callerUserId.Value)
            {
                throw new ForbiddenException("This pick task is assigned to another operator.");
            }
        }

        var result = await _executionService.ConfirmPickAsync(new ConfirmPickRequest
        {
            PickTaskId = command.PickTaskId,
            ScannedLocationBarcode = command.ScannedLocationBarcode,
            ScannedItemBarcode = command.ScannedItemBarcode,
            ScannedLotNumber = command.ScannedLotNumber,
            ScannedToteLpn = command.ScannedToteLpn,
            Quantity = command.Quantity,
            UserId = callerUserId ?? Guid.Empty,
            IdempotencyKey = command.IdempotencyKey,
            RequireAssignedOwnership = true,
            AllowSupervisorOverride = isSupervisorOverride,
            OverrideReason = isSupervisorOverride
                ? $"Task-management override pick confirmation by supervisor '{callerUserId}'"
                : null
        }, cancellationToken);

        return result;
    }
}

