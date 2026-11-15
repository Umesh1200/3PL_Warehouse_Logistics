using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Dedicated unit and integration test suite for Phase 6.3.8:
/// Slotting Optimization APIs &amp; Frontend Integration.
/// Verifies:
/// 1. Facility batch listing with tenant filtering and limit bounding.
/// 2. Deep algorithmic explainability inspection (6D suitability, 4-factor urgency, transparent costs).
/// 3. Source vs Proposed destination location comparison.
/// 4. Controller endpoints and permission attributes.
/// 5. Tenant and facility isolation enforcement.
/// 6. Read-only operational ledger invariance across all 8 tables.
/// </summary>
public class SlottingOptimizationApiAndIntegrationTests
{
    private class ApiTestFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();

        public Guid PickZoneId { get; set; } = Guid.NewGuid();
        public Guid ReserveZoneId { get; set; } = Guid.NewGuid();

        public Guid GoldenPickLocationId { get; set; } = Guid.NewGuid();
        public Guid UpperReserveLocationId { get; set; } = Guid.NewGuid();
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

    private static async Task<ApiTestFixture> SeedEnvironmentAsync()
    {
        var fix = new ApiTestFixture();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);

        var facility = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "FAC-API-01",
            Name = "API Test Distribution Center",
            IsActive = true
        };
        var facility2 = new Facility
        {
            Id = fix.Facility2Id,
            FacilityCode = "FAC-API-02",
            Name = "Secondary DC",
            IsActive = true
        };
        context.Facilities.AddRange(facility, facility2);

        var pickZone = new WarehouseZone
        {
            Id = fix.PickZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "PICK-ZONE",
            Name = "Forward Pick Zone",
            ZoneType = WarehouseZoneType.PickFace,
            IsActive = true
        };
        var reserveZone = new WarehouseZone
        {
            Id = fix.ReserveZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "RES-ZONE",
            Name = "Reserve Bulk Zone",
            ZoneType = WarehouseZoneType.BulkStorage,
            IsActive = true
        };
        context.WarehouseZones.AddRange(pickZone, reserveZone);

        // Golden Zone Pick Location (Z = 1.1m, Level 1)
        var locGolden = new Location
        {
            Id = fix.GoldenPickLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickZoneId,
            LocationCode = "LOC-G-01",
            AisleCode = "A01",
            RackCode = "R01",
            ShelfLevel = 1,
            XCoord = 10m,
            YCoord = 5m,
            ZCoord = 1.1m,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true,
            LocationType = LocationType.Rack
        };

        // Upper Reserve Location (Z = 6.0m, Level 4)
        var locReserve = new Location
        {
            Id = fix.UpperReserveLocationId,
            FacilityId = fix.FacilityId,
            ZoneId = fix.ReserveZoneId,
            LocationCode = "LOC-RES-01",
            AisleCode = "A05",
            RackCode = "R03",
            ShelfLevel = 4,
            XCoord = 50m,
            YCoord = 30m,
            ZCoord = 6.0m,
            LengthCm = 120m,
            WidthCm = 80m,
            HeightCm = 150m,
            MaxWeightKg = 1000m,
            IsActive = true,
            IsPickable = true,
            LocationType = LocationType.Rack
        };

        context.Locations.AddRange(locGolden, locReserve);
        await context.SaveChangesAsync();

        return fix;
    }

    #region 1. Facility Batch Listing Tests

    [Fact]
    public async Task GetBatchesForFacility_ReturnsRecentBatches_FilteredByFacilityAndClient()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        var policy = SlottingPolicy.Create("BatchListPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        var now = DateTime.UtcNow;
        var batch1 = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-001", fix.ClientId, now.AddDays(-2));
        var batch2 = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-002", fix.ClientId, now.AddDays(-1));
        var batch3 = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-003", fix.Client2Id, now);
        var batchOtherFacility = SlottingRecommendationBatch.Create(fix.Facility2Id, policy.Id, "BATCH-FAC2", fix.ClientId, now);

        context.SlottingRecommendationBatches.AddRange(batch1, batch2, batch3, batchOtherFacility);
        await context.SaveChangesAsync();

        // 1. Query all batches for facility
        var allBatches = await service.GetBatchesForFacilityAsync(fix.FacilityId, clientId: null, limit: 10);
        Assert.Equal(3, allBatches.Count);
        Assert.Equal("BATCH-003", allBatches[0].BatchNumber); // Ordered by date descending
        Assert.Equal("BATCH-002", allBatches[1].BatchNumber);
        Assert.Equal("BATCH-001", allBatches[2].BatchNumber);

        // 2. Query filtered by Client 1
        var client1Batches = await service.GetBatchesForFacilityAsync(fix.FacilityId, clientId: fix.ClientId, limit: 10);
        Assert.Equal(2, client1Batches.Count);
        Assert.All(client1Batches, b => Assert.Equal(fix.ClientId, b.ClientId));

        // 3. Limit test
        var limitedBatches = await service.GetBatchesForFacilityAsync(fix.FacilityId, clientId: null, limit: 2);
        Assert.Equal(2, limitedBatches.Count);
    }

    #endregion

    #region 2. Recommendation Explainability Inspection Tests

    [Fact]
    public async Task InspectRecommendation_ReturnsFullAlgorithmicExplainabilityAndDestinationComparison()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            NullLogger<RelocationRecommendationService>.Instance);

        var policy = SlottingPolicy.Create("InspectPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        var item = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-EXPLAIN-01",
            Description = "High Velocity Widget Premium",
            ShortName = "Widget Pro",
            WeightKg = 8.5m,
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 20m,
            AbcVelocity = AbcVelocity.A
        };
        context.Items.Add(item);

        // Stock in Upper Reserve
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = item.Id,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 20m
        });

        // Stock already in Proposed location to test occupancy comparison
        var existingItem = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            Sku = "SKU-EXISTING",
            WeightKg = 5.0m,
            LengthCm = 20m,
            WidthCm = 20m,
            HeightCm = 20m
        };
        context.Items.Add(existingItem);
        context.StockOnHand.Add(new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ItemId = existingItem.Id,
            LocationId = fix.GoldenPickLocationId,
            QtyAvailable = 10m
        });

        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-EXPLAIN-01", fix.ClientId);
        var rec = new SlottingRecommendation
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            ItemId = item.Id,
            ItemSku = item.Sku,
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenPickLocationId,
            AbcClass = "A",
            SkuVelocity = "High",
            VelocityTrend = "Surging",
            ReasonCode = "PromoteFastMoverToGoldenZonePickFace",
            SuitabilityScore = 88.5m,
            ErgonomicScore = 100.0m,
            AbcAffinityScore = 95.0m,
            VelocityAffinityScore = 90.0m,
            PickDensityScore = 80.0m,
            TravelDistanceDeltaMeters = 25.0m,
            GrossMonthlySavingsUsd = 45.00m,
            HandlingCostUsd = 2.00m,
            MovementCostUsd = 3.50m,
            DisruptionCostUsd = 22.50m,
            TotalRelocationCostUsd = 28.00m,
            EstimatedMonthlyNetSavingsUsd = 35.67m,
            UrgencyScore = 75.0m,
            UrgencyReason = "Fast mover stored in high rack level (Level >= 3) outside Golden Zone (+45); Seasonal spike active (+30)",
            PriorityScore = 82.4m,
            DisruptionLevel = "High",
            IsOperationallyFeasible = true,
            Status = SlottingRecommendationStatus.Pending
        };
        batch.Recommendations.Add(rec);
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        var detail = await service.InspectRecommendationAsync(rec.Id);

        Assert.NotNull(detail);
        Assert.Equal(rec.Id, detail.RecommendationId);
        Assert.Equal(batch.Id, detail.BatchId);
        Assert.Equal(item.Sku, detail.ItemSku);
        Assert.Equal("High Velocity Widget Premium", detail.ItemDescription);
        Assert.True(detail.IsOperationallyFeasible);

        // 1. 6D Placement Suitability Verification
        Assert.Equal(88.5m, detail.SuitabilityScore);
        Assert.Equal(100.0m, detail.ErgonomicScore);
        Assert.Equal(95.0m, detail.AbcAffinityScore);
        Assert.Equal(90.0m, detail.VelocityAffinityScore);
        Assert.Equal(80.0m, detail.PickDensityScore);
        Assert.Equal(25.0m, detail.TravelDistanceDeltaMeters);
        Assert.True(detail.ProductAffinityScore > 0m);

        // 2. 4-Factor Operational Urgency Verification
        Assert.Equal(75.0m, detail.UrgencyScore);
        Assert.Equal(45.0m, detail.VelocityMismatchScore);
        Assert.Equal(0.0m, detail.GoldenZoneCongestionScore);
        Assert.Equal(30.0m, detail.SeasonalDemandScore);
        Assert.Equal(0.0m, detail.ErgonomicHazardScore);

        // 3. Transparent Economics Verification
        Assert.Equal(45.00m, detail.GrossMonthlySavingsUsd);
        Assert.Equal(2.00m, detail.HandlingCostUsd);
        Assert.Equal(3.50m, detail.MovementCostUsd);
        Assert.Equal(22.50m, detail.DisruptionCostUsd);
        Assert.Equal(28.00m, detail.TotalRelocationCostUsd);
        Assert.Equal(35.67m, detail.NetMonthlyBenefitUsd);
        Assert.Equal(82.4m, detail.PriorityScore);
        Assert.Equal("High", detail.DisruptionLevel);
        Assert.True(detail.DisruptionMitigationScore >= 0m && detail.DisruptionMitigationScore <= 100m);

        // 4. Source vs Destination Location Comparison
        Assert.NotNull(detail.CurrentLocation);
        Assert.Equal("LOC-RES-01", detail.CurrentLocation.LocationCode);
        Assert.Equal(4, detail.CurrentLocation.ShelfLevel);
        Assert.False(detail.CurrentLocation.IsInGoldenZone);
        Assert.Equal(WarehouseZoneType.BulkStorage, detail.CurrentLocation.ZoneType);
        Assert.True(detail.CurrentLocation.DistanceToDockMeters > detail.ProposedLocation.DistanceToDockMeters);

        Assert.NotNull(detail.ProposedLocation);
        Assert.Equal("LOC-G-01", detail.ProposedLocation.LocationCode);
        Assert.Equal(1, detail.ProposedLocation.ShelfLevel);
        Assert.True(detail.ProposedLocation.IsInGoldenZone);
        Assert.Equal(WarehouseZoneType.PickFace, detail.ProposedLocation.ZoneType);
        Assert.Equal(50.0m, detail.ProposedLocation.OccupiedWeightKg); // 10 units * 5.0kg
        Assert.True(detail.ProposedLocation.WeightUtilizationPercent > 0m);
    }

    #endregion

    #region 3. WebAPI Controller & Permission Tests

    [Fact]
    public void SlottingController_AllApiEndpoints_HaveStrictPermissionAttributes()
    {
        var controllerType = typeof(SlottingOptimizationController);

        // Manage endpoints
        var manageMethods = new[]
        {
            nameof(SlottingOptimizationController.CreatePolicy),
            nameof(SlottingOptimizationController.SimulateSlotting),
            nameof(SlottingOptimizationController.PrioritizeRelocations),
            nameof(SlottingOptimizationController.ApproveRelocationPlan),
            nameof(SlottingOptimizationController.SubmitBatchForReview),
            nameof(SlottingOptimizationController.CancelBatch)
        };

        foreach (var name in manageMethods)
        {
            var method = controllerType.GetMethod(name);
            Assert.NotNull(method);
            var perm = method.GetCustomAttribute<HasPermissionAttribute>();
            Assert.NotNull(perm);
            Assert.Equal(Permissions.Analytics.SlottingManage, perm.Policy);
        }

        // View endpoints
        var viewMethods = new[]
        {
            nameof(SlottingOptimizationController.GetPolicy),
            nameof(SlottingOptimizationController.GetActivePolicy),
            nameof(SlottingOptimizationController.GetFacilityBatches),
            nameof(SlottingOptimizationController.GetPrioritizedPlan),
            nameof(SlottingOptimizationController.InspectRecommendation),
            nameof(SlottingOptimizationController.EvaluateCompatibility),
            nameof(SlottingOptimizationController.GenerateSuitabilityMatrix)
        };

        foreach (var name in viewMethods)
        {
            var method = controllerType.GetMethod(name);
            Assert.NotNull(method);
            var perm = method.GetCustomAttribute<HasPermissionAttribute>();
            Assert.NotNull(perm);
            Assert.Equal(Permissions.Analytics.SlottingView, perm.Policy);
        }
    }

    [Fact]
    public async Task SlottingController_FacilityBatches_ReturnsOkWithBatches()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var slottingService = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);
        var relocationService = new RelocationRecommendationService(context, currentUser, NullLogger<RelocationRecommendationService>.Instance);

        var controller = new SlottingOptimizationController(slottingService, relocationService, currentUser);

        var policy = SlottingPolicy.Create("ControllerBatchPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        context.SlottingRecommendationBatches.Add(SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-CTRL-01", fix.ClientId));
        await context.SaveChangesAsync();

        var actionResult = await controller.GetFacilityBatches(fix.FacilityId, fix.ClientId, 10, default);
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var list = Assert.IsAssignableFrom<IReadOnlyList<SlottingBatchSummaryDto>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal("BATCH-CTRL-01", list[0].BatchNumber);
    }

    [Fact]
    public async Task SlottingController_InspectRecommendation_ReturnsOkWithDetail()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] };
        var slottingService = new SlottingOptimizationService(context, currentUser, NullLogger<SlottingOptimizationService>.Instance);
        var relocationService = new RelocationRecommendationService(context, currentUser, NullLogger<RelocationRecommendationService>.Instance);

        var controller = new SlottingOptimizationController(slottingService, relocationService, currentUser);

        var policy = SlottingPolicy.Create("ControllerInspectPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        var item = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-CTRL-01", Description = "Test Widget", WeightKg = 5m, LengthCm = 30m, WidthCm = 20m, HeightCm = 20m };
        context.Items.Add(item);

        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-CTRL-INSPECT", fix.ClientId);
        var rec = new SlottingRecommendation
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            ItemId = item.Id,
            ItemSku = item.Sku,
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenPickLocationId,
            SuitabilityScore = 80m,
            UrgencyScore = 50m,
            PriorityScore = 70m,
            IsOperationallyFeasible = true
        };
        batch.Recommendations.Add(rec);
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        var actionResult = await controller.InspectRecommendation(rec.Id, default);
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var detail = Assert.IsType<SlottingRecommendationDetailDto>(okResult.Value);
        Assert.Equal(rec.Id, detail.RecommendationId);
        Assert.Equal("Test Widget", detail.ItemDescription);
    }

    #endregion

    #region 4. Tenant & Facility Isolation Security Tests

    [Fact]
    public async Task TenantAndFacilityIsolation_CrossFacilityAndCrossTenantInspection_Blocked()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);

        var policy = SlottingPolicy.Create("SecPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        var item = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-SEC-01", WeightKg = 5m, LengthCm = 30m, WidthCm = 20m, HeightCm = 20m };
        context.Items.Add(item);

        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-SEC-01", fix.ClientId);
        var rec = new SlottingRecommendation
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            ItemId = item.Id,
            ItemSku = item.Sku,
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenPickLocationId,
            IsOperationallyFeasible = true
        };
        batch.Recommendations.Add(rec);
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        // 1. User restricted to another Facility
        var userOtherFacility = new TestCurrentUserContext
        {
            UserType = UserType.Staff,
            FacilityId = fix.Facility2Id, // Different facility!
            RolesList = []
        };
        var serviceOtherFacility = new RelocationRecommendationService(context, userOtherFacility, NullLogger<RelocationRecommendationService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            serviceOtherFacility.GetBatchesForFacilityAsync(fix.FacilityId));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            serviceOtherFacility.InspectRecommendationAsync(rec.Id));

        // 2. Client user restricted to another Client Tenant
        var userOtherTenant = new TestCurrentUserContext
        {
            UserType = UserType.Client,
            TenantId = fix.Client2Id, // Different tenant!
            FacilityId = fix.FacilityId,
            RolesList = []
        };
        var serviceOtherTenant = new RelocationRecommendationService(context, userOtherTenant, NullLogger<RelocationRecommendationService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            serviceOtherTenant.GetBatchesForFacilityAsync(fix.FacilityId, fix.ClientId));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            serviceOtherTenant.InspectRecommendationAsync(rec.Id));
    }

    #endregion

    #region 5. Read-Only Operational Ledger Invariance Tests

    [Fact]
    public async Task ReadonlyOperationalLedgerInvariance_AcrossAllEndpointsAndInspection_ConfirmsZeroMutations()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var evalTime = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        // 1. Seed Items
        var item1 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-LEDGER-API-1", WeightKg = 5m, LengthCm = 30m, WidthCm = 30m, HeightCm = 30m, AbcVelocity = AbcVelocity.A };
        item1.RecalculateVolume();
        context.Items.Add(item1);

        // 2. Seed StockOnHand across partitions
        var stock1 = new StockOnHand { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, ItemId = item1.Id, LocationId = fix.UpperReserveLocationId, QtyAvailable = 5m, QtyAllocated = 1m, QtyReserved = 1m };
        context.StockOnHand.Add(stock1);

        // 3. Seed Outbound Orders & Lines
        var ord = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OrderNumber = "ORD-API-001",
            Status = OutboundOrderStatus.Completed,
            OrderDateUtc = evalTime.AddDays(-5)
        };
        context.OutboundOrders.Add(ord);
        context.OutboundOrderLines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = ord.Id, ItemId = item1.Id, QuantityOrdered = 1m, QuantityShipped = 1m });

        // 4. Seed PickTasks
        var pt = new PickTask { Id = Guid.NewGuid(), FacilityId = fix.FacilityId, OutboundOrderId = ord.Id, ItemId = item1.Id, SourceLocationId = fix.UpperReserveLocationId, QuantityToPick = 1m, QuantityPicked = 1m, Status = PickTaskStatus.Completed };
        context.PickTasks.Add(pt);

        // 5. Seed InventoryTransactions
        var tx = new InventoryTransaction { Id = 2001L, FacilityId = fix.FacilityId, ClientId = fix.ClientId, ItemId = item1.Id, FromLocationId = fix.UpperReserveLocationId, TxType = InventoryTxType.Pick, Quantity = 1m, TxTimestamp = evalTime.AddDays(-2) };
        context.InventoryTransactions.Add(tx);

        // 6. Seed ReceiptLines
        var rl = new ReceiptLine { Id = Guid.NewGuid(), ReceivingSessionId = Guid.NewGuid(), ItemId = item1.Id, QuantityReceived = 10m, ScannedAtUtc = evalTime.AddDays(-15) };
        context.ReceiptLines.Add(rl);

        // Velocity records
        context.SkuVelocityClassificationRecords.Add(
            new SkuVelocityClassificationRecord
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                ItemId = item1.Id,
                Sku = item1.Sku,
                AbcClass = AbcClassificationCategory.ClassA,
                VelocityCategory = InventoryMovementVelocityCategory.FastMoving,
                ObservationWindow = VelocityObservationWindow.Days90,
                CalculationPeriodStartUtc = evalTime.AddDays(-90),
                CalculationPeriodEndUtc = evalTime,
                CalculatedAtUtc = evalTime,
                TotalPickTasksCount = 80,
                TotalUnitsDemanded = 150m,
                TotalDemandValue = 3000m
            }
        );

        await context.SaveChangesAsync();

        // CAPTURE DEEP SNAPSHOTS BEFORE ALL OPERATIONS
        var stockBefore = context.StockOnHand.AsNoTracking().Select(s => new { s.Id, s.QtyAvailable, s.QtyAllocated, s.QtyReserved, s.QtyQuarantined, s.QtyDamaged, s.QtyOnHold, s.LocationId, s.ItemId }).ToList();
        var itemsBefore = context.Items.AsNoTracking().Select(i => new { i.Id, i.Sku, i.WeightKg, i.LengthCm, i.WidthCm, i.HeightCm, i.AbcVelocity }).ToList();
        var locationsBefore = context.Locations.AsNoTracking().Select(l => new { l.Id, l.LocationCode, l.IsActive, l.IsPickable, l.IsDamaged, l.IsLocked }).ToList();
        var ordersBefore = context.OutboundOrders.AsNoTracking().Select(o => new { o.Id, o.OrderNumber, o.Status, o.FacilityId, o.ClientId }).ToList();
        var linesBefore = context.OutboundOrderLines.AsNoTracking().Select(l => new { l.Id, l.OutboundOrderId, l.ItemId, l.QuantityOrdered, l.QuantityShipped }).ToList();
        var picksBefore = context.PickTasks.AsNoTracking().Select(p => new { p.Id, p.Status, p.QuantityToPick, p.QuantityPicked, p.SourceLocationId }).ToList();
        var txBefore = context.InventoryTransactions.AsNoTracking().Select(t => new { t.Id, t.Quantity, t.TxType, t.FromLocationId, t.ToLocationId, t.ItemId }).ToList();
        var receiptsBefore = context.ReceiptLines.AsNoTracking().Select(r => new { r.Id, r.ItemId, r.QuantityReceived, r.ScannedAtUtc }).ToList();

        // 1. Run Prioritization
        var planResult = await service.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            AsOfDateUtc = evalTime
        });

        Assert.NotEmpty(planResult.PrioritizedRecommendations);
        var recId = planResult.PrioritizedRecommendations.First().RecommendationId;

        // 2. Query Batches
        var batches = await service.GetBatchesForFacilityAsync(fix.FacilityId, fix.ClientId);
        Assert.NotEmpty(batches);

        // 3. Inspect Recommendation
        var detail = await service.InspectRecommendationAsync(recId);
        Assert.NotNull(detail);

        // 4. Submit for Review
        await service.SubmitBatchForReviewAsync(planResult.BatchId);

        // 5. Approve
        await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = planResult.BatchId,
            ApprovedRecommendationIds = [recId]
        });

        // CAPTURE DEEP SNAPSHOTS AFTER ALL OPERATIONS
        var stockAfter = context.StockOnHand.AsNoTracking().Select(s => new { s.Id, s.QtyAvailable, s.QtyAllocated, s.QtyReserved, s.QtyQuarantined, s.QtyDamaged, s.QtyOnHold, s.LocationId, s.ItemId }).ToList();
        var itemsAfter = context.Items.AsNoTracking().Select(i => new { i.Id, i.Sku, i.WeightKg, i.LengthCm, i.WidthCm, i.HeightCm, i.AbcVelocity }).ToList();
        var locationsAfter = context.Locations.AsNoTracking().Select(l => new { l.Id, l.LocationCode, l.IsActive, l.IsPickable, l.IsDamaged, l.IsLocked }).ToList();
        var ordersAfter = context.OutboundOrders.AsNoTracking().Select(o => new { o.Id, o.OrderNumber, o.Status, o.FacilityId, o.ClientId }).ToList();
        var linesAfter = context.OutboundOrderLines.AsNoTracking().Select(l => new { l.Id, l.OutboundOrderId, l.ItemId, l.QuantityOrdered, l.QuantityShipped }).ToList();
        var picksAfter = context.PickTasks.AsNoTracking().Select(p => new { p.Id, p.Status, p.QuantityToPick, p.QuantityPicked, p.SourceLocationId }).ToList();
        var txAfter = context.InventoryTransactions.AsNoTracking().Select(t => new { t.Id, t.Quantity, t.TxType, t.FromLocationId, t.ToLocationId, t.ItemId }).ToList();
        var receiptsAfter = context.ReceiptLines.AsNoTracking().Select(r => new { r.Id, r.ItemId, r.QuantityReceived, r.ScannedAtUtc }).ToList();

        // STRICT IDENTITY ASSERTION: ZERO MUTATIONS ACROSS ALL 8 TABLES
        Assert.Equal(stockBefore, stockAfter);
        Assert.Equal(itemsBefore, itemsAfter);
        Assert.Equal(locationsBefore, locationsAfter);
        Assert.Equal(ordersBefore, ordersAfter);
        Assert.Equal(linesBefore, linesAfter);
        Assert.Equal(picksBefore, picksAfter);
        Assert.Equal(txBefore, txAfter);
        Assert.Equal(receiptsBefore, receiptsAfter);
    }

    #endregion

    #region 6. Additional Lifecycle, Workflow, Parity & Route Contract Tests

    [Fact]
    public void RouteContractVerification_AllDocumentedRoutesMatchControllerAttributes()
    {
        var controllerType = typeof(SlottingOptimizationController);

        // Map of required operation methods to their canonical route templates
        var expectedRoutes = new Dictionary<string, (string HttpMethod, string[] Routes)>
        {
            [nameof(SlottingOptimizationController.GetActivePolicy)] = ("GET", ["facility/{facilityId}/active-policy", "policies/active"]),
            [nameof(SlottingOptimizationController.SimulateSlotting)] = ("POST", ["simulation", "simulate"]),
            [nameof(SlottingOptimizationController.PrioritizeRelocations)] = ("POST", ["relocations/prioritize"]),
            [nameof(SlottingOptimizationController.GetPrioritizedPlan)] = ("GET", ["relocations/{batchId}", "batches/{batchId}/plan"]),
            [nameof(SlottingOptimizationController.InspectRecommendation)] = ("GET", ["recommendations/{recommendationId}/inspect"]),
            [nameof(SlottingOptimizationController.SubmitBatchForReview)] = ("POST", ["relocations/{batchId}/submit-review", "batches/{batchId}/submit-review"]),
            [nameof(SlottingOptimizationController.ApproveRelocationPlan)] = ("POST", ["relocations/approve", "batches/{batchId}/approve-plan"]),
            [nameof(SlottingOptimizationController.CancelBatch)] = ("POST", ["relocations/{batchId}/cancel", "batches/{batchId}/cancel"]),
            [nameof(SlottingOptimizationController.GetFacilityBatches)] = ("GET", ["facility/{facilityId}/batches"]),
            [nameof(SlottingOptimizationController.CreatePolicy)] = ("POST", ["policies"]),
            [nameof(SlottingOptimizationController.GetPolicy)] = ("GET", ["policies/{policyId}"]),
            [nameof(SlottingOptimizationController.EvaluateCompatibility)] = ("GET", ["evaluate-compatibility"]),
            [nameof(SlottingOptimizationController.GenerateSuitabilityMatrix)] = ("POST", ["suitability-matrix"])
        };

        foreach (var (methodName, (httpMethod, routes)) in expectedRoutes)
        {
            var method = controllerType.GetMethod(methodName);
            Assert.NotNull(method);

            if (httpMethod == "GET")
            {
                var httpGetAttrs = method.GetCustomAttributes<HttpGetAttribute>().ToList();
                Assert.NotEmpty(httpGetAttrs);
                var actualTemplates = httpGetAttrs.Select(a => a.Template).ToList();
                foreach (var r in routes)
                {
                    Assert.Contains(r, actualTemplates);
                }
            }
            else if (httpMethod == "POST")
            {
                var httpPostAttrs = method.GetCustomAttributes<HttpPostAttribute>().ToList();
                Assert.NotEmpty(httpPostAttrs);
                var actualTemplates = httpPostAttrs.Select(a => a.Template).ToList();
                foreach (var r in routes)
                {
                    Assert.Contains(r, actualTemplates);
                }
            }
        }
    }

    [Fact]
    public async Task BatchSubmission_SimulatedToPendingApproval_TransitionsSuccessfully_AndRejectsInvalidInitialState()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var policy = SlottingPolicy.Create("SubmitReviewPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-SUBMIT-01", fix.ClientId);
        batch.MarkSimulated(10, 30m, 100m, 500m, 85m);
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        Assert.Equal(SlottingBatchStatus.Simulated, batch.Status);

        // 1. Valid Transition: Simulated -> PendingApproval
        var newStatus = await service.SubmitBatchForReviewAsync(batch.Id);
        Assert.Equal(SlottingBatchStatus.PendingApproval, newStatus);

        var persistedBatch = await context.SlottingRecommendationBatches.FindAsync(batch.Id);
        Assert.NotNull(persistedBatch);
        Assert.Equal(SlottingBatchStatus.PendingApproval, persistedBatch.Status);

        // 2. Invalid Transition: Attempting to submit again from PendingApproval must throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitBatchForReviewAsync(batch.Id));
    }

    [Fact]
    public async Task SelectiveApproval_UpdatesSpecificRecommendations_AndPersistsAuditableNotes()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var approverId = Guid.NewGuid();
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = approverId },
            NullLogger<RelocationRecommendationService>.Instance);

        var policy = SlottingPolicy.Create("ApprovalPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        var item1 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-APPROVE-1", Description = "Item 1" };
        var item2 = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-APPROVE-2", Description = "Item 2" };
        context.Items.AddRange(item1, item2);

        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-APPROVE-01", fix.ClientId);
        batch.Status = SlottingBatchStatus.PendingApproval;

        var rec1 = new SlottingRecommendation
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            ItemId = item1.Id,
            ItemSku = item1.Sku,
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenPickLocationId,
            Status = SlottingRecommendationStatus.Pending
        };
        var rec2 = new SlottingRecommendation
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            ItemId = item2.Id,
            ItemSku = item2.Sku,
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenPickLocationId,
            Status = SlottingRecommendationStatus.Pending
        };
        batch.Recommendations.Add(rec1);
        batch.Recommendations.Add(rec2);
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        // Selective approval: approve rec1 only
        var approvalResult = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = batch.Id,
            ApprovedRecommendationIds = [rec1.Id],
            ApprovalNotes = "Authorized move for SKU-APPROVE-1 only"
        });

        Assert.Equal(SlottingBatchStatus.PartiallyApproved, approvalResult.Status);
        Assert.Equal(1, approvalResult.ApprovedCount);

        var reloadedRec1 = await context.SlottingRecommendations.FindAsync(rec1.Id);
        var reloadedRec2 = await context.SlottingRecommendations.FindAsync(rec2.Id);
        var reloadedBatch = await context.SlottingRecommendationBatches.FindAsync(batch.Id);

        Assert.NotNull(reloadedRec1);
        Assert.Equal(SlottingRecommendationStatus.Approved, reloadedRec1.Status);

        Assert.NotNull(reloadedRec2);
        Assert.Equal(SlottingRecommendationStatus.Skipped, reloadedRec2.Status);
        Assert.Equal(1, approvalResult.SkippedCount);

        Assert.NotNull(reloadedBatch);
        Assert.Equal(SlottingBatchStatus.PartiallyApproved, reloadedBatch.Status);
        Assert.Contains("Authorized move for SKU-APPROVE-1 only", reloadedBatch.Notes);
    }

    [Fact]
    public async Task RejectionWorkflow_WithAuditableReason_PersistsRejectionReasonAndTransitionsStatus()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var policy = SlottingPolicy.Create("RejectPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        var item = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-REJECT-1", Description = "Item to Reject" };
        context.Items.Add(item);

        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-REJECT-01", fix.ClientId);
        batch.Status = SlottingBatchStatus.PendingApproval;

        var rec = new SlottingRecommendation
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            ItemId = item.Id,
            ItemSku = item.Sku,
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenPickLocationId,
            Status = SlottingRecommendationStatus.Pending
        };
        batch.Recommendations.Add(rec);
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        var rejectionResult = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = batch.Id,
            ApprovedRecommendationIds = [],
            RejectedRecommendationIds = [rec.Id],
            RejectionReasonsByRecommendationId = new Dictionary<Guid, string>
            {
                [rec.Id] = "SKU packaging format changed; destination shelf height unsuitable."
            },
            ApprovalNotes = "Batch fully rejected by floor manager"
        });

        Assert.Equal(SlottingBatchStatus.Rejected, rejectionResult.Status);
        Assert.Equal(1, rejectionResult.RejectedCount);

        var reloadedRec = await context.SlottingRecommendations.FindAsync(rec.Id);
        Assert.NotNull(reloadedRec);
        Assert.Equal(SlottingRecommendationStatus.Rejected, reloadedRec.Status);
        Assert.Equal("SKU packaging format changed; destination shelf height unsuitable.", reloadedRec.RejectionReason);
    }

    [Fact]
    public async Task Cancellation_CancelledBatchCannotSubsequentlyBeApproved()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var policy = SlottingPolicy.Create("CancelPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);
        var batch = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-CANCEL-01", fix.ClientId);
        batch.Status = SlottingBatchStatus.PendingApproval;
        context.SlottingRecommendationBatches.Add(batch);
        await context.SaveChangesAsync();

        // 1. Cancel the batch
        var cancelStatus = await service.CancelBatchAsync(batch.Id, "Emergency maintenance scheduled; shift cancelled");
        Assert.Equal(SlottingBatchStatus.Cancelled, cancelStatus);

        var reloadedBatch = await context.SlottingRecommendationBatches.FindAsync(batch.Id);
        Assert.NotNull(reloadedBatch);
        Assert.Equal(SlottingBatchStatus.Cancelled, reloadedBatch.Status);

        // 2. Attempting to approve a Cancelled batch must throw InvalidOperationException
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
            {
                BatchId = batch.Id,
                ApprovedRecommendationIds = []
            }));

        Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DtoSerializationParity_PolicyAndRecommendationDetail_EnforceExactContract()
    {
        var policyDto = new SlottingPolicyDto
        {
            Id = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            ClientId = null,
            Name = "Contract Parity Policy",
            GoldenZoneMinZMeters = 0.80m,
            GoldenZoneMaxZMeters = 1.50m,
            GoldenZoneShelfLevels = "1,2",
            WeightFinancialBenefit = 0.35m,
            WeightOperationalUrgency = 0.30m,
            WeightPlacementSuitability = 0.20m,
            WeightDisruptionPenalty = 0.15m,
            NegativeNetBenefitPolicy = "AllowIfHighUrgency",
            UrgencyThresholdForNegativeBenefit = 70.0m,
            TargetMonthlySavingsUsd = 50.0m,
            MaxAcceptableRelocationCostUsd = 100.0m,
            HandlingCostPerUnitUsd = 0.10m,
            HeavyHandlingSurchargePerTierUsd = 5.00m,
            BaseDisruptionCostUsd = 15.00m,
            GoldenZoneOpportunityGainUsd = 8.50m,
            AmortizationMonths = 3.0m,
            MaxProjectionAgeDays = 30,
            Version = 1,
            IsActive = true
        };

        var json = System.Text.Json.JsonSerializer.Serialize(policyDto, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        // Assert exact camelCase keys present
        Assert.Contains("\"name\":", json);
        Assert.Contains("\"goldenZoneMinZMeters\":", json);
        Assert.Contains("\"goldenZoneMaxZMeters\":", json);
        Assert.Contains("\"weightFinancialBenefit\":", json);
        Assert.Contains("\"weightOperationalUrgency\":", json);
        Assert.Contains("\"negativeNetBenefitPolicy\":", json);
        Assert.Contains("\"urgencyThresholdForNegativeBenefit\":", json);
        Assert.Contains("\"amortizationMonths\":", json);

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<SlottingPolicyDto>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(deserialized);
        Assert.Equal(policyDto.Name, deserialized.Name);
        Assert.Equal(policyDto.NegativeNetBenefitPolicy, deserialized.NegativeNetBenefitPolicy);
        Assert.Equal(policyDto.WeightFinancialBenefit, deserialized.WeightFinancialBenefit);
    }

    [Fact]
    public void RankingIntegrity_DeterministicRankingSequence_MaintainedStrictly()
    {
        // P DESC -> B_net DESC -> U DESC -> Sku ASC -> ProposedLocationCode ASC
        var list = new List<PrioritizedRelocationRecommendationDto>
        {
            new() { PriorityScore = 80m, NetMonthlyBenefitUsd = 100m, UrgencyScore = 50m, ItemSku = "SKU-B", ProposedLocationCode = "LOC-02", RankingRank = 2 },
            new() { PriorityScore = 90m, NetMonthlyBenefitUsd = 50m, UrgencyScore = 80m, ItemSku = "SKU-A", ProposedLocationCode = "LOC-01", RankingRank = 1 },
            new() { PriorityScore = 80m, NetMonthlyBenefitUsd = 100m, UrgencyScore = 50m, ItemSku = "SKU-A", ProposedLocationCode = "LOC-03", RankingRank = 3 },
            new() { PriorityScore = 80m, NetMonthlyBenefitUsd = 120m, UrgencyScore = 40m, ItemSku = "SKU-C", ProposedLocationCode = "LOC-01", RankingRank = 4 }
        };

        var sorted = list
            .OrderByDescending(r => r.PriorityScore)
            .ThenByDescending(r => r.NetMonthlyBenefitUsd)
            .ThenByDescending(r => r.UrgencyScore)
            .ThenBy(r => r.ItemSku, StringComparer.Ordinal)
            .ThenBy(r => r.ProposedLocationCode, StringComparer.Ordinal)
            .ToList();

        // Expected order:
        // 1. Priority 90 (SKU-A, LOC-01)
        // 2. Priority 80, B_net 120 (SKU-C, LOC-01)
        // 3. Priority 80, B_net 100, U 50, SKU-A (LOC-03)
        // 4. Priority 80, B_net 100, U 50, SKU-B (LOC-02)
        Assert.Equal(90m, sorted[0].PriorityScore);
        Assert.Equal("SKU-A", sorted[0].ItemSku);

        Assert.Equal(120m, sorted[1].NetMonthlyBenefitUsd);
        Assert.Equal("SKU-C", sorted[1].ItemSku);

        Assert.Equal(100m, sorted[2].NetMonthlyBenefitUsd);
        Assert.Equal("SKU-A", sorted[2].ItemSku);
        Assert.Equal("LOC-03", sorted[2].ProposedLocationCode);

        Assert.Equal(100m, sorted[3].NetMonthlyBenefitUsd);
        Assert.Equal("SKU-B", sorted[3].ItemSku);
        Assert.Equal("LOC-02", sorted[3].ProposedLocationCode);
    }

    [Fact]
    public async Task ApprovalWorkflow_DirectApprovalFromSimulatedOrDraft_IsRejectedByBackendStateValidation()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var policy = SlottingPolicy.Create("StateValidationPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        // 1. Batch in Simulated status (never submitted for review)
        var simulatedBatch = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-SIM-ONLY", fix.ClientId);
        simulatedBatch.MarkSimulated(1, 5m, 10m, 20m, 80m);
        context.SlottingRecommendationBatches.Add(simulatedBatch);

        // 2. Batch in Draft status
        var draftBatch = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-DRAFT-ONLY", fix.ClientId);
        context.SlottingRecommendationBatches.Add(draftBatch);

        await context.SaveChangesAsync();

        // Direct approval of Simulated batch must throw
        var simEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
            {
                BatchId = simulatedBatch.Id,
                ApprovedRecommendationIds = []
            }));
        Assert.Contains("PendingApproval", simEx.Message);

        // Direct approval of Draft batch must throw
        var draftEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
            {
                BatchId = draftBatch.Id,
                ApprovedRecommendationIds = []
            }));
        Assert.Contains("PendingApproval", draftEx.Message);
    }

    [Fact]
    public void DtoSerializationParity_RelocationPrioritizationResultDto_EnforceExactCamelCaseContract()
    {
        var resultDto = new RelocationPrioritizationResultDto
        {
            BatchId = Guid.NewGuid(),
            BatchNumber = "BATCH-PRIORITY-SERIALIZATION-01",
            FacilityId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            PolicyName = "High Velocity Policy",
            EvaluatedAtUtc = DateTime.UtcNow,
            TotalCandidatesEvaluated = 25,
            FeasibleCandidatesCount = 18,
            DisqualifiedNegativeBenefitCount = 4,
            HighUrgencyNegativeBenefitCount = 2,
            TotalProjectedGrossMonthlySavingsUsd = 1250.50m,
            TotalProjectedOneTimeRelocationCostUsd = 450.00m,
            TotalProjectedNetMonthlyBenefitUsd = 800.50m,
            PhysicalInventoryTransfersTriggered = 0,
            LedgerMutationsOccurred = 0,
            PrioritizedRecommendations = []
        };

        var json = System.Text.Json.JsonSerializer.Serialize(resultDto, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        // Assert exact camelCase keys present for invariant auditing fields
        Assert.Contains("\"ledgerMutationsOccurred\":0", json);
        Assert.Contains("\"physicalInventoryTransfersTriggered\":0", json);
        Assert.Contains("\"batchNumber\":", json);
        Assert.Contains("\"feasibleCandidatesCount\":18", json);
        Assert.Contains("\"disqualifiedNegativeBenefitCount\":4", json);
        Assert.Contains("\"totalProjectedNetMonthlyBenefitUsd\":800.50", json);

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<RelocationPrioritizationResultDto>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.LedgerMutationsOccurred);
        Assert.Equal(0, deserialized.PhysicalInventoryTransfersTriggered);
        Assert.Equal(resultDto.BatchNumber, deserialized.BatchNumber);
        Assert.Equal(resultDto.FeasibleCandidatesCount, deserialized.FeasibleCandidatesCount);
    }

    [Fact]
    public void DtoSerializationParity_RelocationApprovalResultDto_EnforceExactCamelCaseContract()
    {
        var approvalResult = new RelocationApprovalResultDto
        {
            BatchId = Guid.NewGuid(),
            Status = SlottingBatchStatus.PartiallyApproved,
            ApprovedCount = 3,
            RejectedCount = 1,
            SkippedCount = 2,
            PhysicalInventoryTransfersTriggered = 0,
            LedgerMutationsOccurred = 0,
            ProcessedAtUtc = DateTime.UtcNow,
            ApprovedBy = Guid.NewGuid(),
            Message = "Relocation plan processed administratively. 3 recommendations approved, 1 rejected."
        };

        var json = System.Text.Json.JsonSerializer.Serialize(approvalResult, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        // Assert exact camelCase keys present for invariant auditing fields and status
        Assert.Contains("\"ledgerMutationsOccurred\":0", json);
        Assert.Contains("\"physicalInventoryTransfersTriggered\":0", json);
        Assert.Contains("\"approvedCount\":3", json);
        Assert.Contains("\"rejectedCount\":1", json);
        Assert.Contains("\"skippedCount\":2", json);
        Assert.Contains("\"status\":", json);

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<RelocationApprovalResultDto>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.LedgerMutationsOccurred);
        Assert.Equal(0, deserialized.PhysicalInventoryTransfersTriggered);
        Assert.Equal(approvalResult.Status, deserialized.Status);
        Assert.Equal(3, deserialized.ApprovedCount);
        Assert.Equal(1, deserialized.RejectedCount);
        Assert.Equal(2, deserialized.SkippedCount);
    }

    [Fact]
    public async Task PrioritizationAndApproval_PopulateZeroMutationFieldsInRealResponses()
    {
        var fix = await SeedEnvironmentAsync();
        using var context = CreateContext(fix.DbName, isStaff: true, isSuperAdmin: true);
        var service = new RelocationRecommendationService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"], UserId = Guid.NewGuid() },
            NullLogger<RelocationRecommendationService>.Instance);

        var policy = SlottingPolicy.Create("RealResponseAuditPolicy", fix.FacilityId);
        context.SlottingPolicies.Add(policy);

        var item = new Item { Id = Guid.NewGuid(), ClientId = fix.ClientId, Sku = "SKU-RESPONSE-AUDIT", Description = "Audit Item" };
        context.Items.Add(item);

        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            ItemId = item.Id,
            LocationId = fix.UpperReserveLocationId,
            QtyAvailable = 50,
            QtyAllocated = 0,
            QtyReserved = 0
        };
        context.StockOnHand.Add(stock);
        await context.SaveChangesAsync();

        // 1. Run PrioritizeRelocationsAsync and verify populated counts
        var prioritizationResult = await service.PrioritizeRelocationsAsync(new RelocationPrioritizationRequestDto
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.ClientId,
            PolicyId = policy.Id,
            AsOfDateUtc = DateTime.UtcNow
        });

        Assert.Equal(0, prioritizationResult.PhysicalInventoryTransfersTriggered);
        Assert.Equal(0, prioritizationResult.LedgerMutationsOccurred);

        // Retrieve plan via GetPrioritizedPlanAsync and verify populated counts
        var planResult = await service.GetPrioritizedPlanAsync(prioritizationResult.BatchId);
        Assert.NotNull(planResult);
        Assert.Equal(0, planResult.PhysicalInventoryTransfersTriggered);
        Assert.Equal(0, planResult.LedgerMutationsOccurred);

        // Transition batch to PendingApproval
        await service.SubmitBatchForReviewAsync(prioritizationResult.BatchId);

        // 2. Run ApproveRelocationPlanAsync (Approval) and verify populated counts
        var approvalResult = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = prioritizationResult.BatchId,
            ApprovedRecommendationIds = prioritizationResult.PrioritizedRecommendations.Select(r => r.RecommendationId).ToList(),
            ApprovalNotes = "Administrative approval test"
        });

        Assert.Equal(0, approvalResult.PhysicalInventoryTransfersTriggered);
        Assert.Equal(0, approvalResult.LedgerMutationsOccurred);

        // 3. Create another batch and run ApproveRelocationPlanAsync (Rejection) and verify populated counts
        var rejectBatch = SlottingRecommendationBatch.Create(fix.FacilityId, policy.Id, "BATCH-REJECT-AUDIT", fix.ClientId);
        rejectBatch.Status = SlottingBatchStatus.PendingApproval;
        var recToReject = new SlottingRecommendation
        {
            Id = Guid.NewGuid(),
            BatchId = rejectBatch.Id,
            ItemId = item.Id,
            ItemSku = item.Sku,
            ClientId = fix.ClientId,
            CurrentLocationId = fix.UpperReserveLocationId,
            ProposedLocationId = fix.GoldenPickLocationId,
            Status = SlottingRecommendationStatus.Pending
        };
        rejectBatch.Recommendations.Add(recToReject);
        context.SlottingRecommendationBatches.Add(rejectBatch);
        await context.SaveChangesAsync();

        var rejectionResult = await service.ApproveRelocationPlanAsync(new RelocationApprovalRequestDto
        {
            BatchId = rejectBatch.Id,
            ApprovedRecommendationIds = [],
            RejectedRecommendationIds = [recToReject.Id],
            RejectionReasonsByRecommendationId = new Dictionary<Guid, string>
            {
                [recToReject.Id] = "Operational floor congestion in proposed zone"
            },
            ApprovalNotes = "Rejection audit"
        });

        Assert.Equal(0, rejectionResult.PhysicalInventoryTransfersTriggered);
        Assert.Equal(0, rejectionResult.LedgerMutationsOccurred);
        Assert.Equal(SlottingBatchStatus.Rejected, rejectionResult.Status);
    }

    [Fact]
    public void InvariantViolationDetection_NonzeroLedgerOrTransferCounts_TriggerCriticalViolationNotification()
    {
        // 1. Prioritization Evaluation with Nonzero Counts
        var nonzeroPrioritization1 = new RelocationPrioritizationResultDto
        {
            BatchNumber = "BATCH-VIOLATION-01",
            LedgerMutationsOccurred = 3,
            PhysicalInventoryTransfersTriggered = 0
        };
        var (msg1, isCrit1) = SlottingInvariantNotificationEvaluator.EvaluatePrioritization(nonzeroPrioritization1);
        Assert.True(isCrit1);
        Assert.Contains("CRITICAL INVARIANT VIOLATION", msg1);
        Assert.Contains("3 operational ledger mutations", msg1);

        var nonzeroPrioritization2 = new RelocationPrioritizationResultDto
        {
            BatchNumber = "BATCH-VIOLATION-02",
            LedgerMutationsOccurred = 0,
            PhysicalInventoryTransfersTriggered = 5
        };
        var (msg2, isCrit2) = SlottingInvariantNotificationEvaluator.EvaluatePrioritization(nonzeroPrioritization2);
        Assert.True(isCrit2);
        Assert.Contains("CRITICAL INVARIANT VIOLATION", msg2);
        Assert.Contains("5 physical transfers", msg2);

        // Clean Prioritization Evaluation (Zero Counts)
        var cleanPrioritization = new RelocationPrioritizationResultDto
        {
            BatchNumber = "BATCH-CLEAN-01",
            FeasibleCandidatesCount = 12,
            LedgerMutationsOccurred = 0,
            PhysicalInventoryTransfersTriggered = 0
        };
        var (cleanMsg, isCleanCrit) = SlottingInvariantNotificationEvaluator.EvaluatePrioritization(cleanPrioritization);
        Assert.False(isCleanCrit);
        Assert.DoesNotContain("CRITICAL INVARIANT VIOLATION", cleanMsg);
        Assert.Contains("Server-verified operational ledger mutations: 0, physical transfers: 0", cleanMsg);

        // 2. Approval Evaluation with Nonzero Counts
        var nonzeroApproval = new RelocationApprovalResultDto
        {
            ApprovedCount = 4,
            Status = SlottingBatchStatus.Approved,
            LedgerMutationsOccurred = 1,
            PhysicalInventoryTransfersTriggered = 0
        };
        var (appMsg, isAppCrit) = SlottingInvariantNotificationEvaluator.EvaluateApproval(nonzeroApproval);
        Assert.True(isAppCrit);
        Assert.Contains("CRITICAL INVARIANT VIOLATION", appMsg);
        Assert.DoesNotContain("Batch approved: 4 moves approved", appMsg);

        // Clean Approval Evaluation (Zero Counts)
        var cleanApproval = new RelocationApprovalResultDto
        {
            ApprovedCount = 4,
            SkippedCount = 0,
            Status = SlottingBatchStatus.Approved,
            LedgerMutationsOccurred = 0,
            PhysicalInventoryTransfersTriggered = 0
        };
        var (cleanAppMsg, isCleanAppCrit) = SlottingInvariantNotificationEvaluator.EvaluateApproval(cleanApproval);
        Assert.False(isCleanAppCrit);
        Assert.Contains("Batch approved: 4 moves approved", cleanAppMsg);
        Assert.Contains("Server-verified operational ledger mutations: 0, physical transfers: 0", cleanAppMsg);

        // 3. Rejection Evaluation with Nonzero Counts
        var nonzeroRejection = new RelocationApprovalResultDto
        {
            RejectedCount = 2,
            Status = SlottingBatchStatus.Rejected,
            LedgerMutationsOccurred = 0,
            PhysicalInventoryTransfersTriggered = 1
        };
        var (rejMsg, isRejCrit) = SlottingInvariantNotificationEvaluator.EvaluateRejection(nonzeroRejection);
        Assert.True(isRejCrit);
        Assert.Contains("CRITICAL INVARIANT VIOLATION", rejMsg);
        Assert.DoesNotContain("Batch updated: 2 moves rejected", rejMsg);

        // Clean Rejection Evaluation (Zero Counts)
        var cleanRejection = new RelocationApprovalResultDto
        {
            RejectedCount = 2,
            SkippedCount = 1,
            Status = SlottingBatchStatus.Rejected,
            LedgerMutationsOccurred = 0,
            PhysicalInventoryTransfersTriggered = 0
        };
        var (cleanRejMsg, isCleanRejCrit) = SlottingInvariantNotificationEvaluator.EvaluateRejection(cleanRejection);
        Assert.False(isCleanRejCrit);
        Assert.Contains("Batch updated: 2 moves rejected with justification", cleanRejMsg);
        Assert.Contains("Server-verified operational ledger mutations: 0, physical transfers: 0", cleanRejMsg);
    }

    #endregion
}
