using Microsoft.AspNetCore.Mvc;
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
using Warehouse3PL.WebApi.Controllers;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Exhaustive unit and integration test suite for Phase 6.3.2:
/// Inventory Aging & Dead Stock Engine.
/// Verifies lot-level aging preservation, multi-expiration dates, configurable policies,
/// non-conflated velocity classifications, holding cost exposure, ledger reconciliation,
/// controller API contracts, and multi-tenant authorization.
/// </summary>
public class InventoryAgingAndDeadStockTests
{
    private class TestFixture
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
            Sku = "SKU-PROD-A",
            Description = "Premium Noise Cancelling Headphones",
            Status = ItemStatus.Active
        };

        var item2 = new Item
        {
            Id = fix.Item2Id,
            ClientId = fix.Client1Id,
            Sku = "SKU-PROD-B",
            Description = "Wireless Charging Pad",
            Status = ItemStatus.Active
        };

        var loc1 = new Location
        {
            Id = fix.Location1Id,
            FacilityId = fix.FacilityId,
            LocationCode = "A-01-01-A",
            LocationType = LocationType.Rack,
            MaxVolumeCm3 = 2_000_000m,
            MaxWeightKg = 1000m,
            IsActive = true
        };

        var loc2 = new Location
        {
            Id = fix.Location2Id,
            FacilityId = fix.FacilityId,
            LocationCode = "B-02-01-A",
            LocationType = LocationType.Rack,
            MaxVolumeCm3 = 2_000_000m,
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
    public async Task LotLevelAging_MultipleLotsOfSameSku_CalculateIndependentAgesAndBuckets()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var lotAId = Guid.NewGuid();
        var lotBId = Guid.NewGuid();

        // Lot A: Received 120 days ago -> Should be SlowMoving_91_180
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 1L,
            clientId: fix.Client1Id,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "A-01-01-A",
            itemId: fix.Item1Id,
            sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate,
            quantityOnHand: 50m,
            receivedAtUtc: asOfDate.AddDays(-120),
            lastMovedAtUtc: asOfDate.AddDays(-120),
            lastDemandedAtUtc: asOfDate.AddDays(-120),
            expirationDateUtc: null,
            unitCost: 100m,
            occupiedCbm: 1.5m,
            palletFootprint: 1.0m,
            hasMissingDimensions: false,
            lotId: lotAId,
            lotNumber: "LOT-OLDER-120D");

        // Lot B: Received 15 days ago -> Should be Current_0_30
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(),
            sequenceNumber: 2L,
            clientId: fix.Client1Id,
            facilityId: fix.FacilityId,
            locationId: fix.Location1Id,
            locationCode: "A-01-01-A",
            itemId: fix.Item1Id,
            sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate,
            quantityOnHand: 80m,
            receivedAtUtc: asOfDate.AddDays(-15),
            lastMovedAtUtc: asOfDate.AddDays(-15),
            lastDemandedAtUtc: asOfDate.AddDays(-15),
            expirationDateUtc: null,
            unitCost: 100m,
            occupiedCbm: 2.0m,
            palletFootprint: 1.0m,
            hasMissingDimensions: false,
            lotId: lotBId,
            lotNumber: "LOT-FRESH-15D");

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);

        // Query lot breakdown for Item 1
        var lotBreakdown = await queryService.GetLotAgingBreakdownAsync(fix.FacilityId, fix.Item1Id, asOfDate);
        Assert.Equal(2, lotBreakdown.Count);

        var lotA = lotBreakdown.First(l => l.LotId == lotAId);
        Assert.Equal(120, lotA.ReceiptAgeDays);
        Assert.Equal(InventoryAgingBucket.SlowMoving_91_180, lotA.AgingBucket);
        Assert.True(lotA.IsAged);

        var lotB = lotBreakdown.First(l => l.LotId == lotBId);
        Assert.Equal(15, lotB.ReceiptAgeDays);
        Assert.Equal(InventoryAgingBucket.Current_0_30, lotB.AgingBucket);
        Assert.False(lotB.IsAged);
    }

    [Fact]
    public async Task ExpirationProximity_MultipleExpirationDates_DistinguishCriticalNearAndExpired()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var lotExpiredId = Guid.NewGuid();
        var lotCriticalId = Guid.NewGuid();
        var lotNearExpiryId = Guid.NewGuid();
        var lotHealthyId = Guid.NewGuid();

        // Expired 5 days ago (DaysUntilExpiry = -5)
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 10L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 20m, receivedAtUtc: asOfDate.AddDays(-200),
            lastMovedAtUtc: asOfDate.AddDays(-100), lastDemandedAtUtc: asOfDate.AddDays(-100),
            expirationDateUtc: asOfDate.AddDays(-5), unitCost: 50m, lotId: lotExpiredId, lotNumber: "LOT-EXPIRED");

        // Critical Expiry in 15 days (<= 30 days)
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 11L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 30m, receivedAtUtc: asOfDate.AddDays(-150),
            lastMovedAtUtc: asOfDate.AddDays(-50), lastDemandedAtUtc: asOfDate.AddDays(-50),
            expirationDateUtc: asOfDate.AddDays(15), unitCost: 50m, lotId: lotCriticalId, lotNumber: "LOT-CRITICAL");

        // Near Expiry in 45 days (<= 60 days, > 30 days)
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 12L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 40m, receivedAtUtc: asOfDate.AddDays(-100),
            lastMovedAtUtc: asOfDate.AddDays(-30), lastDemandedAtUtc: asOfDate.AddDays(-30),
            expirationDateUtc: asOfDate.AddDays(45), unitCost: 50m, lotId: lotNearExpiryId, lotNumber: "LOT-NEAR-EXP");

        // Healthy expiry in 180 days
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 13L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 50m, receivedAtUtc: asOfDate.AddDays(-30),
            lastMovedAtUtc: asOfDate.AddDays(-10), lastDemandedAtUtc: asOfDate.AddDays(-10),
            expirationDateUtc: asOfDate.AddDays(180), unitCost: 50m, lotId: lotHealthyId, lotNumber: "LOT-HEALTHY");

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var lots = await queryService.GetLotAgingBreakdownAsync(fix.FacilityId, fix.Item1Id, asOfDate);

        var expLot = lots.First(l => l.LotId == lotExpiredId);
        Assert.True(expLot.IsExpired);
        Assert.Equal(-5, expLot.DaysUntilExpiry);

        var critLot = lots.First(l => l.LotId == lotCriticalId);
        Assert.False(critLot.IsExpired);
        Assert.True(critLot.IsNearExpiry);
        Assert.Equal(15, critLot.DaysUntilExpiry);

        var nearLot = lots.First(l => l.LotId == lotNearExpiryId);
        Assert.False(nearLot.IsExpired);
        Assert.True(nearLot.IsNearExpiry);
        Assert.Equal(45, nearLot.DaysUntilExpiry);

        var healthyLot = lots.First(l => l.LotId == lotHealthyId);
        Assert.False(healthyLot.IsExpired);
        Assert.False(healthyLot.IsNearExpiry);
        Assert.Equal(180, healthyLot.DaysUntilExpiry);

        var summary = await queryService.GetInventoryHealthSummaryAsync(fix.FacilityId, asOfDate, fix.Client1Id);
        Assert.Equal(1, summary.ExpiredLotCount);
        Assert.Equal(20m * 50m, summary.ExpiredStockValue);
        Assert.Equal(2, summary.NearExpiryLotCount); // critical + near expiry lots
    }

    [Fact]
    public async Task ConfigurableAgingPolicy_CustomThresholds_ClassifyBucketsAndDeadStockCorrectly()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // Custom policy: SlowMoving = 45d, Dormant = 90d, DeadStock = 180d
        var customPolicy = InventoryAgingPolicy.Create(
            name: "High Turn Electronics Policy",
            facilityId: fix.FacilityId,
            clientId: fix.Client1Id,
            slowMovingDays: 45,
            dormantDays: 90,
            deadStockDays: 180,
            nearExpiryDays: 30,
            criticalExpiryDays: 15,
            currency: "USD",
            dailyHoldingCostPerCbm: 0.10m);

        await projService.UpsertInventoryAgingPolicyAsync(customPolicy);

        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Item 1: Received 100 days ago with no demand -> with custom policy (dormant=90d, dead=180d), this falls in Dormant_181_365 bucket range
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 20L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 10m, receivedAtUtc: asOfDate.AddDays(-100),
            lastMovedAtUtc: asOfDate.AddDays(-100), lastDemandedAtUtc: asOfDate.AddDays(-100),
            expirationDateUtc: null, unitCost: 100m);

        // Item 2: Received 200 days ago with no demand -> Dead stock under 180d policy!
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 21L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location2Id, locationCode: "B-02-01-A", itemId: fix.Item2Id, sku: "SKU-PROD-B",
            snapshotDateUtc: asOfDate, quantityOnHand: 25m, receivedAtUtc: asOfDate.AddDays(-200),
            lastMovedAtUtc: asOfDate.AddDays(-200), lastDemandedAtUtc: asOfDate.AddDays(-200),
            expirationDateUtc: null, unitCost: 40m);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var records = await queryService.GetDailyInventoryAgingRecordsAsync(fix.FacilityId, asOfDate, fix.Client1Id);

        var rec1 = records.First(r => r.ItemId == fix.Item1Id);
        Assert.Equal(100, rec1.ReceiptAgeDays);
        Assert.Equal(InventoryAgingBucket.Dormant_181_365, rec1.AgingBucket);
        Assert.False(rec1.IsDeadStock);

        var rec2 = records.First(r => r.ItemId == fix.Item2Id);
        Assert.Equal(200, rec2.ReceiptAgeDays);
        Assert.Equal(InventoryAgingBucket.DeadStock_365Plus, rec2.AgingBucket);
        Assert.True(rec2.IsDeadStock);
    }

    [Fact]
    public async Task DemandInactivity_NoDemandHistory_MaintainsExplicitNullDemandInactivityDays()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Item received 120 days ago that was NEVER demanded
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 30L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 50m, receivedAtUtc: asOfDate.AddDays(-120),
            lastMovedAtUtc: asOfDate.AddDays(-120),
            lastDemandedAtUtc: null, // Never demanded
            expirationDateUtc: null, unitCost: 100m);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var records = await queryService.GetDailyInventoryAgingRecordsAsync(fix.FacilityId, asOfDate, fix.Client1Id);

        var rec = records.First(r => r.ItemId == fix.Item1Id);
        Assert.False(rec.HasDemandHistory);
        Assert.Null(rec.DemandInactivityDays);
        Assert.Equal(120, rec.LastDemandAgeDays); // Falls back to receipt age for bucketing
    }

    [Fact]
    public async Task PhysicalRelocation_DoesNotResetReceiptAgeOrMaskDeadStock()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Item received 400 days ago, last demanded 400 days ago, but physically relocated 2 days ago
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 40L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 15m, receivedAtUtc: asOfDate.AddDays(-400),
            lastMovedAtUtc: asOfDate.AddDays(-2), // Putaway / moved recently
            lastDemandedAtUtc: asOfDate.AddDays(-400),
            expirationDateUtc: null, unitCost: 200m);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var records = await queryService.GetDailyInventoryAgingRecordsAsync(fix.FacilityId, asOfDate, fix.Client1Id);

        var rec = records.First(r => r.ItemId == fix.Item1Id);
        Assert.Equal(400, rec.ReceiptAgeDays);
        Assert.Equal(2, rec.LastMovementAgeDays);
        Assert.True(rec.IsAged);
        Assert.True(rec.IsDeadStock); // Relocation does NOT reset receipt aging or mask dead stock
        Assert.Equal(InventoryAgingBucket.DeadStock_365Plus, rec.AgingBucket);
    }

    [Fact]
    public async Task DistinctClassification_AgedVsSlowMovingVsNonMovingVsDeadStock()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Case 1: Aged item received 400 days ago, BUT sold 5 days ago (Fast velocity, NOT dead stock)
        var item1SnapId = Guid.NewGuid();
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: item1SnapId, sequenceNumber: 50L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 10m, receivedAtUtc: asOfDate.AddDays(-400),
            lastMovedAtUtc: asOfDate.AddDays(-50), lastDemandedAtUtc: asOfDate.AddDays(-5), // Active sales!
            expirationDateUtc: null, unitCost: 100m);

        // Case 2: Item received 400 days ago, last demanded 400 days ago (Dead stock)
        var item2SnapId = Guid.NewGuid();
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: item2SnapId, sequenceNumber: 51L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location2Id, locationCode: "B-02-01-A", itemId: fix.Item2Id, sku: "SKU-PROD-B",
            snapshotDateUtc: asOfDate, quantityOnHand: 20m, receivedAtUtc: asOfDate.AddDays(-400),
            lastMovedAtUtc: asOfDate.AddDays(-400), lastDemandedAtUtc: asOfDate.AddDays(-400),
            expirationDateUtc: null, unitCost: 50m);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var records = await queryService.GetDailyInventoryAgingRecordsAsync(fix.FacilityId, asOfDate, fix.Client1Id);

        var item1 = records.First(r => r.ItemId == fix.Item1Id);
        Assert.True(item1.IsAged); // Receipt age 400d >= 90d
        Assert.False(item1.IsNonMoving);
        Assert.False(item1.IsDeadStock); // Had demand 5 days ago!

        var item2 = records.First(r => r.ItemId == fix.Item2Id);
        Assert.True(item2.IsAged);
        Assert.True(item2.IsDeadStock); // Demand 400d ago >= 365d
    }

    [Fact]
    public async Task HoldingStatusAndLedgerReconciliation_PartitionedQuantitiesReconcileTotalStock()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // 100 units total: 60 Available, 20 Allocated, 10 Quarantined, 10 Damaged
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 60L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 100m, receivedAtUtc: asOfDate.AddDays(-45),
            lastMovedAtUtc: asOfDate.AddDays(-45), lastDemandedAtUtc: asOfDate.AddDays(-10),
            expirationDateUtc: null, unitCost: 80m,
            holdingStatus: InventoryHoldingStatus.Available,
            qtyAvailable: 60m,
            qtyAllocated: 20m,
            qtyReserved: 0m,
            qtyQuarantined: 10m,
            qtyDamaged: 10m,
            qtyOnHold: 0m);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var records = await queryService.GetDailyInventoryAgingRecordsAsync(fix.FacilityId, asOfDate, fix.Client1Id);

        var rec = records.First(r => r.ItemId == fix.Item1Id);
        Assert.Equal(100m, rec.QuantityOnHand);
        Assert.Equal(60m, rec.QtyAvailable);
        Assert.Equal(20m, rec.QtyAllocated);
        Assert.Equal(10m, rec.QtyQuarantined);
        Assert.Equal(10m, rec.QtyDamaged);
        Assert.Equal(rec.QuantityOnHand, rec.QtyAvailable + rec.QtyAllocated + rec.QtyReserved + rec.QtyQuarantined + rec.QtyDamaged + rec.QtyOnHold);

        var summary = await queryService.GetInventoryHealthSummaryAsync(fix.FacilityId, asOfDate, fix.Client1Id);
        Assert.NotEmpty(summary.HoldingStatusBreakdown);
    }

    [Fact]
    public async Task HoldingCostExposure_CalculatesDailyAndAccruedCostsDeterministically()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Occupies 4.0 CBM, receipt age 100 days.
        // Default policy dailyHoldingCostPerCbm = 0.05 USD.
        // Expected DailyHoldingCost = 4.0 * 0.05 = 0.20 USD.
        // Expected AccruedHoldingCost = 0.20 * 100 = 20.00 USD.
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 70L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 50m, receivedAtUtc: asOfDate.AddDays(-100),
            lastMovedAtUtc: asOfDate.AddDays(-100), lastDemandedAtUtc: asOfDate.AddDays(-100),
            expirationDateUtc: null, unitCost: 100m, occupiedCbm: 4.0m, palletFootprint: 2.0m, currency: "USD");

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var records = await queryService.GetDailyInventoryAgingRecordsAsync(fix.FacilityId, asOfDate, fix.Client1Id);

        var rec = records.First(r => r.ItemId == fix.Item1Id);
        Assert.Equal("USD", rec.Currency);
        Assert.Equal(0.20m, rec.DailyHoldingCost);
        Assert.Equal(20.00m, rec.AccruedHoldingCost);
    }

    [Fact]
    public async Task DeadStockAnalysis_AggregatesCapitalExposureAndAccruedHoldingCosts()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Item 1: Dead stock (400 days receipt, 400 days no demand) -> Total value = 50 * 100 = 5,000 USD
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 80L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 50m, receivedAtUtc: asOfDate.AddDays(-400),
            lastMovedAtUtc: asOfDate.AddDays(-400), lastDemandedAtUtc: asOfDate.AddDays(-400),
            expirationDateUtc: null, unitCost: 100m, occupiedCbm: 2.0m, lotId: Guid.NewGuid(), lotNumber: "LOT-DEAD-1");

        // Item 2: Active stock (20 days receipt) -> Total value = 100 * 50 = 5,000 USD
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 81L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location2Id, locationCode: "B-02-01-A", itemId: fix.Item2Id, sku: "SKU-PROD-B",
            snapshotDateUtc: asOfDate, quantityOnHand: 100m, receivedAtUtc: asOfDate.AddDays(-20),
            lastMovedAtUtc: asOfDate.AddDays(-20), lastDemandedAtUtc: asOfDate.AddDays(-5),
            expirationDateUtc: null, unitCost: 50m, occupiedCbm: 2.0m, lotId: Guid.NewGuid(), lotNumber: "LOT-ACTIVE-1");

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var deadStockAnalysis = await queryService.GetDeadStockAnalysisAsync(fix.FacilityId, fix.Client1Id, asOfDate);

        Assert.Equal(1, deadStockAnalysis.TotalDeadStockSkus);
        Assert.Equal(50m, deadStockAnalysis.TotalDeadStockUnits);
        Assert.Equal(5000m, deadStockAnalysis.TotalDeadStockValue);
        Assert.Equal(50.0m, deadStockAnalysis.DeadStockPercentageOfInventoryValue); // 5000 / 10000 = 50%
        Assert.Single(deadStockAnalysis.Items);
        Assert.Equal("SKU-PROD-A", deadStockAnalysis.Items[0].Sku);
    }

    [Fact]
    public async Task DeduplicationAndReplay_EnsuresIdempotencyAndLedgerSafety()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();

        // 1. Process initial event
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: eventId, sequenceNumber: 90L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 50m, receivedAtUtc: asOfDate.AddDays(-50),
            lastMovedAtUtc: asOfDate.AddDays(-50), lastDemandedAtUtc: asOfDate.AddDays(-50),
            expirationDateUtc: null, unitCost: 100m);

        var initialCount = await context.DailyInventoryAgingSnapshots.CountAsync();
        Assert.Equal(1, initialCount);

        // 2. Duplicate event must be skipped idempotently
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: eventId, sequenceNumber: 90L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 50m, receivedAtUtc: asOfDate.AddDays(-50),
            lastMovedAtUtc: asOfDate.AddDays(-50), lastDemandedAtUtc: asOfDate.AddDays(-50),
            expirationDateUtc: null, unitCost: 100m);

        var postDupCount = await context.DailyInventoryAgingSnapshots.CountAsync();
        Assert.Equal(1, postDupCount);

        // 3. Replay projection
        var replayed = await projService.ReplayProjectionsAsync(
            AnalyticsProjectionService.InventoryAgingProjection,
            ProjectionReplayMode.FromBeginning);
        Assert.Equal(0, replayed); // Read-model reset completed cleanly
        var postReplayCount = await context.DailyInventoryAgingSnapshots.CountAsync();
        Assert.Equal(0, postReplayCount);
    }

    [Fact]
    public async Task TenantIsolationAndFacilityAuthorization_EnforceStrictSecurityBoundaries()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Add records for Client 1 in Facility 1
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 100L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 50m, receivedAtUtc: asOfDate.AddDays(-30),
            lastMovedAtUtc: asOfDate.AddDays(-30), lastDemandedAtUtc: asOfDate.AddDays(-30),
            expirationDateUtc: null, unitCost: 100m);

        // Add records for Client 2 in Facility 1
        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 101L, clientId: fix.Client2Id, facilityId: fix.FacilityId,
            locationId: fix.Location2Id, locationCode: "B-02-01-A", itemId: fix.Item2Id, sku: "SKU-PROD-B",
            snapshotDateUtc: asOfDate, quantityOnHand: 75m, receivedAtUtc: asOfDate.AddDays(-40),
            lastMovedAtUtc: asOfDate.AddDays(-40), lastDemandedAtUtc: asOfDate.AddDays(-40),
            expirationDateUtc: null, unitCost: 80m);

        // Client 1 User Context
        var client1Context = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = fix.Client1Id,
            UserType = UserType.Client
        };
        var client1QueryService = new AnalyticsQueryService(context, client1Context, NullLogger<AnalyticsQueryService>.Instance);

        // Client 1 queries own aging data -> Success
        var client1Records = await client1QueryService.GetDailyInventoryAgingRecordsAsync(fix.FacilityId, asOfDate, fix.Client1Id);
        Assert.Single(client1Records);
        Assert.Equal("SKU-PROD-A", client1Records[0].Sku);

        // Client 1 attempting to query Client 2 aging data -> Throws UnauthorizedException (403)
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            client1QueryService.GetDailyInventoryAgingRecordsAsync(fix.FacilityId, asOfDate, fix.Client2Id));

        // Client 1 attempting to query Client 2 dead stock -> Throws UnauthorizedException (403)
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            client1QueryService.GetDeadStockAnalysisAsync(fix.FacilityId, fix.Client2Id, asOfDate));

        // Facility-restricted supervisor attempting to access another facility -> Throws UnauthorizedException (403)
        var facilitySupervisorContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            FacilityId = fix.FacilityId
        };
        var facSupervisorQueryService = new AnalyticsQueryService(context, facilitySupervisorContext, NullLogger<AnalyticsQueryService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            facSupervisorQueryService.GetDailyInventoryAgingRecordsAsync(fix.Facility2Id, asOfDate));
    }

    [Fact]
    public async Task WebApiController_InventoryAgingEndpoints_ReturnExpectedContracts()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var asOfDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        await projService.ProcessDailyInventoryAgingSnapshotAsync(
            eventId: Guid.NewGuid(), sequenceNumber: 110L, clientId: fix.Client1Id, facilityId: fix.FacilityId,
            locationId: fix.Location1Id, locationCode: "A-01-01-A", itemId: fix.Item1Id, sku: "SKU-PROD-A",
            snapshotDateUtc: asOfDate, quantityOnHand: 50m, receivedAtUtc: asOfDate.AddDays(-400),
            lastMovedAtUtc: asOfDate.AddDays(-400), lastDemandedAtUtc: asOfDate.AddDays(-400),
            expirationDateUtc: null, unitCost: 100m, lotId: Guid.NewGuid(), lotNumber: "LOT-API-1");

        var userContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            PermissionsList = ["analytics:inventory-health:view", "analytics:inventory-aging:manage"]
        };

        var queryService = new AnalyticsQueryService(context, userContext, NullLogger<AnalyticsQueryService>.Instance);
        var controller = new InventoryAgingAnalyticsController(queryService, projService, userContext);

        // 1. Get Inventory Health Summary
        var summaryRes = await controller.GetInventoryHealthSummary(fix.FacilityId, asOfDate, fix.Client1Id, CancellationToken.None);
        var okSummary = Assert.IsType<OkObjectResult>(summaryRes);
        var summaryDto = Assert.IsType<InventoryHealthSummaryDto>(okSummary.Value);
        Assert.Equal(1, summaryDto.TotalActiveSkus);
        Assert.Equal(5000m, summaryDto.TotalInventoryValue);
        Assert.Equal(5000m, summaryDto.DeadStockValue);

        // 2. Get Daily Records
        var recordsRes = await controller.GetDailyInventoryAgingRecords(fix.FacilityId, asOfDate, fix.Client1Id, null, null, CancellationToken.None);
        var okRecords = Assert.IsType<OkObjectResult>(recordsRes);
        var recordsDto = Assert.IsAssignableFrom<IReadOnlyList<DailyInventoryAgingRecordDto>>(okRecords.Value);
        Assert.Single(recordsDto);

        // 3. Get Lots
        var lotsRes = await controller.GetLotAgingBreakdown(fix.FacilityId, fix.Item1Id, asOfDate, CancellationToken.None);
        var okLots = Assert.IsType<OkObjectResult>(lotsRes);
        var lotsDto = Assert.IsAssignableFrom<IReadOnlyList<LotAgingRecordDto>>(okLots.Value);
        Assert.Single(lotsDto);
        Assert.Equal("LOT-API-1", lotsDto[0].LotNumber);

        // 4. Get Dead Stock
        var deadRes = await controller.GetDeadStockAnalysis(fix.FacilityId, asOfDate, fix.Client1Id, CancellationToken.None);
        var okDead = Assert.IsType<OkObjectResult>(deadRes);
        var deadDto = Assert.IsType<DeadStockAnalysisDto>(okDead.Value);
        Assert.Equal(1, deadDto.TotalDeadStockSkus);

        // 5. Upsert Policy
        var upsertReq = new UpsertInventoryAgingPolicyRequest
        {
            Name = "API Custom Policy",
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            SlowMovingThresholdDays = 60,
            DormantThresholdDays = 120,
            DeadStockThresholdDays = 240,
            NearExpiryThresholdDays = 45,
            CriticalExpiryThresholdDays = 15,
            Currency = "USD",
            DailyHoldingCostPerCbm = 0.08m
        };

        var policyRes = await controller.UpsertInventoryAgingPolicy(upsertReq, CancellationToken.None);
        var okPolicy = Assert.IsType<OkObjectResult>(policyRes);
        var policyDto = Assert.IsType<InventoryAgingPolicyDto>(okPolicy.Value);
        Assert.Equal("API Custom Policy", policyDto.Name);
        Assert.Equal(60, policyDto.SlowMovingThresholdDays);
        Assert.Equal(240, policyDto.DeadStockThresholdDays);
    }
}
