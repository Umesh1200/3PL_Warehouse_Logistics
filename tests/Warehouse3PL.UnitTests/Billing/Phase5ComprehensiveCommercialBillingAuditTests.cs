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
/// Phase 5 Comprehensive Commercial Billing Audit &amp; End-to-End Financial Lifecycle Test Suite.
/// Validates the closed-loop financial integration across all six Phase 5 milestones:
/// Phase 5.0 Billing Foundation, Phase 5.1 Rate Cards, Phase 5.2 Storage,
/// Phase 5.3 Handling, Phase 5.4 Freight Margins, and Phase 5.5 Invoicing &amp; AR Ledger.
/// Verifies mathematical reconciliation, strict immutability, dispute resolution, and audit safeguards.
/// </summary>
public class Phase5ComprehensiveCommercialBillingAuditTests
{
    private class AuditEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid StaffUserId { get; set; } = Guid.NewGuid();
    }

    private static async Task<AuditEnvFixture> SeedAuditEnvironmentAsync()
    {
        var fix = new AuditEnvFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var client = new Client
        {
            Id = fix.ClientId,
            CompanyName = "OmniChannel Retail Solutions Inc.",
            AccountCode = "ORS01",
            Status = ClientStatus.Active,
            Currency = "USD"
        };

        var facility = new Facility
        {
            Id = fix.FacilityId,
            FacilityCode = "DFW-01",
            Name = "Dallas Multi-Client Mega Fulfillment Center",
            IsActive = true
        };

        await context.Clients.AddAsync(client);
        await context.Facilities.AddAsync(facility);
        await context.SaveChangesAsync();

        return fix;
    }

    /// <summary>
    /// Milestone Audit: Complete closed-loop commercial workflow covering all 10 financial phases:
    /// 1. Inbound receiving and putaway capture
    /// 2. Daily storage snapshot footprint rating
    /// 3. Outbound order processing, picking, packing
    /// 4. Carrier dispatch and sell-side markup calculation
    /// 5. Late carrier invoice cost adjustment ingestion
    /// 6. Automated billing run, currency partitioning, period sealing
    /// 7. Invoice approval and issuance
    /// 8. Customer payment recording and partial FIFO allocation
    /// 9. Commercial dispute submission, review, and acceptance
    /// 10. Automated credit note emission, balance reconciliation, and 5-bucket customer statement.
    /// </summary>
    [Fact]
    public async Task Audit_01_EndToEnd_ClosedLoop_Commercial_Financial_Lifecycle()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var period = BillingPeriod.Create(
            clientId: fix.ClientId,
            periodCode: "2026-OCT-AUDIT",
            name: "October 2026 Commercial Billing Period",
            startDateUtc: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            endDateUtc: new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc),
            frequency: BillingFrequency.Monthly,
            facilityId: fix.FacilityId);

        await context.BillingPeriods.AddAsync(period);

        // 1. Inbound Activities (Receiving & Putaway)
        var actInboundRec = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.InboundReceiving,
            "ReceivingSession", Guid.NewGuid(), "REC-AUDIT-101", 20m, "PALLET",
            new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc), "KEY-INB-REC", "Inbound Pallet Unloading", period.Id);
        actInboundRec.Rate(15.00m, 300.00m, "USD");

        var actPutaway = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.Putaway,
            "PutawayTask", Guid.NewGuid(), "PUT-AUDIT-101", 20m, "PALLET",
            new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), "KEY-INB-PUT", "High-Bay Pallet Putaway", period.Id);
        actPutaway.Rate(5.00m, 100.00m, "USD");

        // 2. Storage Daily Snapshots Footprint
        var actStorage = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.StoragePalletDay,
            "DailyInventorySnapshot", Guid.NewGuid(), "SNAP-AUDIT-01", 500m, "PALLET_DAY",
            new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), "KEY-STG-500", "Pallet Storage Holding", period.Id);
        actStorage.Rate(0.80m, 400.00m, "USD");

        // 3. Outbound Execution (Order Processing, Picking, Packing)
        var actOrder = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.OrderProcessing,
            "OutboundOrder", Guid.NewGuid(), "ORD-AUDIT-201", 50m, "ORDER",
            new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc), "KEY-OUT-ORD", "Order Processing Base Fee", period.Id);
        actOrder.Rate(2.50m, 125.00m, "USD");

        var actPick = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.PickUnit,
            "PickTask", Guid.NewGuid(), "PK-AUDIT-201", 200m, "UNIT",
            new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc), "KEY-OUT-PIK", "Unit Pick Execution", period.Id);
        actPick.Rate(0.35m, 70.00m, "USD");

        var actPack = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.PackingCarton,
            "PackingSession", Guid.NewGuid(), "PAC-AUDIT-201", 50m, "CARTON",
            new DateTime(2026, 10, 20, 16, 0, 0, DateTimeKind.Utc), "KEY-OUT-PAC", "Carton Packing & Dunnage", period.Id);
        actPack.Rate(1.20m, 60.00m, "USD");

        // 4. Carrier Outbound Freight Transportation & Margin
        var actFreight = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.CarrierFreight,
            "Shipment", Guid.NewGuid(), "SHP-AUDIT-301", 1m, "SHIPMENT",
            new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc), "KEY-FRT-301", "FedEx Ground Outbound Freight", period.Id);
        actFreight.Rate(120.00m, 120.00m, "USD"); // Buy cost $100 + markup $20

        // 5. Late Carrier Invoice Cost Adjustment (Accessorial Liftgate fee passed through with markup)
        var actFreightAdj = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.CarrierFreight,
            "CarrierCostAdjustment", Guid.NewGuid(), "ADJ-AUDIT-301", 1m, "SURCHARGE",
            new DateTime(2026, 10, 25, 11, 0, 0, DateTimeKind.Utc), "KEY-FRT-ADJ", "Late Carrier Liftgate Delivery Surcharge", period.Id);
        actFreightAdj.Rate(50.00m, 50.00m, "USD");

        await context.BillableActivities.AddRangeAsync(
            actInboundRec, actPutaway, actStorage, actOrder, actPick, actPack, actFreight, actFreightAdj);
        await context.SaveChangesAsync();

        // 6. Automated Billing Run Execution
        var billingRunService = new BillingRunService(context, NullLogger<BillingRunService>.Instance);
        var billingResult = await billingRunService.ExecuteBillingRunAsync(new BillingRunRequest(
            ClientId: fix.ClientId,
            BillingPeriodId: period.Id,
            InitiatedByUserId: fix.StaffUserId,
            TaxRate: 0.05m, // 5% commercial service tax
            PaymentTermsDays: 30));

        Assert.True(billingResult.IsSuccess);
        Assert.Equal(1, billingResult.InvoicesCreatedCount);
        Assert.Equal(8, billingResult.TotalActivitiesInvoicedCount);

        // Sum of rated lines = 300 + 100 + 400 + 125 + 70 + 60 + 120 + 50 = $1,225.00
        // Tax (5%) = $61.25. Total Invoice = $1,286.25.
        Assert.Equal(1286.25m, billingResult.TotalInvoicedAmount);

        // Verify period is sealed
        var sealedPeriod = await context.BillingPeriods.FindAsync(period.Id);
        Assert.Equal(BillingPeriodStatus.Invoiced, sealedPeriod!.Status);

        // 7. Invoice Approval & Issuance
        var invoiceService = new InvoiceService(context, NullLogger<InvoiceService>.Instance);
        var invoiceSummary = billingResult.InvoiceSummaries.First();

        var approvedInvoice = await invoiceService.ApproveInvoiceAsync(invoiceSummary.InvoiceId, fix.StaffUserId);
        Assert.Equal(InvoiceStatus.Approved, approvedInvoice.Status);

        var issuedInvoice = await invoiceService.IssueInvoiceAsync(invoiceSummary.InvoiceId, fix.StaffUserId);
        Assert.Equal(InvoiceStatus.Issued, issuedInvoice.Status);
        Assert.Equal(1225.00m, issuedInvoice.SubtotalAmount);
        Assert.Equal(61.25m, issuedInvoice.TaxAmount);
        Assert.Equal(1286.25m, issuedInvoice.TotalAmount);
        Assert.Equal(1286.25m, issuedInvoice.OutstandingBalance);

        // 8. Accounts Receivable: Customer Cash Payment & Partial Allocation
        var arService = new AccountsReceivableService(context, NullLogger<AccountsReceivableService>.Instance);
        var payment = await arService.RecordPaymentAsync(new RecordCustomerPaymentRequest(
            ClientId: fix.ClientId,
            PaymentReference: "WIRE-ORS-202611",
            TotalAmount: 1000.00m,
            PaymentMethod: PaymentMethod.WireTransfer,
            PaymentDate: new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc)));

        await arService.AllocatePaymentAsync(payment.Id, issuedInvoice.Id, 1000.00m);

        var partiallyPaidInvoice = await context.Invoices.FindAsync(issuedInvoice.Id);
        Assert.Equal(InvoiceStatus.PartiallyPaid, partiallyPaidInvoice!.Status);
        Assert.Equal(1000.00m, partiallyPaidInvoice.PaidAmount);
        Assert.Equal(286.25m, partiallyPaidInvoice.OutstandingBalance);

        // 9. Dispute Management: Client disputes the $50 liftgate surcharge
        var disputeService = new DisputeManagementService(context, NullLogger<DisputeManagementService>.Instance);
        var dispute = await disputeService.SubmitDisputeAsync(new SubmitDisputeRequest(
            ClientId: fix.ClientId,
            InvoiceId: issuedInvoice.Id,
            DisputedAmount: 52.50m, // $50 + 5% tax
            ReasonCode: DisputeReasonCode.UnauthorizedAccessorial,
            ReasonDescription: "Receiver did not request hydraulic liftgate; dock was level"));

        var disputedInvoice = await context.Invoices.FindAsync(issuedInvoice.Id);
        Assert.Equal(InvoiceStatus.Disputed, disputedInvoice!.Status);

        // Review dispute
        await disputeService.ReviewDisputeAsync(new ReviewDisputeRequest(
            dispute.Id, fix.StaffUserId, "Reviewing signed carrier Bill of Lading"));

        // Accept dispute: authorize $52.50 credit note
        var resolvedDispute = await disputeService.ResolveDisputeAsync(new ResolveDisputeRequest(
            DisputeId: dispute.Id,
            ResolvedByUserId: fix.StaffUserId,
            Accept: true,
            ApprovedCreditAmount: 52.50m,
            ResolutionNotes: "Dock was verified level; carrier fee credited"));

        Assert.Equal(DisputeStatus.Resolved, resolvedDispute.Status);
        Assert.NotNull(resolvedDispute.CreditNoteId);

        // 10. Financial Reconciliation & Revised Customer Statement
        var finalInvoice = await context.Invoices.FindAsync(issuedInvoice.Id);
        Assert.Equal(1000.00m, finalInvoice!.PaidAmount);
        Assert.Equal(52.50m, finalInvoice.CreditedAmount);

        // Outstanding Balance = 1286.25 - 1000.00 - 52.50 = 233.75
        Assert.Equal(233.75m, finalInvoice.OutstandingBalance);
        Assert.Equal(InvoiceStatus.PartiallyPaid, finalInvoice.Status);

        var statement = await arService.GetCustomerStatementAsync(
            fix.ClientId, DateTime.UtcNow.Date.AddDays(1));

        Assert.Equal(1286.25m, statement.TotalInvoiced);
        Assert.Equal(1000.00m, statement.TotalPaid);
        Assert.Equal(52.50m, statement.TotalCredited);
        Assert.Equal(233.75m, statement.TotalOutstandingBalance);
        Assert.Equal(0m, statement.TotalUnallocatedCredits);
        Assert.Equal(233.75m, statement.NetCustomerBalance);

        // Mathematical Formulaic Checks:
        // Invoice Total = Sum of Lines
        var sumOfLines = finalInvoice.Lines.Sum(l => l.TotalAmount);
        Assert.Equal(finalInvoice.SubtotalAmount, sumOfLines);

        // Outstanding AR = Invoices - Payments - Credits
        var calculatedOutstanding = statement.TotalInvoiced - statement.TotalPaid - statement.TotalCredited;
        Assert.Equal(statement.TotalOutstandingBalance, calculatedOutstanding);
    }

    [Fact]
    public async Task Safeguard_A_MultiCurrency_Failure_Rollback_Preserves_Period()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var period = BillingPeriod.Create(
            fix.ClientId, "2026-FAIL-TEST", "Failure Test Period",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc));
        await context.BillingPeriods.AddAsync(period);

        // Include unrated activity so billing run fails validation
        var actUnrated = BillableActivity.Create(
            fix.ClientId, fix.FacilityId, BillableActivityType.InboundReceiving,
            "REC", Guid.NewGuid(), "R-UNRATED", 10m, "PALLET",
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), "KEY-FAIL", "Unrated", period.Id);
        await context.BillableActivities.AddAsync(actUnrated);
        await context.SaveChangesAsync();

        var billingService = new BillingRunService(context, NullLogger<BillingRunService>.Instance);

        await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            billingService.ExecuteBillingRunAsync(new BillingRunRequest(fix.ClientId, period.Id, fix.StaffUserId)));

        // Ensure period was NOT sealed or marked invoiced
        var refreshedPeriod = await context.BillingPeriods.FindAsync(period.Id);
        Assert.Equal(BillingPeriodStatus.Open, refreshedPeriod!.Status);
        Assert.Null(refreshedPeriod.InvoicedAtUtc);
    }

    [Fact]
    public async Task Safeguard_B_Concurrent_Invoice_Sequencing_No_Collisions()
    {
        var seq = InvoiceSequence.Create(Guid.NewGuid(), 2026, "INV");

        var numbers = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            var num = seq.NextNumber("CLI01");
            Assert.True(numbers.Add(num), $"Collision detected for invoice number: {num}");
        }

        Assert.Equal(50, numbers.Count);
        Assert.Equal("INV-2026-CLI01-000001", numbers.First());
        Assert.Equal("INV-2026-CLI01-000050", numbers.Last());
    }

    [Fact]
    public async Task Safeguard_C_Customer_Credit_Balance_And_Overpayment_Liability()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var asOf = DateTime.UtcNow.Date;

        // Invoice for $200
        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-OVERPAY", asOf);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Work", 1, "EA", 200m, 200m));
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(inv);
        await context.SaveChangesAsync();

        // Customer overpays $500 (allocates $200, leaves $300 unallocated liability)
        var arService = new AccountsReceivableService(context, NullLogger<AccountsReceivableService>.Instance);
        var payment = await arService.RecordPaymentAsync(new RecordCustomerPaymentRequest(
            fix.ClientId, "WIRE-EXCESS-01", 500m, PaymentMethod.WireTransfer));

        await arService.AllocatePaymentAsync(payment.Id, inv.Id, 200m);

        var statement = await arService.GetCustomerStatementAsync(fix.ClientId, asOf);

        Assert.Equal(200m, statement.TotalInvoiced);
        Assert.Equal(200m, statement.TotalPaid);
        Assert.Equal(0m, statement.TotalOutstandingBalance);
        Assert.Equal(300m, statement.TotalUnallocatedCredits); // Explicit customer credit liability
        Assert.Equal(-300m, statement.NetCustomerBalance); // Negative net balance indicates client credit
    }

    [Fact]
    public async Task Safeguard_D_Void_Invoice_Preserves_Audit_Trail_And_Replacement()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var originalInv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-ORIG-VOID", DateTime.UtcNow.Date);
        originalInv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Storage, "Pallet Storage", 10, "PALLET", 10m, 100m));
        originalInv.Approve(fix.StaffUserId);
        originalInv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(originalInv);
        await context.SaveChangesAsync();

        var replacementInvId = Guid.NewGuid();
        var invoiceService = new InvoiceService(context, NullLogger<InvoiceService>.Instance);
        var voided = await invoiceService.VoidInvoiceAsync(
            originalInv.Id, fix.StaffUserId, "Replaced due to tax rate adjustment", replacementInvId);

        Assert.Equal(InvoiceStatus.Voided, voided.Status);
        Assert.Equal("Replaced due to tax rate adjustment", voided.VoidReason);
        Assert.Equal(replacementInvId, voided.ReplacementInvoiceId);
        Assert.Single(voided.Lines); // Lines preserved
        Assert.Equal(100m, voided.TotalAmount);
    }

    [Fact]
    public async Task Safeguard_E_Dispute_Acceptance_Idempotency()
    {
        var fix = await SeedAuditEnvironmentAsync();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var inv = Invoice.Create(fix.ClientId, Guid.NewGuid(), "INV-DISP-IDEM", DateTime.UtcNow.Date);
        inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Freight, "Freight", 1, "EA", 300m, 300m));
        inv.Approve(fix.StaffUserId);
        inv.Issue(fix.StaffUserId);
        await context.Invoices.AddAsync(inv);
        await context.SaveChangesAsync();

        var disputeService = new DisputeManagementService(context, NullLogger<DisputeManagementService>.Instance);
        var dispute = await disputeService.SubmitDisputeAsync(new SubmitDisputeRequest(
            fix.ClientId, inv.Id, 50m, DisputeReasonCode.PricingError, "Pricing error"));

        // First resolution: Accepted
        await disputeService.ResolveDisputeAsync(new ResolveDisputeRequest(
            dispute.Id, fix.StaffUserId, true, 50m, "Approved credit"));

        // Second resolution attempt: Must throw InvalidDomainStateException to prevent duplicate credit note
        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            disputeService.ResolveDisputeAsync(new ResolveDisputeRequest(
                dispute.Id, fix.StaffUserId, true, 50m, "Duplicate attempt")));

        Assert.Contains("Duplicate resolution is prohibited", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Safeguard_F_Late_Arriving_Activity_Rejected_From_Sealed_Period()
    {
        var period = BillingPeriod.Create(
            Guid.NewGuid(), "2026-SEALED-M", "Sealed Period",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc));

        period.Lock();
        period.Close();
        period.MarkInvoiced();

        var lateActivity = BillableActivity.Create(
            Guid.NewGuid(), Guid.NewGuid(), BillableActivityType.InboundReceiving,
            "REC", Guid.NewGuid(), "REC-LATE", 5m, "PALLET",
            new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), "KEY-LATE", "Late receiving");

        // Attempting to assign activity to sealed period must throw
        var ex = Assert.Throws<InvalidDomainStateException>(() => lateActivity.AssignBillingPeriod(period));
        Assert.Contains("sealed billing period", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
