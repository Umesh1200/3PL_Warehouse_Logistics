using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Commands;

public record ReportShortPickCommand(
    Guid PickTaskId,
    decimal QuantityPicked,
    ShortPickReason Reason,
    string? Notes = null,
    string? ScannedLocationBarcode = null,
    string? ScannedItemBarcode = null,
    string? ScannedToteLpn = null,
    string? IdempotencyKey = null) : IRequest<ShortPickExecutionResult>;

public class ReportShortPickCommandHandler : IRequestHandler<ReportShortPickCommand, ShortPickExecutionResult>
{
    private readonly IPickExecutionService _executionService;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ReportShortPickCommandHandler(
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

    public async Task<ShortPickExecutionResult> Handle(
        ReportShortPickCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(ReportShortPickCommand));

        var task = await _context.PickTasks
            .AsNoTracking()
            .Select(t => new { t.Id, t.FacilityId, t.AssignedUserId, t.Status })
            .FirstOrDefaultAsync(t => t.Id == command.PickTaskId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PickTask), command.PickTaskId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(task.FacilityId, nameof(ReportShortPickCommand));

        var callerUserId = _currentUser.UserId;
        var isSupervisorOverride = _currentUser.IsSuperAdmin || _currentUser.HasPermission(Permissions.Outbound.TasksManage);

        if (!isSupervisorOverride)
        {
            if (!callerUserId.HasValue || callerUserId.Value == Guid.Empty)
            {
                throw new ForbiddenException("Authenticated staff operator identity is required to report short-pick.");
            }

            if (!task.AssignedUserId.HasValue)
            {
                throw new ForbiddenException("Pick task must be explicitly claimed by the operator before reporting a short-pick.");
            }

            if (task.AssignedUserId.Value != callerUserId.Value)
            {
                throw new ForbiddenException("This pick task is assigned to another operator.");
            }
        }

        var usedOverride = isSupervisorOverride &&
                           (!task.AssignedUserId.HasValue || task.AssignedUserId.Value != callerUserId);

        var result = await _executionService.ReportShortPickAsync(new ReportShortPickRequest
        {
            PickTaskId = command.PickTaskId,
            QuantityPicked = command.QuantityPicked,
            Reason = command.Reason,
            Notes = command.Notes,
            ScannedLocationBarcode = command.ScannedLocationBarcode,
            ScannedItemBarcode = command.ScannedItemBarcode,
            ScannedToteLpn = command.ScannedToteLpn,
            UserId = callerUserId ?? Guid.Empty,
            IdempotencyKey = command.IdempotencyKey
        }, cancellationToken);

        if (usedOverride)
        {
            _context.PickTaskStatusHistories.Add(new PickTaskStatusHistory
            {
                PickTaskId = task.Id,
                FromStatus = task.Status,
                ToStatus = result.TaskStatus,
                Reason = $"Task-management override short-pick by supervisor '{callerUserId}' (prior assignee: '{task.AssignedUserId?.ToString() ?? "unassigned"}').",
                ChangedByUserId = callerUserId ?? Guid.Empty,
                ChangedAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

