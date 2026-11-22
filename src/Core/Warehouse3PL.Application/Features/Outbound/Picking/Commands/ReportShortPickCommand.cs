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
            .Select(t => new { t.Id, t.FacilityId, t.AssignedUserId })
            .FirstOrDefaultAsync(t => t.Id == command.PickTaskId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PickTask), command.PickTaskId);

        _outboundAuth.ValidateStaffMutationFacilityAccess(task.FacilityId, nameof(ReportShortPickCommand));

        if (task.AssignedUserId.HasValue &&
            task.AssignedUserId.Value != _currentUser.UserId &&
            !_currentUser.IsSuperAdmin &&
            !_currentUser.HasPermission(Permissions.Outbound.TasksManage))
        {
            throw new ForbiddenException("This pick task is assigned to another operator.");
        }

        return await _executionService.ReportShortPickAsync(new ReportShortPickRequest
        {
            PickTaskId = command.PickTaskId,
            QuantityPicked = command.QuantityPicked,
            Reason = command.Reason,
            Notes = command.Notes,
            ScannedLocationBarcode = command.ScannedLocationBarcode,
            ScannedItemBarcode = command.ScannedItemBarcode,
            ScannedToteLpn = command.ScannedToteLpn,
            UserId = _currentUser.UserId ?? Guid.Empty,
            IdempotencyKey = command.IdempotencyKey
        }, cancellationToken);
    }
}

