using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.WarehouseMaster;

public sealed record GetLocationsQuery(
    Guid? FacilityId,
    Guid? ZoneId = null,
    Guid? ParentLocationId = null,
    LocationType? LocationType = null,
    LocationStatus? Status = null,
    bool? IsStorable = null,
    bool? IsLocked = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 50) : IRequest<PagedResult<LocationDto>>;

public sealed class GetLocationsQueryHandler : IRequestHandler<GetLocationsQuery, PagedResult<LocationDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetLocationsQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<PagedResult<LocationDto>> Handle(GetLocationsQuery request, CancellationToken cancellationToken)
    {
        var (_, facilityId, _) = await _auth.ResolveAuthorizedFacilityReadAsync(
            request.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsView,
            Permissions.Facilities.View);

        var page = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        var query = _db.Locations
            .AsNoTracking()
            .Where(l => l.FacilityId == facilityId && !l.IsDeleted);

        if (request.ZoneId.HasValue)
            query = query.Where(l => l.ZoneId == request.ZoneId.Value);

        if (request.ParentLocationId.HasValue)
            query = query.Where(l => l.ParentLocationId == request.ParentLocationId.Value);

        if (request.LocationType.HasValue)
            query = query.Where(l => l.LocationType == request.LocationType.Value);

        if (request.Status.HasValue)
            query = query.Where(l => l.Status == request.Status.Value);

        if (request.IsStorable.HasValue)
            query = query.Where(l => l.IsStorable == request.IsStorable.Value);

        if (request.IsLocked.HasValue)
            query = query.Where(l => l.IsLocked == request.IsLocked.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpperInvariant();
            query = query.Where(l =>
                l.NormalizedLocationCode.Contains(term) ||
                l.NormalizedBarcode.Contains(term) ||
                l.LocationName.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(l => l.HierarchyLevel)
            .ThenBy(l => l.LocationCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<LocationDto>(items.Select(WarehouseMasterMappers.ToLocationDto).ToList(), total, page, pageSize);
    }
}

public sealed record GetLocationByIdQuery(Guid Id) : IRequest<LocationDetailDto>;

public sealed class GetLocationByIdQueryHandler : IRequestHandler<GetLocationByIdQuery, LocationDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetLocationByIdQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LocationDetailDto> Handle(GetLocationByIdQuery request, CancellationToken cancellationToken)
    {
        var location = await _db.Locations
            .AsNoTracking()
            .Include(l => l.Zone)
            .Include(l => l.ParentLocation)
            .Include(l => l.StorageProfile)
            .Include(l => l.OperationalProfile)
            .Include(l => l.EquipmentRequirements)
            .Include(l => l.Locks)
            .FirstOrDefaultAsync(l => l.Id == request.Id && !l.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Location), request.Id);

        await _auth.ResolveAuthorizedFacilityReadAsync(
            location.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsView,
            Permissions.Facilities.View);

        return new LocationDetailDto
        {
            Id = location.Id,
            FacilityId = location.FacilityId,
            ZoneId = location.ZoneId,
            ZoneCode = location.Zone?.ZoneCode,
            ParentLocationId = location.ParentLocationId,
            ParentLocationCode = location.ParentLocation?.LocationCode,
            LocationType = location.LocationType,
            LocationCode = location.LocationCode,
            LocationName = location.LocationName,
            Barcode = location.Barcode,
            HierarchyLevel = location.HierarchyLevel,
            HierarchyPath = location.HierarchyPath,
            Remarks = location.Remarks,
            ConfigRevision = location.ConfigRevision,
            AisleCode = location.AisleCode,
            RackCode = location.RackCode,
            ShelfLevel = location.ShelfLevel,
            BinNumber = location.BinNumber,
            PickSequence = location.PickSequence,
            LengthCm = location.LengthCm,
            WidthCm = location.WidthCm,
            HeightCm = location.HeightCm,
            MaxVolumeCm3 = location.MaxVolumeCm3,
            MaxWeightKg = location.MaxWeightKg,
            Status = location.Status,
            IsActive = location.IsActive,
            IsStorable = location.IsStorable,
            IsPickable = location.IsPickable,
            IsReceiving = location.IsReceiving,
            IsDamaged = location.IsDamaged,
            IsLocked = location.IsLocked,
            StorageProfile = location.StorageProfile != null ? WarehouseMasterMappers.ToDto(location.StorageProfile) : null,
            OperationalProfile = location.OperationalProfile != null ? WarehouseMasterMappers.ToDto(location.OperationalProfile) : null,
            EquipmentRequirements = location.EquipmentRequirements.Select(WarehouseMasterMappers.ToDto).ToList(),
            ActiveLockCount = location.Locks.Count(lk => !lk.ReleasedAt.HasValue)
        };
    }
}

public sealed record UpdateLocationCommand : IRequest<LocationDto>
{
    public Guid Id { get; init; }
    public string? LocationCode { get; init; }
    public string? LocationName { get; init; }
    public Guid? ZoneId { get; init; }
    public string? Barcode { get; init; }
    public string? AisleCode { get; init; }
    public string? RackCode { get; init; }
    public int? ShelfLevel { get; init; }
    public int? BinNumber { get; init; }
    public int? PickSequence { get; init; }
    public decimal? LengthCm { get; init; }
    public decimal? WidthCm { get; init; }
    public decimal? HeightCm { get; init; }
    public decimal? MaxVolumeCm3 { get; init; }
    public decimal? MaxWeightKg { get; init; }
    public bool? IsStorable { get; init; }
    public bool? IsPickable { get; init; }
    public bool? IsReceiving { get; init; }
    public bool? IsDamaged { get; init; }
    public string? Remarks { get; init; }
}

public sealed class UpdateLocationCommandValidator : AbstractValidator<UpdateLocationCommand>
{
    public UpdateLocationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.LocationName).MaximumLength(150).When(x => x.LocationName != null);
        RuleFor(x => x.Barcode).NotEmpty().MaximumLength(100).When(x => x.Barcode != null);
        RuleFor(x => x.Remarks).MaximumLength(500).When(x => x.Remarks != null);
    }
}

public sealed class UpdateLocationCommandHandler : IRequestHandler<UpdateLocationCommand, LocationDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpdateLocationCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LocationDto> Handle(UpdateLocationCommand request, CancellationToken cancellationToken)
    {
        var preRead = await _db.Locations
            .AsNoTracking()
            .Where(l => l.Id == request.Id && !l.IsDeleted)
            .Select(l => new { l.FacilityId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Location), request.Id);

        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            preRead.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsManage,
            Permissions.Facilities.Manage);

        // MDM21-R04: Canonical lock order FacilityHierarchy -> LocationLifecycle
        return await _db.ExecuteWithFacilityHierarchyLockAsync(facilityId, async () =>
            await _db.ExecuteWithLocationLifecycleLockAsync(facilityId, request.Id, async () =>
            {
                var location = await _db.Locations
                    .Include(l => l.ParentLocation)
                    .Include(l => l.StorageProfile)
                    .FirstOrDefaultAsync(l => l.Id == request.Id && l.FacilityId == facilityId && !l.IsDeleted, cancellationToken)
                    ?? throw new EntityNotFoundException(nameof(Location), request.Id);

                // A03: LocationCode is immutable by default
                if (!string.IsNullOrWhiteSpace(request.LocationCode) &&
                    !string.Equals(Location.NormalizeCode(request.LocationCode), location.NormalizedLocationCode, StringComparison.Ordinal))
                {
                    throw new DomainException(
                        $"LocationCode '{location.LocationCode}' is immutable and cannot be changed to '{request.LocationCode}'.",
                        "LOCATION_IMMUTABLE_CODE");
                }

                // MDM21-R05: Permanent barcode identity — existing barcodes are immutable by default
                if (!string.IsNullOrWhiteSpace(request.Barcode))
                {
                    var normalizedBarcode = Location.NormalizeCode(request.Barcode);
                    if (!string.Equals(normalizedBarcode, location.NormalizedBarcode, StringComparison.Ordinal))
                    {
                        throw new DomainException(
                            $"Location barcode '{location.Barcode}' is permanently assigned and cannot be replaced with '{request.Barcode.Trim()}'.",
                            "LOCATION_IMMUTABLE_BARCODE");
                    }
                }

                // MDM21-R03 & R06: Active zone check + zone dedication compatibility with existing stock & storage profile
                if (request.ZoneId.HasValue && request.ZoneId != location.ZoneId)
                {
                    var zone = await _db.WarehouseZones
                        .AsNoTracking()
                        .FirstOrDefaultAsync(z => z.Id == request.ZoneId.Value && z.FacilityId == facilityId && !z.IsDeleted, cancellationToken);
                    if (zone == null)
                    {
                        throw new ValidationException(
                            nameof(request.ZoneId),
                            $"Zone '{request.ZoneId.Value}' does not exist in facility '{facilityId}'.");
                    }

                    if (!zone.IsActive)
                    {
                        throw new ValidationException(
                            nameof(request.ZoneId),
                            $"Zone '{zone.ZoneCode}' is inactive and cannot accept new location assignments.");
                    }

                    if (zone.DedicatedClientId.HasValue)
                    {
                        if (location.StorageProfile?.DedicatedClientId is Guid profileClientId &&
                            profileClientId != zone.DedicatedClientId.Value)
                        {
                            throw new DomainException(
                                $"Zone '{zone.ZoneCode}' dedicated client conflicts with location storage profile dedication.",
                                "ZONE_LOCATION_DEDICATION_CONFLICT");
                        }

                        var hasForeignStock = await _db.StockOnHand
                            .IgnoreQueryFilters()
                            .AsNoTracking()
                            .Where(s => s.FacilityId == facilityId && s.LocationId == location.Id && s.ClientId != zone.DedicatedClientId.Value)
                            .AnyAsync(WarehouseInventoryDependencyGuard.HasAnyStockExpression, cancellationToken);
                        if (hasForeignStock)
                        {
                            throw new DomainException(
                                $"Cannot assign location '{location.LocationCode}' to dedicated zone '{zone.ZoneCode}' while stock for another client exists in the location.",
                                "ZONE_DEDICATION_STOCK_CONFLICT");
                        }
                    }
                }

                // MDM21-R01: Complete 6-bucket stock & operational guard when turning IsStorable off
                if (request.IsStorable.HasValue && location.IsStorable && !request.IsStorable.Value)
                {
                    await WarehouseInventoryDependencyGuard.EnsureLocationCanBecomeNonStorableAsync(
                        _db, facilityId, location.Id, location.LocationCode, cancellationToken);
                }

                // MDM21-R02: Validate IsStorableLeaf rule when turning IsStorable on
                if (request.IsStorable == true && !location.IsStorable)
                {
                    await LocationHierarchyRuleEvaluator.ValidatePlacementAsync(
                        _db,
                        facilityId,
                        location.ParentLocation?.LocationType,
                        location.LocationType,
                        location.HierarchyLevel,
                        cancellationToken,
                        requestedIsStorable: true);
                }

                if (request.LocationName != null)
                    location.LocationName = string.IsNullOrWhiteSpace(request.LocationName) ? location.LocationCode : request.LocationName.Trim();

                if (request.ZoneId.HasValue) location.ZoneId = request.ZoneId;
                location.AisleCode = request.AisleCode;
                location.RackCode = request.RackCode;
                location.ShelfLevel = request.ShelfLevel;
                location.BinNumber = request.BinNumber;
                location.PickSequence = request.PickSequence;
                location.LengthCm = request.LengthCm;
                location.WidthCm = request.WidthCm;
                location.HeightCm = request.HeightCm;
                location.MaxVolumeCm3 = request.MaxVolumeCm3 ?? (request.LengthCm * request.WidthCm * request.HeightCm);
                location.MaxWeightKg = request.MaxWeightKg;
                if (request.IsStorable.HasValue) location.IsStorable = request.IsStorable.Value;
                if (request.IsPickable.HasValue) location.IsPickable = request.IsPickable.Value;
                if (request.IsReceiving.HasValue) location.IsReceiving = request.IsReceiving.Value;
                if (request.IsDamaged.HasValue) location.IsDamaged = request.IsDamaged.Value;
                if (request.Remarks != null) location.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();

                location.ConfigRevision++;
                location.UpdatedBy = userId;

                await _db.SaveChangesAsync(cancellationToken);
                return WarehouseMasterMappers.ToLocationDto(location);
            }, cancellationToken), cancellationToken);
    }
}

/// <summary>
/// Explicit reparenting command serialized via per-facility PostgreSQL advisory lock (A02, R02, R04, R06).
/// Validates active parent/ancestors, active zone, cycle prevention, and EVERY affected descendant
/// against its effective hierarchy rule and configurable MaxDepth before committing atomically.
/// </summary>
public sealed record MoveLocationCommand(Guid LocationId, Guid? NewParentLocationId, Guid? NewZoneId = null) : IRequest<LocationDto>;

public sealed class MoveLocationCommandHandler : IRequestHandler<MoveLocationCommand, LocationDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public MoveLocationCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LocationDto> Handle(MoveLocationCommand request, CancellationToken cancellationToken)
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

        return await _db.ExecuteWithFacilityHierarchyLockAsync(facilityId, async () =>
        {
            var location = await _db.Locations
                .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.FacilityId == facilityId && !l.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

            if (request.NewParentLocationId.HasValue && request.NewParentLocationId.Value == location.Id)
            {
                throw new DomainException(
                    $"Location '{location.LocationCode}' cannot be its own parent.",
                    "LOCATION_HIERARCHY_CYCLE");
            }

            if (request.NewZoneId.HasValue)
            {
                var zone = await _db.WarehouseZones
                    .AsNoTracking()
                    .FirstOrDefaultAsync(z => z.Id == request.NewZoneId.Value && z.FacilityId == facilityId && !z.IsDeleted, cancellationToken);
                if (zone == null)
                {
                    throw new ValidationException(
                        nameof(request.NewZoneId),
                        $"Zone '{request.NewZoneId.Value}' does not exist in facility '{facilityId}'.");
                }

                if (!zone.IsActive)
                {
                    throw new ValidationException(
                        nameof(request.NewZoneId),
                        $"Zone '{zone.ZoneCode}' is inactive and cannot accept moved locations.");
                }
            }

            var facilityLocations = await _db.Locations
                .Where(l => l.FacilityId == facilityId && !l.IsDeleted)
                .ToListAsync(cancellationToken);

            var byId = facilityLocations.ToDictionary(l => l.Id);
            var targetNode = byId[location.Id];

            Location? newParent = null;
            if (request.NewParentLocationId.HasValue)
            {
                if (!byId.TryGetValue(request.NewParentLocationId.Value, out newParent))
                {
                    throw new ValidationException(
                        nameof(request.NewParentLocationId),
                        $"Parent location '{request.NewParentLocationId.Value}' does not exist in facility '{facilityId}'.");
                }

                // Walk up from newParent to root to detect cycles AND enforce active parent/ancestor invariant (R04/R06)
                var cursor = newParent;
                var visited = new HashSet<Guid>();
                while (cursor != null)
                {
                    if (cursor.Id == targetNode.Id || !visited.Add(cursor.Id))
                    {
                        throw new DomainException(
                            $"Moving location '{targetNode.LocationCode}' under '{newParent.LocationCode}' would introduce a hierarchy cycle.",
                            "LOCATION_HIERARCHY_CYCLE");
                    }

                    if (targetNode.Status == LocationStatus.Active &&
                        (cursor.Status != LocationStatus.Active || !cursor.IsActive))
                    {
                        throw new DomainException(
                            $"Cannot move active location '{targetNode.LocationCode}' beneath inactive or decommissioned ancestor '{cursor.LocationCode}'.",
                            "LOCATION_INACTIVE_ANCESTOR");
                    }

                    cursor = cursor.ParentLocationId.HasValue && byId.TryGetValue(cursor.ParentLocationId.Value, out var p)
                        ? p
                        : null;
                }
            }

            var childrenByParent = facilityLocations
                .Where(l => l.ParentLocationId.HasValue)
                .GroupBy(l => l.ParentLocationId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            int newRootLevel = newParent == null ? 1 : newParent.HierarchyLevel + 1;

            // Validate moved root node against its effective hierarchy rule
            await LocationHierarchyRuleEvaluator.ValidatePlacementAsync(
                _db,
                facilityId,
                newParent?.LocationType,
                targetNode.LocationType,
                newRootLevel,
                cancellationToken,
                requestedIsStorable: targetNode.IsStorable);

            // MDM21-R02: Validate EVERY affected descendant against its effective rule & configurable MaxDepth BEFORE mutating
            var validationQueue = new Queue<(Location Node, int NewLevel)>();
            validationQueue.Enqueue((targetNode, newRootLevel));
            while (validationQueue.Count > 0)
            {
                var (current, currentLevel) = validationQueue.Dequeue();
                if (!childrenByParent.TryGetValue(current.Id, out var children)) continue;

                foreach (var child in children)
                {
                    int childNewLevel = currentLevel + 1;
                    await LocationHierarchyRuleEvaluator.ValidatePlacementAsync(
                        _db,
                        facilityId,
                        current.LocationType,
                        child.LocationType,
                        childNewLevel,
                        cancellationToken,
                        requestedIsStorable: child.IsStorable);
                    validationQueue.Enqueue((child, childNewLevel));
                }
            }

            // Apply move & atomically recalculate HierarchyLevel & ID-based HierarchyPath for node + entire subtree
            targetNode.ParentLocationId = newParent?.Id;
            if (request.NewZoneId.HasValue)
            {
                targetNode.ZoneId = request.NewZoneId.Value;
            }
            targetNode.HierarchyLevel = newRootLevel;
            targetNode.HierarchyPath = Location.BuildHierarchyPath(newParent?.HierarchyPath, targetNode.Id);
            targetNode.ConfigRevision++;
            targetNode.UpdatedBy = userId;

            var queue = new Queue<Location>();
            queue.Enqueue(targetNode);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!childrenByParent.TryGetValue(current.Id, out var children)) continue;

                foreach (var child in children)
                {
                    child.HierarchyLevel = current.HierarchyLevel + 1;
                    child.HierarchyPath = Location.BuildHierarchyPath(current.HierarchyPath, child.Id);
                    child.ConfigRevision++;
                    child.UpdatedBy = userId;
                    queue.Enqueue(child);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            return WarehouseMasterMappers.ToLocationDto(targetNode);
        }, cancellationToken);
    }
}

/// <summary>
/// Administrative lifecycle transition for Location (A01, A07, R01, R04, R06).
/// Canonical lock order: FacilityHierarchy(facilityId) -> LocationLifecycle(locationId).
/// </summary>
public sealed record UpdateLocationStatusCommand(Guid LocationId, LocationStatus Status, string? Remarks = null) : IRequest<LocationDto>;

public sealed class UpdateLocationStatusCommandHandler : IRequestHandler<UpdateLocationStatusCommand, LocationDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public UpdateLocationStatusCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LocationDto> Handle(UpdateLocationStatusCommand request, CancellationToken cancellationToken)
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

        // MDM21-R04: FacilityHierarchy -> LocationLifecycle serializes parent deactivation vs child creation/move
        return await _db.ExecuteWithFacilityHierarchyLockAsync(facilityId, async () =>
            await _db.ExecuteWithLocationLifecycleLockAsync(facilityId, request.LocationId, async () =>
            {
                var location = await _db.Locations
                    .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.FacilityId == facilityId && !l.IsDeleted, cancellationToken)
                    ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

                if (request.Status is LocationStatus.Inactive or LocationStatus.Decommissioned)
                {
                    await WarehouseInventoryDependencyGuard.EnsureLocationCanBeDeactivatedAsync(
                        _db, facilityId, location.Id, location.LocationCode, request.Status, cancellationToken);
                }
                else if (request.Status == LocationStatus.Active)
                {
                    // MDM21-R04 & R06: Cannot activate a location beneath an inactive ancestor or inactive zone
                    if (location.ZoneId.HasValue)
                    {
                        var zoneActive = await _db.WarehouseZones
                            .AsNoTracking()
                            .AnyAsync(z => z.Id == location.ZoneId.Value && z.FacilityId == facilityId && !z.IsDeleted && z.IsActive, cancellationToken);
                        if (!zoneActive)
                        {
                            throw new DomainException(
                                $"Cannot activate location '{location.LocationCode}' while its assigned zone is inactive.",
                                "LOCATION_INACTIVE_ZONE");
                        }
                    }

                    if (location.ParentLocationId.HasValue)
                    {
                        var facilityLocs = await _db.Locations
                            .AsNoTracking()
                            .Where(l => l.FacilityId == facilityId && !l.IsDeleted)
                            .Select(l => new { l.Id, l.ParentLocationId, l.LocationCode, l.Status, l.IsActive })
                            .ToDictionaryAsync(l => l.Id, cancellationToken);

                        var cursorId = location.ParentLocationId;
                        var guard = new HashSet<Guid>();
                        while (cursorId.HasValue && guard.Add(cursorId.Value))
                        {
                            if (!facilityLocs.TryGetValue(cursorId.Value, out var ancestor) ||
                                ancestor.Status != LocationStatus.Active ||
                                !ancestor.IsActive)
                            {
                                throw new DomainException(
                                    $"Cannot activate location '{location.LocationCode}' while ancestor '{ancestor?.LocationCode ?? cursorId.Value.ToString()}' is inactive or decommissioned.",
                                    "LOCATION_INACTIVE_ANCESTOR");
                            }
                            cursorId = ancestor.ParentLocationId;
                        }
                    }
                }

                // A01: SetLifecycleStatus ONLY changes Status/IsActive — NEVER releases IsLocked or clears IsDamaged
                location.SetLifecycleStatus(request.Status);
                if (request.Remarks != null)
                {
                    location.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
                }
                location.ConfigRevision++;
                location.UpdatedBy = userId;

                await _db.SaveChangesAsync(cancellationToken);
                return WarehouseMasterMappers.ToLocationDto(location);
            }, cancellationToken), cancellationToken);
    }
}

public sealed record GetLocationLocksQuery(Guid LocationId, bool ActiveOnly = false) : IRequest<List<LocationLockDto>>;

public sealed class GetLocationLocksQueryHandler : IRequestHandler<GetLocationLocksQuery, List<LocationLockDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public GetLocationLocksQueryHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<List<LocationLockDto>> Handle(GetLocationLocksQuery request, CancellationToken cancellationToken)
    {
        var location = await _db.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.LocationId && !l.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

        await _auth.ResolveAuthorizedFacilityReadAsync(
            location.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsView,
            Permissions.Facilities.View);

        var query = _db.LocationLocks
            .AsNoTracking()
            .Where(lk => lk.LocationId == request.LocationId);

        if (request.ActiveOnly)
            query = query.Where(lk => lk.ReleasedAt == null);

        var locks = await query
            .OrderByDescending(lk => lk.LockedAt)
            .ToListAsync(cancellationToken);

        return locks.Select(WarehouseMasterMappers.ToDto).ToList();
    }
}

public sealed record ReleaseLocationLockCommand(Guid LocationId, Guid LockId) : IRequest<LocationLockDto>;

public sealed class ReleaseLocationLockCommandHandler : IRequestHandler<ReleaseLocationLockCommand, LocationLockDto>
{
    private readonly IApplicationDbContext _db;
    private readonly WarehouseMasterAuthorization _auth;

    public ReleaseLocationLockCommandHandler(IApplicationDbContext db, WarehouseMasterAuthorization auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<LocationLockDto> Handle(ReleaseLocationLockCommand request, CancellationToken cancellationToken)
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

        return await _db.ExecuteWithLocationLifecycleLockAsync(facilityId, request.LocationId, async () =>
        {
            var location = await _db.Locations
                .Include(l => l.Locks)
                .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.FacilityId == facilityId && !l.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

            var targetLock = location.Locks.FirstOrDefault(lk => lk.Id == request.LockId)
                ?? throw new EntityNotFoundException(nameof(LocationLock), request.LockId);

            if (!targetLock.ReleasedAt.HasValue)
            {
                targetLock.ReleasedAt = DateTime.UtcNow;
                targetLock.ReleasedByUserId = userId;
            }

            location.IsLocked = location.Locks.Any(lk => !lk.ReleasedAt.HasValue);
            location.ConfigRevision++;
            location.UpdatedBy = userId;

            await _db.SaveChangesAsync(cancellationToken);
            return WarehouseMasterMappers.ToDto(targetLock);
        }, cancellationToken);
    }
}
