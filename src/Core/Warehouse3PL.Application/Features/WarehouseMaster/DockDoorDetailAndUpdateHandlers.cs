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
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.WarehouseMaster;

public sealed record GetDockDoorByIdQuery(Guid Id) : IRequest<DockDoorDto>;

public sealed class GetDockDoorByIdQueryHandler : IRequestHandler<GetDockDoorByIdQuery, DockDoorDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetDockDoorByIdQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<DockDoorDto> Handle(GetDockDoorByIdQuery request, CancellationToken cancellationToken)
    {
        var dockDoor = await _db.DockDoors
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Id && !d.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(DockDoor), request.Id);

        await _auth.ResolveAuthorizedFacilityReadAsync(
            dockDoor.FacilityId,
            cancellationToken,
            Permissions.Facilities.DockDoorsView,
            Permissions.Facilities.View);

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

public sealed record UpdateDockDoorCommand : IRequest<DockDoorDto>
{
    public Guid Id { get; init; }
    public Guid? LocationId { get; init; }
    public string? DoorNumber { get; init; }
    public DockDoorType DoorType { get; init; }
    public bool SupportsRefrigerated { get; init; }
    public decimal? MaxTrailerLengthCm { get; init; }
    public int? MaxTrailerLengthFeet { get; init; }
    public string? Notes { get; init; }
}

public sealed class UpdateDockDoorCommandValidator : AbstractValidator<UpdateDockDoorCommand>
{
    public UpdateDockDoorCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DoorType).IsInEnum();
        RuleFor(x => x.MaxTrailerLengthCm).GreaterThan(0).When(x => x.MaxTrailerLengthCm.HasValue);
        RuleFor(x => x.MaxTrailerLengthFeet).GreaterThan(0).When(x => x.MaxTrailerLengthFeet.HasValue);
    }
}

public sealed class UpdateDockDoorCommandHandler : IRequestHandler<UpdateDockDoorCommand, DockDoorDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpdateDockDoorCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<DockDoorDto> Handle(UpdateDockDoorCommand request, CancellationToken cancellationToken)
    {
        var dockDoor = await _db.DockDoors
            .FirstOrDefaultAsync(d => d.Id == request.Id && !d.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(DockDoor), request.Id);

        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            dockDoor.FacilityId,
            cancellationToken,
            Permissions.Facilities.DockDoorsManage,
            Permissions.Facilities.Manage);

        if (!string.IsNullOrWhiteSpace(request.DoorNumber) &&
            !string.Equals(DockDoor.NormalizeDoorNo(request.DoorNumber), dockDoor.NormalizedDoorNumber, StringComparison.Ordinal))
        {
            throw new DomainException("DOCK_DOOR_IMMUTABLE_NUMBER", $"Dock door number '{dockDoor.DoorNumber}' is immutable.");
        }

        if (request.LocationId.HasValue)
        {
            var loc = await _db.Locations
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

        dockDoor.LocationId = request.LocationId;
        dockDoor.DoorType = request.DoorType;
        dockDoor.SupportsRefrigerated = request.SupportsRefrigerated;
        dockDoor.SetMaxTrailerLength(request.MaxTrailerLengthCm, request.MaxTrailerLengthFeet);
        dockDoor.Notes = request.Notes?.Trim();
        dockDoor.ConfigRevision++;
        dockDoor.UpdatedBy = userId;

        await _db.SaveChangesAsync(cancellationToken);

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
