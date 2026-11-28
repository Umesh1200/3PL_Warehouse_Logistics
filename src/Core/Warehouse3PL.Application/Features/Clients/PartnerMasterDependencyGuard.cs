using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Clients;

/// <summary>
/// Centralized operational dependency guard for MDM-2.2 Client lifecycle transitions
/// (Suspended/Terminated/Deactivated) and Client-Facility Access suspension/revocation.
/// Reuses the 6-bucket StockOnHand predicate from <see cref="WarehouseInventoryDependencyGuard"/>.
/// </summary>
public static class PartnerMasterDependencyGuard
{
    public static async Task EnsureClientCanBeDeactivatedAsync(
        IApplicationDbContext db,
        Guid clientId,
        string accountCode,
        ClientStatus targetStatus,
        CancellationToken ct)
    {
        var hasStock = await db.StockOnHand
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.ClientId == clientId)
            .AnyAsync(WarehouseInventoryDependencyGuard.HasAnyStockExpression, ct);
        if (hasStock)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while inventory exists in any stock bucket.",
                "CLIENT_DEACTIVATION_BLOCKED_STOCK");
        }

        var hasPresentHus = await db.HandlingUnits
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(hu => hu.ClientId == clientId && !hu.IsDeleted && hu.CurrentLocationId != null, ct);
        if (hasPresentHus)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while handling units are physically present.",
                "CLIENT_DEACTIVATION_BLOCKED_HU");
        }

        var hasOpenOrders = await db.OutboundOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(o => o.ClientId == clientId &&
                           !o.IsDeleted &&
                           o.Status != OutboundOrderStatus.Completed &&
                           o.Status != OutboundOrderStatus.Cancelled,
                ct);
        if (hasOpenOrders)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while open outbound orders exist.",
                "CLIENT_DEACTIVATION_BLOCKED_ORDERS");
        }

        var hasOpenPos = await db.PurchaseOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(po => po.ClientId == clientId &&
                            !po.IsDeleted &&
                            po.Status != PurchaseOrderStatus.Closed &&
                            po.Status != PurchaseOrderStatus.Cancelled,
                ct);
        if (hasOpenPos)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while open purchase orders exist.",
                "CLIENT_DEACTIVATION_BLOCKED_PO");
        }

        var hasOpenAsns = await db.AdvanceShippingNotices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.ClientId == clientId &&
                           !a.IsDeleted &&
                           a.Status != AsnStatus.Received &&
                           a.Status != AsnStatus.Cancelled,
                ct);
        if (hasOpenAsns)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while open ASNs exist.",
                "CLIENT_DEACTIVATION_BLOCKED_ASN");
        }

        var hasOpenPutaway = await db.PutawayTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.ClientId == clientId &&
                           t.Status != PutawayTaskStatus.Completed &&
                           t.Status != PutawayTaskStatus.Cancelled,
                ct);
        if (hasOpenPutaway)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while open putaway tasks exist.",
                "CLIENT_DEACTIVATION_BLOCKED_TASKS");
        }

        var hasOpenPicks = await db.PickTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.ClientId == clientId &&
                           !t.IsDeleted &&
                           t.Status != PickTaskStatus.Completed &&
                           t.Status != PickTaskStatus.ShortPicked &&
                           t.Status != PickTaskStatus.Cancelled,
                ct);
        if (hasOpenPicks)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while open pick tasks exist.",
                "CLIENT_DEACTIVATION_BLOCKED_TASKS");
        }

        var hasOpenCrossDock = await db.CrossDockTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.ClientId == clientId &&
                           t.Status != CrossDockTaskStatus.Completed &&
                           t.Status != CrossDockTaskStatus.Cancelled,
                ct);
        if (hasOpenCrossDock)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while open cross-dock tasks exist.",
                "CLIENT_DEACTIVATION_BLOCKED_TASKS");
        }

        var hasOpenPacking = await db.PackingSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(ps => ps.ClientId == clientId &&
                            !ps.IsDeleted &&
                            ps.Status != PackingSessionStatus.Completed &&
                            ps.Status != PackingSessionStatus.Aborted,
                ct);
        if (hasOpenPacking)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while open packing sessions exist.",
                "CLIENT_DEACTIVATION_BLOCKED_PACKING");
        }

        var hasActiveAllocations = await db.InventoryAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.ClientId == clientId &&
                           !a.IsDeleted &&
                           a.Status != AllocationStatus.Picked &&
                           a.Status != AllocationStatus.Released &&
                           a.Status != AllocationStatus.Cancelled,
                ct);
        if (hasActiveAllocations)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while active inventory allocations exist.",
                "CLIENT_DEACTIVATION_BLOCKED_ALLOCATION");
        }

        var hasActiveFacilityAccess = await db.ClientFacilityAccesses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.ClientId == clientId && a.Status == FacilityAccessStatus.Active, ct);
        if (hasActiveFacilityAccess)
        {
            throw new DomainException(
                $"Cannot transition client '{accountCode}' to '{targetStatus}' while active facility accesses exist. Suspend or revoke facility assignments first.",
                "CLIENT_DEACTIVATION_BLOCKED_FACILITY_ACCESS");
        }
    }

    public static async Task EnsureClientFacilityAccessCanBeRestrictedAsync(
        IApplicationDbContext db,
        Guid clientId,
        Guid facilityId,
        FacilityAccessStatus targetStatus,
        CancellationToken ct)
    {
        var hasStock = await db.StockOnHand
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.ClientId == clientId && s.FacilityId == facilityId)
            .AnyAsync(WarehouseInventoryDependencyGuard.HasAnyStockExpression, ct);
        if (hasStock)
        {
            throw new DomainException(
                $"Cannot transition client facility access to '{targetStatus}' while client stock exists in this facility.",
                "CLIENT_ACCESS_BLOCKED_STOCK");
        }

        var hasPresentHus = await db.HandlingUnits
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(hu => hu.ClientId == clientId && hu.FacilityId == facilityId && !hu.IsDeleted && hu.CurrentLocationId != null, ct);
        if (hasPresentHus)
        {
            throw new DomainException(
                $"Cannot transition client facility access to '{targetStatus}' while client handling units are physically present in this facility.",
                "CLIENT_ACCESS_BLOCKED_HU");
        }

        var hasOpenOrders = await db.OutboundOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(o => o.ClientId == clientId &&
                           o.FacilityId == facilityId &&
                           !o.IsDeleted &&
                           o.Status != OutboundOrderStatus.Completed &&
                           o.Status != OutboundOrderStatus.Cancelled,
                ct);
        if (hasOpenOrders)
        {
            throw new DomainException(
                $"Cannot transition client facility access to '{targetStatus}' while open outbound orders exist in this facility.",
                "CLIENT_ACCESS_BLOCKED_ORDERS");
        }

        var hasOpenPos = await db.PurchaseOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(po => po.ClientId == clientId &&
                            po.FacilityId == facilityId &&
                            !po.IsDeleted &&
                            po.Status != PurchaseOrderStatus.Closed &&
                            po.Status != PurchaseOrderStatus.Cancelled,
                ct);
        if (hasOpenPos)
        {
            throw new DomainException(
                $"Cannot transition client facility access to '{targetStatus}' while open purchase orders exist in this facility.",
                "CLIENT_ACCESS_BLOCKED_PO");
        }

        var hasOpenAsns = await db.AdvanceShippingNotices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.ClientId == clientId &&
                           a.FacilityId == facilityId &&
                           !a.IsDeleted &&
                           a.Status != AsnStatus.Received &&
                           a.Status != AsnStatus.Cancelled,
                ct);
        if (hasOpenAsns)
        {
            throw new DomainException(
                $"Cannot transition client facility access to '{targetStatus}' while open ASNs exist in this facility.",
                "CLIENT_ACCESS_BLOCKED_ASN");
        }

        var hasOpenPutaway = await db.PutawayTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.ClientId == clientId &&
                           t.FacilityId == facilityId &&
                           t.Status != PutawayTaskStatus.Completed &&
                           t.Status != PutawayTaskStatus.Cancelled,
                ct);
        if (hasOpenPutaway)
        {
            throw new DomainException(
                $"Cannot transition client facility access to '{targetStatus}' while open putaway tasks exist in this facility.",
                "CLIENT_ACCESS_BLOCKED_TASKS");
        }

        var hasOpenPicks = await db.PickTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.ClientId == clientId &&
                           t.FacilityId == facilityId &&
                           !t.IsDeleted &&
                           t.Status != PickTaskStatus.Completed &&
                           t.Status != PickTaskStatus.ShortPicked &&
                           t.Status != PickTaskStatus.Cancelled,
                ct);
        if (hasOpenPicks)
        {
            throw new DomainException(
                $"Cannot transition client facility access to '{targetStatus}' while open pick tasks exist in this facility.",
                "CLIENT_ACCESS_BLOCKED_TASKS");
        }

        var hasOpenCrossDock = await db.CrossDockTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.ClientId == clientId &&
                           t.FacilityId == facilityId &&
                           t.Status != CrossDockTaskStatus.Completed &&
                           t.Status != CrossDockTaskStatus.Cancelled,
                ct);
        if (hasOpenCrossDock)
        {
            throw new DomainException(
                $"Cannot transition client facility access to '{targetStatus}' while open cross-dock tasks exist in this facility.",
                "CLIENT_ACCESS_BLOCKED_TASKS");
        }

        var hasOpenPacking = await db.PackingSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(ps => ps.ClientId == clientId &&
                            ps.FacilityId == facilityId &&
                            !ps.IsDeleted &&
                            ps.Status != PackingSessionStatus.Completed &&
                            ps.Status != PackingSessionStatus.Aborted,
                ct);
        if (hasOpenPacking)
        {
            throw new DomainException(
                $"Cannot transition client facility access to '{targetStatus}' while open packing sessions exist in this facility.",
                "CLIENT_ACCESS_BLOCKED_PACKING");
        }

        var hasActiveAllocations = await db.InventoryAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(a => a.ClientId == clientId &&
                           a.FacilityId == facilityId &&
                           !a.IsDeleted &&
                           a.Status != AllocationStatus.Picked &&
                           a.Status != AllocationStatus.Released &&
                           a.Status != AllocationStatus.Cancelled,
                ct);
        if (hasActiveAllocations)
        {
            throw new DomainException(
                $"Cannot transition client facility access to '{targetStatus}' while active inventory allocations exist in this facility.",
                "CLIENT_ACCESS_BLOCKED_ALLOCATION");
        }
    }
}
