using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

/// <summary>
/// Unit and integration tests verifying Phase 6.0:
/// Analytics Foundation, Read Models, KPI Calculation Engine, Idempotent Event Ingestion &amp; Operational Control Tower.
/// </summary>
public class AnalyticsProjectionAndQueryTests
{
    private class TestAnalyticsFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid OtherFacilityId { get; set; } = Guid.NewGuid();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid UnauthorizedClientId { get; set; } = Guid.NewGuid();
    }

    private static async Task<TestAnalyticsFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestAnalyticsFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var facility = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "CHI-01",
            Name = "Chicago Central Logistics Hub",
            IsActive = true
        };

        var otherFacility = new Facility
        {
            Id = fix.OtherFacilityId,
            FacilityCode = "DAL-02",
            Name = "Dallas Distribution Center",
            IsActive = true
        };

        var client = new Client
        {
            Id = fix.ClientId,
            CompanyName = "Apex Retail Brands",
            AccountCode = "ARB01",
            Status = ClientStatus.Active,
            Currency = "USD"
        };

        var otherClient = new Client
        {
            Id = fix.UnauthorizedClientId,
            CompanyName = "Zenith Consumer Products",
            AccountCode = "ZCP02",
            Status = ClientStatus.Active,
            Currency = "USD"
        };

        await context.Facilities.AddRangeAsync(facility, otherFacility);
        await context.Clients.AddRangeAsync(client, otherClient);
        await context.SaveChangesAsync();

        return fix;
    }

    #region Domain Entity & KPI Calculation Tests

    [Fact]
    public void Test_01_DailyFacilityProductivitySummary_Records_All_Throughput_Milestones()
    {
        var facilityId = Guid.NewGuid();
        var dateUtc = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
        var summary = DailyFacilityProductivitySummary.Create(facilityId, dateUtc, TimeGrain.Daily);

        summary.RecordOrderReceived(100);
        summary.RecordOrderAllocated(95);
        summary.RecordPickCompleted(lines: 450, units: 1200, pickDurationHours: 10m);
        summary.RecordPackCompleted(cartons: 380);
        summary.RecordOrderDispatched(packages: 380, cycleTimeHours: 3.5m, eligibleCutoff: true, sameDayDispatched: true);
        summary.RecordInboundReceived(pallets: 40);
        summary.RecordPutawayCompleted(pallets: 38, putawayDurationHours: 4.5m);
        summary.RecordReturnInspected(items: 12);

        Assert.Equal(100, summary.OrdersReceivedCount);
        Assert.Equal(95, summary.OrdersAllocatedCount);
        Assert.Equal(450, summary.LinesPickedCount);
        Assert.Equal(1200, summary.UnitsPickedCount);
        Assert.Equal(380, summary.CartonsPackedCount);
        Assert.Equal(1, summary.OrdersDispatchedCount);
        Assert.Equal(380, summary.PackagesDispatchedCount);
        Assert.Equal(40, summary.InboundPalletsReceivedCount);
        Assert.Equal(38, summary.InboundPutawayCompletedCount);
        Assert.Equal(12, summary.ReturnsInspectedCount);
    }

    [Fact]
    public void Test_02_DailyFacilityProductivitySummary_Calculates_UPH_And_PPH_Correctly()
    {
        var facilityId = Guid.NewGuid();
        var summary = DailyFacilityProductivitySummary.Create(facilityId, DateTime.UtcNow, TimeGrain.Daily);

        // Pick 1,200 units over 10 pick hours -> UPH = 120.00
        summary.RecordPickCompleted(lines: 100, units: 1200, pickDurationHours: 10m);
        Assert.Equal(120.00m, summary.UnitsPerLaborHour);

        // Putaway 50 pallets over 5 putaway hours -> PPH = 10.00
        summary.RecordPutawayCompleted(pallets: 50, putawayDurationHours: 5m);
        Assert.Equal(10.00m, summary.PutawayPerLaborHour);
    }

    [Fact]
    public void Test_03_DailyFacilityProductivitySummary_Calculates_CycleTime_And_SDA()
    {
        var facilityId = Guid.NewGuid();
        var summary = DailyFacilityProductivitySummary.Create(facilityId, DateTime.UtcNow, TimeGrain.Daily);

        // Order 1: 2.0 hours, same-day
        summary.RecordOrderDispatched(packages: 1, cycleTimeHours: 2.0m, eligibleCutoff: true, sameDayDispatched: true);
        // Order 2: 4.0 hours, same-day
        summary.RecordOrderDispatched(packages: 1, cycleTimeHours: 4.0m, eligibleCutoff: true, sameDayDispatched: true);
        // Order 3: 6.0 hours, next-day (missed same-day)
        summary.RecordOrderDispatched(packages: 1, cycleTimeHours: 6.0m, eligibleCutoff: true, sameDayDispatched: false);

        // Mean cycle time = (2 + 4 + 6) / 3 = 4.00 hours
        Assert.Equal(4.00m, summary.AverageFulfillmentCycleTimeHours);
        // SDA% = 2 same-day / 3 eligible = 66.67%
        Assert.Equal(66.67m, summary.SameDayDispatchAdherencePercent);
    }

    [Fact]
    public void Test_04_DailyFacilityProductivitySummary_Throws_On_Negative_Values()
    {
        var summary = DailyFacilityProductivitySummary.Create(Guid.NewGuid(), DateTime.UtcNow, TimeGrain.Daily);

        Assert.Throws<ValidationException>(() => summary.RecordOrderReceived(-5));
        Assert.Throws<ValidationException>(() => summary.RecordPickCompleted(-1, 10, 1m));
        Assert.Throws<ValidationException>(() => summary.RecordPickCompleted(10, -5, 1m));
        Assert.Throws<ValidationException>(() => summary.RecordPickCompleted(10, 10, -1m));
        Assert.Throws<ValidationException>(() => summary.RecordPutawayCompleted(-2, 1m));
    }

    [Fact]
    public void Test_05_DailyClientProfitabilitySummary_Calculates_Revenue_And_Margins()
    {
        var clientId = Guid.NewGuid();
        var summary = DailyClientProfitabilitySummary.Create(clientId, DateTime.UtcNow, TimeGrain.Daily);

        summary.AddStorageRevenue(500.00m);
        summary.AddHandlingRevenue(300.00m, estimatedLaborCost: 100.00m);
        summary.AddFreightRevenue(billedFreight: 1200.00m, carrierCost: 900.00m);
        summary.AddVasRevenue(150.00m, estimatedLaborCost: 50.00m);

        // Total Billed = 500 + 300 + 1200 + 150 = 2,150.00
        Assert.Equal(2150.00m, summary.TotalBilledRevenue);

        // Net Freight Margin = 1200 - 900 = 300.00 (25.00%)
        Assert.Equal(300.00m, summary.NetFreightMargin);
        Assert.Equal(25.00m, summary.FreightMarginPercent);

        // Net Commercial Margin = 2150 - (900 carrier + 150 labor) = 1,100.00 (51.16%)
        Assert.Equal(1100.00m, summary.NetCommercialMargin);
        Assert.Equal(51.16m, summary.CommercialMarginPercent);
    }

    [Fact]
    public void Test_06_DailyClientProfitabilitySummary_Tracks_Invoices_And_AR_Realization()
    {
        var clientId = Guid.NewGuid();
        var summary = DailyClientProfitabilitySummary.Create(clientId, DateTime.UtcNow, TimeGrain.Daily);

        // Invoice issued for 1000 subtotal + 50 tax = 1050
        summary.RecordInvoiceIssued(subtotal: 1000m, tax: 50m);
        Assert.Equal(1, summary.InvoicesIssuedCount);
        Assert.Equal(1050m, summary.TotalInvoicedAmount);
        Assert.Equal(1050m, summary.OutstandingArBalance);

        // Payment received: 600
        summary.RecordPaymentCollected(600m);
        Assert.Equal(600m, summary.PaymentsCollectedAmount);
        Assert.Equal(450m, summary.OutstandingArBalance);

        // Credit applied: 50
        summary.RecordCreditApplied(50m);
        Assert.Equal(50m, summary.CreditsAppliedAmount);
        Assert.Equal(400m, summary.OutstandingArBalance);
    }

    [Fact]
    public void Test_07_DailyInventoryUtilizationSnapshot_Calculates_Cube_And_Pallet_Occupancy()
    {
        var facilityId = Guid.NewGuid();
        var snapshot = DailyInventoryUtilizationSnapshot.Create(facilityId, DateTime.UtcNow, TimeGrain.Daily);

        // 800 occupied out of 1000 pallet locations -> 80.00%
        // 4,500 m3 occupied out of 5,000 m3 total -> 90.00%
        snapshot.UpdateCapacity(totalLocations: 1000, occupiedLocations: 800, totalCbm: 5000m, occupiedCbm: 4500m);

        Assert.Equal(80.00m, snapshot.PalletPositionOccupancyPercent);
        Assert.Equal(90.00m, snapshot.VolumetricCubeFillPercent);
    }

    [Fact]
    public void Test_08_DailyInventoryUtilizationSnapshot_Tracks_DeadStock_Ratio()
    {
        var facilityId = Guid.NewGuid();
        var snapshot = DailyInventoryUtilizationSnapshot.Create(facilityId, DateTime.UtcNow, TimeGrain.Daily);

        // 250 active SKUs, 50,000 units, $1,000,000 total inventory value
        // 25 dead SKUs, 5,000 dead units, $150,000 dead stock value -> 15.00% dead ratio
        snapshot.UpdateInventoryComposition(
            activeSkus: 250,
            totalUnits: 50000,
            totalValue: 1000000m,
            deadSkus: 25,
            deadUnits: 5000,
            deadValue: 150000m);

        Assert.Equal(15.00m, snapshot.DeadStockRatioPercent);
        Assert.Equal(25, snapshot.DeadStockSkusCount);
        Assert.Equal(150000m, snapshot.DeadStockValue);
    }

    [Fact]
    public void Test_09_DailyCarrierPerformanceRecord_Calculates_SLA_And_Transit_Days()
    {
        var facilityId = Guid.NewGuid();
        var record = DailyCarrierPerformanceRecord.Create("FEDEX", CarrierServiceLevel.StandardGround, facilityId, DateTime.UtcNow, TimeGrain.Daily);

        record.RecordDispatch(10);
        Assert.Equal(10, record.DispatchedShipmentsCount);
        Assert.Equal(10, record.InTransitShipmentsCount);

        // 8 delivered on time, first attempt, 2 days transit
        for (int i = 0; i < 8; i++)
        {
            record.RecordDelivery(isOnTime: true, isFirstAttempt: true, transitDays: 2.0m);
        }

        // 2 delivered late, second attempt, 4 days transit
        for (int i = 0; i < 2; i++)
        {
            record.RecordDelivery(isOnTime: false, isFirstAttempt: false, transitDays: 4.0m);
        }

        // 1 exception
        record.RecordException(1);

        // On-Time = 8 / 10 = 80.00%
        Assert.Equal(80.00m, record.OnTimeDeliveryRatePercent);
        // First-Attempt = 8 / 10 = 80.00%
        Assert.Equal(80.00m, record.FirstAttemptDeliveryRatePercent);
        // Delivery Exception Rate = 1 / 10 = 10.00%
        Assert.Equal(10.00m, record.DeliveryExceptionRatePercent);
        // Average Transit Days = (8*2 + 2*4) / 10 = 24 / 10 = 2.40 days
        Assert.Equal(2.40m, record.AverageTransitDays);
        Assert.Equal(0, record.InTransitShipmentsCount);
    }

    #endregion

    #region Projection Service & Ingestion Tests

    [Fact]
    public async Task Test_10_AnalyticsProjectionService_Fulfillment_Event_Idempotency()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var eventId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);

        // Process event first time
        await service.ProcessOrderReceivedAsync(eventId, 1, fix.FacilityId, fix.ClientId, timestamp, 5);

        var summary = await context.DailyFacilityProductivitySummaries
            .FirstOrDefaultAsync(s => s.FacilityId == fix.FacilityId && s.DateUtc == timestamp.Date);

        Assert.NotNull(summary);
        Assert.Equal(5, summary.OrdersReceivedCount);

        // Re-process identical event ID (at-least-once replay simulation)
        await service.ProcessOrderReceivedAsync(eventId, 1, fix.FacilityId, fix.ClientId, timestamp, 5);

        summary = await context.DailyFacilityProductivitySummaries
            .FirstOrDefaultAsync(s => s.FacilityId == fix.FacilityId && s.DateUtc == timestamp.Date);

        // Order count must remain 5, never double-counted to 10
        Assert.Equal(5, summary!.OrdersReceivedCount);
    }

    [Fact]
    public async Task Test_11_AnalyticsProjectionService_Throughput_Milestones_Aggregate_Correctly()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var date = new DateTime(2026, 11, 13, 8, 0, 0, DateTimeKind.Utc);

        await service.ProcessOrderReceivedAsync(Guid.NewGuid(), 1, fix.FacilityId, fix.ClientId, date, 20);
        await service.ProcessOrderAllocatedAsync(Guid.NewGuid(), 2, fix.FacilityId, fix.ClientId, date, 18);
        await service.ProcessPickCompletedAsync(Guid.NewGuid(), 3, fix.FacilityId, fix.ClientId, date, lines: 90, units: 360, pickDurationHours: 3m);
        await service.ProcessPackCompletedAsync(Guid.NewGuid(), 4, fix.FacilityId, fix.ClientId, date, cartons: 72);
        await service.ProcessOrderDispatchedAsync(Guid.NewGuid(), 5, fix.FacilityId, fix.ClientId, date, packages: 72, cycleTimeHours: 2.5m, eligibleCutoff: true, sameDayDispatched: true);
        await service.ProcessInboundReceivedAsync(Guid.NewGuid(), 6, fix.FacilityId, fix.ClientId, date, pallets: 15);
        await service.ProcessPutawayCompletedAsync(Guid.NewGuid(), 7, fix.FacilityId, fix.ClientId, date, pallets: 15, putawayDurationHours: 1.5m);
        await service.ProcessReturnInspectedAsync(Guid.NewGuid(), 8, fix.FacilityId, fix.ClientId, date, returnItems: 4);

        var summary = await context.DailyFacilityProductivitySummaries
            .FirstOrDefaultAsync(s => s.FacilityId == fix.FacilityId && s.DateUtc == date.Date);

        Assert.NotNull(summary);
        Assert.Equal(20, summary.OrdersReceivedCount);
        Assert.Equal(18, summary.OrdersAllocatedCount);
        Assert.Equal(90, summary.LinesPickedCount);
        Assert.Equal(360, summary.UnitsPickedCount);
        Assert.Equal(72, summary.CartonsPackedCount);
        Assert.Equal(1, summary.OrdersDispatchedCount);
        Assert.Equal(72, summary.PackagesDispatchedCount);
        Assert.Equal(15, summary.InboundPalletsReceivedCount);
        Assert.Equal(15, summary.InboundPutawayCompletedCount);
        Assert.Equal(4, summary.ReturnsInspectedCount);
        Assert.Equal(120.00m, summary.UnitsPerLaborHour);
        Assert.Equal(10.00m, summary.PutawayPerLaborHour);
    }

    [Fact]
    public async Task Test_12_AnalyticsProjectionService_Commercial_Activities_Update_PAndL()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var date = new DateTime(2026, 11, 13, 11, 0, 0, DateTimeKind.Utc);

        await service.ProcessCommercialActivityRatedAsync(Guid.NewGuid(), 1, fix.ClientId, fix.FacilityId, date, BillableActivityType.StoragePalletDay, amount: 250m);
        await service.ProcessCommercialActivityRatedAsync(Guid.NewGuid(), 2, fix.ClientId, fix.FacilityId, date, BillableActivityType.PickUnit, amount: 150m, directCost: 50m);
        await service.ProcessCommercialActivityRatedAsync(Guid.NewGuid(), 3, fix.ClientId, fix.FacilityId, date, BillableActivityType.CarrierFreight, amount: 600m, directCost: 450m);

        // Facility-specific summary
        var facilitySummary = await context.DailyClientProfitabilitySummaries
            .FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.FacilityId == fix.FacilityId && s.DateUtc == date.Date);

        Assert.NotNull(facilitySummary);
        Assert.Equal(250m, facilitySummary.BilledStorageRevenue);
        Assert.Equal(150m, facilitySummary.BilledHandlingRevenue);
        Assert.Equal(600m, facilitySummary.BilledFreightRevenue);
        Assert.Equal(1000m, facilitySummary.TotalBilledRevenue);
        Assert.Equal(150m, facilitySummary.NetFreightMargin); // 600 - 450
        Assert.Equal(500m, facilitySummary.NetCommercialMargin); // 1000 - (450 + 50)

        // Consolidated summary (FacilityId = null)
        var clientSummary = await context.DailyClientProfitabilitySummaries
            .FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.FacilityId == null && s.DateUtc == date.Date);

        Assert.NotNull(clientSummary);
        Assert.Equal(1000m, clientSummary.TotalBilledRevenue);
    }

    [Fact]
    public async Task Test_13_AnalyticsProjectionService_Carrier_Events_Update_Performance()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var date = new DateTime(2026, 11, 13, 14, 0, 0, DateTimeKind.Utc);

        await service.ProcessCarrierDispatchedAsync(Guid.NewGuid(), 1, "UPS", CarrierServiceLevel.StandardGround, fix.FacilityId, date, count: 5);
        await service.ProcessCarrierDeliveredAsync(Guid.NewGuid(), 2, "UPS", CarrierServiceLevel.StandardGround, fix.FacilityId, date, isOnTime: true, isFirstAttempt: true, transitDays: 1.5m);
        await service.ProcessCarrierExceptionAsync(Guid.NewGuid(), 3, "UPS", CarrierServiceLevel.StandardGround, fix.FacilityId, date, count: 1);

        var record = await context.DailyCarrierPerformanceRecords
            .FirstOrDefaultAsync(r => r.CarrierCode == "UPS" && r.FacilityId == fix.FacilityId && r.DateUtc == date.Date);

        Assert.NotNull(record);
        Assert.Equal(5, record.DispatchedShipmentsCount);
        Assert.Equal(1, record.DeliveredShipmentsCount);
        Assert.Equal(4, record.InTransitShipmentsCount);
        Assert.Equal(1, record.DeliveryExceptionsCount);
        Assert.Equal(100.00m, record.OnTimeDeliveryRatePercent);
    }

    [Fact]
    public async Task Test_14_AnalyticsProjectionService_Replay_FromBeginning_Restores_Journal()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff, RolesList = ["SuperAdmin"] },
            fix.DbName);

        var service = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var date = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

        await service.ProcessOrderReceivedAsync(Guid.NewGuid(), 10, fix.FacilityId, fix.ClientId, date, 50);

        var checkpoint = await service.GetOrCreateCheckpointAsync(AnalyticsProjectionService.FacilityProductivityProjection);
        Assert.Equal(10, checkpoint.LastProcessedSequenceNumber);
        Assert.Equal(ProjectionStatus.Active, checkpoint.Status);

        // Trigger Replay FromBeginning
        await service.ReplayProjectionsAsync(AnalyticsProjectionService.FacilityProductivityProjection, ProjectionReplayMode.FromBeginning);

        var replayedCheckpoint = await service.GetOrCreateCheckpointAsync(AnalyticsProjectionService.FacilityProductivityProjection);
        Assert.Equal(10, replayedCheckpoint.LastProcessedSequenceNumber);
        Assert.Equal(ProjectionStatus.Active, replayedCheckpoint.Status);

        // Complete journal effect restored
        var summariesCount = await context.DailyFacilityProductivitySummaries.CountAsync();
        Assert.Equal(1, summariesCount);
    }

    #endregion

    #region Query Service & Control Tower Tests

    [Fact]
    public async Task Test_15_AnalyticsQueryService_Enforces_Tenant_Isolation()
    {
        var fix = await SeedTestEnvironmentAsync();

        // Non-staff user belonging to Client A
        var restrictedUser = new TestCurrentUserContext
        {
            UserType = UserType.Client,
            TenantId = fix.ClientId
        };

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = false },
            restrictedUser,
            fix.DbName);

        var queryService = new AnalyticsQueryService(context, restrictedUser, NullLogger<AnalyticsQueryService>.Instance);

        // Attempting to query UnauthorizedClientId should throw UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetClientProfitabilityAsync(
                fix.UnauthorizedClientId,
                DateTime.UtcNow.AddDays(-7),
                DateTime.UtcNow));
    }

    [Fact]
    public async Task Test_16_AnalyticsQueryService_Enforces_Facility_Access_Restriction()
    {
        var fix = await SeedTestEnvironmentAsync();

        // User restricted to Chicago hub
        var facilityRestrictedUser = new TestCurrentUserContext
        {
            UserType = UserType.Client,
            FacilityId = fix.FacilityId
        };

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            facilityRestrictedUser,
            fix.DbName);

        var queryService = new AnalyticsQueryService(context, facilityRestrictedUser, NullLogger<AnalyticsQueryService>.Instance);

        // Attempting to query Dallas facility should throw UnauthorizedException
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            queryService.GetFacilityProductivityAsync(
                fix.OtherFacilityId,
                DateTime.UtcNow.AddDays(-7),
                DateTime.UtcNow));
    }

    [Fact]
    public async Task Test_17_AnalyticsQueryService_Returns_Facility_Productivity_Time_Series()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var day1 = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

        await projService.ProcessOrderReceivedAsync(Guid.NewGuid(), 1, fix.FacilityId, fix.ClientId, day1, 40);
        await projService.ProcessOrderReceivedAsync(Guid.NewGuid(), 2, fix.FacilityId, fix.ClientId, day2, 60);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var timeSeries = await queryService.GetFacilityProductivityAsync(fix.FacilityId, day1, day2);

        Assert.Equal(2, timeSeries.Count);
        Assert.Equal(40, timeSeries[0].OrdersReceivedCount);
        Assert.Equal(60, timeSeries[1].OrdersReceivedCount);
    }

    [Fact]
    public async Task Test_18_AnalyticsQueryService_Returns_Executive_Control_Tower_Summary()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var projService = new AnalyticsProjectionService(context, NullLogger<AnalyticsProjectionService>.Instance);
        var now = DateTime.UtcNow.Date;

        // Populate today's activities
        await projService.ProcessOrderReceivedAsync(Guid.NewGuid(), 1, fix.FacilityId, fix.ClientId, now, 150);
        await projService.ProcessPickCompletedAsync(Guid.NewGuid(), 2, fix.FacilityId, fix.ClientId, now, lines: 300, units: 1000, pickDurationHours: 8m);
        await projService.ProcessPackCompletedAsync(Guid.NewGuid(), 3, fix.FacilityId, fix.ClientId, now, cartons: 120);
        await projService.ProcessOrderDispatchedAsync(Guid.NewGuid(), 4, fix.FacilityId, fix.ClientId, now, packages: 120, cycleTimeHours: 3.2m, eligibleCutoff: true, sameDayDispatched: true);
        await projService.ProcessCommercialActivityRatedAsync(Guid.NewGuid(), 5, fix.ClientId, fix.FacilityId, now, BillableActivityType.StoragePalletDay, 300m);
        await projService.ProcessCommercialActivityRatedAsync(Guid.NewGuid(), 6, fix.ClientId, fix.FacilityId, now, BillableActivityType.CarrierFreight, 800m, directCost: 600m);
        await projService.ProcessCarrierDeliveredAsync(Guid.NewGuid(), 7, "FEDEX", CarrierServiceLevel.StandardGround, fix.FacilityId, now, isOnTime: true, isFirstAttempt: true, transitDays: 2.1m);
        await projService.ProcessInventorySnapshotAsync(Guid.NewGuid(), 8, fix.FacilityId, null, now, totalLocations: 1000, occupiedLocations: 750, totalCbm: 4000m, occupiedCbm: 3200m, activeSkus: 200, totalUnits: 15000, totalValue: 500000m, deadSkus: 10, deadUnits: 500, deadValue: 20000m);

        var queryService = new AnalyticsQueryService(context, new TestCurrentUserContext { UserType = UserType.Staff }, NullLogger<AnalyticsQueryService>.Instance);
        var summary = await queryService.GetControlTowerSummaryAsync(fix.FacilityId, now);

        Assert.NotNull(summary);
        Assert.Equal(150, summary.OrdersReceivedToday);
        Assert.Equal(1000, summary.UnitsPickedToday);
        Assert.Equal(120, summary.CartonsPackedToday);
        Assert.Equal(1, summary.OrdersDispatchedToday);
        Assert.Equal(120, summary.PackagesDispatchedToday);
        Assert.Equal(125.00m, summary.UnitsPerLaborHourToday);
        Assert.Equal(3.20m, summary.AverageFulfillmentCycleTimeHours);
        Assert.Equal(100.00m, summary.SameDayDispatchAdherencePercent);
        Assert.Equal(1100.00m, summary.TotalBilledRevenueToday); // 300 storage + 800 freight
        Assert.Equal(200.00m, summary.NetFreightMarginToday); // 800 freight - 600 carrier
        Assert.Equal(100.00m, summary.OnTimeDeliveryRatePercent);
        Assert.Equal(75.00m, summary.PalletPositionOccupancyPercent); // 750 / 1000
        Assert.Equal(80.00m, summary.VolumetricCubeFillPercent); // 3200 / 4000
    }

    #endregion
}
