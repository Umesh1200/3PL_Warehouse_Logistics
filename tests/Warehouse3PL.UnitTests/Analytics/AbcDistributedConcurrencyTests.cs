using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Dedicated integration and concurrency test suite for Phase 6.3.5:
/// ABC Inventory Classification Engine - Database-Enforced Concurrency Boundary.
/// Verifies the 7 user-mandated distributed concurrency and version allocation scenarios:
/// 1. Concurrent requests from two application service instances -> Unique sequential portfolio versions.
/// 2. Concurrent requests using separate database contexts -> No duplicate version.
/// 3. Application restart between classification runs -> Version history remains correct.
/// 4. Database conflict during allocation -> Safe retry or transaction rollback.
/// 5. Same client/facility/policy scope -> Serialized sequential version allocation.
/// 6. Different portfolio scopes -> Independent concurrent classification.
/// 7. Failed classification transaction cannot leave a partially persisted portfolio run.
/// </summary>
public class AbcDistributedConcurrencyTests
{
    private class ConcurrencyFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid Facility1Id { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid Client1Id { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();

        public Guid Item1Id { get; set; } = Guid.NewGuid();
        public Guid Item2Id { get; set; } = Guid.NewGuid();
        public Guid Item3Id { get; set; } = Guid.NewGuid();
        public Guid Item4Id { get; set; } = Guid.NewGuid();
    }

    private static ApplicationDbContext CreateContext(string dbName)
    {
        return TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);
    }

    private static async Task<ConcurrencyFixture> SeedConcurrencyEnvironmentAsync()
    {
        var fix = new ConcurrencyFixture();
        using var context = CreateContext(fix.DbName);

        var fac1 = new Facility
        {
            Id = fix.Facility1Id,
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
            AccountCode = "CLI-ALPHA",
            CompanyName = "Alpha Corp",
            Status = ClientStatus.Active
        };

        var client2 = new Client
        {
            Id = fix.Client2Id,
            AccountCode = "CLI-BETA",
            CompanyName = "Beta Corp",
            Status = ClientStatus.Active
        };

        var item1 = new Item
        {
            Id = fix.Item1Id,
            ClientId = fix.Client1Id,
            Sku = "SKU-HOT-01",
            Description = "High Velocity CPU",
            UnitPrice = 500m,
            Status = ItemStatus.Active
        };

        var item2 = new Item
        {
            Id = fix.Item2Id,
            ClientId = fix.Client1Id,
            Sku = "SKU-MED-02",
            Description = "Medium Velocity RAM",
            UnitPrice = 150m,
            Status = ItemStatus.Active
        };

        var item3 = new Item
        {
            Id = fix.Item3Id,
            ClientId = fix.Client1Id,
            Sku = "SKU-LOW-03",
            Description = "Low Velocity Cable",
            UnitPrice = 20m,
            Status = ItemStatus.Active
        };

        var item4 = new Item
        {
            Id = fix.Item4Id,
            ClientId = fix.Client2Id,
            Sku = "SKU-FAC2-01",
            Description = "Facility 2 Widget",
            UnitPrice = 300m,
            Status = ItemStatus.Active
        };

        await context.Facilities.AddRangeAsync(fac1, fac2);
        await context.Clients.AddRangeAsync(client1, client2);
        await context.Items.AddRangeAsync(item1, item2, item3, item4);

        // Seed stock on hand
        await context.StockOnHand.AddRangeAsync(
            new StockOnHand { FacilityId = fix.Facility1Id, ClientId = fix.Client1Id, ItemId = fix.Item1Id, LocationId = Guid.NewGuid(), QtyAvailable = 50m },
            new StockOnHand { FacilityId = fix.Facility1Id, ClientId = fix.Client1Id, ItemId = fix.Item2Id, LocationId = Guid.NewGuid(), QtyAvailable = 100m },
            new StockOnHand { FacilityId = fix.Facility1Id, ClientId = fix.Client1Id, ItemId = fix.Item3Id, LocationId = Guid.NewGuid(), QtyAvailable = 200m },
            new StockOnHand { FacilityId = fix.Facility2Id, ClientId = fix.Client2Id, ItemId = fix.Item4Id, LocationId = Guid.NewGuid(), QtyAvailable = 75m }
        );

        // Seed movement logs
        var now = DateTime.UtcNow;
        for (int i = 0; i < 20; i++)
        {
            await context.InventoryMovementEventLogs.AddAsync(InventoryMovementEventLog.Create(
                Guid.NewGuid(), fix.Facility1Id, fix.Client1Id, fix.Item1Id, "SKU-HOT-01",
                InventoryMovementEventType.OutboundPick, 5m, now.AddDays(-i - 1),
                unitCost: 500m, referenceNumber: $"ORD-CONC-1-{i}", isCanonicalDemand: true));

            await context.InventoryMovementEventLogs.AddAsync(InventoryMovementEventLog.Create(
                Guid.NewGuid(), fix.Facility1Id, fix.Client1Id, fix.Item2Id, "SKU-MED-02",
                InventoryMovementEventType.OutboundPick, 2m, now.AddDays(-i - 1),
                unitCost: 150m, referenceNumber: $"ORD-CONC-2-{i}", isCanonicalDemand: true));
        }

        for (int i = 0; i < 10; i++)
        {
            await context.InventoryMovementEventLogs.AddAsync(InventoryMovementEventLog.Create(
                Guid.NewGuid(), fix.Facility2Id, fix.Client2Id, fix.Item4Id, "SKU-FAC2-01",
                InventoryMovementEventType.OutboundPick, 3m, now.AddDays(-i - 1),
                unitCost: 300m, referenceNumber: $"ORD-FAC2-{i}", isCanonicalDemand: true));
        }

        await context.SaveChangesAsync();
        return fix;
    }

    /// <summary>
    /// Scenario 1: Concurrent requests from two separate application service instances
    /// targeting the same scope allocate unique sequential versions (e.g. 1 and 2).
    /// </summary>
    [Fact]
    public async Task Scenario1_ConcurrentRequests_TwoServiceInstances_AllocatesUniqueSequentialVersions()
    {
        var fix = await SeedConcurrencyEnvironmentAsync();

        using var context1 = CreateContext(fix.DbName);
        using var context2 = CreateContext(fix.DbName);

        var service1 = new AnalyticsProjectionService(context1, NullLogger<AnalyticsProjectionService>.Instance);
        var service2 = new AnalyticsProjectionService(context2, NullLogger<AnalyticsProjectionService>.Instance);

        var task1 = service1.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id,
            criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue,
            windowOverride: VelocityObservationWindow.Days90);
        var task2 = service2.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id,
            criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue,
            windowOverride: VelocityObservationWindow.Days90);

        var results = await Task.WhenAll(task1, task2);

        var v1 = results[0].Summary.Version;
        var v2 = results[1].Summary.Version;

        Assert.NotEqual(v1, v2);
        var versions = new[] { v1, v2 }.OrderBy(x => x).ToList();
        Assert.Equal(1, versions[0]);
        Assert.Equal(2, versions[1]);

        using var verifyContext = CreateContext(fix.DbName);
        var scopeRecord = await verifyContext.AbcClassificationScopeVersions
            .FirstOrDefaultAsync(v => v.FacilityId == fix.Facility1Id &&
                                      v.ClientId == fix.Client1Id &&
                                      v.Criteria == AbcClassificationCriteria.AnnualConsumptionValue &&
                                      v.ObservationWindow == VelocityObservationWindow.Days90);

        Assert.NotNull(scopeRecord);
        Assert.Equal(2, scopeRecord.LatestVersion);
    }

    /// <summary>
    /// Scenario 2: Concurrent requests using separate database contexts guarantee that
    /// SkuVelocityClassificationRecords contains no duplicate version for any item.
    /// </summary>
    [Fact]
    public async Task Scenario2_ConcurrentRequests_SeparateDatabaseContexts_NoDuplicateVersion()
    {
        var fix = await SeedConcurrencyEnvironmentAsync();

        using var context1 = CreateContext(fix.DbName);
        using var context2 = CreateContext(fix.DbName);
        using var context3 = CreateContext(fix.DbName);

        var service1 = new AnalyticsProjectionService(context1, NullLogger<AnalyticsProjectionService>.Instance);
        var service2 = new AnalyticsProjectionService(context2, NullLogger<AnalyticsProjectionService>.Instance);
        var service3 = new AnalyticsProjectionService(context3, NullLogger<AnalyticsProjectionService>.Instance);

        var task1 = service1.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id,
            criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue,
            windowOverride: VelocityObservationWindow.Days90);
        var task2 = service2.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id,
            criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue,
            windowOverride: VelocityObservationWindow.Days90);
        var task3 = service3.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id,
            criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue,
            windowOverride: VelocityObservationWindow.Days90);

        var results = await Task.WhenAll(task1, task2, task3);

        var allocatedVersions = results.Select(r => r.Summary.Version).OrderBy(v => v).ToList();
        Assert.Equal([1, 2, 3], allocatedVersions);

        using var verifyContext = CreateContext(fix.DbName);
        var records = await verifyContext.SkuVelocityClassificationRecords
            .Where(r => r.FacilityId == fix.Facility1Id &&
                        r.ClientId == fix.Client1Id &&
                        r.RecordType == SkuClassificationRecordType.AbcParetoStratification &&
                        r.CriteriaUsed == AbcClassificationCriteria.AnnualConsumptionValue &&
                        r.ObservationWindow == VelocityObservationWindow.Days90)
            .ToListAsync();

        // Verify that per item, versions are strictly unique (no duplicates)
        var groupedByItem = records.GroupBy(r => r.ItemId);
        foreach (var group in groupedByItem)
        {
            var itemVersions = group.Select(r => r.Version).ToList();
            Assert.Equal(itemVersions.Distinct().Count(), itemVersions.Count);
        }
    }

    /// <summary>
    /// Scenario 3: Application restart between classification runs preserves version history
    /// across service and context lifetimes (Pod 1 runs version 1, restarts, Pod 2 runs version 2).
    /// </summary>
    [Fact]
    public async Task Scenario3_ApplicationRestart_BetweenClassificationRuns_VersionHistoryRemainsCorrect()
    {
        var fix = await SeedConcurrencyEnvironmentAsync();

        // First application lifecycle: Pod 1 runs batch
        using (var initialContext = CreateContext(fix.DbName))
        {
            var initialService = new AnalyticsProjectionService(initialContext, NullLogger<AnalyticsProjectionService>.Instance);
            var initialResult = await initialService.CalculateAbcClassificationBatchAsync(
                fix.Facility1Id, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue);

            Assert.Equal(1, initialResult.Summary.Version);
        }

        // Simulate complete application pod restart:
        // Service instance and DB context are disposed. Fresh instance instantiated.
        using (var restartedContext = CreateContext(fix.DbName))
        {
            var restartedService = new AnalyticsProjectionService(restartedContext, NullLogger<AnalyticsProjectionService>.Instance);
            var restartedResult = await restartedService.CalculateAbcClassificationBatchAsync(
                fix.Facility1Id, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue);

            Assert.Equal(2, restartedResult.Summary.Version);

            var scopeVersion = await restartedContext.AbcClassificationScopeVersions
                .FirstOrDefaultAsync(v => v.FacilityId == fix.Facility1Id &&
                                          v.ClientId == fix.Client1Id &&
                                          v.Criteria == AbcClassificationCriteria.AnnualConsumptionValue);

            Assert.NotNull(scopeVersion);
            Assert.Equal(2, scopeVersion.LatestVersion);
        }
    }

    /// <summary>
    /// Scenario 4: Database conflict during allocation triggers safe optimistic concurrency retry
    /// and ensures both conflicting requests succeed without throwing ConcurrencyException.
    /// </summary>
    [Fact]
    public async Task Scenario4_DatabaseConflict_DuringVersionAllocation_SafelyRetriesAndSucceeds()
    {
        var fix = await SeedConcurrencyEnvironmentAsync();

        // Pre-seed scope version record with LatestVersion = 5
        using (var seedContext = CreateContext(fix.DbName))
        {
            var existingScope = AbcClassificationScopeVersion.Create(
                fix.Facility1Id, fix.Client1Id, AbcClassificationCriteria.AnnualConsumptionValue,
                VelocityObservationWindow.Days90, initialVersion: 5);
            await seedContext.AbcClassificationScopeVersions.AddAsync(existingScope);
            await seedContext.SaveChangesAsync();
        }

        using var context1 = CreateContext(fix.DbName);
        using var context2 = CreateContext(fix.DbName);

        var service1 = new AnalyticsProjectionService(context1, NullLogger<AnalyticsProjectionService>.Instance);
        var service2 = new AnalyticsProjectionService(context2, NullLogger<AnalyticsProjectionService>.Instance);

        var task1 = service1.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id,
            criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue,
            windowOverride: VelocityObservationWindow.Days90);
        var task2 = service2.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id,
            criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue,
            windowOverride: VelocityObservationWindow.Days90);

        var results = await Task.WhenAll(task1, task2);

        var v1 = results[0].Summary.Version;
        var v2 = results[1].Summary.Version;

        Assert.NotEqual(v1, v2);
        var versions = new[] { v1, v2 }.OrderBy(x => x).ToList();
        Assert.Equal(6, versions[0]);
        Assert.Equal(7, versions[1]);

        using var verifyContext = CreateContext(fix.DbName);
        var scopeRecord = await verifyContext.AbcClassificationScopeVersions
            .FirstOrDefaultAsync(v => v.FacilityId == fix.Facility1Id &&
                                      v.ClientId == fix.Client1Id &&
                                      v.Criteria == AbcClassificationCriteria.AnnualConsumptionValue &&
                                      v.ObservationWindow == VelocityObservationWindow.Days90);

        Assert.NotNull(scopeRecord);
        Assert.Equal(7, scopeRecord.LatestVersion);
    }

    /// <summary>
    /// Scenario 5: Multiple classification runs on the exact same client/facility/policy scope
    /// allocate strictly serialized, gapless, sequential versions (1, 2, 3).
    /// </summary>
    [Fact]
    public async Task Scenario5_SameScope_ConcurrentOrSerialRuns_AllocatesSequentialVersions()
    {
        var fix = await SeedConcurrencyEnvironmentAsync();

        using var context = CreateContext(fix.DbName);
        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        var result1 = await service.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.PickFrequency);
        var result2 = await service.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.PickFrequency);
        var result3 = await service.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.PickFrequency);

        Assert.Equal(1, result1.Summary.Version);
        Assert.Equal(2, result2.Summary.Version);
        Assert.Equal(3, result3.Summary.Version);

        using var verifyContext = CreateContext(fix.DbName);
        var scopeRecord = await verifyContext.AbcClassificationScopeVersions
            .FirstOrDefaultAsync(v => v.FacilityId == fix.Facility1Id &&
                                      v.ClientId == fix.Client1Id &&
                                      v.Criteria == AbcClassificationCriteria.PickFrequency);

        Assert.NotNull(scopeRecord);
        Assert.Equal(3, scopeRecord.LatestVersion);
    }

    /// <summary>
    /// Scenario 6: Different portfolio scopes (Facility 1 vs Facility 2) run independently
    /// and concurrently, each starting with Version 1 without blocking or conflicting.
    /// </summary>
    [Fact]
    public async Task Scenario6_DifferentScopes_ConcurrentRuns_IndependentVersionAllocations()
    {
        var fix = await SeedConcurrencyEnvironmentAsync();

        using var context1 = CreateContext(fix.DbName);
        using var context2 = CreateContext(fix.DbName);

        var service1 = new AnalyticsProjectionService(context1, NullLogger<AnalyticsProjectionService>.Instance);
        var service2 = new AnalyticsProjectionService(context2, NullLogger<AnalyticsProjectionService>.Instance);

        // Scope 1: Facility 1, Client 1
        var task1 = service1.CalculateAbcClassificationBatchAsync(
            fix.Facility1Id, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue);

        // Scope 2: Facility 2, Client 2
        var task2 = service2.CalculateAbcClassificationBatchAsync(
            fix.Facility2Id, fix.Client2Id, criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue);

        var results = await Task.WhenAll(task1, task2);

        // Both independent scopes start at Version 1
        Assert.Equal(1, results[0].Summary.Version);
        Assert.Equal(1, results[1].Summary.Version);

        using var verifyContext = CreateContext(fix.DbName);
        var scope1 = await verifyContext.AbcClassificationScopeVersions
            .FirstOrDefaultAsync(v => v.FacilityId == fix.Facility1Id && v.ClientId == fix.Client1Id);
        var scope2 = await verifyContext.AbcClassificationScopeVersions
            .FirstOrDefaultAsync(v => v.FacilityId == fix.Facility2Id && v.ClientId == fix.Client2Id);

        Assert.NotNull(scope1);
        Assert.NotNull(scope2);
        Assert.NotEqual(scope1.Id, scope2.Id);
        Assert.Equal(1, scope1.LatestVersion);
        Assert.Equal(1, scope2.LatestVersion);
    }

    /// <summary>
    /// Scenario 7: A failed classification transaction rolls back and leaves no partially
    /// persisted records in SkuVelocityClassificationRecords.
    /// </summary>
    [Fact]
    public async Task Scenario7_FailedClassificationTransaction_RollsBackAndLeavesNoPartiallyPersistedRun()
    {
        var fix = await SeedConcurrencyEnvironmentAsync();

        using var context = CreateContext(fix.DbName);
        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token triggers OperationCanceledException during execution

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.CalculateAbcClassificationBatchAsync(
                fix.Facility1Id, fix.Client1Id,
                criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue,
                cancellationToken: cts.Token);
        });

        // Verify that zero records were persisted
        using var verifyContext = CreateContext(fix.DbName);
        var persistedRecordsCount = await verifyContext.SkuVelocityClassificationRecords
            .CountAsync(r => r.FacilityId == fix.Facility1Id &&
                             r.ClientId == fix.Client1Id &&
                             r.RecordType == SkuClassificationRecordType.AbcParetoStratification);

        Assert.Equal(0, persistedRecordsCount);
    }
}
