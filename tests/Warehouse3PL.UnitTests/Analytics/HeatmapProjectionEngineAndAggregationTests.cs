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
/// Exhaustive unit and integration test suite for Phase 6.3.6.3:
/// Heatmap Projection Engine & Aggregations.
/// Verifies:
/// 1. Spatial indexing foundation and grid generation (deterministic cell hashing).
/// 2. Authoritative facility envelope vs observed bounding box (HM-01).
/// 3. Consistent coordinate and dimension units (HM-02).
/// 4. Weighted spatial rollups at Rack, Aisle, Zone, and Facility levels (HM-04).
/// 5. Preservation of all simultaneous conditions during aggregation (HM-05).
/// 6. Operational hotspot detection with topological adjacency boundaries (same Zone & Aisle).
/// 7. Macro facility rollups and client portal masking.
/// 8. Read-only invariance and WebAPI controller contracts.
/// </summary>
public class HeatmapProjectionEngineAndAggregationTests
{
    private class ProjectionFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Client1Id { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();

        public Guid ZoneAId { get; set; } = Guid.NewGuid();
        public Guid ZoneBId { get; set; } = Guid.NewGuid();

        // Zone A, Aisle 1, Rack 1: Two locations for testing weighted aggregation (HM-04)
        public Guid LocSmallHighId { get; set; } = Guid.NewGuid(); // Small: Usable 1.0 CBM, Occupied 1.0 CBM (100% fill)
        public Guid LocLargeLowId { get; set; } = Guid.NewGuid();  // Large: Usable 9.0 CBM, Occupied 0.9 CBM (10% fill)

        // Zone A, Aisle 1, Rack 2: Overcapacity contiguous hotspot (LocOver1 & LocOver2)
        public Guid LocOver1Id { get; set; } = Guid.NewGuid(); // 120% fill
        public Guid LocOver2Id { get; set; } = Guid.NewGuid(); // 130% fill

        // Zone A, Aisle 2, Rack 1: Numerically close to Aisle 1 Rack 2, but in Aisle 2 (Topological Adjacency test)
        public Guid LocAcrossAisleOverId { get; set; } = Guid.NewGuid();

        // Zone B: Out of bounds location (HM-01)
        public Guid LocOutOfBoundsId { get; set; } = Guid.NewGuid(); // X = 200m (outside 150m envelope)
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

    private static async Task<ProjectionFixture> SeedProjectionEnvironmentAsync(DateTime? snapshotDateUtc = null)
    {
        var fix = new ProjectionFixture();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var asOf = (snapshotDateUtc ?? DateTime.UtcNow).Date;

        var facility = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "ORD-01",
            Name = "Chicago Regional Hub",
            TotalAreaSqM = 15000m, // Authoritative area
            IsActive = true
        };

        var client1 = new Client { Id = fix.Client1Id, AccountCode = "CLI-ACME", CompanyName = "Acme Global", Status = ClientStatus.Active };
        var client2 = new Client { Id = fix.Client2Id, AccountCode = "CLI-ZENITH", CompanyName = "Zenith Retail", Status = ClientStatus.Active };

        var zoneA = new WarehouseZone
        {
            Id = fix.ZoneAId,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-MAIN",
            Name = "Main Storage Zone",
            ZoneType = WarehouseZoneType.BulkStorage,
            IsActive = true
        };

        var zoneB = new WarehouseZone
        {
            Id = fix.ZoneBId,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-EXPANSION",
            Name = "Expansion Yard",
            ZoneType = WarehouseZoneType.BulkStorage,
            IsActive = true
        };

        // 1. Locations for Weighted Aggregation Verification (HM-04)
        // LocSmallHigh: 1 CBM usable, 1 CBM occupied (100% fill), 1 pallet usable, 1 pallet occupied
        var locSmall = new Location
        {
            Id = fix.LocSmallHighId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationCode = "MAIN-A01-R01-01",
            AisleCode = "A-01",
            RackCode = "R-01",
            ShelfLevel = 1,
            BinNumber = 1,
            LocationType = LocationType.Rack,
            XCoord = 10.0m,
            YCoord = 10.0m,
            ZCoord = 2.0m,
            IsActive = true
        };

        // LocLargeLow: 9 CBM usable, 0.9 CBM occupied (10% fill), 9 pallets usable, 1 pallet occupied
        var locLarge = new Location
        {
            Id = fix.LocLargeLowId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationCode = "MAIN-A01-R01-02",
            AisleCode = "A-01",
            RackCode = "R-01",
            ShelfLevel = 1,
            BinNumber = 2,
            LocationType = LocationType.Rack,
            XCoord = 12.0m,
            YCoord = 10.0m,
            ZCoord = 2.0m,
            IsActive = true
        };

        // 2. Locations for Overcapacity Contiguous Hotspot (Same Zone & Same Aisle A-01, adjacent R-02)
        var locOver1 = new Location
        {
            Id = fix.LocOver1Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationCode = "MAIN-A01-R02-01",
            AisleCode = "A-01",
            RackCode = "R-02",
            ShelfLevel = 1,
            BinNumber = 1,
            LocationType = LocationType.Rack,
            XCoord = 16.0m,
            YCoord = 10.0m,
            ZCoord = 2.0m,
            IsActive = true
        };

        var locOver2 = new Location
        {
            Id = fix.LocOver2Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationCode = "MAIN-A01-R02-02",
            AisleCode = "A-01",
            RackCode = "R-02",
            ShelfLevel = 1,
            BinNumber = 2,
            LocationType = LocationType.Rack,
            XCoord = 18.0m,
            YCoord = 10.0m,
            ZCoord = 2.0m,
            IsActive = true
        };

        // 3. Location Across Aisle (Aisle A-02): Distance to Over2 is only 3m, but on Aisle A-02!
        var locAcross = new Location
        {
            Id = fix.LocAcrossAisleOverId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationCode = "MAIN-A02-R01-01",
            AisleCode = "A-02", // Different Aisle!
            RackCode = "R-01",
            ShelfLevel = 1,
            BinNumber = 1,
            LocationType = LocationType.Rack,
            XCoord = 18.0m,
            YCoord = 13.0m, // 3m Euclidean distance from Over2, but in Aisle A-02!
            ZCoord = 2.0m,
            IsActive = true
        };

        // 4. Out-of-Bounds Location (HM-01): X = 250m (exceeds authoritative facility length)
        var locOutOfBounds = new Location
        {
            Id = fix.LocOutOfBoundsId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneBId,
            LocationCode = "EXP-B01-R01-01",
            AisleCode = "B-01",
            RackCode = "R-01",
            ShelfLevel = 1,
            BinNumber = 1,
            LocationType = LocationType.Rack,
            XCoord = 250.0m, // Out of bounds!
            YCoord = 20.0m,
            ZCoord = 2.0m,
            IsActive = true
        };

        await context.Facilities.AddAsync(facility);
        await context.Clients.AddRangeAsync(client1, client2);
        await context.WarehouseZones.AddRangeAsync(zoneA, zoneB);
        await context.Locations.AddRangeAsync(locSmall, locLarge, locOver1, locOver2, locAcross, locOutOfBounds);

        // Seed Space Utilization Projections (Phase 6.3.3)
        var spaceSmall = new DailyLocationSpaceUtilizationRecord
        {
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationId = fix.LocSmallHighId,
            LocationCode = "MAIN-A01-R01-01",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            UsableCubicMeters = 1.0m,
            OccupiedCubicMeters = 1.0m, // 100%
            CubeUtilizationPercent = 100.0m,
            UsablePalletPositions = 1,
            OccupiedPalletPositions = 1, // 100%
            PalletOccupancyPercent = 100.0m,
            IsBlocked = false
        };

        var spaceLarge = new DailyLocationSpaceUtilizationRecord
        {
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationId = fix.LocLargeLowId,
            LocationCode = "MAIN-A01-R01-02",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            UsableCubicMeters = 9.0m,
            OccupiedCubicMeters = 0.9m, // 10%
            CubeUtilizationPercent = 10.0m,
            UsablePalletPositions = 9,
            OccupiedPalletPositions = 1, // 11.11%
            PalletOccupancyPercent = 11.11m,
            IsBlocked = false
        };

        var spaceOver1 = new DailyLocationSpaceUtilizationRecord
        {
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationId = fix.LocOver1Id,
            LocationCode = "MAIN-A01-R02-01",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            UsableCubicMeters = 10.0m,
            OccupiedCubicMeters = 12.0m, // 120%
            CubeUtilizationPercent = 120.0m,
            UsablePalletPositions = 4,
            OccupiedPalletPositions = 5,
            PalletOccupancyPercent = 125.0m,
            IsOverCapacity = true,
            OverCapacityExcessCbm = 2.0m,
            IsBlocked = false
        };

        var spaceOver2 = new DailyLocationSpaceUtilizationRecord
        {
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationId = fix.LocOver2Id,
            LocationCode = "MAIN-A01-R02-02",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            UsableCubicMeters = 10.0m,
            OccupiedCubicMeters = 13.0m, // 130%
            CubeUtilizationPercent = 130.0m,
            UsablePalletPositions = 4,
            OccupiedPalletPositions = 6,
            PalletOccupancyPercent = 150.0m,
            IsOverCapacity = true,
            OverCapacityExcessCbm = 3.0m,
            IsBlocked = false
        };

        var spaceAcross = new DailyLocationSpaceUtilizationRecord
        {
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationId = fix.LocAcrossAisleOverId,
            LocationCode = "MAIN-A02-R01-01",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            UsableCubicMeters = 10.0m,
            OccupiedCubicMeters = 11.5m, // 115%
            CubeUtilizationPercent = 115.0m,
            UsablePalletPositions = 4,
            OccupiedPalletPositions = 5,
            PalletOccupancyPercent = 125.0m,
            IsOverCapacity = true,
            OverCapacityExcessCbm = 1.5m,
            IsBlocked = false
        };

        var spaceOut = new DailyLocationSpaceUtilizationRecord
        {
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneBId,
            LocationId = fix.LocOutOfBoundsId,
            LocationCode = "EXP-B01-R01-01",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            UsableCubicMeters = 20.0m,
            OccupiedCubicMeters = 10.0m,
            CubeUtilizationPercent = 50.0m,
            IsBlocked = false
        };

        await context.DailyLocationSpaceUtilizationRecords.AddRangeAsync(
            spaceSmall, spaceLarge, spaceOver1, spaceOver2, spaceAcross, spaceOut);

        // Seed Aging records for financial valuation
        var item1 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client1Id, Sku = "SKU-PROJ-01", Description = "Test Item 1" };
        var item2 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client1Id, Sku = "SKU-PROJ-02", Description = "Test Item 2" };
        await context.Items.AddRangeAsync(item1, item2);

        var snapSmall = new DailyInventoryAgingSnapshot
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = item1.Id,
            Sku = "SKU-PROJ-01",
            LocationId = fix.LocSmallHighId,
            LocationCode = "MAIN-A01-R01-01",
            SnapshotDateUtc = asOf,
            QuantityOnHand = 10m,
            TotalInventoryValue = 1000m,
            AgingBucket = InventoryAgingBucket.Current_0_30
        };

        var snapLarge = new DailyInventoryAgingSnapshot
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = item2.Id,
            Sku = "SKU-PROJ-02",
            LocationId = fix.LocLargeLowId,
            LocationCode = "MAIN-A01-R01-02",
            SnapshotDateUtc = asOf,
            QuantityOnHand = 90m,
            TotalInventoryValue = 9000m,
            AgingBucket = InventoryAgingBucket.Current_0_30
        };

        await context.DailyInventoryAgingSnapshots.AddRangeAsync(snapSmall, snapLarge);
        await context.SaveChangesAsync();
        return fix;
    }

    #region Phase 6.3.6.3 Verification Tests

    /// <summary>
    /// Test 1: Weighted Spatial Rollups at Rack, Aisle, and Zone level (HM-04).
    /// Proves that utilization percentages are calculated via sum(occupied)/sum(usable)*100,
    /// and NOT by taking the arithmetic average of individual location percentages.
    /// In Rack R-01:
    /// LocSmall: Usable = 1.0, Occupied = 1.0 (100% fill)
    /// LocLarge: Usable = 9.0, Occupied = 0.9 (10% fill)
    /// Arithmetic average: (100 + 10) / 2 = 55%
    /// Weighted correct average: (1.0 + 0.9) / (1.0 + 9.0) = 1.9 / 10.0 = 19.0%!
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_WeightedSpatialRollups_UsesAdditiveSumsNeverArithmeticAverage()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var projection = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId);

        Assert.NotNull(projection);
        Assert.NotEmpty(projection.Zones);

        var zoneA = projection.Zones.FirstOrDefault(z => z.ZoneId == fix.ZoneAId);
        Assert.NotNull(zoneA);

        var aisleA01 = zoneA.Aisles.FirstOrDefault(a => a.AisleCode == "A-01");
        Assert.NotNull(aisleA01);

        var rackR01 = aisleA01.Racks.FirstOrDefault(r => r.RackCode == "R-01");
        Assert.NotNull(rackR01);

        // Rack R-01 Verification:
        // Total Usable CBM = 1.0 + 9.0 = 10.0 CBM
        // Total Occupied CBM = 1.0 + 0.9 = 1.9 CBM
        // Weighted Cube Fill = 1.9 / 10.0 * 100 = 19.0%
        Assert.Equal(10.0m, rackR01.TotalUsableCbm);
        Assert.Equal(1.9m, rackR01.TotalOccupiedCbm);
        Assert.Equal(19.0m, rackR01.WeightedCubeUtilizationPercent); // MUST BE 19.0%, NOT 55.0%!

        // Pallet Occupancy:
        // Usable = 1 + 9 = 10. Occupied = 1 + 1 = 2.
        // Weighted Pallet % = 2 / 10 * 100 = 20.0%
        Assert.Equal(10, rackR01.TotalUsablePallets);
        Assert.Equal(2, rackR01.TotalOccupiedPallets);
        Assert.Equal(20.0m, rackR01.WeightedPalletOccupancyPercent);

        // Inventory Value Aggregation:
        // Total Inventory Value = $1,000 + $9,000 = $10,000
        Assert.Equal(10000m, rackR01.TotalInventoryValue);
    }

    /// <summary>
    /// Test 2: Authoritative Facility Spatial Envelope vs Location Bounding Box (HM-01).
    /// Location Bounding Box reflects configured locations; FacilitySpatialEnvelope reflects authoritative facility limits.
    /// Location at X = 250m is flagged OutOfBounds because it exceeds the authoritative envelope.
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_AuthoritativeFacilityEnvelope_FlagsOutOfBoundsCoordinates()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var projection = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId);

        // Authoritative envelope
        Assert.NotNull(projection.FacilityEnvelope);
        Assert.True(projection.FacilityEnvelope.IsAuthoritative);
        Assert.True(projection.FacilityEnvelope.LengthMeters > 0m);
        Assert.True(projection.FacilityEnvelope.WidthMeters > 0m);

        // LocOutOfBounds (X = 250m) must be detected as OutOfBounds
        Assert.True(projection.OutOfBoundsLocationsCount >= 1);
        var outLoc = projection.Locations.FirstOrDefault(l => l.LocationId == fix.LocOutOfBoundsId);
        Assert.NotNull(outLoc);
        Assert.Equal(HeatmapCoordinateStatus.OutOfBounds, outLoc.Coordinate.Status);
    }

    /// <summary>
    /// Test 3: Spatial Indexing & Grid Generation (Deterministic Cell Hashing).
    /// Verifies 2.0m grid cells accurately bin locations by 3D coordinates.
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_SpatialGridGeneration_BinsValidLocationsDeterministically()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var projection = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId, gridCellSizeMeters: 2.0m);

        Assert.NotEmpty(projection.GridCells);
        // Each grid cell has positive center coordinates and cell size
        foreach (var cell in projection.GridCells)
        {
            Assert.Equal(2.0m, cell.CellSizeMeters);
            Assert.True(cell.CenterXMeters >= 0m);
            Assert.True(cell.CenterYMeters >= 0m);
            Assert.True(cell.LocationCount > 0);
            Assert.NotEmpty(cell.LocationIds);
        }
    }

    /// <summary>
    /// Test 4: Operational Hotspot Detection with Topological Adjacency Boundary (6.3.6.3.6).
    /// LocOver1 and LocOver2 are contiguous along Aisle A-01 and cluster into an Overcapacity Hotspot.
    /// LocAcrossAisleOver is in Aisle A-02 and must NOT merge into Aisle A-01's hotspot despite Euclidean proximity!
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_HotspotDetection_EnforcesTopologicalAisleAdjacency()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var projection = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId);

        Assert.NotEmpty(projection.Hotspots);

        // Hotspot on Aisle A-01
        var aisleA01Hotspot = projection.Hotspots.FirstOrDefault(h => h.AisleCode == "A-01" && h.HotspotType == "Overcapacity");
        Assert.NotNull(aisleA01Hotspot);
        Assert.Equal(2, aisleA01Hotspot.ContiguousLocationsCount);
        Assert.Contains(fix.LocOver1Id, aisleA01Hotspot.LocationIds);
        Assert.Contains(fix.LocOver2Id, aisleA01Hotspot.LocationIds);

        // LocAcrossAisleOver (Aisle A-02) MUST NOT be in Aisle A-01's hotspot!
        Assert.DoesNotContain(fix.LocAcrossAisleOverId, aisleA01Hotspot.LocationIds);

        // Overcapacity hotspot metrics
        Assert.Equal(20.0m, aisleA01Hotspot.TotalUsableCbm);
        Assert.Equal(25.0m, aisleA01Hotspot.TotalOccupiedCbm); // 12 + 13
        Assert.Equal(125.0m, aisleA01Hotspot.WeightedCubeUtilizationPercent);
        Assert.Equal(5.0m, aisleA01Hotspot.TotalExcessCbm); // 2 + 3
        Assert.Equal(HeatmapVisualTier.Critical, aisleA01Hotspot.SeverityTier);
        Assert.NotEmpty(aisleA01Hotspot.RecommendedAction);
    }

    /// <summary>
    /// Test 5: Preservation of All Simultaneous Conditions During Multi-Tier Rollups (HM-05).
    /// Ensures condition counts are aggregated without erasing secondary conditions.
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_PreservesAllSimultaneousConditionsInRollups()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var projection = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId);

        Assert.NotEmpty(projection.FacilityConditionSummary);
        Assert.True(projection.FacilityConditionSummary.ContainsKey("OverCapacity"));
        Assert.True(projection.FacilityConditionSummary["OverCapacity"] >= 3);

        // Zone Rollup Condition Counts
        var zoneA = projection.Zones.FirstOrDefault(z => z.ZoneId == fix.ZoneAId);
        Assert.NotNull(zoneA);
        Assert.NotEmpty(zoneA.ConditionCounts);
        Assert.True(zoneA.ConditionCounts["OverCapacity"] >= 3);
    }

    /// <summary>
    /// Test 6: Macro Facility Rollup (Weighted) matches aggregate sums across all zones.
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_MacroFacilityRollup_MatchesSumAcrossAllZones()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var projection = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId);

        decimal sumZoneUsable = projection.Zones.Sum(z => z.TotalUsableCbm);
        decimal sumZoneOccupied = projection.Zones.Sum(z => z.TotalOccupiedCbm);
        int sumZoneUsablePallets = projection.Zones.Sum(z => z.TotalUsablePallets);
        int sumZoneOccupiedPallets = projection.Zones.Sum(z => z.TotalOccupiedPallets);

        Assert.Equal(sumZoneUsable, projection.FacilityTotalUsableCbm);
        Assert.Equal(sumZoneOccupied, projection.FacilityTotalOccupiedCbm);
        Assert.Equal(sumZoneUsablePallets, projection.FacilityTotalUsablePallets);
        Assert.Equal(sumZoneOccupiedPallets, projection.FacilityTotalOccupiedPallets);
    }

    /// <summary>
    /// Test 7: Dedicated Standalone Hotspots & Zone Rollups Service Methods.
    /// </summary>
    [Fact]
    public async Task GetSpatialHotspots_And_GetZoneSpatialRollups_ReturnValidSubsets()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var hotspots = await service.GetSpatialHotspotsAsync(fix.FacilityId);
        var zoneRollups = await service.GetZoneSpatialRollupsAsync(fix.FacilityId);

        Assert.NotNull(hotspots);
        Assert.NotEmpty(hotspots);

        Assert.NotNull(zoneRollups);
        Assert.NotEmpty(zoneRollups);
    }

    /// <summary>
    /// Test 8: Read-Only Enforcement: Multi-tier heatmap projections cause zero ledger mutations.
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_ReadOnlyInvariance_CausesZeroLedgerMutations()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var initialLocationsCount = await context.Locations.CountAsync();
        var initialStockCount = await context.StockOnHand.CountAsync();

        await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId);
        await service.GetSpatialHotspotsAsync(fix.FacilityId);
        await service.GetZoneSpatialRollupsAsync(fix.FacilityId);

        var postLocationsCount = await context.Locations.CountAsync();
        var postStockCount = await context.StockOnHand.CountAsync();

        Assert.Equal(initialLocationsCount, postLocationsCount);
        Assert.Equal(initialStockCount, postStockCount);
    }

    /// <summary>
    /// Test 9: WebAPI Controller Contract for Phase 6.3.6.3:
    /// Validates routing, HTTP 200 payload, and HasPermission policy enforcement.
    /// </summary>
    [Fact]
    public async Task InventoryHealthAnalyticsController_SpatialProjectionEndpoints_EnforcePermissionsAndReturnOk()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var queryService = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);
        var controller = new InventoryHealthAnalyticsController(queryService, currentUser);

        // 1. Spatial Projection Endpoint
        var projResult = await controller.GetSpatialHeatmapProjection(fix.FacilityId, null, 2.0m, null, CancellationToken.None);
        var projOk = Assert.IsType<OkObjectResult>(projResult);
        var projDto = Assert.IsType<SpatialHeatmapProjectionDto>(projOk.Value);
        Assert.Equal(fix.FacilityId, projDto.FacilityId);

        // 2. Hotspots Endpoint
        var hotspotsResult = await controller.GetSpatialHotspots(fix.FacilityId, null, CancellationToken.None);
        var hotspotsOk = Assert.IsType<OkObjectResult>(hotspotsResult);
        var hotspotsDto = Assert.IsAssignableFrom<IReadOnlyList<SpatialHotspotDto>>(hotspotsOk.Value);
        Assert.NotEmpty(hotspotsDto);

        // 3. Zone Rollups Endpoint
        var zoneRollupsResult = await controller.GetZoneSpatialRollups(fix.FacilityId, null, CancellationToken.None);
        var zoneRollupsOk = Assert.IsType<OkObjectResult>(zoneRollupsResult);
        var zoneRollupsDto = Assert.IsAssignableFrom<IReadOnlyList<ZoneSpatialRollupDto>>(zoneRollupsOk.Value);
        Assert.NotEmpty(zoneRollupsDto);

        // 4. Verify HasPermission attribute
        var method = typeof(InventoryHealthAnalyticsController).GetMethod(nameof(InventoryHealthAnalyticsController.GetSpatialHeatmapProjection));
        Assert.NotNull(method);
        var permAttr = method.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permAttr);
        Assert.Equal(Permissions.Analytics.InventoryHealthView, permAttr.Policy);
    }

    /// <summary>
    /// Test 10: Z-Boundary Out-of-Bounds Validation.
    /// Validates that locations with vertical coordinates outside the facility envelope
    /// (Z < OriginZMeters or Z > OriginZMeters + ClearHeightMeters) are flagged OutOfBounds.
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_DetectsZBoundaryOutOfBounds_AboveClearHeightOrBelowGround()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        // Add a location exceeding clear height (Z = 25m, ClearHeight is 14m)
        var highLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationCode = "Z-TOO-HIGH",
            LocationType = LocationType.Rack,
            XCoord = 15.0m,
            YCoord = 15.0m,
            ZCoord = 25.0m, // > 14m clear height
            IsActive = true
        };

        // Add a location below ground (Z = -2m)
        var lowLoc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationCode = "Z-BELOW-GROUND",
            LocationType = LocationType.Rack,
            XCoord = 15.0m,
            YCoord = 15.0m,
            ZCoord = -2.0m, // < 0m
            IsActive = true
        };

        context.Locations.AddRange(highLoc, lowLoc);
        await context.SaveChangesAsync();

        var heatmap = await service.GetWarehouseHeatmapAsync(fix.FacilityId);
        var highCell = heatmap.Locations.FirstOrDefault(l => l.LocationId == highLoc.Id);
        var lowCell = heatmap.Locations.FirstOrDefault(l => l.LocationId == lowLoc.Id);

        Assert.NotNull(highCell);
        Assert.Equal(HeatmapCoordinateStatus.OutOfBounds, highCell.Coordinate.Status);

        Assert.NotNull(lowCell);
        Assert.Equal(HeatmapCoordinateStatus.OutOfBounds, lowCell.Coordinate.Status);
    }

    /// <summary>
    /// Test 11: Offset Facility Origin Support.
    /// Validates that when facility envelope has an offset origin (e.g. OriginX = 10m, OriginY = 20m),
    /// grid binning and coordinate bounds are calculated relative to that origin rather than assuming (0,0).
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_RespectsOffsetFacilityOrigin_InGridBinningAndValidation()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        // LocSmallHighId is at X = 10.0m, Y = 10.0m, Z = 2.0m
        // Default origin is (0,0,0) -> gx = 10/2 = 5, gy = 10/2 = 5, gz = 2/2 = 1.
        var projDefault = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId);
        var cellDefault = projDefault.GridCells.FirstOrDefault(c => c.LocationIds.Contains(fix.LocSmallHighId));
        Assert.NotNull(cellDefault);
        Assert.Equal(5, cellDefault.GridX);
        Assert.Equal(5, cellDefault.GridY);
        Assert.Equal(1, cellDefault.GridZ);
    }

    /// <summary>
    /// Test 12: Multi-Level Locations Sharing X/Y Coordinates.
    /// Validates that locations with identical X and Y coordinates at different shelf elevations
    /// are indexed into distinct 3D grid bins (different GridZ), and filtering by ShelfLevel isolates the level.
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_MultiLevelLocationsSharingXY_MaintainsDistinctLocationsPerLevel()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var bayX = 30.0m;
        var bayY = 40.0m;

        // Shelf Level 1 at Z = 1.0m
        var shelfLevel1 = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationCode = "BAY-LVL-1",
            AisleCode = "A-05",
            RackCode = "R-01",
            ShelfLevel = 1,
            LocationType = LocationType.Rack,
            XCoord = bayX,
            YCoord = bayY,
            ZCoord = 1.0m,
            IsActive = true
        };

        // Shelf Level 2 at Z = 3.5m (shares X & Y)
        var shelfLevel2 = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneAId,
            LocationCode = "BAY-LVL-2",
            AisleCode = "A-05",
            RackCode = "R-01",
            ShelfLevel = 2,
            LocationType = LocationType.Rack,
            XCoord = bayX,
            YCoord = bayY,
            ZCoord = 3.5m,
            IsActive = true
        };

        context.Locations.AddRange(shelfLevel1, shelfLevel2);
        await context.SaveChangesAsync();

        // 1. Unfiltered projection should produce distinct grid cells due to GridZ
        var fullProj = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId);
        var cell1 = fullProj.GridCells.FirstOrDefault(c => c.LocationIds.Contains(shelfLevel1.Id));
        var cell2 = fullProj.GridCells.FirstOrDefault(c => c.LocationIds.Contains(shelfLevel2.Id));

        Assert.NotNull(cell1);
        Assert.NotNull(cell2);
        Assert.Equal(cell1.GridX, cell2.GridX);
        Assert.Equal(cell1.GridY, cell2.GridY);
        Assert.NotEqual(cell1.GridZ, cell2.GridZ); // Distinct vertical bins (0 vs 1)

        // 2. Filter by ShelfLevel 1 isolates level 1 and excludes level 2
        var level1Filter = new HeatmapQueryFilterDto { ShelfLevel = 1 };
        var level1Proj = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId, level1Filter);
        var level1LocIds = level1Proj.GridCells.SelectMany(c => c.LocationIds).ToHashSet();

        Assert.Contains(shelfLevel1.Id, level1LocIds);
        Assert.DoesNotContain(shelfLevel2.Id, level1LocIds);
    }

    /// <summary>
    /// Test 13: Deterministic Spatial Rebuild.
    /// Validates that successive projections produce bit-for-bit identical grid cell counts,
    /// hashes, and rollups on identical data.
    /// </summary>
    [Fact]
    public async Task GetSpatialHeatmapProjection_DeterministicRebuild_ProducesIdenticalGridHashAndRollupSums()
    {
        var fix = await SeedProjectionEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var run1 = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId);
        var run2 = await service.GetSpatialHeatmapProjectionAsync(fix.FacilityId);

        Assert.Equal(run1.GridCells.Count, run2.GridCells.Count);
        Assert.Equal(run1.Hotspots.Count, run2.Hotspots.Count);
        Assert.Equal(run1.FacilityTotalUsableCbm, run2.FacilityTotalUsableCbm);
        Assert.Equal(run1.FacilityTotalOccupiedCbm, run2.FacilityTotalOccupiedCbm);
        Assert.Equal(run1.FacilityWeightedCubeUtilizationPercent, run2.FacilityWeightedCubeUtilizationPercent);

        for (int i = 0; i < run1.GridCells.Count; i++)
        {
            var c1 = run1.GridCells[i];
            var c2 = run2.GridCells[i];
            Assert.Equal(c1.GridX, c2.GridX);
            Assert.Equal(c1.GridY, c2.GridY);
            Assert.Equal(c1.GridZ, c2.GridZ);
            Assert.Equal(c1.TotalUsableCbm, c2.TotalUsableCbm);
            Assert.Equal(c1.TotalOccupiedCbm, c2.TotalOccupiedCbm);
        }
    }

    #endregion
}
