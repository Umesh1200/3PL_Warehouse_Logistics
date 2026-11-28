using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.CreateDockDoor;

public record CreateDockDoorCommand : IRequest<DockDoorDto>
{
    public Guid FacilityId { get; init; }
    public Guid? LocationId { get; init; }
    public string DoorNumber { get; init; } = string.Empty;
    public DockDoorType DoorType { get; init; } = DockDoorType.Combination;
    public DockDoorStatus Status { get; init; } = DockDoorStatus.Active;
    public bool SupportsRefrigerated { get; init; } = false;
    public decimal? MaxTrailerLengthCm { get; init; }
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

        RuleFor(x => x.MaxTrailerLengthCm)
            .GreaterThan(0).When(x => x.MaxTrailerLengthCm.HasValue)
            .WithMessage("Maximum trailer length (cm) must be positive.");

        RuleFor(x => x.MaxTrailerLengthFeet)
            .GreaterThan(0).When(x => x.MaxTrailerLengthFeet.HasValue)
            .WithMessage("Maximum trailer length (feet) must be positive.");
    }
}

public class CreateDockDoorCommandHandler : IRequestHandler<CreateDockDoorCommand, DockDoorDto>
{
    private readonly IApplicationDbContext _context;
    private readonly WarehouseMasterAuthorization _auth;

    public CreateDockDoorCommandHandler(IApplicationDbContext context, WarehouseMasterAuthorization auth)
    {
        _context = context;
        _auth = auth;
    }

    public async Task<DockDoorDto> Handle(CreateDockDoorCommand request, CancellationToken cancellationToken)
    {
        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.DockDoorsManage,
            Permissions.Facilities.Manage);

        var facility = await _context.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == facilityId && !f.IsDeleted, cancellationToken);

        if (facility == null)
        {
            throw new EntityNotFoundException(nameof(Facility), facilityId);
        }

        if (request.LocationId.HasValue)
        {
            var loc = await _context.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == request.LocationId.Value && l.FacilityId == facilityId && !l.IsDeleted, cancellationToken);
            if (loc == null)
            {
                throw new ValidationException(
                    nameof(request.LocationId),
                    $"Physical location '{request.LocationId.Value}' does not belong to facility '{facilityId}'.");
            }
            if (loc.Status != LocationStatus.Active || !loc.IsActive)
            {
                throw new ValidationException(
                    nameof(request.LocationId),
                    $"Physical location '{loc.LocationCode}' must be active.");
            }
            if (loc.LocationType != LocationType.DockDoor)
            {
                throw new ValidationException(
                    nameof(request.LocationId),
                    $"Physical location '{loc.LocationCode}' must have LocationType 'DockDoor'.");
            }
        }

        var normalizedDoorNumber = DockDoor.NormalizeDoorNo(request.DoorNumber);

        var doorExists = await _context.DockDoors
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(d => d.FacilityId == facilityId && d.NormalizedDoorNumber == normalizedDoorNumber, cancellationToken);

        if (doorExists)
        {
            throw new ValidationException(nameof(request.DoorNumber), $"Dock door '{normalizedDoorNumber}' already exists for facility '{facility.FacilityCode}'.");
        }

        var dockDoor = new DockDoor
        {
            FacilityId = facilityId,
            LocationId = request.LocationId,
            DoorNumber = normalizedDoorNumber,
            NormalizedDoorNumber = normalizedDoorNumber,
            DoorType = request.DoorType,
            Status = request.Status,
            SupportsRefrigerated = request.SupportsRefrigerated,
            Notes = request.Notes?.Trim(),
            ConfigRevision = 1,
            CreatedBy = userId
        };
        dockDoor.SetMaxTrailerLength(request.MaxTrailerLengthCm, request.MaxTrailerLengthFeet);

        _context.DockDoors.Add(dockDoor);
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
