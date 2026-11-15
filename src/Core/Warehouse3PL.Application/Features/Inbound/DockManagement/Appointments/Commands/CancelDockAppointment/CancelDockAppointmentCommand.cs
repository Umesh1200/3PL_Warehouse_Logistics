using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Commands.CancelDockAppointment;

public record CancelDockAppointmentCommand : IRequest<DockAppointmentDto>
{
    public Guid Id { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public class CancelDockAppointmentCommandValidator : AbstractValidator<CancelDockAppointmentCommand>
{
    public CancelDockAppointmentCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Appointment ID is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Cancellation reason is required.")
            .MaximumLength(500).WithMessage("Cancellation reason cannot exceed 500 characters.");
    }
}

public class CancelDockAppointmentCommandHandler : IRequestHandler<CancelDockAppointmentCommand, DockAppointmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public CancelDockAppointmentCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<DockAppointmentDto> Handle(CancelDockAppointmentCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _context.DockAppointments
            .Include(a => a.Facility)
            .Include(a => a.DockDoor)
            .Include(a => a.AdvanceShippingNotice)
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

        if (appointment == null)
        {
            throw new EntityNotFoundException(nameof(DockAppointment), request.Id);
        }

        if (!_currentUser.IsStaff && appointment.ClientId != _currentUser.TenantId)
        {
            throw new ForbiddenException("You do not have permission to cancel this appointment.");
        }

        appointment.Cancel(request.Reason.Trim());
        await _context.SaveChangesAsync(cancellationToken);

        return new DockAppointmentDto
        {
            Id = appointment.Id,
            FacilityId = appointment.FacilityId,
            FacilityCode = appointment.Facility.FacilityCode,
            ClientId = appointment.ClientId,
            AsnId = appointment.AsnId,
            AsnNumber = appointment.AdvanceShippingNotice.AsnNumber,
            DockDoorId = appointment.DockDoorId,
            DoorNumber = appointment.DockDoor?.DoorNumber,
            AppointmentNumber = appointment.AppointmentNumber,
            StartTime = appointment.StartTime,
            EndTime = appointment.EndTime,
            Status = appointment.Status,
            CarrierName = appointment.CarrierName,
            TrailerNumber = appointment.TrailerNumber,
            DriverName = appointment.DriverName,
            DriverPhone = appointment.DriverPhone,
            Notes = appointment.Notes,
            CancellationReason = appointment.CancellationReason,
            CancelledAt = appointment.CancelledAt,
            Version = appointment.Version,
            CreatedAt = appointment.CreatedAt
        };
    }
}

