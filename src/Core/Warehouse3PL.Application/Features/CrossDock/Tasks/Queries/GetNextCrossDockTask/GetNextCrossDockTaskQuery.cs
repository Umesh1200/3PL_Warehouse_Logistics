using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.CrossDock.Tasks.Queries.GetNextCrossDockTask;

public record GetNextCrossDockTaskQuery(Guid FacilityId, Guid? OperatorUserId = null) : IRequest<CrossDockTaskDto?>;

public class GetNextCrossDockTaskQueryHandler : IRequestHandler<GetNextCrossDockTaskQuery, CrossDockTaskDto?>
{
    private readonly IApplicationDbContext _context;

    public GetNextCrossDockTaskQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CrossDockTaskDto?> Handle(GetNextCrossDockTaskQuery request, CancellationToken cancellationToken)
    {
        var tasksQuery = _context.CrossDockTasks
            .AsNoTracking()
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.SourceLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.ActualDestinationLocation)
            .Where(t => t.FacilityId == request.FacilityId &&
                        (t.Status == CrossDockTaskStatus.Pending ||
                         (request.OperatorUserId.HasValue && t.AssignedUserId == request.OperatorUserId &&
                          (t.Status == CrossDockTaskStatus.Assigned || t.Status == CrossDockTaskStatus.InTransit))));

        var tasks = await tasksQuery.ToListAsync(cancellationToken);

        if (tasks.Count == 0)
        {
            return null;
        }

        // Priority ordering: CriticalColdChain (3) > Urgent (2) > Normal (1), FIFO
        var selectedTask = tasks
            .OrderByDescending(t => request.OperatorUserId.HasValue && t.AssignedUserId == request.OperatorUserId)
            .ThenByDescending(t => (int)t.Priority)
            .ThenBy(t => t.CreatedAtUtc)
            .First();

        return new CrossDockTaskDto
        {
            Id = selectedTask.Id,
            TaskNumber = selectedTask.TaskNumber,
            CrossDockPlanId = selectedTask.CrossDockPlanId,
            CrossDockPlanLineId = selectedTask.CrossDockPlanLineId,
            ClientId = selectedTask.ClientId,
            FacilityId = selectedTask.FacilityId,
            ItemId = selectedTask.ItemId,
            Sku = selectedTask.Item.Sku,
            ItemDescription = selectedTask.Item.Description,
            LotId = selectedTask.LotId,
            LotNumber = selectedTask.Lot?.LotNumber,
            Lpn = selectedTask.Lpn,
            SourceLocationId = selectedTask.SourceLocationId,
            SourceLocationCode = selectedTask.SourceLocation.LocationCode,
            DestinationLocationId = selectedTask.DestinationLocationId,
            DestinationLocationCode = selectedTask.DestinationLocation.LocationCode,
            DestinationLocationBarcode = selectedTask.DestinationLocation.Barcode,
            ActualDestinationLocationId = selectedTask.ActualDestinationLocationId,
            ActualDestinationLocationCode = selectedTask.ActualDestinationLocation?.LocationCode,
            Quantity = selectedTask.Quantity,
            Status = selectedTask.Status,
            Priority = selectedTask.Priority,
            AssignedUserId = selectedTask.AssignedUserId,
            CreatedAtUtc = selectedTask.CreatedAtUtc,
            CompletedAtUtc = selectedTask.CompletedAtUtc,
            Notes = selectedTask.Notes,
            Version = selectedTask.Version
        };
    }
}

