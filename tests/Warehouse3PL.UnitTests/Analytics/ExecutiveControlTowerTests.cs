using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Comprehensive verification test suite for Phase 6.1:
/// Executive Control Tower, Real-Time Operational Visibility, Hourly Throughput Pacing,
/// Dock &amp; Yard Turn-Times, Order SLA Monitoring, Bottleneck Diagnostics &amp; Multi-Facility Rollups.
/// </summary>
public class ExecutiveControlTowerTests
{
    private class TestControlTowerFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ChicagoFacilityId { get; set; } = Guid.NewGuid();
        public Guid DallasFacilityId { get; set; } = Guid.NewGuid();
        public Guid AtlantaFacilityId { get; set; } = Guid.NewGuid();
        public Guid ClientAlphaId { get; set; } = Guid.NewGuid();
        public Guid ClientBetaId { get; set; } = Guid.NewGuid();
    }

    private static async Task<TestControlTowerFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestControlTowerFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var fac1 = new Facility
        {
            Id = fix.ChicagoFacilityId,
            FacilityCode = "CHI-01",
            Name = "Chicago Central Logistics Hub",
            IsActive = true
        };

        var fac2 = new Facility
        {
            Id = fix.DallasFacilityId,
            FacilityCode = "DAL-02",
            Name = "Dallas Distribution Center",
            IsActive = true
        };

        var fac3 = new Facility
        {
            Id = fix.AtlantaFacilityId,
            FacilityCode = "ATL-03",
            Name = "Atlanta Gateway Terminal",
            IsActive = true
        };

        var client1 = new Client
        {
            Id = fix.ClientAlphaId,
            CompanyName = "Alpha Omnichannel Retail",
            AccountCode = "AOR01",
            Status = ClientStatus.Active,
            Currency = "USD"
        };

        var client2 = new Client
        {
            Id = fix.ClientBetaId,
            CompanyName = "Beta Direct Brands",
            AccountCode = "BDB02",
            Status = ClientStatus.Active,
            Currency = "USD"
        };

        await context.Facilities.AddRangeAsync(fac1, fac2, fac3);
        await context.Clients.AddRangeAsync(client1, client2);
        await context.SaveChangesAsync();

        return fix;
    }

    #region 1. Hourly Throughput & Pacing Tests

    [Fact]
    public async Task Test_01_HourlyThroughput_Aggregates_Buckets_Correctly()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);

        // Events at 09:15 and 09:45 should fall into 09:00:00 bucket
        var time0915 = new DateTime(2026, 11, 13, 9, 15, 0, DateTimeKind.Utc);
        var time0945 = new DateTime(2026, 11, 13, 9, 45, 0, DateTimeKind.Utc);
        // Event at 10:10 should fall into 10:00:00 bucket
        var time1010 = new DateTime(2026, 11, 13, 10, 10, 0, DateTimeKind.Utc);

        await projService.ProcessOrderReceivedAsync(Guid.NewGuid(), 1, fix.ChicagoFacilityId, fix.ClientAlphaId, time0915, 20);
        await projService.ProcessPickCompletedAsync(Guid.NewGuid(), 2, fix.ChicagoFacilityId, fix.ClientAlphaId, time0945, lines: 40, units: 160, pickDurationHours: 1.5m);
        await projService.ProcessOrderReceivedAsync(Guid.NewGuid(), 3, fix.ChicagoFacilityId, fix.ClientAlphaId, time1010, 35);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var pacing = await queryService.GetHourlyThroughputPacingAsync(
            fix.ChicagoFacilityId,
            new DateTime(2026, 11, 13, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, pacing.Count);

        // 09:00 Bucket
        var bucket9 = pacing.First(p => p.HourTimestampUtc.Hour == 9);
        Assert.Equal(20, bucket9.OrdersReceivedCount);
        Assert.Equal(40, bucket9.LinesPickedCount);
        Assert.Equal(160, bucket9.UnitsPickedCount);
        Assert.Equal(106.67m, bucket9.UnitsPerLaborHour); // 160 / 1.5m = 106.67

        // 10:00 Bucket
        var bucket10 = pacing.First(p => p.HourTimestampUtc.Hour == 10);
        Assert.Equal(35, bucket10.OrdersReceivedCount);
        Assert.Equal(0, bucket10.UnitsPickedCount);
    }

    #endregion

    #region 2. Dock Door, Yard Occupancy & Turn-Time Analytics

    [Fact]
    public async Task Test_02_DockYard_Calculates_Wait_Service_And_TurnTimes_Accurately()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

        // Update physical capacity: 20 dock doors (12 occupied), 50 yard spots (35 occupied)
        await projService.ProcessDockYardCapacityUpdatedAsync(Guid.NewGuid(), 1, fix.ChicagoFacilityId, now, totalDoors: 20, occupiedDoors: 12, totalYardSpots: 50, occupiedYardSpots: 35);

        // Vehicle 1: CheckIn -> Spotted after 25m wait -> Dock Released after 45m service -> Departed (total turn time: 70m)
        await projService.ProcessTruckCheckedInAsync(Guid.NewGuid(), 2, fix.ChicagoFacilityId, now, 1);
        await projService.ProcessTruckSpottedAtDockAsync(Guid.NewGuid(), 3, fix.ChicagoFacilityId, now, yardWaitMinutes: 25m, 1);
        await projService.ProcessDockReleasedAsync(Guid.NewGuid(), 4, fix.ChicagoFacilityId, now, dockServiceMinutes: 45m, 1);
        await projService.ProcessTruckDepartedAsync(Guid.NewGuid(), 5, fix.ChicagoFacilityId, now, totalTurnTimeMinutes: 70m, 1);

        // Vehicle 2: CheckIn -> Spotted after 35m wait -> Dock Released after 55m service -> Departed (total turn time: 90m)
        await projService.ProcessTruckCheckedInAsync(Guid.NewGuid(), 6, fix.ChicagoFacilityId, now, 1);
        await projService.ProcessTruckSpottedAtDockAsync(Guid.NewGuid(), 7, fix.ChicagoFacilityId, now, yardWaitMinutes: 35m, 1);
        await projService.ProcessDockReleasedAsync(Guid.NewGuid(), 8, fix.ChicagoFacilityId, now, dockServiceMinutes: 55m, 1);
        await projService.ProcessTruckDepartedAsync(Guid.NewGuid(), 9, fix.ChicagoFacilityId, now, totalTurnTimeMinutes: 90m, 1);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var dockStatus = await queryService.GetDockYardStatusAsync(fix.ChicagoFacilityId, now);

        Assert.Equal(60.00m, dockStatus.DockDoorUtilizationPercent); // 12 / 20 = 60.00%
        Assert.Equal(70.00m, dockStatus.YardOccupancyPercent); // 35 / 50 = 70.00%
        Assert.Equal(2, dockStatus.TrucksCheckedInCount);
        Assert.Equal(2, dockStatus.TrucksDepartedCount);

        // Average wait time = (25 + 35) / 2 = 30.00 mins
        Assert.Equal(30.00m, dockStatus.AverageYardWaitTimeMinutes);
        // Average dock service time = (45 + 55) / 2 = 50.00 mins
        Assert.Equal(50.00m, dockStatus.AverageDockServiceTimeMinutes);
        // Average total turn time = (70 + 90) / 2 = 80.00 mins
        Assert.Equal(80.00m, dockStatus.AverageTotalTurnTimeMinutes);
    }

    [Fact]
    public void Test_03_DockYard_Rejects_Negative_Times_Or_Invalid_Counts()
    {
        var snapshot = FacilityDockYardSnapshot.Create(Guid.NewGuid(), DateTime.UtcNow, TimeGrain.Daily);

        Assert.Throws<ValidationException>(() => snapshot.RecordTruckCheckIn(-1));
        Assert.Throws<ValidationException>(() => snapshot.RecordTruckSpottedAtDock(-10m, 1));
        Assert.Throws<ValidationException>(() => snapshot.RecordDockReleased(-5m, 1));
        Assert.Throws<ValidationException>(() => snapshot.RecordTruckDeparture(-20m, 1));
        Assert.Throws<ValidationException>(() => snapshot.UpdateCapacity(10, 15, 20, 10)); // occupied > total doors
    }

    #endregion

    #region 3. Order SLA Countdown & Fulfillment Risk Monitoring

    [Fact]
    public async Task Test_04_SlaCountdown_Evaluates_All_Six_Boundary_States()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var refTime = new DateTime(2026, 11, 13, 14, 0, 0, DateTimeKind.Utc);

        // 1. OnTrack: Cutoff is 18:00 (4 hours remaining > 2 hours)
        var orderOnTrack = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-SLA-01",
            ClientId = fix.ClientAlphaId,
            FacilityId = fix.ChicagoFacilityId,
            CustomerName = "Target Store #102",
            Status = OutboundOrderStatus.Allocated,
            OrderDateUtc = refTime.AddHours(-2),
            RequiredShipDateUtc = refTime.AddHours(4)
        };

        // 2. ApproachingDeadline: Cutoff is 15:30 (90 mins remaining: between 60m and 120m)
        var orderApproaching = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-SLA-02",
            ClientId = fix.ClientAlphaId,
            FacilityId = fix.ChicagoFacilityId,
            CustomerName = "Walmart DC #44",
            Status = OutboundOrderStatus.InProgress,
            OrderDateUtc = refTime.AddHours(-4),
            RequiredShipDateUtc = refTime.AddMinutes(90)
        };

        // 3. AtRisk: Cutoff is 14:40 (40 mins remaining: <= 60m)
        var orderAtRisk = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-SLA-03",
            ClientId = fix.ClientAlphaId,
            FacilityId = fix.ChicagoFacilityId,
            CustomerName = "Best Buy Hub",
            Status = OutboundOrderStatus.InProgress,
            OrderDateUtc = refTime.AddHours(-3),
            RequiredShipDateUtc = refTime.AddMinutes(40)
        };

        // 4. Breached: Cutoff was 13:30 (30 mins past cutoff without completed dispatch)
        var orderBreached = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-SLA-04",
            ClientId = fix.ClientAlphaId,
            FacilityId = fix.ChicagoFacilityId,
            CustomerName = "Home Depot #12",
            Status = OutboundOrderStatus.Allocated,
            OrderDateUtc = refTime.AddHours(-6),
            RequiredShipDateUtc = refTime.AddMinutes(-30)
        };

        // 5. CompletedWithinSla: Completed at 13:00 with RequiredShipDate 13:30
        var orderCompletedIn = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-SLA-05",
            ClientId = fix.ClientAlphaId,
            FacilityId = fix.ChicagoFacilityId,
            CustomerName = "Costco #88",
            Status = OutboundOrderStatus.Completed,
            OrderDateUtc = refTime.AddHours(-8),
            RequiredShipDateUtc = refTime.AddMinutes(-30),
            DeliveryStatusUpdatedAtUtc = refTime.AddMinutes(-60) // Completed before req ship date
        };

        // 6. CompletedOutsideSla: Completed at 14:30 with RequiredShipDate 13:00
        var orderCompletedOut = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-SLA-06",
            ClientId = fix.ClientAlphaId,
            FacilityId = fix.ChicagoFacilityId,
            CustomerName = "Kroger #99",
            Status = OutboundOrderStatus.Completed,
            OrderDateUtc = refTime.AddHours(-8),
            RequiredShipDateUtc = refTime.AddMinutes(-60),
            DeliveryStatusUpdatedAtUtc = refTime.AddMinutes(30) // Completed after req ship date
        };

        await context.OutboundOrders.AddRangeAsync(orderOnTrack, orderApproaching, orderAtRisk, orderBreached, orderCompletedIn, orderCompletedOut);
        await context.SaveChangesAsync();

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var slaDashboard = await queryService.GetOrderFulfillmentSlaDashboardAsync(fix.ChicagoFacilityId, null, refTime);

        Assert.Equal(1, slaDashboard.OnTrackCount);
        Assert.Equal(1, slaDashboard.ApproachingDeadlineCount);
        Assert.Equal(1, slaDashboard.AtRiskCount);
        Assert.Equal(1, slaDashboard.BreachedCount);
        Assert.Equal(1, slaDashboard.CompletedWithinSlaCount);
        Assert.Equal(1, slaDashboard.CompletedOutsideSlaCount);

        // Total orders = 6. In compliance = CompletedWithin(1) + OnTrack(1) + Approaching(1) = 3 -> 50.00%
        Assert.Equal(50.00m, slaDashboard.SlaAdherencePercent);

        // Urgent list should contain Breached, AtRisk, and ApproachingDeadline in ascending remaining minutes
        Assert.Equal(3, slaDashboard.UrgentOrders.Count);
        Assert.Equal("ORD-SLA-04", slaDashboard.UrgentOrders[0].OrderNumber); // Breached (-30m)
        Assert.Equal("ORD-SLA-03", slaDashboard.UrgentOrders[1].OrderNumber); // AtRisk (40m)
        Assert.Equal("ORD-SLA-02", slaDashboard.UrgentOrders[2].OrderNumber); // Approaching (90m)
    }

    [Fact]
    public async Task Test_05_SlaCountdown_Timezone_Normalization()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // 14:00 Central Time = 20:00 UTC
        var cutoffUtc = new DateTime(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);
        var currentUtc = new DateTime(2026, 11, 13, 19, 15, 0, DateTimeKind.Utc); // 45 mins before cutoff

        var order = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-TZ-01",
            ClientId = fix.ClientAlphaId,
            FacilityId = fix.ChicagoFacilityId,
            CustomerName = "Nordstrom Direct",
            Status = OutboundOrderStatus.InProgress,
            OrderDateUtc = currentUtc.AddHours(-3),
            RequiredShipDateUtc = cutoffUtc
        };

        await context.OutboundOrders.AddAsync(order);
        await context.SaveChangesAsync();

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var dashboard = await queryService.GetOrderFulfillmentSlaDashboardAsync(fix.ChicagoFacilityId, null, currentUtc);

        // 45 mins remaining in UTC evaluation corresponds directly to AtRisk
        Assert.Equal(1, dashboard.AtRiskCount);
        Assert.Equal(45.0, dashboard.UrgentOrders[0].RemainingMinutes);
    }

    #endregion

    #region 4. Bottleneck Diagnostics & Heatmaps

    [Fact]
    public async Task Test_06_Bottleneck_Diagnostic_Receiving_Congestion_Severity()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        // Stage critical receiving congestion: average yard wait time = 75 minutes, yard occupancy = 90%
        await projService.ProcessDockYardCapacityUpdatedAsync(Guid.NewGuid(), 1, fix.ChicagoFacilityId, now, totalDoors: 10, occupiedDoors: 8, totalYardSpots: 20, occupiedYardSpots: 18);
        await projService.ProcessTruckCheckedInAsync(Guid.NewGuid(), 2, fix.ChicagoFacilityId, now, 6);
        await projService.ProcessTruckSpottedAtDockAsync(Guid.NewGuid(), 3, fix.ChicagoFacilityId, now, yardWaitMinutes: 450m, count: 6); // avg 75m

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var bottlenecks = await queryService.GetFacilityBottlenecksAsync(fix.ChicagoFacilityId, now);

        Assert.Equal(BottleneckSeverity.Critical, bottlenecks.ReceivingCongestion.Severity);
        Assert.Contains("Critical yard congestion", bottlenecks.ReceivingCongestion.SummaryMessage);
        // Health score should incur 25 point penalty -> 75
        Assert.Equal(75, bottlenecks.OverallHealthScore);
    }

    [Fact]
    public async Task Test_07_Bottleneck_Diagnostic_Picking_Backlog_Severity()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // Create 25 allocated orders for Chicago (Warning threshold: > 20 orders)
        for (int i = 0; i < 25; i++)
        {
            await context.OutboundOrders.AddAsync(new OutboundOrder
            {
                Id = Guid.NewGuid(),
                OrderNumber = $"ORD-PICK-BACKLOG-{i:D2}",
                ClientId = fix.ClientAlphaId,
                FacilityId = fix.ChicagoFacilityId,
                CustomerName = $"Retail Store {i}",
                Status = OutboundOrderStatus.Allocated,
                OrderDateUtc = DateTime.UtcNow.AddHours(-1)
            });
        }
        await context.SaveChangesAsync();

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var bottlenecks = await queryService.GetFacilityBottlenecksAsync(fix.ChicagoFacilityId, DateTime.UtcNow);

        Assert.Equal(BottleneckSeverity.Warning, bottlenecks.PickingBacklog.Severity);
        Assert.Equal(25, bottlenecks.PickingBacklog.QueueDepth);
        // Warning penalty is 10 points -> 90
        Assert.Equal(90, bottlenecks.OverallHealthScore);
    }

    #endregion

    #region 5. Multi-Facility Executive Rollup & Comparative Benchmarks

    [Fact]
    public async Task Test_08_MultiFacility_Executive_Rollup_Calculates_Network_Benchmarks()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow.Date;

        // Chicago: 100 received, 80 dispatched (SDA 80%), Pick: 800 units over 8 hours (UPH = 100)
        await projService.ProcessOrderReceivedAsync(Guid.NewGuid(), 1, fix.ChicagoFacilityId, fix.ClientAlphaId, now, 100);
        await projService.ProcessPickCompletedAsync(Guid.NewGuid(), 2, fix.ChicagoFacilityId, fix.ClientAlphaId, now, lines: 200, units: 800, pickDurationHours: 8m);
        for (int i = 0; i < 8; i++)
        {
            await projService.ProcessOrderDispatchedAsync(Guid.NewGuid(), 10 + i, fix.ChicagoFacilityId, fix.ClientAlphaId, now, packages: 10, cycleTimeHours: 2.5m, eligibleCutoff: true, sameDayDispatched: true);
        }
        for (int i = 0; i < 2; i++)
        {
            await projService.ProcessOrderDispatchedAsync(Guid.NewGuid(), 20 + i, fix.ChicagoFacilityId, fix.ClientAlphaId, now, packages: 10, cycleTimeHours: 5.0m, eligibleCutoff: true, sameDayDispatched: false);
        }
        await projService.ProcessInventorySnapshotAsync(Guid.NewGuid(), 30, fix.ChicagoFacilityId, null, now, totalLocations: 1000, occupiedLocations: 800, totalCbm: 5000m, occupiedCbm: 4000m, 200, 10000, 500000m, 5, 200, 10000m);

        // Dallas: 50 received, 50 dispatched (SDA 100%), Pick: 600 units over 4 hours (UPH = 150)
        await projService.ProcessOrderReceivedAsync(Guid.NewGuid(), 31, fix.DallasFacilityId, fix.ClientBetaId, now, 50);
        await projService.ProcessPickCompletedAsync(Guid.NewGuid(), 32, fix.DallasFacilityId, fix.ClientBetaId, now, lines: 150, units: 600, pickDurationHours: 4m);
        for (int i = 0; i < 5; i++)
        {
            await projService.ProcessOrderDispatchedAsync(Guid.NewGuid(), 40 + i, fix.DallasFacilityId, fix.ClientBetaId, now, packages: 10, cycleTimeHours: 2.0m, eligibleCutoff: true, sameDayDispatched: true);
        }
        await projService.ProcessInventorySnapshotAsync(Guid.NewGuid(), 50, fix.DallasFacilityId, null, now, totalLocations: 1000, occupiedLocations: 600, totalCbm: 5000m, occupiedCbm: 3000m, 150, 8000, 400000m, 2, 100, 5000m);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var multiFac = await queryService.GetMultiFacilityComparisonAsync(now);

        Assert.Equal(3, multiFac.Facilities.Count); // Chicago, Dallas, Atlanta
        Assert.Equal(150, multiFac.TotalOrdersReceivedToday); // 100 + 50 + 0
        Assert.Equal(15, multiFac.TotalOrdersDispatchedToday); // 10 + 5 + 0

        var chiSummary = multiFac.Facilities.First(f => f.FacilityId == fix.ChicagoFacilityId);
        Assert.Equal(80.00m, chiSummary.SameDayDispatchAdherencePercent);
        Assert.Equal(100.00m, chiSummary.UnitsPerLaborHour);
        Assert.Equal(80.00m, chiSummary.VolumetricCubeFillPercent);

        var dalSummary = multiFac.Facilities.First(f => f.FacilityId == fix.DallasFacilityId);
        Assert.Equal(100.00m, dalSummary.SameDayDispatchAdherencePercent);
        Assert.Equal(150.00m, dalSummary.UnitsPerLaborHour);
        Assert.Equal(60.00m, dalSummary.VolumetricCubeFillPercent);
    }

    #endregion

    #region 6. Multi-Tenant & Facility Access Security

    [Fact]
    public async Task Test_09_ControlTower_Enforces_Tenant_Security_Boundary()
    {
        var fix = await SeedTestEnvironmentAsync();

        // Non-staff user restricted to ClientAlpha
        var clientAlphaUser = new TestCurrentUserContext
        {
            UserType = UserType.Client,
            TenantId = fix.ClientAlphaId
        };

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { TenantId = fix.ClientAlphaId, BypassTenantFilter = false },
            clientAlphaUser,
            fix.DbName);

        var queryService = new AnalyticsQueryService(context, clientAlphaUser, NullLogger<AnalyticsQueryService>.Instance);

        // Attempting to query ClientBeta SLA or profitability must throw UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetOrderFulfillmentSlaDashboardAsync(fix.ChicagoFacilityId, fix.ClientBetaId));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetClientProfitabilityAsync(fix.ClientBetaId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow));
    }

    [Fact]
    public async Task Test_10_ControlTower_Enforces_Facility_Security_Boundary()
    {
        var fix = await SeedTestEnvironmentAsync();

        // Supervisor restricted to Chicago
        var chicagoSupervisor = new TestCurrentUserContext
        {
            UserType = UserType.Client,
            FacilityId = fix.ChicagoFacilityId
        };

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            chicagoSupervisor,
            fix.DbName);

        var queryService = new AnalyticsQueryService(context, chicagoSupervisor, NullLogger<AnalyticsQueryService>.Instance);

        // Attempting to query Dallas dock/yard status or bottlenecks must throw UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetDockYardStatusAsync(fix.DallasFacilityId));

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetFacilityBottlenecksAsync(fix.DallasFacilityId));
    }

    #endregion

    #region 7. Metadata Freshness & Executive Overview

    [Fact]
    public async Task Test_11_ControlTower_Exposes_Accurate_Metric_Freshness_Metadata()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var overview = await queryService.GetExecutiveOverviewAsync(fix.ChicagoFacilityId, DateTime.UtcNow);

        Assert.NotNull(overview.FreshnessMetadata);
        Assert.Equal(DataFreshnessStatus.Live, overview.FreshnessMetadata.FreshnessStatus);
        Assert.Equal("America/Chicago", overview.FreshnessMetadata.FacilityTimezone);
        Assert.Equal(1, overview.FreshnessMetadata.ProjectionVersion);
    }

    [Fact]
    public async Task Test_12_DockYard_Event_Ingestion_Idempotency()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var eventId = Guid.NewGuid();
        var date = DateTime.UtcNow;

        // Process truck check-in
        await projService.ProcessTruckCheckedInAsync(eventId, 1, fix.ChicagoFacilityId, date, 1);

        var snapshot = await context.FacilityDockYardSnapshots
            .FirstOrDefaultAsync(s => s.FacilityId == fix.ChicagoFacilityId && s.DateUtc == date.Date);

        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot.TrucksCheckedInCount);

        // Replay exact same event (at-least-once delivery duplicate)
        await projService.ProcessTruckCheckedInAsync(eventId, 1, fix.ChicagoFacilityId, date, 1);

        snapshot = await context.FacilityDockYardSnapshots
            .FirstOrDefaultAsync(s => s.FacilityId == fix.ChicagoFacilityId && s.DateUtc == date.Date);

        // Count must remain 1, never double-counted to 2
        Assert.Equal(1, snapshot!.TrucksCheckedInCount);
    }

    [Fact]
    public async Task Test_13_ExecutiveOverview_Consolidates_All_Control_Tower_Panels()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        await projService.ProcessOrderReceivedAsync(Guid.NewGuid(), 1, fix.ChicagoFacilityId, fix.ClientAlphaId, now, 80);
        await projService.ProcessPickCompletedAsync(Guid.NewGuid(), 2, fix.ChicagoFacilityId, fix.ClientAlphaId, now, 100, 400, 4m);
        await projService.ProcessDockYardCapacityUpdatedAsync(Guid.NewGuid(), 3, fix.ChicagoFacilityId, now, 10, 5, 30, 15);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var overview = await queryService.GetExecutiveOverviewAsync(fix.ChicagoFacilityId, now);

        Assert.NotNull(overview);
        Assert.Equal(80, overview.Summary.OrdersReceivedToday);
        Assert.Equal(400, overview.Summary.UnitsPickedToday);
        Assert.Equal(50.00m, overview.DockYardStatus.DockDoorUtilizationPercent);
        Assert.Equal(50.00m, overview.DockYardStatus.YardOccupancyPercent);
        Assert.Equal(100, overview.Bottlenecks.OverallHealthScore);
        Assert.Equal(DataFreshnessStatus.Live, overview.FreshnessMetadata.FreshnessStatus);
    }

    [Fact]
    public async Task Test_14_Replay_Purges_Hourly_And_DockYard_Snapshots_Cleanly()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow;

        await projService.ProcessOrderReceivedAsync(Guid.NewGuid(), 1, fix.ChicagoFacilityId, fix.ClientAlphaId, now, 10);
        await projService.ProcessTruckCheckedInAsync(Guid.NewGuid(), 2, fix.ChicagoFacilityId, now, 2);

        var hourlyCount = await context.HourlyFacilityThroughputSnapshots.CountAsync();
        var dockCount = await context.FacilityDockYardSnapshots.CountAsync();
        Assert.Equal(1, hourlyCount);
        Assert.Equal(1, dockCount);

        // Replay FacilityProductivity (purges hourly snapshots)
        await projService.ReplayProjectionsAsync(AnalyticsProjectionService.FacilityProductivityProjection, ProjectionReplayMode.FromBeginning);
        Assert.Equal(0, await context.HourlyFacilityThroughputSnapshots.CountAsync());

        // Replay DockYard (purges dock snapshots)
        await projService.ReplayProjectionsAsync(AnalyticsProjectionService.DockYardProjection, ProjectionReplayMode.FromBeginning);
        Assert.Equal(0, await context.FacilityDockYardSnapshots.CountAsync());
    }

    #endregion
}

