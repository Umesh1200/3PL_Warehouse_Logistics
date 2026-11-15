using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Billing;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Billing;

/// <summary>
/// Comprehensive verification test suite for Phase 5.1 Client Rate Cards &amp; Agreement Schedules.
/// Validates effective-date boundaries, non-overlapping schedules, version preservation,
/// facility override precedence, graduated vs all-units tier evaluation, minimum spend commitments,
/// missing rate resilience, currency mismatch guards, concurrency/idempotency, and cross-tenant isolation.
/// </summary>
public class RateCardAndRatingEngineTests
{
    private class TestEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid ForeignClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid SecondaryFacilityId { get; set; } = Guid.NewGuid();
        public Guid StaffUserId { get; set; } = Guid.NewGuid();
    }

    private static async Task<TestEnvFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestEnvFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var client = new Client
        {
            Id = fix.ClientId,
            CompanyName = "Global Commerce Logistics Client",
            AccountCode = "GCL01",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Apex External Logistics",
            AccountCode = "APEX",
            Status = ClientStatus.Active
        };

        var facility1 = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "FAC-CHI1",
            Name = "Chicago Gateway Hub"
        };

        var facility2 = new Facility
        {
            Id = fix.SecondaryFacilityId,
            FacilityCode = "FAC-LAX1",
            Name = "Los Angeles West Coast Depot"
        };

        var staff = new User
        {
            Id = fix.StaffUserId,
            Email = "rating.analyst@warehouse3pl.test",
            PasswordHash = "hashed",
            FirstName = "Marcus",
            LastName = "Vance",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        context.Clients.AddRange(client, foreignClient);
        context.Facilities.AddRange(facility1, facility2);
        context.Users.Add(staff);
        await context.SaveChangesAsync();

        return fix;
    }

    private static (RateCardService RateCardService, RatingEngineService RatingService, IApplicationDbContext Context) CreateServices(
        TestEnvFixture fix,
        Guid? tenantOverride = null,
        bool bypassTenant = false)
    {
        var tenantProvider = new TestTenantProvider
        {
            TenantId = tenantOverride ?? fix.ClientId,
            BypassTenantFilter = bypassTenant
        };

        var userContext = new TestCurrentUserContext
        {
            UserId = fix.StaffUserId,
            UserType = UserType.Staff,
            FacilityId = fix.FacilityId
        };

        var context = TestDbContextFactory.Create(tenantProvider, userContext, fix.DbName);

        var rateCardService = new RateCardService(
            context,
            tenantProvider,
            NullLogger<RateCardService>.Instance);

        var ratingEngineService = new RatingEngineService(
            context,
            rateCardService,
            tenantProvider,
            NullLogger<RatingEngineService>.Instance);

        return (rateCardService, ratingEngineService, context);
    }

    [Fact]
    public async Task Test_01_EffectiveDate_Boundary_Enforcement()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var effectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var effectiveTo = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-2026-H1", "2026 H1 Agreement", "USD");
        var schedule = await rateCardService.AddScheduleAsync(rateCard.Id, null, effectiveFrom, effectiveTo);
        await rateCardService.AddLineAsync(schedule.Id, BillableActivityType.PickUnit, "UNIT", RateModelType.PerUnit,
            PricingTierCalculationModel.FlatRate, 0.45m);

        await rateCardService.ActivateScheduleAsync(schedule.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Case A: Occurred exactly on boundary start -> Successful rating
        var actStart = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "TASK-START", 10, "UNIT", effectiveFrom, "IDEMP-BOUND-01", "Boundary Start");
        context.BillableActivities.Add(actStart);
        await context.SaveChangesAsync();

        var ratedStart = await ratingEngine.RateActivityAsync(actStart.Id);
        Assert.Equal(BillableActivityStatus.Rated, ratedStart.Status);
        Assert.Equal(0.45m, ratedStart.UnitPrice);
        Assert.Equal(4.50m, ratedStart.TotalAmount);

        // Case B: Occurred exactly on boundary end -> Successful rating
        var actEnd = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "TASK-END", 20, "UNIT", effectiveTo, "IDEMP-BOUND-02", "Boundary End");
        context.BillableActivities.Add(actEnd);
        await context.SaveChangesAsync();

        var ratedEnd = await ratingEngine.RateActivityAsync(actEnd.Id);
        Assert.Equal(BillableActivityStatus.Rated, ratedEnd.Status);
        Assert.Equal(9.00m, ratedEnd.TotalAmount);

        // Case C: Occurred 1 second before effective window -> Rejection, remains PendingRating
        var actBefore = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "TASK-BEFORE", 10, "UNIT", effectiveFrom.AddSeconds(-1), "IDEMP-BOUND-03", "Before Window");
        context.BillableActivities.Add(actBefore);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDomainStateException>(() => ratingEngine.RateActivityAsync(actBefore.Id));
        var recheckBefore = await context.BillableActivities.FindAsync(actBefore.Id);
        Assert.Equal(BillableActivityStatus.PendingRating, recheckBefore!.Status);

        // Case D: Occurred 1 second after effective window -> Rejection, remains PendingRating
        var actAfter = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "TASK-AFTER", 10, "UNIT", effectiveTo.AddSeconds(1), "IDEMP-BOUND-04", "After Window");
        context.BillableActivities.Add(actAfter);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDomainStateException>(() => ratingEngine.RateActivityAsync(actAfter.Id));
        var recheckAfter = await context.BillableActivities.FindAsync(actAfter.Id);
        Assert.Equal(BillableActivityStatus.PendingRating, recheckAfter!.Status);
    }

    [Fact]
    public async Task Test_02_OverlappingSchedule_Rejection()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, _, _) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-OVERLAP", "Overlap Test Card", "USD");
        var sched1 = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        await rateCardService.ActivateScheduleAsync(sched1.Id);

        // Attempting to add an overlapping schedule for the same client and facility scope (null = client-wide) must fail
        await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            rateCardService.AddScheduleAsync(rateCard.Id, null,
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 5, 31, 23, 59, 59, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Test_03_Historical_Version_Preservation()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-VER", "Version Agreement", "USD");

        // Schedule v1: 2026-01-01 to 2026-06-30 ($0.40/unit)
        var sched1 = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(sched1.Id, BillableActivityType.PickUnit, "UNIT", RateModelType.PerUnit,
            PricingTierCalculationModel.FlatRate, 0.40m);
        await rateCardService.ActivateScheduleAsync(sched1.Id);

        // Schedule v2: 2026-07-01 to 2026-12-31 ($0.48/unit)
        var sched2 = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(sched2.Id, BillableActivityType.PickUnit, "UNIT", RateModelType.PerUnit,
            PricingTierCalculationModel.FlatRate, 0.48m);
        await rateCardService.ActivateScheduleAsync(sched2.Id);

        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Historical activity in May (v1)
        var actMay = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "TASK-MAY", 100, "UNIT",
            new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc), "IDEMP-VER-01", "May Pick");
        context.BillableActivities.Add(actMay);

        // Activity in August (v2)
        var actAug = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "TASK-AUG", 100, "UNIT",
            new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc), "IDEMP-VER-02", "August Pick");
        context.BillableActivities.Add(actAug);
        await context.SaveChangesAsync();

        var ratedMay = await ratingEngine.RateActivityAsync(actMay.Id);
        var ratedAug = await ratingEngine.RateActivityAsync(actAug.Id);

        Assert.Equal(1, ratedMay.RateCardVersion);
        Assert.Equal(0.40m, ratedMay.UnitPrice);
        Assert.Equal(40.00m, ratedMay.TotalAmount);
        Assert.Equal(sched1.Id, ratedMay.RateCardScheduleId);

        Assert.Equal(2, ratedAug.RateCardVersion);
        Assert.Equal(0.48m, ratedAug.UnitPrice);
        Assert.Equal(48.00m, ratedAug.TotalAmount);
        Assert.Equal(sched2.Id, ratedAug.RateCardScheduleId);
    }

    [Fact]
    public async Task Test_04_FacilitySpecific_Override_Precedence()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-OVERRIDE", "Override Agreement", "USD");

        // 1. Client-Wide Default Schedule: $0.50/unit
        var clientWideSched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(clientWideSched.Id, BillableActivityType.OrderProcessing, "ORDER",
            RateModelType.PerOrder, PricingTierCalculationModel.FlatRate, 0.50m);
        await rateCardService.ActivateScheduleAsync(clientWideSched.Id);

        // 2. Chicago Facility Override Schedule: $0.38/unit (volume discount for high-efficiency facility)
        var chicagoSched = await rateCardService.AddScheduleAsync(rateCard.Id, fix.FacilityId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(chicagoSched.Id, BillableActivityType.OrderProcessing, "ORDER",
            RateModelType.PerOrder, PricingTierCalculationModel.FlatRate, 0.38m);
        await rateCardService.ActivateScheduleAsync(chicagoSched.Id);

        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        var actChicago = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.OrderProcessing,
            "OutboundOrder", Guid.NewGuid(), "ORD-CHI-01", 1, "ORDER",
            new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc), "IDEMP-OVR-01", "Chicago Order");
        context.BillableActivities.Add(actChicago);
        await context.SaveChangesAsync();

        var ratedChicago = await ratingEngine.RateActivityAsync(actChicago.Id);

        // Facility override MUST win over client-wide default
        Assert.Equal(0.38m, ratedChicago.UnitPrice);
        Assert.Equal(0.38m, ratedChicago.TotalAmount);
        Assert.Equal(chicagoSched.Id, ratedChicago.RateCardScheduleId);
    }

    [Fact]
    public async Task Test_05_ClientWide_Fallback_When_No_FacilityOverride()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-FALLBACK", "Fallback Agreement", "USD");

        // Client-Wide Schedule: $0.50/unit
        var clientWideSched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(clientWideSched.Id, BillableActivityType.OrderProcessing, "ORDER",
            RateModelType.PerOrder, PricingTierCalculationModel.FlatRate, 0.50m);
        await rateCardService.ActivateScheduleAsync(clientWideSched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Activity occurs at SecondaryFacilityId (Los Angeles), which has no facility override
        var actLax = BillableActivity.Create(fix.ClientId, fix.SecondaryFacilityId, BillableActivityType.OrderProcessing,
            "OutboundOrder", Guid.NewGuid(), "ORD-LAX-01", 1, "ORDER",
            new DateTime(2026, 4, 12, 10, 0, 0, DateTimeKind.Utc), "IDEMP-FALL-01", "LAX Order");
        context.BillableActivities.Add(actLax);
        await context.SaveChangesAsync();

        var ratedLax = await ratingEngine.RateActivityAsync(actLax.Id);

        // Must fall back to client-wide schedule
        Assert.Equal(0.50m, ratedLax.UnitPrice);
        Assert.Equal(0.50m, ratedLax.TotalAmount);
        Assert.Equal(clientWideSched.Id, ratedLax.RateCardScheduleId);
    }

    [Fact]
    public async Task Test_06_Graduated_Volume_Tier_Calculation()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-GRAD", "Graduated Tier Card", "USD");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        // Graduated tiers:
        // Tier 1: 0 - 500 @ $0.40
        // Tier 2: 501 - 1000 @ $0.35
        // Tier 3: > 1000 @ $0.30
        var tiers = new List<(int TierIndex, decimal MinQuantity, decimal? MaxQuantity, decimal UnitPrice)>
        {
            (1, 0m, 500m, 0.40m),
            (2, 501m, 1000m, 0.35m),
            (3, 1001m, null, 0.30m)
        };

        await rateCardService.AddLineAsync(sched.Id, BillableActivityType.PickUnit, "UNIT",
            RateModelType.PerUnit, PricingTierCalculationModel.Graduated, baseRate: 0.40m, tiers: tiers);
        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Scenario 1: Qty 750
        // Bracket 1: 500 * $0.40 = $200.00
        // Bracket 2: 250 * $0.35 = $87.50
        // Total = $287.50, Blended Unit Price = $287.50 / 750 = $0.3833
        var act750 = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickWave", Guid.NewGuid(), "WAVE-750", 750, "UNIT",
            new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), "IDEMP-GRAD-750", "750 units");
        context.BillableActivities.Add(act750);

        // Scenario 2: Qty 1500
        // Bracket 1: 500 * $0.40 = $200.00
        // Bracket 2: 500 * $0.35 = $175.00
        // Bracket 3: 500 * $0.30 = $150.00
        // Total = $525.00, Blended Unit Price = $525.00 / 1500 = $0.3500
        var act1500 = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickWave", Guid.NewGuid(), "WAVE-1500", 1500, "UNIT",
            new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc), "IDEMP-GRAD-1500", "1500 units");
        context.BillableActivities.Add(act1500);

        await context.SaveChangesAsync();

        var rated750 = await ratingEngine.RateActivityAsync(act750.Id);
        var rated1500 = await ratingEngine.RateActivityAsync(act1500.Id);

        Assert.Equal(287.50m, rated750.TotalAmount);
        Assert.Equal(0.3833m, rated750.UnitPrice);

        Assert.Equal(525.00m, rated1500.TotalAmount);
        Assert.Equal(0.3500m, rated1500.UnitPrice);
    }

    [Fact]
    public async Task Test_07_AllUnits_Volume_Tier_Calculation()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-ALLUNITS", "All Units Tier Card", "USD");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        // All-Units tiers:
        // Tier 1: 0 - 500 @ $0.40
        // Tier 2: 501 - 1000 @ $0.35
        // Tier 3: > 1000 @ $0.30
        var tiers = new List<(int TierIndex, decimal MinQuantity, decimal? MaxQuantity, decimal UnitPrice)>
        {
            (1, 0m, 500m, 0.40m),
            (2, 501m, 1000m, 0.35m),
            (3, 1001m, null, 0.30m)
        };

        var line = await rateCardService.AddLineAsync(sched.Id, BillableActivityType.PickUnit, "UNIT",
            RateModelType.PerUnit, PricingTierCalculationModel.AllUnits, baseRate: 0.40m, tiers: tiers);
        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Scenario 1: Qty 750 -> Achieves Tier 2, all 750 units billed at $0.35 = $262.50
        var act750 = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickWave", Guid.NewGuid(), "WAVE-ALL-750", 750, "UNIT",
            new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), "IDEMP-ALL-750", "750 all-units");
        context.BillableActivities.Add(act750);

        // Scenario 2: Qty 1500 -> Achieves Tier 3, all 1500 units billed at $0.30 = $450.00
        var act1500 = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickWave", Guid.NewGuid(), "WAVE-ALL-1500", 1500, "UNIT",
            new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc), "IDEMP-ALL-1500", "1500 all-units");
        context.BillableActivities.Add(act1500);

        await context.SaveChangesAsync();

        var rated750 = await ratingEngine.RateActivityAsync(act750.Id);
        var rated1500 = await ratingEngine.RateActivityAsync(act1500.Id);

        Assert.Equal(262.50m, rated750.TotalAmount);
        Assert.Equal(0.3500m, rated750.UnitPrice);
        Assert.NotNull(rated750.RateCardTierId);

        Assert.Equal(450.00m, rated1500.TotalAmount);
        Assert.Equal(0.3000m, rated1500.UnitPrice);
        Assert.NotNull(rated1500.RateCardTierId);
    }

    [Fact]
    public async Task Test_08_Minimum_Charge_Commitment_Behavior()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-MIN", "Minimum Spend Card", "USD");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            minimumMonthlySpend: 2500.00m,
            notes: "Client has $2,500 monthly minimum commitment.");

        // Activity Line has $10.00 MinimumCharge floor, $0.50 base rate
        await rateCardService.AddLineAsync(sched.Id, BillableActivityType.InboundReceiving, "PALLET",
            RateModelType.PerPallet, PricingTierCalculationModel.FlatRate, baseRate: 0.50m, minimumCharge: 10.00m);

        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Case A: 5 pallets * $0.50 = $2.50 -> Below $10 floor, elevated to $10.00
        var actSmall = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.InboundReceiving,
            "Receipt", Guid.NewGuid(), "RCPT-SMALL", 5, "PALLET",
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), "IDEMP-MIN-01", "Small Receipt");
        context.BillableActivities.Add(actSmall);

        // Case B: 30 pallets * $0.50 = $15.00 -> Above $10 floor, stays $15.00
        var actLarge = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.InboundReceiving,
            "Receipt", Guid.NewGuid(), "RCPT-LARGE", 30, "PALLET",
            new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc), "IDEMP-MIN-02", "Large Receipt");
        context.BillableActivities.Add(actLarge);

        await context.SaveChangesAsync();

        var ratedSmall = await ratingEngine.RateActivityAsync(actSmall.Id);
        var ratedLarge = await ratingEngine.RateActivityAsync(actLarge.Id);

        Assert.Equal(10.00m, ratedSmall.TotalAmount);
        Assert.Equal(2.00m, ratedSmall.UnitPrice); // Effective blended price: $10.00 / 5 units

        Assert.Equal(15.00m, ratedLarge.TotalAmount);
        Assert.Equal(0.50m, ratedLarge.UnitPrice);

        // Verify MinimumMonthlySpend commitment is stored on schedule
        var storedSchedule = await context.RateCardSchedules.FindAsync(sched.Id);
        Assert.Equal(2500.00m, storedSchedule!.MinimumMonthlySpend);
    }

    [Fact]
    public async Task Test_09_MissingRate_Handling_Preserves_PendingRating()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-PARTIAL", "Partial Agreement", "USD");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        // Only OrderProcessing is configured; ReturnProcessing is omitted
        await rateCardService.AddLineAsync(sched.Id, BillableActivityType.OrderProcessing, "ORDER",
            RateModelType.PerOrder, PricingTierCalculationModel.FlatRate, 1.25m);
        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        var missingActivity = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.ReturnProcessing,
            "ReturnSession", Guid.NewGuid(), "RET-001", 1, "ORDER",
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), "IDEMP-MISS-01", "Unconfigured Return");
        context.BillableActivities.Add(missingActivity);
        await context.SaveChangesAsync();

        // Single rating must throw and leave activity PendingRating
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => ratingEngine.RateActivityAsync(missingActivity.Id));

        var recheck = await context.BillableActivities.FindAsync(missingActivity.Id);
        Assert.Equal(BillableActivityStatus.PendingRating, recheck!.Status);
        Assert.Null(recheck.UnitPrice);
        Assert.Null(recheck.TotalAmount);

        // Batch rating must capture missing rate in diagnostics without crashing
        var batchResult = await ratingEngine.RateActivitiesBatchAsync(fix.ClientId);
        Assert.Equal(1, batchResult.TotalActivitiesFound);
        Assert.Equal(0, batchResult.SuccessfullyRatedCount);
        Assert.Equal(1, batchResult.MissingRateCount);
        Assert.Single(batchResult.MissingRateDetails);
        Assert.Equal(missingActivity.Id, batchResult.MissingRateDetails[0].ActivityId);
    }

    [Fact]
    public async Task Test_10_Currency_Mismatch_Rejection()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        // RateCard is in EUR
        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-EUR", "Euro Agreement", "EUR");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(sched.Id, BillableActivityType.PickUnit, "UNIT",
            RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 0.40m);
        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        // Activity was recorded in USD
        var actUsd = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "TASK-USD", 10, "UNIT",
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), "IDEMP-CURR-01", "USD Activity");
        actUsd.Currency = "USD";
        context.BillableActivities.Add(actUsd);
        await context.SaveChangesAsync();

        // Rating engine must refuse currency mismatch and not silently cross-apply rates
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => ratingEngine.RateActivityAsync(actUsd.Id));
    }

    [Fact]
    public async Task Test_11_Concurrent_Rating_Attempts_Protection()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-CONC", "Concurrency Agreement", "USD");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(sched.Id, BillableActivityType.PickLine, "LINE",
            RateModelType.PerLine, PricingTierCalculationModel.FlatRate, 0.15m);
        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        var act = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickLine,
            "PickTask", Guid.NewGuid(), "TASK-RACE", 10, "LINE",
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), "IDEMP-RACE-01", "Concurrent Pick");
        context.BillableActivities.Add(act);
        await context.SaveChangesAsync();

        // Worker 1 rates the activity
        var rated1 = await ratingEngine.RateActivityAsync(act.Id);
        Assert.Equal(BillableActivityStatus.Rated, rated1.Status);
        var originalRatedAt = rated1.RatedAtUtc;

        // Worker 2 attempts to rate the same activity concurrently
        var rated2 = await ratingEngine.RateActivityAsync(act.Id);
        Assert.Equal(BillableActivityStatus.Rated, rated2.Status);
        Assert.Equal(originalRatedAt, rated2.RatedAtUtc);
        Assert.Equal(1.50m, rated2.TotalAmount);
    }

    [Fact]
    public async Task Test_12_Duplicate_Rating_Request_Idempotency()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-IDEMP", "Idempotency Agreement", "USD");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await rateCardService.AddLineAsync(sched.Id, BillableActivityType.PickUnit, "UNIT",
            RateModelType.PerUnit, PricingTierCalculationModel.FlatRate, 0.25m);
        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        var act = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "TASK-IDEMP", 100, "UNIT",
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), "IDEMP-DUP-01", "Duplicate Test");
        context.BillableActivities.Add(act);
        await context.SaveChangesAsync();

        var firstCall = await ratingEngine.RateActivityAsync(act.Id);
        var secondCall = await ratingEngine.RateActivityAsync(act.Id);

        Assert.Equal(firstCall.Id, secondCall.Id);
        Assert.Equal(firstCall.TotalAmount, secondCall.TotalAmount);
        Assert.Equal(firstCall.UnitPrice, secondCall.UnitPrice);
        Assert.Equal(firstCall.RatedAtUtc, secondCall.RatedAtUtc);
    }

    [Fact]
    public async Task Test_13_HistoricalPrice_Preservation_After_RateCard_Changes()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, ratingEngine, context) = CreateServices(fix, bypassTenant: true);

        var rateCard = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-HIST", "Historical Agreement", "USD");
        var sched = await rateCardService.AddScheduleAsync(rateCard.Id, null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        var line = await rateCardService.AddLineAsync(sched.Id, BillableActivityType.OrderProcessing, "ORDER",
            RateModelType.PerOrder, PricingTierCalculationModel.FlatRate, 2.00m);
        await rateCardService.ActivateScheduleAsync(sched.Id);
        await rateCardService.ActivateRateCardAsync(rateCard.Id);

        var act = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.OrderProcessing,
            "OutboundOrder", Guid.NewGuid(), "ORD-HIST-01", 1, "ORDER",
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), "IDEMP-HIST-01", "Historical Order");
        context.BillableActivities.Add(act);
        await context.SaveChangesAsync();

        // Rate the activity under the $2.00 rate
        var rated = await ratingEngine.RateActivityAsync(act.Id);
        Assert.Equal(2.00m, rated.TotalAmount);

        // Later, client contract renegotiation raises rate to $3.50 on the line
        line.BaseRate = 3.50m;
        await context.SaveChangesAsync();

        // The already rated historical activity snapshot MUST remain untouched at $2.00
        var recheck = await context.BillableActivities.FindAsync(act.Id);
        Assert.Equal(2.00m, recheck!.TotalAmount);
        Assert.Equal(2.00m, recheck.UnitPrice);
        Assert.Equal(sched.Id, recheck.RateCardScheduleId);
    }

    [Fact]
    public async Task Test_14_CrossTenant_RateCard_Access_Rejection()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (rateCardService, _, _) = CreateServices(fix, bypassTenant: true);

        // Create RateCard for Client A
        var rateCardA = await rateCardService.CreateRateCardAsync(fix.ClientId, "RC-TENANT-A", "Tenant A Card", "USD");

        // Now attempt to access or add schedule to Client A's RateCard using Client B's tenant context
        var (rateCardServiceTenantB, _, _) = CreateServices(fix, tenantOverride: fix.ForeignClientId, bypassTenant: false);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            rateCardServiceTenantB.AddScheduleAsync(rateCardA.Id, null,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc)));
    }
}

