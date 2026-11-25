using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Services.Billing;

internal static class Mdm03PostgresCheck
{
    public static async Task RunAsync(Func<IInterceptor?, ApplicationDbContext> createDb)
    {
        var clientAId = Guid.NewGuid();
        var clientBId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var fiscalYear = DateTime.UtcNow.Year;

        var periodDryOnlyId = Guid.NewGuid();
        var clientAPeriods = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var clientBPeriods = Enumerable.Range(0, 2).Select(_ => Guid.NewGuid()).ToArray();

        static DateTime PeriodStart(int year, int monthOffset) =>
            new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(monthOffset * 20);

        await using (var seedDb = createDb(null))
        {
            seedDb.Clients.AddRange(
                new Client { Id = clientAId, CompanyName = "MDM03 PG Client A", AccountCode = "M3PA", Status = ClientStatus.Active, Currency = "USD" },
                new Client { Id = clientBId, CompanyName = "MDM03 PG Client B", AccountCode = "M3PB", Status = ClientStatus.Active, Currency = "USD" });

            seedDb.Facilities.Add(new Facility
            {
                Id = facId,
                FacilityCode = "M3PG-" + Guid.NewGuid().ToString("N")[..4],
                Name = "MDM03 PG Hub",
                AddressLine1 = "100 Ledger Way",
                City = "Chicago",
                CountryCode = "US",
                IsActive = true
            });

            void SeedPeriod(Guid pid, Guid cid, string code, int idx, decimal amount)
            {
                var start = new DateTime(fiscalYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var end = new DateTime(fiscalYear, 12, 31, 23, 59, 59, DateTimeKind.Utc);
                var p = BillingPeriod.Create(cid, code, $"Period {code}", start, end);
                p.Id = pid;
                seedDb.BillingPeriods.Add(p);

                var act = BillableActivity.Create(
                    cid, facId, BillableActivityType.PickUnit,
                    "PickTask", Guid.NewGuid(), "PK-" + code,
                    10m, "UNIT", PeriodStart(fiscalYear, idx),
                    "KEY-" + code + "-" + Guid.NewGuid().ToString("N")[..6],
                    "Rated activity " + code, pid);
                act.Rate(amount / 10m, amount, "USD");
                seedDb.BillableActivities.Add(act);
            }

            SeedPeriod(periodDryOnlyId, clientAId, "M3PA-DRY", 0, 150m);
            for (int i = 0; i < clientAPeriods.Length; i++)
            {
                SeedPeriod(clientAPeriods[i], clientAId, $"M3PA-P{i + 1}", i + 1, 100m + (i * 10m));
            }
            for (int i = 0; i < clientBPeriods.Length; i++)
            {
                SeedPeriod(clientBPeriods[i], clientBId, $"M3PB-P{i + 1}", i + 1, 250m);
            }

            await seedDb.SaveChangesAsync();
        }

        // 1. Dry-run BEFORE any InvoiceSequence exists must preview 000001 and leave 0 sequence rows
        await using (var dryDb = createDb(null))
        {
            var svc = new BillingRunService(dryDb, NullLogger<BillingRunService>.Instance);
            var dry1 = await svc.ExecuteBillingRunAsync(new BillingRunRequest(clientAId, periodDryOnlyId, staffId, DryRun: true));
            var dry2 = await svc.ExecuteBillingRunAsync(new BillingRunRequest(clientAId, periodDryOnlyId, staffId, DryRun: true));
            await dryDb.SaveChangesAsync();

            var expectedPreview = $"INV-{fiscalYear}-M3PA-000001";
            if (!dry1.DryRun ||
                dry1.InvoiceSummaries.Single().InvoiceNumber != expectedPreview ||
                dry2.InvoiceSummaries.Single().InvoiceNumber != expectedPreview ||
                await dryDb.InvoiceSequences.IgnoreQueryFilters().CountAsync(s => s.ClientId == clientAId) != 0)
            {
                throw new InvalidOperationException("MDM-0.3 PostgreSQL pre-sequence dry-run mutated state or previewed wrong number.");
            }
        }

        // 2. Concurrent first-row creation race across 6 distinct periods for Client A -> INV-YYYY-M3PA-000001..000006
        var raceTasks = clientAPeriods.Take(6).Select(pid => Task.Run(async () =>
        {
            await using var db = createDb(null);
            var svc = new BillingRunService(db, NullLogger<BillingRunService>.Instance);
            return await svc.ExecuteBillingRunAsync(new BillingRunRequest(clientAId, pid, staffId));
        })).ToArray();

        var raceResults = await Task.WhenAll(raceTasks);
        var actualInvNumbers = raceResults.SelectMany(r => r.InvoiceSummaries.Select(s => s.InvoiceNumber)).OrderBy(x => x).ToArray();
        var expectedFirstSix = Enumerable.Range(1, 6).Select(n => $"INV-{fiscalYear}-M3PA-{n:D6}").ToArray();
        if (!actualInvNumbers.SequenceEqual(expectedFirstSix))
        {
            throw new InvalidOperationException(
                $"MDM-0.3 PostgreSQL concurrent first-row INV allocation mismatch. Got: {string.Join(", ", actualInvNumbers)}");
        }

        // 3. Dry-run AFTER persisted sequence == 6 must preview 000007 and leave persisted LastSequenceNumber == 6
        await using (var dryAfterDb = createDb(null))
        {
            var svc = new BillingRunService(dryAfterDb, NullLogger<BillingRunService>.Instance);
            var dryAfter = await svc.ExecuteBillingRunAsync(new BillingRunRequest(clientAId, clientAPeriods[6], staffId, DryRun: true));
            await dryAfterDb.SaveChangesAsync();

            var seqCount = await dryAfterDb.InvoiceSequences.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(s => s.ClientId == clientAId && s.FiscalYear == fiscalYear && s.Prefix == "INV");

            if (dryAfter.InvoiceSummaries.Single().InvoiceNumber != $"INV-{fiscalYear}-M3PA-000007" ||
                seqCount.LastSequenceNumber != 6)
            {
                throw new InvalidOperationException("MDM-0.3 PostgreSQL post-sequence dry-run mutated LastSequenceNumber.");
            }
        }

        // 4. Rollback & retry atomicity on BillingRunService using ForcedRollback after SaveChangesAsync inside transaction
        await using (var rollbackDb = createDb(new ThrowAfterSaveInterceptor()))
        {
            var svc = new BillingRunService(rollbackDb, NullLogger<BillingRunService>.Instance);
            try
            {
                await svc.ExecuteBillingRunAsync(new BillingRunRequest(clientAId, clientAPeriods[6], staffId));
                throw new InvalidOperationException("Expected ForcedRollbackException did not throw.");
            }
            catch (ForcedRollbackException)
            {
                // Expected
            }
        }

        await using (var verifyRollbackDb = createDb(null))
        {
            var seq = await verifyRollbackDb.InvoiceSequences.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(s => s.ClientId == clientAId && s.FiscalYear == fiscalYear && s.Prefix == "INV");
            var period7 = await verifyRollbackDb.BillingPeriods.AsNoTracking().SingleAsync(p => p.Id == clientAPeriods[6]);
            var p7Invs = await verifyRollbackDb.Invoices.AsNoTracking().IgnoreQueryFilters()
                .CountAsync(i => i.BillingPeriodId == clientAPeriods[6]);

            if (seq.LastSequenceNumber != 6 || period7.Status != BillingPeriodStatus.Open || p7Invs != 0)
            {
                throw new InvalidOperationException(
                    $"MDM-0.3 PostgreSQL BillingRun rollback leaked state! Seq={seq.LastSequenceNumber}, Status={period7.Status}, Invs={p7Invs}");
            }

            // Clean retry must succeed cleanly without skipped numbers (allocates 000007)
            var svc = new BillingRunService(verifyRollbackDb, NullLogger<BillingRunService>.Instance);
            var retryRes = await svc.ExecuteBillingRunAsync(new BillingRunRequest(clientAId, clientAPeriods[6], staffId));
            if (retryRes.InvoiceSummaries.Single().InvoiceNumber != $"INV-{fiscalYear}-M3PA-000007")
            {
                throw new InvalidOperationException("MDM-0.3 PostgreSQL retry after rollback allocated unexpected sequence number.");
            }
        }

        // 5. Duplicate BillingPeriod concurrent execution guard -> 1x success (000008), 1x InvalidDomainStateException
        var dupPeriodTasks = new[]
        {
            AttemptAsync(async () =>
            {
                await using var db = createDb(null);
                await new BillingRunService(db, NullLogger<BillingRunService>.Instance)
                    .ExecuteBillingRunAsync(new BillingRunRequest(clientAId, clientAPeriods[7], staffId));
            }),
            AttemptAsync(async () =>
            {
                await using var db = createDb(null);
                await new BillingRunService(db, NullLogger<BillingRunService>.Instance)
                    .ExecuteBillingRunAsync(new BillingRunRequest(clientAId, clientAPeriods[7], staffId));
            })
        };
        var dupOutcomes = await Task.WhenAll(dupPeriodTasks);
        if (dupOutcomes.Count(e => e == null) != 1 || dupOutcomes.Count(e => e is InvalidDomainStateException) != 1)
        {
            throw new InvalidOperationException("MDM-0.3 PostgreSQL duplicate BillingPeriod race expected 1x success & 1x InvalidDomainStateException.");
        }

        // 6. Credit Note & Dispute Resolution CRN concurrency, duplicate dispute guard, client/prefix isolation, rollback & AR ledger parity
        Guid issuedInvId;
        var disputeDupId = Guid.NewGuid();
        var dispute2Id = Guid.NewGuid();
        var dispute3Id = Guid.NewGuid();
        await using (var setupCrnDb = createDb(null))
        {
            var inv = await setupCrnDb.Invoices.IgnoreQueryFilters()
                .Include(i => i.Lines)
                .FirstAsync(i => i.BillingPeriodId == clientAPeriods[0]);
            inv.Approve(staffId);
            inv.Issue(staffId);
            issuedInvId = inv.Id;

            var d1 = InvoiceDispute.Create(clientAId, inv.Id, "M3PG-DSP-DUP", 10m, DisputeReasonCode.PricingError, "Dup guard");
            d1.Id = disputeDupId;
            var d2 = InvoiceDispute.Create(clientAId, inv.Id, "M3PG-DSP-2", 12m, DisputeReasonCode.PricingError, "Parallel 2");
            d2.Id = dispute2Id;
            var d3 = InvoiceDispute.Create(clientAId, inv.Id, "M3PG-DSP-3", 13m, DisputeReasonCode.PricingError, "Parallel 3");
            d3.Id = dispute3Id;
            setupCrnDb.InvoiceDisputes.AddRange(d1, d2, d3);
            await setupCrnDb.SaveChangesAsync();
        }

        // Duplicate concurrent ResolveDisputeAsync on same dispute -> 1x CRN-000001, 1x InvalidDomainStateException
        var dupDisputeOutcomes = await Task.WhenAll(
            AttemptAsync(async () =>
            {
                await using var db = createDb(null);
                await new DisputeManagementService(db, NullLogger<DisputeManagementService>.Instance)
                    .ResolveDisputeAsync(new ResolveDisputeRequest(disputeDupId, staffId, true, 10m, "OK"));
            }),
            AttemptAsync(async () =>
            {
                await using var db = createDb(null);
                await new DisputeManagementService(db, NullLogger<DisputeManagementService>.Instance)
                    .ResolveDisputeAsync(new ResolveDisputeRequest(disputeDupId, staffId, true, 10m, "OK"));
            }));

        if (dupDisputeOutcomes.Count(e => e == null) != 1 || dupDisputeOutcomes.Count(e => e is InvalidDomainStateException) != 1)
        {
            throw new InvalidOperationException("MDM-0.3 PostgreSQL duplicate Dispute resolution race failed.");
        }

        // Mixed concurrent CRN allocations (2 CreateCreditNoteAsync + 2 ResolveDisputeAsync) + isolated Client B INV & CRN!
        var crnDate = new DateTime(fiscalYear, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        await Task.WhenAll(
            Task.Run(async () =>
            {
                await using var db = createDb(null);
                await new InvoiceService(db, NullLogger<InvoiceService>.Instance).CreateCreditNoteAsync(
                    new CreateCreditNoteRequest(clientAId, issuedInvId, "Manual 1", [new CreditNoteLineDto(null, "L1", 1, 5m, 5m)], IssueDate: crnDate));
            }),
            Task.Run(async () =>
            {
                await using var db = createDb(null);
                await new InvoiceService(db, NullLogger<InvoiceService>.Instance).CreateCreditNoteAsync(
                    new CreateCreditNoteRequest(clientAId, issuedInvId, "Manual 2", [new CreditNoteLineDto(null, "L2", 1, 5m, 5m)], IssueDate: crnDate));
            }),
            Task.Run(async () =>
            {
                await using var db = createDb(null);
                await new DisputeManagementService(db, NullLogger<DisputeManagementService>.Instance)
                    .ResolveDisputeAsync(new ResolveDisputeRequest(dispute2Id, staffId, true, 12m, "OK 2"));
            }),
            Task.Run(async () =>
            {
                await using var db = createDb(null);
                await new DisputeManagementService(db, NullLogger<DisputeManagementService>.Instance)
                    .ResolveDisputeAsync(new ResolveDisputeRequest(dispute3Id, staffId, true, 13m, "OK 3"));
            }),
            Task.Run(async () =>
            {
                await using var db = createDb(null);
                await new BillingRunService(db, NullLogger<BillingRunService>.Instance)
                    .ExecuteBillingRunAsync(new BillingRunRequest(clientBId, clientBPeriods[0], staffId));
            }),
            Task.Run(async () =>
            {
                await using var db = createDb(null);
                await new BillingRunService(db, NullLogger<BillingRunService>.Instance)
                    .ExecuteBillingRunAsync(new BillingRunRequest(clientBId, clientBPeriods[1], staffId));
            }));

        // CRN Rollback & retry test
        await using (var crnRbDb = createDb(new ThrowAfterSaveInterceptor()))
        {
            try
            {
                await new InvoiceService(crnRbDb, NullLogger<InvoiceService>.Instance).CreateCreditNoteAsync(
                    new CreateCreditNoteRequest(clientAId, issuedInvId, "Rollback CRN", [new CreditNoteLineDto(null, "RB", 1, 2m, 2m)], IssueDate: crnDate));
                throw new InvalidOperationException("Expected ForcedRollbackException on CreateCreditNoteAsync did not throw.");
            }
            catch (ForcedRollbackException)
            {
                // Expected
            }
        }

        await using (var finalVerifyDb = createDb(null))
        {
            var crnSeqA = await finalVerifyDb.InvoiceSequences.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(s => s.ClientId == clientAId && s.FiscalYear == fiscalYear && s.Prefix == "CRN");
            var invSeqA = await finalVerifyDb.InvoiceSequences.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(s => s.ClientId == clientAId && s.FiscalYear == fiscalYear && s.Prefix == "INV");
            var invSeqB = await finalVerifyDb.InvoiceSequences.AsNoTracking().IgnoreQueryFilters()
                .SingleAsync(s => s.ClientId == clientBId && s.FiscalYear == fiscalYear && s.Prefix == "INV");

            if (crnSeqA.LastSequenceNumber != 5 || invSeqA.LastSequenceNumber != 8 || invSeqB.LastSequenceNumber != 2)
            {
                throw new InvalidOperationException(
                    $"MDM-0.3 PostgreSQL sequence isolation/rollback mismatch: A-CRN={crnSeqA.LastSequenceNumber}, A-INV={invSeqA.LastSequenceNumber}, B-INV={invSeqB.LastSequenceNumber}");
            }

            // Clean retry after CRN rollback -> must produce CRN-YYYY-M3PA-000006
            var retryCrn = await new InvoiceService(finalVerifyDb, NullLogger<InvoiceService>.Instance).CreateCreditNoteAsync(
                new CreateCreditNoteRequest(clientAId, issuedInvId, "Retry CRN", [new CreditNoteLineDto(null, "Retry", 1, 2m, 2m)], IssueDate: crnDate));
            if (retryCrn.CreditNoteNumber != $"CRN-{fiscalYear}-M3PA-000006")
            {
                throw new InvalidOperationException($"MDM-0.3 PostgreSQL CRN retry mismatch: {retryCrn.CreditNoteNumber}");
            }

            // Preserve AR ledger behavior: record payment on remaining balance of issuedInvId (100 - 35 = 65)
            var arSvc = new AccountsReceivableService(finalVerifyDb, NullLogger<AccountsReceivableService>.Instance);
            var payment = await arSvc.RecordPaymentAsync(new RecordCustomerPaymentRequest(clientAId, "M3PG-PAY-1", 65m, PaymentMethod.Ach));
            await arSvc.AllocatePaymentAsync(payment.Id, issuedInvId, 65m);

            var refreshedInv = await finalVerifyDb.Invoices.AsNoTracking().SingleAsync(i => i.Id == issuedInvId);
            if (refreshedInv.CreditedAmount != 35m || refreshedInv.PaidAmount != 65m || refreshedInv.OutstandingBalance != 0m)
            {
                throw new InvalidOperationException(
                    $"MDM-0.3 PostgreSQL AR ledger parity check failed: Credited={refreshedInv.CreditedAmount}, Paid={refreshedInv.PaidAmount}, Bal={refreshedInv.OutstandingBalance}");
            }
        }

        Console.WriteLine("PASS PostgreSQL MDM-0.3 Invoice & CRN Sequence Concurrency, First-Row Safe Upsert, Rollback/Retry, Dry-Run & Isolation verified cleanly.");
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
