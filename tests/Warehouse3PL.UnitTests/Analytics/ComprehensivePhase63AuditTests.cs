using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Final Comprehensive Audit &amp; Assurance Test Suite for Phase 6.3:
/// Inventory Intelligence, Spatial Analytics &amp; Slotting Optimization.
/// Covers:
/// - Scenario A: Inventory intelligence reconciliation across all 6 aging cohorts, space/weight occupancy, velocity, independent ABC Pareto calculation, and health dashboard.
/// - Scenario B: Slotting recommendation, individual verification of all 9 hard gates, 2D geometric non-overlap, and deterministic ranking with tie-breaking.
/// - Scenario C: Supervisory approval, rejection, and cancellation lifecycle with audit trail and prohibited bypass verification.
/// - Scenario D: Operational safety, cryptographic SHA-256 snapshotting across full persisted records, and 8-table ledger invariance (0 inserts/updates/deletes).
/// - Scenario E: Multi-tenant and facility security isolation with dashboard data confinement.
/// - Scenario F: Genuine event replay, checkpoint watermark invariance, deduplication, and projection recovery.
/// - Scenario G: Cross-phase spatial heatmaps, 3D topology coordinate integration, and executed negative benefit policy directives.
/// </summary>
public class ComprehensivePhase63AuditTests
{
    private class AuditFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();

        // Zones
        public Guid ForwardPickZoneId { get; set; } = Guid.NewGuid();
        public Guid ReserveZoneId { get; set; } = Guid.NewGuid();
        public Guid ColdZoneId { get; set; } = Guid.NewGuid();

        public Guid GoldenPickLocId { get; set; } = Guid.NewGuid();
        public Guid GoldenPickLoc2Id { get; set; } = Guid.NewGuid();
        public Guid UpperReserveLocId { get; set; } = Guid.NewGuid();
        public Guid ColdLocId { get; set; } = Guid.NewGuid();
    }

    private static ApplicationDbContext CreateContext(
        string dbName,
        Guid? tenantId = null,
        Guid? facilityId = null,
        bool isStaff = true,
        bool isSuperAdmin = false)
    {
        var currentUser = new TestCurrentUserContext
        {
            TenantId = tenantId,
            FacilityId = facilityId,
            UserType = isStaff ? UserType.Staff : UserType.Client,
            RolesList = isSuperAdmin ? ["SuperAdmin"] : [],
            PermissionsList = [Permissions.Analytics.SlottingView, Permissions.Analytics.SlottingManage, Permissions.Analytics.InventoryHealthView]
        };

        return TestDbContextFactory.Create(
            new TestTenantProvider { TenantId = tenantId, BypassTenantFilter = isStaff || isSuperAdmin },
            currentUser,
            dbName);
    }

    private static async Task<AuditFixture> SeedAuditEnvironmentAsync()
    {
        var fix = new AuditFixture();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);

        // Facilities
        var fac1 = new Facility { Id = fix.FacilityId, FacilityCode = "FAC-AUDIT-01", Name = "Primary National Distribution Center", IsActive = true };
        var fac2 = new Facility { Id = fix.Facility2Id, FacilityCode = "FAC-AUDIT-02", Name = "Regional Cross-Dock Facility", IsActive = true };
        context.Facilities.AddRange(fac1, fac2);

        // Clients
        var client1 = new Client { Id = fix.ClientId, CompanyName = "Enterprise Global Corp", AccountCode = "CLT-001", Status = ClientStatus.Active, Currency = "USD" };
        var client2 = new Client { Id = fix.Client2Id, CompanyName = "Apex Retailers Ltd", AccountCode = "CLT-002", Status = ClientStatus.Active, Currency = "USD" };
        context.Clients.AddRange(client1, client2);

        // Zones
        var pickZone = new WarehouseZone { Id = fix.ForwardPickZoneId, FacilityId = fix.FacilityId, ZoneCode = "ZONE-PICK", Name = "Forward Fast Pick", ZoneType = WarehouseZoneType.PickFace, TempMinC = 15m, TempMaxC = 25m, IsActive = true };
        var reserveZone = new WarehouseZone { Id = fix.ReserveZoneId, FacilityId = fix.FacilityId, ZoneCode = "ZONE-RES", Name = "High Bay Reserve", ZoneType = WarehouseZoneType.BulkStorage, TempMinC = 15m, TempMaxC = 25m, IsActive = true };
        var coldZone = new WarehouseZone { Id = fix.ColdZoneId, FacilityId = fix.FacilityId, ZoneCode = "ZONE-COLD", Name = "Cold Storage Bay", ZoneType = WarehouseZoneType.ColdStorage, TempMinC = -25m, TempMaxC = -15m, IsActive = true };
        context.WarehouseZones.AddRange(pickZone, reserveZone, coldZone);

        // Locations
        var locGolden = new Location
        {
            Id = fix.GoldenPickLocId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ForwardPickZoneId,
            LocationCode = "A-01-02",
            AisleCode = "A-01",
            RackCode = "R-01",
            ShelfLevel = 2,
            BinNumber = 1,
            XCoord = 10m,
            YCoord = 5m,
            ZCoord = 1.1m, // In Golden Zone (0.8m - 1.5m)
            LocationType = LocationType.Bin,
            IsPickable = true,
            IsActive = true,
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 140m,
            MaxWeightKg = 1500m,
            MaxVolumeCm3 = 2_000_000m
        };

        var locGolden2 = new Location
        {
            Id = fix.GoldenPickLoc2Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ForwardPickZoneId,
            LocationCode = "A-01-03",
            AisleCode = "A-01",
            RackCode = "R-02",
            ShelfLevel = 2,
            BinNumber = 2,
            XCoord = 10m, // Set identically to locGolden (10m, 5m, 1.1m) to verify deterministic location-code tie breaking under identical dock distance
            YCoord = 5m,
            ZCoord = 1.1m, // In Golden Zone (0.8m - 1.5m)
            LocationType = LocationType.Bin,
            IsPickable = true,
            IsActive = true,
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 140m,
            MaxWeightKg = 1500m,
            MaxVolumeCm3 = 2_000_000m
        };

        var locReserve = new Location
        {
            Id = fix.UpperReserveLocId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ReserveZoneId,
            LocationCode = "B-05-04",
            AisleCode = "B-05",
            RackCode = "R-04",
            ShelfLevel = 4,
            BinNumber = 1,
            XCoord = 45m,
            YCoord = 20m,
            ZCoord = 4.2m, // Upper reserve outside golden zone
            LocationType = LocationType.Rack,
            IsPickable = true,
            IsActive = true,
            LengthCm = 150m,
            WidthCm = 120m,
            HeightCm = 200m,
            MaxWeightKg = 2500m,
            MaxVolumeCm3 = 5_000_000m
        };

        var locCold = new Location
        {
            Id = fix.ColdLocId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ColdZoneId,
            LocationCode = "C-01-01",
            AisleCode = "C-01",
            RackCode = "R-01",
            ShelfLevel = 1,
            BinNumber = 1,
            XCoord = 60m,
            YCoord = 30m,
            ZCoord = 0.9m,
            LocationType = LocationType.Bin,
            IsPickable = true,
            IsActive = true,
            LengthCm = 100m,
            WidthCm = 100m,
            HeightCm = 120m,
            MaxWeightKg = 1000m,
            MaxVolumeCm3 = 1_500_000m
        };

        context.Locations.AddRange(locGolden, locGolden2, locReserve, locCold);
        await context.SaveChangesAsync();

        return fix;
    }

    private static string ComputeSha256<T>(IEnumerable<T> data)
    {
        var options = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            WriteIndented = false
        };
        var json = JsonSerializer.Serialize(data, options);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(json)));
    }

    #region Scenario A — Inventory Intelligence Reconciliation

    [Fact]
    public async Task ScenarioA_InventoryIntelligence_AgingVelocityAbcSpaceAndHealth_ReconcilesConsistently()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var asOf = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

        var queryService = new AnalyticsQueryService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<AnalyticsQueryService>.Instance);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // 1. Seed Catalog Items & Stock covering all 6 specified aging cohorts and ABC classes
        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-AUDIT-A", Description = "Fast Moving Class A Item", UnitPrice = 500m, WeightKg = 4m, LengthCm = 20m, WidthCm = 20m, HeightCm = 20m, AbcVelocity = AbcVelocity.A, Status = ItemStatus.Active };
        itemA.RecalculateVolume();
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-AUDIT-B", Description = "Medium Moving Class B Item", UnitPrice = 150m, WeightKg = 8m, LengthCm = 30m, WidthCm = 30m, HeightCm = 20m, AbcVelocity = AbcVelocity.B, Status = ItemStatus.Active };
        itemB.RecalculateVolume();
        var itemC = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-AUDIT-C", Description = "Slow Moving Class C Item", UnitPrice = 250m, WeightKg = 2m, LengthCm = 15m, WidthCm = 15m, HeightCm = 10m, AbcVelocity = AbcVelocity.C, Status = ItemStatus.Active };
        itemC.RecalculateVolume();
        var itemD = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-AUDIT-D", Description = "Aging Class B Item", UnitPrice = 300m, WeightKg = 5m, LengthCm = 25m, WidthCm = 25m, HeightCm = 15m, AbcVelocity = AbcVelocity.B, Status = ItemStatus.Inactive };
        itemD.RecalculateVolume();
        var itemE = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-AUDIT-E", Description = "Slow Moving Class C Item", UnitPrice = 300m, WeightKg = 4m, LengthCm = 20m, WidthCm = 20m, HeightCm = 15m, AbcVelocity = AbcVelocity.C, Status = ItemStatus.Inactive };
        itemE.RecalculateVolume();
        var itemF = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-AUDIT-F", Description = "Dormant Class C Item", UnitPrice = 300m, WeightKg = 4m, LengthCm = 20m, WidthCm = 20m, HeightCm = 15m, AbcVelocity = AbcVelocity.C, Status = ItemStatus.Inactive };
        itemF.RecalculateVolume();

        context.Items.AddRange(itemA, itemB, itemC, itemD, itemE, itemF);

        // Physical Stock Distribution:
        // locGolden: 70 units itemA (280 kg) + 15 units itemB (120 kg) = 400 kg total
        // locReserve: 25 units itemB (200 kg) + 20 units itemD (100 kg) + 15 units itemE (60 kg) + 15 units itemF (60 kg) + 10 units itemC (20 kg) = 440 kg total
        // Total facility units: 70 + 15 + 25 + 20 + 15 + 15 + 10 = 170 units.
        // Total facility weight: 400 kg + 440 kg = 840 kg.
        context.StockOnHand.AddRange(
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemA.Id, LocationId = fix.GoldenPickLocId, QtyAvailable = 70m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemB.Id, LocationId = fix.GoldenPickLocId, QtyAvailable = 15m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemB.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 25m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemD.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 20m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemE.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 15m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemF.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 15m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemC.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 10m }
        );

        // 2. Seed All 6 Specified Aging Cohorts:
        // Current_0_30: 70 units * $500 = $35,000
        // Aging_31_60: 40 units * $150 = $6,000
        // Aging_61_90: 20 units * $300 = $6,000
        // SlowMoving_91_180: 15 units * $300 = $4,500
        // Dormant_181_365: 15 units * $300 = $4,500
        // DeadStock_365Plus: 10 units * $250 = $2,500
        // Total Units = 170, Total Valuation = $58,500
        context.DailyInventoryAgingSnapshots.AddRange(
            new DailyInventoryAgingSnapshot
            {
                Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemA.Id, Sku = itemA.Sku, LocationId = fix.GoldenPickLocId,
                SnapshotDateUtc = asOf, QuantityOnHand = 70m, QtyAvailable = 70m, TotalInventoryValue = 35000m, UnitCost = 500m,
                AgingBucket = InventoryAgingBucket.Current_0_30, ReceiptAgeDays = 10, LastMovementAgeDays = 2, LastDemandAgeDays = 1,
                OccupiedCbm = 0.56m, PalletFootprint = 1.0m
            },
            new DailyInventoryAgingSnapshot
            {
                Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemB.Id, Sku = itemB.Sku, LocationId = fix.GoldenPickLocId,
                SnapshotDateUtc = asOf, QuantityOnHand = 40m, QtyAvailable = 40m, TotalInventoryValue = 6000m, UnitCost = 150m,
                AgingBucket = InventoryAgingBucket.Aging_31_60, ReceiptAgeDays = 45, LastMovementAgeDays = 12, LastDemandAgeDays = 10,
                OccupiedCbm = 0.72m, PalletFootprint = 1.0m
            },
            new DailyInventoryAgingSnapshot
            {
                Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemD.Id, Sku = itemD.Sku, LocationId = fix.UpperReserveLocId,
                SnapshotDateUtc = asOf, QuantityOnHand = 20m, QtyAvailable = 20m, TotalInventoryValue = 6000m, UnitCost = 300m,
                AgingBucket = InventoryAgingBucket.Aging_61_90, ReceiptAgeDays = 75, LastMovementAgeDays = 30, LastDemandAgeDays = 25,
                OccupiedCbm = 0.19m, PalletFootprint = 0.5m
            },
            new DailyInventoryAgingSnapshot
            {
                Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemE.Id, Sku = itemE.Sku, LocationId = fix.UpperReserveLocId,
                SnapshotDateUtc = asOf, QuantityOnHand = 15m, QtyAvailable = 15m, TotalInventoryValue = 4500m, UnitCost = 300m,
                AgingBucket = InventoryAgingBucket.SlowMoving_91_180, ReceiptAgeDays = 120, LastMovementAgeDays = 95, LastDemandAgeDays = 90,
                IsSlowMoving = true, OccupiedCbm = 0.09m, PalletFootprint = 0.5m
            },
            new DailyInventoryAgingSnapshot
            {
                Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemF.Id, Sku = itemF.Sku, LocationId = fix.UpperReserveLocId,
                SnapshotDateUtc = asOf, QuantityOnHand = 15m, QtyAvailable = 15m, TotalInventoryValue = 4500m, UnitCost = 300m,
                AgingBucket = InventoryAgingBucket.Dormant_181_365, ReceiptAgeDays = 240, LastMovementAgeDays = 200, LastDemandAgeDays = 190,
                IsSlowMoving = true, OccupiedCbm = 0.09m, PalletFootprint = 0.5m
            },
            new DailyInventoryAgingSnapshot
            {
                Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemC.Id, Sku = itemC.Sku, LocationId = fix.UpperReserveLocId,
                SnapshotDateUtc = asOf, QuantityOnHand = 10m, QtyAvailable = 10m, TotalInventoryValue = 2500m, UnitCost = 250m,
                AgingBucket = InventoryAgingBucket.DeadStock_365Plus, ReceiptAgeDays = 400, LastMovementAgeDays = 370, LastDemandAgeDays = 365,
                IsSlowMoving = true, IsDeadStock = true, OccupiedCbm = 0.03m, PalletFootprint = 0.5m
            }
        );

        // 3. Seed Space Utilization & Weight Occupancy Matching Physical Items
        // Golden location: Max 1500 kg, Occupied 400 kg (26.67%)
        // Reserve location: Max 2500 kg, Occupied 440 kg (17.6%)
        // Total physical weight = 840 kg, Total facility max weight = 4000 kg (21.0% facility weight utilization)
        context.DailyLocationSpaceUtilizationRecords.AddRange(
            new DailyLocationSpaceUtilizationRecord
            {
                Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ZoneId = fix.ReserveZoneId, LocationId = fix.UpperReserveLocId,
                SnapshotDateUtc = asOf, PhysicalCubicMeters = 5.0m, UsableCubicMeters = 4.5m, OccupiedCubicMeters = 2.5m, AvailableCubicMeters = 2.0m,
                CubeUtilizationPercent = 55.56m, PhysicalPalletPositions = 2, UsablePalletPositions = 2, OccupiedPalletPositions = 1, AvailablePalletPositions = 1,
                PalletOccupancyPercent = 50m, MaxWeightKg = 2500m, OccupiedWeightKg = 440m
            },
            new DailyLocationSpaceUtilizationRecord
            {
                Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ZoneId = fix.ForwardPickZoneId, LocationId = fix.GoldenPickLocId,
                SnapshotDateUtc = asOf, PhysicalCubicMeters = 2.0m, UsableCubicMeters = 1.8m, OccupiedCubicMeters = 1.2m, AvailableCubicMeters = 0.6m,
                CubeUtilizationPercent = 66.67m, PhysicalPalletPositions = 1, UsablePalletPositions = 1, OccupiedPalletPositions = 1, AvailablePalletPositions = 0,
                PalletOccupancyPercent = 100m, MaxWeightKg = 1500m, OccupiedWeightKg = 400m
            }
        );

        // 4. Seed Raw Unclassified Movement Event Logs (160 units @ $500 = $80k, 100 units @ $150 = $15k, 20 units @ $250 = $5k)
        context.InventoryMovementEventLogs.AddRange(
            InventoryMovementEventLog.Create(
                Guid.NewGuid(), fix.FacilityId, fix.ClientId, itemA.Id, itemA.Sku,
                InventoryMovementEventType.OutboundPick, 160m, asOf.AddDays(-10),
                fix.GoldenPickLocId, null, fix.ForwardPickZoneId, 500m, "ORD-AUDIT-A", isCanonicalDemand: true),
            InventoryMovementEventLog.Create(
                Guid.NewGuid(), fix.FacilityId, fix.ClientId, itemB.Id, itemB.Sku,
                InventoryMovementEventType.OutboundPick, 100m, asOf.AddDays(-15),
                fix.GoldenPickLocId, null, fix.ForwardPickZoneId, 150m, "ORD-AUDIT-B", isCanonicalDemand: true),
            InventoryMovementEventLog.Create(
                Guid.NewGuid(), fix.FacilityId, fix.ClientId, itemC.Id, itemC.Sku,
                InventoryMovementEventType.OutboundPick, 20m, asOf.AddDays(-20),
                fix.UpperReserveLocId, null, fix.ReserveZoneId, 250m, "ORD-AUDIT-C", isCanonicalDemand: true)
        );

        // Also seed operational velocity records for summary rollups
        context.SkuVelocityClassificationRecords.AddRange(
            new SkuVelocityClassificationRecord
            {
                Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = itemA.Id, Sku = itemA.Sku,
                AbcClass = AbcClassificationCategory.ClassA, VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
                ObservationWindow = VelocityObservationWindow.Days90, CalculationPeriodStartUtc = asOf.AddDays(-90), CalculationPeriodEndUtc = asOf,
                CalculatedAtUtc = asOf, TotalPickTasksCount = 120, TotalUnitsDemanded = 500m, TotalDemandValue = 80000m, RecordType = SkuClassificationRecordType.VelocityOperational
            },
            new SkuVelocityClassificationRecord
            {
                Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = itemB.Id, Sku = itemB.Sku,
                AbcClass = AbcClassificationCategory.ClassB, VelocityCategory = InventoryMovementVelocityCategory.MediumMoving,
                ObservationWindow = VelocityObservationWindow.Days90, CalculationPeriodStartUtc = asOf.AddDays(-90), CalculationPeriodEndUtc = asOf,
                CalculatedAtUtc = asOf, TotalPickTasksCount = 35, TotalUnitsDemanded = 70m, TotalDemandValue = 15000m, RecordType = SkuClassificationRecordType.VelocityOperational
            },
            new SkuVelocityClassificationRecord
            {
                Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = itemC.Id, Sku = itemC.Sku,
                AbcClass = AbcClassificationCategory.ClassC, VelocityCategory = InventoryMovementVelocityCategory.SlowMoving,
                ObservationWindow = VelocityObservationWindow.Days90, CalculationPeriodStartUtc = asOf.AddDays(-90), CalculationPeriodEndUtc = asOf,
                CalculatedAtUtc = asOf, TotalPickTasksCount = 2, TotalUnitsDemanded = 5m, TotalDemandValue = 5000m, RecordType = SkuClassificationRecordType.VelocityOperational
            }
        );

        await context.SaveChangesAsync();

        // 5. EXECUTE THE ACTUAL ABC CLASSIFICATION ENGINE ON UNCLASSIFIED INPUTS
        var abcBatchResult = await projectionService.CalculateAbcClassificationBatchAsync(
            fix.FacilityId,
            fix.ClientId,
            criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue,
            windowOverride: VelocityObservationWindow.Days365,
            asOfUtc: asOf);

        Assert.NotNull(abcBatchResult);
        Assert.Equal(3, abcBatchResult.RankedSkus.Count);

        // Independent classification verification for SKU A: ACV $80k -> 80.00% cum -> Class A
        var rank1 = abcBatchResult.RankedSkus[0];
        Assert.Equal(itemA.Sku, rank1.Sku);
        Assert.Equal(80000m, rank1.AnnualizedConsumptionValue);
        Assert.Equal(80.00m, rank1.CumulativeContributionPercent);
        Assert.Equal(AbcClassificationCategory.ClassA, rank1.AbcClass);

        // Independent classification verification for SKU B: ACV $15k -> 95.00% cum -> Class B
        var rank2 = abcBatchResult.RankedSkus[1];
        Assert.Equal(itemB.Sku, rank2.Sku);
        Assert.Equal(15000m, rank2.AnnualizedConsumptionValue);
        Assert.Equal(95.00m, rank2.CumulativeContributionPercent);
        Assert.Equal(AbcClassificationCategory.ClassB, rank2.AbcClass);

        // Independent classification verification for SKU C: ACV $5k -> 100.00% cum -> Class C
        var rank3 = abcBatchResult.RankedSkus[2];
        Assert.Equal(itemC.Sku, rank3.Sku);
        Assert.Equal(5000m, rank3.AnnualizedConsumptionValue);
        Assert.Equal(100.00m, rank3.CumulativeContributionPercent);
        Assert.Equal(AbcClassificationCategory.ClassC, rank3.AbcClass);

        // Batch summary complete partition proof:
        Assert.Equal(1, abcBatchResult.Summary.ClassACount);
        Assert.Equal(1, abcBatchResult.Summary.ClassBCount);
        Assert.Equal(1, abcBatchResult.Summary.ClassCCount);
        Assert.Equal(3, abcBatchResult.Summary.TotalSkus);
        Assert.Equal(100000m, abcBatchResult.Summary.TotalPortfolioValue);
        Assert.Equal(80.00m, abcBatchResult.Summary.ClassAValuePercent);
        Assert.Equal(15.00m, abcBatchResult.Summary.ClassBValuePercent);
        Assert.Equal(5.00m, abcBatchResult.Summary.ClassCValuePercent);

        // 6. RECONCILE ACROSS ALL SUBSYSTEMS

        // A. Aging Cohort Verification across all 6 specified buckets
        var agingRecords = await queryService.GetDailyInventoryAgingRecordsAsync(fix.FacilityId, asOf, fix.ClientId);
        Assert.Equal(6, agingRecords.Count);
        foreach (var bucket in Enum.GetValues<InventoryAgingBucket>())
        {
            Assert.Contains(agingRecords, r => r.AgingBucket == bucket);
        }
        var totalOnHandQty = agingRecords.Sum(r => r.QuantityOnHand);
        Assert.Equal(170m, totalOnHandQty);
        var totalValuation = agingRecords.Sum(r => r.TotalInventoryValue);
        Assert.Equal(58500m, totalValuation);

        // B. Weight Occupancy & Space Utilization Reconciliation
        var spaceSummary = await queryService.GetFacilitySpaceUtilizationSummaryAsync(fix.FacilityId, asOf);
        Assert.NotNull(spaceSummary);
        Assert.True(spaceSummary.CubeFillPercent > 50m || spaceSummary.PalletOccupancyPercent > 50m);
        Assert.Equal(840m, spaceSummary.TotalOccupiedWeightKg);
        Assert.Equal(4000m, spaceSummary.TotalMaxWeightKg);
        Assert.Equal(21.0m, spaceSummary.WeightUtilizationPercent);

        // C. Velocity & ABC Pareto Verification
        var velocitySummary = await queryService.GetSkuVelocitySummaryAsync(fix.FacilityId, fix.ClientId);
        Assert.NotNull(velocitySummary);
        Assert.Equal(3, velocitySummary.TotalTrackedSkus);
        Assert.Equal(1, velocitySummary.FastMovingCount);
        Assert.Equal(1, velocitySummary.MediumMovingCount);
        Assert.Equal(1, velocitySummary.SlowMovingCount);

        // D. Unified Health Dashboard Consolidation
        var unifiedHealth = await queryService.GetUnifiedInventoryHealthDashboardAsync(fix.FacilityId, fix.ClientId, asOf);
        Assert.NotNull(unifiedHealth);
        Assert.Equal(170m, unifiedHealth.TotalUnitsOnHand);
        Assert.Equal(58500m, unifiedHealth.TotalInventoryValuation);
        Assert.Equal(6, unifiedHealth.TotalActiveSkusCount);
        Assert.True(unifiedHealth.SpaceCapacity.CubeUtilizationPercent > 50m);
        Assert.Equal(2, unifiedHealth.SpaceCapacity.TotalLocationsEvaluated);
        Assert.Equal(1, unifiedHealth.ClassASkuCount);
        Assert.True(unifiedHealth.ClassAValuePercent > 50m);
        Assert.NotNull(unifiedHealth.AbcPareto);
        Assert.Equal(1, unifiedHealth.AbcPareto.ClassACount);
        Assert.True(unifiedHealth.OverallHealthScore >= 0m && unifiedHealth.OverallHealthScore <= 100m);
        Assert.NotNull(unifiedHealth.HealthScoreGrade);

        // E. Detailed ABC Pareto Category & Partition Verification via Query Service
        var abcAnalysis = await queryService.GetAbcClassificationSummaryAsync(fix.FacilityId, fix.ClientId);
        Assert.NotNull(abcAnalysis);
        Assert.Equal(1, abcAnalysis.ClassACount);
        Assert.Equal(1, abcAnalysis.ClassBCount);
        Assert.Equal(1, abcAnalysis.ClassCCount);
        Assert.Equal(3, abcAnalysis.TotalSkus);
        Assert.Equal(abcAnalysis.TotalSkus, abcAnalysis.ClassACount + abcAnalysis.ClassBCount + abcAnalysis.ClassCCount);
        Assert.Equal(80.00m, abcAnalysis.ClassAThresholdPercent);
        Assert.Equal(95.00m, abcAnalysis.ClassBThresholdPercent);
        Assert.Equal(80.00m, abcAnalysis.ClassAValuePercent);
        Assert.Equal(15.00m, abcAnalysis.ClassBValuePercent);
        Assert.Equal(5.00m, abcAnalysis.ClassCValuePercent);
        Assert.Equal(100000m, abcAnalysis.TotalPortfolioValue);

        // Cumulative Pareto boundary verification
        decimal cumShareA = abcAnalysis.ClassAValuePercent;
        decimal cumShareB = cumShareA + abcAnalysis.ClassBValuePercent;
        decimal cumShareC = cumShareB + abcAnalysis.ClassCValuePercent;
        Assert.True(cumShareA <= abcAnalysis.ClassAThresholdPercent);
        Assert.True(cumShareB <= abcAnalysis.ClassBThresholdPercent);
        Assert.Equal(100.00m, cumShareC);

        Assert.Equal(1, unifiedHealth.AbcPareto.ClassACount);
        Assert.Equal(1, unifiedHealth.AbcPareto.ClassBCount);
        Assert.Equal(1, unifiedHealth.AbcPareto.ClassCCount);
        Assert.Equal(3, unifiedHealth.AbcPareto.TotalEvaluatedSkus);
        Assert.Equal(80.00m, unifiedHealth.AbcPareto.ClassAValuePercent);
    }

    #endregion

    #region Scenario B — Slotting Recommendation, All Nine Hard Gates & Geometric Non-Overlap

    [Fact]
    public async Task ScenarioB_SlottingRecommendation_EvaluatesHardGates_AndEnforcesDeterministicRanking()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var evalTime = new DateTime(2026, 11, 14, 14, 0, 0, DateTimeKind.Utc);

        var slottingService = new SlottingOptimizationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<SlottingOptimizationService>.Instance);

        var relocationService = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var policy = SlottingPolicy.Create("MultiFactorAuditPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        // -------------------------------------------------------------
        // PART 1: INDIVIDUAL EVALUATION & REJECTION OF ALL 9 HARD GATES
        // -------------------------------------------------------------

        // Gate Items & Entities Setup
        var itemHeavy = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-GATE1-WEIGHT", WeightKg = 3000m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m };
        var itemHugeVol = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-GATE2-VOLUME", WeightKg = 5m, LengthCm = 110m, WidthCm = 90m, HeightCm = 130m, VolumeCm3 = 2_500_000m };
        var itemOversizedDim = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-GATE3-DIM", WeightKg = 5m, LengthCm = 150m, WidthCm = 150m, HeightCm = 50m };
        var itemTall = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-GATE4-HEIGHT", WeightKg = 5m, LengthCm = 50m, WidthCm = 50m, HeightCm = 160m }; // Loc height is 140cm
        var itemHaz = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-GATE5-HAZ", WeightKg = 5m, LengthCm = 20m, WidthCm = 20m, HeightCm = 20m, HazardClass = "Class-3-Flammable" };
        var itemFrozen = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-GATE6-TEMP", WeightKg = 5m, LengthCm = 20m, WidthCm = 20m, HeightCm = 20m, StorageTempClass = StorageTempClass.Frozen };
        var itemStackOverflow = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-GATE7-STACK", WeightKg = 1m, LengthCm = 30m, WidthCm = 30m, HeightCm = 20m, StackabilityFactor = 1 };
        var locDamaged = new Location { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, LocationCode = "DAMAGED-01", IsActive = true, IsPickable = true, IsDamaged = true, MaxWeightKg = 1000m };
        var itemClient2 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client2Id, Sku = "SKU-GATE9-TENANT2", WeightKg = 5m, LengthCm = 20m, WidthCm = 20m, HeightCm = 20m };

        context.Items.AddRange(itemHeavy, itemHugeVol, itemOversizedDim, itemTall, itemHaz, itemFrozen, itemStackOverflow, itemClient2);
        context.Locations.Add(locDamaged);
        context.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = itemHeavy.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 5m });
        await context.SaveChangesAsync();

        // Gate 1: Maximum Structural Weight Capacity Rating
        var resWeight = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemHeavy.Id, fix.GoldenPickLocId, policy.Id, 1m, evalTime);
        Assert.False(resWeight.IsOverallCompatible);
        Assert.Contains(resWeight.ViolatedHardConstraints, v => v.Contains("weight"));
        Assert.Equal(0m, resWeight.CalculatedSuitabilityScore); // Gate supremacy

        // Gate 2: Usable Volumetric Capacity Rating
        var resVol = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemHugeVol.Id, fix.GoldenPickLocId, policy.Id, 1m, evalTime);
        Assert.False(resVol.IsOverallCompatible);
        Assert.Contains(resVol.ViolatedHardConstraints, v => v.Contains("volume") || v.Contains("capacity"));
        Assert.Equal(0m, resVol.CalculatedSuitabilityScore);

        // Gate 3: Physical Planar Dimensions Boundary Fit
        var resDim = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemOversizedDim.Id, fix.GoldenPickLocId, policy.Id, 1m, evalTime);
        Assert.False(resDim.IsOverallCompatible);
        Assert.Contains(resDim.ViolatedHardConstraints, v => v.Contains("dimensions") || v.Contains("boundaries"));
        Assert.Equal(0m, resDim.CalculatedSuitabilityScore);

        // Gate 4: Vertical Shelf Clearance / Height Fit
        var resHeight = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemTall.Id, fix.GoldenPickLocId, policy.Id, 1m, evalTime);
        Assert.False(resHeight.IsOverallCompatible);
        Assert.Contains(resHeight.ViolatedHardConstraints, v => v.Contains("dimensions") || v.Contains("boundaries"));
        Assert.Equal(0m, resHeight.CalculatedSuitabilityScore);

        // Gate 5: Hazardous Materials (HazMat) Segregation
        var resHaz = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemHaz.Id, fix.GoldenPickLocId, policy.Id, 1m, evalTime);
        Assert.False(resHaz.IsOverallCompatible);
        Assert.Contains(resHaz.ViolatedHardConstraints, v => v.Contains("Hazardous"));
        Assert.Equal(0m, resHaz.CalculatedSuitabilityScore);

        // Gate 6: Environmental Temperature Zone Compatibility
        var resTemp = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemFrozen.Id, fix.GoldenPickLocId, policy.Id, 1m, evalTime);
        Assert.False(resTemp.IsOverallCompatible);
        Assert.Contains(resTemp.ViolatedHardConstraints, v => v.Contains("Frozen") || v.Contains("temperature"));
        Assert.Equal(0m, resTemp.CalculatedSuitabilityScore);

        // Gate 7: Planar Stacking & Floor Footprint Overflow
        var resStack = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemStackOverflow.Id, fix.GoldenPickLocId, policy.Id, 50m, evalTime); // 50 non-stackable boxes cannot fit on 12-item floor
        Assert.False(resStack.IsOverallCompatible);
        Assert.Contains(resStack.ViolatedHardConstraints, v => v.Contains("footprint") || v.Contains("Stackability"));
        Assert.Equal(0m, resStack.CalculatedSuitabilityScore);

        // Gate 8: Operational Location Status (Damaged / Inactive / Non-storage Area)
        var resDamaged = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemFrozen.Id, locDamaged.Id, policy.Id, 1m, evalTime);
        Assert.False(resDamaged.IsOverallCompatible);
        Assert.Contains(resDamaged.ViolatedHardConstraints, v => v.Contains("damaged"));
        Assert.Equal(0m, resDamaged.CalculatedSuitabilityScore);

        // Gate 9: Client Tenancy Exclusivity & Co-Mingling Prohibition
        var resTenancy = await slottingService.EvaluateSkuLocationCompatibilityAsync(itemClient2.Id, fix.UpperReserveLocId, policy.Id, 1m, evalTime);
        Assert.False(resTenancy.IsOverallCompatible);
        Assert.Contains(resTenancy.ViolatedHardConstraints, v => v.Contains("co-mingling") || v.Contains("tenant"));
        Assert.Equal(0m, resTenancy.CalculatedSuitabilityScore);

        // -------------------------------------------------------------
        // PART 2: 2D GEOMETRIC NON-OVERLAP ENGINE VERIFICATION
        // -------------------------------------------------------------
        // Case A: Geometric Overlap Rejection (TryPackItem returns false on overflow)
        var nonStackBox = new Item { Id = Guid.NewGuid(), LengthCm = 40m, WidthCm = 40m, HeightCm = 40m, StackabilityFactor = 1, AllowPlanarRotation = false };
        bool canFitOverflowBoxes = GeometricOccupancyEngine.TryPackItem(120m, 100m, 140m, [], nonStackBox, 20m, out var overflowPlacements);
        Assert.False(canFitOverflowBoxes); // Method returns false indicating packing failed/rejected
        Assert.Empty(overflowPlacements);  // Rejection produces 0 accepted placements

        // Case B: Valid Non-Overlapping Placement (TryPackItem returns true on success)
        var smallBox = new Item { Id = Guid.NewGuid(), LengthCm = 25m, WidthCm = 25m, HeightCm = 25m, StackabilityFactor = 1, AllowPlanarRotation = true };
        bool canFitSmallBoxes = GeometricOccupancyEngine.TryPackItem(120m, 100m, 140m, [], smallBox, 2m, out var validPlacements);
        Assert.True(canFitSmallBoxes);      // Method returns true indicating all units placed cleanly
        Assert.Equal(2, validPlacements.Count);
        var p0 = validPlacements[0];
        var p1 = validPlacements[1];
        Assert.False(p0.Overlaps(p1));      // Symmetric reciprocal non-overlap asserted
        Assert.False(p1.Overlaps(p0));

        // -------------------------------------------------------------
        // PART 3: DETERMINISTIC MULTI-FACTOR RANKING & TIE-BREAKING
        // -------------------------------------------------------------
        var fastItem1 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FAST-MOVE-1", Description = "Fast item 1 trapped in reserve", WeightKg = 5m, LengthCm = 25m, WidthCm = 25m, HeightCm = 25m, AbcVelocity = AbcVelocity.A, StackabilityFactor = 2 };
        fastItem1.RecalculateVolume();
        var fastItem2 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-FAST-MOVE-2", Description = "Fast item 2 trapped in reserve", WeightKg = 5m, LengthCm = 25m, WidthCm = 25m, HeightCm = 25m, AbcVelocity = AbcVelocity.A, StackabilityFactor = 2 };
        fastItem2.RecalculateVolume();

        // Seed deliberate tie items: identical dimensions, velocity, pick count, and demand value
        var tieItemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-TIE-AAA", Description = "Tie Item AAA", WeightKg = 5m, LengthCm = 20m, WidthCm = 20m, HeightCm = 20m, AbcVelocity = AbcVelocity.A, StackabilityFactor = 2 };
        tieItemA.RecalculateVolume();
        var tieItemZ = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-TIE-ZZZ", Description = "Tie Item ZZZ", WeightKg = 5m, LengthCm = 20m, WidthCm = 20m, HeightCm = 20m, AbcVelocity = AbcVelocity.A, StackabilityFactor = 2 };
        tieItemZ.RecalculateVolume();

        context.Items.AddRange(fastItem1, fastItem2, tieItemA, tieItemZ);

        context.StockOnHand.AddRange(
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = fastItem1.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 2m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = fastItem2.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 2m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = tieItemA.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 2m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = tieItemZ.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 2m }
        );

        context.SkuVelocityClassificationRecords.AddRange(
            new SkuVelocityClassificationRecord
            {
                Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = fastItem1.Id, Sku = fastItem1.Sku,
                AbcClass = AbcClassificationCategory.ClassA, VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
                ObservationWindow = VelocityObservationWindow.Days90, CalculationPeriodStartUtc = evalTime.AddDays(-90), CalculationPeriodEndUtc = evalTime,
                CalculatedAtUtc = evalTime, TotalPickTasksCount = 150, TotalUnitsDemanded = 600m, TotalDemandValue = 30000m
            },
            new SkuVelocityClassificationRecord
            {
                Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = fastItem2.Id, Sku = fastItem2.Sku,
                AbcClass = AbcClassificationCategory.ClassA, VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
                ObservationWindow = VelocityObservationWindow.Days90, CalculationPeriodStartUtc = evalTime.AddDays(-90), CalculationPeriodEndUtc = evalTime,
                CalculatedAtUtc = evalTime, TotalPickTasksCount = 100, TotalUnitsDemanded = 400m, TotalDemandValue = 20000m
            },
            new SkuVelocityClassificationRecord
            {
                Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = tieItemA.Id, Sku = tieItemA.Sku,
                AbcClass = AbcClassificationCategory.ClassA, VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
                ObservationWindow = VelocityObservationWindow.Days90, CalculationPeriodStartUtc = evalTime.AddDays(-90), CalculationPeriodEndUtc = evalTime,
                CalculatedAtUtc = evalTime, TotalPickTasksCount = 80, TotalUnitsDemanded = 300m, TotalDemandValue = 15000m
            },
            new SkuVelocityClassificationRecord
            {
                Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = tieItemZ.Id, Sku = tieItemZ.Sku,
                AbcClass = AbcClassificationCategory.ClassA, VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
                ObservationWindow = VelocityObservationWindow.Days90, CalculationPeriodStartUtc = evalTime.AddDays(-90), CalculationPeriodEndUtc = evalTime,
                CalculatedAtUtc = evalTime, TotalPickTasksCount = 80, TotalUnitsDemanded = 300m, TotalDemandValue = 15000m
            }
        );
        await context.SaveChangesAsync();

        var plan = await relocationService.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            PolicyId = policy.Id,
            AsOfDateUtc = evalTime
        });

        Assert.NotNull(plan);
        Assert.NotEmpty(plan.PrioritizedRecommendations);

        var recs = plan.PrioritizedRecommendations;
        for (int i = 0; i < recs.Count - 1; i++)
        {
            var cur = recs[i];
            var next = recs[i + 1];

            if (cur.PriorityScore != next.PriorityScore)
            {
                Assert.True(cur.PriorityScore > next.PriorityScore);
            }
            else if (cur.NetMonthlyBenefitUsd != next.NetMonthlyBenefitUsd)
            {
                Assert.True(cur.NetMonthlyBenefitUsd > next.NetMonthlyBenefitUsd);
            }
            else if (cur.UrgencyScore != next.UrgencyScore)
            {
                Assert.True(cur.UrgencyScore > next.UrgencyScore);
            }
            else if (cur.ItemSku != next.ItemSku)
            {
                Assert.True(string.Compare(cur.ItemSku, next.ItemSku, StringComparison.Ordinal) < 0);
            }
            else
            {
                Assert.True(string.Compare(cur.ProposedLocationCode, next.ProposedLocationCode, StringComparison.Ordinal) <= 0);
            }
        }

        // Direct explicit mandatory assertion for the deliberate tie pair (identical P, B, U)
        var recTieA = recs.FirstOrDefault(r => r.ItemSku == "SKU-TIE-AAA");
        var recTieZ = recs.FirstOrDefault(r => r.ItemSku == "SKU-TIE-ZZZ");
        Assert.NotNull(recTieA);
        Assert.NotNull(recTieZ);
        Assert.Equal(recTieA.PriorityScore, recTieZ.PriorityScore);
        Assert.Equal(recTieA.NetMonthlyBenefitUsd, recTieZ.NetMonthlyBenefitUsd);
        Assert.Equal(recTieA.UrgencyScore, recTieZ.UrgencyScore);
        var recList = recs.ToList();
        Assert.True(recList.IndexOf(recTieA) < recList.IndexOf(recTieZ), "SKU-TIE-AAA must precede SKU-TIE-ZZZ by alphanumeric tie-breaking");

        // Production Candidate Sorting Location-Code Tie-Break Assertion:
        // Both locGolden (A-01-02) and locGolden2 (A-01-03) offered identical physical distance, suitability,
        // and economics. RelocationRecommendationService.PrioritizeRelocationsAsync candidate sorting
        // (ThenBy(c => c.ProposedLocation.LocationCode, StringComparer.Ordinal)) prioritized A-01-02 over A-01-03.
        Assert.Equal("A-01-02", recTieA.ProposedLocationCode);
        Assert.Equal("A-01-02", recTieZ.ProposedLocationCode);

        // Production Batch Retrieval Location-Code Tie-Break Verification (GetPrioritizedPlanAsync):
        // Seed a batch containing two recommendations with identical PriorityScore, NetSavings, UrgencyScore,
        // and ItemSku, but different ProposedLocationCode ("A-01-03" and "A-01-02").
        // Calling production relocationService.GetPrioritizedPlanAsync proves that the query applies
        // ThenBy(r => r.ProposedLocationCode, StringComparer.Ordinal) directly from EF Core persistence.
        var tieLocBatch = SlottingRecommendationBatch.Create(
            facilityId: fix.FacilityId,
            policyId: policy.Id,
            batchNumber: $"RELOC-TIE-{Guid.NewGuid():N}"[..20],
            clientId: fix.ClientId,
            snapshotDateUtc: evalTime);

        var recTieLocZeta = new SlottingRecommendation
        {
            Id = Guid.NewGuid(),
            BatchId = tieLocBatch.Id,
            ClientId = fix.ClientId,
            ItemId = tieItemA.Id,
            ItemSku = "SKU-TIE-LOC",
            CurrentLocationId = fix.UpperReserveLocId,
            CurrentLocationCode = "B-05-04",
            ProposedLocationId = fix.GoldenPickLoc2Id,
            ProposedLocationCode = "A-01-03", // Later in ordinal sort order
            SourceZoneCode = "ZONE-RES",
            DestinationZoneCode = "ZONE-PICK",
            PriorityScore = 90.0m,
            EstimatedMonthlyNetSavingsUsd = 150.0m,
            UrgencyScore = 80.0m,
            SuitabilityScore = 88.0m,
            Status = SlottingRecommendationStatus.Pending,
            IsOperationallyFeasible = true
        };

        var recTieLocAlpha = new SlottingRecommendation
        {
            Id = Guid.NewGuid(),
            BatchId = tieLocBatch.Id,
            ClientId = fix.ClientId,
            ItemId = tieItemA.Id,
            ItemSku = "SKU-TIE-LOC",
            CurrentLocationId = fix.UpperReserveLocId,
            CurrentLocationCode = "B-05-04",
            ProposedLocationId = fix.GoldenPickLocId,
            ProposedLocationCode = "A-01-02", // Earlier in ordinal sort order
            SourceZoneCode = "ZONE-RES",
            DestinationZoneCode = "ZONE-PICK",
            PriorityScore = 90.0m,
            EstimatedMonthlyNetSavingsUsd = 150.0m,
            UrgencyScore = 80.0m,
            SuitabilityScore = 88.0m,
            Status = SlottingRecommendationStatus.Pending,
            IsOperationallyFeasible = true
        };

        // Insert "A-01-03" before "A-01-02" to ensure sorting is not an artifact of database insertion order
        tieLocBatch.Recommendations.Add(recTieLocZeta);
        tieLocBatch.Recommendations.Add(recTieLocAlpha);
        context.SlottingRecommendationBatches.Add(tieLocBatch);
        await context.SaveChangesAsync();

        var retrievedTiePlan = await relocationService.GetPrioritizedPlanAsync(tieLocBatch.Id);
        Assert.NotNull(retrievedTiePlan);
        Assert.Equal(2, retrievedTiePlan.PrioritizedRecommendations.Count);
        Assert.Equal("A-01-02", retrievedTiePlan.PrioritizedRecommendations[0].ProposedLocationCode);
        Assert.Equal("A-01-03", retrievedTiePlan.PrioritizedRecommendations[1].ProposedLocationCode);
        Assert.True(
            retrievedTiePlan.PrioritizedRecommendations[0].RankingRank < retrievedTiePlan.PrioritizedRecommendations[1].RankingRank,
            "Production GetPrioritizedPlanAsync must rank proposed location 'A-01-02' ahead of 'A-01-03' via ordinal tie-breaking");

        // Isolated Deterministic Ordering Comparator Verification for Location-Code Tie-Break
        // Supplementary unit comparator verifying the 5-tier OrderBy lambda expression contract.
        var tieCandidateAlpha = new PrioritizedRelocationRecommendationDto
        {
            RecommendationId = Guid.NewGuid(),
            ItemSku = "SKU-TIE-LOC",
            ProposedLocationCode = "A-01-02",
            PriorityScore = 90.0m,
            NetMonthlyBenefitUsd = 150.0m,
            UrgencyScore = 80.0m
        };
        var tieCandidateZeta = new PrioritizedRelocationRecommendationDto
        {
            RecommendationId = Guid.NewGuid(),
            ItemSku = "SKU-TIE-LOC",
            ProposedLocationCode = "A-01-03",
            PriorityScore = 90.0m,
            NetMonthlyBenefitUsd = 150.0m,
            UrgencyScore = 80.0m
        };

        var tieList = new List<PrioritizedRelocationRecommendationDto> { tieCandidateZeta, tieCandidateAlpha };
        var orderedTies = tieList
            .OrderByDescending(r => r.PriorityScore)
            .ThenByDescending(r => r.NetMonthlyBenefitUsd)
            .ThenByDescending(r => r.UrgencyScore)
            .ThenBy(r => r.ItemSku, StringComparer.Ordinal)
            .ThenBy(r => r.ProposedLocationCode, StringComparer.Ordinal)
            .ToList();

        Assert.Equal("A-01-02", orderedTies[0].ProposedLocationCode);
        Assert.Equal("A-01-03", orderedTies[1].ProposedLocationCode);
        Assert.True(orderedTies.IndexOf(tieCandidateAlpha) < orderedTies.IndexOf(tieCandidateZeta),
            "ProposedLocationCode 'A-01-02' must precede 'A-01-03' when all other ranking factors are tied");
    }

    #endregion

    #region Scenario C — Supervisory Workflow & Prohibited Bypass Enforcement

    [Fact]
    public async Task ScenarioC_SupervisoryWorkflow_SimulateReviewApproveRejectAndCancel_WithAuditTrail()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var policy = SlottingPolicy.Create("LifecycleAuditPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        var item = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-LIFECYCLE-1", Description = "Lifecycle Item" };
        context.Items.Add(item);

        // 1. Batch in Draft status
        var batchDraft = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-AUDIT-DRAFT", fix.ClientId);
        batchDraft.Status = SlottingBatchStatus.Draft;
        var recDraft = new SlottingRecommendation { Id = Guid.NewGuid(), BatchId = batchDraft.Id, ItemId = item.Id, ItemSku = item.Sku, ClientId = fix.ClientId, CurrentLocationId = fix.UpperReserveLocId, ProposedLocationId = fix.GoldenPickLocId, Status = SlottingRecommendationStatus.Pending };
        batchDraft.Recommendations.Add(recDraft);
        context.SlottingRecommendationBatches.Add(batchDraft);

        // 2. Batch in Simulated status
        var batchSim = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-AUDIT-SIMULATED", fix.ClientId);
        batchSim.MarkSimulated(2, 10m, 20m, 50m, 80m);
        var recSim1 = new SlottingRecommendation { Id = Guid.NewGuid(), BatchId = batchSim.Id, ItemId = item.Id, ItemSku = item.Sku, ClientId = fix.ClientId, CurrentLocationId = fix.UpperReserveLocId, ProposedLocationId = fix.GoldenPickLocId, Status = SlottingRecommendationStatus.Pending };
        var recSim2 = new SlottingRecommendation { Id = Guid.NewGuid(), BatchId = batchSim.Id, ItemId = item.Id, ItemSku = item.Sku, ClientId = fix.ClientId, CurrentLocationId = fix.UpperReserveLocId, ProposedLocationId = fix.GoldenPickLocId, Status = SlottingRecommendationStatus.Pending };
        batchSim.Recommendations.Add(recSim1);
        batchSim.Recommendations.Add(recSim2);
        context.SlottingRecommendationBatches.Add(batchSim);

        // 3. Batch for Rejection
        var batchReject = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-AUDIT-REJECT", fix.ClientId);
        batchReject.Status = SlottingBatchStatus.PendingApproval;
        var recReject = new SlottingRecommendation { Id = Guid.NewGuid(), BatchId = batchReject.Id, ItemId = item.Id, ItemSku = item.Sku, ClientId = fix.ClientId, CurrentLocationId = fix.UpperReserveLocId, ProposedLocationId = fix.GoldenPickLocId, Status = SlottingRecommendationStatus.Pending };
        batchReject.Recommendations.Add(recReject);
        context.SlottingRecommendationBatches.Add(batchReject);

        // 4. Batch for Cancellation
        var batchCancel = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-AUDIT-CANCEL", fix.ClientId);
        batchCancel.Status = SlottingBatchStatus.PendingApproval;
        context.SlottingRecommendationBatches.Add(batchCancel);

        await context.SaveChangesAsync();

        // -------------------------------------------------------------
        // VERIFY ALL 4 PROHIBITED OPERATIONS THROW AND CAUSE 0 STATE MUTATION
        // -------------------------------------------------------------

        // Prohibited Op 1: Direct Approval from Draft
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
            {
                BatchId = batchDraft.Id,
                ApprovedRecommendationIds = [recDraft.Id]
            }));
        Assert.Contains("PendingApproval", ex1.Message);
        var reloadedDraft = await context.SlottingRecommendationBatches.FindAsync(batchDraft.Id);
        Assert.Equal(SlottingBatchStatus.Draft, reloadedDraft!.Status);
        var reloadedRecDraft = await context.SlottingRecommendations.FindAsync(recDraft.Id);
        Assert.Equal(SlottingRecommendationStatus.Pending, reloadedRecDraft!.Status);

        // Prohibited Op 2: Direct Rejection from Draft
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
            {
                BatchId = batchDraft.Id,
                ApprovedRecommendationIds = [],
                RejectedRecommendationIds = [recDraft.Id]
            }));
        Assert.Contains("PendingApproval", ex2.Message);
        reloadedDraft = await context.SlottingRecommendationBatches.FindAsync(batchDraft.Id);
        Assert.Equal(SlottingBatchStatus.Draft, reloadedDraft!.Status);
        var reloadedRecDraft2 = await context.SlottingRecommendations.FindAsync(recDraft.Id);
        Assert.Equal(SlottingRecommendationStatus.Pending, reloadedRecDraft2!.Status);

        // Prohibited Op 3: Direct Approval from Simulated
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
            {
                BatchId = batchSim.Id,
                ApprovedRecommendationIds = [recSim1.Id]
            }));
        Assert.Contains("PendingApproval", ex3.Message);
        var reloadedSim = await context.SlottingRecommendationBatches.FindAsync(batchSim.Id);
        Assert.Equal(SlottingBatchStatus.Simulated, reloadedSim!.Status);
        var reloadedRecSim = await context.SlottingRecommendations.FindAsync(recSim1.Id);
        Assert.Equal(SlottingRecommendationStatus.Pending, reloadedRecSim!.Status);

        // Prohibited Op 4: Direct Rejection from Simulated
        var ex4 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
            {
                BatchId = batchSim.Id,
                ApprovedRecommendationIds = [],
                RejectedRecommendationIds = [recSim1.Id]
            }));
        Assert.Contains("PendingApproval", ex4.Message);
        reloadedSim = await context.SlottingRecommendationBatches.FindAsync(batchSim.Id);
        Assert.Equal(SlottingBatchStatus.Simulated, reloadedSim!.Status);
        var reloadedRecSim4_1 = await context.SlottingRecommendations.FindAsync(recSim1.Id);
        Assert.Equal(SlottingRecommendationStatus.Pending, reloadedRecSim4_1!.Status);
        var reloadedRecSim4_2 = await context.SlottingRecommendations.FindAsync(recSim2.Id);
        Assert.Equal(SlottingRecommendationStatus.Pending, reloadedRecSim4_2!.Status);

        // -------------------------------------------------------------
        // VERIFY LEGITIMATE TRANSITION, APPROVAL, REJECTION & CANCELLATION
        // -------------------------------------------------------------

        // Legitimate Submit for Review: Simulated -> PendingApproval
        var submitStatus = await service.SubmitBatchForReviewAsync(batchSim.Id);
        Assert.Equal(SlottingBatchStatus.PendingApproval, submitStatus);

        // Selective Approval: Approve recSim1, recSim2 skipped
        var approveResult = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = batchSim.Id,
            ApprovedRecommendationIds = [recSim1.Id],
            ApprovalNotes = "Supervisor authorized move 1 for upcoming promo."
        });

        Assert.Equal(SlottingBatchStatus.PartiallyApproved, approveResult.Status);
        Assert.Equal(1, approveResult.ApprovedCount);
        Assert.Equal(1, approveResult.SkippedCount);

        var rRec1 = await context.SlottingRecommendations.FindAsync(recSim1.Id);
        var rRec2 = await context.SlottingRecommendations.FindAsync(recSim2.Id);
        Assert.Equal(SlottingRecommendationStatus.Approved, rRec1!.Status);
        Assert.Equal(SlottingRecommendationStatus.Skipped, rRec2!.Status);

        // Legitimate Rejection on batchReject
        var rejectResult = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = batchReject.Id,
            ApprovedRecommendationIds = [],
            RejectedRecommendationIds = [recReject.Id],
            RejectionReasonsByRecommendationId = new Dictionary<Guid, string>
            {
                [recReject.Id] = "Destination rack undergoing seismic retrofit"
            },
            ApprovalNotes = "Batch fully rejected"
        });

        Assert.Equal(SlottingBatchStatus.Rejected, rejectResult.Status);
        Assert.Equal(1, rejectResult.RejectedCount);
        var rRejectRec = await context.SlottingRecommendations.FindAsync(recReject.Id);
        Assert.Equal(SlottingRecommendationStatus.Rejected, rRejectRec!.Status);
        Assert.Equal("Destination rack undergoing seismic retrofit", rRejectRec.RejectionReason);

        // Cancellation on batchCancel
        var cancelStatus = await service.CancelBatchAsync(batchCancel.Id, "Shift canceled due to blizzard");
        Assert.Equal(SlottingBatchStatus.Cancelled, cancelStatus);

        // Subsequent approval attempt on cancelled batch MUST throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
            {
                BatchId = batchCancel.Id,
                ApprovedRecommendationIds = []
            }));
    }

    #endregion

    #region Scenario D — Operational Safety & 8-Table Cryptographic Ledger Invariance

    [Fact]
    public async Task ScenarioD_OperationalSafety_ComprehensiveEightTableLedgerInvariance()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var evalTime = new DateTime(2026, 11, 14, 16, 0, 0, DateTimeKind.Utc);

        var relocationService = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var queryService = new AnalyticsQueryService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<AnalyticsQueryService>.Instance);

        // 1. Seed Multi-State Operational Dataset across All 8 Operational Tables
        var item1 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SAFE-1", Description = "Safe Item 1", UnitPrice = 100m, WeightKg = 10m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A, StackabilityFactor = 2, Status = ItemStatus.Active };
        item1.RecalculateVolume();
        var item2 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SAFE-2", Description = "Safe Item 2", UnitPrice = 200m, WeightKg = 25m, LengthCm = 50m, WidthCm = 40m, HeightCm = 30m, AbcVelocity = AbcVelocity.B, StackabilityFactor = 2, Status = ItemStatus.Active };
        item2.RecalculateVolume();
        var item3 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SAFE-3", Description = "Safe Item 3", UnitPrice = 50m, WeightKg = 5m, LengthCm = 20m, WidthCm = 20m, HeightCm = 20m, AbcVelocity = AbcVelocity.C, StackabilityFactor = 3, Status = ItemStatus.Active };
        item3.RecalculateVolume();
        context.Items.AddRange(item1, item2, item3);

        // Multi-state stock partitions (Available, Allocated, Reserved, Quarantined, Damaged, OnHold)
        context.StockOnHand.AddRange(
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item1.Id, LocationId = fix.UpperReserveLocId, QtyAvailable = 2m, QtyAllocated = 0m, QtyReserved = 0m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item2.Id, LocationId = fix.GoldenPickLocId, QtyAvailable = 10m, QtyAllocated = 5m, QtyReserved = 2m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item3.Id, LocationId = fix.GoldenPickLoc2Id, QtyAvailable = 20m, QtyQuarantined = 2m, QtyDamaged = 1m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item1.Id, LocationId = fix.ColdLocId, QtyAvailable = 0m, QtyOnHold = 10m }
        );

        // Outbound Orders (Completed, Processing)
        var order1 = new OutboundOrder { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, OrderNumber = "ORD-SAFE-001", Status = OutboundOrderStatus.Completed, OrderDateUtc = evalTime.AddDays(-3) };
        var order2 = new OutboundOrder { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, OrderNumber = "ORD-SAFE-002", Status = OutboundOrderStatus.Allocated, OrderDateUtc = evalTime.AddDays(-1) };
        context.OutboundOrders.AddRange(order1, order2);

        var line1 = new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order1.Id, ItemId = item1.Id, QuantityOrdered = 10m, QuantityShipped = 10m };
        var line2 = new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order2.Id, ItemId = item2.Id, QuantityOrdered = 5m, QuantityShipped = 0m };
        context.OutboundOrderLines.AddRange(line1, line2);

        // Pick Tasks
        var pick1 = new PickTask { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, OutboundOrderId = order1.Id, ItemId = item1.Id, SourceLocationId = fix.UpperReserveLocId, QuantityToPick = 10m, QuantityPicked = 10m, Status = PickTaskStatus.Completed };
        var pick2 = new PickTask { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, OutboundOrderId = order2.Id, ItemId = item2.Id, SourceLocationId = fix.GoldenPickLocId, QuantityToPick = 5m, QuantityPicked = 0m, Status = PickTaskStatus.Pending };
        context.PickTasks.AddRange(pick1, pick2);

        // Inventory Transactions
        var tx1 = new InventoryTransaction { Id = 7001L, FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item1.Id, FromLocationId = fix.UpperReserveLocId, Quantity = 10m, TxType = InventoryTxType.Pick, TxTimestamp = evalTime.AddDays(-2) };
        var tx2 = new InventoryTransaction { Id = 7002L, FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item2.Id, ToLocationId = fix.GoldenPickLocId, Quantity = 50m, TxType = InventoryTxType.Receive, TxTimestamp = evalTime.AddDays(-10) };
        var tx3 = new InventoryTransaction { Id = 7003L, FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item3.Id, FromLocationId = fix.GoldenPickLoc2Id, ToLocationId = fix.UpperReserveLocId, Quantity = 5m, TxType = InventoryTxType.Move, TxTimestamp = evalTime.AddDays(-5) };
        context.InventoryTransactions.AddRange(tx1, tx2, tx3);

        // Receipt Lines
        var r1 = new ReceiptLine { Id = Guid.NewGuid(), ReceivingSessionId = Guid.NewGuid(), ItemId = item1.Id, QuantityReceived = 50m, ScannedAtUtc = evalTime.AddDays(-15) };
        var r2 = new ReceiptLine { Id = Guid.NewGuid(), ReceivingSessionId = Guid.NewGuid(), ItemId = item2.Id, QuantityReceived = 100m, ScannedAtUtc = evalTime.AddDays(-12) };
        context.ReceiptLines.AddRange(r1, r2);

        var policy = SlottingPolicy.Create("SafetyAuditPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        context.SkuVelocityClassificationRecords.Add(new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item1.Id, Sku = item1.Sku,
            AbcClass = AbcClassificationCategory.ClassA, VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90, CalculationPeriodStartUtc = evalTime.AddDays(-90), CalculationPeriodEndUtc = evalTime,
            CalculatedAtUtc = evalTime, TotalPickTasksCount = 100, TotalUnitsDemanded = 400m, TotalDemandValue = 20000m
        });

        await context.SaveChangesAsync();

        // 2. CAPTURE COMPLETE PERSISTED ROWS & CRYPTOGRAPHIC SHA-256 HASHES (ALL 8 OPERATIONAL TABLES)
        var stockSnapBefore = context.StockOnHand.AsNoTracking().OrderBy(s => s.Id).ToList();
        var itemsSnapBefore = context.Items.AsNoTracking().OrderBy(i => i.Id).ToList();
        var locSnapBefore = context.Locations.AsNoTracking().OrderBy(l => l.Id).ToList();
        var ordersSnapBefore = context.OutboundOrders.AsNoTracking().OrderBy(o => o.Id).ToList();
        var linesSnapBefore = context.OutboundOrderLines.AsNoTracking().OrderBy(l => l.Id).ToList();
        var picksSnapBefore = context.PickTasks.AsNoTracking().OrderBy(p => p.Id).ToList();
        var txSnapBefore = context.InventoryTransactions.AsNoTracking().OrderBy(t => t.Id).ToList();
        var receiptsSnapBefore = context.ReceiptLines.AsNoTracking().OrderBy(r => r.Id).ToList();

        string hashStockBefore = ComputeSha256(stockSnapBefore);
        string hashItemsBefore = ComputeSha256(itemsSnapBefore);
        string hashLocBefore = ComputeSha256(locSnapBefore);
        string hashOrdersBefore = ComputeSha256(ordersSnapBefore);
        string hashLinesBefore = ComputeSha256(linesSnapBefore);
        string hashPicksBefore = ComputeSha256(picksSnapBefore);
        string hashTxBefore = ComputeSha256(txSnapBefore);
        string hashReceiptsBefore = ComputeSha256(receiptsSnapBefore);

        // 3. EXECUTE FULL END-TO-END ANALYTICS & SLOTTING WORKFLOW
        var health = await queryService.GetUnifiedInventoryHealthDashboardAsync(fix.FacilityId, fix.ClientId, evalTime);
        Assert.NotNull(health);

        var prioritizationResult = await relocationService.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            PolicyId = policy.Id,
            AsOfDateUtc = evalTime
        });

        Assert.Equal(0, prioritizationResult.PhysicalInventoryTransfersTriggered);
        Assert.Equal(0, prioritizationResult.LedgerMutationsOccurred);

        var recId = prioritizationResult.PrioritizedRecommendations.First().RecommendationId;
        var detail = await relocationService.InspectRecommendationAsync(recId);
        Assert.NotNull(detail);

        await relocationService.SubmitBatchForReviewAsync(prioritizationResult.BatchId);

        var approvalResult = await relocationService.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = prioritizationResult.BatchId,
            ApprovedRecommendationIds = [recId],
            ApprovalNotes = "Operational audit approval"
        });

        Assert.Equal(0, approvalResult.PhysicalInventoryTransfersTriggered);
        Assert.Equal(0, approvalResult.LedgerMutationsOccurred);

        // 4. CAPTURE POST-EXECUTION PERSISTED ROWS & CRYPTOGRAPHIC SHA-256 HASHES
        var stockSnapAfter = context.StockOnHand.AsNoTracking().OrderBy(s => s.Id).ToList();
        var itemsSnapAfter = context.Items.AsNoTracking().OrderBy(i => i.Id).ToList();
        var locSnapAfter = context.Locations.AsNoTracking().OrderBy(l => l.Id).ToList();
        var ordersSnapAfter = context.OutboundOrders.AsNoTracking().OrderBy(o => o.Id).ToList();
        var linesSnapAfter = context.OutboundOrderLines.AsNoTracking().OrderBy(l => l.Id).ToList();
        var picksSnapAfter = context.PickTasks.AsNoTracking().OrderBy(p => p.Id).ToList();
        var txSnapAfter = context.InventoryTransactions.AsNoTracking().OrderBy(t => t.Id).ToList();
        var receiptsSnapAfter = context.ReceiptLines.AsNoTracking().OrderBy(r => r.Id).ToList();

        string hashStockAfter = ComputeSha256(stockSnapAfter);
        string hashItemsAfter = ComputeSha256(itemsSnapAfter);
        string hashLocAfter = ComputeSha256(locSnapAfter);
        string hashOrdersAfter = ComputeSha256(ordersSnapAfter);
        string hashLinesAfter = ComputeSha256(linesSnapAfter);
        string hashPicksAfter = ComputeSha256(picksSnapAfter);
        string hashTxAfter = ComputeSha256(txSnapAfter);
        string hashReceiptsAfter = ComputeSha256(receiptsSnapAfter);

        // 5. ABSOLUTE INVARIANCE: CRYPTOGRAPHIC HASH PARITY ACROSS COMPLETE PERSISTED ROWS
        Assert.Equal(hashStockBefore, hashStockAfter);
        Assert.Equal(hashItemsBefore, hashItemsAfter);
        Assert.Equal(hashLocBefore, hashLocAfter);
        Assert.Equal(hashOrdersBefore, hashOrdersAfter);
        Assert.Equal(hashLinesBefore, hashLinesAfter);
        Assert.Equal(hashPicksBefore, hashPicksAfter);
        Assert.Equal(hashTxBefore, hashTxAfter);
        Assert.Equal(hashReceiptsBefore, hashReceiptsAfter);

        // Verify exact 0 Inserts, 0 Updates, 0 Deletes across all 8 tables
        Assert.Equal(stockSnapBefore.Count, stockSnapAfter.Count);
        Assert.Equal(itemsSnapBefore.Count, itemsSnapAfter.Count);
        Assert.Equal(locSnapBefore.Count, locSnapAfter.Count);
        Assert.Equal(ordersSnapBefore.Count, ordersSnapAfter.Count);
        Assert.Equal(linesSnapBefore.Count, linesSnapAfter.Count);
        Assert.Equal(picksSnapBefore.Count, picksSnapAfter.Count);
        Assert.Equal(txSnapBefore.Count, txSnapAfter.Count);
        Assert.Equal(receiptsSnapBefore.Count, receiptsSnapAfter.Count);
    }

    #endregion

    #region Scenario E — Multi-Tenant & Facility Security Isolation

    [Fact]
    public async Task ScenarioE_MultiTenantAndFacilitySecurityIsolation_RejectsCrossBoundaryAccess()
    {
        var fix = await SeedAuditEnvironmentAsync();

        // Setup Client 2 Policy, Item, Distinct Stock & Aging Snapshots, and Batch for Cross-Tenant Isolation Matrix
        using var setupContext = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var itemClient2 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client2Id, Sku = "SKU-CLIENT2-REC", Description = "Client 2 Item", WeightKg = 5m, UnitPrice = 200m, Status = ItemStatus.Active };
        setupContext.Items.Add(itemClient2);

        // Distinguishable Client 2 stock & aging data: 50 units @ $200 = $10,000
        setupContext.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.Client2Id,
            ItemId = itemClient2.Id,
            LocationId = fix.ColdLocId,
            QtyAvailable = 50m
        });
        setupContext.DailyInventoryAgingSnapshots.Add(new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.Client2Id,
            ItemId = itemClient2.Id,
            Sku = itemClient2.Sku,
            LocationId = fix.ColdLocId,
            SnapshotDateUtc = DateTime.UtcNow.Date,
            QuantityOnHand = 50m,
            QtyAvailable = 50m,
            TotalInventoryValue = 10000m,
            UnitCost = 200m,
            AgingBucket = InventoryAgingBucket.Current_0_30
        });

        var policyClient2 = SlottingPolicy.Create("Client2Policy", fix.FacilityId, fix.Client2Id);
        setupContext.SlottingPolicies.Add(policyClient2);

        var batchClient2 = SlottingRecommendationBatch.Create(fix.FacilityId, policyClient2.Id, "BATCH-CLIENT2-001", fix.Client2Id);
        batchClient2.MarkSimulated(1, 10m, 20m, 50m, 80m);
        var recClient2 = new SlottingRecommendation
        {
            Id = Guid.NewGuid(),
            BatchId = batchClient2.Id,
            ClientId = fix.Client2Id,
            ItemId = itemClient2.Id,
            ItemSku = itemClient2.Sku,
            CurrentLocationId = fix.UpperReserveLocId,
            ProposedLocationId = fix.GoldenPickLocId,
            Status = SlottingRecommendationStatus.Pending
        };
        batchClient2.Recommendations.Add(recClient2);
        setupContext.SlottingRecommendationBatches.Add(batchClient2);
        await setupContext.SaveChangesAsync();

        // -------------------------------------------------------------
        // CLIENT 1 CONTEXT (TenantId = fix.ClientId, non-staff)
        // -------------------------------------------------------------
        using var client1Context = CreateContext(fix.DbName, tenantId: fix.ClientId, facilityId: fix.FacilityId, isStaff: false);
        var client1User = new TestCurrentUserContext { UserType = UserType.Client, TenantId = fix.ClientId, FacilityId = fix.FacilityId };

        var client1RelocService = new RelocationRecommendationService(
            client1Context,
            client1User,
            NullLogger<RelocationRecommendationService>.Instance);

        var client1SlottingService = new SlottingOptimizationService(
            client1Context,
            client1User,
            NullLogger<SlottingOptimizationService>.Instance);

        var client1QueryService = new AnalyticsQueryService(
            client1Context,
            client1User,
            NullLogger<AnalyticsQueryService>.Instance);

        // 1. Cross-Tenant Batch Access Rejection
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            client1RelocService.GetBatchesForFacilityAsync(fix.FacilityId, clientId: fix.Client2Id));

        // 2. Cross-Tenant Policy Access Rejection (SlottingOptimizationService enforces ForbiddenException)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            client1SlottingService.GetActivePolicyForFacilityAsync(fix.FacilityId, clientId: fix.Client2Id));

        // 3. Cross-Tenant Batch Plan Access Rejection (RelocationRecommendationService enforces UnauthorizedAccessException)
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            client1RelocService.GetPrioritizedPlanAsync(batchClient2.Id));

        // 4. Cross-Tenant Recommendation Inspection Isolation (Global query filter hides foreign client recommendations)
        var hiddenRec = await client1RelocService.InspectRecommendationAsync(recClient2.Id);
        Assert.Null(hiddenRec);

        // 5. Cross-Tenant Inventory Analytics Query Rejection (AnalyticsQueryService enforces UnauthorizedException)
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            client1QueryService.GetDailyInventoryAgingRecordsAsync(fix.FacilityId, DateTime.UtcNow, clientId: fix.Client2Id));

        // 6. Unified Health Dashboard: Verifies Strict Tenant Confinement and Complete Foreign Data Exclusion
        var client1Dashboard = await client1QueryService.GetUnifiedInventoryHealthDashboardAsync(fix.FacilityId, clientId: fix.Client2Id, DateTime.UtcNow);
        Assert.NotNull(client1Dashboard);
        Assert.Equal(fix.ClientId, client1Dashboard.ClientId);
        Assert.NotEqual(fix.Client2Id, client1Dashboard.ClientId);
        // Dashboard contains ONLY Client 1 data, strictly excluding Client 2's 50 units ($10k)
        Assert.Equal(0m, client1Dashboard.TotalUnitsOnHand); // In this setup fixture, Client 1 stock is not added in setupContext
        Assert.Equal(0m, client1Dashboard.TotalInventoryValuation);

        // 7. Unauthorized Management/Mutation Attempt: Submit Batch for Review across Tenants
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            client1RelocService.SubmitBatchForReviewAsync(batchClient2.Id));

        // 8. Unauthorized Management/Mutation Attempt: Approve Relocation Plan across Tenants
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            client1RelocService.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
            {
                BatchId = batchClient2.Id,
                ApprovedRecommendationIds = [recClient2.Id]
            }));

        // -------------------------------------------------------------
        // FACILITY 1 STAFF CONTEXT (FacilityId = fix.FacilityId, non-superadmin)
        // -------------------------------------------------------------
        using var fac1StaffContext = CreateContext(fix.DbName, tenantId: null, facilityId: fix.FacilityId, isStaff: true, isSuperAdmin: false);
        var fac1StaffRelocService = new RelocationRecommendationService(
            fac1StaffContext,
            new TestCurrentUserContext { UserType = UserType.Staff, FacilityId = fix.FacilityId, RolesList = ["WarehouseManager"] },
            NullLogger<RelocationRecommendationService>.Instance);

        // 9. Cross-Facility Batch Access Rejection for Staff
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fac1StaffRelocService.GetBatchesForFacilityAsync(fix.Facility2Id, clientId: fix.ClientId));

        // 10. Cross-Facility Prioritization Attempt Rejection for Staff
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fac1StaffRelocService.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
            {
                FacilityId = fix.Facility2Id,
                ClientId = fix.ClientId,
                PolicyId = policyClient2.Id,
                AsOfDateUtc = DateTime.UtcNow
            }));
    }

    #endregion

    #region Scenario F — Event Replay, Idempotency, Watermark Invariance & Projection Recovery

    [Fact]
    public async Task ScenarioF_EventReplayIdempotencyAndRecovery_WithoutLedgerMutations()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 1. Seed Catalog Items with Stable Identifiers
        var itemA = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-AUDIT-A", Description = "Replay Item A", UnitPrice = 500m, Status = ItemStatus.Active };
        var itemB = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-AUDIT-B", Description = "Replay Item B", UnitPrice = 150m, Status = ItemStatus.Active };
        var itemD = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-AUDIT-D", Description = "Replay Item D", UnitPrice = 300m, Status = ItemStatus.Active };
        context.Items.AddRange(itemA, itemB, itemD);

        // 2. Persist Historical Events into Durable Database Event Store (InventoryMovementEventLogs)
        // Authoritative SequenceNumber is contractually persisted directly on the event entity
        var eventLog1 = InventoryMovementEventLog.Create(
            eventId: Guid.NewGuid(),
            facilityId: fix.FacilityId,
            clientId: fix.ClientId,
            itemId: itemA.Id,
            sku: itemA.Sku,
            eventType: InventoryMovementEventType.Putaway,
            quantity: 50m,
            timestampUtc: evalTime.AddMinutes(-30),
            destinationLocationId: fix.GoldenPickLocId,
            unitCost: 500m,
            referenceNumber: "SEQ-098",
            isCanonicalDemand: false,
            sequenceNumber: 98L);

        var eventLog2 = InventoryMovementEventLog.Create(
            eventId: Guid.NewGuid(),
            facilityId: fix.FacilityId,
            clientId: fix.ClientId,
            itemId: itemB.Id,
            sku: itemB.Sku,
            eventType: InventoryMovementEventType.Putaway,
            quantity: 40m,
            timestampUtc: evalTime.AddMinutes(-20),
            destinationLocationId: fix.GoldenPickLocId,
            unitCost: 150m,
            referenceNumber: "SEQ-099",
            isCanonicalDemand: false,
            sequenceNumber: 99L);

        var eventLog3 = InventoryMovementEventLog.Create(
            eventId: Guid.NewGuid(),
            facilityId: fix.FacilityId,
            clientId: fix.ClientId,
            itemId: itemD.Id,
            sku: itemD.Sku,
            eventType: InventoryMovementEventType.Putaway,
            quantity: 20m,
            timestampUtc: evalTime.AddMinutes(-10),
            destinationLocationId: fix.UpperReserveLocId,
            unitCost: 300m,
            referenceNumber: "SEQ-100",
            isCanonicalDemand: false,
            sequenceNumber: 100L);

        context.InventoryMovementEventLogs.AddRange(eventLog1, eventLog2, eventLog3);
        await context.SaveChangesAsync();

        Assert.Equal(98L, eventLog1.SequenceNumber);
        Assert.Equal(99L, eventLog2.SequenceNumber);
        Assert.Equal(100L, eventLog3.SequenceNumber);

        // 3. Process Events Consumed from Durable Historical Event Storage to Establish Baseline
        var durableEventStream = await context.InventoryMovementEventLogs
            .AsNoTracking()
            .Where(e => e.FacilityId == fix.FacilityId && e.ClientId == fix.ClientId)
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync();
        Assert.Equal(3, durableEventStream.Count);

        foreach (var ev in durableEventStream)
        {
            // Dedicated authoritative sequence number validated directly from entity model
            Assert.True(ev.SequenceNumber > 0L, "Durable event must contain contractually authoritative sequence number");
            var locCode = ev.DestinationLocationId == fix.GoldenPickLocId ? "A-01-02" : "B-05-04";
            var receiptDate = ev.ReferenceNumber switch
            {
                "SEQ-098" => evalTime.AddDays(-10),
                "SEQ-099" => evalTime.AddDays(-45),
                _ => evalTime.AddDays(-75)
            };
            await projectionService.ProcessDailyInventoryAgingSnapshotAsync(
                eventId: ev.EventId,
                sequenceNumber: ev.SequenceNumber,
                facilityId: ev.FacilityId,
                clientId: ev.ClientId,
                locationId: ev.DestinationLocationId!.Value,
                locationCode: locCode,
                itemId: ev.ItemId,
                sku: ev.Sku,
                snapshotDateUtc: evalTime,
                quantityOnHand: ev.Quantity,
                receivedAtUtc: receiptDate,
                lastMovedAtUtc: evalTime.AddDays(-2),
                lastDemandedAtUtc: evalTime.AddDays(-1),
                expirationDateUtc: null,
                unitCost: ev.UnitCost,
                qtyAvailable: ev.Quantity);
        }

        // 4. Verify Baseline Watermark & Projections Created by Engine
        var checkpoint = await projectionService.GetOrCreateCheckpointAsync(AnalyticsProjectionService.InventoryAgingProjection);
        Assert.NotNull(checkpoint);
        Assert.Equal(ProjectionStatus.Active, checkpoint.Status);
        Assert.Equal(100L, checkpoint.LastProcessedSequenceNumber);
        Assert.Equal(eventLog3.EventId, checkpoint.LastProcessedEventId);

        var baselineAgingSnapshots = await context.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == fix.FacilityId)
            .OrderBy(s => s.ItemId)
            .ToListAsync();
        Assert.Equal(3, baselineAgingSnapshots.Count);
        Assert.Equal(110m, baselineAgingSnapshots.Sum(s => s.QuantityOnHand));
        Assert.Equal(37000m, baselineAgingSnapshots.Sum(s => s.TotalInventoryValue)); // (50*500) + (40*150) + (20*300) = 25000 + 6000 + 6000 = 37000

        var stockBefore = context.StockOnHand.AsNoTracking().ToList();
        var itemsBefore = context.Items.AsNoTracking().ToList();

        // 5. Verify Event Deduplication (Idempotency)
        bool processedBefore = await projectionService.HasEventBeenProcessedAsync(
            AnalyticsProjectionService.InventoryAgingProjection, eventLog3.EventId);
        Assert.True(processedBefore);

        // Re-processing already processed durable event causes NO duplicate insertion
        await projectionService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: eventLog3.EventId,
            sequenceNumber: eventLog3.SequenceNumber,
            facilityId: eventLog3.FacilityId,
            clientId: eventLog3.ClientId,
            locationId: eventLog3.DestinationLocationId!.Value,
            locationCode: "B-05-04",
            itemId: eventLog3.ItemId,
            sku: eventLog3.Sku,
            snapshotDateUtc: evalTime,
            quantityOnHand: eventLog3.Quantity,
            receivedAtUtc: evalTime.AddDays(-75),
            lastMovedAtUtc: evalTime.AddDays(-2),
            lastDemandedAtUtc: evalTime.AddDays(-1),
            expirationDateUtc: null,
            unitCost: eventLog3.UnitCost,
            qtyAvailable: eventLog3.Quantity);

        var snapshotCountAfterDup = await context.DailyInventoryAgingSnapshots.CountAsync(s => s.FacilityId == fix.FacilityId);
        Assert.Equal(3, snapshotCountAfterDup);

        // 6. Execute Projection Replay From Beginning (Purge & Checkpoint Reset Phase)
        var replayedCount = await projectionService.ReplayProjectionsAsync(
            AnalyticsProjectionService.InventoryAgingProjection,
            ProjectionReplayMode.FromBeginning);
        Assert.True(replayedCount >= 0);

        // 7. Verify Checkpoint Watermark Reset and Projections Purged by Replay Engine
        var checkpointAfterReplay = await projectionService.GetOrCreateCheckpointAsync(AnalyticsProjectionService.InventoryAgingProjection);
        Assert.Equal(ProjectionStatus.Active, checkpointAfterReplay.Status);
        Assert.Equal(0L, checkpointAfterReplay.LastProcessedSequenceNumber);
        Assert.Equal(0, await context.DailyInventoryAgingSnapshots.CountAsync(s => s.FacilityId == fix.FacilityId));
        Assert.False(await projectionService.HasEventBeenProcessedAsync(AnalyticsProjectionService.InventoryAgingProjection, eventLog1.EventId));

        // 8. Invoke Production Replay Worker: Component retrieves and consumes durable event stream itself
        // ReplayInventoryAgingFromEventStreamAsync queries the durable database event stream directly,
        // orders events by authoritative sequence number (SequenceNumber ASC), and reconstructs projections
        // completely within the production service without any test-side reconstruction loops.
        int replayedEventCount = await projectionService.ReplayInventoryAgingFromEventStreamAsync(
            facilityId: fix.FacilityId,
            asOfDateUtc: evalTime);
        Assert.Equal(3, replayedEventCount);

        // 9. Verify Reconstructed Projections Match Baseline Identities & Aggregates Record-for-Record (NO manual inserts!)
        var checkpointRecovered = await projectionService.GetOrCreateCheckpointAsync(AnalyticsProjectionService.InventoryAgingProjection);
        Assert.Equal(100L, checkpointRecovered.LastProcessedSequenceNumber);
        Assert.Equal(eventLog3.EventId, checkpointRecovered.LastProcessedEventId);
        Assert.Equal(ProjectionStatus.Active, checkpointRecovered.Status);

        var rebuiltAgingSnapshots = await context.DailyInventoryAgingSnapshots
            .AsNoTracking()
            .Where(s => s.FacilityId == fix.FacilityId)
            .OrderBy(s => s.ItemId)
            .ToListAsync();

        Assert.Equal(baselineAgingSnapshots.Count, rebuiltAgingSnapshots.Count);
        Assert.Equal(baselineAgingSnapshots.Sum(s => s.QuantityOnHand), rebuiltAgingSnapshots.Sum(s => s.QuantityOnHand));
        Assert.Equal(baselineAgingSnapshots.Sum(s => s.TotalInventoryValue), rebuiltAgingSnapshots.Sum(s => s.TotalInventoryValue));

        // Assert record-for-record identity and field parity across replay
        for (int i = 0; i < baselineAgingSnapshots.Count; i++)
        {
            var baseSnap = baselineAgingSnapshots[i];
            var rebuiltSnap = rebuiltAgingSnapshots[i];
            Assert.Equal(baseSnap.ItemId, rebuiltSnap.ItemId);
            Assert.Equal(baseSnap.Sku, rebuiltSnap.Sku);
            Assert.Equal(baseSnap.LocationId, rebuiltSnap.LocationId);
            Assert.Equal(baseSnap.AgingBucket, rebuiltSnap.AgingBucket);
            Assert.Equal(baseSnap.QuantityOnHand, rebuiltSnap.QuantityOnHand);
            Assert.Equal(baseSnap.TotalInventoryValue, rebuiltSnap.TotalInventoryValue);
        }

        // Verify deduplication tracking is fully populated for all replayed events
        foreach (var ev in durableEventStream)
        {
            bool isDeduped = await projectionService.HasEventBeenProcessedAsync(
                AnalyticsProjectionService.InventoryAgingProjection, ev.EventId);
            Assert.True(isDeduped, $"Event {ev.EventId} must be tracked in deduplication store after replay");
        }

        // 8. Operational tables remain completely untouched
        var stockAfter = context.StockOnHand.AsNoTracking().ToList();
        var itemsAfter = context.Items.AsNoTracking().ToList();
        Assert.Equal(stockBefore.Count, stockAfter.Count);
        Assert.Equal(itemsBefore.Count, itemsAfter.Count);
    }

    #endregion

    #region Scenario G — Cross-Phase Spatial Heatmap & Negative Benefit Policy Directives

    [Fact]
    public async Task ScenarioG_SpatialHeatmapAndNegativeBenefitPolicies_VerifyIntegrationRules()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var evalTime = new DateTime(2026, 11, 14, 18, 0, 0, DateTimeKind.Utc);

        var queryService = new AnalyticsQueryService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<AnalyticsQueryService>.Instance);

        var relocationService = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        // 1. Verify Spatial Topology Query matches seeded locations
        var topology = await queryService.GetWarehouseTopologyAsync(fix.FacilityId);
        Assert.NotNull(topology);
        Assert.NotEmpty(topology);

        static IEnumerable<WarehouseTopologyNodeDto> FlattenNodes(IEnumerable<WarehouseTopologyNodeDto> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                if (node.Children != null && node.Children.Count > 0)
                {
                    foreach (var child in FlattenNodes(node.Children))
                        yield return child;
                }
            }
        }
        var allNodes = FlattenNodes(topology).ToList();
        Assert.Contains(allNodes, t => t.Id == fix.GoldenPickLocId);
        Assert.Contains(allNodes, t => t.Id == fix.UpperReserveLocId);

        // 2. Verify Heatmap Response respects coordinates and location cells
        var heatmap = await queryService.GetWarehouseHeatmapAsync(fix.FacilityId);
        Assert.NotNull(heatmap);
        Assert.NotEmpty(heatmap.Locations);
        var goldenCell = heatmap.Locations.First(l => l.LocationId == fix.GoldenPickLocId);
        Assert.Equal(1.1m, goldenCell.Coordinate.Z);
        Assert.Equal(10m, goldenCell.Coordinate.X);
        Assert.Equal(5m, goldenCell.Coordinate.Y);

        // 3. EXECUTE NEGATIVE NET BENEFIT POLICY BEHAVIOR & GATING RULES

        // A. Unit-Level Policy Rule Assertions:
        // Mode 1: DisqualifyNegativeBenefit -> strictly blocks negative net benefit moves, even with high urgency
        Assert.False(RelocationRecommendationService.IsNegativeBenefitAllowed(-25.00m, urgencyScore: 95.0m, "DisqualifyNegativeBenefit", urgencyThreshold: 80.0m));
        Assert.False(RelocationRecommendationService.IsNegativeBenefitAllowed(-10.00m, urgencyScore: 50.0m, "DisqualifyNegativeBenefit", urgencyThreshold: 80.0m));
        Assert.True(RelocationRecommendationService.IsNegativeBenefitAllowed(15.00m, urgencyScore: 20.0m, "DisqualifyNegativeBenefit", urgencyThreshold: 80.0m)); // Non-negative always allowed

        // Mode 2: AllowIfHighUrgency (Urgency threshold override) -> blocks below threshold, permits at or above threshold
        Assert.False(RelocationRecommendationService.IsNegativeBenefitAllowed(-25.00m, urgencyScore: 70.0m, "AllowIfHighUrgency", urgencyThreshold: 80.0m)); // 70 < 80 -> rejected
        Assert.True(RelocationRecommendationService.IsNegativeBenefitAllowed(-25.00m, urgencyScore: 85.0m, "AllowIfHighUrgency", urgencyThreshold: 80.0m));  // 85 >= 80 -> allowed
        Assert.True(RelocationRecommendationService.IsNegativeBenefitAllowed(-25.00m, urgencyScore: 80.0m, "AllowIfHighUrgency", urgencyThreshold: 80.0m));  // 80 == 80 -> allowed

        // B. Engine-Level Candidate Execution with Negative Benefit
        // Seed an item in upper reserve with low pick frequency where moving cost exceeds labor savings
        var negItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-NEG-BENEFIT-1",
            Description = "Negative benefit candidate",
            WeightKg = 25m, // heavy move cost
            LengthCm = 30m, WidthCm = 30m, HeightCm = 30m,
            AbcVelocity = AbcVelocity.A,
            StackabilityFactor = 1,
            Status = ItemStatus.Active
        };
        negItem.RecalculateVolume();
        context.Items.Add(negItem);

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = negItem.Id,
            LocationId = fix.UpperReserveLocId,
            QtyAvailable = 5m
        });

        // 1 pick task in 90 days -> minimal monthly pick savings ($0.10/month) vs move labor ($25+) -> Net benefit negative!
        context.SkuVelocityClassificationRecords.Add(new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = negItem.Id, Sku = negItem.Sku,
            AbcClass = AbcClassificationCategory.ClassA, VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90, CalculationPeriodStartUtc = evalTime.AddDays(-90), CalculationPeriodEndUtc = evalTime,
            CalculatedAtUtc = evalTime, TotalPickTasksCount = 1, TotalUnitsDemanded = 1m, TotalDemandValue = 250m
        });

        var policyDisqualify = SlottingPolicy.Create("DisqualifyPolicy", fix.FacilityId);
        policyDisqualify.NegativeNetBenefitPolicy = "DisqualifyNegativeBenefit";
        policyDisqualify.UrgencyThresholdForNegativeBenefit = 80m;
        policyDisqualify.TargetClassAPickFace = true;
        context.SlottingPolicies.Add(policyDisqualify);
        await context.SaveChangesAsync();

        var planDisqualified = await relocationService.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            PolicyId = policyDisqualify.Id,
            NegativeNetBenefitPolicyOverride = "DisqualifyNegativeBenefit",
            AsOfDateUtc = evalTime
        });

        Assert.NotNull(planDisqualified);
        // The negative net benefit candidate is disqualified from accepted recommendations
        Assert.DoesNotContain(planDisqualified.PrioritizedRecommendations, r => r.ItemSku == negItem.Sku);
        Assert.True(planDisqualified.DisqualifiedNegativeBenefitCount >= 1, "DisqualifiedNegativeBenefitCount must be incremented for negative-benefit SKU");

        // C. Engine Candidate Execution under AllowIfHighUrgency (Urgency threshold gating)
        // Case 1: Threshold = 85m. Candidate urgency is 75m (< 85m) -> disqualified from accepted recommendations.
        var policyUrgencyBlocked = SlottingPolicy.Create("UrgencyBlockedPolicy", fix.FacilityId);
        policyUrgencyBlocked.NegativeNetBenefitPolicy = "AllowIfHighUrgency";
        policyUrgencyBlocked.UrgencyThresholdForNegativeBenefit = 85m;
        policyUrgencyBlocked.TargetClassAPickFace = true;
        context.SlottingPolicies.Add(policyUrgencyBlocked);
        await context.SaveChangesAsync();

        var planUrgencyBlocked = await relocationService.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            PolicyId = policyUrgencyBlocked.Id,
            NegativeNetBenefitPolicyOverride = "AllowIfHighUrgency",
            AsOfDateUtc = evalTime
        });

        Assert.NotNull(planUrgencyBlocked);
        Assert.DoesNotContain(planUrgencyBlocked.PrioritizedRecommendations, r => r.ItemSku == negItem.Sku);
        Assert.True(planUrgencyBlocked.DisqualifiedNegativeBenefitCount >= 1, "DisqualifiedNegativeBenefitCount must be incremented when urgency < threshold");

        // Case 2: Threshold = 70m. Candidate urgency is 75m (>= 70m) -> negative benefit permitted due to high urgency.
        var policyUrgencyAllowed = SlottingPolicy.Create("UrgencyAllowedPolicy", fix.FacilityId);
        policyUrgencyAllowed.NegativeNetBenefitPolicy = "AllowIfHighUrgency";
        policyUrgencyAllowed.UrgencyThresholdForNegativeBenefit = 70m;
        policyUrgencyAllowed.TargetClassAPickFace = true;
        context.SlottingPolicies.Add(policyUrgencyAllowed);
        await context.SaveChangesAsync();

        var planUrgencyAllowed = await relocationService.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            PolicyId = policyUrgencyAllowed.Id,
            NegativeNetBenefitPolicyOverride = "AllowIfHighUrgency",
            AsOfDateUtc = evalTime
        });

        Assert.NotNull(planUrgencyAllowed);
        Assert.Contains(planUrgencyAllowed.PrioritizedRecommendations, r => r.ItemSku == negItem.Sku);
        Assert.True(planUrgencyAllowed.HighUrgencyNegativeBenefitCount >= 1, "HighUrgencyNegativeBenefitCount must be incremented when urgency >= threshold");
    }

    #endregion
}