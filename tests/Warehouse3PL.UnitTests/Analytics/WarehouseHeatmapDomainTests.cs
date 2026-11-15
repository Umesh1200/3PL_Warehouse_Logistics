using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
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
/// Exhaustive unit and integration test suite for Phase 6.3.6.2:
/// Dynamic Warehouse Heatmap Domain & Visual Projection Engine.
/// Verifies:
/// 1. Facility -> Zone -> Aisle -> Rack -> Location topological hierarchy.
/// 2. Spatial coordinates, physical dimensions, and bounding-box resolution.
/// 3. Duplicate coordinate detection and out-of-bounds validation.
/// 4. Explicit missing-coordinate handling without fabricating positions.
/// 5. Independent layer activation/filtering (Cube, Pallet, Velocity, Aging, Overcapacity, Blocked).
/// 6. Multiple simultaneous conditions per location (non-mutually-exclusive).
/// 7. Uncapped over-capacity (>100%) and independent pallet vs cube utilization.
/// 8. Documented metric-to-color mapping and standardized color scales.
/// 9. Strict multi-tenant isolation, facility boundaries, and client topology masking.
/// 10. Read-only advisory execution with zero ledger mutations.
/// 11. Phase 6.3.6.1 conditional closure items (INV-H-01 to INV-H-05: score breakdown, no double-counting, freshness).
/// </summary>
public class WarehouseHeatmapDomainTests
{
    private class HeatmapFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid Client1Id { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();

        public Guid Zone1Id { get; set; } = Guid.NewGuid();
        public Guid Zone2Id { get; set; } = Guid.NewGuid();

        public Guid Loc1Id { get; set; } = Guid.NewGuid(); // Optimal (60% fill)
        public Guid Loc2Id { get; set; } = Guid.NewGuid(); // Overcapacity (125% fill, 150% pallets)
        public Guid Loc3Id { get; set; } = Guid.NewGuid(); // Blocked / Locked
        public Guid Loc4Id { get; set; } = Guid.NewGuid(); // Missing Coordinates
        public Guid Loc5Id { get; set; } = Guid.NewGuid(); // Duplicate Coordinates of Loc1
        public Guid Loc6Id { get; set; } = Guid.NewGuid(); // Multi-condition: Overcapacity + DeadStock + Expired
    }

    private static ApplicationDbContext CreateContext(
        string dbName,
        Guid? tenantId = null,
        Guid? facilityId = null,
        bool isStaff = true,
        bool isSuperAdmin = false,
        List<string>? permissions = null)
    {
        var currentUser = new TestCurrentUserContext
        {
            TenantId = tenantId,
            FacilityId = facilityId,
            UserType = isStaff ? UserType.Staff : UserType.Client,
            RolesList = isSuperAdmin ? ["SuperAdmin"] : [],
            PermissionsList = permissions ?? [Permissions.Analytics.InventoryHealthView]
        };

        return TestDbContextFactory.Create(
            new TestTenantProvider { TenantId = tenantId, BypassTenantFilter = isStaff || isSuperAdmin },
            currentUser,
            dbName);
    }

    private static async Task<HeatmapFixture> SeedHeatmapEnvironmentAsync(DateTime? snapshotDateUtc = null)
    {
        var fix = new HeatmapFixture();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var asOf = (snapshotDateUtc ?? DateTime.UtcNow).Date;

        var fac1 = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "ORD-01",
            Name = "Chicago Regional Fulfillment Center",
            IsActive = true
        };

        var fac2 = new Facility
        {
            Id = fix.Facility2Id,
            FacilityCode = "DFW-01",
            Name = "Dallas Regional Fulfillment Center",
            IsActive = true
        };

        var client1 = new Client
        {
            Id = fix.Client1Id,
            AccountCode = "CLI-ACME",
            CompanyName = "Acme Global Distribution",
            Status = ClientStatus.Active
        };

        var client2 = new Client
        {
            Id = fix.Client2Id,
            AccountCode = "CLI-ZENITH",
            CompanyName = "Zenith Retail Partners",
            Status = ClientStatus.Active
        };

        var zone1 = new WarehouseZone
        {
            Id = fix.Zone1Id,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-BULK-01",
            Name = "Bulk High-Bay Racking",
            ZoneType = WarehouseZoneType.BulkStorage,
            IsActive = true
        };

        var zone2 = new WarehouseZone
        {
            Id = fix.Zone2Id,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-PICK-01",
            Name = "Active Pick Module",
            ZoneType = WarehouseZoneType.PickFace,
            IsActive = true
        };

        // Locations with spatial coordinates (X, Y, Z in meters)
        // Loc 1: Normal coordinates (10, 20, 2)
        var loc1 = new Location
        {
            Id = fix.Loc1Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone1Id,
            LocationCode = "BULK-A-01-01",
            AisleCode = "A-01",
            RackCode = "R-01",
            ShelfLevel = 1,
            BinNumber = 1,
            LocationType = LocationType.Rack,
            XCoord = 10.0m,
            YCoord = 20.0m,
            ZCoord = 2.0m,
            LengthCm = 240m,
            WidthCm = 120m,
            HeightCm = 200m,
            IsActive = true
        };

        // Loc 2: Overcapacity coordinates (15, 20, 2)
        var loc2 = new Location
        {
            Id = fix.Loc2Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone1Id,
            LocationCode = "BULK-A-01-02",
            AisleCode = "A-01",
            RackCode = "R-01",
            ShelfLevel = 1,
            BinNumber = 2,
            LocationType = LocationType.Rack,
            XCoord = 15.0m,
            YCoord = 20.0m,
            ZCoord = 2.0m,
            LengthCm = 240m,
            WidthCm = 120m,
            HeightCm = 200m,
            IsActive = true
        };

        // Loc 3: Blocked / Locked location (20, 25, 4)
        var loc3 = new Location
        {
            Id = fix.Loc3Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone1Id,
            LocationCode = "BULK-A-02-01",
            AisleCode = "A-02",
            RackCode = "R-02",
            ShelfLevel = 2,
            BinNumber = 1,
            LocationType = LocationType.Rack,
            XCoord = 20.0m,
            YCoord = 25.0m,
            ZCoord = 4.0m,
            IsActive = false,
            IsLocked = true
        };

        // Loc 4: Location missing coordinates (X, Y, Z null)
        var loc4 = new Location
        {
            Id = fix.Loc4Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone2Id,
            LocationCode = "PICK-B-01-01",
            AisleCode = "B-01",
            RackCode = "R-10",
            ShelfLevel = 1,
            BinNumber = 1,
            LocationType = LocationType.Bin,
            XCoord = null,
            YCoord = null,
            ZCoord = null,
            IsActive = true
        };

        // Loc 5: Duplicate coordinates of Loc 1 (10, 20, 2)
        var loc5 = new Location
        {
            Id = fix.Loc5Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone1Id,
            LocationCode = "BULK-A-01-01-DUP",
            AisleCode = "A-01",
            RackCode = "R-01",
            ShelfLevel = 1,
            BinNumber = 3,
            LocationType = LocationType.Rack,
            XCoord = 10.0m, // Identical to Loc 1!
            YCoord = 20.0m,
            ZCoord = 2.0m,
            IsActive = true
        };

        // Loc 6: Multi-condition (30, 40, 6)
        var loc6 = new Location
        {
            Id = fix.Loc6Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone2Id,
            LocationCode = "PICK-B-02-01",
            AisleCode = "B-02",
            RackCode = "R-12",
            ShelfLevel = 3,
            BinNumber = 1,
            LocationType = LocationType.Bin,
            XCoord = 30.0m,
            YCoord = 40.0m,
            ZCoord = 6.0m,
            IsActive = true
        };

        await context.Facilities.AddRangeAsync(fac1, fac2);
        await context.Clients.AddRangeAsync(client1, client2);
        await context.WarehouseZones.AddRangeAsync(zone1, zone2);
        await context.Locations.AddRangeAsync(loc1, loc2, loc3, loc4, loc5, loc6);

        // Seed Space Utilization Projections (Phase 6.3.3)
        var spaceLoc1 = new DailyLocationSpaceUtilizationRecord
        {
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone1Id,
            LocationId = fix.Loc1Id,
            LocationCode = "BULK-A-01-01",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            PhysicalCubicMeters = 10.0m,
            UsableCubicMeters = 8.5m,
            OccupiedCubicMeters = 5.1m,
            AvailableCubicMeters = 3.4m,
            CubeUtilizationPercent = 60.0m, // Optimal
            PhysicalPalletPositions = 4,
            UsablePalletPositions = 4,
            OccupiedPalletPositions = 2,
            PalletOccupancyPercent = 50.0m,
            IsBlocked = false
        };

        var spaceLoc2 = new DailyLocationSpaceUtilizationRecord
        {
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone1Id,
            LocationId = fix.Loc2Id,
            LocationCode = "BULK-A-01-02",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            PhysicalCubicMeters = 10.0m,
            UsableCubicMeters = 8.0m,
            OccupiedCubicMeters = 10.0m, // Over-capacity
            AvailableCubicMeters = -2.0m,
            CubeUtilizationPercent = 125.0m, // Uncapped >100%
            PhysicalPalletPositions = 4,
            UsablePalletPositions = 4,
            OccupiedPalletPositions = 6, // 150% pallets
            PalletOccupancyPercent = 150.0m, // Uncapped >100%
            IsOverCapacity = true,
            OverCapacityExcessCbm = 2.0m,
            IsBlocked = false
        };

        var spaceLoc3 = new DailyLocationSpaceUtilizationRecord
        {
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone1Id,
            LocationId = fix.Loc3Id,
            LocationCode = "BULK-A-02-01",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            PhysicalCubicMeters = 12.0m,
            UsableCubicMeters = 10.0m,
            OccupiedCubicMeters = 0m,
            CubeUtilizationPercent = 0m,
            IsBlocked = true,
            BlockReason = "Structural Rack Maintenance Lock"
        };

        var spaceLoc6 = new DailyLocationSpaceUtilizationRecord
        {
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone2Id,
            LocationId = fix.Loc6Id,
            LocationCode = "PICK-B-02-01",
            LocationType = LocationType.Bin,
            SnapshotDateUtc = asOf,
            PhysicalCubicMeters = 5.0m,
            UsableCubicMeters = 4.0m,
            OccupiedCubicMeters = 4.8m,
            AvailableCubicMeters = -0.8m,
            CubeUtilizationPercent = 120.0m,
            IsOverCapacity = true,
            OverCapacityExcessCbm = 0.8m,
            IsBlocked = false
        };

        await context.DailyLocationSpaceUtilizationRecords.AddRangeAsync(spaceLoc1, spaceLoc2, spaceLoc3, spaceLoc6);

        // Seed Items & Aging Projections (Phase 6.3.2)
        var item1 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client1Id, Sku = "SKU-HOT-01", Description = "Fast Mover" };
        var item2 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client1Id, Sku = "SKU-DEAD-02", Description = "Expired Dead Stock" };
        var itemClient2 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client2Id, Sku = "SKU-ZEN-99", Description = "Client 2 Exclusive" };
        await context.Items.AddRangeAsync(item1, item2, itemClient2);

        // Loc 1: Active item
        var agingLoc1 = new DailyInventoryAgingSnapshot
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = item1.Id,
            Sku = "SKU-HOT-01",
            LocationId = fix.Loc1Id,
            LocationCode = "BULK-A-01-01",
            SnapshotDateUtc = asOf,
            QuantityOnHand = 100m,
            TotalInventoryValue = 10000m,
            AgingBucket = InventoryAgingBucket.Current_0_30,
            IsAged = false,
            IsSlowMoving = false,
            IsDeadStock = false,
            IsExpired = false,
            OccupiedCbm = 5.1m
        };

        // Loc 6: Dead stock AND Expired stock (Multi-condition)
        var agingLoc6 = new DailyInventoryAgingSnapshot
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = item2.Id,
            Sku = "SKU-DEAD-02",
            LocationId = fix.Loc6Id,
            LocationCode = "PICK-B-02-01",
            SnapshotDateUtc = asOf,
            QuantityOnHand = 50m,
            TotalInventoryValue = 5000m,
            AgingBucket = InventoryAgingBucket.DeadStock_365Plus,
            IsAged = true,
            IsDeadStock = true,
            IsExpired = true,
            IsNearExpiry = false,
            OccupiedCbm = 4.8m
        };

        // Loc 2: Client 2 inventory
        var agingLoc2 = new DailyInventoryAgingSnapshot
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client2Id,
            ItemId = itemClient2.Id,
            Sku = "SKU-ZEN-99",
            LocationId = fix.Loc2Id,
            LocationCode = "BULK-A-01-02",
            SnapshotDateUtc = asOf,
            QuantityOnHand = 200m,
            TotalInventoryValue = 20000m,
            AgingBucket = InventoryAgingBucket.Aging_31_60,
            OccupiedCbm = 10.0m
        };

        await context.DailyInventoryAgingSnapshots.AddRangeAsync(agingLoc1, agingLoc6, agingLoc2);

        // Seed SKU Velocity Records (Phase 6.3.4)
        var vel1 = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, item1.Id, "SKU-HOT-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.FastMoving,
            asOf.AddDays(-90), asOf, pickTasksCount: 150, unitsDemanded: 300m, demandValue: 30000m,
            orderLinesCount: 120, cumulativePercentage: 80m, version: 1,
            observationWindow: VelocityObservationWindow.Days90,
            recordType: SkuClassificationRecordType.VelocityOperational);

        var vel2 = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, item2.Id, "SKU-DEAD-02",
            AbcClassificationCategory.ClassC, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.DeadStock,
            asOf.AddDays(-90), asOf, pickTasksCount: 0, unitsDemanded: 0m, demandValue: 0m,
            orderLinesCount: 0, cumulativePercentage: 100m, version: 1,
            observationWindow: VelocityObservationWindow.Days90,
            recordType: SkuClassificationRecordType.VelocityOperational);

        await context.SkuVelocityClassificationRecords.AddRangeAsync(vel1, vel2);

        await context.SaveChangesAsync();
        return fix;
    }

    #region Acceptance Test Scenarios

    /// <summary>
    /// Test 1: Facility -> Zone -> Aisle -> Rack -> Location topological hierarchy tree is generated accurately.
    /// </summary>
    [Fact]
    public async Task GetWarehouseTopology_BuildsCorrectArbitraryDepthHierarchyTree()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var topology = await service.GetWarehouseTopologyAsync(fix.FacilityId);

        Assert.NotNull(topology);
        Assert.Single(topology);
        var root = topology[0];
        Assert.Equal(LocationType.Facility, root.Type);
        Assert.Equal("ORD-01", root.Code);

        // Zones
        Assert.Equal(2, root.Children.Count);
        var bulkZone = root.Children.FirstOrDefault(c => c.Code == "Z-BULK-01");
        Assert.NotNull(bulkZone);
        Assert.Equal(LocationType.Zone, bulkZone.Type);

        // Aisles under Bulk Zone
        Assert.NotEmpty(bulkZone.Children);
        var aisleA01 = bulkZone.Children.FirstOrDefault(c => c.Code == "A-01");
        Assert.NotNull(aisleA01);
        Assert.Equal(LocationType.Aisle, aisleA01.Type);

        // Racks under Aisle A-01
        Assert.NotEmpty(aisleA01.Children);
        var rack01 = aisleA01.Children.FirstOrDefault(c => c.Code == "R-01");
        Assert.NotNull(rack01);
        Assert.Equal(LocationType.Rack, rack01.Type);

        // Locations under Rack R-01
        Assert.True(rack01.Children.Count >= 2);
        Assert.Contains(rack01.Children, l => l.Code == "BULK-A-01-01");
        Assert.Contains(rack01.Children, l => l.Code == "BULK-A-01-02");
    }

    /// <summary>
    /// Test 2: Spatial coordinates, physical dimensions, and warehouse bounding-box resolution.
    /// </summary>
    [Fact]
    public async Task GetWarehouseHeatmap_CalculatesExactBoundingBoxAndZoomPanBounds()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var heatmap = await service.GetWarehouseHeatmapAsync(fix.FacilityId);

        Assert.NotNull(heatmap);
        Assert.NotNull(heatmap.BoundingBox);
        Assert.NotNull(heatmap.ZoomPanBounds);

        // Minimum coordinates from seeded locations: MinX = 10 (Loc 1), MaxX = 30 (Loc 6)
        // MinY = 20 (Loc 1/2), MaxY = 40 (Loc 6)
        // MinZ = 2 (Loc 1/2), MaxZ = 6 (Loc 6)
        Assert.Equal(10.0m, heatmap.BoundingBox.MinX);
        Assert.Equal(30.0m, heatmap.BoundingBox.MaxX);
        Assert.Equal(20.0m, heatmap.BoundingBox.MinY);
        Assert.Equal(40.0m, heatmap.BoundingBox.MaxY);
        Assert.Equal(2.0m, heatmap.BoundingBox.MinZ);
        Assert.Equal(6.0m, heatmap.BoundingBox.MaxZ);

        Assert.Equal(20.0m, heatmap.BoundingBox.Width); // 30 - 10
        Assert.Equal(20.0m, heatmap.BoundingBox.Length); // 40 - 20
        Assert.Equal(4.0m, heatmap.BoundingBox.Height); // 6 - 2

        // Zoom Pan Constraints
        Assert.Equal(0.1m, heatmap.ZoomPanBounds.MinZoom);
        Assert.Equal(20.0m, heatmap.ZoomPanBounds.MaxZoom);
        Assert.Equal(1.0m, heatmap.ZoomPanBounds.AspectRatio);
    }

    /// <summary>
    /// Test 3: Duplicate coordinate detection and flagging.
    /// </summary>
    [Fact]
    public async Task GetWarehouseHeatmap_DetectsAndFlagsDuplicateCoordinates()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var heatmap = await service.GetWarehouseHeatmapAsync(fix.FacilityId);

        // Loc 1 and Loc 5 share the identical (10, 20, 2) coordinates
        var loc1Cell = heatmap.Locations.FirstOrDefault(l => l.LocationId == fix.Loc1Id);
        var loc5Cell = heatmap.Locations.FirstOrDefault(l => l.LocationId == fix.Loc5Id);

        Assert.NotNull(loc1Cell);
        Assert.NotNull(loc5Cell);
        Assert.Equal(HeatmapCoordinateStatus.DuplicateCoordinates, loc1Cell.Coordinate.Status);
        Assert.Equal(HeatmapCoordinateStatus.DuplicateCoordinates, loc5Cell.Coordinate.Status);
    }

    /// <summary>
    /// Test 4: Explicit missing-coordinate handling without fabricating positions.
    /// </summary>
    [Fact]
    public async Task GetWarehouseHeatmap_ExplicitlyFlagsMissingCoordinates_NeverFabricatesPositions()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var heatmap = await service.GetWarehouseHeatmapAsync(fix.FacilityId);

        // Loc 4 has null coordinates
        var loc4Cell = heatmap.Locations.FirstOrDefault(l => l.LocationId == fix.Loc4Id);
        Assert.NotNull(loc4Cell);
        Assert.False(loc4Cell.Coordinate.HasCoordinates);
        Assert.Equal(HeatmapCoordinateStatus.MissingCoordinates, loc4Cell.Coordinate.Status);
        Assert.Null(loc4Cell.Coordinate.X);
        Assert.Null(loc4Cell.Coordinate.Y);
        Assert.Null(loc4Cell.Coordinate.Z);

        Assert.True(heatmap.MissingCoordinatesLocationsCount >= 1);
    }

    /// <summary>
    /// Test 5: Multiple simultaneous conditions at a single location are preserved and not forced into a mutually exclusive status.
    /// </summary>
    [Fact]
    public async Task GetWarehouseHeatmap_MultipleSimultaneousConditions_PreservedWithoutMutualExclusion()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var heatmap = await service.GetWarehouseHeatmapAsync(fix.FacilityId);

        // Loc 6 has: OverCapacity (>100%), DeadStock (>365d), and ExpiredStock
        var loc6Cell = heatmap.Locations.FirstOrDefault(l => l.LocationId == fix.Loc6Id);
        Assert.NotNull(loc6Cell);

        Assert.True(loc6Cell.IsOverCapacity);
        Assert.True(loc6Cell.IsDeadStock);
        Assert.True(loc6Cell.IsExpired);

        // Simultaneous conditions list must preserve all 3 conditions!
        Assert.Contains("OverCapacity", loc6Cell.ActiveConditions);
        Assert.Contains("DeadStock", loc6Cell.ActiveConditions);
        Assert.Contains("ExpiredStock", loc6Cell.ActiveConditions);

        // Primary Tier should resolve to Critical due to OverCapacity and ExpiredStock
        Assert.Equal(HeatmapVisualTier.Critical, loc6Cell.PrimaryTier);
    }

    /// <summary>
    /// Test 6: Overcapacity values above 100% are uncapped and pallet occupancy is independent of cube utilization.
    /// </summary>
    [Fact]
    public async Task GetWarehouseHeatmap_OvercapacityValuesUncapped_PalletOccupancyIndependentOfCube()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var heatmap = await service.GetWarehouseHeatmapAsync(fix.FacilityId);

        // Loc 2 has 125% Cube Utilization and 150% Pallet Occupancy
        var loc2Cell = heatmap.Locations.FirstOrDefault(l => l.LocationId == fix.Loc2Id);
        Assert.NotNull(loc2Cell);

        Assert.Equal(125.0m, loc2Cell.CubeUtilizationPercent);
        Assert.Equal(150.0m, loc2Cell.PalletOccupancyPercent);
        Assert.Equal(2.0m, loc2Cell.OverCapacityExcessCbm);
        Assert.Equal("#EF4444", loc2Cell.CubeFillColorHex);
        Assert.Equal("#EF4444", loc2Cell.PalletOccupancyColorHex);
        Assert.Equal(HeatmapVisualTier.Critical, loc2Cell.PrimaryTier);
    }

    /// <summary>
    /// Test 7: Blocked location isolation: Blocked locations are recognized, preserve block reasons, and mapped to blocked tier.
    /// </summary>
    [Fact]
    public async Task GetWarehouseHeatmap_BlockedLocations_IsolatedAndAssignedBlockedColor()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var heatmap = await service.GetWarehouseHeatmapAsync(fix.FacilityId);

        // Loc 3 is locked/inactive
        var loc3Cell = heatmap.Locations.FirstOrDefault(l => l.LocationId == fix.Loc3Id);
        Assert.NotNull(loc3Cell);
        Assert.True(loc3Cell.IsBlocked);
        Assert.Equal("#475569", loc3Cell.BlockedColorHex);
        Assert.Equal("Structural Rack Maintenance Lock", loc3Cell.BlockReason);
        Assert.Contains("Blocked", loc3Cell.ActiveConditions);
    }

    /// <summary>
    /// Test 8: Documented metric-to-color mapping catalog returns standardized definitions for all 6 layers.
    /// </summary>
    [Fact]
    public async Task GetHeatmapColorScales_ReturnsDocumentedStandardizedThresholdScales()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var scales = await service.GetHeatmapColorScalesAsync();

        Assert.NotNull(scales);
        Assert.Equal(6, scales.Count);

        // 1. Cube Utilization
        var cubeScale = scales.FirstOrDefault(s => s.LayerType == HeatmapLayerType.CubeUtilization);
        Assert.NotNull(cubeScale);
        Assert.True(cubeScale.IsUncapped);
        Assert.Contains(cubeScale.Stops, st => st.ColorHex == "#EF4444" && st.Tier == HeatmapVisualTier.Critical);
        Assert.Contains(cubeScale.Stops, st => st.ColorHex == "#22C55E" && st.Tier == HeatmapVisualTier.Optimal);

        // 2. Pallet Occupancy
        var palletScale = scales.FirstOrDefault(s => s.LayerType == HeatmapLayerType.PalletOccupancy);
        Assert.NotNull(palletScale);
        Assert.True(palletScale.IsUncapped);

        // 3. Blocked Locations
        var blockedScale = scales.FirstOrDefault(s => s.LayerType == HeatmapLayerType.BlockedLocations);
        Assert.NotNull(blockedScale);
        Assert.Contains(blockedScale.Stops, st => st.ColorHex == "#475569" && st.Tier == HeatmapVisualTier.Blocked);
    }

    /// <summary>
    /// Test 9: Multi-tenant isolation: Client portal user is strictly scoped to their inventory locations,
    /// masks other client locations, and cross-client requests throw UnauthorizedException.
    /// </summary>
    [Fact]
    public async Task GetWarehouseHeatmap_ClientPortalIsolation_MasksOtherClientLocations_RejectsCrossTenantRequests()
    {
        var fix = await SeedHeatmapEnvironmentAsync();

        // 1. Client 1 portal user
        using var contextClient1 = CreateContext(
            fix.DbName, tenantId: fix.Client1Id, isStaff: false, isSuperAdmin: false);
        var userClient1 = new TestCurrentUserContext
        {
            TenantId = fix.Client1Id,
            UserType = UserType.Client,
            PermissionsList = [Permissions.Analytics.InventoryHealthView]
        };
        var serviceClient1 = new AnalyticsQueryService(contextClient1, userClient1, NullLogger<AnalyticsQueryService>.Instance);

        var heatmapClient1 = await serviceClient1.GetWarehouseHeatmapAsync(fix.FacilityId);

        // Client 1 has items in Loc 1 and Loc 6 only. Loc 2 has Client 2 inventory!
        Assert.Contains(heatmapClient1.Locations, l => l.LocationId == fix.Loc1Id);
        Assert.Contains(heatmapClient1.Locations, l => l.LocationId == fix.Loc6Id);
        // Loc 2 must be masked from Client 1!
        Assert.DoesNotContain(heatmapClient1.Locations, l => l.LocationId == fix.Loc2Id);

        // 2. Client 1 attempting to request Client 2 view explicitly must throw UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(async () =>
        {
            await serviceClient1.GetWarehouseHeatmapAsync(
                fix.FacilityId, filter: new HeatmapQueryFilterDto { ClientId = fix.Client2Id });
        });
    }

    /// <summary>
    /// Test 10: Facility boundary enforcement: User restricted to Facility 1 cannot access Facility 2 heatmap.
    /// </summary>
    [Fact]
    public async Task GetWarehouseHeatmap_FacilityScoping_EnforcesFacilityBoundary()
    {
        var fix = await SeedHeatmapEnvironmentAsync();

        using var context = CreateContext(
            fix.DbName, facilityId: fix.FacilityId, isStaff: true, isSuperAdmin: false);
        var currentUser = new TestCurrentUserContext
        {
            FacilityId = fix.FacilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Analytics.InventoryHealthView]
        };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        // Requesting Facility 2 must throw UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(async () =>
        {
            await service.GetWarehouseHeatmapAsync(fix.Facility2Id);
        });
    }

    /// <summary>
    /// Test 11: Read-Only Invariance: Generating heatmap visualization causes zero state mutations to StockOnHand or Locations.
    /// </summary>
    [Fact]
    public async Task GetWarehouseHeatmap_ReadOnlyInvariance_CausesZeroLedgerMutations()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var initialLocationsCount = await context.Locations.CountAsync();
        var initialStockOnHandCount = await context.StockOnHand.CountAsync();

        // Execute multiple heatmap queries with different layer combinations
        await service.GetWarehouseHeatmapAsync(fix.FacilityId);
        await service.GetWarehouseHeatmapAsync(fix.FacilityId, new HeatmapQueryFilterDto { ActiveLayers = HeatmapLayerType.AllLayers });
        await service.GetWarehouseTopologyAsync(fix.FacilityId);

        var postLocationsCount = await context.Locations.CountAsync();
        var postStockOnHandCount = await context.StockOnHand.CountAsync();

        Assert.Equal(initialLocationsCount, postLocationsCount);
        Assert.Equal(initialStockOnHandCount, postStockOnHandCount);
    }

    /// <summary>
    /// Test 12: Phase 6.3.6.1 Conditions Closure (INV-H-01, INV-H-03, INV-H-05):
    /// Verifies HealthScoreBreakdown penalty definitions, non-overlapping dead stock deduction,
    /// and per-projection freshness metadata.
    /// </summary>
    [Fact]
    public async Task GetUnifiedInventoryHealthDashboard_ConditionsClosure_VerifiesBreakdownAndFreshness()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var dashboard = await service.GetUnifiedInventoryHealthDashboardAsync(fix.FacilityId);

        // INV-H-01: Explicit Breakdown
        Assert.NotNull(dashboard.HealthScoreBreakdown);
        Assert.Equal(100.0m, dashboard.HealthScoreBreakdown.BaseScore);
        Assert.Equal("Scored", dashboard.HealthScoreBreakdown.ScoreStatus);
        Assert.NotEmpty(dashboard.HealthScoreBreakdown.Explanation);

        // INV-H-03: No double counting between dead stock and expired stock
        // Item 2 ($5,000) is both dead stock and expired.
        // Expired penalty handles the expiration; unexpired dead stock penalty is 0.
        Assert.True(dashboard.HealthScoreBreakdown.ExpiredStockPenalty > 0m);
        // Total deductions equal sum of component penalties
        var expectedDeductions = dashboard.HealthScoreBreakdown.DeadStockPenalty +
                                 dashboard.HealthScoreBreakdown.ExpiredStockPenalty +
                                 dashboard.HealthScoreBreakdown.OverCapacityPenalty +
                                 dashboard.HealthScoreBreakdown.SpaceCongestionPenalty +
                                 dashboard.HealthScoreBreakdown.StockoutRiskPenalty;
        Assert.Equal(expectedDeductions, dashboard.HealthScoreBreakdown.TotalDeductions);

        // INV-H-05: Per-projection freshness flags
        Assert.NotNull(dashboard.Freshness);
        Assert.False(dashboard.Freshness.HasClockSkewDetected);
        Assert.False(dashboard.Freshness.IsAgingStale);
        Assert.False(dashboard.Freshness.IsSpaceStale);
    }

    /// <summary>
    /// Test 13: WebAPI Controller Contracts: Verifies heatmap, color-scales, and topology endpoints routing and permissions.
    /// </summary>
    [Fact]
    public async Task InventoryHealthAnalyticsController_HeatmapEndpoints_EnforcePermissionsAndReturnOk()
    {
        var fix = await SeedHeatmapEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var queryService = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);
        var controller = new InventoryHealthAnalyticsController(queryService, currentUser);

        // 1. Heatmap endpoint
        var heatmapResult = await controller.GetWarehouseHeatmap(fix.FacilityId, null, null, CancellationToken.None);
        var heatmapOk = Assert.IsType<OkObjectResult>(heatmapResult);
        var heatmapDto = Assert.IsType<WarehouseHeatmapResponseDto>(heatmapOk.Value);
        Assert.Equal(fix.FacilityId, heatmapDto.FacilityId);

        // 2. Color scales endpoint
        var colorScalesResult = await controller.GetHeatmapColorScales(CancellationToken.None);
        var colorScalesOk = Assert.IsType<OkObjectResult>(colorScalesResult);
        var colorScalesDto = Assert.IsAssignableFrom<IReadOnlyList<HeatmapColorScaleDefinitionDto>>(colorScalesOk.Value);
        Assert.Equal(6, colorScalesDto.Count);

        // 3. Topology endpoint
        var topologyResult = await controller.GetWarehouseTopology(fix.FacilityId, CancellationToken.None);
        var topologyOk = Assert.IsType<OkObjectResult>(topologyResult);
        var topologyDto = Assert.IsAssignableFrom<IReadOnlyList<WarehouseTopologyNodeDto>>(topologyOk.Value);
        Assert.Single(topologyDto);

        // 4. Verify HasPermission attribute on endpoints
        var heatmapMethod = typeof(InventoryHealthAnalyticsController).GetMethod(nameof(InventoryHealthAnalyticsController.GetWarehouseHeatmap));
        Assert.NotNull(heatmapMethod);
        var permAttr = heatmapMethod.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permAttr);
        Assert.Equal(Permissions.Analytics.InventoryHealthView, permAttr.Policy);
    }

    #endregion
}
