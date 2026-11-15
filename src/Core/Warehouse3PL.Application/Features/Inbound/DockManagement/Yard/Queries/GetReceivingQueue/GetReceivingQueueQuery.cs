using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Queries.GetReceivingQueue;

public record GetReceivingQueueQuery : IRequest<List<ReceivingQueueItemDto>>
{
    public Guid FacilityId { get; init; }
}

public class GetReceivingQueueQueryHandler : IRequestHandler<GetReceivingQueueQuery, List<ReceivingQueueItemDto>>
{
    private readonly IApplicationDbContext _context;

    public GetReceivingQueueQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ReceivingQueueItemDto>> Handle(GetReceivingQueueQuery request, CancellationToken cancellationToken)
    {
        var facilityExists = await _context.Facilities
            .AnyAsync(f => f.Id == request.FacilityId, cancellationToken);

        if (!facilityExists)
        {
            throw new EntityNotFoundException(nameof(Facility), request.FacilityId);
        }

        var activeStatuses = new[]
        {
            YardVisitStatus.CheckedIn,
            YardVisitStatus.WaitingInYard,
            YardVisitStatus.AssignedToDock,
            YardVisitStatus.AtDock
        };

        var visits = await _context.YardVisits
            .AsNoTracking()
            .Where(y => y.FacilityId == request.FacilityId && activeStatuses.Contains(y.Status))
            .Include(y => y.AdvanceShippingNotice)
                .ThenInclude(asn => asn!.PurchaseOrder)
            .Include(y => y.DockAppointment)
            .Include(y => y.Assignments)
                .ThenInclude(a => a.DockDoor)
            .OrderBy(y => y.CheckInTime)
            .ToListAsync(cancellationToken);

        var utcNow = DateTime.UtcNow;

        var queue = visits.Select(y =>
        {
            var activeAssignment = y.Assignments
                .Where(a => a.Status == DockAssignmentStatus.Assigned || a.Status == DockAssignmentStatus.AtDock)
                .OrderByDescending(a => a.AssignedAt)
                .FirstOrDefault();

            var waitDuration = Math.Max(0, (utcNow - y.CheckInTime).TotalMinutes);

            return new ReceivingQueueItemDto
            {
                YardVisitId = y.Id,
                VisitNumber = y.VisitNumber,
                AsnId = y.AsnId,
                AsnNumber = y.AdvanceShippingNotice?.AsnNumber,
                PoNumber = y.AdvanceShippingNotice?.PurchaseOrder?.PoNumber,
                CarrierName = y.CarrierName,
                TrailerNumber = y.TrailerNumber,
                Status = y.Status,
                CheckInTime = y.CheckInTime,
                WaitDurationMinutes = Math.Round(waitDuration, 1),
                AssignedDockDoorNumber = activeAssignment?.DockDoor?.DoorNumber,
                ScheduledStartTime = y.DockAppointment?.StartTime
            };
        }).ToList();

        return queue;
    }
}

