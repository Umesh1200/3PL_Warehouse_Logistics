using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.WarehouseMaster;

public sealed record UpsertLocationStorageProfileCommand : IRequest<LocationStorageProfileDto>
{
    public Guid LocationId { get; init; }
    public Guid? DedicatedClientId { get; init; }
    public int? MaxHandlingUnits { get; init; }
    public bool AllowMixedSku { get; init; } = true;
    public bool AllowMixedLot { get; init; } = true;
    public bool IsPutawayAllowed { get; init; } = true;
}

public sealed class UpsertLocationStorageProfileCommandValidator : AbstractValidator<UpsertLocationStorageProfileCommand>
{
    public UpsertLocationStorageProfileCommandValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.MaxHandlingUnits).GreaterThan(0).When(x => x.MaxHandlingUnits.HasValue);
    }
}

public sealed class UpsertLocationStorageProfileCommandHandler
    : IRequestHandler<UpsertLocationStorageProfileCommand, LocationStorageProfileDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpsertLocationStorageProfileCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LocationStorageProfileDto> Handle(UpsertLocationStorageProfileCommand request, CancellationToken cancellationToken)
    {
        var location = await _db.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LocationId && !l.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            location.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsManage,
            Permissions.Facilities.Manage);

        if (request.DedicatedClientId.HasValue)
        {
            await _auth.ValidateClientFacilityBindingAsync(facilityId, request.DedicatedClientId.Value, cancellationToken);
        }

        var profile = await _db.LocationStorageProfiles
            .FirstOrDefaultAsync(p => p.Id == location.Id, cancellationToken);

        if (profile == null)
        {
            profile = new LocationStorageProfile
            {
                LocationId = location.Id,
                FacilityId = facilityId,
                DedicatedClientId = request.DedicatedClientId,
                MaxHandlingUnits = request.MaxHandlingUnits,
                AllowMixedSku = request.AllowMixedSku,
                AllowMixedLot = request.AllowMixedLot,
                IsPutawayAllowed = request.IsPutawayAllowed,
                ConfigRevision = 1,
                CreatedBy = userId
            };
            _db.LocationStorageProfiles.Add(profile);
        }
        else
        {
            profile.DedicatedClientId = request.DedicatedClientId;
            profile.MaxHandlingUnits = request.MaxHandlingUnits;
            profile.AllowMixedSku = request.AllowMixedSku;
            profile.AllowMixedLot = request.AllowMixedLot;
            profile.IsPutawayAllowed = request.IsPutawayAllowed;
            profile.ConfigRevision++;
            profile.UpdatedBy = userId;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(profile);
    }
}

public sealed record UpsertLocationOperationalProfileCommand : IRequest<LocationOperationalProfileDto>
{
    public Guid LocationId { get; init; }
    public WarehouseStationRole StationRole { get; init; }
    public bool SupportsScale { get; init; }
    public bool SupportsLabelPrinter { get; init; }
    public bool SupportsConveyor { get; init; }
    public int MaxConcurrentSessions { get; init; } = 1;
    public Guid? DefaultStagingLocationId { get; init; }
}

public sealed class UpsertLocationOperationalProfileCommandValidator : AbstractValidator<UpsertLocationOperationalProfileCommand>
{
    public UpsertLocationOperationalProfileCommandValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.StationRole).IsInEnum();
        RuleFor(x => x.MaxConcurrentSessions).GreaterThanOrEqualTo(1);
    }
}

public sealed class UpsertLocationOperationalProfileCommandHandler
    : IRequestHandler<UpsertLocationOperationalProfileCommand, LocationOperationalProfileDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpsertLocationOperationalProfileCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LocationOperationalProfileDto> Handle(UpsertLocationOperationalProfileCommand request, CancellationToken cancellationToken)
    {
        var location = await _db.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LocationId && !l.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            location.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsManage,
            Permissions.Facilities.Manage);

        if (request.DefaultStagingLocationId.HasValue)
        {
            var stagingValid = await _db.Locations
                .AsNoTracking()
                .AnyAsync(l => l.Id == request.DefaultStagingLocationId.Value &&
                               l.FacilityId == facilityId &&
                               !l.IsDeleted,
                    cancellationToken);
            if (!stagingValid)
            {
                throw new ValidationException(
                    nameof(request.DefaultStagingLocationId),
                    $"Default staging location '{request.DefaultStagingLocationId.Value}' does not belong to facility '{facilityId}'.");
            }
        }

        var profile = await _db.LocationOperationalProfiles
            .FirstOrDefaultAsync(p => p.Id == location.Id, cancellationToken);

        if (profile == null)
        {
            profile = new LocationOperationalProfile
            {
                LocationId = location.Id,
                FacilityId = facilityId,
                StationRole = request.StationRole,
                SupportsScale = request.SupportsScale,
                SupportsLabelPrinter = request.SupportsLabelPrinter,
                SupportsConveyor = request.SupportsConveyor,
                MaxConcurrentSessions = request.MaxConcurrentSessions,
                DefaultStagingLocationId = request.DefaultStagingLocationId,
                ConfigRevision = 1,
                CreatedBy = userId
            };
            _db.LocationOperationalProfiles.Add(profile);
        }
        else
        {
            profile.StationRole = request.StationRole;
            profile.SupportsScale = request.SupportsScale;
            profile.SupportsLabelPrinter = request.SupportsLabelPrinter;
            profile.SupportsConveyor = request.SupportsConveyor;
            profile.MaxConcurrentSessions = request.MaxConcurrentSessions;
            profile.DefaultStagingLocationId = request.DefaultStagingLocationId;
            profile.ConfigRevision++;
            profile.UpdatedBy = userId;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(profile);
    }
}

public sealed record AddLocationEquipmentRequirementCommand(
    Guid LocationId,
    WarehouseEquipmentType EquipmentType,
    bool IsMandatory = true) : IRequest<LocationEquipmentRequirementDto>;

public sealed class AddLocationEquipmentRequirementCommandHandler
    : IRequestHandler<AddLocationEquipmentRequirementCommand, LocationEquipmentRequirementDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public AddLocationEquipmentRequirementCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LocationEquipmentRequirementDto> Handle(AddLocationEquipmentRequirementCommand request, CancellationToken cancellationToken)
    {
        var location = await _db.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LocationId && !l.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            location.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsManage,
            Permissions.Facilities.Manage);

        var existing = await _db.LocationEquipmentRequirements
            .FirstOrDefaultAsync(r => r.LocationId == location.Id && r.EquipmentType == request.EquipmentType, cancellationToken);

        if (existing != null)
        {
            existing.IsMandatory = request.IsMandatory;
            existing.ConfigRevision++;
            existing.UpdatedBy = userId;
            await _db.SaveChangesAsync(cancellationToken);
            return WarehouseMasterMappers.ToDto(existing);
        }

        var req = new LocationEquipmentRequirement
        {
            LocationId = location.Id,
            FacilityId = facilityId,
            EquipmentType = request.EquipmentType,
            IsMandatory = request.IsMandatory,
            ConfigRevision = 1,
            CreatedBy = userId
        };

        _db.LocationEquipmentRequirements.Add(req);
        await _db.SaveChangesAsync(cancellationToken);
        return WarehouseMasterMappers.ToDto(req);
    }
}

public sealed record RemoveLocationEquipmentRequirementCommand(Guid LocationId, Guid RequirementId) : IRequest<bool>;

public sealed class RemoveLocationEquipmentRequirementCommandHandler
    : IRequestHandler<RemoveLocationEquipmentRequirementCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public RemoveLocationEquipmentRequirementCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<bool> Handle(RemoveLocationEquipmentRequirementCommand request, CancellationToken cancellationToken)
    {
        var req = await _db.LocationEquipmentRequirements
            .FirstOrDefaultAsync(r => r.Id == request.RequirementId && r.LocationId == request.LocationId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(LocationEquipmentRequirement), request.RequirementId);

        await _auth.ResolveAuthorizedFacilityAsync(
            req.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsManage,
            Permissions.Facilities.Manage);

        _db.LocationEquipmentRequirements.Remove(req);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
