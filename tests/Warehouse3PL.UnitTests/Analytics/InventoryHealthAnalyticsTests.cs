using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Comprehensive verification test suite for Phase 6.3.1:
/// Inventory Health Domain, KPI Dictionary & Aging Definitions.
/// Verifies 5 independent temporal dimensions, unconflated space utilization (Cube vs. Pallet),
/// configurable aging policy precedence, authoritative ledger read-only invariants,
/// deduplication, replay, and multi-tenant authorization.
/// </summary>
public class InventoryHealthAnalyticsTests
{
    private class TestInventoryHealthFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid Client1Id { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();
        public Guid Location1Id { get; set; } = Guid.NewGuid();
        public Guid Location2Id { get; set; } = Guid.NewGuid();
        public Guid Item1Id { get; set; } = Guid.NewGuid();
        public Guid Item2Id { get; set; } = Guid.NewGuid();
    }

    private static async Task<TestInventoryHealthFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestInventoryHealthFixture();
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

        var client1 = new Client
        {
            Id = fix.Client1Id,
            AccountCode = "ACME-CORP",
            CompanyName = "Acme Global Brands",
            Status = ClientStatus.Active
        };

        var client2 = new Client
        {
            Id = fix.Client2Id,
            AccountCode = "NEXUS-TECH",
            CompanyName = "Nexus Tech Supplies",
            Status = ClientStatus.Active
        };

        var item1 = new Item
        {
            Id = fix.Item1Id,
            ClientId = fix.Client1Id,
            Sku = "SKU-PREMIUM-01",
            Description = "Ergonomic Office Chair",
            Status = ItemStatus.Active
        };

        var item2 = new Item
        {
            Id = fix.Item2Id,
            ClientId = fix.Client1Id,
            Sku = "SKU-DESK-02",
            Description = "Standing Desk Riser",
            Status = ItemStatus.Active
        };

        var loc1 = new Location
        {
            Id = fix.Location1Id,
            FacilityId = fix.FacilityId,
            LocationCode = "RACK-01-A-01",
            LocationType = LocationType.Rack,
            MaxVolumeCm3 = 2_000_000m, // 2.0 CBM
            MaxWeightKg = 1000m,
            IsActive = true
        };

        var loc2 = new Location
        {
            Id = fix.Location2Id,
            FacilityId = fix.FacilityId,
            LocationCode = "RACK-01-A-02",
            LocationType = LocationType.Rack,
            MaxVolumeCm3 = 2_000_000m, // 2.0 CBM
            MaxWeightKg = 1000m,
            IsActive = true
        };

        await context.Facilities.AddRangeAsync(fac1, fac2);
        await context.Clients.AddRangeAsync(client1, client2);
        await context.Items.AddRangeAsync(item1, item2);
        await context.Locations.AddRangeAsync(loc1, loc2);
        await context.SaveChangesAsync();

        return fix;
    }

    [Fact]
    public void InventoryAging_DistinguishesFiveIndependentTemporalDimensions()
    {
        // Invariant: Receipt age, movement age, demand age, holding duration, and expiry proximity
        // are strictly independent measurements.
        var today = DateTime.UtcNow.Date;
        var policy = InventoryAgingPolicy.Create("Standard Policy");

        // Scenario: Item arrived 120 days ago (Receipt Age = 120d).
        // It was physically relocated/moved 2 days ago (Movement Age = 2d).
        // It was last demanded/picked 10 days ago (Demand Age = 10d).
        // It expires in 25 days (Expiry Proximity = 25d -> Critical Expiry).
        var snapshot = DailyInventoryAgingSnapshot.Create(
            clientId: Guid.NewGuid(),
            facilityId: Guid.NewGuid(),
            locationId: Guid.NewGuid(),
            locationCode: "BIN-01",
            itemId: Guid.NewGuid(),
            sku: "SKU-PHARMA-01",
            snapshotDateUtc: today,
            quantityOnHand: 50m,
            receivedAtUtc: today.AddDays(-120),
            lastMovedAtUtc: today.AddDays(-2),
            lastDemandedAtUtc: today.AddDays(-10),
            expirationDateUtc: today.AddDays(25),
            policy: policy,
            unitCost: 100.00m);

        // Assert: Temporal dimensions remain completely independent
        Assert.Equal(120, snapshot.ReceiptAgeDays);
        Assert.Equal(2, snapshot.LastMovementAgeDays);
        Assert.Equal(10, snapshot.LastDemandAgeDays);
        Assert.Equal(120, snapshot.HoldingDurationDays);
        Assert.Equal(25, snapshot.DaysUntilExpiry);

        // Assert: Expiry classification
        Assert.True(snapshot.IsCriticalExpiry);
        Assert.True(snapshot.IsNearExpiry);
        Assert.False(snapshot.IsExpired);

        // Assert: Aging bucket classification follows policy default basis (ReceiptDate -> 120d = SlowMoving_91_180)
        Assert.Equal(InventoryAgingBucket.SlowMoving_91_180, snapshot.AgingBucket);

        // Assert: Velocity classification follows LastDemandDate (10d <= 30d -> FastMoving)
        Assert.Equal(InventoryMovementVelocityCategory.FastMoving, snapshot.VelocityCategory);
    }

    [Fact]
    public void SpaceUtilization_IndependentlyCalculatesCubeFillAndPalletOccupancyWithoutConflation()
    {
        // Invariant: Never confuse pallet occupancy with cubic-volume utilization!
        // Cube % = (Occupied Volume / Usable Volume) * 100
        // Pallet % = (Occupied Pallets / Usable Pallets) * 100
        var today = DateTime.UtcNow.Date;

        // Scenario: A tall standard rack bay has 2.5 usable CBM and 1 usable pallet position.
        // It contains 1 short pallet with 1.0 CBM of stock.
        var record = DailyLocationSpaceUtilizationRecord.Create(
            facilityId: Guid.NewGuid(),
            locationId: Guid.NewGuid(),
            locationCode: "RACK-BAY-101",
            snapshotDateUtc: today,
            usableCbm: 2.50m,
            occupiedCbm: 1.00m,
            usablePalletPositions: 1,
            occupiedPalletPositions: 1,
            distinctSkuCount: 1,
            totalUnitsStored: 20m);

        // Assert: Pallet occupancy is 100%, but cubic utilization is only 40.00%
        Assert.Equal(100.00m, record.PalletOccupancyPercent);
        Assert.Equal(40.00m, record.CubeUtilizationPercent);
        Assert.False(record.IsMixedSkuLocation);

        // Primary space classification is Underutilized (< 50% cube)
        Assert.Equal(LocationSpaceUtilizationClassification.Underutilized, record.SpaceClassification);
    }

    [Fact]
    public void SpaceUtilization_FlagsMissingDimensionsFallback_NeverSilentlyTreatsUnknownAsZero()
    {
        // Invariant: Never silently treat unknown volume as zero.
        var today = DateTime.UtcNow.Date;

        var record = DailyLocationSpaceUtilizationRecord.Create(
            facilityId: Guid.NewGuid(),
            locationId: Guid.NewGuid(),
            locationCode: "BULK-01",
            snapshotDateUtc: today,
            usableCbm: 10.00m,
            occupiedCbm: 3.20m, // Fallback estimation
            usablePalletPositions: 4,
            occupiedPalletPositions: 2,
            hasMissingDimensions: true);

        Assert.True(record.HasMissingDimensionsFallback);
        Assert.Equal(32.00m, record.CubeUtilizationPercent);
        Assert.Equal(50.00m, record.PalletOccupancyPercent);
    }

    [Fact]
    public void MixedSkuLocation_CorrectlyIdentifiesMultipleDistinctSkusInSingleLocation()
    {
        var today = DateTime.UtcNow.Date;

        // Single location storing 3 distinct SKUs
        var record = DailyLocationSpaceUtilizationRecord.Create(
            facilityId: Guid.NewGuid(),
            locationId: Guid.NewGuid(),
            locationCode: "PICK-BIN-05",
            snapshotDateUtc: today,
            usableCbm: 0.50m,
            occupiedCbm: 0.40m,
            usablePalletPositions: 0,
            occupiedPalletPositions: 0,
            distinctSkuCount: 3,
            totalUnitsStored: 150m);

        Assert.True(record.IsMixedSkuLocation);
        Assert.Equal(3, record.DistinctSkuCount);
        Assert.Equal(80.00m, record.CubeUtilizationPercent);
        Assert.Equal(LocationSpaceUtilizationClassification.Optimal, record.SpaceClassification);
    }

    [Fact]
    public async Task ConfigurableAgingPolicy_EnforcesFourTierPrecedenceCascade()
    {
        // Precedence:
        // Rank 1: Specific Client + Specific Facility
        // Rank 2: Specific Client + General Facility (Facility is null)
        // Rank 3: Platform Default + Specific Facility (Client is null)
        // Rank 4: Platform Default + General Facility (Client and Facility null)
        // Ties broken by descending Version.
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // 1. Platform Default (Dead stock = 365 days)
        var pDefault = InventoryAgingPolicy.Create(
            "Platform Default", null, null,
            slowMovingDays: 90, dormantDays: 180, deadStockDays: 365, version: 1);

        // 2. Facility Override for Facility 1 (Dead stock = 300 days)
        var pFac1 = InventoryAgingPolicy.Create(
            "Facility 1 Standard", fix.FacilityId, null,
            slowMovingDays: 60, dormantDays: 120, deadStockDays: 300, version: 1);

        // 3. Client Override for Client 1 (Dead stock = 180 days)
        var pClient1 = InventoryAgingPolicy.Create(
            "Client 1 Global", null, fix.Client1Id,
            slowMovingDays: 45, dormantDays: 90, deadStockDays: 180, version: 1);

        // 4. Exact Client 1 + Facility 1 Override, Version 1 (Dead stock = 120 days)
        var pExactV1 = InventoryAgingPolicy.Create(
            "Client 1 Facility 1 v1", fix.FacilityId, fix.Client1Id,
            slowMovingDays: 30, dormantDays: 60, deadStockDays: 120, version: 1);

        // 5. Exact Client 1 + Facility 1 Override, Version 2 (Dead stock = 90 days) - HIGHER VERSION
        var pExactV2 = InventoryAgingPolicy.Create(
            "Client 1 Facility 1 v2", fix.FacilityId, fix.Client1Id,
            slowMovingDays: 20, dormantDays: 45, deadStockDays: 90, version: 2);

        await context.InventoryAgingPolicies.AddRangeAsync(pDefault, pFac1, pClient1, pExactV1, pExactV2);
        await context.SaveChangesAsync();

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);

        // Test 1: Query for Client 1 + Facility 1 -> Resolves Exact v2 (Rank 1, Version 2)
        var policy1 = await queryService.GetInventoryAgingPolicyAsync(fix.FacilityId, fix.Client1Id);
        Assert.Equal("Client 1 Facility 1 v2", policy1.Name);
        Assert.Equal(90, policy1.DeadStockThresholdDays);
        Assert.Equal(2, policy1.Version);

        // Test 2: Query for Client 1 + Facility 2 (no exact fac2 policy) -> Resolves Client 1 Global (Rank 2)
        var policy2 = await queryService.GetInventoryAgingPolicyAsync(fix.Facility2Id, fix.Client1Id);
        Assert.Equal("Client 1 Global", policy2.Name);
        Assert.Equal(180, policy2.DeadStockThresholdDays);

        // Test 3: Query for Client 2 + Facility 1 (no client2 policy) -> Resolves Facility 1 Standard (Rank 3)
        var policy3 = await queryService.GetInventoryAgingPolicyAsync(fix.FacilityId, fix.Client2Id);
        Assert.Equal("Facility 1 Standard", policy3.Name);
        Assert.Equal(300, policy3.DeadStockThresholdDays);

        // Test 4: Query for Client 2 + Facility 2 -> Resolves Platform Default (Rank 4)
        var policy4 = await queryService.GetInventoryAgingPolicyAsync(fix.Facility2Id, fix.Client2Id);
        Assert.Equal("Platform Default", policy4.Name);
        Assert.Equal(365, policy4.DeadStockThresholdDays);
    }

    [Fact]
    public async Task AuthoritativeInventoryLedger_RemainsUntouchedAndReadModelIsSeparated()
    {
        // Architectural Invariant: The analytics plane must never modify StockOnHand or InventoryTransaction.
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // Setup authoritative StockOnHand
        var stockRecord = new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.Client1Id,
            FacilityId = fix.FacilityId,
            ItemId = fix.Item1Id,
            LocationId = fix.Location1Id,
            QtyAvailable = 100m,
            QtyAllocated = 20m,
            Version = 1,
            ReceivedAtUtc = DateTime.UtcNow.AddDays(-60)
        };

        // Setup authoritative InventoryTransaction
        var txRecord = new InventoryTransaction
        {
            ClientId = fix.Client1Id,
            FacilityId = fix.FacilityId,
            ItemId = fix.Item1Id,
            Quantity = 100m,
            TxType = InventoryTxType.Receive,
            TxTimestamp = DateTime.UtcNow.AddDays(-60)
        };

        await context.StockOnHand.AddAsync(stockRecord);
        await context.InventoryTransactions.AddAsync(txRecord);
        await context.SaveChangesAsync();

        var initialStockVersion = stockRecord.Version;
        var initialQty = stockRecord.QtyAvailable;

        // Act: Run analytics projection ingestion
        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var snapshotDate = DateTime.UtcNow.Date;

        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 1L,
            clientId: fix.Client1Id,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "RACK-01-A-01",
            itemId: fix.Item1Id,
            sku: "SKU-PREMIUM-01",
            snapshotDateUtc: snapshotDate,
            quantityOnHand: stockRecord.TotalQuantity,
            receivedAtUtc: stockRecord.ReceivedAtUtc,
            lastMovedAtUtc: stockRecord.ReceivedAtUtc,
            lastDemandedAtUtc: null,
            expirationDateUtc: null,
            unitCost: 50.00m,
            occupiedCbm: 1.2m,
            sourceInventoryReference: stockRecord.Id.ToString());

        // Assert: Authoritative records remain completely unchanged
        var refreshedStock = await context.StockOnHand.FirstAsync(s => s.Id == stockRecord.Id);
        Assert.Equal(initialStockVersion, refreshedStock.Version);
        Assert.Equal(initialQty, refreshedStock.QtyAvailable);
        Assert.Equal(120m, refreshedStock.TotalQuantity);

        var refreshedTxCount = await context.InventoryTransactions.CountAsync();
        Assert.Equal(1, refreshedTxCount);

        // Analytics read model is successfully populated
        var agingSnap = await context.DailyInventoryAgingSnapshots.FirstAsync();
        Assert.Equal(120m, agingSnap.QuantityOnHand);
        Assert.Equal(60, agingSnap.ReceiptAgeDays);
        Assert.Equal(InventoryAgingBucket.Aging_31_60, agingSnap.AgingBucket);
        Assert.Equal(stockRecord.Id.ToString(), agingSnap.SourceInventoryReference);
    }

    [Fact]
    public async Task CheckpointingAndDeduplication_PreventsDoubleCountingOnDuplicateEvents()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var eventId = Guid.NewGuid();
        var today = DateTime.UtcNow.Date;

        // Ingest space utilization event
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: eventId,
            sequenceNumber: 101L,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "RACK-01-A-01",
            snapshotDateUtc: today,
            usableCbm: 2.0m,
            occupiedCbm: 1.5m,
            usablePalletPositions: 1,
            occupiedPalletPositions: 1);

        // Attempt duplicate delivery of identical eventId
        await projService.ProcessLocationSpaceUtilizationAsync(
            eventId: eventId,
            sequenceNumber: 101L,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "RACK-01-A-01",
            snapshotDateUtc: today,
            usableCbm: 2.0m,
            occupiedCbm: 1.5m,
            usablePalletPositions: 1,
            occupiedPalletPositions: 1);

        // Assert: Exactly one record created
        var recordsCount = await context.DailyLocationSpaceUtilizationRecords.CountAsync();
        Assert.Equal(1, recordsCount);
    }

    [Fact]
    public async Task ProjectionReplay_CleanlyPurgesInventoryHealthReadModels()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var today = DateTime.UtcNow.Date;

        // Ingest aging snapshot
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 1L,
            clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "RACK-01",
            itemId: fix.Item1Id, sku: "SKU-01",
            snapshotDateUtc: today, quantityOnHand: 50m,
            receivedAtUtc: today.AddDays(-10), lastMovedAtUtc: today.AddDays(-10),
            lastDemandedAtUtc: null, expirationDateUtc: null);

        Assert.Equal(1, await context.DailyInventoryAgingSnapshots.CountAsync());

        // Act: Replay from beginning
        await projService.ReplayProjectionsAsync(
            AnalyticsProjectionService.InventoryAgingProjection,
            ProjectionReplayMode.FromBeginning);

        // Assert: Read models cleanly purged
        Assert.Equal(0, await context.DailyInventoryAgingSnapshots.CountAsync());
    }

    [Fact]
    public async Task InventoryHealthSummary_AggregatesTotalsAndCalculatesDeadStockRatio()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var today = DateTime.UtcNow.Date;
        var policy = InventoryAgingPolicy.Create("Standard Policy");

        // Record 1: Fresh active stock - 100 units @ $10 = $1,000 (Age = 15d, Current)
        var snap1 = DailyInventoryAgingSnapshot.Create(
            fix.Client1Id, fix.FacilityId, fix.Location1Id, "LOC-1",
            fix.Item1Id, "SKU-ACTIVE", today, quantityOnHand: 100m,
            receivedAtUtc: today.AddDays(-15), lastMovedAtUtc: today.AddDays(-15),
            lastDemandedAtUtc: today.AddDays(-2), expirationDateUtc: today.AddDays(200),
            policy: policy, unitCost: 10.00m, occupiedCbm: 1.0m, palletFootprint: 1m);

        // Record 2: Dead stock - 50 units @ $20 = $1,000 (Age = 400d, DeadStock_365Plus)
        var snap2 = DailyInventoryAgingSnapshot.Create(
            fix.Client1Id, fix.FacilityId, fix.Location2Id, "LOC-2",
            fix.Item2Id, "SKU-DEAD", today, quantityOnHand: 50m,
            receivedAtUtc: today.AddDays(-400), lastMovedAtUtc: today.AddDays(-400),
            lastDemandedAtUtc: today.AddDays(-380), expirationDateUtc: null,
            policy: policy, unitCost: 20.00m, occupiedCbm: 1.0m, palletFootprint: 1m);

        await context.DailyInventoryAgingSnapshots.AddRangeAsync(snap1, snap2);
        await context.SaveChangesAsync();

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);

        // Act
        var summary = await queryService.GetInventoryHealthSummaryAsync(fix.FacilityId, today);

        // Assert
        Assert.Equal(2, summary.TotalActiveSkus);
        Assert.Equal(150m, summary.TotalUnitsOnHand);
        Assert.Equal(2000m, summary.TotalInventoryValue); // $1,000 + $1,000
        Assert.Equal(2.0m, summary.TotalOccupiedCbm);
        Assert.Equal(2.0m, summary.TotalPalletFootprint);

        // Dead stock ratio: $1,000 / $2,000 = 50.00%
        Assert.Equal(1000m, summary.DeadStockValue);
        Assert.Equal(50.00m, summary.DeadStockRatioPercent);
        Assert.Equal(1, summary.DeadStockSkuCount);
        Assert.Equal(50m, summary.DeadStockUnitsCount);

        // Aging buckets breakdown has all 6 buckets
        Assert.Equal(6, summary.AgingBuckets.Count);
        var currentBucket = summary.AgingBuckets.First(b => b.Bucket == InventoryAgingBucket.Current_0_30);
        Assert.Equal(1, currentBucket.SkuCount);
        Assert.Equal(1000m, currentBucket.TotalValue);
        Assert.Equal(50.00m, currentBucket.PercentageOfTotalValue);

        var deadBucket = summary.AgingBuckets.First(b => b.Bucket == InventoryAgingBucket.DeadStock_365Plus);
        Assert.Equal(1, deadBucket.SkuCount);
        Assert.Equal(1000m, deadBucket.TotalValue);
        Assert.Equal(50.00m, deadBucket.PercentageOfTotalValue);
    }

    [Fact]
    public async Task MultiTenantAndPrivacy_RestrictsClientPortalAndEnforcesFacilityBoundaries()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var today = DateTime.UtcNow.Date;

        // Context 1: Client Portal Tenant User (Client 1)
        var client1Context = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = fix.Client1Id,
            UserType = UserType.Client
        };

        var client1QueryService = new AnalyticsQueryService(context, client1Context, NullLogger<AnalyticsQueryService>.Instance);

        // Client 1 can access inventory health summary for own client
        var ownSummary = await client1QueryService.GetInventoryHealthSummaryAsync(fix.FacilityId, today, fix.Client1Id);
        Assert.NotNull(ownSummary);

        // Client 1 attempting to query Client 2 data throws UnauthorizedException (403)
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            client1QueryService.GetInventoryHealthSummaryAsync(fix.FacilityId, today, fix.Client2Id));

        // Client 1 attempting to view internal warehouse layout space utilization throws UnauthorizedException (403)
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            client1QueryService.GetLocationSpaceUtilizationAsync(fix.FacilityId, today));

        // Context 2: Supervisor assigned strictly to Facility 1
        var supervisorFac1Context = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = fix.FacilityId
        };

        var supervisorQueryService = new AnalyticsQueryService(context, supervisorFac1Context, NullLogger<AnalyticsQueryService>.Instance);

        // Supervisor querying own facility succeeds
        var fac1Utilization = await supervisorQueryService.GetLocationSpaceUtilizationAsync(fix.FacilityId, today);
        Assert.NotNull(fac1Utilization);

        // Supervisor querying Facility 2 throws UnauthorizedException (403)
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            supervisorQueryService.GetLocationSpaceUtilizationAsync(fix.Facility2Id, today));
    }

    [Fact]
    public async Task SkuVelocityClassification_PreservesParetoMetricsCriteriaAndVersion()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var periodStart = DateTime.UtcNow.Date.AddDays(-90);
        var periodEnd = DateTime.UtcNow.Date;

        // Record SKU Class A classification
        await projService.ProcessSkuVelocityClassificationAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 50L,
            clientId: fix.Client1Id,
            facilityId: fix.FacilityId,
            itemId: fix.Item1Id,
            sku: "SKU-PREMIUM-01",
            abcClass: AbcClassificationCategory.ClassA,
            criteriaUsed: AbcClassificationCriteria.AnnualConsumptionValue,
            velocityCategory: InventoryMovementVelocityCategory.FastMoving,
            periodStartUtc: periodStart,
            periodEndUtc: periodEnd,
            pickTasksCount: 450,
            unitsDemanded: 1200m,
            demandValue: 45000m,
            orderLinesCount: 400,
            cumulativePercentage: 72.50m,
            version: 1);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);

        var records = await queryService.GetSkuVelocityClassificationsAsync(fix.FacilityId, fix.Client1Id);
        Assert.Single(records);

        var r = records[0];
        Assert.Equal("SKU-PREMIUM-01", r.Sku);
        Assert.Equal(AbcClassificationCategory.ClassA, r.AbcClass);
        Assert.Equal(AbcClassificationCriteria.AnnualConsumptionValue, r.CriteriaUsed);
        Assert.Equal(InventoryMovementVelocityCategory.FastMoving, r.VelocityCategory);
        Assert.Equal(72.50m, r.CumulativePercentage);
        Assert.Equal(90, r.PeriodDurationDays);
        Assert.Equal(Math.Round(1200m / 90m, 4), r.DailyDemandVelocity);
        Assert.Equal(1, r.Version);
    }
}
