using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.WarehouseMaster;

/// <summary>
/// Centralized, EF-translatable inventory presence and operational dependency guard (MDM21-R01 &amp; MDM21-F01).
/// Covers all six StockOnHand quantity buckets (QtyAvailable, QtyAllocated, QtyReserved,
/// QtyQuarantined, QtyDamaged, QtyOnHold) and all active operational references across
/// Location, WarehouseZone, and Facility lifecycle transitions.
/// </summary>
public static class WarehouseInventoryDependencyGuard
{
    /// <summary>
    /// EF-translatable predicate matching any StockOnHand row with positive quantity in any of the six buckets.
    /// </summary>
    public static readonly Expression<Func<StockOnHand, bool>> HasAnyStockExpression = s =>
        s.QtyAvailable > 0m ||
        s.QtyAllocated > 0m ||
        s.QtyReserved > 0m ||
        s.QtyQuarantined > 0m ||
        s.QtyDamaged > 0m ||
        s.QtyOnHold > 0m;

    public static bool HasAnyStock(StockOnHand s) =>
        s.QtyAvailable > 0m ||
        s.QtyAllocated > 0m ||
        s.QtyReserved > 0m ||
        s.QtyQuarantined > 0m ||
        s.QtyDamaged > 0m ||
        s.QtyOnHold > 0m;

    /// <summary>
    /// Revalidates that a facility is active inside a locked transactional mutation boundary (MDM21-F03-C).
    /// </summary>
    public static async Task EnsureFacilityIsActiveAsync(
        IApplicationDbContext db,
        Guid facilityId,
        CancellationToken ct)
    {
        var facility = await db.Facilities
            .AsNoTracking()
            .Where(f => f.Id == facilityId && !f.IsDeleted)
            .Select(f => new { f.FacilityCode, f.IsActive })
            .FirstOrDefaultAsync(ct);

        if (facility == null)
        {
            if (db.IsRelationalDatabase || await db.Facilities.IgnoreQueryFilters().AsNoTracking().AnyAsync(ct))
            {
                throw new EntityNotFoundException("Facility", facilityId);
            }
            return;
        }

        if (!facility.IsActive)
        {
            throw new DomainException(
                $"Facility '{facility.FacilityCode}' is inactive and cannot accept warehouse configuration or operational mutations.",
                "FACILITY_INACTIVE");
        }
    }

    /// <summary>
    /// Authoritative shared zone-assignment validation reused by both UpdateLocationCommandHandler
    /// and MoveLocationCommandHandler (MDM21-F02-B). Validates active same-facility zone and rejects
    /// dedication conflicts against storage profile, 6-bucket stock, and physically present handling units.
    /// </summary>
    public static async Task ValidateLocationZoneAssignmentAsync(
        IApplicationDbContext db,
        Guid facilityId,
        Guid locationId,
        string locationCode,
        Guid targetZoneId,
        CancellationToken ct)
    {
        var zone = await db.WarehouseZones
            .AsNoTracking()
            .FirstOrDefaultAsync(z => z.Id == targetZoneId && z.FacilityId == facilityId && !z.IsDeleted, ct);
        if (zone == null)
        {
            throw new ValidationException(
                "ZoneId",
                $"Zone '{targetZoneId}' does not exist in facility '{facilityId}'.");
        }

        if (!zone.IsActive)
        {
            throw new ValidationException(
                "ZoneId",
                $"Zone '{zone.ZoneCode}' is inactive and cannot accept location assignments.");
        }

        if (!zone.DedicatedClientId.HasValue)
        {
            return;
        }

        var dedicatedClientId = zone.DedicatedClientId.Value;

        var profile = await db.LocationStorageProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == locationId && p.FacilityId == facilityId, ct);
        if (profile?.DedicatedClientId is Guid profileClientId && profileClientId != dedicatedClientId)
        {
            throw new DomainException(
                $"Zone '{zone.ZoneCode}' dedicated client conflicts with location '{locationCode}' storage profile dedication.",
                "ZONE_LOCATION_DEDICATION_CONFLICT");
        }

        var hasForeignStock = await db.StockOnHand
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.LocationId == locationId && s.ClientId != dedicatedClientId)
            .AnyAsync(HasAnyStockExpression, ct);
        if (hasForeignStock)
        {
            throw new DomainException(
                $"Cannot assign location '{locationCode}' to dedicated zone '{zone.ZoneCode}' while stock for another client exists in the location.",
                "ZONE_DEDICATION_STOCK_CONFLICT");
        }

        var hasForeignHu = await db.HandlingUnits
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(hu => hu.FacilityId == facilityId &&
                            hu.CurrentLocationId == locationId &&
                            !hu.IsDeleted &&
                            hu.ClientId != dedicatedClientId,
                ct);
        if (hasForeignHu)
        {
            throw new DomainException(
                $"Cannot assign location '{locationCode}' to dedicated zone '{zone.ZoneCode}' while handling units belonging to another client are physically present.",
                "ZONE_DEDICATION_HU_CONFLICT");
        }
    }

    public static Task<bool> LocationHasAnyStockAsync(
        IApplicationDbContext db,
        Guid facilityId,
        Guid locationId,
        CancellationToken ct) =>
        db.StockOnHand
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.LocationId == locationId)
            .AnyAsync(HasAnyStockExpression, ct);

    public static Task<bool> LocationHasPresentHandlingUnitsAsync(
        IApplicationDbContext db,
        Guid facilityId,
        Guid locationId,
        CancellationToken ct) =>
        db.HandlingUnits
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(hu => hu.FacilityId == facilityId &&
                            hu.CurrentLocationId == locationId &&
                            !hu.IsDeleted,
                ct);

    /// <summary>
    /// Enforces all R01 location deactivation/decommissioning guards:
    /// all 6 stock buckets, physically present HUs (including Damaged), open putaway/pick/cross-dock tasks,
    /// open packing sessions, unreconciled open cycle-count items, active inventory allocations, and active children.
    /// </summary>
    public static async Task EnsureLocationCanBeDeactivatedAsync(
        IApplicationDbContext db,
        Guid facilityId,
        Guid locationId,
        string locationCode,
        LocationStatus targetStatus,
        CancellationToken ct)
    {
        if (await LocationHasAnyStockAsync(db, facilityId, locationId, ct))
        {
            throw new DomainException(
                $"Cannot transition location '{locationCode}' to '{targetStatus}' while inventory exists in any stock bucket.",
                "LOCATION_DEACTIVATION_BLOCKED_STOCK");
        }

        if (await LocationHasPresentHandlingUnitsAsync(db, facilityId, locationId, ct))
        {
            throw new DomainException(
                $"Cannot transition location '{locationCode}' to '{targetStatus}' while handling units (including damaged units) are physically present.",
                "LOCATION_DEACTIVATION_BLOCKED_HU");
        }

        var hasOpenPutaway = await db.PutawayTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.FacilityId == facilityId &&
                           (t.SourceLocationId == locationId ||
                            t.SuggestedLocationId == locationId ||
                            t.ActualLocationId == locationId) &&
                           t.Status != PutawayTaskStatus.Completed &&
                           t.Status != PutawayTaskStatus.Cancelled,
                ct);
        if (hasOpenPutaway)
        {
            throw new DomainException(
                $"Cannot transition location '{locationCode}' to '{targetStatus}' while open putaway tasks reference it.",
                "LOCATION_DEACTIVATION_BLOCKED_PUTAWAY");
        }

        var hasOpenPick = await db.PickTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.FacilityId == facilityId &&
                           !t.IsDeleted &&
                           (t.SourceLocationId == locationId || t.DestinationLocationId == locationId) &&
                           t.Status != PickTaskStatus.Completed &&
                           t.Status != PickTaskStatus.ShortPicked &&
                           t.Status != PickTaskStatus.Cancelled,
                ct);
        if (hasOpenPick)
        {
            throw new DomainException(
                $"Cannot transition location '{locationCode}' to '{targetStatus}' while open pick tasks reference it.",
                "LOCATION_DEACTIVATION_BLOCKED_PICK");
        }

        var hasOpenCrossDock = await db.CrossDockTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.FacilityId == facilityId &&
                           (t.SourceLocationId == locationId ||
                            t.DestinationLocationId == locationId ||
                            t.ActualDestinationLocationId == locationId) &&
                           t.Status != CrossDockTaskStatus.Completed &&
                           t.Status != CrossDockTaskStatus.Cancelled,
                ct);
        if (hasOpenCrossDock)
        {
            throw new DomainException(
                $"Cannot transition location '{locationCode}' to '{targetStatus}' while open cross-dock tasks reference it.",
                "LOCATION_DEACTIVATION_BLOCKED_CROSSDOCK");
        }

        var hasOpenPacking = await db.PackingSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(ps => ps.FacilityId == facilityId &&
                            !ps.IsDeleted &&
                            ps.PackingStationLocationId == locationId &&
                            ps.Status != PackingSessionStatus.Completed &&
                            ps.Status != PackingSessionStatus.Aborted,
                ct);
        if (hasOpenPacking)
        {
            throw new DomainException(
                $"Cannot transition location '{locationCode}' to '{targetStatus}' while open packing sessions are active at this station.",
                "LOCATION_DEACTIVATION_BLOCKED_PACKING");
        }

        var hasOpenCycleCount = await db.CycleCountItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(ci => ci.LocationId == locationId &&
                            !ci.IsReconciled &&
                            ci.CycleCount.FacilityId == facilityId &&
                            ci.CycleCount.Status != CycleCountStatus.Completed &&
                            ci.CycleCount.Status != CycleCountStatus.Cancelled,
                ct);
        if (hasOpenCycleCount)
        {
            throw new DomainException(
                $"Cannot transition location '{locationCode}' to '{targetStatus}' while unreconciled open cycle-count items reference it.",
                "LOCATION_DEACTIVATION_BLOCKED_CYCLE_COUNT");
        }

        var hasActiveAllocations = await db.InventoryAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.FacilityId == facilityId &&
                           !a.IsDeleted &&
                           a.LocationId == locationId &&
                           a.Status != AllocationStatus.Picked &&
                           a.Status != AllocationStatus.Released &&
                           a.Status != AllocationStatus.Cancelled,
                ct);
        if (hasActiveAllocations)
        {
            throw new DomainException(
                $"Cannot transition location '{locationCode}' to '{targetStatus}' while active inventory allocations reference it.",
                "LOCATION_DEACTIVATION_BLOCKED_ALLOCATION");
        }

        var hasActiveChildren = await db.Locations
            .AsNoTracking()
            .AnyAsync(c => c.FacilityId == facilityId &&
                           c.ParentLocationId == locationId &&
                           !c.IsDeleted &&
                           c.Status == LocationStatus.Active,
                ct);
        if (hasActiveChildren)
        {
            throw new DomainException(
                $"Cannot transition location '{locationCode}' to '{targetStatus}' while active child locations exist.",
                "LOCATION_DEACTIVATION_BLOCKED_CHILDREN");
        }
    }

    /// <summary>
    /// Enforces R01 guards when marking a location non-storable (IsStorable = false).
    /// </summary>
    public static async Task EnsureLocationCanBecomeNonStorableAsync(
        IApplicationDbContext db,
        Guid facilityId,
        Guid locationId,
        string locationCode,
        CancellationToken ct)
    {
        if (await LocationHasAnyStockAsync(db, facilityId, locationId, ct))
        {
            throw new DomainException(
                $"Cannot mark location '{locationCode}' as non-storable while inventory exists in any stock bucket.",
                "LOCATION_NON_STORABLE_BLOCKED_STOCK");
        }

        if (await LocationHasPresentHandlingUnitsAsync(db, facilityId, locationId, ct))
        {
            throw new DomainException(
                $"Cannot mark location '{locationCode}' as non-storable while handling units are physically present.",
                "LOCATION_NON_STORABLE_BLOCKED_HU");
        }

        var hasOpenPutawayInbound = await db.PutawayTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.FacilityId == facilityId &&
                           (t.SuggestedLocationId == locationId || t.ActualLocationId == locationId) &&
                           t.Status != PutawayTaskStatus.Completed &&
                           t.Status != PutawayTaskStatus.Cancelled,
                ct);
        if (hasOpenPutawayInbound)
        {
            throw new DomainException(
                $"Cannot mark location '{locationCode}' as non-storable while open putaway tasks target it.",
                "LOCATION_NON_STORABLE_BLOCKED_PUTAWAY");
        }

        var hasActiveAllocations = await db.InventoryAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.FacilityId == facilityId &&
                           !a.IsDeleted &&
                           a.LocationId == locationId &&
                           a.Status != AllocationStatus.Picked &&
                           a.Status != AllocationStatus.Released &&
                           a.Status != AllocationStatus.Cancelled,
                ct);
        if (hasActiveAllocations)
        {
            throw new DomainException(
                $"Cannot mark location '{locationCode}' as non-storable while active inventory allocations reference it.",
                "LOCATION_NON_STORABLE_BLOCKED_ALLOCATION");
        }
    }

    public static async Task EnsureZoneCanBeDeactivatedAsync(
        IApplicationDbContext db,
        Guid facilityId,
        Guid zoneId,
        string zoneCode,
        CancellationToken ct)
    {
        var hasActiveLocations = await db.Locations
            .AsNoTracking()
            .AnyAsync(l => l.FacilityId == facilityId &&
                           l.ZoneId == zoneId &&
                           !l.IsDeleted &&
                           l.Status == LocationStatus.Active,
                ct);
        if (hasActiveLocations)
        {
            throw new DomainException(
                $"Cannot deactivate zone '{zoneCode}' while active locations are assigned to it.",
                "ZONE_DEACTIVATION_BLOCKED_LOCATIONS");
        }

        // MDM21-F01: Include all locations in the zone (even inactive/legacy inconsistent ones)
        // so open operational dependencies on inactive locations cannot be orphaned by zone deactivation.
        var zoneLocationIds = db.Locations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => l.FacilityId == facilityId && l.ZoneId == zoneId)
            .Select(l => l.Id);

        var hasStock = await db.StockOnHand
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && zoneLocationIds.Contains(s.LocationId))
            .AnyAsync(HasAnyStockExpression, ct);
        if (hasStock)
        {
            throw new DomainException(
                $"Cannot deactivate zone '{zoneCode}' while inventory exists in any stock bucket within its locations.",
                "ZONE_DEACTIVATION_BLOCKED_STOCK");
        }

        var hasPresentHus = await db.HandlingUnits
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(hu => hu.FacilityId == facilityId &&
                            !hu.IsDeleted &&
                            hu.CurrentLocationId.HasValue &&
                            zoneLocationIds.Contains(hu.CurrentLocationId.Value),
                ct);
        if (hasPresentHus)
        {
            throw new DomainException(
                $"Cannot deactivate zone '{zoneCode}' while handling units (including damaged units) are physically present within its locations.",
                "ZONE_DEACTIVATION_BLOCKED_HU");
        }

        var hasOpenPutaway = await db.PutawayTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.FacilityId == facilityId &&
                           (zoneLocationIds.Contains(t.SourceLocationId) ||
                            zoneLocationIds.Contains(t.SuggestedLocationId) ||
                            (t.ActualLocationId.HasValue && zoneLocationIds.Contains(t.ActualLocationId.Value))) &&
                           t.Status != PutawayTaskStatus.Completed &&
                           t.Status != PutawayTaskStatus.Cancelled,
                ct);
        if (hasOpenPutaway)
        {
            throw new DomainException(
                $"Cannot deactivate zone '{zoneCode}' while open putaway tasks reference its locations.",
                "ZONE_DEACTIVATION_BLOCKED_PUTAWAY");
        }

        var hasOpenPicks = await db.PickTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.FacilityId == facilityId &&
                           !t.IsDeleted &&
                           (zoneLocationIds.Contains(t.SourceLocationId) ||
                            (t.DestinationLocationId.HasValue && zoneLocationIds.Contains(t.DestinationLocationId.Value))) &&
                           t.Status != PickTaskStatus.Completed &&
                           t.Status != PickTaskStatus.ShortPicked &&
                           t.Status != PickTaskStatus.Cancelled,
                ct);
        if (hasOpenPicks)
        {
            throw new DomainException(
                $"Cannot deactivate zone '{zoneCode}' while open pick tasks reference its locations.",
                "ZONE_DEACTIVATION_BLOCKED_PICK");
        }

        var hasOpenCrossDock = await db.CrossDockTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.FacilityId == facilityId &&
                           (zoneLocationIds.Contains(t.SourceLocationId) ||
                            zoneLocationIds.Contains(t.DestinationLocationId) ||
                            (t.ActualDestinationLocationId.HasValue && zoneLocationIds.Contains(t.ActualDestinationLocationId.Value))) &&
                           t.Status != CrossDockTaskStatus.Completed &&
                           t.Status != CrossDockTaskStatus.Cancelled,
                ct);
        if (hasOpenCrossDock)
        {
            throw new DomainException(
                $"Cannot deactivate zone '{zoneCode}' while open cross-dock tasks reference its locations.",
                "ZONE_DEACTIVATION_BLOCKED_CROSSDOCK");
        }

        var hasOpenPacking = await db.PackingSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(ps => ps.FacilityId == facilityId &&
                            !ps.IsDeleted &&
                            zoneLocationIds.Contains(ps.PackingStationLocationId) &&
                            ps.Status != PackingSessionStatus.Completed &&
                            ps.Status != PackingSessionStatus.Aborted,
                ct);
        if (hasOpenPacking)
        {
            throw new DomainException(
                $"Cannot deactivate zone '{zoneCode}' while open packing sessions are active at stations in this zone.",
                "ZONE_DEACTIVATION_BLOCKED_PACKING");
        }

        var hasOpenCycleCounts = await db.CycleCounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(cc => cc.FacilityId == facilityId &&
                            cc.ZoneId == zoneId &&
                            cc.Status != CycleCountStatus.Completed &&
                            cc.Status != CycleCountStatus.Cancelled,
                ct);
        if (hasOpenCycleCounts)
        {
            throw new DomainException(
                $"Cannot deactivate zone '{zoneCode}' while open cycle counts target it.",
                "ZONE_DEACTIVATION_BLOCKED_CYCLE_COUNT");
        }

        var hasOpenCycleCountItems = await db.CycleCountItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(ci => zoneLocationIds.Contains(ci.LocationId) &&
                            !ci.IsReconciled &&
                            ci.CycleCount.FacilityId == facilityId &&
                            ci.CycleCount.Status != CycleCountStatus.Completed &&
                            ci.CycleCount.Status != CycleCountStatus.Cancelled,
                ct);
        if (hasOpenCycleCountItems)
        {
            throw new DomainException(
                $"Cannot deactivate zone '{zoneCode}' while unreconciled open cycle-count items reference its locations.",
                "ZONE_DEACTIVATION_BLOCKED_CYCLE_COUNT");
        }

        var hasActiveAllocations = await db.InventoryAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.FacilityId == facilityId &&
                           !a.IsDeleted &&
                           zoneLocationIds.Contains(a.LocationId) &&
                           a.Status != AllocationStatus.Picked &&
                           a.Status != AllocationStatus.Released &&
                           a.Status != AllocationStatus.Cancelled,
                ct);
        if (hasActiveAllocations)
        {
            throw new DomainException(
                $"Cannot deactivate zone '{zoneCode}' while active inventory allocations reference its locations.",
                "ZONE_DEACTIVATION_BLOCKED_ALLOCATION");
        }
    }

    public static async Task EnsureFacilityCanBeDeactivatedAsync(
        IApplicationDbContext db,
        Guid facilityId,
        string facilityCode,
        CancellationToken ct)
    {
        var hasAnyStock = await db.StockOnHand
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId)
            .AnyAsync(HasAnyStockExpression, ct);
        if (hasAnyStock)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while inventory exists in any stock bucket.",
                "FACILITY_DEACTIVATION_BLOCKED_STOCK");
        }

        var hasPresentHus = await db.HandlingUnits
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(hu => hu.FacilityId == facilityId && !hu.IsDeleted && hu.CurrentLocationId != null, ct);
        if (hasPresentHus)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while handling units are physically present.",
                "FACILITY_DEACTIVATION_BLOCKED_HU");
        }

        var hasOpenOrders = await db.OutboundOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(o => o.FacilityId == facilityId &&
                           !o.IsDeleted &&
                           o.Status != OutboundOrderStatus.Completed &&
                           o.Status != OutboundOrderStatus.Cancelled,
                ct);
        if (hasOpenOrders)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while open outbound orders exist.",
                "FACILITY_DEACTIVATION_BLOCKED_ORDERS");
        }

        var hasOpenPutaway = await db.PutawayTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.FacilityId == facilityId &&
                           t.Status != PutawayTaskStatus.Completed &&
                           t.Status != PutawayTaskStatus.Cancelled,
                ct);
        if (hasOpenPutaway)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while open putaway tasks exist.",
                "FACILITY_DEACTIVATION_BLOCKED_TASKS");
        }

        var hasOpenPicks = await db.PickTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.FacilityId == facilityId &&
                           !t.IsDeleted &&
                           t.Status != PickTaskStatus.Completed &&
                           t.Status != PickTaskStatus.ShortPicked &&
                           t.Status != PickTaskStatus.Cancelled,
                ct);
        if (hasOpenPicks)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while open pick tasks exist.",
                "FACILITY_DEACTIVATION_BLOCKED_PICK");
        }

        var hasOpenCrossDock = await db.CrossDockTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.FacilityId == facilityId &&
                           t.Status != CrossDockTaskStatus.Completed &&
                           t.Status != CrossDockTaskStatus.Cancelled,
                ct);
        if (hasOpenCrossDock)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while open cross-dock tasks exist.",
                "FACILITY_DEACTIVATION_BLOCKED_CROSSDOCK");
        }

        var hasOpenPacking = await db.PackingSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(ps => ps.FacilityId == facilityId &&
                            !ps.IsDeleted &&
                            ps.Status != PackingSessionStatus.Completed &&
                            ps.Status != PackingSessionStatus.Aborted,
                ct);
        if (hasOpenPacking)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while open packing sessions exist.",
                "FACILITY_DEACTIVATION_BLOCKED_PACKING");
        }

        // MDM21-F01: Open cycle counts and unreconciled open cycle-count items
        var hasOpenCycleCounts = await db.CycleCounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(cc => cc.FacilityId == facilityId &&
                            cc.Status != CycleCountStatus.Completed &&
                            cc.Status != CycleCountStatus.Cancelled,
                ct);
        if (hasOpenCycleCounts)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while open cycle counts exist.",
                "FACILITY_DEACTIVATION_BLOCKED_CYCLE_COUNT");
        }

        var hasOpenCycleCountItems = await db.CycleCountItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(ci => !ci.IsReconciled &&
                            ci.CycleCount.FacilityId == facilityId &&
                            ci.CycleCount.Status != CycleCountStatus.Completed &&
                            ci.CycleCount.Status != CycleCountStatus.Cancelled,
                ct);
        if (hasOpenCycleCountItems)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while unreconciled open cycle-count items exist.",
                "FACILITY_DEACTIVATION_BLOCKED_CYCLE_COUNT");
        }

        // MDM21-F01: Active inventory allocations
        var hasActiveAllocations = await db.InventoryAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.FacilityId == facilityId &&
                           !a.IsDeleted &&
                           a.Status != AllocationStatus.Picked &&
                           a.Status != AllocationStatus.Released &&
                           a.Status != AllocationStatus.Cancelled,
                ct);
        if (hasActiveAllocations)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while active inventory allocations exist.",
                "FACILITY_DEACTIVATION_BLOCKED_ALLOCATION");
        }

        // MDM21-F01: Explicit policy — facility deactivation rejects active subordinate warehouse locations
        // rather than silently cascading administrative state changes.
        var hasActiveLocations = await db.Locations
            .AsNoTracking()
            .AnyAsync(l => l.FacilityId == facilityId &&
                           !l.IsDeleted &&
                           l.Status == LocationStatus.Active,
                ct);
        if (hasActiveLocations)
        {
            throw new DomainException(
                $"Cannot deactivate facility '{facilityCode}' while active locations exist. Deactivate subordinate locations first.",
                "FACILITY_DEACTIVATION_BLOCKED_LOCATIONS");
        }
    }
}
