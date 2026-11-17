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
/// Exhaustive unit and integration test suite for Phase 6.3.4:
/// SKU Velocity & Inventory Movement Analytics.
/// Verifies:
/// 1. Separation of qualifying customer demand from internal warehouse handling (relocation/replenishment).
/// 2. Event deduplication and checkpoint watermark advancing.
/// 3. Multi-window velocity aggregation (30d, 90d, 180d, 365d).
/// 4. Demand trend and seasonal spike detection.
/// 5. Inventory turnover ratio and days-of-supply (DOS) calculations with zero-division safety.
/// 6. Task movement frequency (pick vs replenishment vs relocation).
/// 7. Pareto ABC stratification.
/// 8. Velocity category classification (Fast, Medium, Slow, Non-moving, Dead Stock).
/// 9. Slotting misallocation diagnostics.
/// 10. Authoritative inventory ledger immutability.
/// 11. Multi-tenant and facility authorization boundaries.
/// 12. Projection replay and reset idempotency.
/// </summary>
public class SkuVelocityAnalyticsTests
{
    private class TestFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid Facility2Id { get; set; } = Guid.NewGuid();
        public Guid Client1Id { get; set; } = Guid.NewGuid();
        public Guid Client2Id { get; set; } = Guid.NewGuid();

        public Guid ItemFastId { get; set; } = Guid.NewGuid();
        public Guid ItemMediumId { get; set; } = Guid.NewGuid();
        public Guid ItemSlowId { get; set; } = Guid.NewGuid();
        public Guid ItemDeadId { get; set; } = Guid.NewGuid();

        public Guid PickFaceZoneId { get; set; } = Guid.NewGuid();
        public Guid BulkReserveZoneId { get; set; } = Guid.NewGuid();

        public Guid LocationPick1Id { get; set; } = Guid.NewGuid();
        public Guid LocationBulk1Id { get; set; } = Guid.NewGuid();
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
            AccountCode = "CLI-ALPHA",
            CompanyName = "Alpha Distribution Corp",
            Status = ClientStatus.Active
        };

        var client2 = new Client
        {
            Id = fix.Client2Id,
            AccountCode = "CLI-BETA",
            CompanyName = "Beta Wholesale Ltd",
            Status = ClientStatus.Active
        };

        var itemFast = new Item
        {
            Id = fix.ItemFastId,
            ClientId = fix.Client1Id,
            Sku = "SKU-FAST-01",
            Description = "High Velocity Widget",
            Status = ItemStatus.Active
        };

        var itemMed = new Item
        {
            Id = fix.ItemMediumId,
            ClientId = fix.Client1Id,
            Sku = "SKU-MED-02",
            Description = "Medium Velocity Gadget",
            Status = ItemStatus.Active
        };

        var itemSlow = new Item
        {
            Id = fix.ItemSlowId,
            ClientId = fix.Client1Id,
            Sku = "SKU-SLOW-03",
            Description = "Slow Moving Assembly",
            Status = ItemStatus.Active
        };

        var itemDead = new Item
        {
            Id = fix.ItemDeadId,
            ClientId = fix.Client1Id,
            Sku = "SKU-DEAD-04",
            Description = "Dead Stock Part",
            Status = ItemStatus.Active
        };

        var zonePick = new WarehouseZone
        {
            Id = fix.PickFaceZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "ZONE-PICK-A",
            Name = "Active Pick Faces Zone",
            ZoneType = WarehouseZoneType.PickFace,
            IsActive = true
        };

        var zoneBulk = new WarehouseZone
        {
            Id = fix.BulkReserveZoneId,
            FacilityId = fix.FacilityId,
            ZoneCode = "ZONE-BULK-B",
            Name = "Bulk High-Bay Reserve",
            ZoneType = WarehouseZoneType.BulkStorage,
            IsActive = true
        };

        var locPick = new Location
        {
            Id = fix.LocationPick1Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.PickFaceZoneId,
            LocationCode = "A-01-01",
            LocationType = LocationType.Bin,
            IsActive = true
        };

        var locBulk = new Location
        {
            Id = fix.LocationBulk1Id,
            FacilityId = fix.FacilityId,
            ZoneId = fix.BulkReserveZoneId,
            LocationCode = "B-01-01",
            LocationType = LocationType.BulkFloor,
            IsActive = true
        };

        // Seed stock on hand for authoritative ledger immutability verification
        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            ClientId = fix.Client1Id,
            ItemId = fix.ItemFastId,
            LocationId = fix.LocationPick1Id,
            QtyAvailable = 500m,
            QtyAllocated = 50m,
            ReceivedAtUtc = DateTime.UtcNow.AddDays(-60)
        };

        await context.Facilities.AddRangeAsync(fac1, fac2);
        await context.Clients.AddRangeAsync(client1, client2);
        await context.Items.AddRangeAsync(itemFast, itemMed, itemSlow, itemDead);
        await context.WarehouseZones.AddRangeAsync(zonePick, zoneBulk);
        await context.Locations.AddRangeAsync(locPick, locBulk);
        await context.StockOnHand.AddAsync(stock);

        await context.SaveChangesAsync();

        return fix;
    }

    [Fact]
    public async Task DemandVsMovement_SeparatesQualifyingDemandFromInternalWarehouseHandling()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        // Ingest Qualifying Customer Demand (Picks & Dispatches)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 150m, now.AddDays(-5),
            unitCost: 20m, referenceNumber: "PICK-001");

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.ShipmentDispatch, 150m, now.AddDays(-4),
            unitCost: 20m, referenceNumber: "DISP-001");

        // Ingest Operational Handling Tasks (Replenishment, Relocation, Putaway)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 3, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.Replenishment, 500m, now.AddDays(-3),
            unitCost: 20m, referenceNumber: "REPL-001");

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 4, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.InternalRelocation, 200m, now.AddDays(-2),
            unitCost: 20m, referenceNumber: "RELOC-001");

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 5, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.Putaway, 1000m, now.AddDays(-1),
            unitCost: 20m, referenceNumber: "PUT-001");

        var logs = await context.InventoryMovementEventLogs
            .Where(l => l.ItemId == fix.ItemFastId)
            .ToListAsync();

        Assert.Equal(5, logs.Count);

        // Demand must strictly sum only qualifying events (150 + 150 = 300)
        var qualifyingDemand = logs.Where(l => l.IsQualifyingDemand).Sum(l => l.Quantity);
        Assert.Equal(300m, qualifyingDemand);

        // Physical movement must include handling tasks
        var physicalMoves = logs.Where(l => l.IsPhysicalMovement).Sum(l => l.Quantity);
        // OutboundPick(150) + Replenishment(500) + InternalRelocation(200) + Putaway(1000) = 1850
        Assert.Equal(1850m, physicalMoves);

        // Calculate velocity using projection calculation engine
        var calculated = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            VelocityObservationWindow.Days30, asOfUtc: now);

        // Demand velocity must reflect ONLY qualifying demand (300 / 30 = 10 units/day)
        Assert.Equal(300m, calculated.TotalUnitsDemanded);
        Assert.Equal(10.0m, calculated.DailyDemandVelocity);
        Assert.Equal(1, calculated.TotalReplenishmentCount);
        Assert.Equal(1, calculated.TotalRelocationCount);
        Assert.Equal(InventoryMovementVelocityCategory.FastMoving, calculated.VelocityCategory);
    }

    [Fact]
    public async Task EventDeduplication_AdvancingWatermarkAndIgnoringDuplicates()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // First ingestion
        await projectionService.ProcessInventoryMovementEventAsync(
            eventId, 100, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 50m, now);

        // Duplicate ingestion
        await projectionService.ProcessInventoryMovementEventAsync(
            eventId, 100, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 50m, now);

        var logs = await context.InventoryMovementEventLogs
            .Where(l => l.EventId == eventId)
            .ToListAsync();

        Assert.Single(logs);

        var checkpoint = await projectionService.GetOrCreateCheckpointAsync(AnalyticsProjectionService.SkuVelocityProjection);
        Assert.Equal(eventId, checkpoint.LastProcessedEventId);
        Assert.Equal(100, checkpoint.LastProcessedSequenceNumber);
    }

    [Fact]
    public async Task MultiWindowAggregation_CalculatesAccurateVelocityAcross30d90d180d365d()
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

        var now = DateTime.UtcNow;

        // 30d window events: 600 units
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 600m, now.AddDays(-10), unitCost: 10m);

        // 90d window events: additional 600 units (total 1200 in 90d)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 600m, now.AddDays(-60), unitCost: 10m);

        // 180d window events: additional 600 units (total 1800 in 180d)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 3, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 600m, now.AddDays(-120), unitCost: 10m);

        // 365d window events: additional 600 units (total 2400 in 365d)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 4, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 600m, now.AddDays(-300), unitCost: 10m);

        var comparison = await queryService.GetSkuVelocityMultiWindowComparisonAsync(
            fix.FacilityId, fix.ItemFastId, now);

        Assert.NotNull(comparison);
        // 30d: 600 / 30 = 20.0 u/d
        Assert.Equal(20.0m, comparison.DemandVelocity30d);
        // 90d: 1200 / 90 = 13.3333 u/d
        Assert.Equal(13.3333m, comparison.DemandVelocity90d);
        // 180d: 1800 / 180 = 10.0 u/d
        Assert.Equal(10.0m, comparison.DemandVelocity180d);
        // 365d: 2400 / 365 = 6.5753 u/d
        Assert.Equal(6.5753m, comparison.DemandVelocity365d);
    }

    [Fact]
    public async Task DemandTrendAndSeasonalSpikeDetection_SurgingDecliningAndSpikes()
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

        var now = DateTime.UtcNow;

        // Create a massive surge in the last 15 days: 900 units in 30d, but only 100 units in prior 300 days
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 900m, now.AddDays(-5), unitCost: 10m);

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 100m, now.AddDays(-150), unitCost: 10m);

        var comparison = await queryService.GetSkuVelocityMultiWindowComparisonAsync(
            fix.FacilityId, fix.ItemFastId, now);

        Assert.NotNull(comparison);
        // 30d velocity = 30.0 u/d; 90d velocity = 10.0 u/d; 365d velocity = 1000/365 = 2.74 u/d
        Assert.Equal(30.0m, comparison.DemandVelocity30d);
        Assert.Equal("DemandSpike", comparison.DemandTrend);
        Assert.True(comparison.DemandSpikeDetected);
    }

    [Fact]
    public async Task InventoryTurnoverAndDaysOfSupply_CalculatesAccuratelyWithZeroDivisionSafety()
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

        var now = DateTime.UtcNow;

        // Ingest SkuVelocityRecord with known COGS and Average Inventory Value
        // COGS = $36,500 over 90 days. Average Inventory = $10,000.
        // Annualized Turnover = (36500 / 10000) * (365 / 90) = 3.65 * 4.05555... = 14.8028x
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 1, fix.Client1Id, fix.FacilityId, fix.ItemFastId, "SKU-FAST-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.FastMoving,
            now.AddDays(-90), now,
            pickTasksCount: 100, unitsDemanded: 1825m, demandValue: 36500m,
            orderLinesCount: 100, cumulativePercentage: 30m,
            observationWindow: VelocityObservationWindow.Days90,
            cogsConsumed: 36500m, avgInventoryValue: 10000m);

        // Also test zero valuation item
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 2, fix.Client1Id, fix.FacilityId, fix.ItemDeadId, "SKU-DEAD-04",
            AbcClassificationCategory.ClassC, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.DeadStock,
            now.AddDays(-90), now,
            pickTasksCount: 0, unitsDemanded: 0m, demandValue: 0m,
            orderLinesCount: 0, cumulativePercentage: 100m,
            observationWindow: VelocityObservationWindow.Days90,
            cogsConsumed: 0m, avgInventoryValue: 5000m);

        var turnoverAnalysis = await queryService.GetInventoryTurnoverAnalysisAsync(
            fix.FacilityId, fix.Client1Id, VelocityObservationWindow.Days90, now);

        Assert.NotNull(turnoverAnalysis);
        Assert.Equal(36500m, turnoverAnalysis.TotalCogsConsumed);
        Assert.Equal(15000m, turnoverAnalysis.TotalAverageInventoryValuation);
        // Overall annualized turnover: (36500 / 15000) * (365 / 90) = 2.4333 * 4.05555 = 9.8685
        Assert.Equal(9.8685m, turnoverAnalysis.OverallAnnualizedTurnoverRatio);
        Assert.True(turnoverAnalysis.AverageDaysOfSupply > 0);
        Assert.Equal(5000m, turnoverAnalysis.ZeroTurnoverInventoryValue);
        Assert.Equal(5000m, turnoverAnalysis.DeadStockValue);

        Assert.Single(turnoverAnalysis.FastestTurningSkus);
        Assert.Equal(fix.ItemFastId, turnoverAnalysis.FastestTurningSkus[0].ItemId);
    }

    [Fact]
    public async Task MovementFrequency_PickTasksVsReplenishmentsVsRelocations()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        // Ingest 90-day window record
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 1, fix.Client1Id, fix.FacilityId, fix.ItemFastId, "SKU-FAST-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.PickFrequency,
            InventoryMovementVelocityCategory.FastMoving,
            now.AddDays(-90), now,
            pickTasksCount: 450, unitsDemanded: 1800m, demandValue: 18000m,
            orderLinesCount: 450, cumulativePercentage: 25m,
            observationWindow: VelocityObservationWindow.Days90,
            replenishmentCount: 45, relocationCount: 9);

        var record = await context.SkuVelocityClassificationRecords
            .FirstAsync(r => r.ItemId == fix.ItemFastId);

        // Pick frequency = 450 / 90 = 5.0 picks/day
        Assert.Equal(5.0m, record.PickFrequencyPerDay);
        // Replenishment frequency = 45 / 90 = 0.5 replenishments/day
        Assert.Equal(0.5m, record.ReplenishmentFrequencyPerDay);
        // Relocation frequency = 9 / 90 = 0.1 relocations/day
        Assert.Equal(0.1m, record.RelocationFrequencyPerDay);
    }

    [Fact]
    public async Task ParetoAbcClassification_StratifiesPortfolioByVolumeValue()
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

        var now = DateTime.UtcNow;

        // Class A: 70% cumulative
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 1, fix.Client1Id, fix.FacilityId, fix.ItemFastId, "SKU-FAST-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.FastMoving,
            now.AddDays(-90), now, 200, 2000m, 70000m, 200, cumulativePercentage: 70m);

        // Class B: 90% cumulative
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 2, fix.Client1Id, fix.FacilityId, fix.ItemMediumId, "SKU-MED-02",
            AbcClassificationCategory.ClassB, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.MediumMoving,
            now.AddDays(-90), now, 50, 500m, 20000m, 50, cumulativePercentage: 90m);

        // Class C: 100% cumulative
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 3, fix.Client1Id, fix.FacilityId, fix.ItemSlowId, "SKU-SLOW-03",
            AbcClassificationCategory.ClassC, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.SlowMoving,
            now.AddDays(-90), now, 10, 50m, 1000m, 10, cumulativePercentage: 100m);

        var classifications = await queryService.GetSkuVelocityClassificationsAsync(
            fix.FacilityId, fix.Client1Id, AbcClassificationCriteria.AnnualConsumptionValue);

        Assert.Equal(3, classifications.Count);
        Assert.Equal(AbcClassificationCategory.ClassA, classifications[0].AbcClass);
        Assert.Equal(AbcClassificationCategory.ClassB, classifications[1].AbcClass);
        Assert.Equal(AbcClassificationCategory.ClassC, classifications[2].AbcClass);
    }

    [Fact]
    public async Task VelocityCategoryClassification_FastMediumSlowNonMovingDeadStock()
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

        var now = DateTime.UtcNow;

        // Ingest movement events for fast mover (> 10 units/day)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 350m, now.AddDays(-10));

        // Ingest movement events for medium mover (2 - 10 units/day)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.Client1Id, fix.ItemMediumId, "SKU-MED-02",
            InventoryMovementEventType.OutboundPick, 90m, now.AddDays(-10));

        // Ingest movement events for slow mover (< 2 units/day)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 3, fix.FacilityId, fix.Client1Id, fix.ItemSlowId, "SKU-SLOW-03",
            InventoryMovementEventType.OutboundPick, 5m, now.AddDays(-10));

        var fastRec = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01", VelocityObservationWindow.Days30, asOfUtc: now);
        var medRec = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemMediumId, "SKU-MED-02", VelocityObservationWindow.Days30, asOfUtc: now);
        var slowRec = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemSlowId, "SKU-SLOW-03", VelocityObservationWindow.Days30, asOfUtc: now);
        var deadRec = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemDeadId, "SKU-DEAD-04", VelocityObservationWindow.Days365, asOfUtc: now);

        Assert.Equal(InventoryMovementVelocityCategory.FastMoving, fastRec.VelocityCategory);
        Assert.Equal(InventoryMovementVelocityCategory.MediumMoving, medRec.VelocityCategory);
        Assert.Equal(InventoryMovementVelocityCategory.SlowMoving, slowRec.VelocityCategory);
        Assert.Equal(InventoryMovementVelocityCategory.DeadStock, deadRec.VelocityCategory);

        var summary = await queryService.GetSkuVelocitySummaryAsync(fix.FacilityId, fix.Client1Id, VelocityObservationWindow.Days30);
        Assert.NotNull(summary);
        Assert.Equal(1, summary.FastMovingCount);
        Assert.Equal(1, summary.MediumMovingCount);
        Assert.Equal(1, summary.SlowMovingCount);
    }

    [Fact]
    public async Task SlottingMisallocationDiagnostics_DetectsFastMoversInBulkAndSlowInPickFaces()
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

        var now = DateTime.UtcNow;

        // Space utilization records for the 2 zones
        await projectionService.ProcessLocationSpaceUtilizationAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.LocationPick1Id, "A-01-01", now,
            usableCbm: 10m, occupiedCbm: 8m, usablePalletPositions: 10, occupiedPalletPositions: 8,
            zoneId: fix.PickFaceZoneId, locationType: LocationType.Bin);

        await projectionService.ProcessLocationSpaceUtilizationAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.LocationBulk1Id, "B-01-01", now,
            usableCbm: 100m, occupiedCbm: 80m, usablePalletPositions: 50, occupiedPalletPositions: 40,
            zoneId: fix.BulkReserveZoneId, locationType: LocationType.BulkFloor);

        // Misallocation 1: Fast mover located in Bulk Reserve
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 3, fix.Client1Id, fix.FacilityId, fix.ItemFastId, "SKU-FAST-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.PickFrequency,
            InventoryMovementVelocityCategory.FastMoving, now.AddDays(-90), now,
            pickTasksCount: 300, unitsDemanded: 3000m, demandValue: 30000m, orderLinesCount: 300,
            cumulativePercentage: 30m, zoneId: fix.BulkReserveZoneId);

        // Misallocation 2: Dead stock occupying active Pick Face
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 4, fix.Client1Id, fix.FacilityId, fix.ItemDeadId, "SKU-DEAD-04",
            AbcClassificationCategory.ClassC, AbcClassificationCriteria.PickFrequency,
            InventoryMovementVelocityCategory.DeadStock, now.AddDays(-90), now,
            pickTasksCount: 0, unitsDemanded: 0m, demandValue: 0m, orderLinesCount: 0,
            cumulativePercentage: 100m, zoneId: fix.PickFaceZoneId);

        var profiles = await queryService.GetZoneVelocityProfilesAsync(fix.FacilityId);

        var pickZoneProfile = profiles.FirstOrDefault(p => p.ZoneId == fix.PickFaceZoneId);
        var bulkZoneProfile = profiles.FirstOrDefault(p => p.ZoneId == fix.BulkReserveZoneId);

        Assert.NotNull(pickZoneProfile);
        Assert.NotNull(bulkZoneProfile);

        // Pick Face zone should flag slow/dead SKU occupying prime real estate
        Assert.True(pickZoneProfile.MisallocatedSlowMoverCount > 0);

        // Bulk Reserve zone should flag fast mover requiring excessive travel
        Assert.True(bulkZoneProfile.MisallocatedFastMoverCount > 0);
    }

    [Fact]
    public async Task AuthoritativeLedgerImmutability_DoesNotTouchOperationalInventoryTables()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var initialStock = await context.StockOnHand
            .AsNoTracking()
            .FirstAsync(s => s.ItemId == fix.ItemFastId);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        // Perform event ingestion and classification runs
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 100m, now, unitCost: 15m);

        await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01", VelocityObservationWindow.Days30, asOfUtc: now);

        var postStock = await context.StockOnHand
            .AsNoTracking()
            .FirstAsync(s => s.ItemId == fix.ItemFastId);

        // Operational inventory balances must remain strictly untouched
        Assert.Equal(initialStock.QtyAvailable, postStock.QtyAvailable);
        Assert.Equal(initialStock.QtyAllocated, postStock.QtyAllocated);
        Assert.Equal(initialStock.LocationId, postStock.LocationId);
        Assert.Equal(initialStock.TotalQuantity, postStock.TotalQuantity);
    }

    [Fact]
    public async Task TenantAndFacilityAuthorization_EnforcesCrossTenantAndCrossFacilitySecurity()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 1, fix.Client1Id, fix.FacilityId, fix.ItemFastId, "SKU-FAST-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.PickFrequency,
            InventoryMovementVelocityCategory.FastMoving, now.AddDays(-90), now,
            pickTasksCount: 100, unitsDemanded: 1000m, demandValue: 10000m, orderLinesCount: 100,
            cumulativePercentage: 20m);

        // Context constrained to Client 2
        var client2Context = new TestCurrentUserContext
        {
            UserType = UserType.Client,
            TenantId = fix.Client2Id
        };
        var client2QueryService = new AnalyticsQueryService(
            context, client2Context, NullLogger<AnalyticsQueryService>.Instance);

        // Attempt to query Client 1's data should throw UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            client2QueryService.GetSkuVelocityClassificationsAsync(
                fix.FacilityId, clientId: fix.Client1Id));

        // Facility boundary check: user restricted to Facility 1 querying Facility 2
        var fac1UserContext = new TestCurrentUserContext
        {
            UserType = UserType.Staff,
            FacilityId = fix.FacilityId
        };
        var fac1QueryService = new AnalyticsQueryService(
            context, fac1UserContext, NullLogger<AnalyticsQueryService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fac1QueryService.GetSkuVelocityClassificationsAsync(fix.Facility2Id));
    }

    [Fact]
    public async Task ProjectionReplay_RestoresClassificationsAndPreservesSourceLogs()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 100m, now);

        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 2, fix.Client1Id, fix.FacilityId, fix.ItemFastId, "SKU-FAST-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.PickFrequency,
            InventoryMovementVelocityCategory.FastMoving, now.AddDays(-90), now,
            pickTasksCount: 50, unitsDemanded: 500m, demandValue: 5000m, orderLinesCount: 50,
            cumulativePercentage: 25m);

        Assert.Equal(1, await context.InventoryMovementEventLogs.CountAsync());
        Assert.Equal(1, await context.SkuVelocityClassificationRecords.CountAsync());

        // Replay from beginning
        await projectionService.ReplayProjectionsAsync(
            AnalyticsProjectionService.SkuVelocityProjection,
            ProjectionReplayMode.FromBeginning);

        Assert.Equal(1, await context.InventoryMovementEventLogs.CountAsync());
        Assert.Equal(1, await context.SkuVelocityClassificationRecords.CountAsync());

        var checkpoint = await projectionService.GetOrCreateCheckpointAsync(AnalyticsProjectionService.SkuVelocityProjection);
        Assert.Equal(ProjectionStatus.Active, checkpoint.Status);
        Assert.Equal(2, checkpoint.LastProcessedSequenceNumber);
    }

    [Fact]
    public async Task WebApi_Controller_Endpoints_ReturnExpectedHttpResults()
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

        var userContext = new TestCurrentUserContext { UserType = UserType.Staff };
        var controller = new SkuVelocityAnalyticsController(queryService, projectionService, userContext);
        var now = DateTime.UtcNow;

        // Seed record
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 1, fix.Client1Id, fix.FacilityId, fix.ItemFastId, "SKU-FAST-01",
            AbcClassificationCategory.ClassA, AbcClassificationCriteria.PickFrequency,
            InventoryMovementVelocityCategory.FastMoving, now.AddDays(-90), now,
            pickTasksCount: 120, unitsDemanded: 1200m, demandValue: 12000m, orderLinesCount: 120,
            cumulativePercentage: 30m, observationWindow: VelocityObservationWindow.Days90);

        // GET directory
        var dirResult = await controller.GetSkuVelocityClassifications(
            fix.FacilityId, fix.Client1Id, null, null, null, null, null, CancellationToken.None);
        var okDir = Assert.IsType<OkObjectResult>(dirResult);
        var list = Assert.IsAssignableFrom<IReadOnlyList<SkuVelocityClassificationDto>>(okDir.Value);
        Assert.Single(list);

        // GET summary
        var sumResult = await controller.GetSkuVelocitySummary(
            fix.FacilityId, fix.Client1Id, VelocityObservationWindow.Days90, CancellationToken.None);
        var okSum = Assert.IsType<OkObjectResult>(sumResult);
        var sum = Assert.IsType<SkuVelocitySummaryDto>(okSum.Value);
        Assert.Equal(1, sum.TotalTrackedSkus);

        // GET multi-window
        var mwResult = await controller.GetSkuVelocityMultiWindow(
            fix.FacilityId, fix.ItemFastId, now, CancellationToken.None);
        var okMw = Assert.IsType<OkObjectResult>(mwResult);
        var mw = Assert.IsType<SkuVelocityMultiWindowComparisonDto>(okMw.Value);
        Assert.Equal("SKU-FAST-01", mw.Sku);

        // POST movement event ingestion
        var postResult = await controller.IngestInventoryMovementEvent(
            new ProcessInventoryMovementEventRequest
            {
                EventId = Guid.NewGuid(),
                SequenceNumber = 99,
                FacilityId = fix.FacilityId,
                ClientId = fix.Client1Id,
                ItemId = fix.ItemFastId,
                Sku = "SKU-FAST-01",
                EventType = InventoryMovementEventType.OutboundPick,
                Quantity = 25m,
                TimestampUtc = now,
                UnitCost = 10m
            }, CancellationToken.None);

        Assert.IsType<AcceptedResult>(postResult);
    }

    [Fact]
    public async Task DemandDeduplication_PickAndDispatchForSameOrderLine_CountedOnce()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        // Stage 1: Warehouse picks 50 units for order line ORD-LINE-100
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 50m, now.AddHours(-3),
            unitCost: 15m, referenceNumber: "ORD-LINE-100");

        // Stage 2: Warehouse dispatches the same 50 units for the same order line ORD-LINE-100
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.ShipmentDispatch, 50m, now.AddHours(-1),
            unitCost: 15m, referenceNumber: "ORD-LINE-100");

        var logs = await context.InventoryMovementEventLogs
            .Where(l => l.ItemId == fix.ItemFastId && l.ReferenceNumber == "ORD-LINE-100")
            .ToListAsync();

        Assert.Equal(2, logs.Count);
        // Stage 1 (Pick) is canonical demand; Stage 2 (Dispatch) is non-canonical transport stage
        Assert.True(logs.First(l => l.EventType == InventoryMovementEventType.OutboundPick).IsCanonicalDemand);
        Assert.False(logs.First(l => l.EventType == InventoryMovementEventType.ShipmentDispatch).IsCanonicalDemand);

        // Calculate velocity: Demand must be counted ONCE (50 units), NOT twice (100 units)
        var calculated = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            VelocityObservationWindow.Days30, asOfUtc: now);

        Assert.Equal(50m, calculated.TotalUnitsDemanded);
        Assert.Equal(1.6667m, calculated.DailyDemandVelocity); // 50 / 30
    }

    [Fact]
    public async Task DemandAggregation_SeparateOrdersForSameSku_BothCounted()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        // Order 1: 50 units
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 50m, now.AddHours(-4),
            unitCost: 15m, referenceNumber: "ORD-101");

        // Order 2: 30 units (distinct order reference)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 30m, now.AddHours(-2),
            unitCost: 15m, referenceNumber: "ORD-102");

        var calculated = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            VelocityObservationWindow.Days30, asOfUtc: now);

        // Both orders must be counted (50 + 30 = 80 units)
        Assert.Equal(80m, calculated.TotalUnitsDemanded);
        Assert.Equal(2.6667m, calculated.DailyDemandVelocity); // 80 / 30
    }

    [Fact]
    public async Task OperationalMovements_InternalRelocationAndReplenishment_DoNotInflateDemand()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        // Ingest large handling movements
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.Putaway, 2000m, now.AddDays(-5), referenceNumber: "PUT-01");

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.Replenishment, 1000m, now.AddDays(-3), referenceNumber: "REPL-01");

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 3, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.InternalRelocation, 500m, now.AddDays(-1), referenceNumber: "RELOC-01");

        var calculated = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            VelocityObservationWindow.Days30, asOfUtc: now);

        // Demand must be strictly 0.0 units
        Assert.Equal(0m, calculated.TotalUnitsDemanded);
        Assert.Equal(0m, calculated.DailyDemandVelocity);
        Assert.Equal(3500m, calculated.TotalUnitsMoved);
        Assert.Equal(InventoryMovementVelocityCategory.NonMoving, calculated.VelocityCategory);
    }

    [Fact]
    public async Task InventoryTurnover_ZeroCogsOrZeroAverageInventory_HandledSafelyWithoutException()
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

        var now = DateTime.UtcNow;

        // Item with 0 COGS and positive valuation ($8,000)
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 1, fix.Client1Id, fix.FacilityId, fix.ItemSlowId, "SKU-SLOW-03",
            AbcClassificationCategory.ClassC, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.SlowMoving, now.AddDays(-90), now,
            pickTasksCount: 0, unitsDemanded: 0m, demandValue: 0m, orderLinesCount: 0,
            cumulativePercentage: 100m, cogsConsumed: 0m, avgInventoryValue: 8000m);

        // Item with 0 COGS and 0 valuation
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 2, fix.Client1Id, fix.FacilityId, fix.ItemDeadId, "SKU-DEAD-04",
            AbcClassificationCategory.ClassC, AbcClassificationCriteria.AnnualConsumptionValue,
            InventoryMovementVelocityCategory.DeadStock, now.AddDays(-90), now,
            pickTasksCount: 0, unitsDemanded: 0m, demandValue: 0m, orderLinesCount: 0,
            cumulativePercentage: 100m, cogsConsumed: 0m, avgInventoryValue: 0m);

        var analysis = await queryService.GetInventoryTurnoverAnalysisAsync(
            fix.FacilityId, fix.Client1Id, VelocityObservationWindow.Days90, now);

        Assert.NotNull(analysis);
        Assert.Equal(0m, analysis.TotalCogsConsumed);
        Assert.Equal(8000m, analysis.TotalAverageInventoryValuation);
        Assert.Equal(0m, analysis.OverallAnnualizedTurnoverRatio);
        Assert.Equal(0m, analysis.AverageDaysOfSupply);
        Assert.Equal(8000m, analysis.ZeroTurnoverInventoryValue);
    }

    [Fact]
    public async Task DaysOfSupply_ZeroDailyDemandWithPositiveOnHand_ReturnsExplicitInfiniteSentinel()
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

        var now = DateTime.UtcNow;

        // Record with 0 demand, but 500 units on hand (from initial seed)
        var record = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            VelocityObservationWindow.Days30, asOfUtc: now);

        Assert.Equal(0m, record.DailyDemandVelocity);
        // Explicit sentinel policy: 9999.0d indicates indefinite holding period, never 0, never NaN
        Assert.Equal(9999.0m, record.DaysOfSupplyOperational);
        Assert.Equal(9999.0m, record.DaysOfSupply);

        var multiWindow = await queryService.GetSkuVelocityMultiWindowComparisonAsync(
            fix.FacilityId, fix.ItemFastId, now);

        Assert.NotNull(multiWindow);
        Assert.Equal(9999.0m, multiWindow.DaysOfSupplyOperational);
    }

    [Fact]
    public async Task DemandSpike_Versus_RecurringSeasonalPattern_CorrectClassification()
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

        var now = DateTime.UtcNow;

        // Item 1: Sudden demand spike (30d velocity = 25 u/d, 365d velocity = 3.5 u/d, no prior cycles)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 750m, now.AddDays(-10), unitCost: 10m);

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 500m, now.AddDays(-200), unitCost: 10m);

        var spikeComparison = await queryService.GetSkuVelocityMultiWindowComparisonAsync(
            fix.FacilityId, fix.ItemFastId, now);

        Assert.NotNull(spikeComparison);
        Assert.True(spikeComparison.DemandSpikeDetected);
        Assert.False(spikeComparison.SeasonalPatternDetected);
        Assert.Equal("DemandSpike", spikeComparison.DemandTrend);

        // Item 2: Proven recurring seasonal pattern
        await projectionService.ProcessSkuVelocityClassificationAsync(
            Guid.NewGuid(), 3, fix.Client1Id, fix.FacilityId, fix.ItemMediumId, "SKU-MED-02",
            AbcClassificationCategory.ClassB, AbcClassificationCriteria.PickFrequency,
            InventoryMovementVelocityCategory.FastMoving, now.AddDays(-90), now,
            pickTasksCount: 100, unitsDemanded: 1000m, demandValue: 10000m, orderLinesCount: 100,
            cumulativePercentage: 40m, isSeasonalPattern: true);

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 4, fix.FacilityId, fix.Client1Id, fix.ItemMediumId, "SKU-MED-02",
            InventoryMovementEventType.OutboundPick, 600m, now.AddDays(-10), unitCost: 10m);

        var seasonalComparison = await queryService.GetSkuVelocityMultiWindowComparisonAsync(
            fix.FacilityId, fix.ItemMediumId, now);

        Assert.NotNull(seasonalComparison);
        Assert.True(seasonalComparison.SeasonalPatternDetected);
        Assert.Equal("SeasonalSpike", seasonalComparison.DemandTrend);
    }

    [Fact]
    public async Task TrendPrecedence_SurgingAndSeasonalOverlap_DeterministicPrecedence()
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

        var now = DateTime.UtcNow;

        // Ingest demand that qualifies for both Surging (30d > 90d * 1.3) AND DemandSpike (30d >= 365d * 2.0)
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 600m, now.AddDays(-5), unitCost: 10m);

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 100m, now.AddDays(-60), unitCost: 10m);

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 3, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 100m, now.AddDays(-300), unitCost: 10m);

        var comparison = await queryService.GetSkuVelocityMultiWindowComparisonAsync(
            fix.FacilityId, fix.ItemFastId, now);

        Assert.NotNull(comparison);
        // DemandSpike must take precedence over generic Surging
        Assert.True(comparison.DemandSpikeDetected);
        Assert.Equal("DemandSpike", comparison.DemandTrend);
    }

    [Fact]
    public async Task HistoricalWindowRecalculation_OriginalClassificationRemainsReproducible()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projectionService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var historicalDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // Ingest historical movement events
        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 1, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.OutboundPick, 300m, historicalDate.AddDays(-15), unitCost: 10m);

        await projectionService.ProcessInventoryMovementEventAsync(
            Guid.NewGuid(), 2, fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            InventoryMovementEventType.Replenishment, 100m, historicalDate.AddDays(-10), unitCost: 10m);

        // Run calculation 1
        var run1 = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            VelocityObservationWindow.Days30, asOfUtc: historicalDate, version: 1);

        // Run calculation 2 for same historical period
        var run2 = await projectionService.CalculateSkuVelocityAsync(
            fix.FacilityId, fix.Client1Id, fix.ItemFastId, "SKU-FAST-01",
            VelocityObservationWindow.Days30, asOfUtc: historicalDate, version: 2);

        // All calculated metrics must match deterministically
        Assert.Equal(run1.TotalUnitsDemanded, run2.TotalUnitsDemanded);
        Assert.Equal(run1.DailyDemandVelocity, run2.DailyDemandVelocity);
        Assert.Equal(run1.VelocityCategory, run2.VelocityCategory);
        Assert.Equal(run1.TotalMovementEventsCount, run2.TotalMovementEventsCount);
        Assert.Equal(run1.TotalReplenishmentCount, run2.TotalReplenishmentCount);
        Assert.Equal(run1.DaysOfSupplyOperational, run2.DaysOfSupplyOperational);
    }
}
