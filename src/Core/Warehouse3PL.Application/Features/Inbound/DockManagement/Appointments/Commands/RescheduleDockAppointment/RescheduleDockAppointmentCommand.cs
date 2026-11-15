using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Commands.RescheduleDockAppointment;

public record RescheduleDockAppointmentCommand : IRequest<DockAppointmentDto>
{
    public Guid Id { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public Guid? DockDoorId { get; init; }
}

public class RescheduleDockAppointmentCommandValidator : AbstractValidator<RescheduleDockAppointmentCommand>
{
    public RescheduleDockAppointmentCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Appointment ID is required.");

        RuleFor(x => x.StartTime)
            .NotEmpty().WithMessage("Start time is required.");

        RuleFor(x => x.EndTime)
            .NotEmpty().WithMessage("End time is required.")
            .GreaterThan(x => x.StartTime).WithMessage("End time must be after start time.");
    }
}

public class RescheduleDockAppointmentCommandHandler : IRequestHandler<RescheduleDockAppointmentCommand, DockAppointmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public RescheduleDockAppointmentCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<DockAppointmentDto> Handle(RescheduleDockAppointmentCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _context.DockAppointments
            .Include(a => a.Facility)
            .Include(a => a.DockDoor)
            .Include(a => a.AdvanceShippingNotice)
                .ThenInclude(asn => asn.Lines)
                    .ThenInclude(l => l.Item)
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);

        if (appointment == null)
        {
            throw new EntityNotFoundException(nameof(DockAppointment), request.Id);
        }

        if (!_currentUser.IsStaff && appointment.ClientId != _currentUser.TenantId)
        {
            throw new ForbiddenException("You do not have permission to reschedule this appointment.");
        }

        var targetDoorId = request.DockDoorId ?? appointment.DockDoorId;
        DockDoor? targetDoor = appointment.DockDoor;

        if (targetDoorId.HasValue)
        {
            if (targetDoor == null || targetDoor.Id != targetDoorId.Value)
            {
                targetDoor = await _context.DockDoors
                    .FirstOrDefaultAsync(d => d.Id == targetDoorId.Value && d.FacilityId == appointment.FacilityId, cancellationToken);

                if (targetDoor == null)
                {
                    throw new EntityNotFoundException(nameof(DockDoor), targetDoorId.Value);
                }
            }

            if (!targetDoor.IsOperational)
            {
                throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{targetDoor.DoorNumber}' is not operational (Status: {targetDoor.Status}).");
            }

            var requiresColdStorage = appointment.AdvanceShippingNotice.Lines.Any(l =>
                l.Item.StorageTempClass == StorageTempClass.Refrigerated ||
                l.Item.StorageTempClass == StorageTempClass.Frozen);

            if (requiresColdStorage && !targetDoor.SupportsRefrigerated)
            {
                throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{targetDoor.DoorNumber}' does not support temperature-controlled / refrigerated shipments.");
            }

            var hasOverlap = await _context.DockAppointments
                .AnyAsync(a =>
                    a.Id != appointment.Id &&
                    a.DockDoorId == targetDoorId.Value &&
                    a.Status != DockAppointmentStatus.Cancelled &&
                    request.StartTime < a.EndTime && request.EndTime > a.StartTime,
                    cancellationToken);

            if (hasOverlap)
            {
                throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{targetDoor.DoorNumber}' already has a conflicting appointment in this time window.");
            }
        }

        appointment.Reschedule(request.StartTime, request.EndTime, request.DockDoorId);
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
            DoorNumber = targetDoor?.DoorNumber,
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

