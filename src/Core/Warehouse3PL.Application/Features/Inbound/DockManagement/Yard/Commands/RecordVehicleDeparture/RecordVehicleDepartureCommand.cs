using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.RecordVehicleDeparture;

public record RecordVehicleDepartureCommand : IRequest<YardVisitDto>
{
    public Guid YardVisitId { get; init; }
}

public class RecordVehicleDepartureCommandValidator : AbstractValidator<RecordVehicleDepartureCommand>
{
    public RecordVehicleDepartureCommandValidator()
    {
        RuleFor(x => x.YardVisitId)
            .NotEmpty().WithMessage("Yard visit ID is required.");
    }
}

public class RecordVehicleDepartureCommandHandler : IRequestHandler<RecordVehicleDepartureCommand, YardVisitDto>
{
    private readonly IApplicationDbContext _context;

    public RecordVehicleDepartureCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<YardVisitDto> Handle(RecordVehicleDepartureCommand request, CancellationToken cancellationToken)
    {
        var visit = await _context.YardVisits
            .Include(y => y.Facility)
            .Include(y => y.AdvanceShippingNotice)
            .Include(y => y.DockAppointment)
            .Include(y => y.Assignments)
                .ThenInclude(a => a.DockDoor)
            .FirstOrDefaultAsync(y => y.Id == request.YardVisitId, cancellationToken);

        if (visit == null)
        {
            throw new EntityNotFoundException(nameof(YardVisit), request.YardVisitId);
        }

        visit.RecordDeparture();
        await _context.SaveChangesAsync(cancellationToken);

        var activeDoorNumber = visit.Assignments
            .Where(a => a.Status == Domain.Enums.DockAssignmentStatus.Assigned || a.Status == Domain.Enums.DockAssignmentStatus.AtDock)
            .Select(a => a.DockDoor.DoorNumber)
            .FirstOrDefault();

        return new YardVisitDto
        {
            Id = visit.Id,
            FacilityId = visit.FacilityId,
            FacilityCode = visit.Facility.FacilityCode,
            ClientId = visit.ClientId,
            AsnId = visit.AsnId,
            AsnNumber = visit.AdvanceShippingNotice?.AsnNumber,
            DockAppointmentId = visit.DockAppointmentId,
            AppointmentNumber = visit.DockAppointment?.AppointmentNumber,
            VisitNumber = visit.VisitNumber,
            CarrierName = visit.CarrierName,
            TrailerNumber = visit.TrailerNumber,
            LicensePlate = visit.LicensePlate,
            DriverName = visit.DriverName,
            DriverPhone = visit.DriverPhone,
            CheckInTime = visit.CheckInTime,
            DepartureTime = visit.DepartureTime,
            Status = visit.Status,
            ParkingLocation = visit.ParkingLocation,
            Notes = visit.Notes,
            CurrentDockDoorNumber = activeDoorNumber,
            Version = visit.Version,
            CreatedAt = visit.CreatedAt
        };
    }
}

