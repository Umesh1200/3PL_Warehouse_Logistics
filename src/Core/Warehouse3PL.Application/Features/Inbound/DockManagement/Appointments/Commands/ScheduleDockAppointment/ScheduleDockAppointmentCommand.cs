using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.Appointments.Commands.ScheduleDockAppointment;

public record ScheduleDockAppointmentCommand : IRequest<DockAppointmentDto>
{
    public Guid FacilityId { get; init; }
    public Guid AsnId { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public Guid? DockDoorId { get; init; }
    public string? CarrierName { get; init; }
    public string? TrailerNumber { get; init; }
    public string? DriverName { get; init; }
    public string? DriverPhone { get; init; }
    public string? Notes { get; init; }
}

public class ScheduleDockAppointmentCommandValidator : AbstractValidator<ScheduleDockAppointmentCommand>
{
    public ScheduleDockAppointmentCommandValidator()
    {
        RuleFor(x => x.FacilityId)
            .NotEmpty().WithMessage("Facility ID is required.");

        RuleFor(x => x.AsnId)
            .NotEmpty().WithMessage("ASN ID is required.");

        RuleFor(x => x.StartTime)
            .NotEmpty().WithMessage("Start time is required.");

        RuleFor(x => x.EndTime)
            .NotEmpty().WithMessage("End time is required.")
            .GreaterThan(x => x.StartTime).WithMessage("End time must be after start time.");
    }
}

public class ScheduleDockAppointmentCommandHandler : IRequestHandler<ScheduleDockAppointmentCommand, DockAppointmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public ScheduleDockAppointmentCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<DockAppointmentDto> Handle(ScheduleDockAppointmentCommand request, CancellationToken cancellationToken)
    {
        var facility = await _context.Facilities
            .FirstOrDefaultAsync(f => f.Id == request.FacilityId, cancellationToken);

        if (facility == null)
        {
            throw new EntityNotFoundException(nameof(Facility), request.FacilityId);
        }

        var asn = await _context.AdvanceShippingNotices
            .IgnoreQueryFilters()
            .Include(a => a.Lines)
                .ThenInclude(l => l.Item)
            .FirstOrDefaultAsync(a => a.Id == request.AsnId, cancellationToken);

        if (asn == null)
        {
            throw new EntityNotFoundException(nameof(AdvanceShippingNotice), request.AsnId);
        }

        if (!_currentUser.IsStaff && asn.ClientId != _currentUser.TenantId)
        {
            throw new ForbiddenException("You do not have permission to schedule appointments for this ASN.");
        }

        if (asn.FacilityId != request.FacilityId)
        {
            throw new ValidationException(nameof(request.FacilityId), "ASN facility does not match appointment facility.");
        }

        if (asn.Status == AsnStatus.Cancelled || asn.Status == AsnStatus.Received)
        {
            throw new ValidationException(nameof(request.AsnId), $"Cannot schedule appointment for ASN '{asn.AsnNumber}' in '{asn.Status}' status.");
        }

        DockDoor? dockDoor = null;
        if (request.DockDoorId.HasValue)
        {
            dockDoor = await _context.DockDoors
                .FirstOrDefaultAsync(d => d.Id == request.DockDoorId.Value && d.FacilityId == request.FacilityId, cancellationToken);

            if (dockDoor == null)
            {
                throw new EntityNotFoundException(nameof(DockDoor), request.DockDoorId.Value);
            }

            if (!dockDoor.IsOperational)
            {
                throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{dockDoor.DoorNumber}' is not operational (Status: {dockDoor.Status}).");
            }

            // Thermal check
            var requiresColdStorage = asn.Lines.Any(l => 
                l.Item.StorageTempClass == StorageTempClass.Refrigerated || 
                l.Item.StorageTempClass == StorageTempClass.Frozen);

            if (requiresColdStorage && !dockDoor.SupportsRefrigerated)
            {
                throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{dockDoor.DoorNumber}' does not support temperature-controlled / refrigerated shipments.");
            }

            // Conflict check
            var hasOverlap = await _context.DockAppointments
                .AnyAsync(a =>
                    a.DockDoorId == request.DockDoorId.Value &&
                    a.Status != DockAppointmentStatus.Cancelled &&
                    request.StartTime < a.EndTime && request.EndTime > a.StartTime,
                    cancellationToken);

            if (hasOverlap)
            {
                throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{dockDoor.DoorNumber}' already has a conflicting appointment in this time window.");
            }
        }

        var appointmentCount = await _context.DockAppointments
            .CountAsync(a => a.FacilityId == request.FacilityId, cancellationToken);

        var datePrefix = DateTime.UtcNow.ToString("yyyyMMdd");
        var appointmentNumber = $"APT-{facility.FacilityCode}-{datePrefix}-{(appointmentCount + 1):D4}";

        var appointment = new DockAppointment
        {
            FacilityId = request.FacilityId,
            ClientId = asn.ClientId,
            AsnId = request.AsnId,
            DockDoorId = request.DockDoorId,
            AppointmentNumber = appointmentNumber,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = DockAppointmentStatus.Scheduled,
            CarrierName = request.CarrierName ?? asn.CarrierName,
            TrailerNumber = request.TrailerNumber ?? asn.TrailerOrContainerNumber,
            DriverName = request.DriverName,
            DriverPhone = request.DriverPhone,
            Notes = request.Notes
        };

        _context.DockAppointments.Add(appointment);
        await _context.SaveChangesAsync(cancellationToken);

        return new DockAppointmentDto
        {
            Id = appointment.Id,
            FacilityId = appointment.FacilityId,
            FacilityCode = facility.FacilityCode,
            ClientId = appointment.ClientId,
            AsnId = appointment.AsnId,
            AsnNumber = asn.AsnNumber,
            DockDoorId = appointment.DockDoorId,
            DoorNumber = dockDoor?.DoorNumber,
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

