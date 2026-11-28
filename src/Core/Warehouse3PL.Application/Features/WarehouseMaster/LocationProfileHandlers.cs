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

/// <summary>
/// MDM21-R03 & R04: Runs under LocationLifecycle lock and rejects storage-profile restrictions
/// that contradict existing stock ownership, mixed SKU/lot inventory, current HU count, or zone dedication.
/// </summary>
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
        var preRead = await _db.Locations
            .AsNoTracking()
            .Where(l => l.Id == request.LocationId && !l.IsDeleted)
            .Select(l => new { l.FacilityId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            preRead.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsManage,
            Permissions.Facilities.Manage);

        if (request.DedicatedClientId.HasValue)
        {
            await _auth.ValidateClientFacilityBindingAsync(facilityId, request.DedicatedClientId.Value, cancellationToken);
        }

        return await _db.ExecuteWithLocationLifecycleLockAsync(facilityId, request.LocationId, async () =>
        {
            var location = await _db.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.FacilityId == facilityId && !l.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

            // MDM21-R03: Zone & location dedication compatibility (explicit lookup avoids composite-FK navigation quirks)
            if (request.DedicatedClientId.HasValue && location.ZoneId.HasValue)
            {
                var zone = await _db.WarehouseZones
                    .AsNoTracking()
                    .FirstOrDefaultAsync(z => z.Id == location.ZoneId.Value && z.FacilityId == facilityId && !z.IsDeleted, cancellationToken);
                if (zone?.DedicatedClientId is Guid zoneClientId &&
                    zoneClientId != request.DedicatedClientId.Value)
                {
                    throw new DomainException(
                        $"Location '{location.LocationCode}' cannot be dedicated to client '{request.DedicatedClientId.Value}' because its zone '{zone.ZoneCode}' is dedicated to client '{zoneClientId}'.",
                        "ZONE_LOCATION_DEDICATION_CONFLICT");
                }
            }

            // MDM21-R03: Reject profile changes contradicting existing stock (all 6 quantity buckets)
            var existingStocks = await _db.StockOnHand
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.FacilityId == facilityId && s.LocationId == location.Id)
                .Where(WarehouseInventoryDependencyGuard.HasAnyStockExpression)
                .Select(s => new { s.ClientId, s.ItemId, s.LotId })
                .ToListAsync(cancellationToken);

            if (existingStocks.Count > 0)
            {
                if (request.DedicatedClientId.HasValue &&
                    existingStocks.Any(s => s.ClientId != request.DedicatedClientId.Value))
                {
                    throw new DomainException(
                        $"Cannot dedicate location '{location.LocationCode}' to client '{request.DedicatedClientId.Value}' while stock belonging to another client is present.",
                        "STORAGE_PROFILE_STOCK_CONFLICT");
                }

                if (!request.AllowMixedSku && existingStocks.Select(s => s.ItemId).Distinct().Count() > 1)
                {
                    throw new DomainException(
                        $"Cannot disallow mixed SKUs on location '{location.LocationCode}' while multiple SKUs currently reside in it.",
                        "STORAGE_PROFILE_STOCK_CONFLICT");
                }

                if (!request.AllowMixedLot && existingStocks.Select(s => s.LotId).Distinct().Count() > 1)
                {
                    throw new DomainException(
                        $"Cannot disallow mixed lots on location '{location.LocationCode}' while multiple lots currently reside in it.",
                        "STORAGE_PROFILE_STOCK_CONFLICT");
                }
            }

            if (request.MaxHandlingUnits.HasValue)
            {
                var presentHuCount = await _db.HandlingUnits
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .CountAsync(hu => hu.FacilityId == facilityId &&
                                      hu.CurrentLocationId == location.Id &&
                                      !hu.IsDeleted,
                        cancellationToken);
                if (presentHuCount > request.MaxHandlingUnits.Value)
                {
                    throw new DomainException(
                        $"Cannot set MaxHandlingUnits to {request.MaxHandlingUnits.Value} on location '{location.LocationCode}' while {presentHuCount} handling unit(s) are physically present.",
                        "STORAGE_PROFILE_CAPACITY_CONFLICT");
                }
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
        }, cancellationToken);
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
        RuleFor(x => x.MaxConcurrentSessions).Equal(1)
            .WithMessage("Enforced concurrent session limits (>1) are deferred; MaxConcurrentSessions must remain 1 (descriptive metadata only).");
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

        // MDM21-R03: Reject unsupported session limit enforcement
        if (request.MaxConcurrentSessions != 1)
        {
            throw new ValidationException(
                nameof(request.MaxConcurrentSessions),
                "Enforced concurrent session limits are deferred; MaxConcurrentSessions must remain 1 (descriptive metadata only).");
        }

        // MDM21-R06: DefaultStagingLocationId must be active, same-facility, and a valid staging/receiving location
        if (request.DefaultStagingLocationId.HasValue)
        {
            var staging = await _db.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == request.DefaultStagingLocationId.Value &&
                                          l.FacilityId == facilityId &&
                                          !l.IsDeleted,
                    cancellationToken);
            if (staging == null)
            {
                throw new ValidationException(
                    nameof(request.DefaultStagingLocationId),
                    $"Default staging location '{request.DefaultStagingLocationId.Value}' does not belong to facility '{facilityId}'.");
            }

            if (staging.Status != LocationStatus.Active || !staging.IsActive)
            {
                throw new ValidationException(
                    nameof(request.DefaultStagingLocationId),
                    $"Default staging location '{staging.LocationCode}' must be active.");
            }

            if (staging.LocationType != LocationType.StagingLane && !staging.IsReceiving)
            {
                throw new ValidationException(
                    nameof(request.DefaultStagingLocationId),
                    $"Default staging location '{staging.LocationCode}' must be a StagingLane or receiving-capable location.");
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
                MaxConcurrentSessions = 1,
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
            profile.MaxConcurrentSessions = 1;
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
    bool IsMandatory = false) : IRequest<LocationEquipmentRequirementDto>;

/// <summary>
/// MDM21-R03: Mandatory equipment enforcement is not yet consumed by operational task dispatch;
/// only advisory equipment capability metadata (IsMandatory = false) is permitted until MHE dispatch integration.
/// </summary>
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
        if (request.IsMandatory)
        {
            throw new ValidationException(
                nameof(request.IsMandatory),
                "Mandatory equipment enforcement is deferred until MHE operational dispatch integration; record advisory metadata with IsMandatory = false.");
        }

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
            existing.IsMandatory = false;
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
            IsMandatory = false,
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
