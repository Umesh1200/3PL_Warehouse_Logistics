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
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Exhaustive unit and integration test suite for Phase 6.3.5:
/// ABC Inventory Classification Engine & Pareto Stratification.
/// Verifies:
/// 1. Policy domain modeling, validation invariants, and configurable cutoffs.
/// 2. Annual Consumption Value (ACV) calculation engine.
/// 3. SKU ranking and cumulative Pareto contribution with deterministic tie-breaking.
/// 4. Configurable threshold classification (Class A / B / C).
/// 5. Multi-criteria classification (ACV, Pick Frequency, Unit Velocity, Order Lines, Weighted Composite).
/// 6. Classification versioning, audit trail, and cross-run SKU migration analysis.
/// 7. Facility-specific and client-specific scoping without tenant leakage.
/// 8. Authoritative inventory ledger immutability (strictly advisory intelligence).
/// 9. Multi-tenant and facility RBAC authorization boundaries.
/// 10. WebAPI controller contracts.
/// </summary>
public class AbcInventoryClassificationTests
{
    private class TestFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid Client1Id { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();

        public Guid ItemAId { get; set; } = Guid.NewGuid();
        public Guid ItemBId { get; set; } = Guid.NewGuid();
        public Guid ItemCId { get; set; } = Guid.NewGuid();
        public Guid ItemDId { get; set; } = Guid.NewGuid();
        public Guid ItemClient2Id { get; set; } = Guid.NewGuid();
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
            CompanyName = "Acme Corp",
            Status = ClientStatus.Active
        };

        var client2 = new Client
        {
            Id = fix.Client2Id,
            AccountCode = "CLI-ZENITH",
            CompanyName = "Zenith Retail",
            Status = ClientStatus.Active
        };

        // Items for Client 1
        var itemA = new Item
        {
            Id = fix.ItemAId,
            ClientId = fix.Client1Id,
            Sku = "SKU-AAA-01",
            Description = "High Value Server CPU",
            UnitPrice = 1000m,
            Status = ItemStatus.Active
        };

        var itemB = new Item
        {
            Id = fix.ItemBId,
            ClientId = fix.Client1Id,
            Sku = "SKU-BBB-02",
            Description = "Medium Value Memory Module",
            UnitPrice = 200m,
            Status = ItemStatus.Active
        };

        var itemC = new Item
        {
            Id = fix.ItemCId,
            ClientId = fix.Client1Id,
            Sku = "SKU-CCC-03",
            Description = "Low Value Patch Cable",
            UnitPrice = 10m,
            Status = ItemStatus.Active
        };

        var itemD = new Item
        {
            Id = fix.ItemDId,
            ClientId = fix.Client1Id,
            Sku = "SKU-DDD-04",
            Description = "Dormant Spare Fan",
            UnitPrice = 25m,
            Status = ItemStatus.Active
        };

        // Item for Client 2 (Tenant isolation test)
        var itemClient2 = new Item
        {
            Id = fix.ItemClient2Id,
            ClientId = fix.Client2Id,
            Sku = "SKU-ZZZ-99",
            Description = "Client 2 Exclusive Product",
            UnitPrice = 500m,
            Status = ItemStatus.Active
        };

        await context.Facilities.AddRangeAsync(fac1, fac2);
        await context.Clients.AddRangeAsync(client1, client2);
        await context.Items.AddRangeAsync(itemA, itemB, itemC, itemD, itemClient2);

        // Seed stock on hand for Client 1 items
        var stockA = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = fix.ItemAId,
            LocationId = Guid.NewGuid(),
            QtyAvailable = 50m,
            QtyAllocated = 0m,
            QtyReserved = 0m
        };

        var stockB = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = fix.ItemBId,
            LocationId = Guid.NewGuid(),
            QtyAvailable = 200m,
            QtyAllocated = 0m,
            QtyReserved = 0m
        };

        var stockC = new StockOnHand
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = fix.ItemCId,
            LocationId = Guid.NewGuid(),
            QtyAvailable = 1000m,
            QtyAllocated = 0m,
            QtyReserved = 0m
        };

        await context.StockOnHand.AddRangeAsync(stockA, stockB, stockC);

        // Seed Movement Event Logs over past 90 days
        var now = DateTime.UtcNow;

        // Item A: 80 units consumed @ $1000 = $80,000 ACV, 40 pick tasks
        for (int i = 0; i < 40; i++)
        {
            await context.InventoryMovementEventLogs.AddAsync(InventoryMovementEventLog.Create(
                Guid.NewGuid(), fix.FacilityId, fix.Client1Id, fix.ItemAId, "SKU-AAA-01",
                InventoryMovementEventType.OutboundPick, 2m, now.AddDays(-i - 1),
                unitCost: 1000m, referenceNumber: $"ORD-A-{i}", isCanonicalDemand: true));
        }

        // Item B: 75 units consumed @ $200 = $15,000 ACV, 15 pick tasks
        for (int i = 0; i < 15; i++)
        {
            await context.InventoryMovementEventLogs.AddAsync(InventoryMovementEventLog.Create(
                Guid.NewGuid(), fix.FacilityId, fix.Client1Id, fix.ItemBId, "SKU-BBB-02",
                InventoryMovementEventType.OutboundPick, 5m, now.AddDays(-i * 2 - 1),
                unitCost: 200m, referenceNumber: $"ORD-B-{i}", isCanonicalDemand: true));
        }

        // Item C: 500 units consumed @ $10 = $5,000 ACV, 50 pick tasks (High pick frequency, low value!)
        for (int i = 0; i < 50; i++)
        {
            await context.InventoryMovementEventLogs.AddAsync(InventoryMovementEventLog.Create(
                Guid.NewGuid(), fix.FacilityId, fix.Client1Id, fix.ItemCId, "SKU-CCC-03",
                InventoryMovementEventType.OutboundPick, 10m, now.AddDays(-i % 40 - 1),
                unitCost: 10m, referenceNumber: $"ORD-C-{i}", isCanonicalDemand: true));
        }

        // Item D: 0 demand events (dormant)

        // Client 2 Item: 50 units @ $500 = $25,000 ACV
        await context.InventoryMovementEventLogs.AddAsync(InventoryMovementEventLog.Create(
            Guid.NewGuid(), fix.FacilityId, fix.Client2Id, fix.ItemClient2Id, "SKU-ZZZ-99",
            InventoryMovementEventType.OutboundPick, 50m, now.AddDays(-5),
            unitCost: 500m, referenceNumber: "ORD-CLI2-1", isCanonicalDemand: true));

        await context.SaveChangesAsync();
        return fix;
    }

    [Fact]
    public void AbcClassificationPolicy_Create_ValidParameters_Succeeds()
    {
        var facilityId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        var policy = AbcClassificationPolicy.Create(
            name: "High Value Electronics Policy",
            facilityId: facilityId,
            clientId: clientId,
            criteria: AbcClassificationCriteria.AnnualConsumptionValue,
            classAThresholdPercent: 80.00m,
            classBThresholdPercent: 95.00m,
            observationWindowDays: 365,
            observationWindow: VelocityObservationWindow.Days365,
            costBasis: InventoryValuationBasis.UnitCost);

        Assert.NotNull(policy);
        Assert.Equal("High Value Electronics Policy", policy.Name);
        Assert.Equal(facilityId, policy.FacilityId);
        Assert.Equal(clientId, policy.ClientId);
        Assert.Equal(80.00m, policy.ClassACumulativeThresholdPercent);
        Assert.Equal(95.00m, policy.ClassBCumulativeThresholdPercent);
        Assert.Equal(365, policy.ObservationWindowDays);
        Assert.Equal(InventoryValuationBasis.UnitCost, policy.CostValuationBasis);
        Assert.True(policy.IsActive);
        Assert.Equal(1, policy.Version);
    }

    [Fact]
    public void AbcClassificationPolicy_Create_InvalidThresholds_ThrowsValidationException()
    {
        // Class A <= 0
        Assert.Throws<ValidationException>(() => AbcClassificationPolicy.Create("P1", classAThresholdPercent: 0m));

        // Class A >= 100
        Assert.Throws<ValidationException>(() => AbcClassificationPolicy.Create("P2", classAThresholdPercent: 100m));

        // Class B <= Class A
        Assert.Throws<ValidationException>(() => AbcClassificationPolicy.Create("P3", classAThresholdPercent: 80m, classBThresholdPercent: 75m));

        // Class B > 100
        Assert.Throws<ValidationException>(() => AbcClassificationPolicy.Create("P4", classAThresholdPercent: 80m, classBThresholdPercent: 105m));

        // Observation window <= 0
        Assert.Throws<ValidationException>(() => AbcClassificationPolicy.Create("P5", observationWindowDays: 0));

        // Empty Name
        Assert.Throws<ValidationException>(() => AbcClassificationPolicy.Create("   "));
    }

    [Fact]
    public void AbcClassificationPolicy_Create_WeightedComposite_ValidatesAndNormalizesWeights()
    {
        // Negative weight throws
        Assert.Throws<ValidationException>(() => AbcClassificationPolicy.Create(
            "P-Comp", criteria: AbcClassificationCriteria.WeightedComposite,
            weightAcv: -0.1m, weightPickFrequency: 0.5m, weightUnitVelocity: 0.3m, weightOrderLines: 0.3m));

        // Weights not summing to 1.0 or 100 throws
        Assert.Throws<ValidationException>(() => AbcClassificationPolicy.Create(
            "P-Comp2", criteria: AbcClassificationCriteria.WeightedComposite,
            weightAcv: 0.2m, weightPickFrequency: 0.2m, weightUnitVelocity: 0.1m, weightOrderLines: 0.1m));

        // Percentage weights (40, 30, 20, 10) normalize to 1.0
        var policy = AbcClassificationPolicy.Create(
            "P-Comp3", criteria: AbcClassificationCriteria.WeightedComposite,
            weightAcv: 40m, weightPickFrequency: 30m, weightUnitVelocity: 20m, weightOrderLines: 10m);

        Assert.Equal(0.40m, policy.WeightAcv);
        Assert.Equal(0.30m, policy.WeightPickFrequency);
        Assert.Equal(0.20m, policy.WeightUnitVelocity);
        Assert.Equal(0.10m, policy.WeightOrderLines);
    }

    [Fact]
    public void AbcClassificationPolicy_ClassifyCategory_CorrectlyStratifiesThresholds()
    {
        var policy = AbcClassificationPolicy.Create("Standard", classAThresholdPercent: 80m, classBThresholdPercent: 95m);

        Assert.Equal(AbcClassificationCategory.ClassA, policy.ClassifyCategory(50.00m));
        Assert.Equal(AbcClassificationCategory.ClassA, policy.ClassifyCategory(80.00m));
        Assert.Equal(AbcClassificationCategory.ClassB, policy.ClassifyCategory(80.01m));
        Assert.Equal(AbcClassificationCategory.ClassB, policy.ClassifyCategory(95.00m));
        Assert.Equal(AbcClassificationCategory.ClassC, policy.ClassifyCategory(95.01m));
        Assert.Equal(AbcClassificationCategory.ClassC, policy.ClassifyCategory(100.00m));
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_AnnualConsumptionValue_RanksAndClassifiesByAcv()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // Execute batch for Client 1 with 365-day observation window
        var result = await service.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue);

        Assert.NotNull(result);
        Assert.NotNull(result.Summary);
        Assert.Equal(4, result.Summary.TotalSkus); // Items A, B, C, D

        // ACV Check:
        // Item A: 80 units * $1000 = $80,000
        // Item B: 75 units * $200 = $15,000
        // Item C: 500 units * $10 = $5,000
        // Item D: 0 units = $0
        // Total Value = $100,000
        Assert.Equal(100000m, result.Summary.TotalPortfolioValue);

        // Verification of ranked SKUs:
        Assert.Equal(4, result.RankedSkus.Count);

        var rank1 = result.RankedSkus[0];
        Assert.Equal("SKU-AAA-01", rank1.Sku);
        Assert.Equal(1, rank1.Rank);
        Assert.Equal(AbcClassificationCategory.ClassA, rank1.AbcClass);
        Assert.Equal(80000m, rank1.AnnualConsumptionValue);
        Assert.Equal(80.00m, rank1.CumulativeContributionPercent);

        var rank2 = result.RankedSkus[1];
        Assert.Equal("SKU-BBB-02", rank2.Sku);
        Assert.Equal(2, rank2.Rank);
        Assert.Equal(AbcClassificationCategory.ClassB, rank2.AbcClass);
        Assert.Equal(15000m, rank2.AnnualConsumptionValue);
        Assert.Equal(95.00m, rank2.CumulativeContributionPercent);

        var rank3 = result.RankedSkus[2];
        Assert.Equal("SKU-CCC-03", rank3.Sku);
        Assert.Equal(3, rank3.Rank);
        Assert.Equal(AbcClassificationCategory.ClassC, rank3.AbcClass);
        Assert.Equal(5000m, rank3.AnnualConsumptionValue);
        Assert.Equal(100.00m, rank3.CumulativeContributionPercent);

        var rank4 = result.RankedSkus[3];
        Assert.Equal("SKU-DDD-04", rank4.Sku);
        Assert.Equal(4, rank4.Rank);
        Assert.Equal(AbcClassificationCategory.ClassC, rank4.AbcClass);
        Assert.Equal(0m, rank4.AnnualConsumptionValue);
        Assert.Equal(100.00m, rank4.CumulativeContributionPercent);

        // Verify records persisted to SkuVelocityClassificationRecords
        var persistedRecords = await context.SkuVelocityClassificationRecords
            .Where(r => r.FacilityId == fix.FacilityId && r.ClientId == fix.Client1Id)
            .ToListAsync();

        Assert.Equal(4, persistedRecords.Count);
        Assert.All(persistedRecords, r => Assert.True(r.RankInPortfolio > 0));
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_DeterministicTieBreaking_AlphabeticalBySku()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // Add 2 new items with identical demand: Item T2 (SKU-TIE-2) and Item T1 (SKU-TIE-1)
        var itemT2 = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.Client1Id,
            Sku = "SKU-TIE-B",
            Description = "Tie Item B",
            UnitPrice = 50m,
            Status = ItemStatus.Active
        };
        var itemT1 = new Item
        {
            Id = Guid.NewGuid(),
            ClientId = fix.Client1Id,
            Sku = "SKU-TIE-A",
            Description = "Tie Item A",
            UnitPrice = 50m,
            Status = ItemStatus.Active
        };
        await context.Items.AddRangeAsync(itemT2, itemT1);

        // Both have exact same demand: 10 units @ $50 = $500
        var now = DateTime.UtcNow;
        await context.InventoryMovementEventLogs.AddAsync(InventoryMovementEventLog.Create(
            Guid.NewGuid(), fix.FacilityId, fix.Client1Id, itemT2.Id, "SKU-TIE-B",
            InventoryMovementEventType.OutboundPick, 10m, now.AddDays(-2), unitCost: 50m, isCanonicalDemand: true));

        await context.InventoryMovementEventLogs.AddAsync(InventoryMovementEventLog.Create(
            Guid.NewGuid(), fix.FacilityId, fix.Client1Id, itemT1.Id, "SKU-TIE-A",
            InventoryMovementEventType.OutboundPick, 10m, now.AddDays(-2), unitCost: 50m, isCanonicalDemand: true));

        await context.SaveChangesAsync();

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        var result = await service.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue);

        // Find the two tie items in ranked list
        var tieA = result.RankedSkus.First(s => s.Sku == "SKU-TIE-A");
        var tieB = result.RankedSkus.First(s => s.Sku == "SKU-TIE-B");

        Assert.Equal(500m, tieA.AnnualConsumptionValue);
        Assert.Equal(500m, tieB.AnnualConsumptionValue);

        // SKU-TIE-A must be ranked immediately before SKU-TIE-B due to alphabetical secondary sort
        Assert.True(tieA.Rank < tieB.Rank);
        Assert.Equal(tieA.Rank + 1, tieB.Rank);
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_TopSkuExceedingClassAThreshold_ClassifiedAsClassA()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // Upsert policy where Class A threshold is strictly 70.00%.
        // Item A accounts for 80.00% of total value, which exceeds the 70.00% threshold.
        // It must STILL be classified as Class A because it is the top rank Pareto contributor!
        var policy = await service.UpsertAbcClassificationPolicyAsync(new UpsertAbcClassificationPolicyRequest
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            Name = "Strict 70% Class A Policy",
            ClassACumulativeThresholdPercent = 70.00m,
            ClassBCumulativeThresholdPercent = 95.00m,
            Criteria = AbcClassificationCriteria.AnnualConsumptionValue
        });

        var result = await service.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client1Id, policyId: policy.Id, criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue);

        var topItem = result.RankedSkus.First(s => s.Rank == 1);
        Assert.Equal("SKU-AAA-01", topItem.Sku);
        Assert.Equal(AbcClassificationCategory.ClassA, topItem.AbcClass);
        Assert.Equal(80.00m, topItem.CumulativeContributionPercent);
        Assert.True(topItem.CumulativeContributionPercent > 70.00m);
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_ZeroDemandPortfolio_ClassifiedAsClassCWithoutDivideByZero()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // Facility 2 has no movements seeded for Client 1
        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        var result = await service.CalculateAbcClassificationBatchAsync(
            fix.Facility2Id, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.AnnualConsumptionValue);

        Assert.NotNull(result);
        Assert.Equal(0m, result.Summary.TotalPortfolioValue);
        Assert.Equal(4, result.Summary.TotalSkus);
        Assert.Equal(4, result.Summary.ClassCCount);
        Assert.Equal(0, result.Summary.ClassACount);
        Assert.Equal(0, result.Summary.ClassBCount);

        // All items should have 0% cumulative and Class C in zero-value portfolio
        Assert.False(result.Summary.HasPositiveConsumptionValue);
        Assert.Equal("NoPositiveConsumptionValue", result.Summary.PortfolioConsumptionStatus);
        Assert.All(result.RankedSkus, sku =>
        {
            Assert.Equal(AbcClassificationCategory.ClassC, sku.AbcClass);
            Assert.Equal(0.00m, sku.CumulativeContributionPercent);
        });
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_PickFrequencyCriteria_StratifiesByPicks()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // Pick Frequency Criteria:
        // Item C has 100 picks ($2,000 ACV)
        // Item A has 50 picks ($100,000 ACV)
        // Item B has 15 picks ($15,000 ACV)
        // Item D has 0 picks ($0 ACV)
        var result = await service.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.PickFrequency);

        Assert.Equal(4, result.RankedSkus.Count);

        // Under PickFrequency criteria, Item C must be Rank 1!
        var rank1 = result.RankedSkus[0];
        Assert.Equal("SKU-CCC-03", rank1.Sku);
        Assert.Equal(1, rank1.Rank);
        Assert.Equal(50, rank1.TotalPickTasksCount);
        Assert.Equal(AbcClassificationCategory.ClassA, rank1.AbcClass);

        var rank2 = result.RankedSkus[1];
        Assert.Equal("SKU-AAA-01", rank2.Sku);
        Assert.Equal(2, rank2.Rank);
        Assert.Equal(40, rank2.TotalPickTasksCount);

        var rank3 = result.RankedSkus[2];
        Assert.Equal("SKU-BBB-02", rank3.Sku);
        Assert.Equal(3, rank3.Rank);
        Assert.Equal(15, rank3.TotalPickTasksCount);

        var rank4 = result.RankedSkus[3];
        Assert.Equal("SKU-DDD-04", rank4.Sku);
        Assert.Equal(4, rank4.Rank);
        Assert.Equal(0, rank4.TotalPickTasksCount);
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_WeightedCompositeCriteria_ComputesNormalizedScore()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // Create policy with WeightedComposite (50% ACV, 50% Picks)
        await service.UpsertAbcClassificationPolicyAsync(new UpsertAbcClassificationPolicyRequest
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            Name = "50-50 Value and Picks Policy",
            Criteria = AbcClassificationCriteria.WeightedComposite,
            WeightAcv = 0.50m,
            WeightPickFrequency = 0.50m,
            WeightUnitVelocity = 0.00m,
            WeightOrderLines = 0.00m,
            ClassACumulativeThresholdPercent = 80m,
            ClassBCumulativeThresholdPercent = 95m
        });

        var result = await service.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.WeightedComposite);

        Assert.All(result.RankedSkus, sku =>
        {
            Assert.True(sku.CompositeScore >= 0m);
        });

        // Top ranked SKU should have high composite score
        Assert.True(result.RankedSkus[0].CompositeScore > 50m);
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_ClientIsolation_ExcludesOtherTenantData()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // Run batch specifically for Client 1
        var resultClient1 = await service.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client1Id);

        // None of the returned SKUs should belong to Client 2
        Assert.DoesNotContain(resultClient1.RankedSkus, s => s.ItemId == fix.ItemClient2Id);
        Assert.DoesNotContain(resultClient1.RankedSkus, s => s.Sku == "SKU-ZZZ-99");

        // Run batch specifically for Client 2
        var resultClient2 = await service.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client2Id);

        Assert.Single(resultClient2.RankedSkus);
        Assert.Equal("SKU-ZZZ-99", resultClient2.RankedSkus[0].Sku);
        Assert.Equal(fix.ItemClient2Id, resultClient2.RankedSkus[0].ItemId);
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_AdvisoryOnly_DoesNotMutateStockOnHandOrLocations()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // Snapshot authoritative stock records prior to calculation
        var stockBefore = await context.StockOnHand.AsNoTracking()
            .Where(s => s.FacilityId == fix.FacilityId)
            .ToListAsync();

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        await service.CalculateAbcClassificationBatchAsync(fix.FacilityId, fix.Client1Id);

        // Verify authoritative stock records remain 100% identical after calculation
        var stockAfter = await context.StockOnHand.AsNoTracking()
            .Where(s => s.FacilityId == fix.FacilityId)
            .ToListAsync();

        Assert.Equal(stockBefore.Count, stockAfter.Count);
        for (int i = 0; i < stockBefore.Count; i++)
        {
            Assert.Equal(stockBefore[i].QtyAvailable, stockAfter[i].QtyAvailable);
            Assert.Equal(stockBefore[i].QtyAllocated, stockAfter[i].QtyAllocated);
            Assert.Equal(stockBefore[i].LocationId, stockAfter[i].LocationId);
            Assert.Equal(stockBefore[i].QtyReserved, stockAfter[i].QtyReserved);
        }
    }

    [Fact]
    public async Task GetAbcMigrationAnalysis_DetectsPromotionsDemotionsAndUnchanged()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var now = DateTime.UtcNow;

        // Seed Version 1 classifications
        var v1ItemA = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.ItemAId, "SKU-AAA-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.FastMoving, now.AddDays(-60), now.AddDays(-30),
            50, 100m, 100000m, 50, 80m, version: 1, rankInPortfolio: 1,
            recordType: SkuClassificationRecordType.AbcParetoStratification);

        var v1ItemB = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.ItemBId, "SKU-BBB-02",
            AbcClassificationCategory.ClassB, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.MediumMoving, now.AddDays(-60), now.AddDays(-30),
            20, 40m, 8000m, 20, 95m, version: 1, rankInPortfolio: 2,
            recordType: SkuClassificationRecordType.AbcParetoStratification);

        var v1ItemC = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.ItemCId, "SKU-CCC-03",
            AbcClassificationCategory.ClassC, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.SlowMoving, now.AddDays(-60), now.AddDays(-30),
            5, 10m, 100m, 5, 100m, version: 1, rankInPortfolio: 3,
            recordType: SkuClassificationRecordType.AbcParetoStratification);

        // Seed Version 2 classifications:
        // Item A: Class A -> Class B (Demoted, rank 1 -> 2)
        var v2ItemA = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.ItemAId, "SKU-AAA-01",
            AbcClassificationCategory.ClassB, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.MediumMoving, now.AddDays(-30), now,
            20, 30m, 30000m, 20, 92m, version: 2, rankInPortfolio: 2,
            recordType: SkuClassificationRecordType.AbcParetoStratification);

        // Item B: Class B -> Class A (Promoted, rank 2 -> 1)
        var v2ItemB = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.ItemBId, "SKU-BBB-02",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.FastMoving, now.AddDays(-30), now,
            100, 300m, 60000m, 100, 75m, version: 2, rankInPortfolio: 1,
            recordType: SkuClassificationRecordType.AbcParetoStratification);

        // Item C: Class C -> Class C (Unchanged, rank 3 -> 3)
        var v2ItemC = SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.ItemCId, "SKU-CCC-03",
            AbcClassificationCategory.ClassC, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.SlowMoving, now.AddDays(-30), now,
            4, 8m, 80m, 4, 100m, version: 2, rankInPortfolio: 3,
            recordType: SkuClassificationRecordType.AbcParetoStratification);

        await context.SkuVelocityClassificationRecords.AddRangeAsync(v1ItemA, v1ItemB, v1ItemC, v2ItemA, v2ItemB, v2ItemC);
        await context.SaveChangesAsync();

        var queryService = new AnalyticsQueryService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff },
            NullLogger<AnalyticsQueryService>.Instance);

        var migration = await queryService.GetAbcMigrationAnalysisAsync(fix.FacilityId, fix.Client1Id, 1, 2);

        Assert.NotNull(migration);
        Assert.Equal(1, migration.BaseVersion);
        Assert.Equal(2, migration.TargetVersion);
        Assert.Equal(3, migration.TotalEvaluatedSkus);
        Assert.Equal(1, migration.PromotionsCount);
        Assert.Equal(1, migration.DemotionsCount);
        Assert.Equal(1, migration.UnchangedCount);

        var itemBMig = migration.Migrations.First(m => m.Sku == "SKU-BBB-02");
        Assert.Equal("Promoted", itemBMig.MigrationType);
        Assert.Equal(AbcClassificationCategory.ClassB, itemBMig.BaseClass);
        Assert.Equal(AbcClassificationCategory.ClassA, itemBMig.TargetClass);
        Assert.Equal(1, itemBMig.RankDelta); // 2 - 1 = +1 improvement

        var itemAMig = migration.Migrations.First(m => m.Sku == "SKU-AAA-01");
        Assert.Equal("Demoted", itemAMig.MigrationType);
        Assert.Equal(AbcClassificationCategory.ClassA, itemAMig.BaseClass);
        Assert.Equal(AbcClassificationCategory.ClassB, itemAMig.TargetClass);
        Assert.Equal(-1, itemAMig.RankDelta); // 1 - 2 = -1 drop

        var itemCMig = migration.Migrations.First(m => m.Sku == "SKU-CCC-03");
        Assert.Equal("Unchanged", itemCMig.MigrationType);
    }

    [Fact]
    public async Task AnalyticsQueryService_AbcMethods_EnforceFacilityAndTenantAuthorization()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // Seed a classification record for Client 1 at Facility 1
        await context.SkuVelocityClassificationRecords.AddAsync(SkuVelocityClassificationRecord.Create(
            fix.Client1Id, fix.FacilityId, fix.ItemAId, "SKU-AAA-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.FastMoving, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow,
            10, 20m, 20000m, 10, 80m, version: 1, rankInPortfolio: 1));
        await context.SaveChangesAsync();

        // 1. Client 2 User attempts to query Client 1 summary -> Throws UnauthorizedException
        var client2UserContext = new TestCurrentUserContext
        {
            UserType = UserType.Client,
            TenantId = fix.Client2Id
        };
        var queryServiceClient2 = new AnalyticsQueryService(context, client2UserContext, NullLogger<AnalyticsQueryService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryServiceClient2.GetAbcClassificationSummaryAsync(fix.FacilityId, fix.Client1Id));

        // 2. Facility 2 restricted user attempts to query Facility 1 -> Throws UnauthorizedException
        var facility2UserContext = new TestCurrentUserContext
        {
            UserType = UserType.Staff,
            FacilityId = fix.Facility2Id
        };
        var queryServiceFacility2 = new AnalyticsQueryService(context, facility2UserContext, NullLogger<AnalyticsQueryService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryServiceFacility2.GetAbcClassificationSummaryAsync(fix.FacilityId, null));
    }

    [Fact]
    public async Task AbcClassificationAnalyticsController_EndToEndApiVerification()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var userContext = new TestCurrentUserContext { UserType = UserType.Staff };
        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var queryService = new AnalyticsQueryService(context, userContext, NullLogger<AnalyticsQueryService>.Instance);

        var controller = new AbcClassificationAnalyticsController(queryService, projectionService, userContext);

        // 1. Upsert Policy via Controller
        var upsertReq = new UpsertAbcClassificationPolicyRequest
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            Name = "Automated Controller ABC Policy",
            ClassACumulativeThresholdPercent = 80.00m,
            ClassBCumulativeThresholdPercent = 95.00m,
            Criteria = AbcClassificationCriteria.AnnualConsumptionValue
        };
        var upsertActionResult = await controller.UpsertPolicy(upsertReq, default);
        var okUpsert = Assert.IsType<OkObjectResult>(upsertActionResult);
        var policyDto = Assert.IsType<AbcClassificationPolicyDto>(okUpsert.Value);
        Assert.Equal("Automated Controller ABC Policy", policyDto.Name);

        // 2. Get Effective Policy via Controller
        var getPolicyResult = await controller.GetEffectivePolicy(fix.FacilityId, fix.Client1Id, default);
        var okGetPolicy = Assert.IsType<OkObjectResult>(getPolicyResult);
        var fetchedPolicy = Assert.IsType<AbcClassificationPolicyDto>(okGetPolicy.Value);
        Assert.Equal(policyDto.Id, fetchedPolicy.Id);

        // 3. Execute Batch Calculation via Controller
        var batchReq = new ExecuteAbcClassificationBatchRequest
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            PolicyId = policyDto.Id
        };
        var batchActionResult = await controller.ExecuteBatchCalculation(fix.FacilityId, batchReq, default);
        var okBatch = Assert.IsType<OkObjectResult>(batchActionResult);
        var batchResult = Assert.IsType<AbcClassificationBatchResultDto>(okBatch.Value);
        Assert.Equal(4, batchResult.Summary.TotalSkus);
        Assert.NotEmpty(batchResult.RankedSkus);

        // 4. Get Summary via Controller
        var summaryActionResult = await controller.GetSummary(fix.FacilityId, fix.Client1Id, null, null, default);
        var okSummary = Assert.IsType<OkObjectResult>(summaryActionResult);
        var summaryDto = Assert.IsType<AbcClassificationSummaryDto>(okSummary.Value);
        Assert.Equal(4, summaryDto.TotalSkus);

        // 5. Get Ranked SKUs via Controller
        var rankedActionResult = await controller.GetRankedSkus(fix.FacilityId, fix.Client1Id, null, null, null, 1, 50, default);
        var okRanked = Assert.IsType<OkObjectResult>(rankedActionResult);
        var rankedSkus = Assert.IsAssignableFrom<IReadOnlyList<AbcRankedSkuDto>>(okRanked.Value);
        Assert.Equal(4, rankedSkus.Count);
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_30DayVs365DayObservationWindow_ProducesComparableAnnualizedAcv()
    {
        var fix = new TestFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var facility = new Facility { Id = fix.FacilityId, FacilityCode = "FAC-WIN", Name = "Window Test Facility", IsActive = true };
        var client = new Client { Id = fix.Client1Id, AccountCode = "CLI-WIN", CompanyName = "Window Client", Status = ClientStatus.Active };
        var item = new Item { Id = fix.ItemAId, ClientId = fix.Client1Id, Sku = "SKU-STEADY", Description = "Constant Demand Item", UnitPrice = 100m, Status = ItemStatus.Active };

        await context.Facilities.AddAsync(facility);
        await context.Clients.AddAsync(client);
        await context.Items.AddAsync(item);

        var now = DateTime.UtcNow;
        // Seed 365 daily demand events of 2 units @ $100 = $200/day
        for (int i = 0; i < 365; i++)
        {
            await context.InventoryMovementEventLogs.AddAsync(InventoryMovementEventLog.Create(
                Guid.NewGuid(), fix.FacilityId, fix.Client1Id, fix.ItemAId, "SKU-STEADY",
                InventoryMovementEventType.OutboundPick, 2m, now.AddDays(-i - 0.5),
                unitCost: 100m, isCanonicalDemand: true));
        }
        await context.SaveChangesAsync();

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // 30-day window: 60 units * $100 = $6,000 period value -> Annualized ACV = $6,000 * (365 / 30) = $73,000
        var result30 = await service.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client1Id, windowOverride: VelocityObservationWindow.Days30, asOfUtc: now);

        // 365-day window: 730 units * $100 = $73,000 period value -> Annualized ACV = $73,000 * (365 / 365) = $73,000
        var result365 = await service.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client1Id, windowOverride: VelocityObservationWindow.Days365, asOfUtc: now);

        Assert.Single(result30.RankedSkus);
        Assert.Single(result365.RankedSkus);

        var sku30 = result30.RankedSkus[0];
        var sku365 = result365.RankedSkus[0];

        Assert.Equal(6000.00m, sku30.PeriodDemandValue);
        Assert.Equal(73000.00m, sku30.AnnualizedConsumptionValue);

        Assert.Equal(73000.00m, sku365.PeriodDemandValue);
        Assert.Equal(73000.00m, sku365.AnnualizedConsumptionValue);

        // Prove identical annualized ACV baseline across differing window horizons
        Assert.Equal(sku30.AnnualizedConsumptionValue, sku365.AnnualizedConsumptionValue);
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_EntirelyZeroValuePortfolio_ClassifiesAsClassCWithZeroCumulativeContribution()
    {
        var fix = new TestFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var facility = new Facility { Id = fix.FacilityId, FacilityCode = "FAC-ZERO", Name = "Zero Facility", IsActive = true };
        var client = new Client { Id = fix.Client1Id, AccountCode = "CLI-ZERO", CompanyName = "Zero Client", Status = ClientStatus.Active };
        var item1 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client1Id, Sku = "SKU-Z1", Description = "Zero Item 1", UnitPrice = 50m, Status = ItemStatus.Active };
        var item2 = new Item { Id = Guid.NewGuid(), ClientId = fix.Client1Id, Sku = "SKU-Z2", Description = "Zero Item 2", UnitPrice = 100m, Status = ItemStatus.Active };

        await context.Facilities.AddAsync(facility);
        await context.Clients.AddAsync(client);
        await context.Items.AddRangeAsync(item1, item2);
        await context.SaveChangesAsync();

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var result = await service.CalculateAbcClassificationBatchAsync(fix.FacilityId, fix.Client1Id);

        Assert.NotNull(result.Summary);
        Assert.False(result.Summary.HasPositiveConsumptionValue);
        Assert.Equal("NoPositiveConsumptionValue", result.Summary.PortfolioConsumptionStatus);
        Assert.Equal(0m, result.Summary.TotalPortfolioValue);
        Assert.Equal(0m, result.Summary.PeriodPortfolioValue);
        Assert.Equal(0m, result.Summary.AnnualizedPortfolioValue);
        Assert.Equal(2, result.Summary.ClassCCount);
        Assert.Equal(0, result.Summary.ClassACount);
        Assert.Equal(0, result.Summary.ClassBCount);

        // Cumulative contribution must be 0.00% (undefined/zero, NOT fabricated 100%)
        Assert.All(result.RankedSkus, sku =>
        {
            Assert.Equal(AbcClassificationCategory.ClassC, sku.AbcClass);
            Assert.Equal(0.00m, sku.CumulativeContributionPercent);
            Assert.Equal(0m, sku.PeriodDemandValue);
            Assert.Equal(0m, sku.AnnualizedConsumptionValue);
        });
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_WeightedCompositeWithAllZeroCriteria_NoDivideByZeroOrNaN()
    {
        var fix = new TestFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var facility = new Facility { Id = fix.FacilityId, FacilityCode = "FAC-COMP-ZERO", Name = "Composite Zero Facility", IsActive = true };
        var client = new Client { Id = fix.Client1Id, AccountCode = "CLI-COMP-ZERO", CompanyName = "Composite Zero Client", Status = ClientStatus.Active };
        var item = new Item { Id = Guid.NewGuid(), ClientId = fix.Client1Id, Sku = "SKU-CZ1", Description = "Comp Zero Item", UnitPrice = 50m, Status = ItemStatus.Active };

        await context.Facilities.AddAsync(facility);
        await context.Clients.AddAsync(client);
        await context.Items.AddAsync(item);
        await context.SaveChangesAsync();

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var result = await service.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client1Id, criteriaOverride: AbcClassificationCriteria.WeightedComposite);

        Assert.Single(result.RankedSkus);
        var ranked = result.RankedSkus[0];
        Assert.Equal(0.00m, ranked.CompositeScore);
        Assert.Equal(0.00m, ranked.CumulativeContributionPercent);
        Assert.Equal(AbcClassificationCategory.ClassC, ranked.AbcClass);
        Assert.False(double.IsNaN((double)ranked.CompositeScore));
        Assert.False(double.IsInfinity((double)ranked.CompositeScore));
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_ConcurrentRuns_AllocatesSequentialVersionsAndPreservesHistory()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // Execute two concurrent batch calculations for the same facility and client
        var task1 = service.CalculateAbcClassificationBatchAsync(fix.FacilityId, fix.Client1Id);
        var task2 = service.CalculateAbcClassificationBatchAsync(fix.FacilityId, fix.Client1Id);

        var results = await Task.WhenAll(task1, task2);

        var result1 = results[0];
        var result2 = results[1];

        Assert.NotNull(result1.Summary);
        Assert.NotNull(result2.Summary);

        // Versions must be sequential distinct integers
        var versions = new[] { result1.Summary.Version, result2.Summary.Version };
        Assert.Contains(1, versions);
        Assert.Contains(2, versions);
        Assert.NotEqual(result1.Summary.Version, result2.Summary.Version);

        // Unique classification run IDs
        Assert.NotEqual(result1.Summary.ClassificationRunId, result2.Summary.ClassificationRunId);

        // Verify both versions exist in database without overwriting
        var v1Records = await context.SkuVelocityClassificationRecords
            .Where(r => r.FacilityId == fix.FacilityId && r.Version == 1 && r.RecordType == SkuClassificationRecordType.AbcParetoStratification)
            .ToListAsync();
        var v2Records = await context.SkuVelocityClassificationRecords
            .Where(r => r.FacilityId == fix.FacilityId && r.Version == 2 && r.RecordType == SkuClassificationRecordType.AbcParetoStratification)
            .ToListAsync();

        Assert.Equal(4, v1Records.Count);
        Assert.Equal(4, v2Records.Count);
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_VelocityAndAbcRecalculations_PersistIndependently()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var queryService = new AnalyticsQueryService(
            context,
            new TestCurrentUserContext { UserType = UserType.Staff },
            NullLogger<AnalyticsQueryService>.Instance);

        // 1. Calculate operational SKU velocity for Item A
        var velocityRecord = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemAId, "SKU-AAA-01");

        Assert.Equal(SkuClassificationRecordType.VelocityOperational, velocityRecord.RecordType);

        // 2. Calculate Pareto ABC batch for the facility
        var abcResult = await projectionService.CalculateAbcClassificationBatchAsync(
            fix.FacilityId, fix.Client1Id);

        // 3. Query via AnalyticsQueryService
        var velocityDtos = await queryService.GetSkuVelocityClassificationsAsync(fix.FacilityId, fix.Client1Id);
        var abcDtos = await queryService.GetAbcRankedSkusAsync(fix.FacilityId, fix.Client1Id);

        // Velocity query must only return VelocityOperational records
        Assert.All(velocityDtos, dto => Assert.Equal(SkuClassificationRecordType.VelocityOperational, dto.RecordType));
        Assert.Contains(velocityDtos, d => d.ItemId == fix.ItemAId);

        // ABC query must only return AbcParetoStratification records
        Assert.Equal(4, abcDtos.Count);

        // Check raw database table has both types persisting cleanly side-by-side
        var totalOperational = await context.SkuVelocityClassificationRecords
            .CountAsync(r => r.FacilityId == fix.FacilityId && r.RecordType == SkuClassificationRecordType.VelocityOperational);
        var totalPareto = await context.SkuVelocityClassificationRecords
            .CountAsync(r => r.FacilityId == fix.FacilityId && r.RecordType == SkuClassificationRecordType.AbcParetoStratification);

        Assert.True(totalOperational >= 1);
        Assert.True(totalPareto >= 4);
    }

    [Fact]
    public async Task CalculateAbcClassificationBatch_HistoricalPolicyChanges_PreservesPreviouslyCompletedClassifications()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // 1. Policy V1: 80% Class A, 95% Class B
        var policyV1Req = new UpsertAbcClassificationPolicyRequest
        {
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            Name = "Policy Version 1",
            ClassACumulativeThresholdPercent = 80.00m,
            ClassBCumulativeThresholdPercent = 95.00m,
            Criteria = AbcClassificationCriteria.AnnualConsumptionValue
        };
        var policyV1 = await service.UpsertAbcClassificationPolicyAsync(policyV1Req);

        var runV1 = await service.CalculateAbcClassificationBatchAsync(fix.FacilityId, fix.Client1Id, policyId: policyV1.Id);
        int v1Number = runV1.Summary.Version;

        // 2. Policy V2: More restrictive thresholds (70% Class A, 90% Class B)
        var policyV2Req = new UpsertAbcClassificationPolicyRequest
        {
            Id = policyV1.Id,
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            Name = "Policy Version 2 (Stricter)",
            ClassACumulativeThresholdPercent = 70.00m,
            ClassBCumulativeThresholdPercent = 90.00m,
            Criteria = AbcClassificationCriteria.AnnualConsumptionValue
        };
        var policyV2 = await service.UpsertAbcClassificationPolicyAsync(policyV2Req);

        var runV2 = await service.CalculateAbcClassificationBatchAsync(fix.FacilityId, fix.Client1Id, policyId: policyV2.Id);
        int v2Number = runV2.Summary.Version;

        Assert.NotEqual(v1Number, v2Number);

        // 3. Inspect persisted records in DB:
        // Version 1 records must retain 80.00m / 95.00m thresholds
        var v1DbRecords = await context.SkuVelocityClassificationRecords
            .Where(r => r.FacilityId == fix.FacilityId && r.Version == v1Number && r.RecordType == SkuClassificationRecordType.AbcParetoStratification)
            .ToListAsync();

        Assert.All(v1DbRecords, r =>
        {
            Assert.Equal(80.00m, r.ClassAThresholdPercent);
            Assert.Equal(95.00m, r.ClassBThresholdPercent);
        });

        // Version 2 records must record 70.00m / 90.00m thresholds
        var v2DbRecords = await context.SkuVelocityClassificationRecords
            .Where(r => r.FacilityId == fix.FacilityId && r.Version == v2Number && r.RecordType == SkuClassificationRecordType.AbcParetoStratification)
            .ToListAsync();

        Assert.All(v2DbRecords, r =>
        {
            Assert.Equal(70.00m, r.ClassAThresholdPercent);
            Assert.Equal(90.00m, r.ClassBThresholdPercent);
        });
    }
}
