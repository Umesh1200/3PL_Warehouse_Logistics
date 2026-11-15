using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.ReleaseDockDoor;

public record ReleaseDockDoorCommand : IRequest<DockAssignmentDto>
{
    public Guid DockAssignmentId { get; init; }
    public string? Notes { get; init; }
}

public class ReleaseDockDoorCommandValidator : AbstractValidator<ReleaseDockDoorCommand>
{
    public ReleaseDockDoorCommandValidator()
    {
        RuleFor(x => x.DockAssignmentId)
            .NotEmpty().WithMessage("Dock assignment ID is required.");
    }
}

public class ReleaseDockDoorCommandHandler : IRequestHandler<ReleaseDockDoorCommand, DockAssignmentDto>
{
    private readonly IApplicationDbContext _context;

    public ReleaseDockDoorCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DockAssignmentDto> Handle(ReleaseDockDoorCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _context.DockAssignments
            .Include(a => a.YardVisit)
            .Include(a => a.DockDoor)
            .Include(a => a.AdvanceShippingNotice)
            .FirstOrDefaultAsync(a => a.Id == request.DockAssignmentId, cancellationToken);

        if (assignment == null)
        {
            throw new EntityNotFoundException(nameof(DockAssignment), request.DockAssignmentId);
        }

        assignment.ReleaseDock(request.Notes);
        assignment.YardVisit.ReleaseFromDock();

        await _context.SaveChangesAsync(cancellationToken);

        return new DockAssignmentDto
        {
            Id = assignment.Id,
            FacilityId = assignment.FacilityId,
            DockDoorId = assignment.DockDoorId,
            DoorNumber = assignment.DockDoor.DoorNumber,
            YardVisitId = assignment.YardVisitId,
            VisitNumber = assignment.YardVisit.VisitNumber,
            TrailerNumber = assignment.YardVisit.TrailerNumber,
            AsnId = assignment.AsnId,
            AsnNumber = assignment.AdvanceShippingNotice?.AsnNumber,
            AssignedAt = assignment.AssignedAt,
            ArrivedAtDock = assignment.ArrivedAtDock,
            ReleasedAt = assignment.ReleasedAt,
            Status = assignment.Status,
            Notes = assignment.Notes,
            Version = assignment.Version
        };
    }
}

