using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
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

    public UpdateDockDoorStatusCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DockDoorDto> Handle(UpdateDockDoorStatusCommand request, CancellationToken cancellationToken)
    {
        var dockDoor = await _context.DockDoors
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);

        if (dockDoor == null)
        {
            throw new EntityNotFoundException(nameof(DockDoor), request.Id);
        }

        dockDoor.Status = request.Status;
        if (request.Notes != null)
        {
            dockDoor.Notes = request.Notes;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new DockDoorDto
        {
            Id = dockDoor.Id,
            FacilityId = dockDoor.FacilityId,
            DoorNumber = dockDoor.DoorNumber,
            DoorType = dockDoor.DoorType,
            Status = dockDoor.Status,
            SupportsRefrigerated = dockDoor.SupportsRefrigerated,
            MaxTrailerLengthFeet = dockDoor.MaxTrailerLengthFeet,
            Notes = dockDoor.Notes,
            IsOperational = dockDoor.IsOperational,
            CreatedAt = dockDoor.CreatedAt
        };
    }
}

