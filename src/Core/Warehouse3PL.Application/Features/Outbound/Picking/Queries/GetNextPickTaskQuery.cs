using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Outbound.Picking.Queries;

/// <summary>
/// Strictly read-only RF query returning the next eligible pick task for the authenticated operator.
/// Never claims or modifies task ownership implicitly, and never exposes another operator's assigned tasks.
/// </summary>
public record GetNextPickTaskQuery(Guid? FacilityId = null) : IRequest<PickTaskDto?>;

public class GetNextPickTaskQueryHandler : IRequestHandler<GetNextPickTaskQuery, PickTaskDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly OutboundOperationalAuthorization _outboundAuth;

    public GetNextPickTaskQueryHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        OutboundOperationalAuthorization outboundAuth)
    {
        _context = context;
        _currentUser = currentUser;
        _outboundAuth = outboundAuth;
    }

    public async Task<PickTaskDto?> Handle(GetNextPickTaskQuery request, CancellationToken cancellationToken)
    {
        _outboundAuth.EnsureStaffOperationalIdentity(nameof(GetNextPickTaskQuery));

        var scope = await _outboundAuth.ResolveOutboundQueryScopeAsync(
            requestedClientId: null,
            requestedFacilityId: request.FacilityId,
            allowCrossFacilityRead: false,
            allowClient: false,
            cancellationToken);

        if (!scope.FacilityId.HasValue || scope.FacilityId.Value == Guid.Empty)
        {
            return null;
        }

        var callerUserId = _currentUser.UserId;
        var hasCallerId = callerUserId.HasValue && callerUserId.Value != Guid.Empty;
        var callerId = callerUserId.GetValueOrDefault();
        var facilityId = scope.FacilityId.Value;

        var candidate = await _context.PickTasks
            .AsNoTracking()
            .Where(t => !t.IsDeleted &&
                        t.FacilityId == facilityId &&
                        ((hasCallerId &&
                          t.AssignedUserId == callerId &&
                          (t.Status == PickTaskStatus.Assigned || t.Status == PickTaskStatus.InProgress)) ||
                         (t.AssignedUserId == null &&
                          t.Status == PickTaskStatus.Pending &&
                          (t.PickWave.Status == PickWaveStatus.Released || t.PickWave.Status == PickWaveStatus.InProgress))))
            .OrderByDescending(t => hasCallerId && t.AssignedUserId == callerId)
            .ThenByDescending(t => t.PickWave.Priority)
            .ThenBy(t => t.PickSequence)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Include(t => t.SourceLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.OutboundOrder)
            .FirstOrDefaultAsync(cancellationToken);

        return candidate == null ? null : PickTaskMappings.ToDto(candidate);
    }
}
