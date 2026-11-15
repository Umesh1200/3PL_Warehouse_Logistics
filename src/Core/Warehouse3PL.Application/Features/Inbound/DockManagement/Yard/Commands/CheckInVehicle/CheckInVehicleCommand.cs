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

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.CheckInVehicle;

public record CheckInVehicleCommand : IRequest<YardVisitDto>
{
    public Guid FacilityId { get; init; }
    public string CarrierName { get; init; } = string.Empty;
    public string TrailerNumber { get; init; } = string.Empty;
    public string? LicensePlate { get; init; }
    public string? DriverName { get; init; }
    public string? DriverPhone { get; init; }
    public string? ParkingLocation { get; init; }
    public string? Notes { get; init; }
    public Guid? DockAppointmentId { get; init; }
    public Guid? AsnId { get; init; }
}

public class CheckInVehicleCommandValidator : AbstractValidator<CheckInVehicleCommand>
{
    public CheckInVehicleCommandValidator()
    {
        RuleFor(x => x.FacilityId)
            .NotEmpty().WithMessage("Facility ID is required.");

        RuleFor(x => x.CarrierName)
            .NotEmpty().WithMessage("Carrier name is required.")
            .MaximumLength(100).WithMessage("Carrier name must not exceed 100 characters.");

        RuleFor(x => x.TrailerNumber)
            .NotEmpty().WithMessage("Trailer / container number is required.")
            .MaximumLength(50).WithMessage("Trailer number must not exceed 50 characters.");
    }
}

public class CheckInVehicleCommandHandler : IRequestHandler<CheckInVehicleCommand, YardVisitDto>
{
    private readonly IApplicationDbContext _context;

    public CheckInVehicleCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<YardVisitDto> Handle(CheckInVehicleCommand request, CancellationToken cancellationToken)
    {
        var facility = await _context.Facilities
            .FirstOrDefaultAsync(f => f.Id == request.FacilityId, cancellationToken);

        if (facility == null)
        {
            throw new EntityNotFoundException(nameof(Facility), request.FacilityId);
        }

        var normalizedTrailer = request.TrailerNumber.Trim().ToUpperInvariant();

        var activeTrailerExists = await _context.YardVisits
            .AnyAsync(y =>
                y.FacilityId == request.FacilityId &&
                y.TrailerNumber == normalizedTrailer &&
                y.Status != YardVisitStatus.Departed,
                cancellationToken);

        if (activeTrailerExists)
        {
            throw new ValidationException(nameof(request.TrailerNumber), $"Trailer '{normalizedTrailer}' is already checked in and active in this facility yard.");
        }

        Guid? targetClientId = null;
        Guid? targetAsnId = request.AsnId;
        string? asnNumber = null;
        string? appointmentNumber = null;

        if (request.DockAppointmentId.HasValue)
        {
            var appointment = await _context.DockAppointments
                .Include(a => a.AdvanceShippingNotice)
                .FirstOrDefaultAsync(a => a.Id == request.DockAppointmentId.Value, cancellationToken);

            if (appointment == null)
            {
                throw new EntityNotFoundException(nameof(DockAppointment), request.DockAppointmentId.Value);
            }

            if (appointment.FacilityId != request.FacilityId)
            {
                throw new ValidationException(nameof(request.FacilityId), "Appointment facility does not match vehicle check-in facility.");
            }

            if (appointment.Status != DockAppointmentStatus.Scheduled)
            {
                throw new ValidationException(nameof(request.DockAppointmentId), $"Appointment is in '{appointment.Status}' status. Only Scheduled appointments can be checked in.");
            }

            appointment.MarkCheckedIn();
            appointmentNumber = appointment.AppointmentNumber;
            targetClientId = appointment.ClientId;
            targetAsnId ??= appointment.AsnId;
        }

        if (targetAsnId.HasValue)
        {
            var asn = await _context.AdvanceShippingNotices
                .IgnoreQueryFilters()
                .Include(a => a.Lines)
                .FirstOrDefaultAsync(a => a.Id == targetAsnId.Value, cancellationToken);

            if (asn == null)
            {
                throw new EntityNotFoundException(nameof(AdvanceShippingNotice), targetAsnId.Value);
            }

            if (asn.FacilityId != request.FacilityId)
            {
                throw new ValidationException(nameof(request.FacilityId), "ASN facility does not match vehicle check-in facility.");
            }

            targetClientId ??= asn.ClientId;
            asnNumber = asn.AsnNumber;

            if (asn.Status == AsnStatus.Draft || asn.Status == AsnStatus.InTransit)
            {
                asn.MarkArrived();
            }
        }

        var visitCount = await _context.YardVisits
            .CountAsync(y => y.FacilityId == request.FacilityId, cancellationToken);

        var datePrefix = DateTime.UtcNow.ToString("yyyyMMdd");
        var visitNumber = $"YV-{facility.FacilityCode}-{datePrefix}-{(visitCount + 1):D4}";

        var yardVisit = new YardVisit
        {
            FacilityId = request.FacilityId,
            ClientId = targetClientId,
            AsnId = targetAsnId,
            DockAppointmentId = request.DockAppointmentId,
            VisitNumber = visitNumber,
            CarrierName = request.CarrierName.Trim(),
            TrailerNumber = normalizedTrailer,
            LicensePlate = request.LicensePlate?.Trim(),
            DriverName = request.DriverName?.Trim(),
            DriverPhone = request.DriverPhone?.Trim(),
            ParkingLocation = request.ParkingLocation?.Trim(),
            Notes = request.Notes,
            CheckInTime = DateTime.UtcNow,
            Status = YardVisitStatus.CheckedIn
        };

        _context.YardVisits.Add(yardVisit);
        await _context.SaveChangesAsync(cancellationToken);

        return new YardVisitDto
        {
            Id = yardVisit.Id,
            FacilityId = yardVisit.FacilityId,
            FacilityCode = facility.FacilityCode,
            ClientId = yardVisit.ClientId,
            AsnId = yardVisit.AsnId,
            AsnNumber = asnNumber,
            DockAppointmentId = yardVisit.DockAppointmentId,
            AppointmentNumber = appointmentNumber,
            VisitNumber = yardVisit.VisitNumber,
            CarrierName = yardVisit.CarrierName,
            TrailerNumber = yardVisit.TrailerNumber,
            LicensePlate = yardVisit.LicensePlate,
            DriverName = yardVisit.DriverName,
            DriverPhone = yardVisit.DriverPhone,
            CheckInTime = yardVisit.CheckInTime,
            DepartureTime = yardVisit.DepartureTime,
            Status = yardVisit.Status,
            ParkingLocation = yardVisit.ParkingLocation,
            Notes = yardVisit.Notes,
            CurrentDockDoorNumber = null,
            Version = yardVisit.Version,
            CreatedAt = yardVisit.CreatedAt
        };
    }
}
