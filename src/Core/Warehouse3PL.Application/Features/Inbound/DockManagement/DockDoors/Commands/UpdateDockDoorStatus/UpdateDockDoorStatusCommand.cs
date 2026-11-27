using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.UpdateDockDoorStatus;

public record UpdateDockDoorStatusCommand : IRequest<DockDoorDto>
{
    public Guid Id { get; init; }
    public DockDoorStatus Status { get; init; }
    public string? Notes { get; init; }
}

public class UpdateDockDoorStatusCommandValidator : AbstractValidator<UpdateDockDoorStatusCommand>
{
    public UpdateDockDoorStatusCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Dock door ID is required.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Valid dock door status is required.");
    }
}

public class UpdateDockDoorStatusCommandHandler : IRequestHandler<UpdateDockDoorStatusCommand, DockDoorDto>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseMasterAuthorization _auth;

    public UpdateDockDoorStatusCommandHandler(IApplicationDbContext context, WarehouseMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<DockDoorDto> Handle(UpdateDockDoorStatusCommand request, CancellationToken cancellationToken)
    {
        var dockDoor = await _context.DockDoors
            .FirstOrDefaultAsync(d => d.Id == request.Id && !d.IsDeleted, cancellationToken);

        if (dockDoor == null)
        {
            throw new EntityNotFoundException(nameof(DockDoor), request.Id);
        }

        var (userId, _, _) = await _auth.ResolveAuthorizedFacilityAsync(
            dockDoor.FacilityId,
            cancellationToken,
            Permissions.Facilities.DockDoorsManage,
            Permissions.Facilities.Manage);

        // Guard deactivation/maintenance against active dock assignments or in-progress appointments
        if (request.Status != DockDoorStatus.Active)
        {
            var hasActiveAssignments = await _context.DockAssignments
                .AsNoTracking()
                .AnyAsync(a => a.DockDoorId == dockDoor.Id &&
                               !a.IsDeleted &&
                               !a.ReleasedAt.HasValue &&
                               (a.Status == DockAssignmentStatus.Assigned || a.Status == DockAssignmentStatus.AtDock),
                    cancellationToken);
            if (hasActiveAssignments)
            {
                throw new DomainException(
                    "DOCK_DOOR_STATUS_BLOCKED_ASSIGNMENT",
                    $"Cannot set dock door '{dockDoor.DoorNumber}' to '{request.Status}' while an active trailer assignment is in progress.");
            }

            var hasActiveAppointments = await _context.DockAppointments
                .AsNoTracking()
                .AnyAsync(a => a.DockDoorId == dockDoor.Id &&
                               !a.IsDeleted &&
                               (a.Status == DockAppointmentStatus.Scheduled ||
                                a.Status == DockAppointmentStatus.CheckedIn),
                    cancellationToken);
            if (hasActiveAppointments)
            {
                throw new DomainException(
                    "DOCK_DOOR_STATUS_BLOCKED_APPOINTMENT",
                    $"Cannot set dock door '{dockDoor.DoorNumber}' to '{request.Status}' while active or scheduled dock appointments exist.");
            }
        }

        dockDoor.Status = request.Status;
        if (request.Notes != null)
        {
            dockDoor.Notes = request.Notes;
        }
        dockDoor.ConfigRevision++;
        dockDoor.UpdatedBy = userId;

        await _context.SaveChangesAsync(cancellationToken);

        return new DockDoorDto
        {
            Id = dockDoor.Id,
            FacilityId = dockDoor.FacilityId,
            LocationId = dockDoor.LocationId,
            DoorNumber = dockDoor.DoorNumber,
            DoorType = dockDoor.DoorType,
            Status = dockDoor.Status,
            SupportsRefrigerated = dockDoor.SupportsRefrigerated,
            MaxTrailerLengthCm = dockDoor.MaxTrailerLengthCm,
            MaxTrailerLengthFeet = dockDoor.MaxTrailerLengthFeet,
            Notes = dockDoor.Notes,
            ConfigRevision = dockDoor.ConfigRevision,
            IsOperational = dockDoor.IsOperational,
            CreatedAt = dockDoor.CreatedAt
        };
    }
}
