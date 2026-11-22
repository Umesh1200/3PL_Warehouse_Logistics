using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Commands;

/// <summary>
/// Atomically claims an eligible unassigned Pending pick task for the authenticated RF operator.
/// Operator identity is strictly derived from ICurrentUserContext.UserId (never caller-supplied).
/// </summary>
public record ClaimPickTaskCommand(Guid PickTaskId) : IRequest<PickTaskDto>;

public class ClaimPickTaskCommandHandler : IRequestHandler<ClaimPickTaskCommand, PickTaskDto>
{
    private static readonly SemaphoreSlim NonRelationalClaimGate = new(1, 1);

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public ClaimPickTaskCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<PickTaskDto> Handle(
        ClaimPickTaskCommand command,
        CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(ClaimPickTaskCommand));

        var callerUserId = _currentUser.UserId;
        if (!callerUserId.HasValue || callerUserId.Value == Guid.Empty)
        {
            throw new ForbiddenException("Authenticated staff user identity is required to claim a pick task.");
        }

        var snapshot = await _context.PickTasks
            .AsNoTracking()
            .Where(t => t.Id == command.PickTaskId && !t.IsDeleted)
            .Select(t => new
            {
                t.Id,
                t.FacilityId,
                t.Status,
                t.AssignedUserId,
                t.Version,
                PickWaveStatus = t.PickWave.Status
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot == null)
        {
            throw new EntityNotFoundException(nameof(PickTask), command.PickTaskId);
        }

        _outboundAuth.ValidateStaffMutationFacilityAccess(snapshot.FacilityId, nameof(ClaimPickTaskCommand));

        if (snapshot.AssignedUserId == callerUserId.Value &&
            (snapshot.Status == PickTaskStatus.Assigned || snapshot.Status == PickTaskStatus.InProgress))
        {
            return await LoadDtoAsync(command.PickTaskId, cancellationToken);
        }

        if (snapshot.AssignedUserId.HasValue && snapshot.AssignedUserId.Value != callerUserId.Value)
        {
            throw new ConcurrencyException($"Pick task '{command.PickTaskId}' has already been claimed by another operator.");
        }

        if (snapshot.Status != PickTaskStatus.Pending)
        {
            throw new InvalidDomainStateException(
                $"Pick task '{command.PickTaskId}' cannot be claimed in status '{snapshot.Status}'.");
        }

        if (snapshot.PickWaveStatus != PickWaveStatus.Released && snapshot.PickWaveStatus != PickWaveStatus.InProgress)
        {
            throw new InvalidDomainStateException(
                $"Pick task '{command.PickTaskId}' belongs to wave with status '{snapshot.PickWaveStatus}' and is not released for claiming.");
        }

        var isRelational = _context is DbContext dbCtx &&
                           !string.Equals(dbCtx.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.OrdinalIgnoreCase);
        if (isRelational)
        {
            var relCtx = (DbContext)_context;
            await _context.ExecuteInTransactionAsync(async () =>
            {
                var now = DateTime.UtcNow;
                var nextVersion = unchecked(snapshot.Version + 1u);
                var affected = await relCtx.Set<PickTask>()
                    .Where(t => t.Id == command.PickTaskId &&
                                !t.IsDeleted &&
                                t.Status == PickTaskStatus.Pending &&
                                t.AssignedUserId == null &&
                                t.Version == snapshot.Version)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.Status, PickTaskStatus.Assigned)
                        .SetProperty(t => t.AssignedUserId, callerUserId.Value)
                        .SetProperty(t => t.AssignedAtUtc, now)
                        .SetProperty(t => t.Version, nextVersion),
                        cancellationToken);

                if (affected == 0)
                {
                    var latest = await _context.PickTasks
                        .AsNoTracking()
                        .Where(t => t.Id == command.PickTaskId && !t.IsDeleted)
                        .Select(t => new { t.Status, t.AssignedUserId })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (latest != null &&
                        latest.AssignedUserId == callerUserId.Value &&
                        (latest.Status == PickTaskStatus.Assigned || latest.Status == PickTaskStatus.InProgress))
                    {
                        return true;
                    }

                    throw new ConcurrencyException(
                        $"Pick task '{command.PickTaskId}' was modified or claimed concurrently by another operator.");
                }

                _context.PickTaskStatusHistories.Add(new PickTaskStatusHistory
                {
                    PickTaskId = command.PickTaskId,
                    FromStatus = PickTaskStatus.Pending,
                    ToStatus = PickTaskStatus.Assigned,
                    Reason = "Self-claimed by RF operator",
                    ChangedByUserId = callerUserId.Value,
                    ChangedAtUtc = now
                });
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }, cancellationToken);

            return await LoadDtoAsync(command.PickTaskId, cancellationToken);
        }

        await NonRelationalClaimGate.WaitAsync(cancellationToken);
        try
        {
            _context.ClearChangeTracker();
            var tracked = await _context.PickTasks
                .Include(t => t.PickWave)
                .FirstOrDefaultAsync(t => t.Id == command.PickTaskId && !t.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(PickTask), command.PickTaskId);

            if (tracked.AssignedUserId == callerUserId.Value &&
                (tracked.Status == PickTaskStatus.Assigned || tracked.Status == PickTaskStatus.InProgress))
            {
                return await LoadDtoAsync(command.PickTaskId, cancellationToken);
            }

            if (tracked.Status != PickTaskStatus.Pending ||
                tracked.AssignedUserId.HasValue ||
                tracked.Version != snapshot.Version)
            {
                throw new ConcurrencyException(
                    $"Pick task '{command.PickTaskId}' was modified or claimed concurrently by another operator.");
            }

            tracked.Assign(callerUserId.Value);
            tracked.Version = unchecked(tracked.Version + 1u);
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            NonRelationalClaimGate.Release();
        }

        return await LoadDtoAsync(command.PickTaskId, cancellationToken);
    }

    private async Task<PickTaskDto> LoadDtoAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _context.PickTasks
            .Include(t => t.SourceLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.OutboundOrder)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PickTask), taskId);

        return PickTaskMappings.ToDto(task);
    }
}
