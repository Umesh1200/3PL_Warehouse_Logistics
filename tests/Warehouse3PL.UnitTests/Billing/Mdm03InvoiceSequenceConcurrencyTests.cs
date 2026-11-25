using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Billing;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Billing;

public sealed class Mdm03InvoiceSequenceConcurrencyTests
{
    private static async Task<(string DbName, Guid Client1Id, Guid Client2Id, Guid FacId, Guid UserId)> SeedClientsAsync()
    {
        var dbName = $"mdm03-seq-{Guid.NewGuid():N}";
        var client1Id = Guid.NewGuid();
        var client2Id = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var db = CreateDb(dbName);
        db.Clients.AddRange(
            new Client { Id = client1Id, CompanyName = "Client One", AccountCode = "M3C1", Status = ClientStatus.Active, Currency = "USD" },
            new Client { Id = client2Id, CompanyName = "Client Two", AccountCode = "M3C2", Status = ClientStatus.Active, Currency = "USD" });
        db.Facilities.Add(new Facility { Id = facId, FacilityCode = "M3F1", Name = "Hub 1", IsActive = true });
        await db.SaveChangesAsync();

        return (dbName, client1Id, client2Id, facId, userId);
    }

    private static ApplicationDbContext CreateDb(string dbName) =>
        TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

    private static async Task<Guid> SeedPeriodWithRatedActivityAsync(
        string dbName,
        Guid clientId,
        Guid facId,
        string periodCode,
        DateTime startUtc,
        DateTime endUtc,
        decimal amount = 100m)
    {
        await using var db = CreateDb(dbName);
        var period = BillingPeriod.Create(clientId, periodCode, $"Period {periodCode}", startUtc, endUtc);
        var act = BillableActivity.Create(
            clientId,
            facId,
            BillableActivityType.PickUnit,
            "PickTask",
            Guid.NewGuid(),
            "PK-" + periodCode,
            10m,
            "UNIT",
            startUtc.AddHours(1),
            "KEY-" + periodCode + "-" + Guid.NewGuid().ToString("N")[..6],
            "Rated pick",
            period.Id);
        act.Rate(amount / 10m, amount, "USD");

        db.BillingPeriods.Add(period);
        db.BillableActivities.Add(act);
        await db.SaveChangesAsync();
        return period.Id;
    }

    [Fact]
    public async Task DryRun_DoesNotCreateOrAdvanceSequence_EvenAcrossRepeatedRunsAndSubsequentSaveChanges()
    {
        var (dbName, client1Id, _, facId, userId) = await SeedClientsAsync();
        var period1Id = await SeedPeriodWithRatedActivityAsync(
            dbName, client1Id, facId, "2026-P1",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc));

        await using (var db = CreateDb(dbName))
        {
            var svc = new BillingRunService(db, NullLogger<BillingRunService>.Instance);

            var dry1 = await svc.ExecuteBillingRunAsync(new BillingRunRequest(client1Id, period1Id, userId, DryRun: true));
            var dry2 = await svc.ExecuteBillingRunAsync(new BillingRunRequest(client1Id, period1Id, userId, DryRun: true));

            // Subsequent SaveChangesAsync on same context must not leak a dry-run sequence increment
            await db.SaveChangesAsync();

            Assert.True(dry1.DryRun);
            Assert.Equal("INV-2026-M3C1-000001", dry1.InvoiceSummaries.Single().InvoiceNumber);
            Assert.Equal("INV-2026-M3C1-000001", dry2.InvoiceSummaries.Single().InvoiceNumber);
            Assert.Equal(0, await db.InvoiceSequences.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await db.Invoices.IgnoreQueryFilters().CountAsync());

            var periodAfterDry = await db.BillingPeriods.AsNoTracking().SingleAsync(p => p.Id == period1Id);
            Assert.Equal(BillingPeriodStatus.Open, periodAfterDry.Status);
        }

        // Real execution creates sequence at 1 and issues INV-2026-M3C1-000001
        await using (var db = CreateDb(dbName))
        {
            var svc = new BillingRunService(db, NullLogger<BillingRunService>.Instance);
            var live1 = await svc.ExecuteBillingRunAsync(new BillingRunRequest(client1Id, period1Id, userId, DryRun: false));
            Assert.False(live1.DryRun);
            Assert.Equal("INV-2026-M3C1-000001", live1.InvoiceSummaries.Single().InvoiceNumber);
        }

        var period2Id = await SeedPeriodWithRatedActivityAsync(
            dbName, client1Id, facId, "2026-P2",
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc));

        await using (var db = CreateDb(dbName))
        {
            var svc = new BillingRunService(db, NullLogger<BillingRunService>.Instance);
            var dryP2a = await svc.ExecuteBillingRunAsync(new BillingRunRequest(client1Id, period2Id, userId, DryRun: true));
            var dryP2b = await svc.ExecuteBillingRunAsync(new BillingRunRequest(client1Id, period2Id, userId, DryRun: true));
            await db.SaveChangesAsync();

            Assert.Equal("INV-2026-M3C1-000002", dryP2a.InvoiceSummaries.Single().InvoiceNumber);
            Assert.Equal("INV-2026-M3C1-000002", dryP2b.InvoiceSummaries.Single().InvoiceNumber);

            var seq = await db.InvoiceSequences.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(s => s.ClientId == client1Id && s.FiscalYear == 2026 && s.Prefix == "INV");
            Assert.Equal(1, seq.LastSequenceNumber);

            var liveP2 = await svc.ExecuteBillingRunAsync(new BillingRunRequest(client1Id, period2Id, userId, DryRun: false));
            Assert.Equal("INV-2026-M3C1-000002", liveP2.InvoiceSummaries.Single().InvoiceNumber);
        }
    }

    [Fact]
    public async Task Concurrent_InvAndCrnAndMultiClient_AllocationsAreSequentialAndIsolated()
    {
        var (dbName, client1Id, client2Id, facId, userId) = await SeedClientsAsync();

        var c1Periods = new Guid[4];
        var c2Periods = new Guid[2];
        for (int i = 0; i < 4; i++)
        {
            c1Periods[i] = await SeedPeriodWithRatedActivityAsync(
                dbName, client1Id, facId, $"C1-2026-{i + 1}",
                new DateTime(2026, i + 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, i + 1, 15, 0, 0, 0, DateTimeKind.Utc));
        }
        for (int i = 0; i < 2; i++)
        {
            c2Periods[i] = await SeedPeriodWithRatedActivityAsync(
                dbName, client2Id, facId, $"C2-2026-{i + 1}",
                new DateTime(2026, i + 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, i + 1, 15, 0, 0, 0, DateTimeKind.Utc));
        }

        // Issue a baseline invoice for Client1 CRN & dispute tests
        Guid c1BaseInvId;
        var dispute1Id = Guid.NewGuid();
        var dispute2Id = Guid.NewGuid();
        await using (var db = CreateDb(dbName))
        {
            var inv = Invoice.Create(client1Id, c1Periods[0], "INV-2026-M3C1-BASE", new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc));
            inv.AddLine(InvoiceLine.Create(1, InvoiceChargeCategory.Handling, "Base", 1, "EA", 500m, 500m));
            inv.Approve(userId);
            inv.Issue(userId);
            c1BaseInvId = inv.Id;
            db.Invoices.Add(inv);

            var d1 = InvoiceDispute.Create(client1Id, inv.Id, "DSP-M3-1", 50m, DisputeReasonCode.PricingError, "Rate check 1");
            d1.Id = dispute1Id;
            var d2 = InvoiceDispute.Create(client1Id, inv.Id, "DSP-M3-2", 60m, DisputeReasonCode.PricingError, "Rate check 2");
            d2.Id = dispute2Id;
            db.InvoiceDisputes.AddRange(d1, d2);
            await db.SaveChangesAsync();
        }

        // Run concurrent BillingRuns (Client1 INV x4 + Client2 INV x2) + CreditNotes + DisputeResolutions (Client1 CRN x4)
        var invTasksC1 = c1Periods.Select(pid => Task.Run(async () =>
        {
            await using var db = CreateDb(dbName);
            var svc = new BillingRunService(db, NullLogger<BillingRunService>.Instance);
            return await svc.ExecuteBillingRunAsync(new BillingRunRequest(client1Id, pid, userId));
        })).ToArray();

        var invTasksC2 = c2Periods.Select(pid => Task.Run(async () =>
        {
            await using var db = CreateDb(dbName);
            var svc = new BillingRunService(db, NullLogger<BillingRunService>.Instance);
            return await svc.ExecuteBillingRunAsync(new BillingRunRequest(client2Id, pid, userId));
        })).ToArray();

        var crnTask1 = Task.Run(async () =>
        {
            await using var db = CreateDb(dbName);
            var svc = new InvoiceService(db, NullLogger<InvoiceService>.Instance);
            return await svc.CreateCreditNoteAsync(new CreateCreditNoteRequest(
                client1Id, c1BaseInvId, "Manual adj 1",
                [new CreditNoteLineDto(null, "Adj 1", 1, 20m, 20m)],
                IssueDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        });
        var crnTask2 = Task.Run(async () =>
        {
            await using var db = CreateDb(dbName);
            var svc = new InvoiceService(db, NullLogger<InvoiceService>.Instance);
            return await svc.CreateCreditNoteAsync(new CreateCreditNoteRequest(
                client1Id, c1BaseInvId, "Manual adj 2",
                [new CreditNoteLineDto(null, "Adj 2", 1, 25m, 25m)],
                IssueDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        });
        var dspTask1 = Task.Run(async () =>
        {
            await using var db = CreateDb(dbName);
            var svc = new DisputeManagementService(db, NullLogger<DisputeManagementService>.Instance);
            await svc.ResolveDisputeAsync(new ResolveDisputeRequest(dispute1Id, userId, Accept: true, ApprovedCreditAmount: 50m, ResolutionNotes: "Approved 1"));
        });
        var dspTask2 = Task.Run(async () =>
        {
            await using var db = CreateDb(dbName);
            var svc = new DisputeManagementService(db, NullLogger<DisputeManagementService>.Instance);
            await svc.ResolveDisputeAsync(new ResolveDisputeRequest(dispute2Id, userId, Accept: true, ApprovedCreditAmount: 60m, ResolutionNotes: "Approved 2"));
        });

        await Task.WhenAll([.. invTasksC1, .. invTasksC2, crnTask1, crnTask2, dspTask1, dspTask2]);

        var c1InvNumbers = invTasksC1.SelectMany(t => t.Result.InvoiceSummaries.Select(s => s.InvoiceNumber)).OrderBy(x => x).ToArray();
        Assert.Equal(
            ["INV-2026-M3C1-000001", "INV-2026-M3C1-000002", "INV-2026-M3C1-000003", "INV-2026-M3C1-000004"],
            c1InvNumbers);

        var c2InvNumbers = invTasksC2.SelectMany(t => t.Result.InvoiceSummaries.Select(s => s.InvoiceNumber)).OrderBy(x => x).ToArray();
        Assert.Equal(["INV-2026-M3C2-000001", "INV-2026-M3C2-000002"], c2InvNumbers);

        await using (var verifyDb = CreateDb(dbName))
        {
            var crnNumbers = await verifyDb.CreditNotes.IgnoreQueryFilters()
                .Where(c => c.ClientId == client1Id)
                .Select(c => c.CreditNoteNumber)
                .OrderBy(x => x)
                .ToListAsync();

            var year = DateTime.UtcNow.Year;
            // Credit notes from manual IssueDate=2026 & ResolveDispute=UtcNow.Year
            if (year == 2026)
            {
                Assert.Equal(
                    ["CRN-2026-M3C1-000001", "CRN-2026-M3C1-000002", "CRN-2026-M3C1-000003", "CRN-2026-M3C1-000004"],
                    crnNumbers);
            }
            else
            {
                Assert.Equal(4, crnNumbers.Distinct().Count());
            }

            var c1InvSeq = await verifyDb.InvoiceSequences.IgnoreQueryFilters()
                .SingleAsync(s => s.ClientId == client1Id && s.FiscalYear == 2026 && s.Prefix == "INV");
            Assert.Equal(4, c1InvSeq.LastSequenceNumber);

            var c2InvSeq = await verifyDb.InvoiceSequences.IgnoreQueryFilters()
                .SingleAsync(s => s.ClientId == client2Id && s.FiscalYear == 2026 && s.Prefix == "INV");
            Assert.Equal(2, c2InvSeq.LastSequenceNumber);
        }
    }

    [Fact]
    public async Task Concurrent_DuplicateBillingPeriod_And_DuplicateDispute_PreventDuplicatesAndExtraSequenceAdvance()
    {
        var (dbName, client1Id, _, facId, userId) = await SeedClientsAsync();
        var periodId = await SeedPeriodWithRatedActivityAsync(
            dbName, client1Id, facId, "2026-DUP",
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            amount: 200m);

        var periodResults = await Task.WhenAll(
            AttemptAsync(async () =>
            {
                await using var db = CreateDb(dbName);
                await new BillingRunService(db, NullLogger<BillingRunService>.Instance)
                    .ExecuteBillingRunAsync(new BillingRunRequest(client1Id, periodId, userId));
            }),
            AttemptAsync(async () =>
            {
                await using var db = CreateDb(dbName);
                await new BillingRunService(db, NullLogger<BillingRunService>.Instance)
                    .ExecuteBillingRunAsync(new BillingRunRequest(client1Id, periodId, userId));
            }));

        Assert.Equal(1, periodResults.Count(r => r == null));
        Assert.Equal(1, periodResults.Count(r => r is InvalidDomainStateException));

        Guid issuedInvId;
        var disputeId = Guid.NewGuid();
        await using (var db = CreateDb(dbName))
        {
            var seq = await db.InvoiceSequences.IgnoreQueryFilters()
                .SingleAsync(s => s.ClientId == client1Id && s.FiscalYear == 2026 && s.Prefix == "INV");
            Assert.Equal(1, seq.LastSequenceNumber);

            var inv = await db.Invoices.IgnoreQueryFilters()
                .Include(i => i.Lines)
                .SingleAsync(i => i.BillingPeriodId == periodId);
            inv.Approve(userId);
            inv.Issue(userId);
            issuedInvId = inv.Id;

            var dsp = InvoiceDispute.Create(client1Id, inv.Id, "DSP-DUP-1", 40m, DisputeReasonCode.QuantityDiscrepancy, "Short pick");
            dsp.Id = disputeId;
            db.InvoiceDisputes.Add(dsp);
            await db.SaveChangesAsync();
        }

        var disputeResults = await Task.WhenAll(
            AttemptAsync(async () =>
            {
                await using var db = CreateDb(dbName);
                await new DisputeManagementService(db, NullLogger<DisputeManagementService>.Instance)
                    .ResolveDisputeAsync(new ResolveDisputeRequest(disputeId, userId, true, 40m, "OK"));
            }),
            AttemptAsync(async () =>
            {
                await using var db = CreateDb(dbName);
                await new DisputeManagementService(db, NullLogger<DisputeManagementService>.Instance)
                    .ResolveDisputeAsync(new ResolveDisputeRequest(disputeId, userId, true, 40m, "OK"));
            }));

        Assert.Equal(1, disputeResults.Count(r => r == null));
        Assert.Equal(1, disputeResults.Count(r => r is InvalidDomainStateException));

        await using (var verifyDb = CreateDb(dbName))
        {
            Assert.Equal(1, await verifyDb.CreditNotes.IgnoreQueryFilters().CountAsync(c => c.OriginalInvoiceId == issuedInvId));
            var inv = await verifyDb.Invoices.IgnoreQueryFilters().SingleAsync(i => i.Id == issuedInvId);
            Assert.Equal(40m, inv.CreditedAmount);
            Assert.Equal(160m, inv.OutstandingBalance);
        }
    }

    [Fact]
    public async Task ValidationFailure_BeforePersistence_DoesNotAdvanceSequence()
    {
        var (dbName, client1Id, _, facId, userId) = await SeedClientsAsync();
        var periodId = await SeedPeriodWithRatedActivityAsync(
            dbName, client1Id, facId, "2026-VAL",
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc));

        await using (var db = CreateDb(dbName))
        {
            var billingSvc = new BillingRunService(db, NullLogger<BillingRunService>.Instance);
            var invRes = await billingSvc.ExecuteBillingRunAsync(new BillingRunRequest(client1Id, periodId, userId));
            var invId = invRes.InvoiceSummaries.Single().InvoiceId;

            var inv = await db.Invoices.IgnoreQueryFilters()
                .Include(i => i.Lines)
                .SingleAsync(i => i.Id == invId);
            inv.Approve(userId);
            inv.Issue(userId);
            await db.SaveChangesAsync();

            var invSvc = new InvoiceService(db, NullLogger<InvoiceService>.Instance);
            // Invalid empty description throws ValidationException before consuming any CRN number
            await Assert.ThrowsAsync<ValidationException>(() =>
                invSvc.CreateCreditNoteAsync(new CreateCreditNoteRequest(
                    client1Id, invId, "Reason",
                    [new CreditNoteLineDto(null, "", 1, 10m, 10m)],
                    IssueDate: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc))));

            Assert.Equal(0, await db.InvoiceSequences.IgnoreQueryFilters()
                .CountAsync(s => s.ClientId == client1Id && s.Prefix == "CRN"));

            var crn = await invSvc.CreateCreditNoteAsync(new CreateCreditNoteRequest(
                client1Id, invId, "Valid adj",
                [new CreditNoteLineDto(null, "Valid line", 1, 10m, 10m)],
                IssueDate: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));

            Assert.Equal("CRN-2026-M3C1-000001", crn.CreditNoteNumber);
        }
    }

    private static async Task<Exception?> AttemptAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
