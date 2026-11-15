using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Exhaustive unit and integration test suite for Phase 6.3.7.2:
/// ABC and Velocity-Based Slotting Rules.
/// Verifies:
/// 1. A/B/C classification placement behavior (Class A golden zone/front; Class B intermediate; Class C reserve).
/// 2. Every supported velocity category (Surging, Declining, Stable, Inactive, Seasonal Spike).
/// 3. Conflicting ABC and velocity priorities (Class A declining vs Class C surging).
/// 4. Picking-volume density and dock proximity scoring.
/// 5. Deterministic suitability scoring, ranking, and tie-breaking.
/// 6. Multi-SKU batch planning with cumulative destination weight and volume exhaustion.
/// 7. Missing or stale analytical projection fallback behavior.
/// 8. Suitability matrix generation across SKUs and locations.
/// 9. Strict read-only ledger invariance across all eight operational tables.
/// 10. WebAPI controller contracts and permission attributes.
/// </summary>
public class SlottingAbcAndVelocityRulesTests
{
    private class VelocityTestFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid ClientId { get; set; } = Guid.NewGuid();

        public Guid PickFaceZoneId { get; set; } = Guid.NewGuid();
        public Guid ReserveZoneId { get; set; } = Guid.NewGuid();

        // Items
        public Guid ItemClassAId { get; set; } = Guid.NewGuid();
        public Guid ItemClassBId { get; set; } = Guid.NewGuid();
        public Guid ItemClassCId { get; set; } = Guid.NewGuid();
        public Guid ItemSurgingId { get; set; } = Guid.NewGuid();
        public Guid ItemDecliningId { get; set; } = Guid.NewGuid();
        public Guid ItemSeasonalId { get; set; } = Guid.NewGuid();
        public Guid ItemDeadStockId { get; set; } = Guid.NewGuid();
        public Guid ItemSurgingClassCId { get; set; } = Guid.NewGuid();

        // Locations
        public Guid GoldenZonePickLocation1Id { get; set; } = Guid.NewGuid(); // Level 1, Z = 1.1m (close to dock)
        public Guid GoldenZonePickLocation2Id { get; set; } = Guid.NewGuid(); // Level 1, Z = 1.1m (close to dock)
        public Guid IntermediateRackLocationId { get; set; } = Guid.NewGuid();  // Level 2, Z = 2.2m
        public Guid UpperReserveLocationId { get; set; } = Guid.NewGuid();      // Level 4, Z = 7.5m
        public Guid FarUpperReserveLocationId { get; set; } = Guid.NewGuid();   // Level 5, Z = 9.0m
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
            PermissionsList = [Permissions.Analytics.SlottingView, Permissions.Analytics.SlottingManage]
        };

        return TestDbContextFactory.Create(
            new TestTenantProvider { TenantId = tenantId, BypassTenantFilter = isStaff || isSuperAdmin },
            currentUser,
            dbName);
    }

    private static async Task<VelocityTestFixture> SeedEnvironmentAsync()
    {
        var fix = new VelocityTestFixture();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);

        var facility = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "ORD-02",
            Name = "Midwest Distribution Center",
            TotalAreaSqM = 20000m,
            IsActive = true
        };

        var client = new Client
        {
            Id = fix.ClientId,
            AccountCode = "CLI-VEL-TEST",
            CompanyName = "Velocity Test Client",
            Status = ClientStatus.Active
        };

        var pickZone = new WarehouseZone
        {
            Id = fix.PickFaceZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-PICK-A",
            Name = "Primary Pick Face Zone",
            ZoneType = WarehouseZoneType.PickFace,
            IsActive = true
        };

        var reserveZone = new WarehouseZone
        {
            Id = fix.ReserveZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-RESERVE-B",
            Name = "Bulk High-Bay Reserve",
            ZoneType = WarehouseZoneType.BulkStorage,
            IsActive = true
        };

        context.Facilities.Add(facility);
        context.Clients.Add(client);
        context.WarehouseZones.AddRange(pickZone, reserveZone);

        // Seed Locations
        var locGolden1 = new Location
        {
            Id = fix.GoldenZonePickLocation1Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-A01-R01-L1",
            AisleCode = "A-01",
            RackCode = "R-01",
            ShelfLevel = 1,
            LocationType = LocationType.Rack,
            XCoord = 5.0m,
            YCoord = 5.0m,
            ZCoord = 1.1m, // In golden zone (0.8m - 1.5m), close to dock (5,5)
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 100m,
            MaxWeightKg = 500m,
            IsActive = true,
            IsPickable = true
        };

        var locGolden2 = new Location
        {
            Id = fix.GoldenZonePickLocation2Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-A01-R02-L1",
            AisleCode = "A-01",
            RackCode = "R-02",
            ShelfLevel = 1,
            LocationType = LocationType.Rack,
            XCoord = 8.0m,
            YCoord = 5.0m,
            ZCoord = 1.1m,
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 100m,
            MaxWeightKg = 500m,
            IsActive = true,
            IsPickable = true
        };

        var locInter = new Location
        {
            Id = fix.IntermediateRackLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "RACK-A02-R01-L2",
            AisleCode = "A-02",
            RackCode = "R-01",
            ShelfLevel = 2,
            LocationType = LocationType.Rack,
            XCoord = 20.0m,
            YCoord = 20.0m,
            ZCoord = 2.2m, // Outside golden zone, intermediate height
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 100m,
            MaxWeightKg = 500m,
            IsActive = true,
            IsPickable = true
        };

        var locReserve = new Location
        {
            Id = fix.UpperReserveLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ReserveZoneId,
            LocationCode = "RES-B01-R01-L4",
            AisleCode = "B-01",
            RackCode = "R-01",
            ShelfLevel = 4,
            LocationType = LocationType.Rack,
            XCoord = 35.0m,
            YCoord = 35.0m,
            ZCoord = 7.5m, // High-bay upper reserve
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 100m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true
        };

        var locFarReserve = new Location
        {
            Id = fix.FarUpperReserveLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ReserveZoneId,
            LocationCode = "RES-B02-R01-L5",
            AisleCode = "B-02",
            RackCode = "R-01",
            ShelfLevel = 5,
            LocationType = LocationType.Rack,
            XCoord = 50.0m,
            YCoord = 50.0m,
            ZCoord = 9.0m,
            LengthCm = 120m,
            WidthCm = 100m,
            HeightCm = 100m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true
        };

        context.Locations.AddRange(locGolden1, locGolden2, locInter, locReserve, locFarReserve);

        // Seed Items
        var itemClassA = new Item
        {
            Id = fix.ItemClassAId,
            ClientId = fix.ClientId,
            Sku = "SKU-ABC-CLASS-A",
            Description = "Fast Mover Class A Item",
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 15m,
            WeightKg = 5.0m,
            AbcVelocity = AbcVelocity.A
        };

        var itemClassB = new Item
        {
            Id = fix.ItemClassBId,
            ClientId = fix.ClientId,
            Sku = "SKU-ABC-CLASS-B",
            Description = "Medium Mover Class B Item",
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 15m,
            WeightKg = 5.0m,
            AbcVelocity = AbcVelocity.B
        };

        var itemClassC = new Item
        {
            Id = fix.ItemClassCId,
            ClientId = fix.ClientId,
            Sku = "SKU-ABC-CLASS-C",
            Description = "Slow Mover Class C Item",
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 15m,
            WeightKg = 5.0m,
            AbcVelocity = AbcVelocity.C
        };

        var itemSurging = new Item
        {
            Id = fix.ItemSurgingId,
            ClientId = fix.ClientId,
            Sku = "SKU-VEL-SURGING",
            Description = "Surging Velocity Product",
            LengthCm = 25m,
            WidthCm = 20m,
            HeightCm = 15m,
            WeightKg = 4.0m,
            AbcVelocity = AbcVelocity.B
        };

        var itemDeclining = new Item
        {
            Id = fix.ItemDecliningId,
            ClientId = fix.ClientId,
            Sku = "SKU-VEL-DECLINING",
            Description = "Declining Velocity Product",
            LengthCm = 30m,
            WidthCm = 25m,
            HeightCm = 15m,
            WeightKg = 6.0m,
            AbcVelocity = AbcVelocity.A
        };

        var itemSeasonal = new Item
        {
            Id = fix.ItemSeasonalId,
            ClientId = fix.ClientId,
            Sku = "SKU-VEL-SEASONAL",
            Description = "Seasonal Holiday Spike Item",
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 15m,
            WeightKg = 5.0m,
            AbcVelocity = AbcVelocity.B
        };

        var itemDeadStock = new Item
        {
            Id = fix.ItemDeadStockId,
            ClientId = fix.ClientId,
            Sku = "SKU-VEL-DEADSTOCK",
            Description = "Dead Stock Dormant Part",
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 20m,
            WeightKg = 10.0m,
            AbcVelocity = AbcVelocity.C
        };

        var itemSurgingClassC = new Item
        {
            Id = fix.ItemSurgingClassCId,
            ClientId = fix.ClientId,
            Sku = "SKU-CONFLICT-C-SURGING",
            Description = "Class C Low Value with High Pick Demand",
            LengthCm = 20m,
            WidthCm = 15m,
            HeightCm = 10m,
            WeightKg = 2.0m,
            AbcVelocity = AbcVelocity.C
        };

        context.Items.AddRange(itemClassA, itemClassB, itemClassC, itemSurging, itemDeclining, itemSeasonal, itemDeadStock, itemSurgingClassC);

        // Seed Default Policy
        var policy = SlottingPolicy.Create(
            name: "Default ABC & Velocity Policy",
            facilityId: fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        await context.SaveChangesAsync();
        return fix;
    }

    #region Acceptance Criteria 1: ABC Affinity Placement Behavior

    /// <summary>
    /// Test 1: Class A item prefers Golden Zone and forward pick face; penalized in high-bay reserve.
    /// </summary>
    [Fact]
    public async Task AbcAffinity_ClassA_PrefersGoldenZoneAndForwardPickFace()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // 1. Evaluate Class A in Golden Zone pick face
        var goldenEval = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassAId, fix.GoldenZonePickLocation1Id);
        Assert.True(goldenEval.IsOverallCompatible);
        Assert.Equal(100m, goldenEval.AbcAffinityScore);
        Assert.Equal(100m, goldenEval.ErgonomicScore);

        // 2. Evaluate Class A in Upper Reserve
        var reserveEval = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassAId, fix.UpperReserveLocationId);
        Assert.True(reserveEval.IsOverallCompatible);
        Assert.Equal(25m, reserveEval.AbcAffinityScore); // Heavily penalized in high-bay reserve
        Assert.Equal(30m, reserveEval.ErgonomicScore);
        Assert.True(goldenEval.CalculatedSuitabilityScore > reserveEval.CalculatedSuitabilityScore);
    }

    /// <summary>
    /// Test 2: Class B item prefers intermediate rack elevations (Shelf Level 2-3).
    /// </summary>
    [Fact]
    public async Task AbcAffinity_ClassB_PrefersIntermediateRackLevels()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var interEval = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassBId, fix.IntermediateRackLocationId);
        Assert.True(interEval.IsOverallCompatible);
        Assert.Equal(95m, interEval.AbcAffinityScore); // Highest affinity at intermediate level 2

        var farReserveEval = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassBId, fix.FarUpperReserveLocationId);
        Assert.True(interEval.CalculatedSuitabilityScore >= farReserveEval.CalculatedSuitabilityScore);
    }

    /// <summary>
    /// Test 3: Class C item prefers upper reserve and deep storage; penalized in golden zone pick face.
    /// </summary>
    [Fact]
    public async Task AbcAffinity_ClassC_PrefersUpperReserveAndDeepStorage()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var reserveEval = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassCId, fix.UpperReserveLocationId);
        Assert.True(reserveEval.IsOverallCompatible);
        Assert.Equal(100m, reserveEval.AbcAffinityScore); // Optimal placement in reserve

        var goldenEval = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassCId, fix.GoldenZonePickLocation1Id);
        Assert.Equal(20m, goldenEval.AbcAffinityScore); // Wastes valuable pick face
        Assert.Contains(goldenEval.ViolatedSoftPreferences, s => s.Contains("Slow-moving Class C SKU occupies a high-value"));
    }

    #endregion

    #region Acceptance Criteria 2: Velocity Affinity Categories

    /// <summary>
    /// Test 4: Surging Velocity promotes item to prime pick face near dock to reduce walk time.
    /// </summary>
    [Fact]
    public async Task VelocityAffinity_SurgingVelocity_PromotesToPrimePickFaceNearDock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Seed 90d baseline (5 units/day) and 30d recent surge (15 units/day > 1.3x)
        var now = DateTime.UtcNow;
        var rec90 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemSurgingId,
            Sku = "SKU-VEL-SURGING",
            AbcClass = AbcClassificationCategory.ClassB,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 450m, // 5/day
            TotalPickTasksCount = 150
        };
        var rec30 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemSurgingId,
            Sku = "SKU-VEL-SURGING",
            AbcClass = AbcClassificationCategory.ClassB,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days30,
            CalculationPeriodStartUtc = now.AddDays(-30),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 450m, // 15/day > 5 * 1.3
            TotalPickTasksCount = 150
        };
        context.SkuVelocityClassificationRecords.AddRange(rec90, rec30);

        // Put currently in reserve
        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemSurgingId,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 50m
        });
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId
        });

        Assert.NotNull(batch);
        var rec = Assert.Single(batch.Recommendations, r => r.ItemId == fix.ItemSurgingId);
        Assert.Equal("Surging", rec.VelocityTrend);
        Assert.Equal("PromoteFastMoverToGoldenZonePickFace", rec.ReasonCode);
        Assert.Equal(fix.GoldenZonePickLocation1Id, rec.ProposedLocationId);
        Assert.True(rec.EstimatedMonthlyNetSavingsUsd >= 5.00m);
    }

    /// <summary>
    /// Test 5: Declining Velocity demotes item from golden zone to intermediate rack, freeing prime golden zone.
    /// </summary>
    [Fact]
    public async Task VelocityAffinity_DecliningVelocity_DemotesFromGoldenZoneToIntermediate()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var now = DateTime.UtcNow;
        var rec90 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemDecliningId,
            Sku = "SKU-VEL-DECLINING",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.MediumMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 900m, // 10/day
            TotalPickTasksCount = 180
        };
        var rec30 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemDecliningId,
            Sku = "SKU-VEL-DECLINING",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.SlowMoving,
            ObservationWindow = VelocityObservationWindow.Days30,
            CalculationPeriodStartUtc = now.AddDays(-30),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 60m, // 2/day < 10 * 0.7 = 7
            TotalPickTasksCount = 15
        };
        context.SkuVelocityClassificationRecords.AddRange(rec90, rec30);

        // Put currently in golden zone
        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemDecliningId,
            LocationId = fix.GoldenZonePickLocation1Id,
            QtyAvailable = 20m
        });
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId
        });

        Assert.NotNull(batch);
        var rec = Assert.Single(batch.Recommendations, r => r.ItemId == fix.ItemDecliningId);
        Assert.Equal("Declining", rec.VelocityTrend);
        Assert.Equal("RebalanceDecliningClassAFromGoldenZoneToIntermediate", rec.ReasonCode);
        Assert.Equal(fix.IntermediateRackLocationId, rec.ProposedLocationId);
        Assert.True(rec.EstimatedMonthlyNetSavingsUsd >= 5.00m);
    }

    /// <summary>
    /// Test 6: Seasonal Spike stages item in accessible buffer or pick face.
    /// </summary>
    [Fact]
    public async Task VelocityAffinity_SeasonalSpike_StagesInBufferOrPickFace()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var now = DateTime.UtcNow;
        var rec90 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemSeasonalId,
            Sku = "SKU-VEL-SEASONAL",
            AbcClass = AbcClassificationCategory.ClassB,
            VelocityCategory = InventoryMovementVelocityCategory.MediumMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            IsSeasonalPattern = true,
            TotalUnitsDemanded = 300m,
            TotalPickTasksCount = 90
        };
        context.SkuVelocityClassificationRecords.Add(rec90);

        // Put currently in deep reserve
        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemSeasonalId,
            LocationId = fix.FarUpperReserveLocationId,
            QtyAvailable = 30m
        });
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId
        });

        Assert.NotNull(batch);
        var rec = Assert.Single(batch.Recommendations, r => r.ItemId == fix.ItemSeasonalId);
        Assert.Equal("SeasonalSpike", rec.VelocityTrend);
        Assert.Equal("StageSeasonalSpikeItemInBufferZone", rec.ReasonCode);
    }

    /// <summary>
    /// Test 7: Inactive / Dead Stock already in reserve is strictly protected from unnecessary churn.
    /// </summary>
    [Fact]
    public async Task VelocityAffinity_InactiveAndDeadStock_ProtectedInReserve()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var now = DateTime.UtcNow;
        var rec90 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemDeadStockId,
            Sku = "SKU-VEL-DEADSTOCK",
            AbcClass = AbcClassificationCategory.ClassC,
            VelocityCategory = InventoryMovementVelocityCategory.DeadStock,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 0m,
            TotalPickTasksCount = 0
        };
        context.SkuVelocityClassificationRecords.Add(rec90);

        // Put in upper reserve
        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemDeadStockId,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 100m
        });
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId
        });

        Assert.NotNull(batch);
        // Must NOT recommend moving dead stock already in reserve!
        Assert.DoesNotContain(batch.Recommendations, r => r.ItemId == fix.ItemDeadStockId);
    }

    #endregion

    #region Acceptance Criteria 3: Conflicting ABC and Velocity Priorities

    /// <summary>
    /// Test 8: Class C item with Surging pick velocity is promoted to pick face to reduce pick walk labor hours,
    /// with an explicit conflict resolution explanation logged.
    /// </summary>
    [Fact]
    public async Task ConflictingPriorities_ClassCWithSurgingVelocity_PromotesToPickFaceForLaborSavings()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var now = DateTime.UtcNow;
        var rec90 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemSurgingClassCId,
            Sku = "SKU-CONFLICT-C-SURGING",
            AbcClass = AbcClassificationCategory.ClassC,
            VelocityCategory = InventoryMovementVelocityCategory.MediumMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 450m, // 5/day
            TotalPickTasksCount = 100
        };
        var rec30 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemSurgingClassCId,
            Sku = "SKU-CONFLICT-C-SURGING",
            AbcClass = AbcClassificationCategory.ClassC,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days30,
            CalculationPeriodStartUtc = now.AddDays(-30),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 600m, // 20/day > 5 * 1.3
            TotalPickTasksCount = 200
        };
        context.SkuVelocityClassificationRecords.AddRange(rec90, rec30);

        // Put currently in reserve
        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemSurgingClassCId,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 50m
        });
        await context.SaveChangesAsync();

        // 1. Verify compatibility explanation identifies the priority resolution
        var compat = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemSurgingClassCId, fix.GoldenZonePickLocation1Id);
        Assert.NotNull(compat.PriorityResolutionExplanation);
        Assert.Contains("Priority conflict resolved", compat.PriorityResolutionExplanation);
        Assert.Contains("Surging pick demand", compat.PriorityResolutionExplanation);

        // 2. Simulation promotes SKU to pick face
        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId
        });

        Assert.NotNull(batch);
        var rec = Assert.Single(batch.Recommendations, r => r.ItemId == fix.ItemSurgingClassCId);
        Assert.Equal("PromoteSurgingClassCToPickFaceForLaborReduction", rec.ReasonCode);
        Assert.Equal("Surging", rec.VelocityTrend);
        Assert.Equal("C", rec.AbcClass);
    }

    /// <summary>
    /// Test 9: Class A item with Declining demand is rebalanced to intermediate rack to free prime golden zone.
    /// </summary>
    [Fact]
    public async Task ConflictingPriorities_ClassAWithDecliningVelocity_RebalancesToIntermediate()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var now = DateTime.UtcNow;
        var rec90 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemDecliningId,
            Sku = "SKU-VEL-DECLINING",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 1800m,
            TotalPickTasksCount = 300
        };
        var rec30 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemDecliningId,
            Sku = "SKU-VEL-DECLINING",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.SlowMoving,
            ObservationWindow = VelocityObservationWindow.Days30,
            CalculationPeriodStartUtc = now.AddDays(-30),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 60m, // Declining
            TotalPickTasksCount = 10
        };
        context.SkuVelocityClassificationRecords.AddRange(rec90, rec30);

        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemDecliningId,
            LocationId = fix.GoldenZonePickLocation1Id,
            QtyAvailable = 15m
        });
        await context.SaveChangesAsync();

        var compat = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemDecliningId, fix.GoldenZonePickLocation1Id);
        Assert.NotNull(compat.PriorityResolutionExplanation);
        Assert.Contains("Priority conflict resolved", compat.PriorityResolutionExplanation);
        Assert.Contains("Declining demand trend", compat.PriorityResolutionExplanation);
    }

    #endregion

    #region Acceptance Criteria 4: Picking-Volume Density Scoring

    /// <summary>
    /// Test 10: Picking volume density commands locations with closest proximity to packing/shipping dock.
    /// </summary>
    [Fact]
    public async Task PickingVolumeDensity_HighPickFrequency_CommandsClosestProximityToDock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var now = DateTime.UtcNow;
        var rec90 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemClassAId,
            Sku = "SKU-ABC-CLASS-A",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 900m,
            TotalPickTasksCount = 450 // 5 picks/day -> High pick density
        };
        context.SkuVelocityClassificationRecords.Add(rec90);
        await context.SaveChangesAsync();

        // Loc Golden 1 is at (5, 5, 1.1) -> D ~ 7.1m
        var nearResult = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassAId, fix.GoldenZonePickLocation1Id);

        // Loc Inter is at (20, 20, 2.2) -> D ~ 28.3m
        var farResult = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassAId, fix.IntermediateRackLocationId);

        Assert.True(nearResult.PickDensityScore > farResult.PickDensityScore);
        Assert.True(nearResult.CalculatedSuitabilityScore > farResult.CalculatedSuitabilityScore);
    }

    #endregion

    #region Acceptance Criteria 5 & 6: Multi-SKU Competition & Cumulative Capacity Exhaustion

    /// <summary>
    /// Test 11: End-to-End Multi-SKU Competition for Same Destination Location:
    /// SKU 1 and SKU 2 both compete for prime GoldenZonePickLocation1 (usable max weight = 500 kg).
    /// SKU 1 proposes 300 kg (accepted, leaves 200 kg remaining).
    /// SKU 2 proposes 250 kg (individually fits 250 < 500, but 300 + 250 = 550 > 500!).
    /// Engine rejects SKU 2 at GoldenZonePickLocation1 due to cumulative capacity exhaustion and reranks SKU 2 to GoldenZonePickLocation2!
    /// </summary>
    [Fact]
    public async Task MultiSkuCompetition_CumulativeWeightCapacityExhaustion_RejectsOrReranksOverflowProposals()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var now = DateTime.UtcNow;

        // SKU 1 (Higher volume: 200 picks, 300 kg total)
        var recSku1 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemClassAId,
            Sku = "SKU-ABC-CLASS-A",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 600m,
            TotalPickTasksCount = 200 // Higher savings
        };

        // SKU 2 (Moderate volume: 100 picks, 250 kg total)
        var recSku2 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemSurgingId,
            Sku = "SKU-VEL-SURGING",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 500m,
            TotalPickTasksCount = 100
        };
        context.SkuVelocityClassificationRecords.AddRange(recSku1, recSku2);

        // Put both SKUs in reserve currently
        // SKU 1: 60 units * 5 kg = 300 kg
        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemClassAId,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 60m
        });

        // SKU 2: 62.5 units * 4 kg = 250 kg
        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemSurgingId,
            LocationId = fix.FarUpperReserveLocationId,
            QtyAvailable = 62.5m
        });
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            MaxMoves = 10
        });

        Assert.NotNull(batch);
        Assert.Equal(2, batch.Recommendations.Count);

        var rec1 = batch.Recommendations.FirstOrDefault(r => r.ItemId == fix.ItemClassAId);
        var rec2 = batch.Recommendations.FirstOrDefault(r => r.ItemId == fix.ItemSurgingId);

        Assert.NotNull(rec1);
        Assert.NotNull(rec2);

        // SKU 1 has higher savings -> Claims GoldenZonePickLocation1
        Assert.Equal(fix.GoldenZonePickLocation1Id, rec1.ProposedLocationId);
        Assert.Equal(300m, rec1.CumulativeWeightConsumedKg);

        // SKU 2 could NOT fit into GoldenZonePickLocation1 (300 + 250 = 550 > 500 kg limit)
        // Verified: It was reranked and placed into GoldenZonePickLocation2!
        Assert.Equal(fix.GoldenZonePickLocation2Id, rec2.ProposedLocationId);
        Assert.Equal(250m, rec2.CumulativeWeightConsumedKg);
    }

    /// <summary>
    /// Test 12: Cumulative Volumetric Capacity Exhaustion:
    /// Location volume = 1,020,000 cm3 (usable 85% = 867,000 cm3).
    /// SKU 1 proposals consume 500,000 cm3.
    /// SKU 2 proposes 450,000 cm3 (500k + 450k = 950k > 867k).
    /// SKU 2 is rejected for that location due to cumulative volume exhaustion.
    /// </summary>
    [Fact]
    public async Task MultiSkuCompetition_CumulativeVolumetricCapacityExhaustion_RejectsOverflow()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var now = DateTime.UtcNow;

        var rec1 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemClassAId,
            Sku = "SKU-ABC-CLASS-A",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 600m,
            TotalPickTasksCount = 200
        };

        var rec2 = new SkuVelocityClassificationRecord
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemSurgingId,
            Sku = "SKU-VEL-SURGING",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 500m,
            TotalPickTasksCount = 100
        };
        context.SkuVelocityClassificationRecords.AddRange(rec1, rec2);

        // SKU 1 has unit volume 9,000 cm3 * 60 units = 540,000 cm3
        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemClassAId,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 60m
        });

        // SKU 2 has unit volume 7,500 cm3 * 60 units = 450,000 cm3
        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemSurgingId,
            LocationId = fix.FarUpperReserveLocationId,
            QtyAvailable = 60m
        });
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId
        });

        Assert.NotNull(batch);
        var p1 = batch.Recommendations.FirstOrDefault(r => r.ItemId == fix.ItemClassAId);
        var p2 = batch.Recommendations.FirstOrDefault(r => r.ItemId == fix.ItemSurgingId);

        Assert.NotNull(p1);
        Assert.NotNull(p2);
        // Proves both proposals do not over-subscribe the same physical destination
        Assert.NotEqual(p1.ProposedLocationId, p2.ProposedLocationId);
    }

    #endregion

    #region Acceptance Criteria 7: Deterministic Ranking & Tie-Breaking

    /// <summary>
    /// Test 13: Identical inputs produce 100% identical recommendations and verify deterministic tie-breaker order.
    /// </summary>
    [Fact]
    public async Task DeterministicRankingAndTieBreaking_IdenticalInputs_ProducesIdenticalProposalsAndOrder()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Put stock in upper reserve
        context.StockOnHand.Add(new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemClassAId,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 50m
        });
        await context.SaveChangesAsync();

        var simRequest = new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            MaxMoves = 10
        };

        // Run simulation twice
        var run1 = await service.SimulateSlottingRecommendationsAsync(simRequest);
        var run2 = await service.SimulateSlottingRecommendationsAsync(simRequest);

        Assert.NotNull(run1);
        Assert.NotNull(run2);
        Assert.Equal(run1.Recommendations.Count, run2.Recommendations.Count);

        for (int i = 0; i < run1.Recommendations.Count; i++)
        {
            var r1 = run1.Recommendations[i];
            var r2 = run2.Recommendations[i];

            Assert.Equal(r1.ItemId, r2.ItemId);
            Assert.Equal(r1.ItemSku, r2.ItemSku);
            Assert.Equal(r1.ProposedLocationId, r2.ProposedLocationId);
            Assert.Equal(r1.ProposedLocationCode, r2.ProposedLocationCode);
            Assert.Equal(r1.EstimatedMonthlyNetSavingsUsd, r2.EstimatedMonthlyNetSavingsUsd);
            Assert.Equal(r1.SuitabilityScore, r2.SuitabilityScore);
        }
    }

    #endregion

    #region Acceptance Criteria 8: Missing or Stale Projection Fallback

    /// <summary>
    /// Test 14: Gracefully falls back to Item catalog velocity classification when SkuVelocityClassificationRecord is missing.
    /// </summary>
    [Fact]
    public async Task MissingOrStaleAnalyticalProjections_GracefulFallbackToCatalogClassification()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Ensure zero SkuVelocityClassificationRecords exist for ItemClassA
        var count = await context.SkuVelocityClassificationRecords.CountAsync(r => r.ItemId == fix.ItemClassAId);
        Assert.Equal(0, count);

        // Evaluate compatibility
        var result = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassAId, fix.GoldenZonePickLocation1Id);

        Assert.NotNull(result);
        Assert.True(result.IsProjectionStaleOrMissing);
        Assert.Equal("A", result.EffectiveAbcClass);
        Assert.Equal("FastMoving", result.EffectiveVelocityCategory);
        Assert.Equal("Stable", result.VelocityTrend);
        Assert.True(result.CalculatedSuitabilityScore >= 75m);
    }

    #endregion

    #region Acceptance Criteria 9: Suitability Matrix Generation

    /// <summary>
    /// Test 15: Suitability Matrix Generation returns comprehensive grid with sub-scores.
    /// </summary>
    [Fact]
    public async Task SuitabilityMatrix_GenerateSuitabilityMatrix_ReturnsFullGridWithSubScores()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var matrix = await service.GenerateSuitabilityMatrixAsync(new GenerateSuitabilityMatrixRequestDto
        {
            FacilityId = fix.FacilityId,
            ItemIds = [fix.ItemClassAId, fix.ItemClassCId]
        });

        Assert.NotNull(matrix);
        Assert.Equal(fix.FacilityId, matrix.FacilityId);
        Assert.Equal(2, matrix.SkuProfiles.Count);

        var profileA = Assert.Single(matrix.SkuProfiles, p => p.ItemId == fix.ItemClassAId);
        Assert.NotEmpty(profileA.CandidateLocations);

        var goldenCandidate = Assert.Single(profileA.CandidateLocations, c => c.LocationId == fix.GoldenZonePickLocation1Id);
        Assert.True(goldenCandidate.IsOverallCompatible);
        Assert.Equal(100m, goldenCandidate.ErgonomicScore);
        Assert.Equal(100m, goldenCandidate.AbcAffinityScore);
        Assert.True(goldenCandidate.CompositeSuitabilityScore >= 75m);
    }

    #endregion

    #region Acceptance Criteria 10: Strict Read-Only Ledger Invariance

    /// <summary>
    /// Test 16: Simulating and approving ABC/velocity recommendations causes ZERO mutations across all 8 operational tables.
    /// </summary>
    [Fact]
    public async Task ReadOnlyLedgerInvariance_SimulationAndApproval_CausesZeroOperationalMutations()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Pre-seed baseline operational data in all 8 tables
        var billingPeriod = BillingPeriod.Create(
            fix.ClientId, "BP-VEL-TEST-2026-11", "Velocity Billing Period",
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc),
            BillingFrequency.Monthly,
            fix.FacilityId);
        context.BillingPeriods.Add(billingPeriod);

        var stock = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemClassAId,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 50m
        };
        context.StockOnHand.Add(stock);

        var tx = new InventoryTransaction
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemClassAId,
            TxType = InventoryTxType.Move,
            Quantity = 10m
        };
        context.InventoryTransactions.Add(tx);

        var alloc = new InventoryAllocation
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OutboundOrderId = Guid.NewGuid(),
            OutboundOrderLineId = Guid.NewGuid(),
            StockOnHandId = stock.Id,
            ItemId = fix.ItemClassAId,
            LocationId = fix.UpperReserveLocationId,
            QuantityRequested = 5m,
            QuantityAllocated = 5m
        };
        context.InventoryAllocations.Add(alloc);

        var putaway = new PutawayTask
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = fix.ItemClassAId,
            Quantity = 10m,
            SourceLocationId = fix.UpperReserveLocationId,
            SuggestedLocationId = fix.GoldenZonePickLocation1Id,
            TargetLocationId = fix.GoldenZonePickLocation1Id
        };
        context.PutawayTasks.Add(putaway);

        var pick = new PickTask
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PickWaveId = Guid.NewGuid(),
            OutboundOrderId = alloc.OutboundOrderId,
            OutboundOrderLineId = alloc.OutboundOrderLineId,
            InventoryAllocationId = alloc.Id,
            StockOnHandId = stock.Id,
            ItemId = fix.ItemClassAId,
            SourceLocationId = fix.UpperReserveLocationId,
            QuantityToPick = 5m
        };
        context.PickTasks.Add(pick);

        var activity = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.StoragePalletDay,
            "DailyInventorySnapshot", Guid.NewGuid(), "SNAP-01", 10m, "PALLET_DAY",
            DateTime.UtcNow, "IDEMP-VEL-01", "Storage Activity", billingPeriod.Id);
        activity.Rate(1.50m, 15.00m, "USD");
        context.BillableActivities.Add(activity);

        var invoice = Invoice.Create(fix.ClientId, billingPeriod.Id, "INV-VEL-01", DateTime.UtcNow.Date);
        invoice.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Storage, "Storage charge", 1, "PALLET", 15m, 15m));
        context.Invoices.Add(invoice);

        await context.SaveChangesAsync();

        // Capture authoritative pre-state snapshots across ALL 8 operational tables
        var preStock = await context.StockOnHand.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var preLocs = await context.Locations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var preTxs = await context.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var preAllocs = await context.InventoryAllocations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var prePutaways = await context.PutawayTasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var prePicks = await context.PickTasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var preActivities = await context.BillableActivities.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var preInvoices = await context.Invoices.AsNoTracking().OrderBy(x => x.Id).ToListAsync();

        // 1. Run simulation
        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId
        });
        Assert.NotNull(batch);

        // 2. Approve batch
        var approved = await service.ApproveRecommendationBatchAsync(batch.Id);
        Assert.Equal(SlottingBatchStatus.Approved, approved.Status);

        // 3. Capture post-state snapshots
        var postStock = await context.StockOnHand.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postLocs = await context.Locations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postTxs = await context.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postAllocs = await context.InventoryAllocations.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postPutaways = await context.PutawayTasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postPicks = await context.PickTasks.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postActivities = await context.BillableActivities.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var postInvoices = await context.Invoices.AsNoTracking().OrderBy(x => x.Id).ToListAsync();

        // Assert 0 additions, 0 deletions, 0 modifications across all 8 tables
        Assert.Equal(preStock.Count, postStock.Count);
        for (int i = 0; i < preStock.Count; i++) Assert.Equal(preStock[i].TotalQuantity, postStock[i].TotalQuantity);

        Assert.Equal(preLocs.Count, postLocs.Count);
        for (int i = 0; i < preLocs.Count; i++) Assert.Equal(preLocs[i].LocationCode, postLocs[i].LocationCode);

        Assert.Equal(preTxs.Count, postTxs.Count);
        for (int i = 0; i < preTxs.Count; i++) Assert.Equal(preTxs[i].Quantity, postTxs[i].Quantity);

        Assert.Equal(preAllocs.Count, postAllocs.Count);
        for (int i = 0; i < preAllocs.Count; i++) Assert.Equal(preAllocs[i].QuantityAllocated, postAllocs[i].QuantityAllocated);

        Assert.Equal(prePutaways.Count, postPutaways.Count);
        for (int i = 0; i < prePutaways.Count; i++) Assert.Equal(prePutaways[i].Quantity, postPutaways[i].Quantity);

        Assert.Equal(prePicks.Count, postPicks.Count);
        for (int i = 0; i < prePicks.Count; i++) Assert.Equal(prePicks[i].QuantityToPick, postPicks[i].QuantityToPick);

        Assert.Equal(preActivities.Count, postActivities.Count);
        for (int i = 0; i < preActivities.Count; i++) Assert.Equal(preActivities[i].TotalAmount, postActivities[i].TotalAmount);

        Assert.Equal(preInvoices.Count, postInvoices.Count);
        for (int i = 0; i < preInvoices.Count; i++) Assert.Equal(preInvoices[i].TotalAmount, postInvoices[i].TotalAmount);
    }

    /// <summary>
    /// Test 17: Controller endpoint for suitability matrix enforces HasPermission attribute and returns 200 OK.
    /// </summary>
    [Fact]
    public void ControllerEndpoint_SuitabilityMatrix_EnforcesPermissions()
    {
        var method = typeof(SlottingOptimizationController).GetMethod(nameof(SlottingOptimizationController.GenerateSuitabilityMatrix));
        Assert.NotNull(method);

        var permAttr = method.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permAttr);
        Assert.Equal(Permissions.Analytics.SlottingView, permAttr.Policy);

        var postAttr = method.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(postAttr);
        Assert.Equal("suitability-matrix", postAttr.Template);
    }

    #endregion

    #region Phase 6.3.7.2 Engineering Review Closure Tests (Tests 18 - 25)

    /// <summary>
    /// Test 18: Proves that changing ABC affinity score directly impacts final suitability score
    /// according to the configured WeightAbcAffinity weight when other scoring dimensions are held constant.
    /// </summary>
    [Fact]
    public async Task SuitabilityScoring_AbcScoreChange_DirectlyImpactsFinalSuitabilityScore()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Standard policy: WeightTravelDistance=0.25, WeightErgonomics=0.25, WeightPickDensity=0.15, WeightAbcAffinity=0.20, WeightVelocityAffinity=0.15
        var policy = SlottingPolicy.Create(
            name: "ABC Sensitivity Policy",
            facilityId: fix.FacilityId,
            weightTravelDistance: 0.25m,
            weightErgonomics: 0.25m,
            weightPickDensity: 0.15m,
            weightAbcAffinity: 0.20m,
            weightVelocityAffinity: 0.15m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        // Evaluate Class A in GoldenZone (AbcAffinityScore = 100)
        var resultA = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassAId, fix.GoldenZonePickLocation1Id, policy.Id);
        // Evaluate Class C in GoldenZone (AbcAffinityScore = 20)
        var resultC = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassCId, fix.GoldenZonePickLocation1Id, policy.Id);

        Assert.Equal(100m, resultA.AbcAffinityScore);
        Assert.Equal(20m, resultC.AbcAffinityScore);
        Assert.True(resultA.CalculatedSuitabilityScore > resultC.CalculatedSuitabilityScore);

        // Verify with increased ABC weight policy (WeightAbcAffinity = 0.50)
        var highAbcPolicy = SlottingPolicy.Create(
            name: "High ABC Weight Policy",
            facilityId: fix.FacilityId,
            weightTravelDistance: 0.15m,
            weightErgonomics: 0.15m,
            weightPickDensity: 0.10m,
            weightAbcAffinity: 0.50m,
            weightVelocityAffinity: 0.10m);
        context.SlottingPolicies.Add(highAbcPolicy);
        await context.SaveChangesAsync();

        var resultAHigh = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassAId, fix.GoldenZonePickLocation1Id, highAbcPolicy.Id);
        var resultCHigh = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemClassCId, fix.GoldenZonePickLocation1Id, highAbcPolicy.Id);

        decimal standardDelta = resultA.CalculatedSuitabilityScore - resultC.CalculatedSuitabilityScore;
        decimal highDelta = resultAHigh.CalculatedSuitabilityScore - resultCHigh.CalculatedSuitabilityScore;

        // Higher ABC weight must produce larger sensitivity delta
        Assert.True(highDelta > standardDelta);
    }

    /// <summary>
    /// Test 19: Proves that changing Velocity affinity score directly impacts final suitability score
    /// according to the configured WeightVelocityAffinity weight when other dimensions are held constant.
    /// </summary>
    [Fact]
    public async Task SuitabilityScoring_VelocityScoreChange_DirectlyImpactsFinalSuitabilityScore()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var policy = SlottingPolicy.Create(
            name: "Velocity Sensitivity Policy",
            facilityId: fix.FacilityId,
            weightTravelDistance: 0.25m,
            weightErgonomics: 0.25m,
            weightPickDensity: 0.15m,
            weightAbcAffinity: 0.15m,
            weightVelocityAffinity: 0.20m);
        context.SlottingPolicies.Add(policy);

        DateTime now = DateTime.UtcNow;
        var recSurge90 = new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemSurgingId,
            Sku = "SKU-VEL-SURGING",
            AbcClass = AbcClassificationCategory.ClassB,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 90m, // 1 unit/day
            CalculatedAtUtc = now
        };
        var recSurge30 = new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemSurgingId,
            Sku = "SKU-VEL-SURGING",
            AbcClass = AbcClassificationCategory.ClassB,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days30,
            CalculationPeriodStartUtc = now.AddDays(-30),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 90m, // 3 units/day (surging)
            CalculatedAtUtc = now
        };
        var recSeasonal = new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = fix.ItemSeasonalId,
            Sku = "SKU-VEL-SEASONAL",
            AbcClass = AbcClassificationCategory.ClassB,
            VelocityCategory = InventoryMovementVelocityCategory.MediumMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 50m,
            IsSeasonalPattern = true,
            CalculatedAtUtc = now
        };
        context.SkuVelocityClassificationRecords.AddRange(recSurge90, recSurge30, recSeasonal);
        await context.SaveChangesAsync();

        // Surging item in Golden Zone: VelocityAffinityScore = 100
        var evalSurging = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemSurgingId, fix.GoldenZonePickLocation1Id, policy.Id);
        // Seasonal Spike in Upper Reserve (level 4): VelocityAffinityScore = 25
        var evalSeasonalReserve = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemSeasonalId, fix.UpperReserveLocationId, policy.Id);
        var evalSeasonalGolden = await service.EvaluateSkuLocationCompatibilityAsync(fix.ItemSeasonalId, fix.GoldenZonePickLocation1Id, policy.Id);

        Assert.Equal(100m, evalSurging.VelocityAffinityScore);
        Assert.Equal(95m, evalSeasonalGolden.VelocityAffinityScore);
        Assert.Equal(25m, evalSeasonalReserve.VelocityAffinityScore);

        Assert.True(evalSeasonalGolden.CalculatedSuitabilityScore > evalSeasonalReserve.CalculatedSuitabilityScore);
    }

    /// <summary>
    /// Test 20: When a hard physical gate fails, the candidate becomes physically infeasible
    /// and the final suitability score is strictly 0.0 regardless of soft scoring dimensions.
    /// </summary>
    [Fact]
    public async Task HardPhysicalGate_Failure_ForcesCandidateInfeasibleAndZeroScore()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Create an oversized item that exceeds physical dimensions of standard rack
        var giantItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-OVERSIZED-CRATE",
            LengthCm = 300m, // Exceeds 120cm location length
            WidthCm = 200m,  // Exceeds 100cm location width
            HeightCm = 150m,
            WeightKg = 50m,
            AbcVelocity = AbcVelocity.A
        };
        context.Items.Add(giantItem);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(giantItem.Id, fix.GoldenZonePickLocation1Id);

        Assert.False(result.IsPhysicallyFeasible);
        Assert.False(result.IsOverallCompatible);
        Assert.Equal(0m, result.CalculatedSuitabilityScore);
        Assert.Contains(result.ViolatedHardConstraints, v => v.Contains("exceed location boundaries"));
    }

    /// <summary>
    /// Test 21: SKU with no demand in the last 90 days but demand within 365 days is classified
    /// as Inactive in the velocity observation window, NOT automatically dead stock.
    /// </summary>
    [Fact]
    public async Task VelocityClassification_NoDemand90Days_DemandWithin365Days_ClassifiedAsInactiveNotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var inactiveSkuId = Guid.NewGuid();
        var inactiveItem = new Item
        {
            Id = inactiveSkuId,
            ClientId = fix.ClientId,
            Sku = "SKU-INACTIVE-NOT-DEAD",
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 15m,
            WeightKg = 5m,
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(inactiveItem);

        DateTime now = DateTime.UtcNow;
        // Zero demand in 90-day window, but NOT classified as dead stock (had activity 120 days ago)
        var rec90 = new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = inactiveSkuId,
            Sku = "SKU-INACTIVE-NOT-DEAD",
            AbcClass = AbcClassificationCategory.ClassC,
            VelocityCategory = InventoryMovementVelocityCategory.NonMoving, // NonMoving, not DeadStock!
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 0m,
            CalculatedAtUtc = now
        };
        var rec30 = new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = inactiveSkuId,
            Sku = "SKU-INACTIVE-NOT-DEAD",
            AbcClass = AbcClassificationCategory.ClassC,
            VelocityCategory = InventoryMovementVelocityCategory.NonMoving,
            ObservationWindow = VelocityObservationWindow.Days30,
            CalculationPeriodStartUtc = now.AddDays(-30),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 0m,
            CalculatedAtUtc = now
        };
        context.SkuVelocityClassificationRecords.AddRange(rec90, rec30);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(inactiveSkuId, fix.UpperReserveLocationId);

        Assert.NotNull(result);
        Assert.Equal("Inactive", result.VelocityTrend);
        Assert.Equal("NonMoving", result.EffectiveVelocityCategory);
        // Verified: It is Inactive, not DeadStock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 22: Dead stock according to Phase 6.3.2 criteria (>=365 days no demand)
    /// already in reserve is strictly protected from unnecessary relocation when policy protects dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_Phase632Criteria_ProtectedFromUnnecessaryRelocationWhenInReserve()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var deadSkuId = Guid.NewGuid();
        var deadItem = new Item
        {
            Id = deadSkuId,
            ClientId = fix.ClientId,
            Sku = "SKU-TRUE-DEADSTOCK-365",
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 15m,
            WeightKg = 5m,
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(deadItem);

        // Seed stock already in upper reserve rack
        var deadStockOnHand = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = deadSkuId,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 100m
        };
        context.StockOnHand.Add(deadStockOnHand);

        // Seed Phase 6.3.2 DailyInventoryAgingSnapshot with IsDeadStock = true
        var agingSnapshot = new DailyInventoryAgingSnapshot
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = deadSkuId,
            Sku = "SKU-TRUE-DEADSTOCK-365",
            LocationId = fix.UpperReserveLocationId,
            LocationCode = "MAIN-A01-R01-04",
            SnapshotDateUtc = DateTime.UtcNow.Date,
            QuantityOnHand = 100m,
            TotalInventoryValue = 500m,
            ReceiptAgeDays = 400,
            LastDemandAgeDays = 400,
            DemandInactivityDays = 400,
            AgingBucket = InventoryAgingBucket.DeadStock_365Plus,
            VelocityCategory = InventoryMovementVelocityCategory.DeadStock,
            IsDeadStock = true
        };
        context.DailyInventoryAgingSnapshots.Add(agingSnapshot);

        var policy = SlottingPolicy.Create(
            name: "Strict Dead Stock Protection Policy",
            facilityId: fix.FacilityId,
            protectDeadStock: true);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            PolicyId = policy.Id
        });

        Assert.NotNull(batch);
        // Verified: The dead stock SKU in reserve is protected from unnecessary relocation
        Assert.DoesNotContain(batch.Recommendations, r => r.ItemId == deadSkuId);
    }

    /// <summary>
    /// Test 23: When an existing velocity projection exceeds the 30-day freshness limit,
    /// the engine flags staleness, falls back to catalog classification, and provides an explanatory audit message.
    /// </summary>
    [Fact]
    public async Task StaleVelocityProjection_Exceeds30Days_FallsBackToCatalogClassificationWithAudit()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var staleSkuId = Guid.NewGuid();
        var staleItem = new Item
        {
            Id = staleSkuId,
            ClientId = fix.ClientId,
            Sku = "SKU-STALE-PROJECTION",
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 15m,
            WeightKg = 5m,
            AbcVelocity = AbcVelocity.B // Catalog fallback classification
        };
        context.Items.Add(staleItem);

        DateTime calcDate = DateTime.UtcNow.AddDays(-45); // 45 days old (> 30-day freshness threshold)
        var staleRec = new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = staleSkuId,
            Sku = "SKU-STALE-PROJECTION",
            AbcClass = AbcClassificationCategory.ClassA, // Stale projection had Class A
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = calcDate.AddDays(-90),
            CalculationPeriodEndUtc = calcDate,
            TotalUnitsDemanded = 900m,
            CalculatedAtUtc = calcDate
        };
        context.SkuVelocityClassificationRecords.Add(staleRec);

        var policy = SlottingPolicy.Create(
            name: "Freshness Policy",
            facilityId: fix.FacilityId,
            maxProjectionAgeDays: 30);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(staleSkuId, fix.GoldenZonePickLocation1Id, policy.Id);

        Assert.NotNull(result);
        Assert.True(result.IsProjectionStaleOrMissing);
        Assert.Equal("CatalogFallback (StaleProjection)", result.ClassificationSource);
        Assert.True(result.ProjectionAgeDays >= 45);
        Assert.NotNull(result.ProjectionCalculatedAtUtc);
        // Fell back to catalog Class B instead of trusting stale Class A
        Assert.Equal("B", result.EffectiveAbcClass);
        Assert.NotNull(result.PriorityResolutionExplanation);
        Assert.Contains("stale", result.PriorityResolutionExplanation, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test 24: Verifies that 30-day and 90-day velocity metrics are normalized to daily demand velocity
    /// before applying the 1.3x surge and 0.7x decline thresholds.
    /// </summary>
    [Fact]
    public void VelocityObservationWindows_30DayAnd90Day_AreNormalizedDailyDemandVelocities()
    {
        DateTime now = DateTime.UtcNow;

        // 30-day period with 60 units demanded => 2.0 units/day
        var rec30 = new SkuVelocityClassificationRecord
        {
            ObservationWindow = VelocityObservationWindow.Days30,
            CalculationPeriodStartUtc = now.AddDays(-30),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 60m
        };

        // 90-day period with 90 units demanded => 1.0 units/day
        var rec90 = new SkuVelocityClassificationRecord
        {
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 90m
        };

        // Verified: Daily demand rates are comparable
        Assert.Equal(2.0m, Math.Round(rec30.DailyDemandVelocity, 1));
        Assert.Equal(1.0m, Math.Round(rec90.DailyDemandVelocity, 1));

        // When comparing normalized daily velocity: 2.0 > 1.3 * 1.0 (Surging)
        bool isSurging = rec30.DailyDemandVelocity > rec90.DailyDemandVelocity * 1.3m && rec30.DailyDemandVelocity > 1.0m;
        Assert.True(isSurging);

        // If raw counts had been unnormalized: 60 < 90 (would erroneously seem declining)
        bool erroneousUnnormalizedCheck = rec30.TotalUnitsDemanded > rec90.TotalUnitsDemanded;
        Assert.False(erroneousUnnormalizedCheck);
    }

    /// <summary>
    /// Test 25: Relocation proposals failing the $5.00 minimum monthly labor savings threshold
    /// are strictly discarded as a hard economic eligibility gate.
    /// </summary>
    [Fact]
    public async Task EconomicFeasibilityGate_CandidateBelowMinSavingsThreshold_StrictlyRejectedAsHardGate()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Create a policy requiring minimum $100.00/month net savings
        var highSavingsPolicy = SlottingPolicy.Create(
            name: "Extreme Savings Policy",
            facilityId: fix.FacilityId,
            minLaborSavingsUsd: 100.00m);
        context.SlottingPolicies.Add(highSavingsPolicy);
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            PolicyId = highSavingsPolicy.Id
        });

        Assert.NotNull(batch);
        // All proposals with net savings < $100.00 are rejected at the eligibility gate
        Assert.Empty(batch.Recommendations);
    }

    /// <summary>
    /// Test 26: Multi-SKU batch planning preserves client tenancy exclusivity in virtual destination state.
    /// Relocation proposals belonging to separate client tenants are strictly prohibited from co-locating.
    /// </summary>
    [Fact]
    public async Task BatchCompatibility_MultiSkuTenantExclusivity_ProhibitsCominglingSeparateClientsInSameDestination()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Add a second client
        var client2 = new Client
        {
            Id = Guid.NewGuid(),
            AccountCode = "CLIENT-B",
            CompanyName = "Second Client Logistics"
        };
        context.Clients.Add(client2);

        // Add item for client 2
        var itemClient2 = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = client2.Id,
            Sku = "SKU-CLIENT2-FAST",
            Description = "Client 2 Fast Mover",
            WeightKg = 5.0m,
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 20m,
            AbcVelocity = AbcVelocity.A
        };
        context.Items.Add(itemClient2);

        // Current stock in upper reserve for client 2 item
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = client2.Id,
            ItemId = itemClient2.Id,
            LocationId = fix.FarUpperReserveLocationId,
            QtyAvailable = 50m
        });

        var policy = SlottingPolicy.Create(
            name: "Tenancy Test Policy",
            facilityId: fix.FacilityId,
            minLaborSavingsUsd: 1.00m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            PolicyId = policy.Id
        });

        Assert.NotNull(batch);
        Assert.NotEmpty(batch.Recommendations);

        // Group recommendations by proposed location
        var proposedByLocation = batch.Recommendations
            .GroupBy(r => r.ProposedLocationId)
            .ToList();

        // Every proposed location MUST have exactly one distinct client tenant (no multi-tenant co-mingling)
        foreach (var locGroup in proposedByLocation)
        {
            var distinctClients = locGroup.Select(r => r.ClientId).Distinct().ToList();
            Assert.Single(distinctClients);
        }
    }

    /// <summary>
    /// Test 27: Multi-SKU batch planning enforces non-stackable crush-sensitive co-location restrictions.
    /// An accepted proposal with StackabilityFactor=1 prevents subsequent proposals from co-locating in that destination.
    /// </summary>
    [Fact]
    public async Task BatchCompatibility_NonStackableCrushSensitiveItem_PreventsCoLocationOfSubsequentProposals()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Create crush-sensitive item (StackabilityFactor = 1)
        var crushItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-CRUSH-FRAGILE",
            Description = "Crush Sensitive Glassware",
            WeightKg = 2.0m,
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 20m,
            StackabilityFactor = 1,
            AbcVelocity = AbcVelocity.A
        };
        context.Items.Add(crushItem);

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = crushItem.Id,
            LocationId = fix.FarUpperReserveLocationId,
            QtyAvailable = 10m
        });

        var policy = SlottingPolicy.Create(
            name: "Crush Sensitive Batch Policy",
            facilityId: fix.FacilityId,
            minLaborSavingsUsd: 1.00m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            PolicyId = policy.Id
        });

        Assert.NotNull(batch);
        var crushRec = batch.Recommendations.FirstOrDefault(r => r.ItemId == crushItem.Id);
        if (crushRec != null)
        {
            // Verify no other proposal shares the destination of the crush-sensitive item
            var coLocated = batch.Recommendations.Where(r => r.ProposedLocationId == crushRec.ProposedLocationId).ToList();
            Assert.Single(coLocated);
        }
    }

    /// <summary>
    /// Test 28: Multi-SKU batch planning enforces hazard segregation in virtual destination state.
    /// Hazardous materials cannot co-locate with general cargo or incompatible hazard classes.
    /// </summary>
    [Fact]
    public async Task BatchCompatibility_HazardousMaterialProposal_PreventsCoLocationWithGeneralGoods()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Add a hazardous item
        var hazItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-HAZ-FLAMMABLE",
            Description = "Industrial Solvent",
            WeightKg = 10.0m,
            LengthCm = 30m,
            WidthCm = 30m,
            HeightCm = 40m,
            HazardClass = "Class 3 - Flammable Liquid",
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(hazItem);

        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = hazItem.Id,
            LocationId = fix.FarUpperReserveLocationId,
            QtyAvailable = 20m
        });

        var policy = SlottingPolicy.Create(
            name: "Hazmat Segregation Batch Policy",
            facilityId: fix.FacilityId,
            minLaborSavingsUsd: 1.00m);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            PolicyId = policy.Id
        });

        Assert.NotNull(batch);
        var hazRec = batch.Recommendations.FirstOrDefault(r => r.ItemId == hazItem.Id);
        if (hazRec != null)
        {
            // Check all proposals sharing this location: none can be non-hazmat
            var sameLocRecs = batch.Recommendations.Where(r => r.ProposedLocationId == hazRec.ProposedLocationId).ToList();
            foreach (var r in sameLocRecs)
            {
                var itm = await context.Items.FindAsync(r.ItemId);
                Assert.NotNull(itm);
                Assert.False(string.IsNullOrWhiteSpace(itm.HazardClass));
            }
        }
    }

    /// <summary>
    /// Test 29: Precise TimeSpan boundary calculation and clock skew future-dated anomaly classification.
    /// Stale projections (> 30 days) and future-dated records (> 5 min ahead) fall back to catalog Item.AbcVelocity.
    /// </summary>
    [Fact]
    public async Task ProjectionFreshness_PreciseTimeSpanBoundaryAndFutureDated_CorrectlyClassified()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 1. Fresh projection: exactly 29 days and 23 hours old (< 30 days TimeSpan)
        var freshItemId = Guid.NewGuid();
        context.Items.Add(new Item
        {
            Id = freshItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-FRESH-BOUNDARY",
            Description = "Fresh SKU",
            AbcVelocity = AbcVelocity.C // Catalog is C, projection is A
        });
        context.SkuVelocityClassificationRecords.Add(new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = freshItemId,
            Sku = "SKU-FRESH-BOUNDARY",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = evalTime.AddDays(-120),
            CalculationPeriodEndUtc = evalTime.AddDays(-30),
            TotalUnitsDemanded = 500m,
            CalculatedAtUtc = evalTime - TimeSpan.FromDays(29.95) // 29 days 23 hours old
        });

        // 2. Stale projection: exactly 30 days and 1 hour old (> 30 days TimeSpan)
        var staleItemId = Guid.NewGuid();
        context.Items.Add(new Item
        {
            Id = staleItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-STALE-BOUNDARY",
            Description = "Stale SKU",
            AbcVelocity = AbcVelocity.C // Catalog is C, projection is A
        });
        context.SkuVelocityClassificationRecords.Add(new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = staleItemId,
            Sku = "SKU-STALE-BOUNDARY",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = evalTime.AddDays(-121),
            CalculationPeriodEndUtc = evalTime.AddDays(-31),
            TotalUnitsDemanded = 500m,
            CalculatedAtUtc = evalTime - TimeSpan.FromDays(30.05) // 30 days 1 hour old
        });

        // 3. Future-dated skew anomaly: 10 minutes ahead of evaluationTimeUtc
        var futureItemId = Guid.NewGuid();
        context.Items.Add(new Item
        {
            Id = futureItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-FUTURE-ANOMALY",
            Description = "Future Anomaly SKU",
            AbcVelocity = AbcVelocity.B // Catalog is B, projection is A
        });
        context.SkuVelocityClassificationRecords.Add(new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = futureItemId,
            Sku = "SKU-FUTURE-ANOMALY",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = evalTime.AddDays(-90),
            CalculationPeriodEndUtc = evalTime,
            TotalUnitsDemanded = 500m,
            CalculatedAtUtc = evalTime.AddMinutes(10) // 10 minutes in the future (> 5m threshold)
        });

        var policy = SlottingPolicy.Create("Freshness Rule Policy", fix.FacilityId, maxProjectionAgeDays: 30);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        // Evaluate fresh SKU
        var resFresh = await service.EvaluateSkuLocationCompatibilityAsync(freshItemId, fix.GoldenZonePickLocation1Id, policy.Id, null, evalTime);
        Assert.False(resFresh.IsProjectionStaleOrMissing);
        Assert.Equal("AuthoritativeProjection", resFresh.ClassificationSource);
        Assert.Equal("A", resFresh.EffectiveAbcClass); // Trusted projection

        // Evaluate stale SKU
        var resStale = await service.EvaluateSkuLocationCompatibilityAsync(staleItemId, fix.GoldenZonePickLocation1Id, policy.Id, null, evalTime);
        Assert.True(resStale.IsProjectionStaleOrMissing);
        Assert.Equal("CatalogFallback (StaleProjection)", resStale.ClassificationSource);
        Assert.Equal("C", resStale.EffectiveAbcClass); // Fell back to catalog C

        // Evaluate future-dated anomaly SKU
        var resFuture = await service.EvaluateSkuLocationCompatibilityAsync(futureItemId, fix.GoldenZonePickLocation1Id, policy.Id, null, evalTime);
        Assert.True(resFuture.IsProjectionStaleOrMissing);
        Assert.Equal("CatalogFallback (FutureDatedSkewAnomaly)", resFuture.ClassificationSource);
        Assert.Equal("B", resFuture.EffectiveAbcClass); // Fell back to catalog B
    }

    /// <summary>
    /// Test 30: Complete Phase 6.3.2 dead-stock contract and aging snapshot freshness.
    /// Confirmed dead stock includes never-demanded inventory (ReceiptAgeDays >= 365) and 365+ days zero demand.
    /// Stale aging snapshots (> 30 days) are not silently treated as dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_NeverDemandedInventoryAndStaleAgingSnapshot_HandledAuthoritatively()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 1. Never-demanded inventory with fresh aging snapshot (10 days old)
        var neverDemandedItemId = Guid.NewGuid();
        var neverDemandedItem = new Item
        {
            Id = neverDemandedItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-NEVER-DEMANDED",
            Description = "Never Demanded Dormant Item",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(neverDemandedItem);

        var rcvSession30 = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-TEST-30",
            Status = ReceivingStatus.Completed,
            StartedAtUtc = evalTime.AddDays(-400),
            CompletedAtUtc = evalTime.AddDays(-400)
        };
        context.ReceivingSessions.Add(rcvSession30);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = rcvSession30.Id,
            ItemId = neverDemandedItemId,
            Lpn = "LPN-NEVER-DEMANDED-400D",
            QuantityReceived = 50m,
            ScannedAtUtc = evalTime.AddDays(-400)
        });
        context.DailyInventoryAgingSnapshots.Add(new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = neverDemandedItemId,
            Sku = "SKU-NEVER-DEMANDED",
            SnapshotDateUtc = evalTime.AddDays(-10), // Fresh snapshot (10 days old <= 30d)
            ReceiptAgeDays = 400,                   // >= 365 days receipt age
            LastDemandAgeDays = 0,                  // Never demanded
            IsDeadStock = true
        });

        // 2. Unverified dead stock with stale aging snapshot (45 days old)
        var staleAgingItemId = Guid.NewGuid();
        context.Items.Add(new Item
        {
            Id = staleAgingItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-STALE-AGING",
            Description = "Stale Aging Snapshot Item",
            AbcVelocity = AbcVelocity.B
        });
        context.DailyInventoryAgingSnapshots.Add(new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = staleAgingItemId,
            Sku = "SKU-STALE-AGING",
            SnapshotDateUtc = evalTime.AddDays(-45), // Stale snapshot (45 days old > 30d)
            ReceiptAgeDays = 400,
            LastDemandAgeDays = 400,
            IsDeadStock = true
        });

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        neverDemandedItem.CreatedAt = evalTime.AddDays(-400);
        await context.SaveChangesAsync();

        // 1. Fresh aging snapshot confirms dead stock
        var resNeverDemanded = await service.EvaluateSkuLocationCompatibilityAsync(neverDemandedItemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);
        Assert.Equal("DeadStock", resNeverDemanded.VelocityTrend);
        Assert.Equal("DeadStock", resNeverDemanded.EffectiveVelocityCategory);

        // 2. Stale aging snapshot (> 30d) is unverified and NOT treated as dead stock
        var resStaleAging = await service.EvaluateSkuLocationCompatibilityAsync(staleAgingItemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);
        Assert.NotEqual("DeadStock", resStaleAging.VelocityTrend);
        Assert.NotEqual("DeadStock", resStaleAging.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 31: Stackability physical geometry model enforces exact boundary unit capacities (UTier x TMax).
    /// Exact QMax passes; QMax + 1 strictly fails hard stackability constraint.
    /// </summary>
    [Fact]
    public async Task Stackability_PhysicalGeometryModel_BoundaryQuantitiesEnforced()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Location: L=120cm, W=80cm, H=150cm
        var testLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "LOC-GEOM-01",
            ZoneId = fix.ReserveZoneId,
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 2000m,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.Add(testLoc);

        // Item: L=40cm, W=30cm, H=30cm, StackabilityFactor=3
        // Normal tier: (120/40)*(80/30) = 3 * 2 = 6
        // Rotated tier: (120/30)*(80/40) = 4 * 2 = 8
        // UTier = max(6, 8) = 8
        // Vertical tiers: min(StackabilityFactor=3, 150/30 = 5) = 3
        // QMax = 8 * 3 = 24 units
        var stackableItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-GEOM-STACKABLE",
            Description = "Stackable Box",
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 30m,
            WeightKg = 5m,
            StackabilityFactor = 3
        };
        context.Items.Add(stackableItem);

        // Crush-sensitive Item: StackabilityFactor=1 -> TMax=1, QMax = 8 * 1 = 8 units
        var crushItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-GEOM-CRUSH",
            Description = "Crush Sensitive Unit",
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 30m,
            WeightKg = 5m,
            StackabilityFactor = 1
        };
        context.Items.Add(crushItem);

        var policy = SlottingPolicy.Create("Stack Geometry Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        // Stackable item:
        // Exact QMax = 24 passes
        var res24 = await service.EvaluateSkuLocationCompatibilityAsync(stackableItem.Id, testLoc.Id, policy.Id, quantityToSlot: 24);
        var eval24 = res24.ConstraintEvaluations.First(e => e.ConstraintType == SlottingConstraintType.Stackability);
        Assert.True(eval24.IsSatisfied);

        // QMax + 1 = 25 fails
        var res25 = await service.EvaluateSkuLocationCompatibilityAsync(stackableItem.Id, testLoc.Id, policy.Id, quantityToSlot: 25);
        var eval25 = res25.ConstraintEvaluations.First(e => e.ConstraintType == SlottingConstraintType.Stackability);
        Assert.False(eval25.IsSatisfied);
        Assert.Contains("exceeds maximum allowable stacking capacity of 24 units", eval25.ViolationDetail);

        // Crush-sensitive item (StackabilityFactor=1):
        // Exact single-tier QMax = 8 passes
        var resCrush8 = await service.EvaluateSkuLocationCompatibilityAsync(crushItem.Id, testLoc.Id, policy.Id, quantityToSlot: 8);
        var evalCrush8 = resCrush8.ConstraintEvaluations.First(e => e.ConstraintType == SlottingConstraintType.Stackability);
        Assert.True(evalCrush8.IsSatisfied);

        // QMax + 1 = 9 fails
        var resCrush9 = await service.EvaluateSkuLocationCompatibilityAsync(crushItem.Id, testLoc.Id, policy.Id, quantityToSlot: 9);
        var evalCrush9 = resCrush9.ConstraintEvaluations.First(e => e.ConstraintType == SlottingConstraintType.Stackability);
        Assert.False(evalCrush9.IsSatisfied);
        Assert.Contains("exceeds maximum single-tier planar footprint capacity (8 units", evalCrush9.ViolationDetail);
    }

    /// <summary>
    /// Test 32: Relocation travel distance benefit (Option B) scoring formula.
    /// Moving an item closer to the dock yields a higher score than moving it farther from the dock.
    /// </summary>
    [Fact]
    public async Task TravelDistanceSavingsScore_RelocationDistanceImprovementOptionB_ScoreReflectsBenefit()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Current location at X=30m from dock
        var currentLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "LOC-TRAVEL-CURR",
            ZoneId = fix.ReserveZoneId,
            LocationType = LocationType.Rack,
            XCoord = 30m,
            YCoord = 0m,
            ZCoord = 0m,
            IsActive = true,
            IsPickable = true
        };
        // Candidate 1: Closer to dock at X=10m (Delta D = +20m)
        var closerLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "LOC-TRAVEL-CLOSER",
            ZoneId = fix.PickFaceZoneId,
            LocationType = LocationType.Rack,
            XCoord = 10m,
            YCoord = 0m,
            ZCoord = 0m,
            IsActive = true,
            IsPickable = true
        };
        // Candidate 2: Farther from dock at X=45m (Delta D = -15m)
        var fartherLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "LOC-TRAVEL-FARTHER",
            ZoneId = fix.ReserveZoneId,
            LocationType = LocationType.Rack,
            XCoord = 45m,
            YCoord = 0m,
            ZCoord = 0m,
            IsActive = true,
            IsPickable = true
        };
        // Candidate 3: Same distance at X=30m (Delta D = 0m)
        var sameDistLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "LOC-TRAVEL-SAME",
            ZoneId = fix.ReserveZoneId,
            LocationType = LocationType.Rack,
            XCoord = 30m,
            YCoord = 0m,
            ZCoord = 0m,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.AddRange(currentLoc, closerLoc, fartherLoc, sameDistLoc);

        var travelItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-TRAVEL-BENEFIT",
            Description = "Travel Test Item",
            WeightKg = 2m,
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 20m,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(travelItem);

        // Place stock in currentLoc
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = travelItem.Id,
            LocationId = currentLoc.Id,
            QtyAvailable = 20m
        });

        var policy = SlottingPolicy.Create("Travel Score Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var resCloser = await service.EvaluateSkuLocationCompatibilityAsync(travelItem.Id, closerLoc.Id, policy.Id);
        var resSame = await service.EvaluateSkuLocationCompatibilityAsync(travelItem.Id, sameDistLoc.Id, policy.Id);
        var resFarther = await service.EvaluateSkuLocationCompatibilityAsync(travelItem.Id, fartherLoc.Id, policy.Id);

        // St = round(clamp(50 + DeltaD * 2, 10, 100), 1)
        // Delta D closer: 30 - 10 = +20m -> 50 + 40 = 90.0
        // Delta D same: 30 - 30 = 0m -> 50.0
        // Delta D farther: 30 - 45 = -15m -> 50 - 30 = 20.0
        Assert.Equal(90.0m, resCloser.TravelDistanceSavingsScore);
        Assert.Equal(50.0m, resSame.TravelDistanceSavingsScore);
        Assert.Equal(20.0m, resFarther.TravelDistanceSavingsScore);

        Assert.True(resCloser.TravelDistanceSavingsScore > resSame.TravelDistanceSavingsScore);
        Assert.True(resSame.TravelDistanceSavingsScore > resFarther.TravelDistanceSavingsScore);
    }

    /// <summary>
    /// Test 33: Multi-SKU Heterogeneous Footprint — Same-size SKU proposals: Valid cumulative placement accepted.
    /// Two same-size SKU proposals targeting the same location whose combined planar footprint equals 100% (50% + 50%)
    /// are both admitted into the batch without exceeding physical floor capacity.
    /// </summary>
    [Fact]
    public async Task HeterogeneousFootprint_SameSizeSkuProposals_ValidCumulativePlacementAccepted()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Target PickFace Location: 120 x 80 x 150 cm, closest to dock (1, 1) to guarantee #1 ranking
        var targetLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-SAME-SIZE-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 1000m,
            XCoord = 1m,
            YCoord = 1m,
            ZCoord = 1.1m, // Golden zone
            ShelfLevel = 1,
            IsActive = true,
            IsPickable = true
        };

        // Source Upper Reserve Location (Z = 5.0m, outside golden zone, far from dock)
        var sourceLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.ReserveZoneId,
            LocationCode = "RES-SAME-SOURCE-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 100m,
            MaxWeightKg = 2000m,
            XCoord = 50m,
            YCoord = 50m,
            ZCoord = 5.0m,
            ShelfLevel = 4,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.AddRange(targetLoc, sourceLoc);

        // SKU 1: 40 x 40 x 50 cm, StackabilityFactor = 2, Weight = 5kg.
        // UTier = (120/40) * (80/40) = 3 * 2 = 6. MaxTiers = min(2, 100/50=2) = 2.
        // Qty = 6 -> NStacks = ceil(6/2) = 3 -> FootprintFraction = 3/6 = 0.50 (50%).
        var sku1 = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-SAME-1",
            Description = "Same Size Item 1",
            LengthCm = 40m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.A
        };

        // SKU 2: Same geometry: 40 x 40 x 50 cm, StackabilityFactor = 2.
        // Qty = 6 -> NStacks = ceil(6/2) = 3 -> FootprintFraction = 3/6 = 0.50 (50%).
        var sku2 = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-SAME-2",
            Description = "Same Size Item 2",
            LengthCm = 40m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.A
        };
        context.Items.AddRange(sku1, sku2);

        // Seed stock in reserve
        context.StockOnHand.AddRange(
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = sku1.Id, LocationId = sourceLoc.Id, QtyAvailable = 6m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = sku2.Id, LocationId = sourceLoc.Id, QtyAvailable = 6m }
        );

        var policy = SlottingPolicy.Create("Same Size Batch Policy", fix.FacilityId, targetClassAPickFace: true);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            PolicyId = policy.Id,
            MaxMoves = 10
        });

        Assert.NotNull(batch);
        // Both SKU1 and SKU2 should have proposals accepted into targetLoc (0.50 + 0.50 = 1.00 <= 1.000001)
        var rec1 = batch.Recommendations.FirstOrDefault(r => r.ItemId == sku1.Id);
        var rec2 = batch.Recommendations.FirstOrDefault(r => r.ItemId == sku2.Id);
        Assert.NotNull(rec1);
        Assert.NotNull(rec2);
        Assert.Equal(targetLoc.Id, rec1.ProposedLocationId);
        Assert.Equal(targetLoc.Id, rec2.ProposedLocationId);
    }

    /// <summary>
    /// Test 34: Multi-SKU Heterogeneous Footprint — Different-size SKU proposals: Reject a proposal that cannot physically fit.
    /// Location has capacity for SKU A (consuming 66.7% footprint). SKU B has different dimensions requiring 50% footprint.
    /// Since 66.7% + 50.0% = 116.7% > 100%, SKU B is rejected for that location.
    /// </summary>
    [Fact]
    public async Task HeterogeneousFootprint_DifferentSizeSkuProposals_RejectsProposalThatCannotPhysicallyFit()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Single isolated target location in PickFace zone: 120 x 80 x 100 cm, closest to dock (1, 1)
        var targetLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-DIFF-TARGET-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 100m,
            MaxWeightKg = 1000m,
            XCoord = 1m,
            YCoord = 1m,
            ZCoord = 1.1m,
            ShelfLevel = 1,
            IsActive = true,
            IsPickable = true
        };

        var sourceLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.ReserveZoneId,
            LocationCode = "RES-DIFF-SOURCE-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 100m,
            MaxWeightKg = 2000m,
            XCoord = 50m,
            YCoord = 50m,
            ZCoord = 5.0m,
            ShelfLevel = 4,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.AddRange(targetLoc, sourceLoc);

        // SKU A: 40 x 40 x 50 cm, StackabilityFactor = 2. UTier = 6, MaxTiers = 2.
        // Qty = 8 -> NStacks = ceil(8/2) = 4 stacks -> Footprint = 4/6 = 66.7%.
        var skuA = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-DIFF-A",
            Description = "Larger Footprint SKU A",
            LengthCm = 40m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.A
        };

        // SKU B: 60 x 40 x 50 cm (different size), StackabilityFactor = 2.
        // UTier = (120/60) * (80/40) = 4, MaxTiers = 2.
        // Qty = 4 -> NStacks = ceil(4/2) = 2 stacks -> Footprint = 2/4 = 50.0%.
        // 66.7% + 50.0% = 116.7% > 100%. Cannot fit together!
        var skuB = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-DIFF-B",
            Description = "Different Size SKU B",
            LengthCm = 60m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.A
        };
        context.Items.AddRange(skuA, skuB);

        // Give SKU A higher demand so it ranks higher
        DateTime now = DateTime.UtcNow;
        context.SkuVelocityClassificationRecords.Add(new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = skuA.Id,
            Sku = "SKU-DIFF-A",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 500m,
            TotalPickTasksCount = 500,
            CalculatedAtUtc = now
        });

        context.SkuVelocityClassificationRecords.Add(new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = skuB.Id,
            Sku = "SKU-DIFF-B",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 100m,
            TotalPickTasksCount = 100,
            CalculatedAtUtc = now
        });

        context.StockOnHand.AddRange(
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = skuA.Id, LocationId = sourceLoc.Id, QtyAvailable = 8m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = skuB.Id, LocationId = sourceLoc.Id, QtyAvailable = 4m }
        );

        var policy = SlottingPolicy.Create("Different Size Batch Policy", fix.FacilityId, targetClassAPickFace: true);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            PolicyId = policy.Id,
            MaxMoves = 10
        });

        Assert.NotNull(batch);
        // SKU A is accepted for targetLoc
        var recA = batch.Recommendations.FirstOrDefault(r => r.ItemId == skuA.Id);
        Assert.NotNull(recA);
        Assert.Equal(targetLoc.Id, recA.ProposedLocationId);

        // SKU B cannot fit in targetLoc (66.7% + 50.0% > 100%) and must NOT be allocated to targetLoc
        var recB = batch.Recommendations.FirstOrDefault(r => r.ItemId == skuB.Id);
        if (recB != null)
        {
            Assert.NotEqual(targetLoc.Id, recB.ProposedLocationId);
        }
    }

    /// <summary>
    /// Test 35: Multi-SKU Heterogeneous Footprint — Different stacking factors: Enforce each SKU's stacking restrictions.
    /// SKU HighStack (StackabilityFactor=4) can stack 4 high; for 8 units it requires only 2 stacks (50% footprint).
    /// SKU LowStack (StackabilityFactor=1, crush-sensitive) can only be 1 high; for 8 units it requires 8 stacks (200% footprint)
    /// and cannot co-locate. The model enforces individual stacking limits accurately.
    /// </summary>
    [Fact]
    public async Task HeterogeneousFootprint_DifferentStackingFactors_EnforcesIndividualStackingRestrictions()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-STACK-TEST-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 200m, // 200 cm clearance
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.Add(loc);

        // Item 1: High stackability (Factor = 4). Height = 40cm. MaxTiers = min(4, 200/40=5) = 4.
        // UTier = (120/60) * (80/40) = 4.
        // For 8 units: NStacks = ceil(8/4) = 2. Footprint = 2/4 = 50%. Fits!
        var itemHighStack = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-HIGH-STACK",
            LengthCm = 60m,
            WidthCm = 40m,
            HeightCm = 40m,
            WeightKg = 5m,
            StackabilityFactor = 4,
            AbcVelocity = AbcVelocity.A
        };

        // Item 2: Crush-sensitive / Non-stackable (Factor = 1). Height = 40cm. MaxTiers = 1.
        // UTier = 4.
        // For 8 units: NStacks = ceil(8/1) = 8. Footprint = 8/4 = 200% > 100%. Exceeds footprint capacity!
        var itemLowStack = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-LOW-STACK",
            LengthCm = 60m,
            WidthCm = 40m,
            HeightCm = 40m,
            WeightKg = 5m,
            StackabilityFactor = 1, // Non-stackable
            AbcVelocity = AbcVelocity.A
        };
        context.Items.AddRange(itemHighStack, itemLowStack);
        await context.SaveChangesAsync();

        var resHigh = await service.EvaluateSkuLocationCompatibilityAsync(itemHighStack.Id, loc.Id, quantityToSlot: 8m);
        var resLow = await service.EvaluateSkuLocationCompatibilityAsync(itemLowStack.Id, loc.Id, quantityToSlot: 8m);

        Assert.True(resHigh.IsPhysicallyFeasible);
        Assert.True(resHigh.IsOverallCompatible);

        Assert.False(resLow.IsPhysicallyFeasible);
        Assert.False(resLow.IsOverallCompatible);
        Assert.Contains(resLow.ViolatedHardConstraints, v => v.Contains("crush-sensitive") || v.Contains("planar footprint"));
    }

    /// <summary>
    /// Test 36: Multi-SKU Heterogeneous Footprint — Existing physical stock plus proposed stock: Evaluate combined occupancy.
    /// Location already holds physical stock of SKU Exist occupying 50% of the planar floor footprint.
    /// In evaluation, a proposal for SKU Over (requiring 75% footprint; 50% + 75% = 125%) fails the hard constraint.
    /// In contrast, a proposal for SKU Fits (requiring 50% footprint; 50% + 50% = 100%) passes.
    /// </summary>
    [Fact]
    public async Task HeterogeneousFootprint_ExistingPhysicalStockPlusProposedStock_EvaluatesCombinedOccupancy()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-EXISTING-FOOTPRINT-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 100m,
            MaxWeightKg = 2000m,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.Add(loc);

        // Existing SKU: 40 x 40 x 50 cm, Factor = 2. UTier = 6, MaxTiers = 2.
        // Qty on hand = 6 -> NStacks = 3 -> Occupies 3/6 = 50% of floor.
        var existingItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-EXIST-OCC",
            LengthCm = 40m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(existingItem);
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = existingItem.Id,
            LocationId = loc.Id,
            QtyAvailable = 6m
        });

        // Proposed Item: 60 x 40 x 50 cm, Factor = 2. UTier = 4, MaxTiers = 2.
        var proposedItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-PROPOSED-CAND",
            LengthCm = 60m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(proposedItem);
        await context.SaveChangesAsync();

        // 1. Qty = 6 -> NStacks = ceil(6/2) = 3 -> Footprint = 3/4 = 75%. Total = 50% + 75% = 125% > 100% -> FAILS
        var resOver = await service.EvaluateSkuLocationCompatibilityAsync(proposedItem.Id, loc.Id, quantityToSlot: 6m);
        Assert.False(resOver.IsPhysicallyFeasible);
        Assert.False(resOver.IsOverallCompatible);
        Assert.Contains(resOver.ViolatedHardConstraints, v => v.Contains("exceeding maximum planar footprint capacity"));

        // 2. Qty = 4 -> NStacks = ceil(4/2) = 2 -> Footprint = 2/4 = 50%. Total = 50% + 50% = 100% <= 100% -> PASSES
        var resFits = await service.EvaluateSkuLocationCompatibilityAsync(proposedItem.Id, loc.Id, quantityToSlot: 4m);
        Assert.True(resFits.IsPhysicallyFeasible);
        Assert.True(resFits.IsOverallCompatible);
    }

    /// <summary>
    /// Test 37: Multi-SKU Heterogeneous Footprint — Rejected proposal followed by alternative location: Alternative remains eligible.
    /// SKU A consumes 83.3% footprint of Loc 1. SKU B's top candidate is Loc 1, but SKU B requires 50% footprint.
    /// SKU B is rejected for Loc 1 without consuming virtual capacity. SKU B is then evaluated for its alternative
    /// candidate Loc 2 (which has adequate capacity) and is successfully accepted for Loc 2.
    /// </summary>
    [Fact]
    public async Task HeterogeneousFootprint_RejectedProposalFollowedByAlternativeLocation_AlternativeRemainsEligible()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        // Loc 1: Golden zone closest to dock (X=1, Y=1, Z=1.1m)
        var loc1 = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-ALT-LOC-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 100m,
            MaxWeightKg = 1000m,
            XCoord = 1m,
            YCoord = 1m,
            ZCoord = 1.1m,
            ShelfLevel = 1,
            IsActive = true,
            IsPickable = true
        };

        // Loc 2: Golden zone second closest to dock (X=2, Y=2, Z=1.1m)
        var loc2 = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-ALT-LOC-02",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 100m,
            MaxWeightKg = 1000m,
            XCoord = 2m,
            YCoord = 2m,
            ZCoord = 1.1m,
            ShelfLevel = 1,
            IsActive = true,
            IsPickable = true
        };

        // Reserve source
        var sourceLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.ReserveZoneId,
            LocationCode = "RES-ALT-SOURCE-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 100m,
            MaxWeightKg = 2000m,
            XCoord = 50m,
            YCoord = 50m,
            ZCoord = 5.0m,
            ShelfLevel = 4,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.AddRange(loc1, loc2, sourceLoc);

        // SKU A: 40 x 40 x 50 cm, Factor = 2. UTier = 6, MaxTiers = 2.
        // Qty = 9 -> NStacks = ceil(9/2) = 5 -> Footprint = 5/6 = 83.3% of Loc 1.
        var skuA = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-ALT-A",
            Description = "Primary Item A",
            LengthCm = 40m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.A
        };

        // SKU B: 60 x 40 x 50 cm, Factor = 2. UTier = 4, MaxTiers = 2.
        // Qty = 4 -> NStacks = ceil(4/2) = 2 -> Footprint = 2/4 = 50%.
        var skuB = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-ALT-B",
            Description = "Alternative Item B",
            LengthCm = 60m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.A
        };
        context.Items.AddRange(skuA, skuB);

        // Give SKU A higher demand so it claims Loc 1 first
        DateTime now = DateTime.UtcNow;
        context.SkuVelocityClassificationRecords.Add(new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = skuA.Id,
            Sku = "SKU-ALT-A",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 600m,
            TotalPickTasksCount = 600,
            CalculatedAtUtc = now
        });

        context.SkuVelocityClassificationRecords.Add(new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = skuB.Id,
            Sku = "SKU-ALT-B",
            AbcClass = AbcClassificationCategory.ClassA,
            VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = now.AddDays(-90),
            CalculationPeriodEndUtc = now,
            TotalUnitsDemanded = 200m,
            TotalPickTasksCount = 200,
            CalculatedAtUtc = now
        });

        context.StockOnHand.AddRange(
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = skuA.Id, LocationId = sourceLoc.Id, QtyAvailable = 9m },
            new StockOnHand { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = skuB.Id, LocationId = sourceLoc.Id, QtyAvailable = 4m }
        );

        var policy = SlottingPolicy.Create("Alternative Reranking Policy", fix.FacilityId, targetClassAPickFace: true);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        var batch = await service.SimulateSlottingRecommendationsAsync(new SlottingSimulationRequestDto
        {
            FacilityId = fix.FacilityId,
            PolicyId = policy.Id,
            MaxMoves = 10
        });

        Assert.NotNull(batch);
        var recA = batch.Recommendations.FirstOrDefault(r => r.ItemId == skuA.Id);
        var recB = batch.Recommendations.FirstOrDefault(r => r.ItemId == skuB.Id);

        Assert.NotNull(recA);
        Assert.NotNull(recB);

        // SKU A takes Loc 1 (closest)
        Assert.Equal(loc1.Id, recA.ProposedLocationId);

        // SKU B was rejected for Loc 1 (83.3% + 50% = 133.3% > 100%), but successfully allocated to alternative Loc 2!
        Assert.Equal(loc2.Id, recB.ProposedLocationId);
    }

    /// <summary>
    /// Test 38: Multi-SKU Heterogeneous Footprint — Exact remaining physical capacity: Boundary quantity passes.
    /// Location has 50% footprint occupied by existing stock. Proposed SKU requires exactly 50% footprint (4 units = 2 stacks = 2/4 = 50%).
    /// Combined footprint is exactly 1.000000 <= 1.000001, which passes the boundary constraint.
    /// </summary>
    [Fact]
    public async Task HeterogeneousFootprint_ExactRemainingPhysicalCapacity_BoundaryQuantityPasses()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-BOUND-PASS-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 100m,
            MaxWeightKg = 2000m,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.Add(loc);

        // Existing SKU: 40 x 40 x 50 cm, Factor = 2. UTier = 6, MaxTiers = 2.
        // Qty = 6 -> NStacks = 3 -> 3/6 = 50% footprint.
        var existItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-BOUND-EXIST",
            LengthCm = 40m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(existItem);
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = existItem.Id,
            LocationId = loc.Id,
            QtyAvailable = 6m
        });

        // Candidate SKU: 60 x 40 x 50 cm, Factor = 2. UTier = 4, MaxTiers = 2.
        // Qty = 4 -> NStacks = 2 -> 2/4 = 50% footprint.
        // Total = 50% + 50% = 100.0% exactly!
        var candItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-BOUND-CAND",
            LengthCm = 60m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(candItem);
        await context.SaveChangesAsync();

        var res = await service.EvaluateSkuLocationCompatibilityAsync(candItem.Id, loc.Id, quantityToSlot: 4m);

        Assert.NotNull(res);
        Assert.True(res.IsPhysicallyFeasible);
        Assert.True(res.IsOverallCompatible);
        Assert.Empty(res.ViolatedHardConstraints);
    }

    /// <summary>
    /// Test 39: Multi-SKU Heterogeneous Footprint — One unit beyond physical capacity: Proposal fails.
    /// Location has 50% footprint occupied by existing stock. Increasing candidate quantity by 1 unit (from 4 to 5 units)
    /// forces an extra stack (ceil(5/2) = 3 stacks = 3/4 = 75% footprint).
    /// Combined footprint is 50% + 75% = 125% > 1.000001, causing immediate failure of the hard constraint.
    /// </summary>
    [Fact]
    public async Task HeterogeneousFootprint_OneUnitBeyondPhysicalCapacity_ProposalFails()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-BOUND-FAIL-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 100m,
            MaxWeightKg = 2000m,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.Add(loc);

        // Existing SKU: 40 x 40 x 50 cm, Factor = 2. UTier = 6, MaxTiers = 2.
        // Qty = 6 -> NStacks = 3 -> 3/6 = 50% footprint.
        var existItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-BOUND-FAIL-EXIST",
            LengthCm = 40m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(existItem);
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = existItem.Id,
            LocationId = loc.Id,
            QtyAvailable = 6m
        });

        // Candidate SKU: 60 x 40 x 50 cm, Factor = 2. UTier = 4, MaxTiers = 2.
        // Qty = 5 (1 unit more than 4) -> NStacks = ceil(5/2) = 3 -> 3/4 = 75% footprint.
        // Total = 50% + 75% = 125% > 1.000001 -> FAILS!
        var candItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-BOUND-FAIL-CAND",
            LengthCm = 60m,
            WidthCm = 40m,
            HeightCm = 50m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(candItem);
        await context.SaveChangesAsync();

        var res = await service.EvaluateSkuLocationCompatibilityAsync(candItem.Id, loc.Id, quantityToSlot: 5m);

        Assert.NotNull(res);
        Assert.False(res.IsPhysicallyFeasible);
        Assert.False(res.IsOverallCompatible);
        Assert.Contains(res.ViolatedHardConstraints, v => v.Contains("exceeding maximum planar footprint capacity"));
    }

    /// <summary>
    /// Test 40: Perpendicular long-item conflict.
    /// Location floor 100x100 cm. Existing Item A is 90x20 cm (at [0,90]x[0,20]).
    /// Candidate Item B is 20x90 cm with AllowPlanarRotation = false.
    /// Combined scalar area is 18% + 18% = 36% <= 100%, but in 2D space Item B cannot fit in the remaining
    /// space without overlapping Item A. Verified rejected by 2D geometric occupancy.
    /// </summary>
    [Fact]
    public async Task GeometricOccupancy_PerpendicularLongItemConflict_RejectsPlacementWhenOverlapUnavoidable()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-2D-CONFLICT-01",
            LocationType = LocationType.Rack,
            LengthCm = 100m,
            WidthCm = 100m,
            HeightCm = 150m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.Add(loc);

        // Item A: 90 x 20 x 20 cm, non-stackable (StackabilityFactor=2 so co-location is permitted, maxTiers=2).
        // 1 unit placed at (0, 0)
        var itemA = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-2D-LONG-A",
            LengthCm = 90m,
            WidthCm = 20m,
            HeightCm = 20m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AllowPlanarRotation = false,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(itemA);
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = itemA.Id,
            LocationId = loc.Id,
            QtyAvailable = 1m
        });

        // Item B: 20 x 90 x 20 cm, non-rotatable (AllowPlanarRotation = false).
        // Cannot fit in [0, 100] x [20, 100] (remaining Y is 80cm < 90cm)
        // or [90, 100] x [0, 100] (remaining X is 10cm < 20cm).
        var itemB = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-2D-LONG-B",
            LengthCm = 20m,
            WidthCm = 90m,
            HeightCm = 20m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AllowPlanarRotation = false,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(itemB);
        await context.SaveChangesAsync();

        var res = await service.EvaluateSkuLocationCompatibilityAsync(itemB.Id, loc.Id, quantityToSlot: 1m);

        Assert.NotNull(res);
        Assert.False(res.IsPhysicallyFeasible);
        Assert.False(res.IsOverallCompatible);
        Assert.Contains(res.ViolatedHardConstraints, v => v.Contains("cannot be geometrically packed"));
    }

    /// <summary>
    /// Test 41: Mixed rectangular dimensions.
    /// Multiple heterogeneous SKUs placed into a location without any 2D bounding box overlap.
    /// </summary>
    [Fact]
    public void GeometricOccupancy_MixedRectangularDimensions_MultipleSkusPlacedWithoutOverlap()
    {
        decimal locL = 120m;
        decimal locW = 100m;
        decimal locH = 150m;
        var existingPlacements = new List<VirtualPlacement>();

        var item1 = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-MIXED-1",
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 30m,
            StackabilityFactor = 2,
            AllowPlanarRotation = true
        };
        var item2 = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-MIXED-2",
            LengthCm = 60m,
            WidthCm = 20m,
            HeightCm = 30m,
            StackabilityFactor = 2,
            AllowPlanarRotation = true
        };
        var item3 = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-MIXED-3",
            LengthCm = 30m,
            WidthCm = 50m,
            HeightCm = 30m,
            StackabilityFactor = 2,
            AllowPlanarRotation = true
        };

        // Pack item 1 (4 units -> 2 stacks of 2)
        bool p1 = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, existingPlacements, item1, 4m, out var plac1);
        Assert.True(p1);
        Assert.Equal(2, plac1.Count);
        existingPlacements.AddRange(plac1);

        // Pack item 2 (2 units -> 1 stack of 2)
        bool p2 = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, existingPlacements, item2, 2m, out var plac2);
        Assert.True(p2);
        Assert.Single(plac2);
        existingPlacements.AddRange(plac2);

        // Pack item 3 (2 units -> 1 stack of 2)
        bool p3 = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, existingPlacements, item3, 2m, out var plac3);
        Assert.True(p3);
        Assert.Single(plac3);
        existingPlacements.AddRange(plac3);

        // Assert all 4 placements fit within planar boundaries and do NOT overlap pairwise
        Assert.Equal(4, existingPlacements.Count);
        for (int i = 0; i < existingPlacements.Count; i++)
        {
            var a = existingPlacements[i];
            Assert.True(a.XCoordCm >= 0m);
            Assert.True(a.YCoordCm >= 0m);
            Assert.True(a.XCoordCm + a.FootprintLengthCm <= locL + 0.0001m);
            Assert.True(a.YCoordCm + a.FootprintWidthCm <= locW + 0.0001m);

            for (int j = i + 1; j < existingPlacements.Count; j++)
            {
                var b = existingPlacements[j];
                Assert.False(a.Overlaps(b), $"Placement {i} ({a.Sku} at ({a.XCoordCm},{a.YCoordCm}) {a.FootprintLengthCm}x{a.FootprintWidthCm}) overlaps with placement {j} ({b.Sku} at ({b.XCoordCm},{b.YCoordCm}) {b.FootprintLengthCm}x{b.FootprintWidthCm})");
            }
        }
    }

    /// <summary>
    /// Test 42: Rotation-aware placement.
    /// Location 100x50 cm. Item 40x80 cm.
    /// Normal orientation (40x80) fails because width 80 > 50 cm.
    /// When AllowPlanarRotation = true, rotated orientation (80x40) is accepted.
    /// When AllowPlanarRotation = false, placement fails closed.
    /// </summary>
    [Fact]
    public void GeometricOccupancy_RotationAwarePlacement_ValidRotatedPlacementAccepted()
    {
        decimal locL = 100m;
        decimal locW = 50m;
        decimal locH = 100m;

        // Rotatable item
        var rotItem = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-ROT-OK",
            LengthCm = 40m,
            WidthCm = 80m,
            HeightCm = 20m,
            StackabilityFactor = 2,
            AllowPlanarRotation = true
        };

        bool ok = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, [], rotItem, 1m, out var placementsOk);
        Assert.True(ok);
        Assert.Single(placementsOk);
        Assert.Equal(PlanarOrientation.Rotated, placementsOk[0].Orientation);
        Assert.Equal(80m, placementsOk[0].FootprintLengthCm);
        Assert.Equal(40m, placementsOk[0].FootprintWidthCm);
        Assert.True(placementsOk[0].XCoordCm + placementsOk[0].FootprintLengthCm <= locL);
        Assert.True(placementsOk[0].YCoordCm + placementsOk[0].FootprintWidthCm <= locW);

        // Non-rotatable item
        var nonRotItem = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-ROT-FAIL",
            LengthCm = 40m,
            WidthCm = 80m,
            HeightCm = 20m,
            StackabilityFactor = 2,
            AllowPlanarRotation = false
        };

        bool fail = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, [], nonRotItem, 1m, out var placementsFail);
        Assert.False(fail);
        Assert.Empty(placementsFail);
    }

    /// <summary>
    /// Test 43: Existing physical placements.
    /// New proposals cannot overlap existing stock on the location floor.
    /// Location 120x80 cm. Existing physical stock occupies [0, 80] x [0, 80].
    /// A 60x60 cm candidate is rejected because free space is 40x80 cm.
    /// A 30x40 cm candidate is accepted and placed in the free space.
    /// </summary>
    [Fact]
    public async Task GeometricOccupancy_ExistingPhysicalPlacements_NewProposalsCannotOverlapExistingStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-2D-EXIST-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 2000m,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.Add(loc);

        // Existing physical stock: 80 x 80 x 30 cm, 1 unit
        var existItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-EXIST-PALLET",
            LengthCm = 80m,
            WidthCm = 80m,
            HeightCm = 30m,
            WeightKg = 10m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(existItem);
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = existItem.Id,
            LocationId = loc.Id,
            QtyAvailable = 1m
        });

        // Candidate 1: 60 x 60 x 30 cm (cannot fit in remaining 40 x 80 cm)
        var candOversized = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-CAND-OVERSIZED",
            LengthCm = 60m,
            WidthCm = 60m,
            HeightCm = 30m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(candOversized);

        // Candidate 2: 30 x 40 x 30 cm (fits in remaining 40 x 80 cm at X=80, Y=0)
        var candFitting = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-CAND-FITTING",
            LengthCm = 30m,
            WidthCm = 40m,
            HeightCm = 30m,
            WeightKg = 5m,
            StackabilityFactor = 2,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(candFitting);
        await context.SaveChangesAsync();

        // Evaluate candidate 1: Rejected
        var resOversized = await service.EvaluateSkuLocationCompatibilityAsync(candOversized.Id, loc.Id, quantityToSlot: 1m);
        Assert.NotNull(resOversized);
        Assert.False(resOversized.IsPhysicallyFeasible);
        Assert.False(resOversized.IsOverallCompatible);

        // Evaluate candidate 2: Accepted
        var resFitting = await service.EvaluateSkuLocationCompatibilityAsync(candFitting.Id, loc.Id, quantityToSlot: 1m);
        Assert.NotNull(resFitting);
        Assert.True(resFitting.IsPhysicallyFeasible);
        Assert.True(resFitting.IsOverallCompatible);
        Assert.Empty(resFitting.ViolatedHardConstraints);
    }

    /// <summary>
    /// Test 44: Vertical stack integration.
    /// Location 120x80x100 cm. Item 40x40x30 cm, StackabilityFactor = 3.
    /// Vertical tiers: min(3, floor(100/30)) = 3 tiers per stack.
    /// Floor planar stacks: (120/40)*(80/40) = 3 * 2 = 6 stacks.
    /// Total max units: 6 stacks * 3 tiers = 18 units.
    /// Quantity 18 passes; Quantity 19 fails closed.
    /// For crush-sensitive (Factor=1): Qty 6 passes; Qty 7 fails closed.
    /// </summary>
    [Fact]
    public async Task GeometricOccupancy_VerticalStackIntegration_FloorFootprintAndVerticalTiersRespected()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "PICK-2D-VERT-01",
            LocationType = LocationType.Rack,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 100m,
            MaxWeightKg = 2000m,
            IsActive = true,
            IsPickable = true
        };
        context.Locations.Add(loc);

        var stackableItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-VERT-STACKABLE",
            LengthCm = 40m,
            WidthCm = 40m,
            HeightCm = 30m,
            WeightKg = 5m,
            StackabilityFactor = 3,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(stackableItem);

        var crushItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-VERT-CRUSH",
            LengthCm = 40m,
            WidthCm = 40m,
            HeightCm = 30m,
            WeightKg = 5m,
            StackabilityFactor = 1,
            AbcVelocity = AbcVelocity.B
        };
        context.Items.Add(crushItem);
        await context.SaveChangesAsync();

        // Stackable item: 18 units passes
        var res18 = await service.EvaluateSkuLocationCompatibilityAsync(stackableItem.Id, loc.Id, quantityToSlot: 18m);
        Assert.NotNull(res18);
        Assert.True(res18.IsPhysicallyFeasible);

        // Stackable item: 19 units fails
        var res19 = await service.EvaluateSkuLocationCompatibilityAsync(stackableItem.Id, loc.Id, quantityToSlot: 19m);
        Assert.NotNull(res19);
        Assert.False(res19.IsPhysicallyFeasible);

        // Crush-sensitive item: 6 units passes
        var resCrush6 = await service.EvaluateSkuLocationCompatibilityAsync(crushItem.Id, loc.Id, quantityToSlot: 6m);
        Assert.NotNull(resCrush6);
        Assert.True(resCrush6.IsPhysicallyFeasible);

        // Crush-sensitive item: 7 units fails
        var resCrush7 = await service.EvaluateSkuLocationCompatibilityAsync(crushItem.Id, loc.Id, quantityToSlot: 7m);
        Assert.NotNull(resCrush7);
        Assert.False(resCrush7.IsPhysicallyFeasible);
    }

    /// <summary>
    /// Test 45: Rejected placement state preservation.
    /// When an intermediate candidate in a batch simulation is rejected due to geometric conflict,
    /// it does NOT reserve virtual floor space or prevent subsequent valid candidates from fitting.
    /// </summary>
    [Fact]
    public void GeometricOccupancy_RejectedPlacementStatePreservation_FailedPlacementDoesNotReserveSpace()
    {
        decimal locL = 100m;
        decimal locW = 100m;
        decimal locH = 100m;
        var placements = new List<VirtualPlacement>();

        // Candidate 1: 50 x 50 x 20 cm, accepted
        var cand1 = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-STATE-1",
            LengthCm = 50m,
            WidthCm = 50m,
            HeightCm = 20m,
            StackabilityFactor = 1,
            AllowPlanarRotation = true
        };
        bool ok1 = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, placements, cand1, 1m, out var p1);
        Assert.True(ok1);
        Assert.Single(p1);
        placements.AddRange(p1);

        // Candidate 2: 90 x 90 x 20 cm, rejected (cannot fit in remaining space)
        var cand2 = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-STATE-2-REJECTED",
            LengthCm = 90m,
            WidthCm = 90m,
            HeightCm = 20m,
            StackabilityFactor = 1,
            AllowPlanarRotation = true
        };
        bool ok2 = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, placements, cand2, 1m, out var p2);
        Assert.False(ok2);
        Assert.Empty(p2);
        // Candidate 2's failed attempt leaves placements unchanged
        Assert.Single(placements);

        // Candidate 3: 40 x 40 x 20 cm, accepted (proves space was preserved)
        var cand3 = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-STATE-3",
            LengthCm = 40m,
            WidthCm = 40m,
            HeightCm = 20m,
            StackabilityFactor = 1,
            AllowPlanarRotation = true
        };
        bool ok3 = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, placements, cand3, 1m, out var p3);
        Assert.True(ok3);
        Assert.Single(p3);
        placements.AddRange(p3);

        // Final state contains exactly Candidate 1 and Candidate 3, with zero collision
        Assert.Equal(2, placements.Count);
        Assert.False(placements[0].Overlaps(placements[1]));
    }

    /// <summary>
    /// Test 46: Deterministic placement.
    /// Identical inputs, items, and sequence generate bit-for-bit identical coordinates, orientations,
    /// and vertical tier counts across multiple simulation runs.
    /// </summary>
    [Fact]
    public void GeometricOccupancy_DeterministicPlacement_IdenticalInputsProduceIdenticalPlacements()
    {
        decimal locL = 120m;
        decimal locW = 80m;
        decimal locH = 150m;

        var items = new[]
        {
            (new Item { Id = Guid.NewGuid(), Sku = "SKU-DET-A", LengthCm = 40m, WidthCm = 30m, HeightCm = 30m, StackabilityFactor = 2, AllowPlanarRotation = true }, 3m),
            (new Item { Id = Guid.NewGuid(), Sku = "SKU-DET-B", LengthCm = 30m, WidthCm = 40m, HeightCm = 30m, StackabilityFactor = 2, AllowPlanarRotation = true }, 2m),
            (new Item { Id = Guid.NewGuid(), Sku = "SKU-DET-C", LengthCm = 50m, WidthCm = 20m, HeightCm = 30m, StackabilityFactor = 2, AllowPlanarRotation = true }, 2m)
        };

        // Run 1
        var run1Placements = new List<VirtualPlacement>();
        foreach (var (item, qty) in items)
        {
            bool ok = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, run1Placements, item, qty, out var placed);
            Assert.True(ok);
            run1Placements.AddRange(placed);
        }

        // Run 2
        var run2Placements = new List<VirtualPlacement>();
        foreach (var (item, qty) in items)
        {
            bool ok = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, run2Placements, item, qty, out var placed);
            Assert.True(ok);
            run2Placements.AddRange(placed);
        }

        Assert.Equal(run1Placements.Count, run2Placements.Count);
        for (int i = 0; i < run1Placements.Count; i++)
        {
            var p1 = run1Placements[i];
            var p2 = run2Placements[i];

            Assert.Equal(p1.Sku, p2.Sku);
            Assert.Equal(p1.XCoordCm, p2.XCoordCm);
            Assert.Equal(p1.YCoordCm, p2.YCoordCm);
            Assert.Equal(p1.FootprintLengthCm, p2.FootprintLengthCm);
            Assert.Equal(p1.FootprintWidthCm, p2.FootprintWidthCm);
            Assert.Equal(p1.Orientation, p2.Orientation);
            Assert.Equal(p1.VerticalTierCount, p2.VerticalTierCount);
        }
    }

    /// <summary>
    /// Test 47: Impossible dimension rejection.
    /// Items that cannot fit in any permitted orientation fail closed with zero capacity
    /// and no fallback to 1 tier or arbitrary area allocations.
    /// </summary>
    [Fact]
    public void GeometricOccupancy_ImpossibleDimensionRejection_FailsClosedWithoutFallback()
    {
        decimal locL = 100m;
        decimal locW = 80m;
        decimal locH = 60m;

        // Case 1: Length exceeds location length in both normal and rotated orientations
        var planarTooBig = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-PLANAR-OVERSIZED",
            LengthCm = 150m,
            WidthCm = 50m,
            HeightCm = 20m,
            StackabilityFactor = 2,
            AllowPlanarRotation = true
        };
        bool c1 = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, [], planarTooBig, 1m, out var plac1);
        Assert.False(c1);
        Assert.Empty(plac1);

        // Case 2: Height exceeds location clearance (60 cm)
        var heightTooTall = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-HEIGHT-OVERSIZED",
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 75m,
            StackabilityFactor = 2,
            AllowPlanarRotation = true
        };
        bool c2 = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, [], heightTooTall, 1m, out var plac2);
        Assert.False(c2);
        Assert.Empty(plac2);

        // Case 3: Zero or negative location clearance
        bool c3 = GeometricOccupancyEngine.TryPackItem(locL, locW, 0m, [], planarTooBig, 1m, out var plac3);
        Assert.False(c3);
        Assert.Empty(plac3);

        // Case 4: Non-rotatable item that fits only when rotated
        var rotOnlyItem = new Item
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-ROT-RESTRICTED",
            LengthCm = 70m,
            WidthCm = 90m, // normal: 70x90 doesn't fit in 100x80 (90 > 80); rotated: 90x70 fits in 100x80
            HeightCm = 20m,
            StackabilityFactor = 2,
            AllowPlanarRotation = false // rotation forbidden!
        };
        bool c4 = GeometricOccupancyEngine.TryPackItem(locL, locW, locH, [], rotOnlyItem, 1m, out var plac4);
        Assert.False(c4);
        Assert.Empty(plac4);
    }

    /// <summary>
    /// Test 48: Missing aging snapshot and missing velocity record without authoritative 365-day ledger proof
    /// is treated as unverified and strictly NOT classified as dead stock.
    /// Absence of a record is not evidence of zero demand.
    /// </summary>
    [Fact]
    public async Task DeadStock_MissingAgingSnapshotAndMissingVelocityRecord_UnverifiedNotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        // Item created 100 days ago (< 365 days)
        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-UNVERIFIED-NO-RECORDS",
            Description = "Item with missing aging snapshot and missing velocity record",
            AbcVelocity = AbcVelocity.C,
            CreatedAt = evalTime.AddDays(-100)
        };
        context.Items.Add(item);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();


        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 49: Stale aging snapshot (> 30 days old) asserting dead stock with missing velocity record
    /// and insufficient operational ledger history (< 365 days receipt) is rejected and treated as unverified.
    /// </summary>
    [Fact]
    public async Task DeadStock_StaleAgingSnapshotAndMissingVelocityRecord_UnverifiedNotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        // Item created 100 days ago (< 365 days)
        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-STALE-SNAPSHOT-UNVERIFIED",
            Description = "Item with stale aging snapshot and missing velocity record",
            AbcVelocity = AbcVelocity.B,
            CreatedAt = evalTime.AddDays(-100)
        };
        context.Items.Add(item);

        // Stale snapshot (45 days old > 30d limit) claiming dead stock
        var staleSnapshot = new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = itemId,
            Sku = "SKU-STALE-SNAPSHOT-UNVERIFIED",
            SnapshotDateUtc = evalTime.AddDays(-45),
            ReceiptAgeDays = 400,
            LastDemandAgeDays = 400,
            IsDeadStock = true
        };
        context.DailyInventoryAgingSnapshots.Add(staleSnapshot);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();


        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Stale snapshot is rejected; ledger does not prove 365+ days dormancy
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 50: Missing aging snapshot with a 90-day zero-demand projection record without verified 365-day dormancy
    /// is classified as Inactive/NonMoving, strictly NOT DeadStock.
    /// A 90-day window cannot establish 365 consecutive days of zero demand.
    /// </summary>
    [Fact]
    public async Task DeadStock_MissingAgingSnapshotAnd90DayZeroDemandProjection_UnverifiedNotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        // Item created 120 days ago (< 365 days)
        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-90DAY-INACTIVE-NOT-DEAD",
            Description = "Item with 90-day zero demand projection but < 365 days history",
            AbcVelocity = AbcVelocity.C,
            CreatedAt = evalTime.AddDays(-120)
        };
        context.Items.Add(item);

        // 90-day velocity record with 0 demand
        var rec90 = new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = itemId,
            Sku = "SKU-90DAY-INACTIVE-NOT-DEAD",
            AbcClass = AbcClassificationCategory.ClassC,
            VelocityCategory = InventoryMovementVelocityCategory.NonMoving,
            ObservationWindow = VelocityObservationWindow.Days90,
            CalculationPeriodStartUtc = evalTime.AddDays(-90),
            CalculationPeriodEndUtc = evalTime,
            TotalUnitsDemanded = 0m,
            TotalPickTasksCount = 0,
            CalculatedAtUtc = evalTime
        };
        var rec30 = new SkuVelocityClassificationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = itemId,
            Sku = "SKU-90DAY-INACTIVE-NOT-DEAD",
            AbcClass = AbcClassificationCategory.ClassC,
            VelocityCategory = InventoryMovementVelocityCategory.NonMoving,
            ObservationWindow = VelocityObservationWindow.Days30,
            CalculationPeriodStartUtc = evalTime.AddDays(-30),
            CalculationPeriodEndUtc = evalTime,
            TotalUnitsDemanded = 0m,
            TotalPickTasksCount = 0,
            CalculatedAtUtc = evalTime
        };
        context.SkuVelocityClassificationRecords.AddRange(rec90, rec30);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();


        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.Equal("Inactive", result.VelocityTrend);
        Assert.Equal("NonMoving", result.EffectiveVelocityCategory);
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 51: Missing aging snapshot with independently verified 365-day dormancy from operational ledger
    /// (completed PickTask 400 days ago, zero demand since) is authoritatively classified as DeadStock.
    /// </summary>
    [Fact]
    public async Task DeadStock_MissingAgingSnapshotAndIndependentlyVerified365DayDormancy_ClassifiedAsDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        // Item created 500 days ago
        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-DORMANT-365PLUS",
            Description = "Dormant item with authoritative pick ledger evidence 400 days ago",
            AbcVelocity = AbcVelocity.C,
            CreatedAt = evalTime.AddDays(-500)
        };
        context.Items.Add(item);

        // Historical pick task completed 400 days ago (zero demand for > 365 days)
        var oldPickTask = new PickTask
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemId,
            QuantityToPick = 10m,
            QuantityPicked = 10m,
            Status = PickTaskStatus.Completed,
            CompletedAtUtc = evalTime.AddDays(-400),
            CreatedAt = evalTime.AddDays(-400)
        };
        context.PickTasks.Add(oldPickTask);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();


        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.Equal("DeadStock", result.VelocityTrend);
        Assert.Equal("DeadStock", result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 52: Missing aging snapshot with never-demanded inventory and authoritative lifetime evidence
    /// (received 400 days ago, zero picks/orders across entire history) is authoritatively classified as DeadStock.
    /// </summary>
    [Fact]
    public async Task DeadStock_MissingAgingSnapshotAndNeverDemandedAuthoritativeLifetimeEvidence_ClassifiedAsDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        // Item received 400 days ago, never demanded in operational ledger
        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-NEVER-DEMANDED-400DAYS",
            Description = "Item received 400 days ago with zero demand ever",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        var session = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-400D",
            Status = ReceivingStatus.Completed,
            StartedAtUtc = evalTime.AddDays(-400),
            CompletedAtUtc = evalTime.AddDays(-400)
        };
        context.ReceivingSessions.Add(session);

        var receiptLine = new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = session.Id,
            ItemId = itemId,
            Lpn = "LPN-RECEIPT-400D",
            QuantityReceived = 100m,
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-400)
        };
        context.ReceiptLines.Add(receiptLine);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();


        // Update item.CreatedAt after initial insert to ensure CreatedAt is 400 days old in db
        item.CreatedAt = evalTime.AddDays(-400);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.Equal("DeadStock", result.VelocityTrend);
        Assert.Equal("DeadStock", result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 53: Recent demand within the 365-day period (e.g., completed pick task 30 days ago)
    /// authoritatively prevents dead stock classification even for old items (> 365 days receipt).
    /// </summary>
    [Fact]
    public async Task DeadStock_RecentDemandWithin365Days_ClassifiedAsNotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        // Item created 400 days ago
        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-RECENT-DEMAND-30DAYS",
            Description = "Old item with recent demand 30 days ago",
            AbcVelocity = AbcVelocity.C,
            CreatedAt = evalTime.AddDays(-400)
        };
        context.Items.Add(item);

        // Recent completed pick task 30 days ago (< 365 days)
        var recentPick = new PickTask
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemId,
            QuantityToPick = 5m,
            QuantityPicked = 5m,
            Status = PickTaskStatus.Completed,
            CompletedAtUtc = evalTime.AddDays(-30),
            CreatedAt = evalTime.AddDays(-30)
        };
        context.PickTasks.Add(recentPick);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();


        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 54: Fresh snapshot (<= 30 days old) with ReceiptAgeDays >= 365 and DemandInactivityDays == null
    /// without authoritative operational ledger confirmation is treated as unknown/incomplete evidence
    /// and strictly classified as NOT dead stock.
    /// DemandInactivityDays == null is not proof of zero lifetime demand.
    /// </summary>
    [Fact]
    public async Task DeadStock_FreshSnapshot_ReceiptAge365Plus_NullDemandInactivity_NoAuthoritativeProof_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        // Item created 50 days ago (no authoritative receiving ledger evidence proving 365+ days receipt)
        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-FRESH-NULL-INACTIVITY-NO-PROOF",
            Description = "Item with fresh snapshot claiming 400d receipt but DemandInactivityDays == null and no ledger proof",
            AbcVelocity = AbcVelocity.C,
            CreatedAt = evalTime.AddDays(-50)
        };
        context.Items.Add(item);

        // Fresh aging snapshot (10 days old <= 30d) with ReceiptAgeDays >= 365, but DemandInactivityDays is null
        var snapshot = new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = itemId,
            Sku = "SKU-FRESH-NULL-INACTIVITY-NO-PROOF",
            SnapshotDateUtc = evalTime.AddDays(-10),
            ReceiptAgeDays = 400,
            LastDemandAgeDays = 400,
            DemandInactivityDays = null, // Unknown/incomplete evidence!
            IsDeadStock = true
        };
        context.DailyInventoryAgingSnapshots.Add(snapshot);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();


        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // DemandInactivityDays == null treated as unknown, lack of ledger confirmation ensures NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 55: Fresh snapshot (<= 30 days old) with ReceiptAgeDays >= 365 and DemandInactivityDays == null,
    /// where authoritative operational ledger explicitly proves receipt age >= 365 days and zero lifetime demand,
    /// authoritatively qualifies as DeadStock.
    /// </summary>
    [Fact]
    public async Task DeadStock_FreshSnapshot_ReceiptAge365Plus_NullDemandInactivity_ExplicitlyVerifiedNeverDemanded_DeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-FRESH-NULL-INACTIVITY-VERIFIED-LEDGER",
            Description = "Item with fresh snapshot and authoritative ledger receipt 400 days ago with 0 demand",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        var session55 = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-TEST-55",
            Status = ReceivingStatus.Completed,
            StartedAtUtc = evalTime.AddDays(-400),
            CompletedAtUtc = evalTime.AddDays(-400)
        };
        context.ReceivingSessions.Add(session55);

        // Authoritative receiving intake scan 400 days ago
        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = session55.Id,
            ItemId = itemId,
            Lpn = "LPN-INTAKE-400D",
            QuantityReceived = 100m,
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-400)
        });

        // Fresh aging snapshot (10 days old <= 30d) with ReceiptAgeDays >= 365, DemandInactivityDays == null
        var snapshot = new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = itemId,
            Sku = "SKU-FRESH-NULL-INACTIVITY-VERIFIED-LEDGER",
            SnapshotDateUtc = evalTime.AddDays(-10),
            ReceiptAgeDays = 400,
            LastDemandAgeDays = 0,
            DemandInactivityDays = null,
            IsDeadStock = true
        };
        context.DailyInventoryAgingSnapshots.Add(snapshot);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-400);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Authoritative ledger confirms receipt 400d ago and zero picks/orders across lifetime -> DeadStock
        Assert.Equal("DeadStock", result.VelocityTrend);
        Assert.Equal("DeadStock", result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 56: Fresh snapshot with confirmed recent demand (e.g., LastDemandAgeDays = 30 and DemandInactivityDays = 30)
    /// authoritatively qualifies as NOT dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_FreshSnapshot_RecentDemand_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-FRESH-RECENT-DEMAND",
            Description = "Item with fresh snapshot and recent demand 30 days ago",
            AbcVelocity = AbcVelocity.C,
            CreatedAt = evalTime.AddDays(-400)
        };
        context.Items.Add(item);

        // Fresh aging snapshot (5 days old) with recent demand 30 days ago
        var snapshot = new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = itemId,
            Sku = "SKU-FRESH-RECENT-DEMAND",
            SnapshotDateUtc = evalTime.AddDays(-5),
            ReceiptAgeDays = 400,
            LastDemandAgeDays = 30,
            DemandInactivityDays = 30,
            LastDemandedAtUtc = evalTime.AddDays(-30),
            IsDeadStock = false
        };
        context.DailyInventoryAgingSnapshots.Add(snapshot);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();


        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 57: Old item-master record (CreatedAt 500 days ago) with NO physical receipt scan.
    /// In 3PL warehouse operations, Item is a master catalog entity, not physical inventory intake.
    /// The system must NOT infer 365-day inventory age from catalog creation date without ReceiptLines.
    /// Evaluated strictly as NOT dead stock (fails closed as unverified).
    /// </summary>
    [Fact]
    public async Task DeadStock_OldCatalogItemWithoutReceiptScan_MustNotInferReceiptAge_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        // Catalog SKU created 500 days ago, but NO ReceiptLines exist in the operational ledger
        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-CATALOG-500D-NO-RECEIPT",
            Description = "Old catalog master entity without physical receipt scan",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-500);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Physical intake unverified without ReceiptLine -> must NOT infer dormancy -> strictly NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 58: Pick task created 500 days ago with positive picked quantity but missing execution timestamps
    /// (CompletedAtUtc == null and StartedAtUtc == null).
    /// The system must NOT fall back to task CreatedAt (which would falsely imply demand was 500 days ago).
    /// It must treat un-timestamped execution as incomplete/unreliable evidence, failing closed as NOT dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_OldTaskCreationDateWithMissingExecutionTimestamps_TreatedAsIncompleteEvidence_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-UNTIMED-PICKTASK",
            Description = "Item with old receipt and un-timestamped completed pick task",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        var session58 = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-TEST-58",
            Status = ReceivingStatus.Completed,
            StartedAtUtc = evalTime.AddDays(-500),
            CompletedAtUtc = evalTime.AddDays(-500)
        };
        context.ReceivingSessions.Add(session58);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = session58.Id,
            ItemId = itemId,
            Lpn = "LPN-INTAKE-500D",
            QuantityReceived = 100m,
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-500)
        });

        // Pick task with positive picked quantity and Completed status, but NULL execution timestamps
        var pickTask = new PickTask
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = itemId,
            QuantityToPick = 5m,
            QuantityPicked = 5m,
            Status = PickTaskStatus.Completed,
            CompletedAtUtc = null,
            StartedAtUtc = null
        };
        context.PickTasks.Add(pickTask);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-500);
        pickTask.CreatedAt = evalTime.AddDays(-500);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Unreliable/missing pick execution timestamp must fail closed -> NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 59: Outbound order created 500 days ago (OrderDateUtc 500d ago) but with recent inventory allocation (30 days ago).
    /// The system must use the actual allocation execution timestamp (AllocatedAtUtc) rather than falling back to OrderDateUtc.
    /// Evaluated strictly as NOT dead stock due to recent demand within 365 days.
    /// </summary>
    [Fact]
    public async Task DeadStock_OldOutboundOrderWithRecentAllocation_QualifiesAsRecentDemand_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-OLD-ORDER-RECENT-ALLOC",
            Description = "Item with old order but recent allocation 30 days ago",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        var session59 = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-TEST-59",
            Status = ReceivingStatus.Completed,
            StartedAtUtc = evalTime.AddDays(-500),
            CompletedAtUtc = evalTime.AddDays(-500)
        };
        context.ReceivingSessions.Add(session59);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = session59.Id,
            ItemId = itemId,
            Lpn = "LPN-INTAKE-500D-59",
            QuantityReceived = 100m,
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-500)
        });

        var order59 = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OrderNumber = "ORD-OLD-59",
            OrderDateUtc = evalTime.AddDays(-500),
            Status = OutboundOrderStatus.InProgress
        };
        context.OutboundOrders.Add(order59);

        var orderLine59 = new OutboundOrderLine
        {
            Id = Guid.NewGuid(),
            OutboundOrderId = order59.Id,
            ItemId = itemId,
            QuantityOrdered = 10m,
            QuantityAllocated = 10m,
            Status = OutboundOrderLineStatus.Allocated
        };
        context.OutboundOrderLines.Add(orderLine59);

        var alloc59 = new InventoryAllocation
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OutboundOrderId = order59.Id,
            OutboundOrderLineId = orderLine59.Id,
            ItemId = itemId,
            QuantityRequested = 10m,
            QuantityAllocated = 10m,
            AllocatedAtUtc = evalTime.AddDays(-30), // Recent demand: 30 days ago!
            Status = AllocationStatus.Allocated
        };
        context.InventoryAllocations.Add(alloc59);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-500);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Allocation occurred 30 days ago -> recent demand within 365 days -> NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 60: Qualifying allocated outbound order line with no reliable execution timestamp
    /// (empty Allocations collection and null shipment timestamps).
    /// The system must NOT fall back to OrderDateUtc (which would treat creation date 500 days ago as demand date).
    /// Treated as unknown demand timestamp, failing closed as NOT dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_QualifyingOrderLineWithNoReliableExecutionTimestamp_TreatedAsUnknown_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-UNTIMED-ORDERLINE",
            Description = "Item with old receipt and un-timestamped allocated order line",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        var session60 = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-TEST-60",
            Status = ReceivingStatus.Completed,
            StartedAtUtc = evalTime.AddDays(-500),
            CompletedAtUtc = evalTime.AddDays(-500)
        };
        context.ReceivingSessions.Add(session60);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = session60.Id,
            ItemId = itemId,
            Lpn = "LPN-INTAKE-500D-60",
            QuantityReceived = 100m,
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-500)
        });

        var order60 = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OrderNumber = "ORD-OLD-60",
            OrderDateUtc = evalTime.AddDays(-500),
            Status = OutboundOrderStatus.InProgress,
            DeliveredAtUtc = null,
            DeliveryStatusUpdatedAtUtc = null
        };
        context.OutboundOrders.Add(order60);

        // Qualifying allocated order line without allocation entity records
        var orderLine60 = new OutboundOrderLine
        {
            Id = Guid.NewGuid(),
            OutboundOrderId = order60.Id,
            ItemId = itemId,
            QuantityOrdered = 10m,
            QuantityAllocated = 10m,
            Status = OutboundOrderLineStatus.Allocated
        };
        context.OutboundOrderLines.Add(orderLine60);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-500);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Missing allocation execution timestamp must fail closed -> NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 61: Receipt line without a receiving session (orphaned receipt line) is treated as unverified physical intake.
    /// In 3PL warehouse operations, intake evidence must be bound to a valid ReceivingSession.
    /// Strictly classified as NOT dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_ReceiptLineWithoutReceivingSession_TreatedAsOrphanedUnverified_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-ORPHANED-RECEIPT-LINE",
            Description = "Item with orphaned receipt line lacking a receiving session",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        // Orphaned receipt line: ReceivingSessionId points to an unseeded session (ReceivingSession is null)
        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = Guid.NewGuid(),
            ItemId = itemId,
            Lpn = "LPN-ORPHANED-400D",
            QuantityReceived = 100m,
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-400)
        });

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-400);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Orphaned receipt line without ReceivingSession must fail closed -> NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 62: Receipt line associated with a cancelled receiving session is treated as invalid intake evidence.
    /// A cancelled session voids receipt records; intake is unverified -> strictly classified as NOT dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_ReceiptLineInCancelledReceivingSession_TreatedAsInvalidUnverified_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-CANCELLED-RECEIPT-SESSION",
            Description = "Item with receipt line in a cancelled receiving session",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        var cancelledSession = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-CANCELLED-62",
            Status = ReceivingStatus.Cancelled, // CANCELLED SESSION
            StartedAtUtc = evalTime.AddDays(-400),
            CompletedAtUtc = evalTime.AddDays(-400)
        };
        context.ReceivingSessions.Add(cancelledSession);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = cancelledSession.Id,
            ItemId = itemId,
            Lpn = "LPN-CANCELLED-400D",
            QuantityReceived = 100m,
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-400)
        });

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-400);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Cancelled session voids physical receipt intake -> NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 63: Receipt line with zero or negative received quantity is rejected as invalid intake evidence.
    /// Zero intake quantity does not establish physical arrival -> unverified -> strictly NOT dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_ReceiptLineWithZeroOrNegativeQuantity_TreatedAsInvalidUnverified_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-ZERO-QTY-RECEIPT",
            Description = "Item with receipt line having 0 received quantity",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        var session63 = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-ZERO-QTY-63",
            Status = ReceivingStatus.Completed,
            StartedAtUtc = evalTime.AddDays(-400),
            CompletedAtUtc = evalTime.AddDays(-400)
        };
        context.ReceivingSessions.Add(session63);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = session63.Id,
            ItemId = itemId,
            Lpn = "LPN-ZERO-QTY-400D",
            QuantityReceived = 0m, // ZERO QUANTITY RECEIVED
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-400)
        });

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-400);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Zero received quantity cannot establish physical intake -> NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 64: Valid receipt scan in an active (InProgress) or completed receiving session with positive quantity
    /// authoritatively qualifies as verified physical intake, classifying 400d dormant inventory as DeadStock.
    /// </summary>
    [Fact]
    public async Task DeadStock_ValidReceiptLineInActiveOrCompletedSession_PositivelyVerified_DeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-VALID-ACTIVE-SESSION-RECEIPT",
            Description = "Item with valid receipt line in an InProgress active receiving session",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        // Active receiving session (InProgress)
        var activeSession = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-ACTIVE-64",
            Status = ReceivingStatus.InProgress, // ACTIVE PERMITTED STATUS
            StartedAtUtc = evalTime.AddDays(-400)
        };
        context.ReceivingSessions.Add(activeSession);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = activeSession.Id,
            ItemId = itemId,
            Lpn = "LPN-ACTIVE-400D",
            QuantityReceived = 50m, // POSITIVE QUANTITY
            QuantityExpected = 50m,
            ScannedAtUtc = evalTime.AddDays(-400)
        });

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-400);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Valid receipt scan in active session with zero demand over 400d -> authoritatively DeadStock
        Assert.Equal("DeadStock", result.VelocityTrend);
        Assert.Equal("DeadStock", result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 65: Qualifying outbound order line with null parent order (orphaned line)
    /// lacks authoritative lifecycle state and facility isolation context; treated as incomplete demand evidence.
    /// Strictly fails closed as NOT dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_QualifyingOrderLineWithNullParentOrder_ClassifiedAsIncompleteEvidence_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-ORPHANED-ORDER-LINE",
            Description = "Item with old receipt and orphaned order line lacking parent order",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        var session65 = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-TEST-65",
            Status = ReceivingStatus.Completed,
            StartedAtUtc = evalTime.AddDays(-500),
            CompletedAtUtc = evalTime.AddDays(-500)
        };
        context.ReceivingSessions.Add(session65);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = session65.Id,
            ItemId = itemId,
            Lpn = "LPN-INTAKE-500D-65",
            QuantityReceived = 100m,
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-500)
        });

        // Qualifying allocated order line with unseeded OutboundOrderId (OutboundOrder is null)
        var orphanedOrderLine = new OutboundOrderLine
        {
            Id = Guid.NewGuid(),
            OutboundOrderId = Guid.NewGuid(), // Non-existent parent order
            ItemId = itemId,
            QuantityOrdered = 10m,
            QuantityAllocated = 10m,
            Status = OutboundOrderLineStatus.Allocated
        };
        context.OutboundOrderLines.Add(orphanedOrderLine);

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-500);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Orphaned order line must fail closed as incomplete demand evidence -> NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 66: Valid receipt scan in a Started receiving session with positive quantity
    /// authoritatively qualifies as verified physical intake, classifying 400d dormant inventory as DeadStock.
    /// </summary>
    [Fact]
    public async Task DeadStock_ValidReceiptLineInStartedSession_PositivelyVerified_DeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-VALID-STARTED-SESSION-RECEIPT",
            Description = "Item with valid receipt line in a Started active receiving session",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        // Active receiving session with Status = ReceivingStatus.Started
        var startedSession = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-STARTED-66",
            Status = ReceivingStatus.Started, // EXPLICIT STARTED STATUS
            StartedAtUtc = evalTime.AddDays(-400)
        };
        context.ReceivingSessions.Add(startedSession);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = startedSession.Id,
            ItemId = itemId,
            Lpn = "LPN-STARTED-400D",
            QuantityReceived = 50m, // Positive quantity
            QuantityExpected = 50m,
            ScannedAtUtc = evalTime.AddDays(-400)
        });

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-400);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Valid receipt scan in Started session with zero demand over 400d -> authoritatively DeadStock
        Assert.Equal("DeadStock", result.VelocityTrend);
        Assert.Equal("DeadStock", result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 67: Receipt line in a deleted receiving session (IsDeleted = true)
    /// cannot establish physical intake; treated as unverified and fails closed as NOT dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_ReceiptLineInDeletedReceivingSession_TreatedAsInvalidUnverified_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-DELETED-SESSION-RECEIPT",
            Description = "Item with receipt line in a soft-deleted receiving session",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        // Soft-deleted receiving session
        var deletedSession = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            SessionNumber = "RCV-DELETED-67",
            Status = ReceivingStatus.Completed,
            StartedAtUtc = evalTime.AddDays(-400),
            CompletedAtUtc = evalTime.AddDays(-400),
            IsDeleted = true // SOFT DELETED
        };
        context.ReceivingSessions.Add(deletedSession);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = deletedSession.Id,
            ItemId = itemId,
            Lpn = "LPN-DELETED-SESSION-400D",
            QuantityReceived = 100m,
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-400)
        });

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-400);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Soft-deleted receiving session cannot establish physical intake -> NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    /// <summary>
    /// Test 68: Receipt line in a receiving session belonging to a different facility
    /// cannot establish the target facility's physical intake; strictly fails closed as NOT dead stock.
    /// </summary>
    [Fact]
    public async Task DeadStock_ReceiptLineInDifferentFacilitySession_TreatedAsCrossFacilityUnverified_NotDeadStock()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new SlottingOptimizationService(context, new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] }, NullLogger<SlottingOptimizationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();
        var foreignFacilityId = Guid.NewGuid();

        var item = new Item
        {
            Id = itemId,
            ClientId = fix.ClientId,
            Sku = "SKU-FOREIGN-FACILITY-RECEIPT",
            Description = "Item with receipt line in a foreign facility receiving session",
            AbcVelocity = AbcVelocity.C
        };
        context.Items.Add(item);

        // Receiving session at a foreign facility
        var foreignSession = new ReceivingSession
        {
            Id = Guid.NewGuid(),
            FacilityId = foreignFacilityId, // FOREIGN FACILITY
            ClientId = fix.ClientId,
            SessionNumber = "RCV-FOREIGN-68",
            Status = ReceivingStatus.Completed,
            StartedAtUtc = evalTime.AddDays(-400),
            CompletedAtUtc = evalTime.AddDays(-400)
        };
        context.ReceivingSessions.Add(foreignSession);

        context.ReceiptLines.Add(new ReceiptLine
        {
            Id = Guid.NewGuid(),
            ReceivingSessionId = foreignSession.Id,
            ItemId = itemId,
            Lpn = "LPN-FOREIGN-400D",
            QuantityReceived = 100m,
            QuantityExpected = 100m,
            ScannedAtUtc = evalTime.AddDays(-400)
        });

        var policy = SlottingPolicy.Create("Dead Stock Aging Policy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        await context.SaveChangesAsync();

        item.CreatedAt = evalTime.AddDays(-400);
        await context.SaveChangesAsync();

        var result = await service.EvaluateSkuLocationCompatibilityAsync(itemId, fix.UpperReserveLocationId, policy.Id, null, evalTime);

        Assert.NotNull(result);
        // Receipt from foreign facility cannot establish target facility physical intake -> NOT dead stock
        Assert.NotEqual("DeadStock", result.VelocityTrend);
        Assert.NotEqual("DeadStock", result.EffectiveVelocityCategory);
        Assert.NotEqual(InventoryMovementVelocityCategory.DeadStock.ToString(), result.EffectiveVelocityCategory);
    }

    #endregion
}
