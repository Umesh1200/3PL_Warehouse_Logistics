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
/// Comprehensive verification test suite for Phase 5.5:
/// Automated Billing Runs, Invoicing Ledger, Customer Statements &amp; Dispute Management.
/// Validates invoice aggregation, period locking, multi-currency isolation, sequential numbering,
/// Accounts Receivable ledger, 5-bucket aging calculation, credit notes, and full dispute lifecycles.
/// </summary>
public class InvoiceAndAccountsReceivableTests
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
            CompanyName = "Global Retail Logistics Corp",
            AccountCode = "GRL01",
            Status = ClientStatus.Active,
            Currency = "USD"
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Apex Canada Distribution",
            AccountCode = "ACD01",
            Status = ClientStatus.Active,
            Currency = "CAD"
        };

        var facility = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "DAL01",
            Name = "Dallas Distribution Center",
            IsActive = true
        };

        var secondaryFacility = new Facility
        {
            Id = fix.SecondaryFacilityId,
            FacilityCode = "CHI01",
            Name = "Chicago Logistics Hub",
            IsActive = true
        };

        await context.Clients.AddRangeAsync(client, foreignClient);
        await context.Facilities.AddRangeAsync(facility, secondaryFacility);
        await context.SaveChangesAsync();

        return fix;
    }

    [Fact]
    public async Task Test_01_BillingRun_Groups_RatedActivities_Into_Invoices_And_Lines()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var period = BillingPeriod.Create(
            clientId: fix.ClientId,
            periodCode: "2026-OCT-M",
            name: "October 2026 Monthly Billing",
            startDateUtc: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            endDateUtc: new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc),
            frequency: BillingFrequency.Monthly);

        await context.BillingPeriods.AddAsync(period);

        // Add 3 rated activities: Storage, Handling, Freight
        var actStorage = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.StoragePalletDay,
            "DailyInventorySnapshot", Guid.NewGuid(), "SNAP-01", 100m, "PALLET_DAY",
            new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), "KEY-STG-1", "Storage 100 pallet days", period.Id);
        actStorage.Rate(1.50m, 150.00m, "USD");

        var actHandling = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "PICK-01", 500m, "UNIT",
            new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc), "KEY-HDL-1", "Pick 500 units", period.Id);
        actHandling.Rate(0.25m, 125.00m, "USD");

        var actFreight = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.CarrierFreight,
            "Shipment", Guid.NewGuid(), "SHP-01", 1m, "SHIPMENT",
            new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc), "KEY-FRT-1", "Outbound carrier freight", period.Id);
        actFreight.Rate(75.00m, 75.00m, "USD");

        await context.BillableActivities.AddRangeAsync(actStorage, actHandling, actFreight);
        await context.SaveChangesAsync();

        var billingService = new BillingRunService(context, NullLogger<BillingRunService>.Instance);
        var result = await billingService.ExecuteBillingRunAsync(new BillingRunRequest(
            ClientId: fix.ClientId,
            BillingPeriodId: period.Id,
            InitiatedByUserId: fix.StaffUserId,
            TaxRate: 0.10m));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.InvoicesCreatedCount);
        Assert.Equal(3, result.TotalActivitiesInvoicedCount);

        // Subtotal = 150 + 125 + 75 = 350. Tax (10%) = 35. Total = 385.
        Assert.Equal(385.00m, result.TotalInvoicedAmount);

        var invoice = await context.Invoices.Include(i => i.Lines).FirstAsync();
        Assert.Equal("USD", invoice.Currency);
        Assert.Equal(350.00m, invoice.SubtotalAmount);
        Assert.Equal(35.00m, invoice.TaxAmount);
        Assert.Equal(385.00m, invoice.TotalAmount);
        Assert.Equal(385.00m, invoice.OutstandingBalance);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(3, invoice.Lines.Count);

        Assert.Contains(invoice.Lines, l => l.ChargeCategory == InvoiceChargeCategory.Storage && l.TotalAmount == 150.00m);
        Assert.Contains(invoice.Lines, l => l.ChargeCategory == InvoiceChargeCategory.Handling && l.TotalAmount == 125.00m);
        Assert.Contains(invoice.Lines, l => l.ChargeCategory == InvoiceChargeCategory.Freight && l.TotalAmount == 75.00m);
    }

    [Fact]
    public async Task Test_02_BillingRun_Locks_Period_And_Marks_Activities_Invoiced()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var period = BillingPeriod.Create(
            fix.ClientId, "2026-NOV-M", "November 2026 Billing",
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc));
        await context.BillingPeriods.AddAsync(period);

        var act = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.InboundReceiving,
            "ReceivingSession", Guid.NewGuid(), "REC-01", 10m, "PALLET",
            new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), "KEY-SEAL-1", "Inbound Pallets", period.Id);
        act.Rate(12.00m, 120.00m, "USD");
        await context.BillableActivities.AddAsync(act);
        await context.SaveChangesAsync();

        var billingService = new BillingRunService(context, NullLogger<BillingRunService>.Instance);
        await billingService.ExecuteBillingRunAsync(new BillingRunRequest(fix.ClientId, period.Id, fix.StaffUserId));

        // Period must be Invoiced
        var refreshedPeriod = await context.BillingPeriods.FindAsync(period.Id);
        Assert.NotNull(refreshedPeriod);
        Assert.Equal(BillingPeriodStatus.Invoiced, refreshedPeriod.Status);
        Assert.NotNull(refreshedPeriod.LockedAtUtc);
        Assert.NotNull(refreshedPeriod.ClosedAtUtc);
        Assert.NotNull(refreshedPeriod.InvoicedAtUtc);

        // Activity must be Invoiced and linked to invoice
        var refreshedAct = await context.BillableActivities.FindAsync(act.Id);
        Assert.NotNull(refreshedAct);
        Assert.Equal(BillableActivityStatus.Invoiced, refreshedAct.Status);
        Assert.NotNull(refreshedAct.InvoiceId);
        Assert.NotNull(refreshedAct.InvoiceLineId);

        // Cannot re-run on sealed period
        await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            billingService.ExecuteBillingRunAsync(new BillingRunRequest(fix.ClientId, period.Id, fix.StaffUserId)));
    }

    [Fact]
    public async Task Test_03_BillingRun_Throws_When_Unrated_Activities_Exist()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var period = BillingPeriod.Create(
            fix.ClientId, "2026-DEC-M", "December 2026 Billing",
            new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        await context.BillingPeriods.AddAsync(period);

        // Activity left in PendingRating
        var unratedAct = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.InboundReceiving,
            "ReceivingSession", Guid.NewGuid(), "REC-UNRATED", 5m, "PALLET",
            new DateTime(2026, 12, 10, 0, 0, 0, DateTimeKind.Utc), "KEY-UNRATED-1", "Unrated activity", period.Id);
        await context.BillableActivities.AddAsync(unratedAct);
        await context.SaveChangesAsync();

        var billingService = new BillingRunService(context, NullLogger<BillingRunService>.Instance);
        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            billingService.ExecuteBillingRunAsync(new BillingRunRequest(fix.ClientId, period.Id, fix.StaffUserId)));

        Assert.Contains("unrated billable activities", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_04_BillingRun_Emits_Separate_Invoices_Per_Currency()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var period = BillingPeriod.Create(
            fix.ClientId, "2026-SEP-M", "September 2026 Billing",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc));
        await context.BillingPeriods.AddAsync(period);

        // Activity in USD
        var actUsd = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "PK-USD", 100m, "UNIT",
            new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), "KEY-USD", "Pick USD", period.Id);
        actUsd.Rate(0.50m, 50.00m, "USD");

        // Activity in CAD for same client
        var actCad = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "PK-CAD", 100m, "UNIT",
            new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc), "KEY-CAD", "Pick CAD", period.Id);
        actCad.Rate(0.70m, 70.00m, "CAD");

        await context.BillableActivities.AddRangeAsync(actUsd, actCad);
        await context.SaveChangesAsync();

        var billingService = new BillingRunService(context, NullLogger<BillingRunService>.Instance);
        var result = await billingService.ExecuteBillingRunAsync(new BillingRunRequest(
            fix.ClientId, period.Id, fix.StaffUserId, ConsolidateByFacility: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.InvoicesCreatedCount);

        var invoices = await context.Invoices.ToListAsync();
        Assert.Equal(2, invoices.Count);

        var usdInvoice = invoices.First(i => i.Currency == "USD");
        var cadInvoice = invoices.First(i => i.Currency == "CAD");

        Assert.Equal(50.00m, usdInvoice.TotalAmount);
        Assert.Equal(70.00m, cadInvoice.TotalAmount);
        Assert.NotEqual(usdInvoice.InvoiceNumber, cadInvoice.InvoiceNumber);
    }

    [Fact]
    public async Task Test_05_InvoiceSequence_Generates_Sequential_CollisionFree_Numbers()
    {
        var seq = InvoiceSequence.Create(Guid.NewGuid(), 2026, "INV");

        var num1 = seq.NextNumber("GRL01");
        var num2 = seq.NextNumber("GRL01");
        var num3 = seq.NextNumber("GRL01");

        Assert.Equal("INV-2026-GRL01-000001", num1);
        Assert.Equal("INV-2026-GRL01-000002", num2);
        Assert.Equal("INV-2026-GRL01-000003", num3);
    }

    [Fact]
    public async Task Test_06_BillingRun_Consolidated_Vs_FacilitySpecific()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var period = BillingPeriod.Create(
            fix.ClientId, "2026-AUG-M", "August 2026 Billing",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc));
        await context.BillingPeriods.AddAsync(period);

        var actFacility1 = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.InboundReceiving,
            "REC", Guid.NewGuid(), "R1", 1m, "PALLET",
            new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc), "K-FAC1", "Dallas Ops", period.Id);
        actFacility1.Rate(10m, 10m, "USD");

        var actFacility2 = BillableActivity.Create(
            fix.ClientId, fix.SecondaryFacilityId, BillableActivityType.InboundReceiving,
            "REC", Guid.NewGuid(), "R2", 1m, "PALLET",
            new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc), "K-FAC2", "Chicago Ops", period.Id);
        actFacility2.Rate(20m, 20m, "USD");

        await context.BillableActivities.AddRangeAsync(actFacility1, actFacility2);
        await context.SaveChangesAsync();

        var billingService = new BillingRunService(context, NullLogger<BillingRunService>.Instance);
        var result = await billingService.ExecuteBillingRunAsync(new BillingRunRequest(
            fix.ClientId, period.Id, fix.StaffUserId, ConsolidateByFacility: false));

        // When not consolidated, creates 2 separate invoices for the 2 facilities
        Assert.Equal(2, result.InvoicesCreatedCount);
        var facilityInvoices = await context.Invoices.ToListAsync();
        Assert.Contains(facilityInvoices, i => i.FacilityId == fix.FacilityId);
        Assert.Contains(facilityInvoices, i => i.FacilityId == fix.SecondaryFacilityId);
    }

    [Fact]
    public async Task Test_07_BillingRun_DryRun_Does_Not_Mutate_Database_State()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var period = BillingPeriod.Create(
            fix.ClientId, "2026-JUL-M", "July 2026 Billing",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc));
        await context.BillingPeriods.AddAsync(period);

        var act = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.PickLine,
            "PICK", Guid.NewGuid(), "P-DRY", 10m, "LINE",
            new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc), "K-DRY", "Pick dry run", period.Id);
        act.Rate(2.00m, 20.00m, "USD");
        await context.BillableActivities.AddAsync(act);
        await context.SaveChangesAsync();

        var billingService = new BillingRunService(context, NullLogger<BillingRunService>.Instance);
        var result = await billingService.ExecuteBillingRunAsync(new BillingRunRequest(
            fix.ClientId, period.Id, fix.StaffUserId, DryRun: true));

        Assert.True(result.IsSuccess);
        Assert.True(result.DryRun);
        Assert.Equal(1, result.InvoicesCreatedCount);
        Assert.Equal(20.00m, result.TotalInvoicedAmount);

        // Database should be unchanged
        Assert.Empty(await context.Invoices.ToListAsync());
        var refreshedPeriod = await context.BillingPeriods.FindAsync(period.Id);
        Assert.Equal(BillingPeriodStatus.Open, refreshedPeriod!.Status);
        var refreshedAct = await context.BillableActivities.FindAsync(act.Id);
        Assert.Equal(BillableActivityStatus.Rated, refreshedAct!.Status);
        Assert.Null(refreshedAct.InvoiceId);
    }

    [Fact]
    public async Task Test_08_Invoice_Approval_And_Issuance_Lifecycle()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var invoice = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-2026-TEST-001", DateTime.UtcNow.Date);
        invoice.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Handling", 1, "EA", 100m, 100m));
        await context.Invoices.AddAsync(invoice);
        await context.SaveChangesAsync();

        var invoiceService = new InvoiceService(context, NullLogger<InvoiceService>.Instance);

        // Cannot issue directly from Draft
        await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            invoiceService.IssueInvoiceAsync(invoice.Id, fix.StaffUserId));

        // Approve
        var approved = await invoiceService.ApproveInvoiceAsync(invoice.Id, fix.StaffUserId);
        Assert.Equal(InvoiceStatus.Approved, approved.Status);
        Assert.NotNull(approved.ApprovedAtUtc);

        // Issue
        var issued = await invoiceService.IssueInvoiceAsync(invoice.Id, fix.StaffUserId);
        Assert.Equal(InvoiceStatus.Issued, issued.Status);
        Assert.NotNull(issued.IssuedAtUtc);
    }

    [Fact]
    public async Task Test_09_Issued_Invoice_Is_Immutable()
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), "INV-IMMUTABLE-01", DateTime.UtcNow.Date);
        invoice.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Storage, "Storage", 1, "PALLET", 50m, 50m));
        invoice.Approve(Guid.NewGuid());
        invoice.Issue(Guid.NewGuid());

        // Attempting to add lines to an issued invoice must throw
        var ex = Assert.Throws<InvalidDomainStateException>(() =>
            invoice.AddLine(InvoiceLine.Create(2, InvoiceChargeCategory.Handling, "Handling", 1, "EA", 10m, 10m)));

        Assert.Contains("Issued", ex.Message);
    }

    [Fact]
    public async Task Test_10_CustomerPayment_Records_Total_And_Unallocated_Amount()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var arService = new AccountsReceivableService(context, NullLogger<AccountsReceivableService>.Instance);
        var payment = await arService.RecordPaymentAsync(new RecordCustomerPaymentRequest(
            ClientId: fix.ClientId,
            PaymentReference: "WIRE-99210",
            TotalAmount: 5000.00m,
            PaymentMethod: PaymentMethod.WireTransfer));

        Assert.Equal(5000.00m, payment.TotalAmount);
        Assert.Equal(0m, payment.AllocatedAmount);
        Assert.Equal(5000.00m, payment.UnallocatedAmount);
        Assert.False(payment.IsReversed);
    }

    [Fact]
    public async Task Test_11_Payment_Allocation_Across_Multiple_Invoices()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var inv1 = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-001", DateTime.UtcNow.Date);
        inv1.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Handling 1", 1, "EA", 300m, 300m));
        inv1.Approve(fix.StaffUserId);
        inv1.Issue(fix.StaffUserId);

        var inv2 = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-002", DateTime.UtcNow.Date);
        inv2.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Freight 1", 1, "EA", 500m, 500m));
        inv2.Approve(fix.StaffUserId);
        inv2.Issue(fix.StaffUserId);

        await context.Invoices.AddRangeAsync(inv1, inv2);
        await context.SaveChangesAsync();

        var arService = new AccountsReceivableService(context, NullLogger<AccountsReceivableService>.Instance);
        var payment = await arService.RecordPaymentAsync(new RecordCustomerPaymentRequest(
            fix.ClientId, "CHK-1002", 700.00m, PaymentMethod.Check));

        // Allocate $300 to Invoice 1 (full settlement)
        await arService.AllocatePaymentAsync(payment.Id, inv1.Id, 300.00m);

        // Allocate $400 to Invoice 2 (partial settlement)
        await arService.AllocatePaymentAsync(payment.Id, inv2.Id, 400.00m);

        var refreshedInv1 = await context.Invoices.FindAsync(inv1.Id);
        var refreshedInv2 = await context.Invoices.FindAsync(inv2.Id);
        var refreshedPayment = await context.CustomerPayments.FindAsync(payment.Id);

        Assert.Equal(InvoiceStatus.Paid, refreshedInv1!.Status);
        Assert.Equal(0m, refreshedInv1.OutstandingBalance);

        Assert.Equal(InvoiceStatus.PartiallyPaid, refreshedInv2!.Status);
        Assert.Equal(100.00m, refreshedInv2.OutstandingBalance);

        Assert.Equal(700.00m, refreshedPayment!.AllocatedAmount);
        Assert.Equal(0m, refreshedPayment.UnallocatedAmount);
    }

    [Fact]
    public async Task Test_12_Partial_Payment_Leaves_Invoice_In_PartiallyPaid_Status()
    {
        var inv = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), "INV-PARTIAL", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Work", 1, "EA", 1000m, 1000m));
        inv.Approve(Guid.NewGuid());
        inv.Issue(Guid.NewGuid());

        inv.ApplyPayment(400m);

        Assert.Equal(InvoiceStatus.PartiallyPaid, inv.Status);
        Assert.Equal(400m, inv.PaidAmount);
        Assert.Equal(600m, inv.OutstandingBalance);
    }

    [Fact]
    public async Task Test_13_Full_Payment_Settles_Invoice_To_Paid_Status()
    {
        var inv = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), "INV-FULL", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Work", 1, "EA", 1000m, 1000m));
        inv.Approve(Guid.NewGuid());
        inv.Issue(Guid.NewGuid());

        inv.ApplyPayment(1000m);

        Assert.Equal(InvoiceStatus.Paid, inv.Status);
        Assert.Equal(1000m, inv.PaidAmount);
        Assert.Equal(0m, inv.OutstandingBalance);
    }

    [Fact]
    public async Task Test_14_Payment_Allocation_Exceeding_Invoice_Balance_Throws()
    {
        var inv = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), "INV-EXCEED", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Work", 1, "EA", 500m, 500m));
        inv.Approve(Guid.NewGuid());
        inv.Issue(Guid.NewGuid());

        var ex = Assert.Throws<InvalidDomainStateException>(() => inv.ApplyPayment(600m));
        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_15_Payment_Allocation_Exceeding_Payment_Balance_Throws()
    {
        var payment = CustomerPayment.Create(Guid.NewGuid(), "WIRE-01", 100m, PaymentMethod.WireTransfer, DateTime.UtcNow.Date);

        var ex = Assert.Throws<InvalidDomainStateException>(() => payment.Allocate(Guid.NewGuid(), 150m));
        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_16_Payment_Allocation_Currency_Mismatch_Throws()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-CAD-01", DateTime.UtcNow.Date, currency: "CAD");
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Storage, "Storage", 1, "PALLET", 100m, 100m));
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(inv);

        var arService = new AccountsReceivableService(context, NullLogger<AccountsReceivableService>.Instance);
        var payment = await arService.RecordPaymentAsync(new RecordCustomerPaymentRequest(
            fix.ClientId, "USD-CHECK", 100m, PaymentMethod.Check, Currency: "USD"));

        await Assert.ThrowsAsync<ValidationException>(() =>
            arService.AllocatePaymentAsync(payment.Id, inv.Id, 100m));
    }

    [Fact]
    public async Task Test_17_Payment_Reversal_Restores_Invoice_Balance_And_Status()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-REV-01", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Ops", 1, "EA", 400m, 400m));
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(inv);

        var arService = new AccountsReceivableService(context, NullLogger<AccountsReceivableService>.Instance);
        var payment = await arService.RecordPaymentAsync(new RecordCustomerPaymentRequest(
            fix.ClientId, "ACH-BOUNCE", 400m, PaymentMethod.Ach));

        await arService.AllocatePaymentAsync(payment.Id, inv.Id, 400m);

        var paidInv = await context.Invoices.FindAsync(inv.Id);
        Assert.Equal(InvoiceStatus.Paid, paidInv!.Status);

        // Reverse the NSF / bounced payment
        await arService.ReversePaymentAsync(payment.Id, "ACH direct debit returned - NSF insufficient funds");

        var revertedInv = await context.Invoices.FindAsync(inv.Id);
        var revertedPayment = await context.CustomerPayments.FindAsync(payment.Id);

        Assert.Equal(InvoiceStatus.Issued, revertedInv!.Status);
        Assert.Equal(0m, revertedInv.PaidAmount);
        Assert.Equal(400m, revertedInv.OutstandingBalance);

        Assert.True(revertedPayment!.IsReversed);
        Assert.Equal("ACH direct debit returned - NSF insufficient funds", revertedPayment.ReversalReason);
    }

    [Fact]
    public async Task Test_18_Partial_Payment_Reversal_Calculates_Correct_Remaining_Balance()
    {
        var inv = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), "INV-REV-PART", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Ops", 1, "EA", 1000m, 1000m));
        inv.Approve(Guid.NewGuid());
        inv.Issue(Guid.NewGuid());

        inv.ApplyPayment(600m); // Paid: 600, Balance: 400, PartiallyPaid
        Assert.Equal(InvoiceStatus.PartiallyPaid, inv.Status);

        inv.ReversePayment(200m); // Paid: 400, Balance: 600, Still PartiallyPaid
        Assert.Equal(InvoiceStatus.PartiallyPaid, inv.Status);
        Assert.Equal(400m, inv.PaidAmount);
        Assert.Equal(600m, inv.OutstandingBalance);

        inv.ReversePayment(400m); // Paid: 0, Balance: 1000, Back to Issued
        Assert.Equal(InvoiceStatus.Issued, inv.Status);
        Assert.Equal(0m, inv.PaidAmount);
        Assert.Equal(1000m, inv.OutstandingBalance);
    }

    [Fact]
    public async Task Test_19_CustomerStatement_Reconciles_Totals()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var asOf = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

        var inv1 = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-STMT-1", asOf.AddDays(-20));
        inv1.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Work 1", 1, "EA", 1000m, 1000m));
        inv1.Approve(fix.StaffUserId);
        inv1.Issue(fix.StaffUserId);

        var inv2 = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-STMT-2", asOf.AddDays(-10));
        inv2.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Freight 1", 1, "EA", 500m, 500m));
        inv2.Approve(fix.StaffUserId);
        inv2.Issue(fix.StaffUserId);

        await context.Invoices.AddRangeAsync(inv1, inv2);
        await context.SaveChangesAsync();

        var arService = new AccountsReceivableService(context, NullLogger<AccountsReceivableService>.Instance);
        var payment = await arService.RecordPaymentAsync(new RecordCustomerPaymentRequest(
            fix.ClientId, "PAY-01", 600m, PaymentMethod.WireTransfer, PaymentDate: asOf.AddDays(-5)));

        await arService.AllocatePaymentAsync(payment.Id, inv1.Id, 600m);

        var stmt = await arService.GetCustomerStatementAsync(fix.ClientId, asOf);

        Assert.Equal(1500m, stmt.TotalInvoiced);
        Assert.Equal(600m, stmt.TotalPaid);
        Assert.Equal(0m, stmt.TotalCredited);
        Assert.Equal(900m, stmt.TotalOutstandingBalance); // 400 (inv1) + 500 (inv2)
        Assert.Equal(3, stmt.Transactions.Count); // 2 Invoices + 1 Payment
    }

    [Fact]
    public async Task Test_20_CustomerStatement_Aging_Buckets_Classification()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var asOf = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        // Current (DueDate in future: 2026-12-10)
        var invCurrent = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-CURR", asOf, paymentTermsDays: 10);
        invCurrent.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Current", 1, "EA", 100m, 100m));
        invCurrent.Approve(fix.StaffUserId);
        invCurrent.Issue(fix.StaffUserId);

        // Days1To30 (DueDate 15 days ago: 2026-11-16)
        var inv1To30 = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-1TO30", asOf.AddDays(-45), paymentTermsDays: 30);
        inv1To30.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "1to30", 1, "EA", 200m, 200m));
        inv1To30.Approve(fix.StaffUserId);
        inv1To30.Issue(fix.StaffUserId);

        // Days31To60 (DueDate 45 days ago: 2026-10-17)
        var inv31To60 = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-31TO60", asOf.AddDays(-75), paymentTermsDays: 30);
        inv31To60.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "31to60", 1, "EA", 300m, 300m));
        inv31To60.Approve(fix.StaffUserId);
        inv31To60.Issue(fix.StaffUserId);

        // Days61To90 (DueDate 75 days ago: 2026-09-17)
        var inv61To90 = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-61TO90", asOf.AddDays(-105), paymentTermsDays: 30);
        inv61To90.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "61to90", 1, "EA", 400m, 400m));
        inv61To90.Approve(fix.StaffUserId);
        inv61To90.Issue(fix.StaffUserId);

        // DaysOver90 (DueDate 110 days ago: 2026-08-13)
        var invOver90 = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-OVER90", asOf.AddDays(-140), paymentTermsDays: 30);
        invOver90.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Over90", 1, "EA", 500m, 500m));
        invOver90.Approve(fix.StaffUserId);
        invOver90.Issue(fix.StaffUserId);

        await context.Invoices.AddRangeAsync(invCurrent, inv1To30, inv31To60, inv61To90, invOver90);
        await context.SaveChangesAsync();

        var arService = new AccountsReceivableService(context, NullLogger<AccountsReceivableService>.Instance);
        var stmt = await arService.GetCustomerStatementAsync(fix.ClientId, asOf);

        Assert.Equal(100m, stmt.Aging.Current);
        Assert.Equal(200m, stmt.Aging.Days1To30);
        Assert.Equal(300m, stmt.Aging.Days31To60);
        Assert.Equal(400m, stmt.Aging.Days61To90);
        Assert.Equal(500m, stmt.Aging.DaysOver90);
        Assert.Equal(1500m, stmt.Aging.TotalOutstanding);
    }

    [Fact]
    public async Task Test_21_CreditNote_Creation_And_Issuance()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-ORIG-01", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Storage, "Storage", 10, "PALLET", 10m, 100m));
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(inv);
        await context.SaveChangesAsync();

        var invoiceService = new InvoiceService(context, NullLogger<InvoiceService>.Instance);
        var creditNote = await invoiceService.CreateCreditNoteAsync(new CreateCreditNoteRequest(
            ClientId: fix.ClientId,
            OriginalInvoiceId: inv.Id,
            Reason: "Storage rate adjustment for empty pallets",
            Lines: new[] { new CreditNoteLineDto(inv.Lines.First().Id, "Adjusted 2 pallets", 2, 10m, 20m) }));

        Assert.Equal(CreditNoteStatus.Draft, creditNote.Status);
        Assert.Equal(20m, creditNote.TotalAmount);
        Assert.StartsWith("CRN-", creditNote.CreditNoteNumber);

        var issuedCn = await invoiceService.IssueCreditNoteAsync(creditNote.Id);
        Assert.Equal(CreditNoteStatus.Issued, issuedCn.Status);
    }

    [Fact]
    public async Task Test_22_CreditNote_Application_Reduces_OutstandingBalance()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-CRN-APPLY", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Work", 1, "EA", 500m, 500m));
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(inv);
        await context.SaveChangesAsync();

        var invoiceService = new InvoiceService(context, NullLogger<InvoiceService>.Instance);
        var creditNote = await invoiceService.CreateCreditNoteAsync(new CreateCreditNoteRequest(
            fix.ClientId, inv.Id, "Credit allowance",
            new[] { new CreditNoteLineDto(null, "Goodwill allowance", 1, 100m, 100m) }));

        await invoiceService.IssueCreditNoteAsync(creditNote.Id);
        await invoiceService.ApplyCreditNoteToInvoiceAsync(creditNote.Id, inv.Id, 100m);

        var refreshedInv = await context.Invoices.FindAsync(inv.Id);
        Assert.Equal(100m, refreshedInv!.CreditedAmount);
        Assert.Equal(400m, refreshedInv.OutstandingBalance);
        Assert.Equal(InvoiceStatus.PartiallyPaid, refreshedInv.Status);
    }

    [Fact]
    public async Task Test_23_Combined_Payment_And_CreditNote_Full_Settlement()
    {
        var inv = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), "INV-COMBO", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Ops", 1, "EA", 1000m, 1000m));
        inv.Approve(Guid.NewGuid());
        inv.Issue(Guid.NewGuid());

        // Apply $700 payment
        inv.ApplyPayment(700m);
        Assert.Equal(InvoiceStatus.PartiallyPaid, inv.Status);

        // Apply $300 credit
        inv.ApplyCredit(300m);
        Assert.Equal(InvoiceStatus.Paid, inv.Status);
        Assert.Equal(0m, inv.OutstandingBalance);
        Assert.Equal(700m, inv.PaidAmount);
        Assert.Equal(300m, inv.CreditedAmount);
    }

    [Fact]
    public async Task Test_24_Dispute_Submission_Marks_Invoice_Disputed()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-DISP-01", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Freight", 1, "EA", 600m, 600m));
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(inv);
        await context.SaveChangesAsync();

        var disputeService = new DisputeManagementService(context, NullLogger<DisputeManagementService>.Instance);
        var dispute = await disputeService.SubmitDisputeAsync(new SubmitDisputeRequest(
            ClientId: fix.ClientId,
            InvoiceId: inv.Id,
            DisputedAmount: 150m,
            ReasonCode: DisputeReasonCode.PricingError,
            ReasonDescription: "Freight markup exceeded contractual cap"));

        Assert.Equal(DisputeStatus.Submitted, dispute.Status);
        Assert.Equal(150m, dispute.DisputedAmount);
        Assert.StartsWith("DSP-INV-DISP-01-", dispute.DisputeReference);

        var refreshedInv = await context.Invoices.FindAsync(inv.Id);
        Assert.Equal(InvoiceStatus.Disputed, refreshedInv!.Status);
    }

    [Fact]
    public async Task Test_25_Dispute_Review_Transitions_To_UnderReview()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-DISP-REV", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Handling", 1, "EA", 300m, 300m));
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(inv);

        var dispute = InvoiceDispute.Create(fix.ClientId, inv.Id, "DSP-01", 50m, DisputeReasonCode.QuantityDiscrepancy, "Wrong count");
        await context.InvoiceDisputes.AddAsync(dispute);
        await context.SaveChangesAsync();

        var disputeService = new DisputeManagementService(context, NullLogger<DisputeManagementService>.Instance);
        var reviewed = await disputeService.ReviewDisputeAsync(new ReviewDisputeRequest(
            dispute.Id, fix.StaffUserId, "Auditing warehouse pick logs"));

        Assert.Equal(DisputeStatus.UnderReview, reviewed.Status);
        Assert.Equal("Auditing warehouse pick logs", reviewed.ReviewNotes);
    }

    [Fact]
    public async Task Test_26_Dispute_Acceptance_Issues_CreditNote_And_Reduces_Balance()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-DISP-ACC", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Freight", 1, "EA", 400m, 400m));
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(inv);
        await context.SaveChangesAsync();

        var disputeService = new DisputeManagementService(context, NullLogger<DisputeManagementService>.Instance);
        var dispute = await disputeService.SubmitDisputeAsync(new SubmitDisputeRequest(
            fix.ClientId, inv.Id, 100m, DisputeReasonCode.PricingError, "Billed rate above contract"));

        // Accept dispute for $100
        var resolved = await disputeService.ResolveDisputeAsync(new ResolveDisputeRequest(
            DisputeId: dispute.Id,
            ResolvedByUserId: fix.StaffUserId,
            Accept: true,
            ApprovedCreditAmount: 100m,
            ResolutionNotes: "Verified agreement schedule; credit approved"));

        Assert.Equal(DisputeStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.CreditNoteId);

        var refreshedInv = await context.Invoices.FindAsync(inv.Id);
        Assert.Equal(100m, refreshedInv!.CreditedAmount);
        Assert.Equal(300m, refreshedInv.OutstandingBalance);
        Assert.Equal(InvoiceStatus.PartiallyPaid, refreshedInv.Status);

        var creditNote = await context.CreditNotes.FindAsync(resolved.CreditNoteId);
        Assert.NotNull(creditNote);
        Assert.Equal(CreditNoteStatus.Applied, creditNote.Status);
        Assert.Equal(100m, creditNote.TotalAmount);
    }

    [Fact]
    public async Task Test_27_Dispute_Rejection_Restores_Invoice_Status()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-DISP-REJ", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Freight", 1, "EA", 400m, 400m));
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(inv);
        await context.SaveChangesAsync();

        var disputeService = new DisputeManagementService(context, NullLogger<DisputeManagementService>.Instance);
        var dispute = await disputeService.SubmitDisputeAsync(new SubmitDisputeRequest(
            fix.ClientId, inv.Id, 50m, DisputeReasonCode.UnauthorizedAccessorial, "Claimed liftgate not needed"));

        // Reject dispute with justification
        var resolved = await disputeService.ResolveDisputeAsync(new ResolveDisputeRequest(
            DisputeId: dispute.Id,
            ResolvedByUserId: fix.StaffUserId,
            Accept: false,
            ApprovedCreditAmount: null,
            ResolutionNotes: "Carrier delivery receipt confirms hydraulic liftgate was required by receiver"));

        Assert.Equal(DisputeStatus.Resolved, resolved.Status);
        Assert.Null(resolved.CreditNoteId);

        var refreshedInv = await context.Invoices.FindAsync(inv.Id);
        Assert.Equal(InvoiceStatus.Issued, refreshedInv!.Status);
        Assert.Equal(400m, refreshedInv.OutstandingBalance);
    }

    [Fact]
    public async Task Test_28_Dispute_Validations_Prevent_Invalid_Disputes()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // Draft invoice cannot be disputed
        var draftInv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-DRAFT", DateTime.UtcNow.Date);
        draftInv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Work", 1, "EA", 100m, 100m));
        await context.Invoices.AddAsync(draftInv);
        await context.SaveChangesAsync();

        var disputeService = new DisputeManagementService(context, NullLogger<DisputeManagementService>.Instance);

        await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            disputeService.SubmitDisputeAsync(new SubmitDisputeRequest(
                fix.ClientId, draftInv.Id, 50m, DisputeReasonCode.PricingError, "Dispute draft")));

        // Issued invoice cannot dispute amount exceeding total
        var issuedInv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-VAL", DateTime.UtcNow.Date);
        issuedInv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Work", 1, "EA", 100m, 100m));
        issuedInv.Approve(fix.StaffUserId);
        issuedInv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(issuedInv);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            disputeService.SubmitDisputeAsync(new SubmitDisputeRequest(
                fix.ClientId, issuedInv.Id, 150m, DisputeReasonCode.PricingError, "Exceeds total")));
    }

    [Fact]
    public async Task Test_29_Void_Invoice_Releases_BillableActivities()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var act = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.InboundReceiving,
            "REC", Guid.NewGuid(), "REC-V1", 1m, "PALLET",
            DateTime.UtcNow, "K-VOID", "Receiving");
        act.Rate(50m, 50m, "USD");
        await context.BillableActivities.AddAsync(act);

        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-TO-VOID", DateTime.UtcNow.Date);
        var line = InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Receiving", 1, "PALLET", 50m, 50m, act.Id);
        inv.AddLine(line);
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        act.MarkInvoiced(inv.Id, line.Id);

        await context.Invoices.AddAsync(inv);
        await context.SaveChangesAsync();

        var invoiceService = new InvoiceService(context, NullLogger<InvoiceService>.Instance);
        var voided = await invoiceService.VoidInvoiceAsync(inv.Id, fix.StaffUserId, "Client requested cancellation due to wrong billing address");

        Assert.Equal(InvoiceStatus.Voided, voided.Status);

        var refreshedAct = await context.BillableActivities.FindAsync(act.Id);
        Assert.NotNull(refreshedAct);
        Assert.Equal(BillableActivityStatus.Rated, refreshedAct.Status);
        Assert.Null(refreshedAct.InvoiceId);
        Assert.Null(refreshedAct.InvoiceLineId);
    }

    [Fact]
    public async Task Test_30_Void_Invoice_Prevented_When_Payments_Allocated()
    {
        var inv = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), "INV-VOID-ERR", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Work", 1, "EA", 500m, 500m));
        inv.Approve(Guid.NewGuid());
        inv.Issue(Guid.NewGuid());
        inv.ApplyPayment(200m);

        var ex = Assert.Throws<InvalidDomainStateException>(() => inv.Void(Guid.NewGuid(), "Try void"));
        Assert.Contains("active payment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_31_BillingRun_Handles_Waived_Activities_At_Zero_Amount()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var period = BillingPeriod.Create(
            fix.ClientId, "2026-JUN-M", "June 2026 Billing",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc));
        await context.BillingPeriods.AddAsync(period);

        var actWaived = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.InboundInspection,
            "INSP", Guid.NewGuid(), "INS-01", 10m, "PALLET",
            new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc), "K-WAIVE", "Waived QA", period.Id);
        actWaived.Waive("Contractual courtesy waiver for launch month");

        await context.BillableActivities.AddAsync(actWaived);
        await context.SaveChangesAsync();

        var billingService = new BillingRunService(context, NullLogger<BillingRunService>.Instance);
        var result = await billingService.ExecuteBillingRunAsync(new BillingRunRequest(fix.ClientId, period.Id, fix.StaffUserId));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.InvoicesCreatedCount);
        Assert.Equal(0m, result.TotalInvoicedAmount);

        var invoice = await context.Invoices.Include(i => i.Lines).FirstAsync();
        Assert.Single(invoice.Lines);
        Assert.Equal(0m, invoice.Lines.First().TotalAmount);
    }

    [Fact]
    public async Task Test_32_ChargeCategory_Classification_Across_Operations()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var period = BillingPeriod.Create(
            fix.ClientId, "2026-MAY-M", "May 2026 Billing",
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 31, 23, 59, 59, DateTimeKind.Utc));
        await context.BillingPeriods.AddAsync(period);

        var a1 = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.StorageCbmDay, "SNP", Guid.NewGuid(), "R1", 10m, "CBM_DAY", new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc), "K1", "Storage", period.Id);
        a1.Rate(1m, 10m, "USD");

        var a2 = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.ReturnProcessing, "RET", Guid.NewGuid(), "R2", 5m, "UNIT", new DateTime(2026, 5, 6, 0, 0, 0, DateTimeKind.Utc), "K2", "Return", period.Id);
        a2.Rate(2m, 10m, "USD");

        var a3 = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.CarrierFreight, "SHP", Guid.NewGuid(), "R3", 1m, "SHIPMENT", new DateTime(2026, 5, 7, 0, 0, 0, DateTimeKind.Utc), "K3", "Carrier", period.Id);
        a3.Rate(50m, 50m, "USD");

        var a4 = BillableActivity.Create(fix.ClientId, fix.FacilityId, BillableActivityType.KittingAndAssembly, "VAS", Guid.NewGuid(), "R4", 20m, "UNIT", new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc), "K4", "Kitting", period.Id);
        a4.Rate(3m, 60m, "USD");

        await context.BillableActivities.AddRangeAsync(a1, a2, a3, a4);
        await context.SaveChangesAsync();

        var billingService = new BillingRunService(context, NullLogger<BillingRunService>.Instance);
        await billingService.ExecuteBillingRunAsync(new BillingRunRequest(fix.ClientId, period.Id, fix.StaffUserId));

        var invoice = await context.Invoices.Include(i => i.Lines).FirstAsync();
        Assert.Equal(4, invoice.Lines.Count);

        Assert.Equal(InvoiceChargeCategory.Storage, invoice.Lines.First(l => l.ActivityType == BillableActivityType.StorageCbmDay).ChargeCategory);
        Assert.Equal(InvoiceChargeCategory.Handling, invoice.Lines.First(l => l.ActivityType == BillableActivityType.ReturnProcessing).ChargeCategory);
        Assert.Equal(InvoiceChargeCategory.Freight, invoice.Lines.First(l => l.ActivityType == BillableActivityType.CarrierFreight).ChargeCategory);
        Assert.Equal(InvoiceChargeCategory.Vas, invoice.Lines.First(l => l.ActivityType == BillableActivityType.KittingAndAssembly).ChargeCategory);
    }

    [Fact]
    public async Task Test_33_RecordPayment_With_AutoAllocate_Applies_FIFO_By_DueDate()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // Older invoice (due earlier)
        var oldInv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-OLD", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), paymentTermsDays: 15);
        oldInv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Old work", 1, "EA", 400m, 400m));
        oldInv.Approve(fix.StaffUserId);
        oldInv.Issue(fix.StaffUserId);

        // Newer invoice (due later)
        var newInv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-NEW", new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), paymentTermsDays: 30);
        newInv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "New work", 1, "EA", 500m, 500m));
        newInv.Approve(fix.StaffUserId);
        newInv.Issue(fix.StaffUserId);

        await context.Invoices.AddRangeAsync(oldInv, newInv);
        await context.SaveChangesAsync();

        var arService = new AccountsReceivableService(context, NullLogger<AccountsReceivableService>.Instance);

        // Single $600 payment with AutoAllocate = true
        var payment = await arService.RecordPaymentAsync(new RecordCustomerPaymentRequest(
            ClientId: fix.ClientId,
            PaymentReference: "FIFO-WIRE-01",
            TotalAmount: 600m,
            PaymentMethod: PaymentMethod.WireTransfer,
            AutoAllocate: true));

        var refreshedOld = await context.Invoices.FindAsync(oldInv.Id);
        var refreshedNew = await context.Invoices.FindAsync(newInv.Id);

        // Old invoice must be completely paid ($400 allocated)
        Assert.Equal(InvoiceStatus.Paid, refreshedOld!.Status);
        Assert.Equal(0m, refreshedOld.OutstandingBalance);

        // New invoice must be partially paid ($200 allocated, $300 remaining)
        Assert.Equal(InvoiceStatus.PartiallyPaid, refreshedNew!.Status);
        Assert.Equal(300m, refreshedNew.OutstandingBalance);

        // Payment fully allocated
        Assert.Equal(600m, payment.AllocatedAmount);
        Assert.Equal(0m, payment.UnallocatedAmount);
    }
}
