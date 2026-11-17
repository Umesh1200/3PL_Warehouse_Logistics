using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Exhaustive unit and integration test suite for Phase 6.3.3:
/// Cubic-Volume & Warehouse Space Utilization Engine.
/// Verifies independent Cube Fill % and Pallet Occupancy %, usable capacity with safety clearances,
/// uncapped over-capacity detection, mixed-SKU/status partitioning without double counting,
/// zone/aisle aggregations, deduplication/replay, and security boundaries.
/// </summary>
public class SpaceUtilizationAnalyticsTests
{
    private class TestFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid Zone1Id { get; set; } = Guid.NewGuid();
        public Guid Zone2Id { get; set; } = Guid.NewGuid();
        public Guid Location1Id { get; set; } = Guid.NewGuid();
        public Guid Location2Id { get; set; } = Guid.NewGuid();
        public Guid Location3Id { get; set; } = Guid.NewGuid();
    }

    private static async Task<TestFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var fac1 = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "ORD-01",
            Name = "Chicago Regional Logistics Center",
            IsActive = true
        };

        var fac2 = new Facility
        {
            Id = fix.Facility2Id,
            FacilityCode = "DFW-01",
            Name = "Dallas Regional Logistics Center",
            IsActive = true
        };

        var zone1 = new WarehouseZone
        {
            Id = fix.Zone1Id,
            FacilityId = fix.FacilityId,
            ZoneCode = "BULK-01",
            Name = "Bulk High-Bay Racking",
            ZoneType = WarehouseZoneType.BulkStorage,
            IsActive = true
        };

        var zone2 = new WarehouseZone
        {
            Id = fix.Zone2Id,
            FacilityId = fix.FacilityId,
            ZoneCode = "PICK-01",
            Name = "Active Pick Bin Module",
            ZoneType = WarehouseZoneType.PickFace,
            IsActive = true
        };

        var loc1 = new Location
        {
            Id = fix.Location1Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone1Id,
            LocationCode = "BULK-A-01",
            LocationType = LocationType.Rack,
            MaxVolumeCm3 = 10_000_000m, // 10.0 CBM
            MaxWeightKg = 2000m,
            IsActive = true
        };

        var loc2 = new Location
        {
            Id = fix.Location2Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone1Id,
            LocationCode = "BULK-A-02",
            LocationType = LocationType.Rack,
            MaxVolumeCm3 = 10_000_000m, // 10.0 CBM
            MaxWeightKg = 2000m,
            IsActive = true
        };

        var loc3 = new Location
        {
            Id = fix.Location3Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.Zone2Id,
            LocationCode = "PICK-B-01",
            LocationType = LocationType.Bin,
            MaxVolumeCm3 = 2_000_000m, // 2.0 CBM
            MaxWeightKg = 500m,
            IsActive = true
        };

        await context.Facilities.AddRangeAsync(fac1, fac2);
        await context.WarehouseZones.AddRangeAsync(zone1, zone2);
        await context.Locations.AddRangeAsync(loc1, loc2, loc3);
        await context.SaveChangesAsync();

        return fix;
    }

    [Fact]
    public async Task UsableCapacity_AccountingForSafetyClearances_CalculatesAvailableCapacityAccurately()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Physical volume = 10.0 CBM. Safety clearance factor = 0.85 (15% flue/sprinkler clearance).
        // Usable volume = 8.5 CBM. Occupied = 5.1 CBM.
        // Expected available = 8.5 - 5.1 = 3.4 CBM.
        // Expected Cube Fill = (5.1 / 8.5) * 100 = 60.00%.
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 1L,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "BULK-A-01",
            snapshotDateUtc: snapshotDate,
            usableCbm: 8.5m,
            occupiedCbm: 5.1m,
            usablePalletPositions: 4,
            occupiedPalletPositions: 2,
            zoneId: fix.Zone1Id,
            locationType: LocationType.Rack,
            aisleCode: "A",
            rackCode: "01",
            physicalCbm: 10.0m,
            usableCapacityFactor: 0.85m,
            physicalPalletPositions: 4);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var locations = await queryService.GetLocationSpaceUtilizationAsync(fix.FacilityId, snapshotDate, fix.Zone1Id);

        Assert.Single(locations);
        var loc = locations[0];
        Assert.Equal(10.0m, loc.PhysicalCubicMeters);
        Assert.Equal(0.85m, loc.UsableCapacityFactor);
        Assert.Equal(8.5m, loc.UsableCubicMeters);
        Assert.Equal(5.1m, loc.OccupiedCubicMeters);
        Assert.Equal(3.4m, loc.AvailableCubicMeters);
        Assert.Equal(60.00m, loc.CubeUtilizationPercent);
        Assert.Equal(LocationSpaceUtilizationClassification.Optimal, loc.SpaceClassification);
    }

    [Fact]
    public async Task CubeFillAndPalletOccupancy_StrictlySeparated_NeverConflatedOrBlended()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Usable CBM = 10.0, Occupied CBM = 8.0 -> Cube Fill = 80.00%
        // Usable Pallets = 4, Occupied Pallets = 1 -> Pallet Occupancy = 25.00%
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 10L,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "BULK-A-01",
            snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m,
            occupiedCbm: 8.0m,
            usablePalletPositions: 4,
            occupiedPalletPositions: 1,
            zoneId: fix.Zone1Id,
            locationType: LocationType.Rack);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var locations = await queryService.GetLocationSpaceUtilizationAsync(fix.FacilityId, snapshotDate);

        var loc = locations.First(l => l.LocationId == fix.Location1Id);
        Assert.Equal(80.00m, loc.CubeUtilizationPercent);
        Assert.Equal(25.00m, loc.PalletOccupancyPercent);
        Assert.NotEqual(loc.CubeUtilizationPercent, loc.PalletOccupancyPercent); // Strict independence
    }

    [Fact]
    public async Task OverCapacity_NotCappedAt100Percent_PreservesActualOverfillAndGeneratesAlert()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Usable CBM = 10.0. Occupied CBM = 11.5 -> 115.00% (Over-capacity!)
        // Invariant: Do NOT cap utilization at 100%. Retain 115% and flag condition.
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 20L,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "BULK-A-01",
            snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m,
            occupiedCbm: 11.5m,
            usablePalletPositions: 4,
            occupiedPalletPositions: 5, // 5 out of 4 pallets = 125%
            zoneId: fix.Zone1Id,
            locationType: LocationType.Rack,
            distinctSkuCount: 3,
            totalUnitsStored: 150m);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var locations = await queryService.GetLocationSpaceUtilizationAsync(fix.FacilityId, snapshotDate);

        var loc = locations.First(l => l.LocationId == fix.Location1Id);
        Assert.Equal(115.00m, loc.CubeUtilizationPercent);
        Assert.Equal(125.00m, loc.PalletOccupancyPercent);
        Assert.True(loc.IsOverCapacity);
        Assert.Equal(1.5m, loc.OverCapacityExcessCbm);
        Assert.Equal(LocationSpaceUtilizationClassification.OverCapacity, loc.SpaceClassification);

        // Alert Query
        var alerts = await queryService.GetOverCapacityAlertsAsync(fix.FacilityId, snapshotDate);
        Assert.Single(alerts);
        var alert = alerts[0];
        Assert.Equal("BULK-A-01", alert.LocationCode);
        Assert.Equal(115.00m, alert.CubeUtilizationPercent);
        Assert.Equal(1.5m, alert.ExcessCbm);
    }

    [Fact]
    public async Task MixedSkuAndStatusPartitioning_PreventsDoubleCountingAndPreservesPartitions()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Location has 2 distinct SKUs (Mixed-SKU).
        // Total volume occupied = 6.0 CBM:
        // 3.0 CBM Available, 1.5 CBM Quarantined, 1.5 CBM Damaged.
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 30L,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "BULK-A-01",
            snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m,
            occupiedCbm: 6.0m,
            usablePalletPositions: 4,
            occupiedPalletPositions: 3,
            zoneId: fix.Zone1Id,
            distinctSkuCount: 2,
            totalUnitsStored: 100m,
            unitsAvailable: 60m,
            unitsQuarantined: 20m,
            unitsDamaged: 20m,
            occupiedCbmAvailable: 3.0m,
            occupiedCbmQuarantined: 1.5m,
            occupiedCbmDamaged: 1.5m);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var locations = await queryService.GetLocationSpaceUtilizationAsync(fix.FacilityId, snapshotDate);

        var loc = locations.First(l => l.LocationId == fix.Location1Id);
        Assert.Equal(2, loc.DistinctSkuCount);
        Assert.True(loc.IsMixedSkuLocation);
        Assert.Equal(100m, loc.TotalUnitsStored);
        Assert.Equal(6.0m, loc.OccupiedCubicMeters);
        Assert.Equal(loc.OccupiedCubicMeters, loc.OccupiedCbmAvailable + loc.OccupiedCbmQuarantined + loc.OccupiedCbmDamaged); // Zero double-counting
        Assert.Equal(3.0m, loc.OccupiedCbmAvailable);
        Assert.Equal(1.5m, loc.OccupiedCbmQuarantined);
        Assert.Equal(1.5m, loc.OccupiedCbmDamaged);
    }

    [Fact]
    public async Task BlockedAndUnavailableLocations_ClassifiedCorrectlyAndExcludedFromActiveUsableMetrics()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Location 1: Blocked due to damaged upright beam
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 40L,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "BULK-A-01",
            snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m,
            occupiedCbm: 0m,
            usablePalletPositions: 4,
            occupiedPalletPositions: 0,
            zoneId: fix.Zone1Id,
            isBlocked: true,
            blockReason: "Damaged rack upright column pending repair");

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var locations = await queryService.GetLocationSpaceUtilizationAsync(fix.FacilityId, snapshotDate);

        var loc = locations.First(l => l.LocationId == fix.Location1Id);
        Assert.True(loc.IsBlocked);
        Assert.Equal("Damaged rack upright column pending repair", loc.BlockReason);
        Assert.Equal(LocationSpaceUtilizationClassification.BlockedOrUnavailable, loc.SpaceClassification);

        var summary = await queryService.GetFacilitySpaceUtilizationSummaryAsync(fix.FacilityId, snapshotDate);
        Assert.Equal(1, summary.BlockedLocationsCount);
    }

    [Fact]
    public async Task MissingDimensions_FlagsFallbackWithoutTreatingVolumeAsZero()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Location storing items with unknown dimensions using estimated fallback
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 50L,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "BULK-A-01",
            snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m,
            occupiedCbm: 2.0m, // Estimated fallback volume
            usablePalletPositions: 4,
            occupiedPalletPositions: 1,
            zoneId: fix.Zone1Id,
            hasMissingDimensions: true);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var locations = await queryService.GetLocationSpaceUtilizationAsync(fix.FacilityId, snapshotDate);

        var loc = locations.First(l => l.LocationId == fix.Location1Id);
        Assert.True(loc.HasMissingDimensionsFallback);
        Assert.Equal(2.0m, loc.OccupiedCubicMeters); // Not zero!
    }

    [Fact]
    public async Task ZoneAisleAndFacilityRollups_AggregateHierarchically()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Location 1 (Zone 1, Aisle A): Usable 10.0 CBM, Occupied 6.0 CBM, 4 Pallets (2 Occ)
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 60L, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "BULK-A-01", snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m, occupiedCbm: 6.0m, usablePalletPositions: 4, occupiedPalletPositions: 2,
            zoneId: fix.Zone1Id, aisleCode: "A", rackCode: "01",
            occupiedCbmAvailable: 4.0m, occupiedCbmAllocated: 2.0m);

        // Location 2 (Zone 1, Aisle A): Usable 10.0 CBM, Occupied 4.0 CBM, 4 Pallets (1 Occ)
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 61L, facilityId: fix.FacilityId,
            locationId: fix.Location2Id, locationCode: "BULK-A-02", snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m, occupiedCbm: 4.0m, usablePalletPositions: 4, occupiedPalletPositions: 1,
            zoneId: fix.Zone1Id, aisleCode: "A", rackCode: "02",
            occupiedCbmAvailable: 4.0m);

        // Location 3 (Zone 2, Aisle B): Usable 2.0 CBM, Occupied 1.0 CBM, 1 Pallet (0 Occ)
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 62L, facilityId: fix.FacilityId,
            locationId: fix.Location3Id, locationCode: "PICK-B-01", snapshotDateUtc: snapshotDate,
            usableCbm: 2.0m, occupiedCbm: 1.0m, usablePalletPositions: 1, occupiedPalletPositions: 0,
            zoneId: fix.Zone2Id, aisleCode: "B", rackCode: "01",
            occupiedCbmAvailable: 1.0m);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var summary = await queryService.GetFacilitySpaceUtilizationSummaryAsync(fix.FacilityId, snapshotDate);

        // Facility level totals
        Assert.Equal(3, summary.TotalLocationsCount);
        Assert.Equal(22.0m, summary.TotalUsableCbm);
        Assert.Equal(11.0m, summary.TotalOccupiedCbm);
        Assert.Equal(50.00m, summary.CubeFillPercent); // 11 / 22 = 50%
        Assert.Equal(9, summary.TotalUsablePalletPositions);
        Assert.Equal(3, summary.TotalOccupiedPalletPositions);
        Assert.Equal(33.33m, summary.PalletOccupancyPercent); // 3 / 9 = 33.33%

        // Status volume partitions
        Assert.Equal(9.0m, summary.OccupiedCbmAvailable);
        Assert.Equal(2.0m, summary.OccupiedCbmAllocated);

        // Zone Rollup
        Assert.Equal(2, summary.ZoneBreakdowns.Count);
        var zone1 = summary.ZoneBreakdowns.First(z => z.ZoneId == fix.Zone1Id);
        Assert.Equal(20.0m, zone1.UsableCbm);
        Assert.Equal(10.0m, zone1.OccupiedCbm);
        Assert.Equal(50.00m, zone1.CubeUtilizationPercent);
        Assert.Single(zone1.AisleBreakdowns);
        Assert.Equal("A", zone1.AisleBreakdowns[0].AisleCode);
        Assert.Equal(2, zone1.AisleBreakdowns[0].LocationsCount);
        Assert.Equal(2, zone1.AisleBreakdowns[0].RacksCount);
    }

    [Fact]
    public async Task DeduplicationAndReplay_ProtectsReadModelsAndEnsuresIdempotency()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();

        // 1. Initial snapshot processing
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: eventId, sequenceNumber: 70L, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "BULK-A-01", snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m, occupiedCbm: 5.0m, usablePalletPositions: 4, occupiedPalletPositions: 2,
            zoneId: fix.Zone1Id);

        Assert.Equal(1, await context.DailyLocationSpaceUtilizationRecords.CountAsync());

        // 2. Duplicate event must be skipped idempotently
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: eventId, sequenceNumber: 70L, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "BULK-A-01", snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m, occupiedCbm: 5.0m, usablePalletPositions: 4, occupiedPalletPositions: 2,
            zoneId: fix.Zone1Id);

        Assert.Equal(1, await context.DailyLocationSpaceUtilizationRecords.CountAsync());

        // 3. Replay projection cleanly purges records
        var replayed = await projService.ReplayProjectionsAsync(
            AnalyticsProjectionService.LocationSpaceUtilizationProjection,
            ProjectionReplayMode.FromBeginning);
        Assert.Equal(1, replayed);
        Assert.Equal(1, await context.DailyLocationSpaceUtilizationRecords.CountAsync());
    }

    [Fact]
    public async Task Authorization_RestrictsFacilityAndInternalStaffBoundaries()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 80L, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "BULK-A-01", snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m, occupiedCbm: 5.0m, usablePalletPositions: 4, occupiedPalletPositions: 2,
            zoneId: fix.Zone1Id);

        // Context 1: Client portal tenant user cannot view internal warehouse space utilization
        var clientContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Client,
            TenantId = Guid.NewGuid()
        };
        var clientQueryService = new AnalyticsQueryService(context, clientContext, NullLogger<AnalyticsQueryService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            clientQueryService.GetLocationSpaceUtilizationAsync(fix.FacilityId, snapshotDate));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            clientQueryService.GetFacilitySpaceUtilizationSummaryAsync(fix.FacilityId, snapshotDate));

        // Context 2: Supervisor assigned to Facility 1 cannot query Facility 2
        var facilitySupervisorContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = fix.FacilityId
        };
        var supervisorQueryService = new AnalyticsQueryService(context, facilitySupervisorContext, NullLogger<AnalyticsQueryService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            supervisorQueryService.GetFacilitySpaceUtilizationSummaryAsync(fix.Facility2Id, snapshotDate));
    }

    [Fact]
    public async Task WebApiController_SpaceUtilizationEndpoints_ReturnExpectedContracts()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Add 1 over-capacity location and 1 normal location
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 90L, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "BULK-A-01", snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m, occupiedCbm: 12.0m, usablePalletPositions: 4, occupiedPalletPositions: 5,
            zoneId: fix.Zone1Id);

        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 91L, facilityId: fix.FacilityId,
            locationId: fix.Location2Id, locationCode: "BULK-A-02", snapshotDateUtc: snapshotDate,
            usableCbm: 10.0m, occupiedCbm: 5.0m, usablePalletPositions: 4, occupiedPalletPositions: 2,
            zoneId: fix.Zone1Id);

        var userContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            PermissionsList = ["analytics:warehouse-heatmap:view"]
        };

        var queryService = new AnalyticsQueryService(context, userContext, NullLogger<AnalyticsQueryService>.Instance);
        var controller = new SpaceUtilizationAnalyticsController(queryService, projService, userContext);

        // 1. Get Summary
        var summaryRes = await controller.GetFacilitySpaceUtilizationSummary(fix.FacilityId, snapshotDate, CancellationToken.None);
        var okSummary = Assert.IsType<OkObjectResult>(summaryRes);
        var summaryDto = Assert.IsType<FacilitySpaceUtilizationSummaryDto>(okSummary.Value);
        Assert.Equal(2, summaryDto.TotalLocationsCount);
        Assert.Equal(1, summaryDto.OverCapacityLocationsCount);
        Assert.Equal(17.0m, summaryDto.TotalOccupiedCbm);

        // 2. Get Locations
        var locsRes = await controller.GetLocationSpaceUtilization(fix.FacilityId, snapshotDate, fix.Zone1Id, CancellationToken.None);
        var okLocs = Assert.IsType<OkObjectResult>(locsRes);
        var locsDto = Assert.IsAssignableFrom<IReadOnlyList<LocationSpaceUtilizationDto>>(okLocs.Value);
        Assert.Equal(2, locsDto.Count);

        // 3. Get Over-Capacity Alerts
        var alertsRes = await controller.GetOverCapacityAlerts(fix.FacilityId, snapshotDate, CancellationToken.None);
        var okAlerts = Assert.IsType<OkObjectResult>(alertsRes);
        var alertsDto = Assert.IsAssignableFrom<IReadOnlyList<OverCapacityAlertDto>>(okAlerts.Value);
        Assert.Single(alertsDto);
        Assert.Equal("BULK-A-01", alertsDto[0].LocationCode);
        Assert.Equal(120.00m, alertsDto[0].CubeUtilizationPercent);
    }
}
