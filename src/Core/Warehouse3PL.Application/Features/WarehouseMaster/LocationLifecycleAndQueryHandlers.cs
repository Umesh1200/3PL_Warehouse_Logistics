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
        var (_, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
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

        await _auth.ResolveAuthorizedFacilityAsync(
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
        var location = await _db.Locations
            .FirstOrDefaultAsync(l => l.Id == request.Id && !l.IsDeleted, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Location), request.Id);

        var (userId, facilityId, _) = await _auth.ResolveAuthorizedFacilityAsync(
            location.FacilityId,
            cancellationToken,
            Permissions.Facilities.LocationsManage,
            Permissions.Facilities.Manage);

        // A03: LocationCode is immutable by default
        if (!string.IsNullOrWhiteSpace(request.LocationCode) &&
            !string.Equals(Location.NormalizeCode(request.LocationCode), location.NormalizedLocationCode, StringComparison.Ordinal))
        {
            throw new DomainException(
                "LOCATION_IMMUTABLE_CODE",
                $"LocationCode '{location.LocationCode}' is immutable and cannot be changed to '{request.LocationCode}'.");
        }

        if (request.ZoneId.HasValue)
        {
            var zoneValid = await _db.WarehouseZones
                .AsNoTracking()
                .AnyAsync(z => z.Id == request.ZoneId.Value && z.FacilityId == facilityId && !z.IsDeleted, cancellationToken);
            if (!zoneValid)
            {
                throw new ValidationException(
                    nameof(request.ZoneId),
                    $"Zone '{request.ZoneId.Value}' does not exist in facility '{facilityId}'.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            var normalizedBarcode = Location.NormalizeCode(request.Barcode);
            if (!string.Equals(normalizedBarcode, location.NormalizedBarcode, StringComparison.Ordinal))
            {
                var barcodeExists = await _db.Locations
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(l => l.FacilityId == facilityId && l.Id != location.Id && l.NormalizedBarcode == normalizedBarcode, cancellationToken);
                if (barcodeExists)
                {
                    throw new ValidationException(
                        nameof(request.Barcode),
                        $"Barcode '{request.Barcode.Trim()}' is already permanently assigned in this facility.");
                }

                location.Barcode = request.Barcode.Trim();
                location.NormalizedBarcode = normalizedBarcode;
            }
        }

        if (request.IsStorable.HasValue && location.IsStorable && !request.IsStorable.Value)
        {
            var hasStock = await _db.StockOnHand
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(s => s.FacilityId == facilityId && s.LocationId == location.Id &&
                               (s.QtyAvailable > 0m || s.QtyAllocated > 0m || s.QtyQuarantined > 0m),
                    cancellationToken);
            if (hasStock)
            {
                throw new DomainException(
                    "LOCATION_NON_STORABLE_BLOCKED_STOCK",
                    $"Cannot mark location '{location.LocationCode}' as non-storable while active stock resides in it.");
            }
        }

        if (request.LocationName != null)
            location.LocationName = string.IsNullOrWhiteSpace(request.LocationName) ? location.LocationCode : request.LocationName.Trim();

        location.ZoneId = request.ZoneId;
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
    }
}

/// <summary>
/// Explicit reparenting command serialized via per-facility PostgreSQL advisory lock (A02).
/// Prevents direct/indirect cycles, concurrent opposite A->B vs B->A races, cross-facility reparenting,
/// hierarchy rule violations, and atomically recalculates HierarchyLevel & ID-based HierarchyPath across the entire subtree.
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
                    "LOCATION_HIERARCHY_CYCLE",
                    $"Location '{location.LocationCode}' cannot be its own parent.");
            }

            if (request.NewZoneId.HasValue)
            {
                var zoneValid = await _db.WarehouseZones
                    .AsNoTracking()
                    .AnyAsync(z => z.Id == request.NewZoneId.Value && z.FacilityId == facilityId && !z.IsDeleted, cancellationToken);
                if (!zoneValid)
                {
                    throw new ValidationException(
                        nameof(request.NewZoneId),
                        $"Zone '{request.NewZoneId.Value}' does not exist in facility '{facilityId}'.");
                }
            }

            // Load all facility locations under the hierarchy lock so cycle detection & subtree recalculation are 100% atomic.
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

                // Walk up from newParent to root to detect any indirect cycle
                var cursor = newParent;
                var visited = new HashSet<Guid>();
                while (cursor != null)
                {
                    if (cursor.Id == targetNode.Id || !visited.Add(cursor.Id))
                    {
                        throw new DomainException(
                            "LOCATION_HIERARCHY_CYCLE",
                            $"Moving location '{targetNode.LocationCode}' under '{newParent.LocationCode}' would introduce a hierarchy cycle.");
                    }

                    cursor = cursor.ParentLocationId.HasValue && byId.TryGetValue(cursor.ParentLocationId.Value, out var p)
                        ? p
                        : null;
                }
            }

            // Compute subtree descendants of targetNode
            var childrenByParent = facilityLocations
                .Where(l => l.ParentLocationId.HasValue)
                .GroupBy(l => l.ParentLocationId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            int newRootLevel = newParent == null ? 1 : newParent.HierarchyLevel + 1;
            int maxRelativeDepth = ComputeMaxSubtreeDepth(targetNode.Id, childrenByParent);

            if (newRootLevel + maxRelativeDepth > LocationHierarchyRuleEvaluator.HardMaxHierarchyDepth)
            {
                throw new ValidationException(
                    nameof(request.NewParentLocationId),
                    $"Moving '{targetNode.LocationCode}' under '{newParent?.LocationCode ?? "ROOT"}' would exceed maximum hierarchy depth {LocationHierarchyRuleEvaluator.HardMaxHierarchyDepth}.");
            }

            await LocationHierarchyRuleEvaluator.ValidatePlacementAsync(
                _db,
                facilityId,
                newParent?.LocationType,
                targetNode.LocationType,
                newRootLevel,
                cancellationToken);

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

    private static int ComputeMaxSubtreeDepth(Guid rootId, Dictionary<Guid, List<Location>> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(rootId, out var children) || children.Count == 0)
            return 0;

        int max = 0;
        foreach (var child in children)
        {
            var d = 1 + ComputeMaxSubtreeDepth(child.Id, childrenByParent);
            if (d > max) max = d;
        }
        return max;
    }
}

/// <summary>
/// Administrative lifecycle transition for Location (A01 & A07).
/// Serialized via ExecuteWithLocationLifecycleLockAsync (row FOR UPDATE + advisory lock).
/// Blocks deactivation/decommissioning when active stock, handling units, open putaway/pick tasks, or active children exist.
/// Reactivating to Active NEVER implicitly releases operational locks or clears IsDamaged (A01).
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

        return await _db.ExecuteWithLocationLifecycleLockAsync(facilityId, request.LocationId, async () =>
        {
            var location = await _db.Locations
                .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.FacilityId == facilityId && !l.IsDeleted, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Location), request.LocationId);

            if (request.Status is LocationStatus.Inactive or LocationStatus.Decommissioned)
            {
                var hasActiveStock = await _db.StockOnHand
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(s => s.FacilityId == facilityId && s.LocationId == location.Id &&
                                   (s.QtyAvailable > 0m || s.QtyAllocated > 0m || s.QtyQuarantined > 0m),
                        cancellationToken);
                if (hasActiveStock)
                {
                    throw new DomainException(
                        "LOCATION_DEACTIVATION_BLOCKED_STOCK",
                        $"Cannot transition location '{location.LocationCode}' to '{request.Status}' while active stock resides in it.");
                }

                var hasActiveHandlingUnits = await _db.HandlingUnits
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(hu => hu.FacilityId == facilityId &&
                                    hu.CurrentLocationId == location.Id &&
                                    !hu.IsDeleted &&
                                    hu.Status != HandlingUnitStatus.Damaged,
                        cancellationToken);
                if (hasActiveHandlingUnits)
                {
                    throw new DomainException(
                        "LOCATION_DEACTIVATION_BLOCKED_HU",
                        $"Cannot transition location '{location.LocationCode}' to '{request.Status}' while active handling units are located in it.");
                }

                var hasOpenPutaway = await _db.PutawayTasks
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(t => t.FacilityId == facilityId &&
                                   (t.SourceLocationId == location.Id ||
                                    t.SuggestedLocationId == location.Id ||
                                    t.ActualLocationId == location.Id) &&
                                   t.Status != PutawayTaskStatus.Completed &&
                                   t.Status != PutawayTaskStatus.Cancelled,
                        cancellationToken);
                if (hasOpenPutaway)
                {
                    throw new DomainException(
                        "LOCATION_DEACTIVATION_BLOCKED_PUTAWAY",
                        $"Cannot transition location '{location.LocationCode}' to '{request.Status}' while open putaway tasks reference it.");
                }

                var hasOpenPick = await _db.PickTasks
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(t => t.FacilityId == facilityId &&
                                   t.SourceLocationId == location.Id &&
                                   t.Status != PickTaskStatus.Completed &&
                                   t.Status != PickTaskStatus.Cancelled,
                        cancellationToken);
                if (hasOpenPick)
                {
                    throw new DomainException(
                        "LOCATION_DEACTIVATION_BLOCKED_PICK",
                        $"Cannot transition location '{location.LocationCode}' to '{request.Status}' while open pick tasks reference it.");
                }

                var hasActiveChildren = await _db.Locations
                    .AsNoTracking()
                    .AnyAsync(c => c.FacilityId == facilityId &&
                                   c.ParentLocationId == location.Id &&
                                   !c.IsDeleted &&
                                   c.Status == LocationStatus.Active,
                        cancellationToken);
                if (hasActiveChildren)
                {
                    throw new DomainException(
                        "LOCATION_DEACTIVATION_BLOCKED_CHILDREN",
                        $"Cannot transition location '{location.LocationCode}' to '{request.Status}' while active child locations exist.");
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
        }, cancellationToken);
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

        await _auth.ResolveAuthorizedFacilityAsync(
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

/// <summary>
/// A01: Releases a single specific operational LocationLock by ID and recalculates Location.IsLocked
/// from remaining active locks without touching other independent locks or Location.Status.
/// </summary>
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
