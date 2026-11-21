using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Putaway.Tasks.Queries.GetNextPutawayTask;

public record GetNextPutawayTaskQuery(Guid FacilityId, Guid? OperatorUserId = null) : IRequest<PutawayTaskDto?>;

public class GetNextPutawayTaskQueryHandler : IRequestHandler<GetNextPutawayTaskQuery, PutawayTaskDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseOperationalAuthorization _operationalAuth;

    public GetNextPutawayTaskQueryHandler(
        IApplicationDbContext context,
        WarehouseOperationalAuthorization operationalAuth)
    {
        _context = context;
        _operationalAuth = operationalAuth;
    }

    public async Task<PutawayTaskDto?> Handle(GetNextPutawayTaskQuery request, CancellationToken cancellationToken)
    {
        var scope = await _operationalAuth.ResolveQueryScopeAsync(
            null, request.FacilityId, cancellationToken);

        var tasksQuery = _context.PutawayTasks
            .AsNoTracking()
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.SourceLocation)
            .Include(t => t.SuggestedLocation)
            .Include(t => t.ActualLocation)
            .Where(t => t.FacilityId == request.FacilityId &&
                        (t.Status == PutawayTaskStatus.Pending ||
                         t.Status == PutawayTaskStatus.ReRouted ||
                         (request.OperatorUserId.HasValue && t.AssignedUserId == request.OperatorUserId &&
                          (t.Status == PutawayTaskStatus.Assigned || t.Status == PutawayTaskStatus.InTransit))));

        if (scope.ClientId.HasValue)
        {
            tasksQuery = tasksQuery.Where(t => t.ClientId == scope.ClientId.Value);
        }

        var tasks = await tasksQuery.ToListAsync(cancellationToken);

        if (tasks.Count == 0)
        {
            return null;
        }

        // Prioritize:
        // 1. Any task already assigned to this operator
        // 2. Priority: Expedite (3) > High (2) > Normal (1)
        // 3. FIFO by CreatedAtUtc
        var selectedTask = tasks
            .OrderByDescending(t => request.OperatorUserId.HasValue && t.AssignedUserId == request.OperatorUserId)
            .ThenByDescending(t => (int)t.Priority)
            .ThenBy(t => t.CreatedAtUtc)
            .First();

        return new PutawayTaskDto
        {
            Id = selectedTask.Id,
            ClientId = selectedTask.ClientId,
            FacilityId = selectedTask.FacilityId,
            ItemId = selectedTask.ItemId,
            Sku = selectedTask.Item.Sku,
            ItemDescription = selectedTask.Item.Description,
            LotId = selectedTask.LotId,
            LotNumber = selectedTask.Lot?.LotNumber,
            LpnId = selectedTask.LpnId,
            LpnCode = selectedTask.LpnCode,
            SourceLocationId = selectedTask.SourceLocationId,
            SourceLocationCode = selectedTask.SourceLocation.LocationCode,
            SuggestedLocationId = selectedTask.SuggestedLocationId,
            SuggestedLocationCode = selectedTask.SuggestedLocation.LocationCode,
            SuggestedLocationBarcode = selectedTask.SuggestedLocation.Barcode,
            ActualLocationId = selectedTask.ActualLocationId,
            ActualLocationCode = selectedTask.ActualLocation?.LocationCode,
            Quantity = selectedTask.Quantity,
            Status = selectedTask.Status,
            Priority = selectedTask.Priority,
            AssignedUserId = selectedTask.AssignedUserId,
            CreatedAtUtc = selectedTask.CreatedAtUtc,
            CompletedAtUtc = selectedTask.CompletedAtUtc,
            ExceptionReason = selectedTask.ExceptionReason,
            Notes = selectedTask.Notes,
            Version = selectedTask.Version
        };
    }
}

