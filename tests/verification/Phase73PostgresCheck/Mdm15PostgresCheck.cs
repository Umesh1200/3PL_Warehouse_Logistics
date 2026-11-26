using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Reference;
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

            // 9. Dispatcher retryable delivery without false completion on live PostgreSQL
            var pgTransport = new FailingThenWorkingPgTransport();
            await using (var dispDb = CreatePgDb(targetConn))
            {
                var processor = new ConfigurationInvalidationProcessor(
                    dispDb, pgTransport, clock, NullLogger<ConfigurationInvalidationProcessor>.Instance);

                var resFail = await processor.DispatchPendingAsync();
                if (resFail.Attempted == 0 || resFail.Failed != resFail.Attempted || resFail.Dispatched != 0)
                {
                    throw new InvalidOperationException("MDM-1.5 PG dispatcher should fail all events when transport throws.");
                }

                var stillPending = await dispDb.ConfigurationInvalidationEvents
                    .AsNoTracking()
                    .Where(e => e.DispatchedToRedisAtUtc == null)
                    .ToListAsync();
                if (stillPending.Count == 0 || stillPending.Any(e => e.DispatchAttempts < 1 || string.IsNullOrEmpty(e.LastDispatchError)))
                {
                    throw new InvalidOperationException("MDM-1.5 PG failed dispatch events lost or missing retry metadata.");
                }

                pgTransport.ShouldFail = false;
                var resOk = await processor.DispatchPendingAsync();
                if (resOk.Failed != 0 || resOk.Dispatched != stillPending.Count)
                {
                    throw new InvalidOperationException("MDM-1.5 PG retry sweep did not complete all pending outbox events cleanly.");
                }

                if (await dispDb.ConfigurationInvalidationEvents.AsNoTracking().AnyAsync(e => e.DispatchedToRedisAtUtc == null))
                {
                    throw new InvalidOperationException("MDM-1.5 PG undispatched events remain after clean retry sweep.");
                }
            }

            Console.WriteLine("PASS PostgreSQL MDM-1.5 Scope Epochs, Outbox Retry, Concurrency Serialization, Rollback & 2-Instance No-Redis Verification.");
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
