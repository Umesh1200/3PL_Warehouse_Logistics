using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Queries.GetYardVisits;

public record GetYardVisitsQuery : PagedQuery, IRequest<PagedResult<YardVisitDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public YardVisitStatus? Status { get; init; }
    public string? TrailerNumber { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
}

public class GetYardVisitsQueryHandler : IRequestHandler<GetYardVisitsQuery, PagedResult<YardVisitDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetYardVisitsQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<YardVisitDto>> Handle(GetYardVisitsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.YardVisits
            .AsNoTracking()
            .Include(y => y.Facility)
            .Include(y => y.AdvanceShippingNotice)
            .Include(y => y.DockAppointment)
            .Include(y => y.Assignments)
                .ThenInclude(a => a.DockDoor)
            .AsQueryable();

        if (!_currentUser.IsStaff)
        {
            query = query.Where(y => y.ClientId == _currentUser.TenantId || y.ClientId == null);
        }
        else if (request.ClientId.HasValue)
        {
            query = query.Where(y => y.ClientId == request.ClientId.Value);
        }

        if (request.FacilityId.HasValue)
        {
            query = query.Where(y => y.FacilityId == request.FacilityId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(y => y.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.TrailerNumber))
        {
            var trailer = request.TrailerNumber.Trim().ToUpperInvariant();
            query = query.Where(y => y.TrailerNumber == trailer);
        }

        if (request.DateFrom.HasValue)
        {
            query = query.Where(y => y.CheckInTime >= request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            query = query.Where(y => y.CheckInTime <= request.DateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(y =>
                y.VisitNumber.ToLower().Contains(term) ||
                y.TrailerNumber.ToLower().Contains(term) ||
                y.CarrierName.ToLower().Contains(term) ||
                (y.DriverName != null && y.DriverName.ToLower().Contains(term)) ||
                (y.LicensePlate != null && y.LicensePlate.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var visits = await query
            .OrderByDescending(y => y.CheckInTime)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = visits.Select(y =>
        {
            var activeAssignment = y.Assignments
                .Where(a => a.Status == DockAssignmentStatus.Assigned || a.Status == DockAssignmentStatus.AtDock)
                .OrderByDescending(a => a.AssignedAt)
                .FirstOrDefault();

            return new YardVisitDto
            {
                Id = y.Id,
                FacilityId = y.FacilityId,
                FacilityCode = y.Facility.FacilityCode,
                ClientId = y.ClientId,
                AsnId = y.AsnId,
                AsnNumber = y.AdvanceShippingNotice?.AsnNumber,
                DockAppointmentId = y.DockAppointmentId,
                AppointmentNumber = y.DockAppointment?.AppointmentNumber,
                VisitNumber = y.VisitNumber,
                CarrierName = y.CarrierName,
                TrailerNumber = y.TrailerNumber,
                LicensePlate = y.LicensePlate,
                DriverName = y.DriverName,
                DriverPhone = y.DriverPhone,
                CheckInTime = y.CheckInTime,
                DepartureTime = y.DepartureTime,
                Status = y.Status,
                ParkingLocation = y.ParkingLocation,
                Notes = y.Notes,
                CurrentDockDoorNumber = activeAssignment?.DockDoor?.DoorNumber,
                Version = y.Version,
                CreatedAt = y.CreatedAt
            };
        }).ToList();

        return PagedResult<YardVisitDto>.Create(items, totalCount, request.PageNumber, request.PageSize);
    }
}

