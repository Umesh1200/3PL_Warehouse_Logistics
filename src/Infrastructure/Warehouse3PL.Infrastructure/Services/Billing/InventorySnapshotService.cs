using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Services.Billing;

/// <summary>
/// Authoritative service implementation for capturing immutable daily inventory snapshots.
/// Freezes end-of-day balances and performs volumetric (Pallet, Carton, CBM) calculations with missing-dimension fallbacks.
/// DB-SPEC-3PL-2026 Phase 5.2 Recurring Storage &amp; Holding Fee Calculation Engine.
/// </summary>
public class InventorySnapshotService : IInventorySnapshotService
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<InventorySnapshotService> _logger;

    public InventorySnapshotService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider,
        ILogger<InventorySnapshotService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DailyInventorySnapshot> CaptureDailySnapshotAsync(
        Guid clientId,
        Guid facilityId,
        DateOnly snapshotDate,
        DateTime? snapshotTimestampUtc = null,
        Guid? userId = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        // 1. Idempotency Check: Exactly one snapshot per Client, Facility, and Date
        var existing = await _context.DailyInventorySnapshots
            .Include(s => s.Records)
            .FirstOrDefaultAsync(s =>
                s.ClientId == clientId &&
                s.FacilityId == facilityId &&
                s.SnapshotDate == snapshotDate, cancellationToken);

        if (existing != null)
        {
            _logger.LogInformation(
                "Idempotent daily snapshot detected for Client '{ClientId}', Facility '{FacilityId}', Date '{Date}'. Returning existing snapshot '{Id}'.",
                clientId, facilityId, snapshotDate, existing.Id);

            return existing;
        }

        // 2. Resolve Client Storage Billing Policy (Facility override -> Client-wide default -> Standard default)
        var policy = await _context.StorageBillingPolicies
            .FirstOrDefaultAsync(p => p.ClientId == clientId && p.FacilityId == facilityId, cancellationToken)
            ?? await _context.StorageBillingPolicies
                .FirstOrDefaultAsync(p => p.ClientId == clientId && p.FacilityId == null, cancellationToken)
            ?? StorageBillingPolicy.CreateDefault(clientId, facilityId);

        // 3. Query current inventory positions from StockOnHand
        var stockPositions = await _context.StockOnHand
            .Include(s => s.Item)
                .ThenInclude(i => i.PackagingUnits)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Include(s => s.HandlingUnit)
            .Where(s => s.ClientId == clientId && s.FacilityId == facilityId)
            .ToListAsync(cancellationToken);

        var snapshotUtc = snapshotTimestampUtc ?? DateTime.UtcNow;
        var snapshot = DailyInventorySnapshot.Create(clientId, facilityId, snapshotDate, snapshotUtc, notes);

        // 4. Transform StockOnHand into granular snapshot lines partitioned by status
        foreach (var stock in stockPositions)
        {
            if (stock.TotalQuantity <= 0)
            {
                continue;
            }

            var item = stock.Item;
            var locationCode = stock.Location?.LocationCode ?? "UNKNOWN";
            var sku = item?.Sku ?? "UNKNOWN-SKU";

            // Status partition mappings
            var partitions = new (InventoryHoldingStatus Status, decimal Qty)[]
            {
                (InventoryHoldingStatus.Available, stock.QtyAvailable),
                (InventoryHoldingStatus.Allocated, stock.QtyAllocated + stock.QtyReserved),
                (InventoryHoldingStatus.Damaged, stock.QtyDamaged),
                (InventoryHoldingStatus.Quarantined, stock.QtyQuarantined),
                (InventoryHoldingStatus.OnHold, stock.QtyOnHold)
            };

            foreach (var (holdingStatus, qty) in partitions)
            {
                if (qty <= 0)
                {
                    continue;
                }

                // A. Pallet Calculation
                var palletUnit = item?.PackagingUnits.FirstOrDefault(p => p.UnitLevel == PackagingUnitLevel.Pallet);
                var palletCount = 0m;
                if (stock.HandlingUnit != null && stock.HandlingUnit.UnitType == HandlingUnitType.Pallet)
                {
                    palletCount = 1.0m;
                }
                else if (palletUnit != null && palletUnit.ConversionQty > 0)
                {
                    palletCount = qty / palletUnit.ConversionQty;
                }
                else
                {
                    palletCount = qty / policy.FallbackUnitsPerPallet;
                }

                // B. Carton Calculation
                var cartonUnit = item?.PackagingUnits.FirstOrDefault(p =>
                    p.UnitLevel == PackagingUnitLevel.Case || p.UnitLevel == PackagingUnitLevel.InnerPack);
                var cartonCount = 0m;
                if (cartonUnit != null && cartonUnit.ConversionQty > 0)
                {
                    cartonCount = qty / cartonUnit.ConversionQty;
                }
                else
                {
                    cartonCount = qty / policy.FallbackUnitsPerCarton;
                }

                // C. CBM (Cubic Meters) Calculation
                var cbm = 0m;
                var hasMissingDimensions = false;

                if (item != null && item.VolumeCm3 > 0)
                {
                    var cbmPerUnit = item.VolumeCm3 / 1_000_000m;
                    cbm = qty * cbmPerUnit;
                }
                else if (item != null && item.LengthCm > 0 && item.WidthCm > 0 && item.HeightCm > 0)
                {
                    var cbmPerUnit = (item.LengthCm * item.WidthCm * item.HeightCm) / 1_000_000m;
                    cbm = qty * cbmPerUnit;
                }
                else
                {
                    // Guard against missing dimensions: never silently substitute zero
                    hasMissingDimensions = true;
                    cbm = qty * policy.FallbackUnitCbm;
                }

                // D. Policy Billability Qualification
                var isBillable = policy.IsBillable(holdingStatus);

                var record = DailyInventorySnapshotRecord.Create(
                    clientId: clientId,
                    dailyInventorySnapshotId: snapshot.Id,
                    facilityId: facilityId,
                    locationId: stock.LocationId,
                    locationCode: locationCode,
                    itemId: stock.ItemId,
                    sku: sku,
                    holdingStatus: holdingStatus,
                    quantity: qty,
                    palletCount: palletCount,
                    cartonCount: cartonCount,
                    cbm: cbm,
                    hasMissingDimensions: hasMissingDimensions,
                    isBillable: isBillable,
                    sourceInventoryReference: stock.Id.ToString(),
                    lotId: stock.LotId,
                    lotNumber: stock.Lot?.LotNumber,
                    handlingUnitId: stock.HandlingUnitId,
                    lpnCode: stock.LpnCode);

                snapshot.AddRecord(record);
            }
        }

        // 5. Seal and Finalize Snapshot
        snapshot.FinalizeSnapshot(userId, snapshotUtc);

        _context.DailyInventorySnapshots.Add(snapshot);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Captured and finalized DailyInventorySnapshot '{Id}' for Client '{ClientId}', Facility '{FacilityId}', Date '{Date}'. Records: {Records}, Billable Qty: {Qty}, Pallets: {Pallets:F2}, Cartons: {Cartons:F2}, CBM: {Cbm:F4}.",
            snapshot.Id, clientId, facilityId, snapshotDate, snapshot.RecordCount, snapshot.TotalBillableQuantity,
            snapshot.TotalBillablePalletCount, snapshot.TotalBillableCartonCount, snapshot.TotalBillableCbm);

        return snapshot;
    }

    /// <inheritdoc />
    public async Task<DailyInventorySnapshot?> GetSnapshotAsync(
        Guid clientId,
        Guid facilityId,
        DateOnly snapshotDate,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantAccess(clientId);

        return await _context.DailyInventorySnapshots
            .Include(s => s.Records)
            .FirstOrDefaultAsync(s =>
                s.ClientId == clientId &&
                s.FacilityId == facilityId &&
                s.SnapshotDate == snapshotDate, cancellationToken);
    }

    private void ValidateTenantAccess(Guid clientId)
    {
        if (_tenantProvider.TenantId.HasValue &&
            !_tenantProvider.BypassTenantFilter &&
            _tenantProvider.TenantId.Value != clientId)
        {
            throw new UnauthorizedException("Unauthorized cross-tenant inventory snapshot operation.");
        }
    }
}
