using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Domain.ValueObjects;
using Warehouse3PL.Infrastructure.Services.Billing;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Billing;

/// <summary>
/// Unit &amp; integration test suite for Phase 5.0 Billing Foundation, Money value object,
/// billing periods, and the immutable billable activity ledger with idempotency guarantees.
/// </summary>
public class BillingFoundationAndActivityLedgerTests
{
    private class TestEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid ForeignClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
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
            CompanyName = "Acro Logistics Client",
            AccountCode = "ACRO",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Rival Distribution",
            AccountCode = "RIVL",
            Status = ClientStatus.Active
        };

        var facility = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "FAC-ORD1",
            Name = "Chicago Gateway Hub"
        };

        var staff = new User
        {
            Id = fix.StaffUserId,
            Email = "billing.specialist@warehouse3pl.test",
            PasswordHash = "hashed",
            FirstName = "Dana",
            LastName = "Scully",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        context.Clients.AddRange(client, foreignClient);
        context.Facilities.Add(facility);
        context.Users.Add(staff);
        await context.SaveChangesAsync();

        return fix;
    }

    private static BillableActivityService CreateService(
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
        return new BillableActivityService(
            context,
            tenantProvider,
            userContext,
            NullLogger<BillableActivityService>.Instance);
    }

    // =========================================================================================================
    // 1. MONEY VALUE OBJECT TESTS
    // =========================================================================================================

    [Fact]
    public void Money_CreationAndArithmetic_MaintainsHighPrecisionAndSingleCurrency()
    {
        var m1 = new Money(125.50505m, "USD");
        var m2 = new Money(74.49495m, "usd");

        // Precision rounds away from zero at 4 decimals
        Assert.Equal(125.5051m, m1.Amount);
        Assert.Equal(74.4950m, m2.Amount);
        Assert.Equal("USD", m1.Currency);
        Assert.Equal("USD", m2.Currency);

        // Addition & Subtraction
        var sum = m1 + m2;
        Assert.Equal(200.0001m, sum.Amount);

        var diff = m1 - m2;
        Assert.Equal(51.0101m, diff.Amount);

        // Scalar multiplication & division
        var mult = m1 * 2m;
        Assert.Equal(251.0102m, mult.Amount);

        var div = m1 / 2m;
        Assert.Equal(62.7526m, div.Amount);
    }

    [Fact]
    public void Money_CrossCurrencyArithmetic_ThrowsValidationException()
    {
        var usd = new Money(100m, "USD");
        var eur = new Money(100m, "EUR");

        var ex = Assert.Throws<ValidationException>(() => _ = usd + eur);
        Assert.Contains("Currency mismatch", ex.Message);

        var ex2 = Assert.Throws<ValidationException>(() => _ = usd.CompareTo(eur));
        Assert.Contains("Currency mismatch", ex2.Message);
    }

    [Fact]
    public void Money_DivideByZero_ThrowsValidationException()
    {
        var money = new Money(100m, "USD");
        Assert.Throws<ValidationException>(() => _ = money / 0m);
    }

    // =========================================================================================================
    // 2. BILLING PERIOD DOMAIN LIFECYCLE TESTS
    // =========================================================================================================

    [Fact]
    public void BillingPeriod_LifecycleProgression_FollowsStrictStateGuards()
    {
        var clientId = Guid.NewGuid();
        var start = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc);

        var period = BillingPeriod.Create(clientId, "BP-2026-11", "November 2026", start, end);
        Assert.Equal(BillingPeriodStatus.Open, period.Status);
        Assert.Equal("BP-2026-11", period.PeriodCode);

        // Cannot close an Open period directly without locking
        var directCloseEx = Assert.Throws<InvalidDomainStateException>(() => period.Close());
        Assert.Contains("must be Locked before it can be Closed", directCloseEx.Message);

        // Lock period
        period.Lock();
        Assert.Equal(BillingPeriodStatus.Locked, period.Status);
        Assert.NotNull(period.LockedAtUtc);

        // Reopen period
        period.Reopen();
        Assert.Equal(BillingPeriodStatus.Open, period.Status);
        Assert.Null(period.LockedAtUtc);

        // Lock and Close
        period.Lock();
        period.Close();
        Assert.Equal(BillingPeriodStatus.Closed, period.Status);
        Assert.NotNull(period.ClosedAtUtc);

        // Invoice period
        period.MarkInvoiced();
        Assert.Equal(BillingPeriodStatus.Invoiced, period.Status);
        Assert.NotNull(period.InvoicedAtUtc);
    }

    [Fact]
    public void BillingPeriod_InvalidDateRange_ThrowsValidationException()
    {
        var clientId = Guid.NewGuid();
        var start = DateTime.UtcNow;
        var end = start.AddDays(-1); // Inverted date range

        Assert.Throws<ValidationException>(() =>
            BillingPeriod.Create(clientId, "BP-INV", "Invalid Range", start, end));
    }

    // =========================================================================================================
    // 3. BILLABLE ACTIVITY DOMAIN RULES & RATING TESTS
    // =========================================================================================================

    [Fact]
    public void BillableActivity_CreateAndRate_EnforcesStatusAndFinancialRules()
    {
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        var activity = BillableActivity.Create(
            clientId,
            facilityId,
            BillableActivityType.PickLine,
            "PickTask",
            sourceId,
            "TASK-1001",
            12m,
            "LINE",
            DateTime.UtcNow,
            "IDEMP-PICK-1001",
            "Pick 12 lines for order");

        Assert.Equal(BillableActivityStatus.PendingRating, activity.Status);
        Assert.Equal(12m, activity.Quantity);
        Assert.Null(activity.UnitPrice);
        Assert.Null(activity.TotalAmount);

        // Rate the activity: $0.35 per line * 12 lines = $4.20
        var rateCardId = Guid.NewGuid();
        activity.Rate(0.35m, 4.20m, "USD", rateCardId, null, 1);

        Assert.Equal(BillableActivityStatus.Rated, activity.Status);
        Assert.Equal(0.35m, activity.UnitPrice);
        Assert.Equal(4.20m, activity.TotalAmount);
        Assert.Equal("USD", activity.Currency);
        Assert.Equal(rateCardId, activity.RateCardId);

        // Link to invoice
        var invoiceId = Guid.NewGuid();
        var invoiceLineId = Guid.NewGuid();
        activity.MarkInvoiced(invoiceId, invoiceLineId);

        Assert.Equal(BillableActivityStatus.Invoiced, activity.Status);
        Assert.Equal(invoiceId, activity.InvoiceId);
        Assert.Equal(invoiceLineId, activity.InvoiceLineId);

        // Modifying invoiced activity is strictly blocked
        Assert.Throws<InvalidDomainStateException>(() => activity.Rate(0.50m, 6.00m, "USD"));
        Assert.Throws<InvalidDomainStateException>(() => activity.Cancel("Voiding"));
        Assert.Throws<InvalidDomainStateException>(() => activity.Waive("Waiving"));
    }

    [Fact]
    public void BillableActivity_Waiver_SetsZeroChargeWithAuditDescription()
    {
        var activity = BillableActivity.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            BillableActivityType.ReturnInspection,
            "ReturnReceivingSession",
            Guid.NewGuid(),
            "RRCV-001",
            1m,
            "SESSION",
            DateTime.UtcNow,
            "IDEMP-RET-WAIVE",
            "Return inspection session");

        activity.Waive("Contractual courtesy credit for initial onboarding month");

        Assert.Equal(BillableActivityStatus.Waived, activity.Status);
        Assert.Equal(0m, activity.UnitPrice);
        Assert.Equal(0m, activity.TotalAmount);
        Assert.Contains("Waived", activity.Description);
    }

    // =========================================================================================================
    // 4. BILLABLE ACTIVITY SERVICE & IDEMPOTENCY TESTS
    // =========================================================================================================

    [Fact]
    public async Task BillableActivityService_RecordActivity_AutoResolvesPeriodAndPersists()
    {
        var fix = await SeedTestEnvironmentAsync();
        var service = CreateService(fix);

        // Create open billing period covering now
        var period = await service.CreateBillingPeriodAsync(new CreateBillingPeriodRequest
        {
            ClientId = fix.ClientId,
            PeriodCode = "BP-CURRENT",
            Name = "Current Month Period",
            StartDateUtc = DateTime.UtcNow.AddDays(-10),
            EndDateUtc = DateTime.UtcNow.AddDays(20)
        });

        var sourceId = Guid.NewGuid();
        var result = await service.RecordActivityAsync(new RecordBillableActivityRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ActivityType = BillableActivityType.OrderProcessing,
            SourceType = "OutboundOrder",
            SourceId = sourceId,
            SourceReference = "ORD-777",
            Quantity = 1m,
            UnitOfMeasure = "ORDER",
            OccurredAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"ACT:ORD:{sourceId}",
            Description = "Base fulfillment charge for ORD-777"
        });

        Assert.True(result.IsSuccess);
        Assert.False(result.IsDuplicate);
        Assert.Equal(BillableActivityStatus.PendingRating, result.Status);

        // Verify database entity and period link
        using var verifyContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext(), fix.DbName);
        var inDb = await verifyContext.BillableActivities.FindAsync(result.BillableActivityId);
        Assert.NotNull(inDb);
        Assert.Equal(period.Id, inDb.BillingPeriodId);
        Assert.Equal(fix.ClientId, inDb.ClientId);
        Assert.Equal(1m, inDb.Quantity);
    }

    [Fact]
    public async Task BillableActivityService_IdempotencyDeduplication_ReturnsExistingRecordWithoutDuplication()
    {
        var fix = await SeedTestEnvironmentAsync();
        var service = CreateService(fix);
        var sourceId = Guid.NewGuid();
        var key = $"ACT:PUTAWAY:{sourceId}:PALLET";

        var req = new RecordBillableActivityRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ActivityType = BillableActivityType.Putaway,
            SourceType = "PutawayTask",
            SourceId = sourceId,
            SourceReference = "PT-88",
            Quantity = 2m,
            UnitOfMeasure = "PALLET",
            OccurredAtUtc = DateTime.UtcNow,
            IdempotencyKey = key
        };

        // First call records activity
        var firstResult = await service.RecordActivityAsync(req);
        Assert.True(firstResult.IsSuccess);
        Assert.False(firstResult.IsDuplicate);

        // Second call with identical key is safely de-duplicated
        var secondResult = await service.RecordActivityAsync(req);
        Assert.True(secondResult.IsSuccess);
        Assert.True(secondResult.IsDuplicate);
        Assert.Equal(firstResult.BillableActivityId, secondResult.BillableActivityId);

        // Verify count in database is exactly 1
        using var verifyContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext(), fix.DbName);
        var count = await verifyContext.BillableActivities.CountAsync(a => a.ClientId == fix.ClientId && a.IdempotencyKey == key);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task BillableActivityService_BatchRecording_DeDuplicatesWithinBatchAndAcrossDb()
    {
        var fix = await SeedTestEnvironmentAsync();
        var service = CreateService(fix);

        var task1Id = Guid.NewGuid();
        var task2Id = Guid.NewGuid();

        var batch = new List<RecordBillableActivityRequest>
        {
            new()
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                ActivityType = BillableActivityType.PickUnit,
                SourceType = "PickTask",
                SourceId = task1Id,
                Quantity = 5m,
                UnitOfMeasure = "UNIT",
                OccurredAtUtc = DateTime.UtcNow,
                IdempotencyKey = $"ACT:PICK:{task1Id}"
            },
            new()
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                ActivityType = BillableActivityType.PickUnit,
                SourceType = "PickTask",
                SourceId = task2Id,
                Quantity = 10m,
                UnitOfMeasure = "UNIT",
                OccurredAtUtc = DateTime.UtcNow,
                IdempotencyKey = $"ACT:PICK:{task2Id}"
            },
            // Duplicate of task1 within same batch
            new()
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                ActivityType = BillableActivityType.PickUnit,
                SourceType = "PickTask",
                SourceId = task1Id,
                Quantity = 5m,
                UnitOfMeasure = "UNIT",
                OccurredAtUtc = DateTime.UtcNow,
                IdempotencyKey = $"ACT:PICK:{task1Id}"
            }
        };

        var results = await service.RecordActivitiesBatchAsync(batch);
        Assert.Equal(3, results.Count);
        Assert.False(results[0].IsDuplicate);
        Assert.False(results[1].IsDuplicate);
        Assert.True(results[2].IsDuplicate);
        Assert.Equal(results[0].BillableActivityId, results[2].BillableActivityId);
    }

    [Fact]
    public async Task BillableActivityService_CrossTenantIsolation_ThrowsUnauthorizedException()
    {
        var fix = await SeedTestEnvironmentAsync();
        // Service running under ForeignClientId
        var crossTenantService = CreateService(fix, tenantOverride: fix.ForeignClientId, bypassTenant: false);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            crossTenantService.RecordActivityAsync(new RecordBillableActivityRequest
            {
                ClientId = fix.ClientId, // Target client mismatched with context tenant
                FacilityId = fix.FacilityId,
                ActivityType = BillableActivityType.InboundReceiving,
                SourceType = "GoodsReceipt",
                SourceId = Guid.NewGuid(),
                Quantity = 1m,
                UnitOfMeasure = "PALLET",
                IdempotencyKey = "CROSS-TENANT-KEY"
            }));

        Assert.Contains("cross-tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

