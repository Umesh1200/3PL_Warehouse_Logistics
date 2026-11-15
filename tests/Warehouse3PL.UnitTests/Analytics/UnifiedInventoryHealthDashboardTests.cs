using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
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
/// Unit and integration test suite for Phase 6.3.6.1:
/// Unified Inventory Health Dashboard Foundation.
/// Verifies:
/// 1. Consolidated executive rollup KPIs (valuation, units, active SKUs, space, velocity, ABC).
/// 2. Deterministic Composite Inventory Health Score algorithm & grade classification.
/// 3. Five subsystem domain summaries (Aging & Expiry, Space, Velocity, ABC, Stock Risk).
/// 4. Freshness metadata, snapshot age in hours, and automated staleness detection.
/// 5. Strict multi-tenant isolation and client portal masking.
/// 6. Facility scoping and RBAC authorization boundaries.
/// 7. Empty facility / zero-inventory divide-by-zero resilience.
/// 8. Authoritative inventory ledger immutability (strictly advisory intelligence).
/// 9. WebAPI controller contracts, endpoints, and security attributes.
/// </summary>
public class UnifiedInventoryHealthDashboardTests
{
    private class TestFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid Client1Id { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();

        public Guid Item1Id { get; set; } = Guid.NewGuid();
        public Guid Item2Id { get; set; } = Guid.NewGuid();
        public Guid Item3Id { get; set; } = Guid.NewGuid();
        public Guid ItemClient2Id { get; set; } = Guid.NewGuid();

        public Guid ZoneId { get; set; } = Guid.NewGuid();
        public Guid Location1Id { get; set; } = Guid.NewGuid();
        public Guid Location2Id { get; set; } = Guid.NewGuid();
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

    private static async Task<TestFixture> SeedEnvironmentAsync(DateTime? snapshotDateUtc = null)
    {
        var fix = new TestFixture();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);

        var asOf = (snapshotDateUtc ?? DateTime.UtcNow).Date;

        var fac1 = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "ORD-01",
            Name = "Chicago Logistics Center",
            IsActive = true
        };

        var fac2 = new Facility
        {
            Id = fix.Facility2Id,
            FacilityCode = "DFW-01",
            Name = "Dallas Logistics Center",
            IsActive = true
        };

        var client1 = new Client
        {
            Id = fix.Client1Id,
            AccountCode = "CLI-ACME",
            CompanyName = "Acme Global",
            Status = ClientStatus.Active
        };

        var client2 = new Client
        {
            Id = fix.Client2Id,
            AccountCode = "CLI-ZENITH",
            CompanyName = "Zenith Retail",
            Status = ClientStatus.Active
        };

        var zone = new WarehouseZone
        {
            Id = fix.ZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "Z-PICK-01",
            Name = "Primary Pick Zone",
            IsActive = true
        };

        var loc1 = new Location
        {
            Id = fix.Location1Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneId,
            LocationCode = "A-01-01",
            LocationType = LocationType.Rack,
            IsActive = true
        };

        var loc2 = new Location
        {
            Id = fix.Location2Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneId,
            LocationCode = "A-01-02",
            LocationType = LocationType.Rack,
            IsActive = true
        };

        var item1 = new Item
        {
            Id = fix.Item1Id,
            ClientId = fix.Client1Id,
            Sku = "SKU-AAA-01",
            Description = "Enterprise CPU Server",
            UnitPrice = 1000m,
            Status = ItemStatus.Active
        };

        var item2 = new Item
        {
            Id = fix.Item2Id,
            ClientId = fix.Client1Id,
            Sku = "SKU-BBB-02",
            Description = "Standard DDR5 RAM",
            UnitPrice = 200m,
            Status = ItemStatus.Active
        };

        var item3 = new Item
        {
            Id = fix.Item3Id,
            ClientId = fix.Client1Id,
            Sku = "SKU-CCC-03",
            Description = "Cat6 Patch Cable",
            UnitPrice = 10m,
            Status = ItemStatus.Active
        };

        var itemClient2 = new Item
        {
            Id = fix.ItemClient2Id,
            ClientId = fix.Client2Id,
            Sku = "SKU-ZZZ-99",
            Description = "Client 2 Exclusive Unit",
            UnitPrice = 500m,
            Status = ItemStatus.Active
        };

        await context.Facilities.AddRangeAsync(fac1, fac2);
        await context.Clients.AddRangeAsync(client1, client2);
        await context.WarehouseZones.AddAsync(zone);
        await context.Locations.AddRangeAsync(loc1, loc2);
        await context.Items.AddRangeAsync(item1, item2, item3, itemClient2);

        // 1. Seed Stock On Hand
        await context.StockOnHand.AddRangeAsync(
            new StockOnHand { FacilityId = fix.FacilityId, ClientId = fix.Client1Id, ItemId = fix.Item1Id, LocationId = fix.Location1Id, QtyAvailable = 50m },
            new StockOnHand { FacilityId = fix.FacilityId, ClientId = fix.Client1Id, ItemId = fix.Item2Id, LocationId = fix.Location2Id, QtyAvailable = 200m },
            new StockOnHand { FacilityId = fix.FacilityId, ClientId = fix.Client1Id, ItemId = fix.Item3Id, LocationId = fix.Location2Id, QtyAvailable = 1000m },
            new StockOnHand { FacilityId = fix.FacilityId, ClientId = fix.Client2Id, ItemId = fix.ItemClient2Id, LocationId = fix.Location2Id, QtyAvailable = 100m }
        );

        // 2. Seed Daily Inventory Aging Snapshots (Phase 6.3.2)
        // Item 1: Active stock (0-30 days, $50,000)
        var snap1 = new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = fix.Item1Id,
            Sku = "SKU-AAA-01",
            LocationId = fix.Location1Id,
            LocationCode = "A-01-01",
            SnapshotDateUtc = asOf,
            QuantityOnHand = 50m,
            QtyAvailable = 50m,
            TotalInventoryValue = 50000m,
            UnitCost = 1000m,
            ReceiptAgeDays = 15,
            LastMovementAgeDays = 2,
            LastDemandAgeDays = 2,
            AgingBucket = InventoryAgingBucket.Current_0_30,
            IsAged = false,
            IsSlowMoving = false,
            IsNonMoving = false,
            IsDeadStock = false,
            IsExpired = false,
            IsNearExpiry = false,
            OccupiedCbm = 5.0m,
            PalletFootprint = 2.0m
        };

        // Item 2: Slow moving stock (91-180 days, $40,000)
        var snap2 = new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = fix.Item2Id,
            Sku = "SKU-BBB-02",
            LocationId = fix.Location2Id,
            LocationCode = "A-01-02",
            SnapshotDateUtc = asOf,
            QuantityOnHand = 200m,
            QtyAvailable = 200m,
            TotalInventoryValue = 40000m,
            UnitCost = 200m,
            ReceiptAgeDays = 120,
            LastMovementAgeDays = 45,
            LastDemandAgeDays = 45,
            AgingBucket = InventoryAgingBucket.SlowMoving_91_180,
            IsAged = true,
            IsSlowMoving = true,
            IsNonMoving = false,
            IsDeadStock = false,
            IsExpired = false,
            IsNearExpiry = false,
            OccupiedCbm = 10.0m,
            PalletFootprint = 4.0m
        };

        // Item 3: Dead stock (>365 days, $10,000) with expired lot
        var snap3 = new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = fix.Item3Id,
            Sku = "SKU-CCC-03",
            LocationId = fix.Location2Id,
            LocationCode = "A-01-02",
            SnapshotDateUtc = asOf,
            QuantityOnHand = 1000m,
            QtyAvailable = 1000m,
            TotalInventoryValue = 10000m,
            UnitCost = 10m,
            ReceiptAgeDays = 400,
            LastMovementAgeDays = 200,
            LastDemandAgeDays = 200,
            AgingBucket = InventoryAgingBucket.DeadStock_365Plus,
            IsAged = true,
            IsSlowMoving = false,
            IsNonMoving = false,
            IsDeadStock = true,
            IsExpired = true,
            IsNearExpiry = false,
            OccupiedCbm = 15.0m,
            PalletFootprint = 6.0m
        };

        // Client 2 Snapshot: $50,000
        var snapClient2 = new DailyInventoryAgingSnapshot
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.Client2Id,
            ItemId = fix.ItemClient2Id,
            Sku = "SKU-ZZZ-99",
            LocationId = fix.Location2Id,
            LocationCode = "A-01-02",
            SnapshotDateUtc = asOf,
            QuantityOnHand = 100m,
            QtyAvailable = 100m,
            TotalInventoryValue = 50000m,
            UnitCost = 500m,
            ReceiptAgeDays = 20,
            LastMovementAgeDays = 5,
            LastDemandAgeDays = 5,
            AgingBucket = InventoryAgingBucket.Current_0_30,
            IsAged = false,
            IsSlowMoving = false,
            IsNonMoving = false,
            IsDeadStock = false,
            IsExpired = false,
            IsNearExpiry = false,
            OccupiedCbm = 8.0m,
            PalletFootprint = 3.0m
        };

        await context.DailyInventoryAgingSnapshots.AddRangeAsync(snap1, snap2, snap3, snapClient2);

        // 3. Seed Space Utilization Records (Phase 6.3.3)
        var spaceLoc1 = new DailyLocationSpaceUtilizationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneId,
            LocationId = fix.Location1Id,
            LocationCode = "A-01-01",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            PhysicalCubicMeters = 20.0m,
            UsableCubicMeters = 18.0m,
            OccupiedCubicMeters = 10.0m,
            AvailableCubicMeters = 8.0m,
            CubeUtilizationPercent = 55.56m,
            PhysicalPalletPositions = 4,
            UsablePalletPositions = 4,
            OccupiedPalletPositions = 2,
            AvailablePalletPositions = 2,
            PalletOccupancyPercent = 50.0m,
            IsBlocked = false,
            MaxWeightKg = 2000m,
            OccupiedWeightKg = 800m
        };

        // Loc 2 is over-capacity
        var spaceLoc2 = new DailyLocationSpaceUtilizationRecord
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ZoneId = fix.ZoneId,
            LocationId = fix.Location2Id,
            LocationCode = "A-01-02",
            LocationType = LocationType.Rack,
            SnapshotDateUtc = asOf,
            PhysicalCubicMeters = 25.0m,
            UsableCubicMeters = 22.0m,
            OccupiedCubicMeters = 25.0m, // 3.0 CBM over usable
            AvailableCubicMeters = -3.0m,
            CubeUtilizationPercent = 113.64m,
            PhysicalPalletPositions = 6,
            UsablePalletPositions = 5,
            OccupiedPalletPositions = 5,
            AvailablePalletPositions = 0,
            PalletOccupancyPercent = 100.0m,
            IsBlocked = false,
            IsOverCapacity = true,
            OverCapacityExcessCbm = 3.0m,
            MaxWeightKg = 5000m,
            OccupiedWeightKg = 3000m
        };

        await context.DailyLocationSpaceUtilizationRecords.AddRangeAsync(spaceLoc1, spaceLoc2);

        // 4. Seed SKU Velocity Operational Records (Phase 6.3.4, RecordType = 1)
        var startUtc = asOf.AddDays(-90);
        var endUtc = asOf;

        var velItem1 = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.Item1Id, "SKU-AAA-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.FastMoving,
            startUtc, endUtc, pickTasksCount: 80, unitsDemanded: 160m, demandValue: 160000m,
            orderLinesCount: 80, cumulativePercentage: 80m, version: 1,
            observationWindow: VelocityObservationWindow.Days90,
            recordType: SkuClassificationRecordType.VelocityOperational);

        var velItem2 = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.Item2Id, "SKU-BBB-02",
            AbcClassificationCategory.ClassB, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.MediumMoving,
            startUtc, endUtc, pickTasksCount: 20, unitsDemanded: 40m, demandValue: 8000m,
            orderLinesCount: 20, cumulativePercentage: 95m, version: 1,
            observationWindow: VelocityObservationWindow.Days90,
            recordType: SkuClassificationRecordType.VelocityOperational);

        var velItem3 = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.Item3Id, "SKU-CCC-03",
            AbcClassificationCategory.ClassC, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.SlowMoving,
            startUtc, endUtc, pickTasksCount: 2, unitsDemanded: 5m, demandValue: 50m,
            orderLinesCount: 2, cumulativePercentage: 100m, version: 1,
            observationWindow: VelocityObservationWindow.Days90,
            recordType: SkuClassificationRecordType.VelocityOperational);

        await context.SkuVelocityClassificationRecords.AddRangeAsync(velItem1, velItem2, velItem3);

        // 5. Seed ABC Pareto Records (Phase 6.3.5, RecordType = 2)
        var abcRunId = Guid.NewGuid();
        var abcItem1 = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.Item1Id, "SKU-AAA-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.FastMoving,
            startUtc, endUtc, pickTasksCount: 80, unitsDemanded: 160m, demandValue: 160000m,
            orderLinesCount: 80, cumulativePercentage: 85.0m, version: 1,
            observationWindow: VelocityObservationWindow.Days90,
            recordType: SkuClassificationRecordType.AbcParetoStratification,
            classificationRunId: abcRunId,
            rankInPortfolio: 1,
            annualizedConsumptionValue: 648888.89m);

        var abcItem2 = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.Item2Id, "SKU-BBB-02",
            AbcClassificationCategory.ClassB, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.MediumMoving,
            startUtc, endUtc, pickTasksCount: 20, unitsDemanded: 40m, demandValue: 8000m,
            orderLinesCount: 20, cumulativePercentage: 96.0m, version: 1,
            observationWindow: VelocityObservationWindow.Days90,
            recordType: SkuClassificationRecordType.AbcParetoStratification,
            classificationRunId: abcRunId,
            rankInPortfolio: 2,
            annualizedConsumptionValue: 32444.44m);

        var abcItem3 = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.Item3Id, "SKU-CCC-03",
            AbcClassificationCategory.ClassC, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.SlowMoving,
            startUtc, endUtc, pickTasksCount: 2, unitsDemanded: 5m, demandValue: 50m,
            orderLinesCount: 2, cumulativePercentage: 100.0m, version: 1,
            observationWindow: VelocityObservationWindow.Days90,
            recordType: SkuClassificationRecordType.AbcParetoStratification,
            classificationRunId: abcRunId,
            rankInPortfolio: 3,
            annualizedConsumptionValue: 202.78m);

        await context.SkuVelocityClassificationRecords.AddRangeAsync(abcItem1, abcItem2, abcItem3);

        await context.SaveChangesAsync();
        return fix;
    }

    /// <summary>
    /// Test 1: Consolidated executive rollup KPIs and all 5 domain subsystems are calculated accurately.
    /// </summary>
    [Fact]
    public async Task GetUnifiedInventoryHealthDashboard_ConsolidatesAllSubsystems_CalculatesAccurateRollupKpis()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var dashboard = await service.GetUnifiedInventoryHealthDashboardAsync(fix.FacilityId);

        // 1. Facility & Header
        Assert.Equal(fix.FacilityId, dashboard.FacilityId);
        Assert.Equal("ORD-01", dashboard.FacilityCode);
        Assert.Equal("Chicago Logistics Center", dashboard.FacilityName);
        Assert.Null(dashboard.ClientId);

        // 2. Executive Rollup KPIs
        // Total valuation = $50,000 (Item 1) + $40,000 (Item 2) + $10,000 (Item 3) + $50,000 (Client 2) = $150,000
        Assert.Equal(150000m, dashboard.TotalInventoryValuation);
        // Total units = 50 + 200 + 1000 + 100 = 1350
        Assert.Equal(1350m, dashboard.TotalUnitsOnHand);
        Assert.Equal(4, dashboard.TotalActiveSkusCount);

        // Space KPIs: Usable = 18 + 22 = 40. Occupied = 10 + 25 = 35. 35/40 = 87.5%
        Assert.Equal(87.5m, dashboard.SpaceUtilizationPercent);
        // Pallets: Usable = 4 + 5 = 9. Occupied = 2 + 5 = 7. 7/9 = 77.78%
        Assert.Equal(77.78m, dashboard.PalletOccupancyPercent);

        // Dead stock: Item 3 = $10,000 / $150,000 = 6.67%
        Assert.Equal(10000m, dashboard.DeadStockValue);
        Assert.Equal(6.67m, dashboard.DeadStockRatioPercent);
        Assert.Equal(10000m, dashboard.ExpiredStockValue);

        // 3. Subsystem Domain Summaries
        Assert.NotNull(dashboard.AgingAndExpiry);
        Assert.Equal(150000m, dashboard.AgingAndExpiry.TotalInventoryValue);
        Assert.Equal(10000m, dashboard.AgingAndExpiry.DeadStockValue);
        Assert.Equal(1800m, dashboard.AgingAndExpiry.EstimatedAnnualHoldingCostExposure); // 10000 * 0.18

        Assert.NotNull(dashboard.SpaceCapacity);
        Assert.Equal(40.0m, dashboard.SpaceCapacity.TotalUsableCbm);
        Assert.Equal(35.0m, dashboard.SpaceCapacity.TotalOccupiedCbm);
        Assert.Equal(1, dashboard.SpaceCapacity.OverCapacityLocationsCount); // Loc 2 is over-capacity

        Assert.NotNull(dashboard.VelocityAndMovement);
        Assert.Equal(1, dashboard.VelocityAndMovement.FastMovingSkusCount);
        Assert.Equal(1, dashboard.VelocityAndMovement.MediumMovingSkusCount);
        Assert.Equal(1, dashboard.VelocityAndMovement.SlowMovingSkusCount);

        Assert.NotNull(dashboard.AbcPareto);
        Assert.Equal(1, dashboard.AbcPareto.ClassACount);
        Assert.Equal(1, dashboard.AbcPareto.ClassBCount);
        Assert.Equal(1, dashboard.AbcPareto.ClassCCount);
        Assert.Single(dashboard.AbcPareto.TopClassASkus);
        Assert.Equal("SKU-AAA-01", dashboard.AbcPareto.TopClassASkus[0].Sku);

        Assert.NotNull(dashboard.StockRiskAlerts);
        Assert.True(dashboard.StockRiskAlerts.TotalAlertsCount > 0);

        // 4. Freshness Metadata
        Assert.NotNull(dashboard.Freshness);
        Assert.NotNull(dashboard.Freshness.AgingSnapshotDateUtc);
        Assert.NotNull(dashboard.Freshness.SpaceSnapshotDateUtc);
        Assert.NotNull(dashboard.Freshness.VelocityCalculatedAtUtc);
        Assert.False(dashboard.Freshness.IsStale);

        // 5. Composite Health Score
        Assert.True(dashboard.OverallHealthScore >= 0m && dashboard.OverallHealthScore <= 100m);
        Assert.NotEmpty(dashboard.HealthScoreGrade);
    }

    /// <summary>
    /// Test 2: Composite health score applies all risk deductions deterministically.
    /// </summary>
    [Fact]
    public async Task GetUnifiedInventoryHealthDashboard_CompositeHealthScore_AppliesAllRiskDeductionsDeterministically()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var dashboard = await service.GetUnifiedInventoryHealthDashboardAsync(fix.FacilityId);

        // Dead stock ratio: 6.67% * 0.50 = 3.335
        // Expired ratio: (10000 / 150000) * 100 * 1.5 = 10.0
        // Overcapacity locations: 1 * 2.0 = 2.0
        // Space congestion: 87.5% <= 90%, so 0
        // Total deduction ~ 15.3
        // Health score ~ 84.7 (Grade B)
        Assert.True(dashboard.OverallHealthScore >= 80m && dashboard.OverallHealthScore < 90m);
        Assert.Equal("B (Good)", dashboard.HealthScoreGrade);
    }

    /// <summary>
    /// Test 3: Multi-tenant isolation: Client portal user view is strictly scoped to client's inventory,
    /// masks warehouse rack internals, and leaks zero data from another client.
    /// </summary>
    [Fact]
    public async Task GetUnifiedInventoryHealthDashboard_TenantIsolation_ClientViewStrictlyMasksOtherTenantData()
    {
        var fix = await SeedEnvironmentAsync();

        // Simulate client portal user for Client 1
        using var context = CreateContext(
            fix.DbName, tenantId: fix.Client1Id, isStaff: false, isSuperAdmin: false);
        var currentUser = new TestCurrentUserContext
        {
            TenantId = fix.Client1Id,
            UserType = UserType.Client,
            PermissionsList = [Permissions.Analytics.InventoryHealthView]
        };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var dashboard = await service.GetUnifiedInventoryHealthDashboardAsync(
            fix.FacilityId, clientId: fix.Client1Id);

        // Must reflect Client 1 only
        Assert.Equal(fix.Client1Id, dashboard.ClientId);
        Assert.Equal("CLI-ACME", dashboard.ClientCode);
        Assert.Equal("Acme Global", dashboard.ClientName);

        // Total valuation must be Client 1 items only ($50,000 + $40,000 + $10,000 = $100,000)
        // Must NOT include Client 2's $50,000!
        Assert.Equal(100000m, dashboard.TotalInventoryValuation);
        Assert.Equal(1250m, dashboard.TotalUnitsOnHand);
        Assert.Equal(3, dashboard.TotalActiveSkusCount); // 3 items, not 4

        // Space capacity for client portal user masks internal locations
        Assert.Equal(0, dashboard.SpaceCapacity.TotalLocationsEvaluated);
        Assert.Equal(0, dashboard.SpaceCapacity.OverCapacityLocationsCount);
        // Occupied CBM matches Client 1 aging snapshot CBM sum (5 + 10 + 15 = 30 CBM)
        Assert.Equal(30.0m, dashboard.SpaceCapacity.TotalOccupiedCbm);
    }

    /// <summary>
    /// Test 4: Facility scoping: A user restricted to Facility 1 cannot access Facility 2.
    /// </summary>
    [Fact]
    public async Task GetUnifiedInventoryHealthDashboard_FacilityScoping_EnforcesFacilityBoundary()
    {
        var fix = await SeedEnvironmentAsync();

        // User restricted to Facility 1
        using var context = CreateContext(
            fix.DbName, facilityId: fix.FacilityId, isStaff: true, isSuperAdmin: false);
        var currentUser = new TestCurrentUserContext
        {
            FacilityId = fix.FacilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Analytics.InventoryHealthView]
        };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        // Querying Facility 2 must throw UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(async () =>
        {
            await service.GetUnifiedInventoryHealthDashboardAsync(fix.Facility2Id);
        });
    }

    /// <summary>
    /// Test 5: Freshness metadata flags IsStale = true when snapshots are older than 24 hours.
    /// </summary>
    [Fact]
    public async Task GetUnifiedInventoryHealthDashboard_FreshnessMetadata_FlagsStaleWhenProjectionsOlderThan24Hours()
    {
        // Seed environment with snapshots from 48 hours ago
        var oldDate = DateTime.UtcNow.AddHours(-48);
        var fix = await SeedEnvironmentAsync(snapshotDateUtc: oldDate);

        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var dashboard = await service.GetUnifiedInventoryHealthDashboardAsync(
            fix.FacilityId, asOfDateUtc: oldDate);

        Assert.NotNull(dashboard.Freshness);
        Assert.True(dashboard.Freshness.IsStale);
        Assert.NotNull(dashboard.Freshness.StalenessReason);
        Assert.Contains("old (> 24h)", dashboard.Freshness.StalenessReason);
    }

    /// <summary>
    /// Test 6: Empty facility with zero inventory handles gracefully without division by zero.
    /// </summary>
    [Fact]
    public async Task GetUnifiedInventoryHealthDashboard_EmptyFacility_HandlesZeroInventoryWithoutDivideByZero()
    {
        var fix = new TestFixture();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);

        // Seed only facility
        await context.Facilities.AddAsync(new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "EMPTY-01",
            Name = "Empty Facility",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var dashboard = await service.GetUnifiedInventoryHealthDashboardAsync(fix.FacilityId);

        Assert.Equal(fix.FacilityId, dashboard.FacilityId);
        Assert.Equal(0m, dashboard.TotalInventoryValuation);
        Assert.Equal(0m, dashboard.TotalUnitsOnHand);
        Assert.Equal(0, dashboard.TotalActiveSkusCount);
        Assert.Equal(0m, dashboard.SpaceUtilizationPercent);
        Assert.Equal(0m, dashboard.DeadStockRatioPercent);
        Assert.Equal(0m, dashboard.OverallHealthScore);
        Assert.Equal("NoInventory", dashboard.HealthScoreGrade);
        Assert.Equal("NoInventory", dashboard.ScoreStatus);
        Assert.NotNull(dashboard.HealthScoreBreakdown);
        Assert.Equal("NoInventory", dashboard.HealthScoreBreakdown.ScoreStatus);
    }

    /// <summary>
    /// Test 7: Ledger Immutability: Dashboard queries are strictly advisory read plane
    /// and do not mutate StockOnHand, Locations, or MovementEventLogs.
    /// </summary>
    [Fact]
    public async Task GetUnifiedInventoryHealthDashboard_LedgerImmutability_DoesNotMutateStockOnHandOrLocations()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var service = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var initialStockCount = await context.StockOnHand.CountAsync();
        var initialStockQty = await context.StockOnHand.SumAsync(s => s.QtyAvailable);
        var initialLocationsCount = await context.Locations.CountAsync();

        // Execute dashboard queries multiple times
        await service.GetUnifiedInventoryHealthDashboardAsync(fix.FacilityId);
        await service.GetUnifiedInventoryHealthDashboardAsync(fix.FacilityId, fix.Client1Id);

        var postStockCount = await context.StockOnHand.CountAsync();
        var postStockQty = await context.StockOnHand.SumAsync(s => s.QtyAvailable);
        var postLocationsCount = await context.Locations.CountAsync();

        Assert.Equal(initialStockCount, postStockCount);
        Assert.Equal(initialStockQty, postStockQty);
        Assert.Equal(initialLocationsCount, postLocationsCount);
    }

    /// <summary>
    /// Test 8: WebAPI Controller Contract: Verifies endpoint routing, 200 OK result,
    /// and HasPermission attribute for Permissions.Analytics.InventoryHealthView.
    /// </summary>
    [Fact]
    public async Task InventoryHealthAnalyticsController_UnifiedDashboardEndpoint_ReturnsOkAndEnforcesPermissions()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var queryService = new AnalyticsQueryService(context, currentUser, NullLogger<AnalyticsQueryService>.Instance);

        var controller = new InventoryHealthAnalyticsController(queryService, currentUser);

        var actionResult = await controller.GetUnifiedInventoryHealthDashboard(
            fix.FacilityId, null, null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var dashboard = Assert.IsType<UnifiedInventoryHealthDashboardDto>(okResult.Value);

        Assert.Equal(fix.FacilityId, dashboard.FacilityId);
        Assert.Equal("ORD-01", dashboard.FacilityCode);

        // Verify [HasPermission(Permissions.Analytics.InventoryHealthView)] attribute
        var method = typeof(InventoryHealthAnalyticsController)
            .GetMethod(nameof(InventoryHealthAnalyticsController.GetUnifiedInventoryHealthDashboard));

        Assert.NotNull(method);
        var permissionAttr = method.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permissionAttr);
        Assert.Equal(Permissions.Analytics.InventoryHealthView, permissionAttr.Policy);
    }
}
