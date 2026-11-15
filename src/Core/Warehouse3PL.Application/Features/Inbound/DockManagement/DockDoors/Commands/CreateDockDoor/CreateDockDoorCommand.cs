using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.CreateDockDoor;

public record CreateDockDoorCommand : IRequest<DockDoorDto>
{
    public Guid FacilityId { get; init; }
    public string DoorNumber { get; init; } = string.Empty;
    public DockDoorType DoorType { get; init; } = DockDoorType.Combination;
    public DockDoorStatus Status { get; init; } = DockDoorStatus.Active;
    public bool SupportsRefrigerated { get; init; } = false;
    public int? MaxTrailerLengthFeet { get; init; }
    public string? Notes { get; init; }
}

public class CreateDockDoorCommandValidator : AbstractValidator<CreateDockDoorCommand>
{
    public CreateDockDoorCommandValidator()
    {
        RuleFor(x => x.FacilityId)
            .NotEmpty().WithMessage("Facility ID is required.");

        RuleFor(x => x.DoorNumber)
            .NotEmpty().WithMessage("Door number is required.")
            .MaximumLength(50).WithMessage("Door number must not exceed 50 characters.");

        RuleFor(x => x.MaxTrailerLengthFeet)
            .GreaterThan(0).When(x => x.MaxTrailerLengthFeet.HasValue)
            .WithMessage("Maximum trailer length must be positive.");
    }
}

public class CreateDockDoorCommandHandler : IRequestHandler<CreateDockDoorCommand, DockDoorDto>
{
    private readonly IApplicationDbContext _context;

    public CreateDockDoorCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DockDoorDto> Handle(CreateDockDoorCommand request, CancellationToken cancellationToken)
    {
        var facility = await _context.Facilities
            .FirstOrDefaultAsync(f => f.Id == request.FacilityId, cancellationToken);

        if (facility == null)
        {
            throw new EntityNotFoundException(nameof(Facility), request.FacilityId);
        }

        var normalizedDoorNumber = request.DoorNumber.Trim().ToUpperInvariant();

        var doorExists = await _context.DockDoors
            .AnyAsync(d => d.FacilityId == request.FacilityId && d.DoorNumber == normalizedDoorNumber, cancellationToken);

        if (doorExists)
        {
            throw new ValidationException(nameof(request.DoorNumber), $"Dock door '{normalizedDoorNumber}' already exists for facility '{facility.FacilityCode}'.");
        }

        var dockDoor = new DockDoor
        {
            FacilityId = request.FacilityId,
            DoorNumber = normalizedDoorNumber,
            DoorType = request.DoorType,
            Status = request.Status,
            SupportsRefrigerated = request.SupportsRefrigerated,
            MaxTrailerLengthFeet = request.MaxTrailerLengthFeet,
            Notes = request.Notes?.Trim()
        };

        _context.DockDoors.Add(dockDoor);
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

