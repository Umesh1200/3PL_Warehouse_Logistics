using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Infrastructure;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.BackgroundJobs;
using Warehouse3PL.Infrastructure.Caching;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.Infrastructure.Services.Reference;

internal static class Mdm15PostgresCheck
{
    public static async Task RunAsync(string baseConnectionString)
    {
        Console.WriteLine("\n[MDM-1.5] Running Live PostgreSQL ConfigurationScopeEpoch, Outbox, Concurrent Writes & 2-Instance No-Redis Verification...");

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm15_pg_{Guid.NewGuid():N}";

        await using (var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConn.OpenAsync();
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", adminConn);
            await createCmd.ExecuteNonQueryAsync();
        }

        var targetBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = dbName };
        var targetConn = targetBuilder.ConnectionString;

        try
        {
            // 1. Clean migration MDM1_4 -> MDM1_5 + seed
            await using (var migDb = CreatePgDb(targetConn))
            {
                var migrator = migDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync("20261126070837_MDM1_4_CalendarsAndBikramSambat");
                await migrator.MigrateAsync();
                await ReferenceCatalogSeeder.SeedAsync(migDb);
            }

            // 2. Setup initial v1 MDM entities on Instance A
            var facNpId = Guid.NewGuid();
            var approverId = Guid.NewGuid();
            var leId = Guid.NewGuid();
            var effFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var evalTime = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

            await using (var initDb = CreatePgDb(targetConn))
            {
                initDb.Facilities.Add(new Facility
                {
                    Id = facNpId,
                    FacilityCode = "PG15-NP",
                    Name = "PG15 Kathmandu",
                    CountryCode = "NP",
                    StateOrProvince = "Bagmati Province",
                    Timezone = "Asia/Kathmandu",
                    IsActive = true
                });

                var p1 = CountryCompliancePolicy.CreateDraft(
                    "NP", "NP-PG15-TEST", 1, "PG15 V1", "Nepal Gazette 2083", "Notice-PG15-1",
                    """{"RequiredBusinessClassifications":["WHOLESALE"],"ExemptBusinessClassifications":["MICRO_RETAIL"]}""",
                    ComplianceEnforcementMode.MandatoryFailClosed, effFrom, null);
                p1.Approve(approverId, effFrom, "Verified V1 gazette");

                var fx1 = FxRateRecord.CreateDraft(
                    "USD", "NPR", effFrom, null, 1, 133.5m, 134.5m, 134.0m,
                    FxRateSourceAuthority.ManualApproved, "PG15-V1", effFrom, evidenceReference: "BOARD_V1");
                fx1.Approve(approverId, effFrom, "Approved V1", evidenceReference: "BOARD_V1");

                var cal1 = FacilityCalendar.Create(
                    facNpId, effFrom, null, "Saturday", new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), new TimeSpan(17, 0, 0));

                var le = LegalEntity.Create(
                    "PG15-LE", "Nepal 3PL Corp", "REG-PG15", "NP",
                    TaxIdentifierType.VAT, "609876543", "Tripureshwor 1", null, "Kathmandu", "Bagmati", "44600",
                    isCbmsMandated: false, iroSoftwarePermitRef: null, defaultCurrencyCode: "NPR", id: leId);

                initDb.CountryCompliancePolicies.Add(p1);
                initDb.FxRateRecords.Add(fx1);
                initDb.FacilityCalendars.Add(cal1);
                initDb.LegalEntities.Add(le);
                await initDb.SaveChangesAsync();
            }

            // 3. Instance B (independent ApplicationDbContext + MemoryCache + RedisCacheService; Redis notifications DISABLED)
            using var memB = new MemoryCache(new MemoryCacheOptions());
            var cacheOptsB = Options.Create(new CacheOptions { EnableRedis = false, DefaultExpirationMinutes = 30, LocalFallbackMaxTtlSeconds = 30 });
            var cacheB = new RedisCacheService(memB, cacheOptsB, NullLogger<RedisCacheService>.Instance);
            var clock = new DateTimeProvider();

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            await using (var dbB = CreatePgDb(targetConn))
            {
                var consB = new ConfigurationConsistencyService(dbB, cacheB, clock, NullLogger<ConfigurationConsistencyService>.Instance);
                var evalB = new CountryComplianceEvaluator(dbB, clock, consB);
                var fxB = new FxConversionService(dbB, new FxRateProvider(), clock, consB);
                var wcB = new WorkingCalendarService(dbB, consB);

                // Warm cache in Instance B and verify hit on 2nd call + strict tenant separation
                var diag1 = await consB.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "compliance-policies:NP-PG15-TEST",
                    (d, ct) => d.CountryCompliancePolicies.AsNoTracking().Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-TEST").ToListAsync(ct),
                    tenantA, facNpId);
                var diag2 = await consB.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "compliance-policies:NP-PG15-TEST",
                    (d, ct) => d.CountryCompliancePolicies.AsNoTracking().Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-TEST").ToListAsync(ct),
                    tenantA, facNpId);
                var diagTenantB = await consB.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "compliance-policies:NP-PG15-TEST",
                    (d, ct) => d.CountryCompliancePolicies.AsNoTracking().Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-TEST").ToListAsync(ct),
                    tenantB, facNpId);

                if (diag1.ServedFromCache || !diag2.ServedFromCache || diagTenantB.ServedFromCache ||
                    diag1.ScopedCacheKey == diagTenantB.ScopedCacheKey)
                {
                    throw new InvalidOperationException("MDM-1.5 PG cache hit / tenant-scoped key separation check failed.");
                }

                var comp1 = await evalB.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-PG15-TEST", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
                var fxRes1 = await fxB.ConvertAmountAsync(10m, "USD", "NPR", evalTime);
                var sunRes1 = await wcB.EvaluateWorkingDayAsync(facNpId, new DateOnly(2026, 11, 22));
                var le1 = await consB.GetVerifiedLegalEntityAsync(leId);

                if (comp1.Status != ComplianceApplicabilityStatus.NotRequired ||
                    fxRes1.ConvertedAmount != 1340.00m ||
                    !sunRes1.IsWorkingDay ||
                    le1!.IsCbmsMandated)
                {
                    throw new InvalidOperationException("MDM-1.5 PG initial Instance B v1 values mismatch.");
                }
            }

            // 4. Instance A commits v2 mutations on PostgreSQL (with ZERO Redis notification / memB untouched!)
            await using (var dbA = CreatePgDb(targetConn))
            {
                var p1 = await dbA.CountryCompliancePolicies.SingleAsync(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-TEST" && p.Version == 1);
                var p2 = CountryCompliancePolicy.CreateDraft(
                    "NP", "NP-PG15-TEST", 2, "PG15 V2", "Nepal Gazette 2083 Rev2", "Notice-PG15-2",
                    """{"RequiredBusinessClassifications":["WHOLESALE","MICRO_RETAIL"]}""",
                    ComplianceEnforcementMode.MandatoryFailClosed, effFrom, null);
                dbA.CountryCompliancePolicies.Add(p2);

                var fx1 = await dbA.FxRateRecords.SingleAsync(r => r.BulletinVersion == "PG15-V1");
                var fx2 = FxRateRecord.CreateDraft(
                    "USD", "NPR", effFrom, null, 1, 135.5m, 136.5m, 136.0m,
                    FxRateSourceAuthority.ManualApproved, "PG15-V2", effFrom.AddDays(1), evidenceReference: "BOARD_V2");
                dbA.FxRateRecords.Add(fx2);
                await dbA.SaveChangesAsync(); // Persist Draft targets first so self-FK SupersededBy* references real rows

                p1.Supersede(p2.Id, effFrom);
                fx1.Supersede(fx2.Id, effFrom.AddDays(1));
                var cal1 = await dbA.FacilityCalendars.SingleAsync(c => c.FacilityId == facNpId && c.IsActive);
                cal1.Deactivate();
                await dbA.SaveChangesAsync(); // Flush deactivate/supersede before activating replacements (non-deferred EXCLUDE USING gist)

                p2.Approve(approverId, effFrom.AddDays(1), "Verified V2 mandate");
                fx2.Approve(approverId, effFrom.AddDays(1), "Approved V2", evidenceReference: "BOARD_V2");

                dbA.FacilityCalendars.Add(FacilityCalendar.Create(
                    facNpId, effFrom, null, "Saturday,Sunday", new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), new TimeSpan(17, 0, 0)));

                var le = await dbA.LegalEntities.SingleAsync(l => l.Id == leId);
                le.UpdateDetails(
                    "Nepal 3PL Corp", "REG-PG15", "NP", TaxIdentifierType.VAT, "609876543",
                    "Tripureshwor 1", null, "Kathmandu", "Bagmati", "44600",
                    isCbmsMandated: true, iroSoftwarePermitRef: "PG15-IRO-99", defaultCurrencyCode: "NPR");

                await dbA.SaveChangesAsync();

                if (le.ConfigRevision < 2 || p2.Version != 2)
                {
                    throw new InvalidOperationException("MDM-1.5 PG ConfigRevision / version check failed.");
                }
            }

            // 5. Instance B (NO Redis pub/sub has run!) immediately detects PostgreSQL epoch updates on critical reads!
            await using (var dbB2 = CreatePgDb(targetConn))
            {
                var consB2 = new ConfigurationConsistencyService(dbB2, cacheB, clock, NullLogger<ConfigurationConsistencyService>.Instance);
                var evalB2 = new CountryComplianceEvaluator(dbB2, clock, consB2);
                var fxB2 = new FxConversionService(dbB2, new FxRateProvider(), clock, consB2);
                var wcB2 = new WorkingCalendarService(dbB2, consB2);

                var staleCheck = await consB2.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "compliance-policies:NP-PG15-TEST",
                    (d, ct) => d.CountryCompliancePolicies.AsNoTracking().Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-TEST").ToListAsync(ct),
                    tenantA, facNpId);
                if (!staleCheck.StaleCacheEvicted || staleCheck.ServedFromCache)
                {
                    throw new InvalidOperationException("MDM-1.5 PG Instance B did not evict stale cache on DB epoch mismatch without Redis!");
                }

                var comp2 = await evalB2.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-PG15-TEST", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
                var fxRes2 = await fxB2.ConvertAmountAsync(10m, "USD", "NPR", evalTime);
                var sunRes2 = await wcB2.EvaluateWorkingDayAsync(facNpId, new DateOnly(2026, 11, 22));
                var le2 = await consB2.GetVerifiedLegalEntityAsync(leId);

                if (comp2.Status != ComplianceApplicabilityStatus.Required || comp2.EvaluatedPolicyVersion != 2 ||
                    fxRes2.ConvertedAmount != 1360.00m ||
                    sunRes2.IsWorkingDay ||
                    !le2!.IsCbmsMandated || le2.IroSoftwarePermitRef != "PG15-IRO-99")
                {
                    throw new InvalidOperationException("MDM-1.5 PG Instance B failed to load v2 authoritative config without Redis.");
                }
            }

            // 6. Fail-closed verification when authoritative DB epoch lookup is unreachable (even with warm local cache!)
            var deadConn = new NpgsqlConnectionStringBuilder(targetConn) { Database = $"nonexistent_db_{Guid.NewGuid():N}", Timeout = 1 }.ConnectionString;
            await using (var deadDb = CreatePgDb(deadConn))
            {
                var consDead = new ConfigurationConsistencyService(deadDb, cacheB, clock, NullLogger<ConfigurationConsistencyService>.Instance);
                bool failedClosed = false;
                try
                {
                    await consDead.GetOrLoadVerifiedAsync(
                        ConfigurationScopeType.Country, "NP", "compliance-policies:NP-PG15-TEST",
                        (_, _) => Task.FromResult(new List<CountryCompliancePolicy>()),
                        tenantA, facNpId);
                }
                catch (DomainException ex) when (ex.Code == "CONFIG_EPOCH_VERIFICATION_UNAVAILABLE")
                {
                    failedClosed = true;
                }

                if (!failedClosed)
                {
                    throw new InvalidOperationException("MDM-1.5 PG failed to fail closed when DB epoch verification failed!");
                }
            }

            // 7. Transactional rollback MUST NOT increment epoch or emit outbox events
            long epochBeforeRollback;
            int outboxBeforeRollback;
            await using (var preDb = CreatePgDb(targetConn))
            {
                epochBeforeRollback = await preDb.ConfigurationScopeEpochs
                    .AsNoTracking()
                    .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "US")
                    .Select(e => e.CurrentEpoch)
                    .FirstAsync();
                outboxBeforeRollback = await preDb.ConfigurationInvalidationEvents
                    .AsNoTracking()
                    .CountAsync(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "US");

                await preDb.BeginTransactionAsync();
                preDb.HolidayCalendars.Add(HolidayCalendar.Create("US", HolidayScope.National, new DateOnly(2026, 12, 31), "Rollback Test Holiday"));
                await preDb.SaveChangesAsync();
                await preDb.RollbackTransactionAsync();
            }

            await using (var postRbDb = CreatePgDb(targetConn))
            {
                var epochAfterRb = await postRbDb.ConfigurationScopeEpochs
                    .AsNoTracking()
                    .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "US")
                    .Select(e => e.CurrentEpoch)
                    .FirstAsync();
                var outboxAfterRb = await postRbDb.ConfigurationInvalidationEvents
                    .AsNoTracking()
                    .CountAsync(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "US");

                if (epochAfterRb != epochBeforeRollback || outboxAfterRb != outboxBeforeRollback)
                {
                    throw new InvalidOperationException("MDM-1.5 PG rolled-back transaction leaked epoch increment or outbox event!");
                }
            }

            // 8. Concurrent PostgreSQL config mutations MUST serialize epoch increments with ZERO lost increments
            const int concurrentWrites = 10;
            await Task.WhenAll(Enumerable.Range(1, concurrentWrites).Select(async idx =>
            {
                await using var cDb = CreatePgDb(targetConn);
                cDb.HolidayCalendars.Add(HolidayCalendar.Create(
                    "US", HolidayScope.National, new DateOnly(2027, 1, idx), $"Concurrent US Holiday {idx}"));
                await cDb.SaveChangesAsync();
            }));

            await using (var concCheckDb = CreatePgDb(targetConn))
            {
                var finalEpoch = await concCheckDb.ConfigurationScopeEpochs
                    .AsNoTracking()
                    .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "US")
                    .Select(e => e.CurrentEpoch)
                    .FirstAsync();
                var usEpochs = await concCheckDb.ConfigurationInvalidationEvents
                    .AsNoTracking()
                    .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "US" && e.NewEpoch > epochBeforeRollback)
                    .OrderBy(e => e.NewEpoch)
                    .Select(e => e.NewEpoch)
                    .ToListAsync();

                if (finalEpoch != epochBeforeRollback + concurrentWrites ||
                    usEpochs.Count != concurrentWrites ||
                    !usEpochs.SequenceEqual(Enumerable.Range(1, concurrentWrites).Select(i => epochBeforeRollback + i)))
                {
                    throw new InvalidOperationException(
                        $"MDM-1.5 PG concurrent epoch increments lost or non-monotonic! Expected {epochBeforeRollback + concurrentWrites}, got {finalEpoch}, events={string.Join(",", usEpochs)}");
                }
            }

            // 9. Dispatcher retryable delivery + exponential backoff + retention cleanup on live PostgreSQL
            var pgClock = new PgMutableClock(DateTime.UtcNow);
            var pgTransport = new FailingThenWorkingPgTransport();
            await using (var dispDb = CreatePgDb(targetConn))
            {
                var processor = new ConfigurationInvalidationProcessor(
                    dispDb, pgTransport, pgClock, NullLogger<ConfigurationInvalidationProcessor>.Instance);

                var resFail = await processor.DispatchPendingAsync();
                if (resFail.Attempted == 0 || resFail.Failed != resFail.Attempted || resFail.Dispatched != 0)
                {
                    throw new InvalidOperationException("MDM-1.5 PG dispatcher should fail all events when transport throws.");
                }

                var stillPending = await dispDb.ConfigurationInvalidationEvents
                    .AsNoTracking()
                    .Where(e => e.DispatchedToRedisAtUtc == null)
                    .ToListAsync();
                if (stillPending.Count == 0 || stillPending.Any(e => e.DispatchAttempts < 1 || !e.NextRetryAtUtc.HasValue || string.IsNullOrEmpty(e.LastDispatchError)))
                {
                    throw new InvalidOperationException("MDM-1.5 PG failed dispatch events lost or missing backoff metadata.");
                }

                pgTransport.ShouldFail = false;
                var deferredSweep = await processor.DispatchPendingAsync();
                if (deferredSweep.Attempted != 0 || deferredSweep.DeferredByBackoff != stillPending.Count)
                {
                    throw new InvalidOperationException("MDM-1.5 PG dispatcher did not honor exponential retry backoff window.");
                }

                pgClock.UtcNow = pgClock.UtcNow.AddSeconds(10);
                var resOk = await processor.DispatchPendingAsync();
                if (resOk.Failed != 0 || resOk.Dispatched != stillPending.Count)
                {
                    throw new InvalidOperationException("MDM-1.5 PG retry sweep did not complete all pending outbox events cleanly.");
                }

                if (await dispDb.ConfigurationInvalidationEvents.AsNoTracking().AnyAsync(e => e.DispatchedToRedisAtUtc == null))
                {
                    throw new InvalidOperationException("MDM-1.5 PG undispatched events remain after clean retry sweep.");
                }

                // Retention pruning removes aged dispatched rows cleanly
                pgClock.UtcNow = pgClock.UtcNow.AddDays(10);
                var purgeSweep = await processor.DispatchPendingAsync();
                if (purgeSweep.PurgedExpired == 0)
                {
                    throw new InvalidOperationException("MDM-1.5 PG outbox retention cleanup did not purge aged dispatched events.");
                }
            }

            // 10. R01 + R02 + Mutable Scope + Rollback-on-Epoch-Failure on Live PostgreSQL
            await RunRemediationPgChecksAsync(targetConn, facNpId, approverId);

            // 11. R05 Live PostgreSQL + REAL Redis 2-Instance Integration (executed whenever PHASE73_REDIS_CONNECTION is configured)
            var redisConn = Environment.GetEnvironmentVariable("PHASE73_REDIS_CONNECTION");
            if (!string.IsNullOrWhiteSpace(redisConn))
            {
                await RunLiveRedisMultiInstanceChecksAsync(targetConn, redisConn, approverId);
                await RunLiveHostRedisResilienceChecksAsync(targetConn, redisConn, approverId);
            }

            Console.WriteLine("PASS PostgreSQL MDM-1.5 Scope Epochs, Outbox Backoff/Retry, Concurrency, R01-R05 & Real Redis Verification.");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var dropConn = new NpgsqlConnection(adminBuilder.ConnectionString);
            await dropConn.OpenAsync();
            await using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", dropConn);
            await dropCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task RunRemediationPgChecksAsync(string connStr, Guid facNpId, Guid approverId)
    {
        var clock = new DateTimeProvider();

        // R01 Effective-date-safe FX caching across multiple non-overlapping windows (reversed query order)
        var w1From = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var w2From = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var w2To = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        await using (var fxSetupDb = CreatePgDb(connStr))
        {
            var r1 = FxRateRecord.CreateDraft("EUR", "NPR", w1From, w2From, 1, 139.0m, 141.0m, 140.0m, FxRateSourceAuthority.ManualApproved, "PG-EUR-W1", w1From, evidenceReference: "BOARD_W1");
            r1.Approve(approverId, w1From, "PG R01 W1", evidenceReference: "BOARD_W1");
            var r2 = FxRateRecord.CreateDraft("EUR", "NPR", w2From, w2To, 10, 1440.0m, 1460.0m, 1450.0m, FxRateSourceAuthority.ManualApproved, "PG-EUR-W2", w2From, evidenceReference: "BOARD_W2");
            r2.Approve(approverId, w2From, "PG R01 W2", evidenceReference: "BOARD_W2");
            fxSetupDb.FxRateRecords.AddRange(r1, r2);
            await fxSetupDb.SaveChangesAsync();
        }

        using (var memFx = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }))
        {
            var cacheFx = new RedisCacheService(memFx, Options.Create(new CacheOptions()), NullLogger<RedisCacheService>.Instance);
            await using var fxDb = CreatePgDb(connStr);
            var consFx = new ConfigurationConsistencyService(fxDb, cacheFx, clock, NullLogger<ConfigurationConsistencyService>.Instance);
            var fxCached = new FxConversionService(fxDb, new FxRateProvider(), clock, consFx);
            var fxUncached = new FxConversionService(fxDb, new FxRateProvider(), clock, null);

            var cJun = await fxCached.ConvertAmountAsync(100m, "EUR", "NPR", new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), FxRateType.Sell, w2To);
            var uJun = await fxUncached.ConvertAmountAsync(100m, "EUR", "NPR", new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), FxRateType.Sell, w2To);
            var cFeb = await fxCached.ConvertAmountAsync(100m, "EUR", "NPR", new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc), FxRateType.Buy, w2To);
            var uFeb = await fxUncached.ConvertAmountAsync(100m, "EUR", "NPR", new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc), FxRateType.Buy, w2To);
            if (cJun.ConvertedAmount != uJun.ConvertedAmount || cFeb.ConvertedAmount != uFeb.ConvertedAmount ||
                cJun.ConvertedAmount != 14600.00m || cFeb.ConvertedAmount != 13900.00m)
            {
                throw new InvalidOperationException("MDM-1.5 R01 PG effective-date-safe FX cache parity failed.");
            }
        }

        // R02 Transaction-aware caching: Rollback followed by reuse of the SAME epoch number MUST NEVER leak rolled-back config
        var r02Eff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var r02Init = CreatePgDb(connStr))
        {
            var p1 = CountryCompliancePolicy.CreateDraft(
                "NP", "NP-PG15-R02", 1, "R02 Baseline", "Gazette R02", "Notice-R02-1",
                """{"ExemptBusinessClassifications":["MICRO_RETAIL"]}""",
                ComplianceEnforcementMode.MandatoryFailClosed, r02Eff, null);
            p1.Approve(approverId, r02Eff, "Verified R02 baseline");
            r02Init.CountryCompliancePolicies.Add(p1);
            await r02Init.SaveChangesAsync();
        }

        using var sharedMem = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        var sharedCache = new RedisCacheService(sharedMem, Options.Create(new CacheOptions()), NullLogger<RedisCacheService>.Instance);

        long baselineNpEpoch;
        await using (var txDb = CreatePgDb(connStr))
        {
            var consTx = new ConfigurationConsistencyService(txDb, sharedCache, clock, NullLogger<ConfigurationConsistencyService>.Instance);
            // Warm cache at committed baseline epoch E
            var baseEval = await consTx.GetOrLoadVerifiedWithDiagnosticsAsync(
                ConfigurationScopeType.Country, "NP", "r02-bundle",
                (d, ct) => d.CountryCompliancePolicies.AsNoTracking().Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-R02" && p.IsActive).Select(p => p.Title).SingleAsync(ct));
            baselineNpEpoch = baseEval.AuthoritativeEpoch;
            if (baseEval.Value != "R02 Baseline") throw new InvalidOperationException("R02 baseline setup failed.");

            await txDb.BeginTransactionAsync();
            var p1 = await txDb.CountryCompliancePolicies.SingleAsync(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-R02" && p.Version == 1);
            var pBad = CountryCompliancePolicy.CreateDraft(
                "NP", "NP-PG15-R02", 2, "ROLLED_BACK_PHANTOM", "Gazette R02", "Notice-R02-BAD",
                """{"RequiredBusinessClassifications":["MICRO_RETAIL"]}""",
                ComplianceEnforcementMode.MandatoryFailClosed, r02Eff, null);
            txDb.CountryCompliancePolicies.Add(pBad);
            await txDb.SaveChangesAsync();
            p1.Supersede(pBad.Id, r02Eff);
            await txDb.SaveChangesAsync(); // Flush supersede before activating replacement (non-deferred EXCLUDE USING gist)
            pBad.Approve(approverId, r02Eff.AddDays(1), "Uncommitted bad version");
            await txDb.SaveChangesAsync();

            // Read INSIDE active transaction: MUST see uncommitted title, MUST NOT touch sharedCache!
            var insideTxRead = await consTx.GetOrLoadVerifiedWithDiagnosticsAsync(
                ConfigurationScopeType.Country, "NP", "r02-bundle",
                (d, ct) => d.CountryCompliancePolicies.AsNoTracking().Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-R02" && p.IsActive).Select(p => p.Title).SingleAsync(ct));
            if (insideTxRead.ServedFromCache || insideTxRead.Value != "ROLLED_BACK_PHANTOM")
            {
                throw new InvalidOperationException("MDM-1.5 R02 PG in-transaction read did not bypass cache cleanly.");
            }

            await txDb.RollbackTransactionAsync();
        }

        // Now commit a NEW valid change cleanly -> PostgreSQL reuses epoch baselineNpEpoch + 1 (or next monotonic)!
        await using (var realCommitDb = CreatePgDb(connStr))
        {
            await realCommitDb.BeginTransactionAsync();
            var p1 = await realCommitDb.CountryCompliancePolicies.SingleAsync(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-R02" && p.Version == 1);
            var pGood = CountryCompliancePolicy.CreateDraft(
                "NP", "NP-PG15-R02", 2, "R02 AUTHORITATIVE V2", "Gazette R02", "Notice-R02-GOOD",
                """{"RequiredBusinessClassifications":["MICRO_RETAIL"]}""",
                ComplianceEnforcementMode.MandatoryFailClosed, r02Eff, null);
            realCommitDb.CountryCompliancePolicies.Add(pGood);
            await realCommitDb.SaveChangesAsync();
            p1.Supersede(pGood.Id, r02Eff);
            await realCommitDb.SaveChangesAsync(); // Flush supersede before activating replacement (non-deferred EXCLUDE USING gist)
            pGood.Approve(approverId, r02Eff.AddDays(1), "Committed real V2");
            await realCommitDb.SaveChangesAsync();
            await realCommitDb.CommitTransactionAsync();
        }

        await using (var verifyDb = CreatePgDb(connStr))
        {
            var consVerify = new ConfigurationConsistencyService(verifyDb, sharedCache, clock, NullLogger<ConfigurationConsistencyService>.Instance);
            var finalRead = await consVerify.GetOrLoadVerifiedWithDiagnosticsAsync(
                ConfigurationScopeType.Country, "NP", "r02-bundle",
                (d, ct) => d.CountryCompliancePolicies.AsNoTracking().Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-R02" && p.IsActive).Select(p => p.Title).SingleAsync(ct));
            if (finalRead.Value != "R02 AUTHORITATIVE V2" || finalRead.AuthoritativeEpoch <= baselineNpEpoch)
            {
                throw new InvalidOperationException($"MDM-1.5 R02 PG exposed stale/rolled-back value '{finalRead.Value}' after rollback+reuse!");
            }
        }

        // Mutable scope change (NP -> US on HolidayCalendar) & Transaction rollback when epoch/outbox fails
        await using (var scopeMoveDb = CreatePgDb(connStr))
        {
            var hol = HolidayCalendar.Create("NP", HolidayScope.National, new DateOnly(2029, 4, 5), "PG Moveable Holiday");
            scopeMoveDb.HolidayCalendars.Add(hol);
            await scopeMoveDb.SaveChangesAsync();

            long npBefore = await scopeMoveDb.ConfigurationScopeEpochs.AsNoTracking().Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "NP").Select(e => e.CurrentEpoch).FirstAsync();
            long usBefore = await scopeMoveDb.ConfigurationScopeEpochs.AsNoTracking().Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "US").Select(e => e.CurrentEpoch).FirstAsync();

            hol.CountryCode = "US";
            await scopeMoveDb.SaveChangesAsync();

            long npAfter = await scopeMoveDb.ConfigurationScopeEpochs.AsNoTracking().Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "NP").Select(e => e.CurrentEpoch).FirstAsync();
            long usAfter = await scopeMoveDb.ConfigurationScopeEpochs.AsNoTracking().Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "US").Select(e => e.CurrentEpoch).FirstAsync();
            if (npAfter != npBefore + 1 || usAfter != usBefore + 1)
            {
                throw new InvalidOperationException("MDM-1.5 PG mutable scope move did not increment BOTH original and destination scope epochs.");
            }
        }

        // Additional Review: Missing-schema fail-closed check when MDM1_5 migration is recorded in __EFMigrationsHistory
        await using (var schemaFailDb = CreatePgDb(connStr))
        {
            await schemaFailDb.BeginTransactionAsync();
            await schemaFailDb.Database.ExecuteSqlRawAsync("DROP TABLE configuration_invalidation_events; DROP TABLE configuration_scope_epochs;");
            schemaFailDb.HolidayCalendars.Add(HolidayCalendar.Create("NP", HolidayScope.National, new DateOnly(2029, 5, 6), "Missing Schema Should Fail"));
            bool missingSchemaBlocked = false;
            try
            {
                await schemaFailDb.SaveChangesAsync();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("failing closed", StringComparison.OrdinalIgnoreCase))
            {
                missingSchemaBlocked = true;
            }
            await schemaFailDb.RollbackTransactionAsync();
            if (!missingSchemaBlocked)
            {
                throw new InvalidOperationException("MDM-1.5 PG silently bypassed missing configuration_scope_epochs schema despite recorded MDM1_5 migration!");
            }
        }

        // Additional Hardening: Partial MDM-1.5 schema (epochs present, outbox missing) MUST fail closed
        await using (var partialFailDb = CreatePgDb(connStr))
        {
            await partialFailDb.BeginTransactionAsync();
            await partialFailDb.Database.ExecuteSqlRawAsync("DROP TABLE configuration_invalidation_events;");
            partialFailDb.HolidayCalendars.Add(HolidayCalendar.Create("NP", HolidayScope.National, new DateOnly(2029, 5, 16), "Partial Schema Should Fail"));
            bool partialBlocked = false;
            try
            {
                await partialFailDb.SaveChangesAsync();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("failing closed", StringComparison.OrdinalIgnoreCase))
            {
                partialBlocked = true;
            }
            await partialFailDb.RollbackTransactionAsync();
            if (!partialBlocked)
            {
                throw new InvalidOperationException("MDM-1.5 PG silently bypassed partial MDM-1.5 schema when configuration_invalidation_events was dropped!");
            }
        }

        // Additional Hardening: Caller-owned transaction failure after SaveChangesAsync MUST roll back holiday, epoch, and outbox atomically without polluting shared cache
        using (var callerMem = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }))
        {
            var callerCache = new RedisCacheService(callerMem, Options.Create(new CacheOptions()), NullLogger<RedisCacheService>.Instance);
            long npEpochBaseline;
            int outboxCountBaseline;
            int warmHolidayCount;

            await using (var baselineDb = CreatePgDb(connStr))
            {
                var consBaseline = new ConfigurationConsistencyService(baselineDb, callerCache, clock, NullLogger<ConfigurationConsistencyService>.Instance);
                var warmed = await consBaseline.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "caller-tx-holidays",
                    (d, ct) => d.HolidayCalendars.AsNoTracking().CountAsync(h => h.CountryCode == "NP", ct));
                warmHolidayCount = warmed.Value;
                npEpochBaseline = await baselineDb.ConfigurationScopeEpochs.AsNoTracking()
                    .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "NP")
                    .Select(e => e.CurrentEpoch).FirstAsync();
                outboxCountBaseline = await baselineDb.ConfigurationInvalidationEvents.AsNoTracking().CountAsync();
            }

            await using (var callerTxDb = CreatePgDb(connStr))
            {
                await callerTxDb.BeginTransactionAsync();
                callerTxDb.HolidayCalendars.Add(HolidayCalendar.Create("NP", HolidayScope.National, new DateOnly(2029, 5, 25), "Caller Abort Should Rollback"));
                await callerTxDb.SaveChangesAsync();

                // Verify inside caller tx we observe uncommitted holiday (+1) via cache bypass before caller fails
                var consInsideCallerTx = new ConfigurationConsistencyService(callerTxDb, callerCache, clock, NullLogger<ConfigurationConsistencyService>.Instance);
                var insideCallerRead = await consInsideCallerTx.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "caller-tx-holidays",
                    (d, ct) => d.HolidayCalendars.AsNoTracking().CountAsync(h => h.CountryCode == "NP", ct));
                if (insideCallerRead.ServedFromCache || insideCallerRead.Value != warmHolidayCount + 1)
                {
                    throw new InvalidOperationException("MDM-1.5 PG caller-owned tx did not reflect uncommitted write via tx-local bypass.");
                }

                // Simulated caller domain error after SaveChangesAsync triggers caller rollback
                await callerTxDb.RollbackTransactionAsync();
            }

            await using (var verifyRollbackDb = CreatePgDb(connStr))
            {
                long npEpochAfter = await verifyRollbackDb.ConfigurationScopeEpochs.AsNoTracking()
                    .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "NP")
                    .Select(e => e.CurrentEpoch).FirstAsync();
                int outboxAfter = await verifyRollbackDb.ConfigurationInvalidationEvents.AsNoTracking().CountAsync();
                bool leakedCallerRow = await verifyRollbackDb.HolidayCalendars.AsNoTracking()
                    .AnyAsync(h => h.HolidayDate == new DateOnly(2029, 5, 25) && h.Name == "Caller Abort Should Rollback");

                var consPostRollback = new ConfigurationConsistencyService(verifyRollbackDb, callerCache, clock, NullLogger<ConfigurationConsistencyService>.Instance);
                var cachedPostRollback = await consPostRollback.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "caller-tx-holidays",
                    (d, ct) => d.HolidayCalendars.AsNoTracking().CountAsync(h => h.CountryCode == "NP", ct));

                if (npEpochAfter != npEpochBaseline || outboxAfter != outboxCountBaseline || leakedCallerRow ||
                    !cachedPostRollback.ServedFromCache || cachedPostRollback.Value != warmHolidayCount)
                {
                    throw new InvalidOperationException("MDM-1.5 PG caller-owned transaction rollback leaked PostgreSQL state or polluted shared L1 cache!");
                }
            }
        }

        // Additional Review: When outbox persistence fails inside SaveChangesAsync's own transaction, primary MDM entity & epoch rollback atomically
        long npBeforeOutboxFail;
        await using (var outboxFailSetup = CreatePgDb(connStr))
        {
            npBeforeOutboxFail = await outboxFailSetup.ConfigurationScopeEpochs.AsNoTracking()
                .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "NP")
                .Select(e => e.CurrentEpoch).FirstAsync();

            await outboxFailSetup.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION mdm15_fail_outbox_once() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'SIMULATED_OUTBOX_PERSISTENCE_FAILURE';
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_mdm15_fail_outbox BEFORE INSERT ON configuration_invalidation_events
                FOR EACH ROW EXECUTE FUNCTION mdm15_fail_outbox_once();
                """);
        }

        try
        {
            await using var failingWriteDb = CreatePgDb(connStr);
            failingWriteDb.HolidayCalendars.Add(HolidayCalendar.Create("NP", HolidayScope.National, new DateOnly(2029, 6, 7), "Should Rollback With Outbox"));
            bool threw = false;
            try
            {
                await failingWriteDb.SaveChangesAsync();
            }
            catch (PostgresException pgEx) when (pgEx.MessageText.Contains("SIMULATED_OUTBOX_PERSISTENCE_FAILURE", StringComparison.Ordinal))
            {
                threw = true;
            }

            if (!threw)
            {
                throw new InvalidOperationException("MDM-1.5 PG simulated outbox failure trigger did not fire.");
            }
        }
        finally
        {
            await using var cleanupDb = CreatePgDb(connStr);
            await cleanupDb.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS trg_mdm15_fail_outbox ON configuration_invalidation_events; DROP FUNCTION IF EXISTS mdm15_fail_outbox_once();");
        }

        await using (var postFailCheck = CreatePgDb(connStr))
        {
            long npAfterOutboxFail = await postFailCheck.ConfigurationScopeEpochs.AsNoTracking()
                .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "NP")
                .Select(e => e.CurrentEpoch).FirstAsync();
            bool leakedHoliday = await postFailCheck.HolidayCalendars.AsNoTracking()
                .AnyAsync(h => h.HolidayDate == new DateOnly(2029, 6, 7) && h.Name == "Should Rollback With Outbox");

            if (npAfterOutboxFail != npBeforeOutboxFail || leakedHoliday)
            {
                throw new InvalidOperationException("MDM-1.5 PG primary HolidayCalendar write or epoch increment leaked after outbox persistence failure!");
            }
        }
    }

    private static async Task RunLiveRedisMultiInstanceChecksAsync(string pgConn, string redisConn, Guid approverId)
    {
        Console.WriteLine("[MDM-1.5-R05] Running Live PostgreSQL + Live Redis 2-Container Pub/Sub & Outage Recovery Verification...");
        var instancePrefix = $"Mdm15Live:{Guid.NewGuid():N}:";
        var clock = new PgMutableClock(DateTime.UtcNow);
        var eff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var evalTime = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        await using (var seedDb = CreatePgDb(pgConn))
        {
            var p1 = CountryCompliancePolicy.CreateDraft(
                "NP", "NP-REDIS-LIVE", 1, "Redis Live V1", "Gazette RL1", "Notice-RL-1",
                """{"ExemptBusinessClassifications":["MICRO_RETAIL"]}""",
                ComplianceEnforcementMode.MandatoryFailClosed, eff, null);
            p1.Approve(approverId, eff, "Approved RL1");
            seedDb.CountryCompliancePolicies.Add(p1);
            await seedDb.SaveChangesAsync();
        }

        // Build two completely independent Redis + L1 + Subscriber service containers
        var muxConfig = ConfigurationOptions.Parse(redisConn, ignoreUnknown: true);
        muxConfig.AbortOnConnectFail = false;

        using var muxA = await ConnectionMultiplexer.ConnectAsync(muxConfig);
        using var muxB = await ConnectionMultiplexer.ConnectAsync(muxConfig);

        var optsA = Options.Create(new CacheOptions { EnableRedis = true, ConnectionString = redisConn, InstanceName = instancePrefix, DefaultExpirationMinutes = 30, LocalFallbackMaxTtlSeconds = 30 });
        var optsB = Options.Create(new CacheOptions { EnableRedis = true, ConnectionString = redisConn, InstanceName = instancePrefix, DefaultExpirationMinutes = 30, LocalFallbackMaxTtlSeconds = 30 });

        var distA = new RedisCache(new RedisCacheOptions { Configuration = redisConn, InstanceName = instancePrefix });
        var distB = new RedisCache(new RedisCacheOptions { Configuration = redisConn, InstanceName = instancePrefix });

        using var memA = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        using var memB = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });

        var cacheA = new RedisCacheService(memA, optsA, NullLogger<RedisCacheService>.Instance, distA);
        var cacheB = new RedisCacheService(memB, optsB, NullLogger<RedisCacheService>.Instance, distB);

        // Drain pending outbox events emitted by prior setup/remediation phases so Instance B only observes V2 invalidation here
        await using (var preDrainDb = CreatePgDb(pgConn))
        {
            var preDrainTransport = new RedisConfigurationInvalidationTransport(cacheA, optsA, NullLogger<RedisConfigurationInvalidationTransport>.Instance, muxA);
            var preDrainProc = new ConfigurationInvalidationProcessor(preDrainDb, preDrainTransport, clock, NullLogger<ConfigurationInvalidationProcessor>.Instance);
            await preDrainProc.DispatchPendingAsync();
        }

        var subB = new RedisConfigurationInvalidationSubscriber(cacheB, optsB, NullLogger<RedisConfigurationInvalidationSubscriber>.Instance, muxB);
        await subB.StartAsync(CancellationToken.None);

        try
        {
            // Wait deterministically for Redis pub/sub subscription registration
            for (int i = 0; i < 40 && !subB.IsSubscribed; i++)
            {
                await Task.Delay(50);
            }
            if (!subB.IsSubscribed)
            {
                throw new InvalidOperationException("MDM-1.5 R05 Instance B subscriber did not establish initial Redis subscription.");
            }

            // 1. Instance B warms targeted Country:NP L1 + Redis entries AND unrelated Country:US entry at V1
            string targetedPolicyFullKey;
            string targetedBundleFullKey;
            string unrelatedFullKey;
            await using (var dbB1 = CreatePgDb(pgConn))
            {
                var consB1 = new ConfigurationConsistencyService(dbB1, cacheB, clock, NullLogger<ConfigurationConsistencyService>.Instance);
                var diagPolicy = await consB1.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "compliance-policies:NP-REDIS-LIVE",
                    (d, ct) => d.CountryCompliancePolicies.AsNoTracking()
                        .Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-REDIS-LIVE")
                        .OrderBy(p => p.Version)
                        .ToListAsync(ct));
                var diagBundle = await consB1.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "r05-np-targeted",
                    (d, ct) => d.CountryCompliancePolicies.AsNoTracking()
                        .Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-REDIS-LIVE")
                        .Select(p => p.Title)
                        .FirstAsync(ct));
                var diagUs = await consB1.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "US", "r05-us-unrelated",
                    (d, ct) => d.HolidayCalendars.AsNoTracking()
                        .CountAsync(h => h.CountryCode == "US", ct));

                targetedPolicyFullKey = $"{instancePrefix}{diagPolicy.ScopedCacheKey}";
                targetedBundleFullKey = $"{instancePrefix}{diagBundle.ScopedCacheKey}";
                unrelatedFullKey = $"{instancePrefix}{diagUs.ScopedCacheKey}";

                var evalB1 = new CountryComplianceEvaluator(dbB1, clock, consB1);
                var r1 = await evalB1.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-REDIS-LIVE", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
                if (r1.Status != ComplianceApplicabilityStatus.NotRequired ||
                    !memB.TryGetValue(targetedPolicyFullKey, out _) ||
                    !memB.TryGetValue(targetedBundleFullKey, out _) ||
                    !memB.TryGetValue(unrelatedFullKey, out _))
                {
                    throw new InvalidOperationException("MDM-1.5 R05 Instance B failed to warm targeted NP and unrelated US L1+Redis entries at V1.");
                }
            }

            // 2. Instance A updates policy to V2 and runs dispatcher against REAL Redis
            await using (var dbA = CreatePgDb(pgConn))
            {
                await dbA.BeginTransactionAsync();
                var p1 = await dbA.CountryCompliancePolicies.SingleAsync(p => p.CountryCode == "NP" && p.PolicyCode == "NP-REDIS-LIVE" && p.Version == 1);
                var p2 = CountryCompliancePolicy.CreateDraft(
                    "NP", "NP-REDIS-LIVE", 2, "Redis Live V2", "Gazette RL2", "Notice-RL-2",
                    """{"RequiredBusinessClassifications":["MICRO_RETAIL"]}""",
                    ComplianceEnforcementMode.MandatoryFailClosed, eff, null);
                dbA.CountryCompliancePolicies.Add(p2);
                await dbA.SaveChangesAsync();
                p1.Supersede(p2.Id, eff);
                await dbA.SaveChangesAsync(); // Flush supersede before activating replacement (non-deferred EXCLUDE USING gist)
                p2.Approve(approverId, eff.AddDays(1), "Approved RL2");
                await dbA.SaveChangesAsync();
                await dbA.CommitTransactionAsync();

                var transportA = new RedisConfigurationInvalidationTransport(cacheA, optsA, NullLogger<RedisConfigurationInvalidationTransport>.Instance, muxA);
                var procA = new ConfigurationInvalidationProcessor(dbA, transportA, clock, NullLogger<ConfigurationInvalidationProcessor>.Instance);
                var sweep = await procA.DispatchPendingAsync();
                if (sweep.Failed != 0 || sweep.Dispatched == 0)
                {
                    throw new InvalidOperationException("MDM-1.5 R05 Redis pub/sub dispatch failed on live Redis.");
                }
            }

            // 3. Verify Instance B's live Redis subscriber received notification AND actually evicted targeted NP L1 keys while keeping unrelated US L1 key intact
            for (int i = 0; i < 40 &&
                 (subB.ReceivedNotificationCount == 0 ||
                  memB.TryGetValue(targetedPolicyFullKey, out _) ||
                  memB.TryGetValue(targetedBundleFullKey, out _)); i++)
            {
                await Task.Delay(100);
            }

            if (subB.ReceivedNotificationCount == 0)
            {
                throw new InvalidOperationException($"MDM-1.5 R05 receiving instance subscriber did not receive real Redis notification (count={subB.ReceivedNotificationCount}).");
            }

            if (memB.TryGetValue(targetedPolicyFullKey, out _) || memB.TryGetValue(targetedBundleFullKey, out _))
            {
                throw new InvalidOperationException($"MDM-1.5 R05-F01-B FAIL: receiving instance RETAINED targeted NP L1 cache entry after bounded notification window (TrackedLocalKeyCount={cacheB.TrackedLocalKeyCount}, memB.Count={memB.Count}).");
            }

            if (!memB.TryGetValue(unrelatedFullKey, out _))
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01-B FAIL: unrelated Country:US L1 cache entry was unexpectedly evicted by Country:NP invalidation!");
            }

            // Send duplicate notification cleanly (idempotent)
            await muxA.GetSubscriber().PublishAsync(
                RedisChannel.Literal(RedisConfigurationInvalidationSubscriber.ChannelName),
                """{"Id":9999,"ScopeType":"Country","ScopeKey":"NP","NewEpoch":99,"EntityType":"CountryCompliancePolicy","EntityId":"dup"}""");
            await Task.Delay(200);

            // Instance B evaluates V2 cleanly
            await using (var dbB2 = CreatePgDb(pgConn))
            {
                var consB2 = new ConfigurationConsistencyService(dbB2, cacheB, clock, NullLogger<ConfigurationConsistencyService>.Instance);
                var evalB2 = new CountryComplianceEvaluator(dbB2, clock, consB2);
                var r2 = await evalB2.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-REDIS-LIVE", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
                if (r2.Status != ComplianceApplicabilityStatus.Required || r2.EvaluatedPolicyVersion != 2)
                {
                    throw new InvalidOperationException("MDM-1.5 R05 Instance B did not observe V2 after live Redis invalidation.");
                }
            }

            // 4. Simulate Redis Outage on dispatcher -> retained pending event + authoritative DB read STILL works -> recovery retry succeeds!
            var deadMuxOpts = ConfigurationOptions.Parse("127.0.0.1:6398,abortConnect=false,connectTimeout=200");
            using var deadMux = await ConnectionMultiplexer.ConnectAsync(deadMuxOpts);
            var deadCacheOpts = Options.Create(new CacheOptions { EnableRedis = true, ConnectionString = "127.0.0.1:6398,abortConnect=false", InstanceName = instancePrefix });

            await using (var outageDb = CreatePgDb(pgConn))
            {
                await outageDb.BeginTransactionAsync();
                var p2 = await outageDb.CountryCompliancePolicies.SingleAsync(p => p.CountryCode == "NP" && p.PolicyCode == "NP-REDIS-LIVE" && p.Version == 2);
                var p3 = CountryCompliancePolicy.CreateDraft(
                    "NP", "NP-REDIS-LIVE", 3, "Redis Live V3 Outage", "Gazette RL3", "Notice-RL-3",
                    """{"ExemptBusinessClassifications":["MICRO_RETAIL"]}""",
                    ComplianceEnforcementMode.MandatoryFailClosed, eff, null);
                outageDb.CountryCompliancePolicies.Add(p3);
                await outageDb.SaveChangesAsync();
                p2.Supersede(p3.Id, eff);
                await outageDb.SaveChangesAsync(); // Flush supersede before activating replacement (non-deferred EXCLUDE USING gist)
                p3.Approve(approverId, eff.AddDays(2), "Approved RL3 during Redis outage");
                await outageDb.SaveChangesAsync();
                await outageDb.CommitTransactionAsync();

                var brokenTransport = new RedisConfigurationInvalidationTransport(cacheA, deadCacheOpts, NullLogger<RedisConfigurationInvalidationTransport>.Instance, deadMux);
                var outageProc = new ConfigurationInvalidationProcessor(outageDb, brokenTransport, clock, NullLogger<ConfigurationInvalidationProcessor>.Instance);
                var outSweep = await outageProc.DispatchPendingAsync();
                if (outSweep.Failed == 0 || outSweep.Dispatched != 0)
                {
                    throw new InvalidOperationException("MDM-1.5 R05 outage dispatcher should retain failed events without false completion.");
                }

                // Authoritative DB consistency MUST STILL observe V3 immediately despite Redis outage!
                var consBOutage = new ConfigurationConsistencyService(outageDb, cacheB, clock, NullLogger<ConfigurationConsistencyService>.Instance);
                var evalOutage = new CountryComplianceEvaluator(outageDb, clock, consBOutage);
                var r3 = await evalOutage.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-REDIS-LIVE", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
                if (r3.Status != ComplianceApplicabilityStatus.NotRequired || r3.EvaluatedPolicyVersion != 3)
                {
                    throw new InvalidOperationException("MDM-1.5 R05 DB-authoritative consistency failed during Redis outage.");
                }

                // Recovery: advance clock past backoff & switch back to healthy Redis transport -> retries and completes!
                clock.UtcNow = clock.UtcNow.AddSeconds(10);
                var recoveredTransport = new RedisConfigurationInvalidationTransport(cacheA, optsA, NullLogger<RedisConfigurationInvalidationTransport>.Instance, muxA);
                var recoverProc = new ConfigurationInvalidationProcessor(outageDb, recoveredTransport, clock, NullLogger<ConfigurationInvalidationProcessor>.Instance);
                var recSweep = await recoverProc.DispatchPendingAsync();
                if (recSweep.Failed != 0 || recSweep.Dispatched == 0)
                {
                    throw new InvalidOperationException("MDM-1.5 R05 Redis recovery retry sweep failed.");
                }
            }

            Console.WriteLine("PASS Live PostgreSQL + Real Redis 7 Two-Instance Pub/Sub, Duplicate Handling & Outage Recovery Verified.");
        }
        finally
        {
            await subB.StopAsync(CancellationToken.None);
        }
    }

    private static async Task RunLiveHostRedisResilienceChecksAsync(string pgConn, string redisConn, Guid approverId)
    {
        Console.WriteLine("[MDM-1.5-R05-F01] Running Production Generic Host Offline Startup -> Late Redis -> Disconnect -> Reconnect Flush -> Outbox Backoff Recovery Checks...");

        var parsedRedis = ConfigurationOptions.Parse(redisConn, ignoreUnknown: true);
        var (upstreamHost, upstreamPort) = ExtractRedisEndpoint(parsedRedis);

        await using var proxy = new RedisTcpProxy(upstreamHost, upstreamPort);
        var hostInstancePrefix = $"Mdm15HostLive:{Guid.NewGuid():N}:";
        var eff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var evalTime = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        var testClock = new PgMutableClock(DateTime.UtcNow);

        await using (var seedDb = CreatePgDb(pgConn))
        {
            var p1 = CountryCompliancePolicy.CreateDraft(
                "NP", "NP-HOST-LIVE", 1, "Host Live V1", "Gazette HL1", "Notice-HL-1",
                """{"ExemptBusinessClassifications":["MICRO_RETAIL"]}""",
                ComplianceEnforcementMode.MandatoryFailClosed, eff, null);
            p1.Approve(approverId, eff, "Approved HL1");
            seedDb.CountryCompliancePolicies.Add(p1);
            await seedDb.SaveChangesAsync();

            // Drain seed setup outbox row before proxy outage/host checks so V2 dispatch emits a single clean notification
            using var seedMux = await ConnectionMultiplexer.ConnectAsync(parsedRedis);
            using var seedMem = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
            var seedOpts = Options.Create(new CacheOptions { EnableRedis = true, ConnectionString = redisConn, InstanceName = hostInstancePrefix });
            var seedTransport = new RedisConfigurationInvalidationTransport(
                new RedisCacheService(seedMem, seedOpts, NullLogger<RedisCacheService>.Instance),
                seedOpts,
                NullLogger<RedisConfigurationInvalidationTransport>.Instance,
                seedMux);
            var seedProc = new ConfigurationInvalidationProcessor(seedDb, seedTransport, testClock, NullLogger<ConfigurationInvalidationProcessor>.Instance);
            await seedProc.DispatchPendingAsync();
        }

        // 1. Build REAL Production Generic Host pointing to proxy.Port BEFORE proxy starts listening (Redis unreachable at startup!)
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = pgConn,
            ["Redis:EnableRedis"] = "true",
            ["Redis:ConnectionString"] = $"127.0.0.1:{proxy.Port},abortConnect=false,connectTimeout=500,syncTimeout=1000",
            ["Redis:InstanceName"] = hostInstancePrefix,
            ["Redis:DefaultExpirationMinutes"] = "30",
            ["Redis:LocalFallbackMaxTtlSeconds"] = "30",
            ["ConfigurationConsistency:EnableDispatcher"] = "false",
            ["ObjectStorage:SigningSecretKey"] = "ProductionGradeTestSigningSecretKeyForMdm15Verification2026!"
        });
        builder.Services.AddApplicationServices();
        builder.Services.AddInfrastructureServices(builder.Configuration);
        builder.Services.AddSingleton<IDateTimeProvider>(testClock);
        builder.Services.AddScoped<ICurrentUserContext>(_ => new PgAdminUserContext(approverId));
        builder.Services.AddScoped<ITenantProvider>(_ => new PgBypassTenantProvider());

        using var host = builder.Build();
        await host.StartAsync();

        var mux = host.Services.GetRequiredService<IConnectionMultiplexer>();
        var sub = host.Services.GetRequiredService<RedisConfigurationInvalidationSubscriber>();
        var hostCache = host.Services.GetRequiredService<ICacheService>();
        var hostMem = host.Services.GetRequiredService<IMemoryCache>();
        var hostTransport = host.Services.GetRequiredService<IConfigurationInvalidationTransport>();

        try
        {
            if (mux.IsConnected || sub.IsSubscribed)
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: multiplexer/subscriber reported connected when proxy was closed at startup.");
            }

            // 2. Successful PostgreSQL-authoritative reads during Redis unavailability at startup
            string offlineNpKey;
            string offlineUsKey;
            await using (var offlineScope = host.Services.CreateAsyncScope())
            {
                var consOffline = offlineScope.ServiceProvider.GetRequiredService<IConfigurationConsistencyService>();
                var evalOffline = offlineScope.ServiceProvider.GetRequiredService<ICountryComplianceEvaluator>();

                var offlinePolicyDiag = await consOffline.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "host-np-offline",
                    (d, ct) => d.CountryCompliancePolicies.AsNoTracking()
                        .Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-HOST-LIVE" && p.IsActive)
                        .Select(p => p.Version).FirstAsync(ct));
                var offlineUsDiag = await consOffline.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "US", "host-us-keep",
                    (d, ct) => d.HolidayCalendars.AsNoTracking().CountAsync(h => h.CountryCode == "US", ct));

                offlineNpKey = $"{hostInstancePrefix}{offlinePolicyDiag.ScopedCacheKey}";
                offlineUsKey = $"{hostInstancePrefix}{offlineUsDiag.ScopedCacheKey}";

                var r1 = await evalOffline.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-HOST-LIVE", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
                if (r1.Status != ComplianceApplicabilityStatus.NotRequired || r1.EvaluatedPolicyVersion != 1 ||
                    !hostMem.TryGetValue(offlineNpKey, out _))
                {
                    throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: PostgreSQL-authoritative reads did not succeed while Redis was unavailable at startup.");
                }
            }

            // 3. Redis becomes available AFTER application startup on SAME host/mux/subscriber instances
            proxy.StartListening();
            for (int i = 0; i < 250 && (!mux.IsConnected || !sub.IsSubscribed || sub.ReconnectFlushCount < 1); i++)
            {
                await Task.Delay(100);
            }

            if (!mux.IsConnected || !sub.IsSubscribed || sub.ReconnectFlushCount < 1)
            {
                throw new InvalidOperationException($"MDM-1.5 R05-F01 FAIL: subscriber did not recover after Redis came online (connected={mux.IsConnected}, subscribed={sub.IsSubscribed}, flushes={sub.ReconnectFlushCount}).");
            }

            if (hostMem.TryGetValue(offlineNpKey, out _) || hostMem.TryGetValue(offlineUsKey, out _))
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: post-startup recovery flush did not clear offline-era local MDM cache entries.");
            }

            // 4. Subscriber receives live invalidations after late startup connection & evicts only targeted scope
            string npV1FullKey;
            string usKeepFullKey;
            await using (var warmScope1 = host.Services.CreateAsyncScope())
            {
                var cons1 = warmScope1.ServiceProvider.GetRequiredService<IConfigurationConsistencyService>();
                var npDiag = await cons1.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "compliance-policies:NP-HOST-LIVE",
                    (d, ct) => d.CountryCompliancePolicies.AsNoTracking()
                        .Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-HOST-LIVE").ToListAsync(ct));
                var usDiag = await cons1.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "US", "host-us-unrelated",
                    (d, ct) => d.HolidayCalendars.AsNoTracking().CountAsync(h => h.CountryCode == "US", ct));
                npV1FullKey = $"{hostInstancePrefix}{npDiag.ScopedCacheKey}";
                usKeepFullKey = $"{hostInstancePrefix}{usDiag.ScopedCacheKey}";
            }

            long notifBeforeV2 = sub.ReceivedNotificationCount;
            int expectedV2Dispatched;
            await using (var dbV2 = CreatePgDb(pgConn))
            {
                await dbV2.BeginTransactionAsync();
                var p1 = await dbV2.CountryCompliancePolicies.SingleAsync(p => p.CountryCode == "NP" && p.PolicyCode == "NP-HOST-LIVE" && p.Version == 1);
                var p2 = CountryCompliancePolicy.CreateDraft(
                    "NP", "NP-HOST-LIVE", 2, "Host Live V2", "Gazette HL2", "Notice-HL-2",
                    """{"RequiredBusinessClassifications":["MICRO_RETAIL"]}""",
                    ComplianceEnforcementMode.MandatoryFailClosed, eff, null);
                dbV2.CountryCompliancePolicies.Add(p2);
                await dbV2.SaveChangesAsync();
                p1.Supersede(p2.Id, eff);
                await dbV2.SaveChangesAsync();
                p2.Approve(approverId, eff.AddDays(1), "Approved HL2");
                await dbV2.SaveChangesAsync();
                await dbV2.CommitTransactionAsync();

                var procV2 = new ConfigurationInvalidationProcessor(dbV2, hostTransport, testClock, NullLogger<ConfigurationInvalidationProcessor>.Instance);
                var s2 = await procV2.DispatchPendingAsync();
                if (s2.Failed != 0 || s2.Dispatched == 0)
                {
                    throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: host transport failed to publish V2 invalidation after Redis became available.");
                }
                expectedV2Dispatched = s2.Dispatched;
            }

            for (int i = 0; i < 60 && (sub.ReceivedNotificationCount - notifBeforeV2 < expectedV2Dispatched || hostMem.TryGetValue(npV1FullKey, out _)); i++)
            {
                await Task.Delay(50);
            }
            await Task.Delay(150);

            if (sub.ReceivedNotificationCount - notifBeforeV2 != expectedV2Dispatched)
            {
                throw new InvalidOperationException($"MDM-1.5 R05-F01 FAIL: expected 1:1 post-startup notification delivery (dispatched={expectedV2Dispatched}, delta={sub.ReceivedNotificationCount - notifBeforeV2}).");
            }
            if (hostMem.TryGetValue(npV1FullKey, out _))
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: targeted NP L1 entry was retained after notification.");
            }
            if (!hostMem.TryGetValue(usKeepFullKey, out _))
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: unrelated US L1 entry was evicted during NP invalidation.");
            }

            // Warm V2 into L1 before simulating mid-flight Redis interruption
            string npV2FullKey;
            await using (var warmScope2 = host.Services.CreateAsyncScope())
            {
                var cons2 = warmScope2.ServiceProvider.GetRequiredService<IConfigurationConsistencyService>();
                var eval2 = warmScope2.ServiceProvider.GetRequiredService<ICountryComplianceEvaluator>();
                var r2 = await eval2.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-HOST-LIVE", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
                if (r2.Status != ComplianceApplicabilityStatus.Required || r2.EvaluatedPolicyVersion != 2)
                {
                    throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: host did not evaluate V2 cleanly.");
                }
                var d2 = await cons2.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "compliance-policies:NP-HOST-LIVE",
                    (d, ct) => d.CountryCompliancePolicies.AsNoTracking()
                        .Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-HOST-LIVE").ToListAsync(ct));
                npV2FullKey = $"{hostInstancePrefix}{d2.ScopedCacheKey}";
            }

            // 5. Actual Redis connection interruption on SAME host/mux/subscriber/transport
            long flushBeforeInterrupt = sub.ReconnectFlushCount;
            await proxy.SimulateOutageAsync();
            for (int i = 0; i < 100 && mux.IsConnected; i++)
            {
                await Task.Delay(100);
            }
            if (mux.IsConnected)
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: multiplexer stayed connected after TCP proxy outage simulation.");
            }

            await using (var outageDb = CreatePgDb(pgConn))
            {
                await outageDb.BeginTransactionAsync();
                var p2 = await outageDb.CountryCompliancePolicies.SingleAsync(p => p.CountryCode == "NP" && p.PolicyCode == "NP-HOST-LIVE" && p.Version == 2);
                var p3 = CountryCompliancePolicy.CreateDraft(
                    "NP", "NP-HOST-LIVE", 3, "Host Live V3 Outage", "Gazette HL3", "Notice-HL-3",
                    """{"ExemptBusinessClassifications":["MICRO_RETAIL"]}""",
                    ComplianceEnforcementMode.MandatoryFailClosed, eff, null);
                outageDb.CountryCompliancePolicies.Add(p3);
                await outageDb.SaveChangesAsync();
                p2.Supersede(p3.Id, eff);
                await outageDb.SaveChangesAsync();
                p3.Approve(approverId, eff.AddDays(2), "Approved HL3 during proxy outage");
                await outageDb.SaveChangesAsync();
                await outageDb.CommitTransactionAsync();

                var outageProc = new ConfigurationInvalidationProcessor(outageDb, hostTransport, testClock, NullLogger<ConfigurationInvalidationProcessor>.Instance);
                var outSweep = await outageProc.DispatchPendingAsync();
                if (outSweep.Failed == 0 || outSweep.Dispatched != 0)
                {
                    throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: host transport did not fail/retain outbox events during Redis interruption.");
                }

                // Immediate backoff check while pending event awaits NextRetryAtUtc
                var earlyRetrySweep = await outageProc.DispatchPendingAsync();
                if (earlyRetrySweep.Attempted != 0 || earlyRetrySweep.DeferredByBackoff == 0)
                {
                    throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: pending outbox event did not enforce exponential backoff window.");
                }
            }

            // Authoritative PostgreSQL read during outage MUST observe V3 cleanly on SAME host
            await using (var outageReadScope = host.Services.CreateAsyncScope())
            {
                var evalOut = outageReadScope.ServiceProvider.GetRequiredService<ICountryComplianceEvaluator>();
                var r3 = await evalOut.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-HOST-LIVE", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
                if (r3.Status != ComplianceApplicabilityStatus.NotRequired || r3.EvaluatedPolicyVersion != 3)
                {
                    throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: host did not serve V3 authoritatively from PostgreSQL during Redis outage.");
                }
            }

            // Add an outage-era local L1 entry directly in hostCache to verify reconnect flush explicitly
            await hostCache.SetAsync("mdm:cfg:Country:NP:outage-stale-key", "stale-v3");
            var staleOutageFullKey = $"{hostInstancePrefix}mdm:cfg:Country:NP:outage-stale-key";
            if (!hostMem.TryGetValue(staleOutageFullKey, out _))
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01 setup failure: outage-era L1 key missing before reconnect.");
            }

            // 6. Restore SAME proxy port -> same multiplexer/subscriber reconnects, flushes L1, and pending outbox retries succeed
            long notifBeforeRecovery = sub.ReceivedNotificationCount;
            proxy.Restore();
            for (int i = 0; i < 250 &&
                 (!mux.IsConnected || !sub.IsSubscribed || sub.ReconnectFlushCount <= flushBeforeInterrupt ||
                  hostMem.TryGetValue(staleOutageFullKey, out _) || hostMem.TryGetValue(npV2FullKey, out _)); i++)
            {
                await Task.Delay(100);
            }

            if (!mux.IsConnected || !sub.IsSubscribed || sub.ReconnectFlushCount <= flushBeforeInterrupt)
            {
                throw new InvalidOperationException($"MDM-1.5 R05-F01 FAIL: host subscriber did not restore subscription & flush after Redis reconnection (connected={mux.IsConnected}, sub={sub.IsSubscribed}, flush={sub.ReconnectFlushCount}/{flushBeforeInterrupt}).");
            }
            if (hostMem.TryGetValue(staleOutageFullKey, out _) || hostMem.TryGetValue(npV2FullKey, out _))
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: reconnect flush left stale NP local L1 cache entries behind.");
            }

            // 7. Advance clock past NextRetryAtUtc backoff and dispatch pending outbox events on SAME hostTransport
            testClock.UtcNow = testClock.UtcNow.AddSeconds(15);
            await using (var recDb = CreatePgDb(pgConn))
            {
                var recoverProc = new ConfigurationInvalidationProcessor(recDb, hostTransport, testClock, NullLogger<ConfigurationInvalidationProcessor>.Instance);
                var recSweep = await recoverProc.DispatchPendingAsync();
                if (recSweep.Failed != 0 || recSweep.Dispatched == 0)
                {
                    throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: outbox retry sweep on recovered host transport failed.");
                }
            }

            for (int i = 0; i < 60 && sub.ReceivedNotificationCount == notifBeforeRecovery; i++)
            {
                await Task.Delay(50);
            }
            if (sub.ReceivedNotificationCount == notifBeforeRecovery)
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: recovered outbox retry notification was not delivered to reconnected subscriber.");
            }

            // Subsequent mutation V3 -> V4 MUST increment ReceivedNotificationCount by EXACTLY 1 (proving zero duplicate callback registration across reconnect)
            await Task.Delay(150);
            string npV3FullKey;
            await using (var warmScope3 = host.Services.CreateAsyncScope())
            {
                var cons3 = warmScope3.ServiceProvider.GetRequiredService<IConfigurationConsistencyService>();
                var d3 = await cons3.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "NP", "compliance-policies:NP-HOST-LIVE",
                    (d, ct) => d.CountryCompliancePolicies.AsNoTracking()
                        .Where(p => p.CountryCode == "NP" && p.PolicyCode == "NP-HOST-LIVE").ToListAsync(ct));
                await cons3.GetOrLoadVerifiedWithDiagnosticsAsync(
                    ConfigurationScopeType.Country, "US", "host-us-unrelated",
                    (d, ct) => d.HolidayCalendars.AsNoTracking().CountAsync(h => h.CountryCode == "US", ct));
                npV3FullKey = $"{hostInstancePrefix}{d3.ScopedCacheKey}";
            }

            long notifBeforeV4 = sub.ReceivedNotificationCount;
            int expectedV4Dispatched;
            await using (var dbV4 = CreatePgDb(pgConn))
            {
                await dbV4.BeginTransactionAsync();
                var p3 = await dbV4.CountryCompliancePolicies.SingleAsync(p => p.CountryCode == "NP" && p.PolicyCode == "NP-HOST-LIVE" && p.Version == 3);
                var p4 = CountryCompliancePolicy.CreateDraft(
                    "NP", "NP-HOST-LIVE", 4, "Host Live V4 Post-Reconnect", "Gazette HL4", "Notice-HL-4",
                    """{"RequiredBusinessClassifications":["MICRO_RETAIL"]}""",
                    ComplianceEnforcementMode.MandatoryFailClosed, eff, null);
                dbV4.CountryCompliancePolicies.Add(p4);
                await dbV4.SaveChangesAsync();
                p3.Supersede(p4.Id, eff);
                await dbV4.SaveChangesAsync();
                p4.Approve(approverId, eff.AddDays(3), "Approved HL4 post reconnect");
                await dbV4.SaveChangesAsync();
                await dbV4.CommitTransactionAsync();

                var procV4 = new ConfigurationInvalidationProcessor(dbV4, hostTransport, testClock, NullLogger<ConfigurationInvalidationProcessor>.Instance);
                var s4 = await procV4.DispatchPendingAsync();
                if (s4.Failed != 0 || s4.Dispatched == 0)
                {
                    throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: V4 dispatch after reconnect failed.");
                }
                expectedV4Dispatched = s4.Dispatched;
            }

            for (int i = 0; i < 60 && (sub.ReceivedNotificationCount - notifBeforeV4 < expectedV4Dispatched || hostMem.TryGetValue(npV3FullKey, out _)); i++)
            {
                await Task.Delay(50);
            }
            await Task.Delay(150);
            if (sub.ReceivedNotificationCount - notifBeforeV4 != expectedV4Dispatched)
            {
                throw new InvalidOperationException($"MDM-1.5 R05-F01 FAIL: duplicate/missing subscriber callback after reconnect (dispatched={expectedV4Dispatched}, delta={sub.ReceivedNotificationCount - notifBeforeV4}).");
            }
            if (hostMem.TryGetValue(npV3FullKey, out _) || !hostMem.TryGetValue(usKeepFullKey, out _))
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: targeted NP eviction or unrelated US retention failed post-reconnect.");
            }

            // 8. Harmless duplicate notification + authoritative V4 verification
            await mux.GetSubscriber().PublishAsync(
                RedisChannel.Literal(RedisConfigurationInvalidationSubscriber.ChannelName),
                """{"Id":88888,"ScopeType":"Country","ScopeKey":"NP","NewEpoch":404,"EntityType":"CountryCompliancePolicy","EntityId":"dup-host"}""");
            await Task.Delay(250);

            await using (var finalScope = host.Services.CreateAsyncScope())
            {
                var eval4 = finalScope.ServiceProvider.GetRequiredService<ICountryComplianceEvaluator>();
                var r4 = await eval4.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-HOST-LIVE", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
                if (r4.Status != ComplianceApplicabilityStatus.Required || r4.EvaluatedPolicyVersion != 4)
                {
                    throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: host did not serve V4 cleanly after duplicate notification.");
                }
            }

            await host.StopAsync();
            if (sub.IsSubscribed)
            {
                throw new InvalidOperationException("MDM-1.5 R05-F01 FAIL: subscriber remained marked subscribed after host shutdown.");
            }

            Console.WriteLine("PASS Live Generic Host Offline Startup -> Late Redis -> Disconnect/Reconnect Flush -> Single-Callback Recovery Verified.");
        }
        finally
        {
            if (host != null)
            {
                try { await host.StopAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
        }
    }

    private static (string Host, int Port) ExtractRedisEndpoint(ConfigurationOptions options)
    {
        var ep = options.EndPoints.FirstOrDefault() ?? throw new InvalidOperationException("Redis connection string missing endpoint.");
        return ep switch
        {
            DnsEndPoint dns => (dns.Host, dns.Port > 0 ? dns.Port : 6379),
            IPEndPoint ip => (ip.Address.ToString(), ip.Port > 0 ? ip.Port : 6379),
            _ => ("127.0.0.1", 6379)
        };
    }

    private sealed class RedisTcpProxy : IAsyncDisposable
    {
        private readonly string _upstreamHost;
        private readonly int _upstreamPort;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<long, (TcpClient Client, TcpClient Upstream)> _sessions = new();
        private TcpListener? _listener;
        private Task? _acceptTask;
        private long _nextSessionId;
        private volatile bool _outageMode;

        public int Port { get; }

        public RedisTcpProxy(string upstreamHost, int upstreamPort)
        {
            _upstreamHost = upstreamHost;
            _upstreamPort = upstreamPort;

            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }

        public void StartListening()
        {
            if (_listener != null) return;
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public async Task SimulateOutageAsync()
        {
            _outageMode = true;
            foreach (var session in _sessions.Values)
            {
                try { session.Client.Dispose(); } catch { }
                try { session.Upstream.Dispose(); } catch { }
            }
            _sessions.Clear();
            await Task.Delay(200);
        }

        public void Restore() => _outageMode = false;

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                TcpClient? client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct);
                    if (_outageMode)
                    {
                        client.Dispose();
                        continue;
                    }

                    _ = Task.Run(() => ForwardClientAsync(client, ct), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    client?.Dispose();
                    break;
                }
                catch
                {
                    client?.Dispose();
                }
            }
        }

        private async Task ForwardClientAsync(TcpClient client, CancellationToken ct)
        {
            var upstream = new TcpClient();
            long sessionId = Interlocked.Increment(ref _nextSessionId);
            try
            {
                client.NoDelay = true;
                upstream.NoDelay = true;
                await upstream.ConnectAsync(_upstreamHost, _upstreamPort, ct);
                if (_outageMode)
                {
                    return;
                }

                _sessions[sessionId] = (client, upstream);
                await using var cStream = client.GetStream();
                await using var uStream = upstream.GetStream();

                var t1 = cStream.CopyToAsync(uStream, ct);
                var t2 = uStream.CopyToAsync(cStream, ct);
                await Task.WhenAny(t1, t2);
            }
            catch
            {
            }
            finally
            {
                _sessions.TryRemove(sessionId, out _);
                try { client.Dispose(); } catch { }
                try { upstream.Dispose(); } catch { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _listener?.Stop(); } catch { }
            foreach (var session in _sessions.Values)
            {
                try { session.Client.Dispose(); } catch { }
                try { session.Upstream.Dispose(); } catch { }
            }
            _sessions.Clear();
            if (_acceptTask != null)
            {
                try { await _acceptTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            }
            _cts.Dispose();
        }
    }

    private sealed class PgMutableClock(DateTime initialUtc) : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = initialUtc;
    }

    private static ApplicationDbContext CreatePgDb(string connStr)
    {
        var user = new PgAdminUserContext(Guid.NewGuid());
        var audit = new AuditableEntityInterceptor(user, new DateTimeProvider());
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connStr, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(audit)
            .Options;
        return new ApplicationDbContext(opts, new PgBypassTenantProvider(), user, audit);
    }

    private sealed class FailingThenWorkingPgTransport : IConfigurationInvalidationTransport
    {
        public bool ShouldFail { get; set; } = true;

        public Task PublishInvalidationAsync(ConfigurationInvalidationEvent evt, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new InvalidOperationException("Simulated PostgreSQL Redis pub/sub unavailable.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class PgBypassTenantProvider : ITenantProvider
    {
        public Guid? TenantId => null;
        public bool HasTenant => false;
        public bool BypassTenantFilter { get; set; } = true;
        public void SetTenantId(Guid tenantId) { }
    }

    private sealed class PgAdminUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "pg15@m15.test";
        public Guid? TenantId => null;
        public Guid? FacilityId => null;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => true;
        public bool HasPermission(string permission) => true;
        public bool IsInRole(string role) => false;
    }
}
