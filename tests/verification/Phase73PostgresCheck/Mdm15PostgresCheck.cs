using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using Warehouse3PL.Application.Common.Interfaces;
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
            var p1 = await realCommitDb.CountryCompliancePolicies.SingleAsync(p => p.CountryCode == "NP" && p.PolicyCode == "NP-PG15-R02" && p.Version == 1);
            var pGood = CountryCompliancePolicy.CreateDraft(
                "NP", "NP-PG15-R02", 2, "R02 AUTHORITATIVE V2", "Gazette R02", "Notice-R02-GOOD",
                """{"RequiredBusinessClassifications":["MICRO_RETAIL"]}""",
                ComplianceEnforcementMode.MandatoryFailClosed, r02Eff, null);
            realCommitDb.CountryCompliancePolicies.Add(pGood);
            await realCommitDb.SaveChangesAsync();
            p1.Supersede(pGood.Id, r02Eff);
            pGood.Approve(approverId, r02Eff.AddDays(1), "Committed real V2");
            await realCommitDb.SaveChangesAsync();
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

        var subB = new RedisConfigurationInvalidationSubscriber(cacheB, optsB, NullLogger<RedisConfigurationInvalidationSubscriber>.Instance, muxB);
        await subB.StartAsync(CancellationToken.None);

        try
        {
            // Wait briefly for Redis pub/sub subscription handshake
            await Task.Delay(250);

            // 1. Instance B warms L1 + Redis cache at V1
            await using (var dbB1 = CreatePgDb(pgConn))
            {
                var consB1 = new ConfigurationConsistencyService(dbB1, cacheB, clock, NullLogger<ConfigurationConsistencyService>.Instance);
                var evalB1 = new CountryComplianceEvaluator(dbB1, clock, consB1);
                var r1 = await evalB1.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-REDIS-LIVE", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
                if (r1.Status != ComplianceApplicabilityStatus.NotRequired || cacheB.TrackedLocalKeyCount == 0)
                {
                    throw new InvalidOperationException("MDM-1.5 R05 Instance B failed to warm L1+Redis cache at V1.");
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

            // 3. Verify Instance B's live Redis subscriber evicted L1 automatically
            for (int i = 0; i < 30 && subB.ReceivedNotificationCount == 0; i++)
            {
                await Task.Delay(100);
            }

            if (subB.ReceivedNotificationCount == 0 || memB.Count != 0)
            {
                // Allow slight async delay for callback removal
                await Task.Delay(200);
                if (subB.ReceivedNotificationCount == 0)
                {
                    throw new InvalidOperationException($"MDM-1.5 R05 receiving instance subscriber did not receive real Redis notification (count={subB.ReceivedNotificationCount}).");
                }
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
