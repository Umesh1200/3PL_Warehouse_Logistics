using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.AssignDockDoor;

public record AssignDockDoorCommand : IRequest<DockAssignmentDto>
{
    public Guid YardVisitId { get; init; }
    public Guid DockDoorId { get; init; }
    public string? Notes { get; init; }
}

public class AssignDockDoorCommandValidator : AbstractValidator<AssignDockDoorCommand>
{
    public AssignDockDoorCommandValidator()
    {
        RuleFor(x => x.YardVisitId)
            .NotEmpty().WithMessage("Yard visit ID is required.");

        RuleFor(x => x.DockDoorId)
            .NotEmpty().WithMessage("Dock door ID is required.");
    }
}

public class AssignDockDoorCommandHandler : IRequestHandler<AssignDockDoorCommand, DockAssignmentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;

    public AssignDockDoorCommandHandler(IApplicationDbContext context, ICurrentUserContext currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<DockAssignmentDto> Handle(AssignDockDoorCommand request, CancellationToken cancellationToken)
    {
        var visit = await _context.YardVisits
            .Include(y => y.Facility)
            .Include(y => y.AdvanceShippingNotice)
                .ThenInclude(asn => asn!.Lines)
                    .ThenInclude(l => l.Item)
            .FirstOrDefaultAsync(y => y.Id == request.YardVisitId, cancellationToken);

        if (visit == null)
        {
            throw new EntityNotFoundException(nameof(YardVisit), request.YardVisitId);
        }

        var dockDoor = await _context.DockDoors
            .FirstOrDefaultAsync(d => d.Id == request.DockDoorId && d.FacilityId == visit.FacilityId, cancellationToken);

        if (dockDoor == null)
        {
            throw new EntityNotFoundException(nameof(DockDoor), request.DockDoorId);
        }

        if (!dockDoor.IsOperational)
        {
            throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{dockDoor.DoorNumber}' is not operational (Status: {dockDoor.Status}).");
        }

        // Thermal check
        if (visit.AdvanceShippingNotice != null)
        {
            var requiresColdStorage = visit.AdvanceShippingNotice.Lines.Any(l =>
                l.Item.StorageTempClass == StorageTempClass.Refrigerated ||
                l.Item.StorageTempClass == StorageTempClass.Frozen);

            if (requiresColdStorage && !dockDoor.SupportsRefrigerated)
            {
                throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{dockDoor.DoorNumber}' does not support temperature-controlled / refrigerated shipments.");
            }
        }

        // Occupancy check
        var doorOccupied = await _context.DockAssignments
            .AnyAsync(a =>
                a.DockDoorId == request.DockDoorId &&
                (a.Status == DockAssignmentStatus.Assigned || a.Status == DockAssignmentStatus.AtDock),
                cancellationToken);

        if (doorOccupied)
        {
            throw new ValidationException(nameof(request.DockDoorId), $"Dock door '{dockDoor.DoorNumber}' is currently occupied or assigned to another shipment.");
        }

        // Transition visit state
        visit.AssignToDock();

        var assignment = new DockAssignment
        {
            FacilityId = visit.FacilityId,
            DockDoorId = request.DockDoorId,
            YardVisitId = visit.Id,
            AsnId = visit.AsnId,
            AssignedAt = DateTime.UtcNow,
            Status = DockAssignmentStatus.Assigned,
            AssignedByUserId = _currentUser.UserId,
            Notes = request.Notes
        };

        _context.DockAssignments.Add(assignment);
        await _context.SaveChangesAsync(cancellationToken);

        return new DockAssignmentDto
        {
            Id = assignment.Id,
            FacilityId = assignment.FacilityId,
            DockDoorId = assignment.DockDoorId,
            DoorNumber = dockDoor.DoorNumber,
            YardVisitId = assignment.YardVisitId,
            VisitNumber = visit.VisitNumber,
            TrailerNumber = visit.TrailerNumber,
            AsnId = assignment.AsnId,
            AsnNumber = visit.AdvanceShippingNotice?.AsnNumber,
            AssignedAt = assignment.AssignedAt,
            ArrivedAtDock = assignment.ArrivedAtDock,
            ReleasedAt = assignment.ReleasedAt,
            Status = assignment.Status,
            Notes = assignment.Notes,
            Version = assignment.Version
        };
    }
}

