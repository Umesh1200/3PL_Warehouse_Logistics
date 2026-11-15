using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Queries.GetDockAppointments;

public record GetDockAppointmentsQuery : PagedQuery, IRequest<PagedResult<DockAppointmentDto>>
{
    public Guid? FacilityId { get; init; }
    public Guid? ClientId { get; init; }
    public Guid? DockDoorId { get; init; }
    public DockAppointmentStatus? Status { get; init; }
    public DateTime? DateFrom { get; init; }
    public DateTime? DateTo { get; init; }
}

public class GetDockAppointmentsQueryHandler : IRequestHandler<GetDockAppointmentsQuery, PagedResult<DockAppointmentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public GetDockAppointmentsQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<DockAppointmentDto>> Handle(GetDockAppointmentsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.DockAppointments
            .AsNoTracking()
            .Include(a => a.Facility)
            .Include(a => a.DockDoor)
            .Include(a => a.AdvanceShippingNotice)
            .AsQueryable();

        if (!_currentUser.IsStaff)
        {
            query = query.Where(a => a.ClientId == _currentUser.TenantId);
        }
        else if (request.ClientId.HasValue)
        {
            query = query.Where(a => a.ClientId == request.ClientId.Value);
        }

        if (request.FacilityId.HasValue)
        {
            query = query.Where(a => a.FacilityId == request.FacilityId.Value);
        }

        if (request.DockDoorId.HasValue)
        {
            query = query.Where(a => a.DockDoorId == request.DockDoorId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(a => a.Status == request.Status.Value);
        }

        if (request.DateFrom.HasValue)
        {
            query = query.Where(a => a.StartTime >= request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            query = query.Where(a => a.EndTime <= request.DateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim().ToLower();
            query = query.Where(a =>
                a.AppointmentNumber.ToLower().Contains(term) ||
                a.AdvanceShippingNotice.AsnNumber.ToLower().Contains(term) ||
                (a.CarrierName != null && a.CarrierName.ToLower().Contains(term)) ||
                (a.TrailerNumber != null && a.TrailerNumber.ToLower().Contains(term)) ||
                (a.DriverName != null && a.DriverName.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.StartTime)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new DockAppointmentDto
            {
                Id = a.Id,
                FacilityId = a.FacilityId,
                FacilityCode = a.Facility.FacilityCode,
                ClientId = a.ClientId,
                AsnId = a.AsnId,
                AsnNumber = a.AdvanceShippingNotice.AsnNumber,
                DockDoorId = a.DockDoorId,
                DoorNumber = a.DockDoor != null ? a.DockDoor.DoorNumber : null,
                AppointmentNumber = a.AppointmentNumber,
                StartTime = a.StartTime,
                EndTime = a.EndTime,
                Status = a.Status,
                CarrierName = a.CarrierName,
                TrailerNumber = a.TrailerNumber,
                DriverName = a.DriverName,
                DriverPhone = a.DriverPhone,
                Notes = a.Notes,
                CancellationReason = a.CancellationReason,
                CancelledAt = a.CancelledAt,
                Version = a.Version,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return PagedResult<DockAppointmentDto>.Create(items, totalCount, request.PageNumber, request.PageSize);
    }
}

