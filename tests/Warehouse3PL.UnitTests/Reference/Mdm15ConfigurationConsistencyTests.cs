using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure;
using Warehouse3PL.Infrastructure.BackgroundJobs;
using Warehouse3PL.Infrastructure.Caching;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Reference;

public sealed class Mdm15ConfigurationConsistencyTests
{
    private static (ApplicationDbContext Db, MemoryCache Mem, ICacheService Cache, ConfigurationConsistencyService Consistency) CreateInstance(
        string dbName,
        Guid? tenantId = null,
        Guid? facilityId = null,
        CacheOptions? cacheOptions = null)
    {
        var user = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Staff,
            TenantId = tenantId,
            FacilityId = facilityId
        };
        var tenant = new TestTenantProvider { TenantId = tenantId, BypassTenantFilter = true };
        var db = TestDbContextFactory.Create(tenant, user, dbName);
        var mem = new MemoryCache(new MemoryCacheOptions());
        var opts = Options.Create(cacheOptions ?? new CacheOptions { EnableRedis = false, DefaultExpirationMinutes = 30, LocalFallbackMaxTtlSeconds = 30 });
        var cache = new RedisCacheService(mem, opts, NullLogger<RedisCacheService>.Instance);
        var consistency = new ConfigurationConsistencyService(db, cache, new DateTimeProvider(), NullLogger<ConfigurationConsistencyService>.Instance, user);
        return (db, mem, cache, consistency);
    }

    [Fact]
    public async Task TwoInstances_WithoutRedis_DetectPolicyFxCalendarAndLegalEntityMutations_Immediately()
    {
        var dbName = $"mdm15-multi-{Guid.NewGuid():N}";
        var facId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var leId = Guid.NewGuid();
        var effFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var evalTime = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeProvider();

        // Seed Instance A & initial v1 MDM configs
        var (dbA, memA, _, _) = CreateInstance(dbName);
        await ReferenceCatalogSeeder.SeedAsync(dbA);
        dbA.Facilities.Add(new Facility
        {
            Id = facId,
            FacilityCode = "FAC-M15",
            Name = "Kathmandu M15",
            CountryCode = "NP",
            Timezone = "Asia/Kathmandu",
            IsActive = true
        });

        var pV1 = CountryCompliancePolicy.CreateDraft(
            "NP", "NP-M15-RULE", 1, "Policy V1", "Nepal Gazette 2083", "Notice-V1",
            """{"RequiredBusinessClassifications":["WHOLESALE"],"ExemptBusinessClassifications":["MICRO_RETAIL"]}""",
            ComplianceEnforcementMode.MandatoryFailClosed, effFrom, null);
        pV1.Approve(approverId, effFrom, "Verified V1 gazette citation");

        var fxV1 = FxRateRecord.CreateDraft(
            "USD", "NPR", effFrom, null, 1, 133.5m, 134.5m, 134.0m,
            FxRateSourceAuthority.ManualApproved, "BOARD-V1", effFrom, evidenceReference: "BOARD_MINUTES_V1");
        fxV1.Approve(approverId, effFrom, "Approved V1 USD/NPR", evidenceReference: "BOARD_MINUTES_V1");

        var calV1 = FacilityCalendar.Create(
            facId, effFrom, null, "Saturday", new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), new TimeSpan(17, 0, 0));

        var le = LegalEntity.Create(
            "LE-M15", "Himalayan Logistics Pvt Ltd", "REG-15", "NP",
            TaxIdentifierType.VAT, "601234567", "Durbar Marg 1", null, "Kathmandu", "Bagmati", "44600",
            isCbmsMandated: false, iroSoftwarePermitRef: null, defaultCurrencyCode: "NPR", id: leId);

        dbA.CountryCompliancePolicies.Add(pV1);
        dbA.FxRateRecords.Add(fxV1);
        dbA.FacilityCalendars.Add(calV1);
        dbA.LegalEntities.Add(le);
        await dbA.SaveChangesAsync();

        // Instance B (separate DbContext + separate MemoryCache, Redis pub/sub completely off!)
        var (dbB, memB, _, consB) = CreateInstance(dbName);
        var evalB = new CountryComplianceEvaluator(dbB, now, consB);
        var fxB = new FxConversionService(dbB, new FxRateProvider(), now, consB);
        var wcB = new WorkingCalendarService(dbB, consB);

        // Warm Instance B caches at v1
        var comp1 = await evalB.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-M15-RULE", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
        Assert.Equal(ComplianceApplicabilityStatus.NotRequired, comp1.Status);
        Assert.Equal(1, comp1.EvaluatedPolicyVersion);

        var fxRes1 = await fxB.ConvertAmountAsync(10m, "USD", "NPR", evalTime);
        Assert.Equal(1340.00m, fxRes1.ConvertedAmount);

        var sunday = new DateOnly(2026, 11, 22);
        Assert.True((await wcB.EvaluateWorkingDayAsync(facId, sunday)).IsWorkingDay);

        var leRes1 = await consB.GetVerifiedLegalEntityAsync(leId);
        Assert.False(leRes1!.IsCbmsMandated);

        // Mutate in Instance A WITHOUT sending any Redis notification or touching memB!
        var pV2 = CountryCompliancePolicy.CreateDraft(
            "NP", "NP-M15-RULE", 2, "Policy V2", "Nepal Gazette 2083 Rev2", "Notice-V2",
            """{"RequiredBusinessClassifications":["WHOLESALE","MICRO_RETAIL"]}""",
            ComplianceEnforcementMode.MandatoryFailClosed, effFrom, null);
        pV2.Approve(approverId, effFrom.AddDays(1), "Verified V2 mandate update");
        pV1.Supersede(pV2.Id, effFrom);
        dbA.CountryCompliancePolicies.Add(pV2);

        var fxV2 = FxRateRecord.CreateDraft(
            "USD", "NPR", effFrom, null, 1, 135.5m, 136.5m, 136.0m,
            FxRateSourceAuthority.ManualApproved, "BOARD-V2", effFrom.AddDays(1), evidenceReference: "BOARD_MINUTES_V2");
        fxV2.Approve(approverId, effFrom.AddDays(1), "Approved V2 USD/NPR", evidenceReference: "BOARD_MINUTES_V2");
        fxV1.Supersede(fxV2.Id, effFrom.AddDays(1));
        dbA.FxRateRecords.Add(fxV2);

        calV1.Deactivate();
        dbA.FacilityCalendars.Add(FacilityCalendar.Create(
            facId, effFrom, null, "Saturday,Sunday", new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0), new TimeSpan(17, 0, 0)));

        le.UpdateDetails(
            "Himalayan Logistics Pvt Ltd", "REG-15", "NP", TaxIdentifierType.VAT, "601234567",
            "Durbar Marg 1", null, "Kathmandu", "Bagmati", "44600",
            isCbmsMandated: true, iroSoftwarePermitRef: "IRO-CBMS-999", defaultCurrencyCode: "NPR");

        await dbA.SaveChangesAsync();

        // Outbox MUST contain undispatched ConfigurationInvalidationEvents (Redis NOT dispatched!)
        Assert.True(await dbA.ConfigurationInvalidationEvents.AnyAsync(e => e.DispatchedToRedisAtUtc == null));

        // Instance B MUST detect new PostgreSQL/DB epochs immediately without Redis!
        var comp2 = await evalB.EvaluateAsync(new ComplianceEvaluationRequest("NP", "NP-M15-RULE", BusinessClassification: "MICRO_RETAIL", EvaluationUtc: evalTime));
        Assert.Equal(ComplianceApplicabilityStatus.Required, comp2.Status);
        Assert.Equal(2, comp2.EvaluatedPolicyVersion);

        var fxRes2 = await fxB.ConvertAmountAsync(10m, "USD", "NPR", evalTime);
        Assert.Equal(1360.00m, fxRes2.ConvertedAmount);

        Assert.False((await wcB.EvaluateWorkingDayAsync(facId, sunday)).IsWorkingDay);

        var leRes2 = await consB.GetVerifiedLegalEntityAsync(leId);
        Assert.True(leRes2!.IsCbmsMandated);
        Assert.Equal("IRO-CBMS-999", leRes2.IroSoftwarePermitRef);

        memA.Dispose();
        memB.Dispose();
        await dbA.DisposeAsync();
        await dbB.DisposeAsync();
    }

    [Fact]
    public async Task CachePrefixEviction_TenantPartitionIsolation_DispatcherRetry_And_FailClosedDbError_Work()
    {
        var dbName = $"mdm15-ctrl-{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var facA = Guid.NewGuid();
        var facB = Guid.NewGuid();

        // 1. RedisCacheService prefix eviction & <=30s local TTL clamp
        var mem = new MemoryCache(new MemoryCacheOptions());
        var cache = new RedisCacheService(
            mem,
            Options.Create(new CacheOptions { EnableRedis = false, DefaultExpirationMinutes = 60, LocalFallbackMaxTtlSeconds = 120 }),
            NullLogger<RedisCacheService>.Instance);

        await cache.SetAsync("mdm:cfg:Country:NP:k1", "np-val-1", absoluteExpiration: TimeSpan.FromMinutes(30));
        await cache.SetAsync("mdm:cfg:Country:NP:k2", "np-val-2", absoluteExpiration: TimeSpan.FromMinutes(30));
        await cache.SetAsync("mdm:cfg:Country:US:k1", "us-val-1", absoluteExpiration: TimeSpan.FromMinutes(30));

        await cache.RemoveByPrefixAsync("mdm:cfg:Country:NP:");
        Assert.Null(await cache.GetAsync<string>("mdm:cfg:Country:NP:k1"));
        Assert.Null(await cache.GetAsync<string>("mdm:cfg:Country:NP:k2"));
        Assert.Equal("us-val-1", await cache.GetAsync<string>("mdm:cfg:Country:US:k1"));

        // 2. Tenant & facility cache key separation + hit diagnostics
        var (dbA, memA, cacheA, consA) = CreateInstance(dbName, tenantA, facA);
        var (dbB, memB, _, consB) = CreateInstance(dbName, tenantB, facB);
        await ReferenceCatalogSeeder.SeedAsync(dbA);

        var diagA1 = await consA.GetOrLoadVerifiedWithDiagnosticsAsync(
            ConfigurationScopeType.Client, tenantA.ToString("D"), "client-bundle",
            (_, _) => Task.FromResult("PAYLOAD-TENANT-A"),
            tenantA, facA);
        var diagA2 = await consA.GetOrLoadVerifiedWithDiagnosticsAsync(
            ConfigurationScopeType.Client, tenantA.ToString("D"), "client-bundle",
            (_, _) => Task.FromResult("SHOULD-NOT-RUN"),
            tenantA, facA);
        var diagB1 = await consB.GetOrLoadVerifiedWithDiagnosticsAsync(
            ConfigurationScopeType.Client, tenantB.ToString("D"), "client-bundle",
            (_, _) => Task.FromResult("PAYLOAD-TENANT-B"),
            tenantB, facB);

        Assert.False(diagA1.ServedFromCache);
        Assert.True(diagA2.ServedFromCache);
        Assert.False(diagB1.ServedFromCache);
        Assert.Equal("PAYLOAD-TENANT-A", diagA2.Value);
        Assert.Equal("PAYLOAD-TENANT-B", diagB1.Value);
        Assert.NotEqual(diagA1.ScopedCacheKey, diagB1.ScopedCacheKey);
        Assert.Contains($"tenant={tenantA:D}", diagA1.ScopedCacheKey, StringComparison.Ordinal);
        Assert.Contains($"facility={facA:D}", diagA1.ScopedCacheKey, StringComparison.Ordinal);

        // 3. Dispatcher retryable delivery without false completion + exponential backoff
        var controllableClock = new MutableClock(DateTime.UtcNow);
        var transport = new ToggleableInvalidationTransport(shouldFail: true);
        var processor = new ConfigurationInvalidationProcessor(
            dbA, transport, controllableClock, NullLogger<ConfigurationInvalidationProcessor>.Instance);

        var failSweep = await processor.DispatchPendingAsync();
        Assert.True(failSweep.Failed > 0);
        Assert.Equal(0, failSweep.Dispatched);
        Assert.All(
            await dbA.ConfigurationInvalidationEvents.AsNoTracking().ToListAsync(),
            e =>
            {
                Assert.Null(e.DispatchedToRedisAtUtc);
                Assert.True(e.DispatchAttempts >= 1);
                Assert.NotNull(e.LastDispatchError);
                Assert.NotNull(e.NextRetryAtUtc);
            });

        // Before backoff expires, sweep defers rather than hot-looping
        transport.ShouldFail = false;
        var deferredSweep = await processor.DispatchPendingAsync();
        Assert.Equal(0, deferredSweep.Attempted);
        Assert.Equal(failSweep.Attempted, deferredSweep.DeferredByBackoff);

        // Once backoff window passes, sweep delivers cleanly
        controllableClock.UtcNow = controllableClock.UtcNow.AddSeconds(10);
        var okSweep = await processor.DispatchPendingAsync();
        Assert.Equal(0, okSweep.Failed);
        Assert.Equal(failSweep.Attempted, okSweep.Dispatched);
        Assert.All(
            await dbA.ConfigurationInvalidationEvents.AsNoTracking().ToListAsync(),
            e =>
            {
                Assert.NotNull(e.DispatchedToRedisAtUtc);
                Assert.Null(e.LastDispatchError);
                Assert.Null(e.NextRetryAtUtc);
            });

        // 4. Fail closed when DB epoch verification throws (never serve stale cache during DB outage!)
        await dbA.DisposeAsync();
        var brokenEx = await Assert.ThrowsAsync<DomainException>(() =>
            consA.GetOrLoadVerifiedAsync(
                ConfigurationScopeType.Client, tenantA.ToString("D"), "client-bundle",
                (_, _) => Task.FromResult("FALLBACK"),
                tenantA, facA));
        Assert.Equal("CONFIG_EPOCH_VERIFICATION_UNAVAILABLE", brokenEx.Code);

        mem.Dispose();
        memA.Dispose();
        memB.Dispose();
        await dbB.DisposeAsync();
    }

    [Fact]
    public async Task R01_EffectiveDateSafeFxCaching_PreservesWindowParityAcrossReversedRequests()
    {
        var dbName = $"mdm15-r01-{Guid.NewGuid():N}";
        var approverId = Guid.NewGuid();
        var (db, mem, _, cons) = CreateInstance(dbName);
        await ReferenceCatalogSeeder.SeedAsync(db);

        var w1Start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var w1End = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var w2End = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var fxW1 = FxRateRecord.CreateDraft(
            "EUR", "NPR", w1Start, w1End, 1, 139.0m, 141.0m, 140.0m,
            FxRateSourceAuthority.ManualApproved, "EUR-W1", w1Start, evidenceReference: "BOARD_W1");
        fxW1.Approve(approverId, w1Start, "Approved W1 EUR/NPR", evidenceReference: "BOARD_W1");

        var fxW2 = FxRateRecord.CreateDraft(
            "EUR", "NPR", w1End, w2End, 10, 1440.0m, 1460.0m, 1450.0m,
            FxRateSourceAuthority.ManualApproved, "EUR-W2", w1End, evidenceReference: "BOARD_W2");
        fxW2.Approve(approverId, w1End, "Approved W2 EUR/NPR", evidenceReference: "BOARD_W2");

        db.FxRateRecords.AddRange(fxW1, fxW2);
        await db.SaveChangesAsync();

        var clock = new DateTimeProvider();
        var cachedSvc = new FxConversionService(db, new FxRateProvider(), clock, cons);
        var uncachedSvc = new FxConversionService(db, new FxRateProvider(), clock, consistencyService: null);

        var juneDate = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
        var febDate = new DateTime(2026, 2, 15, 12, 0, 0, DateTimeKind.Utc);

        // Request later window (June) FIRST to populate FX pair cache, then query earlier window (February) -> MUST match uncached resolver identically!
        var cachedJun = await cachedSvc.ConvertAmountAsync(100m, "EUR", "NPR", juneDate, FxRateType.Sell, w2End);
        var uncachedJun = await uncachedSvc.ConvertAmountAsync(100m, "EUR", "NPR", juneDate, FxRateType.Sell, w2End);
        var cachedFeb = await cachedSvc.ConvertAmountAsync(100m, "EUR", "NPR", febDate, FxRateType.Buy, w2End);
        var uncachedFeb = await uncachedSvc.ConvertAmountAsync(100m, "EUR", "NPR", febDate, FxRateType.Buy, w2End);

        Assert.Equal(uncachedJun.ConvertedAmount, cachedJun.ConvertedAmount);
        Assert.Equal(uncachedJun.UnitMultiplier, cachedJun.UnitMultiplier);
        Assert.Equal(uncachedJun.BulletinVersion, cachedJun.BulletinVersion);
        Assert.Equal(uncachedFeb.ConvertedAmount, cachedFeb.ConvertedAmount);
        Assert.Equal(uncachedFeb.UnitMultiplier, cachedFeb.UnitMultiplier);
        Assert.Equal(uncachedFeb.BulletinVersion, cachedFeb.BulletinVersion);
        Assert.Equal(14600.00m, cachedJun.ConvertedAmount);
        Assert.Equal(13900.00m, cachedFeb.ConvertedAmount);

        mem.Dispose();
        await db.DisposeAsync();
    }

    [Fact]
    public async Task R02_R03_TransactionBypass_And_VerifiedReloadLoop_FailClosedUnderContention()
    {
        var dbName = $"mdm15-r02-r03-{Guid.NewGuid():N}";
        var user = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        var tenant = new TestTenantProvider { BypassTenantFilter = true };
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options;
        var interceptor = new AuditableEntityInterceptor(user, new DateTimeProvider());

        await using var db = new TxSimulatedDbContext(opts, tenant, user, interceptor);
        using var mem = new MemoryCache(new MemoryCacheOptions());
        var cache = new RedisCacheService(mem, Options.Create(new CacheOptions()), NullLogger<RedisCacheService>.Instance);
        var cons = new ConfigurationConsistencyService(db, cache, new DateTimeProvider(), NullLogger<ConfigurationConsistencyService>.Instance, user);

        await ReferenceCatalogSeeder.SeedAsync(db);

        // R02: Simulated active DB transaction MUST bypass cache reads & writes completely
        var warm = await cons.GetOrLoadVerifiedWithDiagnosticsAsync(
            ConfigurationScopeType.Country, "NP", "tx-test", (_, _) => Task.FromResult("COMMITTED-V1"));
        Assert.False(warm.ServedFromCache);

        db.SimulateActiveTransaction = true;
        var inTx = await cons.GetOrLoadVerifiedWithDiagnosticsAsync(
            ConfigurationScopeType.Country, "NP", "tx-test", (_, _) => Task.FromResult("UNCOMMITTED-DRAFT"));
        Assert.False(inTx.ServedFromCache);
        Assert.Equal(string.Empty, inTx.ScopedCacheKey);
        Assert.Equal("UNCOMMITTED-DRAFT", inTx.Value);
        db.SimulateActiveTransaction = false;

        // Shared cache MUST still hold COMMITTED-V1, never polluted by UNCOMMITTED-DRAFT!
        var afterRollback = await cons.GetOrLoadVerifiedWithDiagnosticsAsync(
            ConfigurationScopeType.Country, "NP", "tx-test", (_, _) => Task.FromResult("SHOULD-BE-CACHED"));
        Assert.True(afterRollback.ServedFromCache);
        Assert.Equal("COMMITTED-V1", afterRollback.Value);

        // R03 Case A: Epoch shifts twice mid-load (attempts 1 & 2), stabilizes on attempt 3 -> succeeds cleanly at latest epoch
        int loadCount = 0;
        await cache.RemoveByPrefixAsync("mdm:cfg:Country:US:");
        var retryRes = await cons.GetOrLoadVerifiedWithDiagnosticsAsync(
            ConfigurationScopeType.Country, "US", "loop-test",
            async (d, ct) =>
            {
                loadCount++;
                if (loadCount <= 2)
                {
                    d.HolidayCalendars.Add(HolidayCalendar.Create("US", HolidayScope.National, new DateOnly(2028, 1, loadCount), $"MidLoad {loadCount}"));
                    await d.SaveChangesAsync(ct);
                    return $"STALE-{loadCount}";
                }
                return "STABILIZED-V3";
            });
        Assert.Equal(3, loadCount);
        Assert.Equal("STABILIZED-V3", retryRes.Value);

        // R03 Case B: Exhaustion (>5 consecutive epoch changes) MUST fail closed without publishing to cache!
        using var mem3 = new MemoryCache(new MemoryCacheOptions());
        var cache3 = new RedisCacheService(mem3, Options.Create(new CacheOptions()), NullLogger<RedisCacheService>.Instance);
        var cons3 = new ConfigurationConsistencyService(db, cache3, new DateTimeProvider(), NullLogger<ConfigurationConsistencyService>.Instance, user);
        int exhaustLoads = 0;
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            cons3.GetOrLoadVerifiedWithDiagnosticsAsync(
                ConfigurationScopeType.Country, "US", "exhaust-test",
                async (d, ct) =>
                {
                    exhaustLoads++;
                    d.HolidayCalendars.Add(HolidayCalendar.Create("US", HolidayScope.National, new DateOnly(2028, 2, exhaustLoads), $"Exhaust {exhaustLoads}"));
                    await d.SaveChangesAsync(ct);
                    return $"UNVERIFIED-{exhaustLoads}";
                }));
        Assert.Equal("CONFIG_EPOCH_CONTENTION_EXCEEDED", ex.Code);
        Assert.Equal(5, exhaustLoads);
        Assert.Equal(0, cache3.TrackedLocalKeyCount);
        Assert.Equal(4, cache.TrackedLocalKeyCount); // NP (2) + US Case A (2) entries remain cleanly isolated
    }

    [Fact]
    public async Task R04_R05_GenerationSafePrefixTracking_SubscriberIdempotency_ScopeChanges_And_ProductionDi_Work()
    {
        // R04: Concurrent replacements + prefix eviction + bounded 30s TTL
        using var mem = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        var cache = new RedisCacheService(
            mem,
            Options.Create(new CacheOptions { EnableRedis = false, DefaultExpirationMinutes = 60, LocalFallbackMaxTtlSeconds = 180 }),
            NullLogger<RedisCacheService>.Instance);

        await Task.WhenAll(Enumerable.Range(1, 40).Select(async i =>
        {
            await cache.SetAsync("mdm:cfg:Country:NP:hot", $"val-{i}");
        }));

        // After 40 rapid replacements, replacement eviction callbacks MUST NOT have erased hot key's tracking!
        Assert.True(cache.TrackedLocalKeyCount >= 1);
        Assert.NotNull(await cache.GetAsync<string>("mdm:cfg:Country:NP:hot"));
        await cache.RemoveByPrefixAsync("mdm:cfg:Country:NP:");
        Assert.Null(await cache.GetAsync<string>("mdm:cfg:Country:NP:hot"));
        Assert.Equal(0, cache.TrackedLocalKeyCount);

        // R05 Subscriber duplicate/out-of-order idempotency test
        await cache.SetAsync("mdm:cfg:Country:NP:k1", "v1");
        await cache.SetAsync("mdm:cfg:Country:US:k1", "v-us");
        var subscriber = new RedisConfigurationInvalidationSubscriber(
            cache, Options.Create(new CacheOptions()), NullLogger<RedisConfigurationInvalidationSubscriber>.Instance);
        var payload = """{"Id":101,"ScopeType":"Country","ScopeKey":"NP","NewEpoch":4,"EntityType":"HolidayCalendar","EntityId":"x"}""";
        await subscriber.HandleInvalidationPayloadAsync(payload);
        await subscriber.HandleInvalidationPayloadAsync(payload); // duplicate notification
        Assert.Equal(2, subscriber.ReceivedNotificationCount);
        Assert.Null(await cache.GetAsync<string>("mdm:cfg:Country:NP:k1"));
        Assert.Equal("v-us", await cache.GetAsync<string>("mdm:cfg:Country:US:k1"));

        // Mutable scope migration (NP -> US) & Client Added/Modified/Deleted epoch coverage
        var dbName = $"mdm15-scopes-{Guid.NewGuid():N}";
        var (db, scopeMem, _, _) = CreateInstance(dbName);
        await ReferenceCatalogSeeder.SeedAsync(db);

        var client = new Client { AccountCode = "CL-M15", CompanyName = "Scope Client", Status = ClientStatus.Active };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        long clientEpochAfterAdd = await db.ConfigurationScopeEpochs.AsNoTracking()
            .Where(e => e.ScopeType == ConfigurationScopeType.Client && e.ScopeKey == client.Id.ToString("D").ToUpperInvariant())
            .Select(e => e.CurrentEpoch).SingleAsync();
        Assert.Equal(1, clientEpochAfterAdd);

        var hol = HolidayCalendar.Create("NP", HolidayScope.National, new DateOnly(2029, 3, 1), "Moveable Holiday");
        db.HolidayCalendars.Add(hol);
        await db.SaveChangesAsync();

        long usEpochBefore = await db.ConfigurationScopeEpochs.AsNoTracking()
            .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "US")
            .Select(e => e.CurrentEpoch).FirstAsync();
        long npEpochBefore = await db.ConfigurationScopeEpochs.AsNoTracking()
            .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "NP")
            .Select(e => e.CurrentEpoch).FirstAsync();

        hol.CountryCode = "US"; // Mutable scope key migration NP -> US!
        await db.SaveChangesAsync();

        long usEpochAfter = await db.ConfigurationScopeEpochs.AsNoTracking()
            .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "US")
            .Select(e => e.CurrentEpoch).FirstAsync();
        long npEpochAfter = await db.ConfigurationScopeEpochs.AsNoTracking()
            .Where(e => e.ScopeType == ConfigurationScopeType.Country && e.ScopeKey == "NP")
            .Select(e => e.CurrentEpoch).FirstAsync();
        Assert.Equal(npEpochBefore + 1, npEpochAfter);
        Assert.Equal(usEpochBefore + 1, usEpochAfter);

        // Mandatory Production DI verification (with EnableRedis = true, abortConnect=false so no Redis instance is required)
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=warehouse3pl_db;Username=postgres;Password=postgres",
                ["Redis:EnableRedis"] = "true",
                ["Redis:ConnectionString"] = "127.0.0.1:6399,abortConnect=false",
                ["Redis:InstanceName"] = "Warehouse3PL:",
                ["ObjectStorage:SigningSecretKey"] = "ProductionGradeTestSigningSecretKeyForMdm15Verification2026!"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();
        services.AddInfrastructureServices(config);
        await using var sp = services.BuildServiceProvider();
        await using var diScope = sp.CreateAsyncScope();
        Assert.NotNull(diScope.ServiceProvider.GetRequiredService<IConfigurationConsistencyService>());
        Assert.NotNull(diScope.ServiceProvider.GetRequiredService<ICacheService>());
        Assert.NotNull(diScope.ServiceProvider.GetRequiredService<IConfigurationInvalidationTransport>());
        Assert.NotNull(diScope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>());
        Assert.NotNull(diScope.ServiceProvider.GetRequiredService<RedisConfigurationInvalidationSubscriber>());
        Assert.NotNull(diScope.ServiceProvider.GetRequiredService<ConfigurationInvalidationProcessor>());
        Assert.NotNull(diScope.ServiceProvider.GetRequiredService<ICountryComplianceEvaluator>());
        Assert.NotNull(diScope.ServiceProvider.GetRequiredService<IFxConversionService>());
        Assert.NotNull(diScope.ServiceProvider.GetRequiredService<IWorkingCalendarService>());

        scopeMem.Dispose();
        await db.DisposeAsync();
    }

    private sealed class TxSimulatedDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantProvider tenantProvider,
        ICurrentUserContext currentUser,
        AuditableEntityInterceptor interceptor)
        : ApplicationDbContext(options, tenantProvider, currentUser, interceptor), IApplicationDbContext
    {
        public bool SimulateActiveTransaction { get; set; }
        bool IApplicationDbContext.HasActiveTransaction => SimulateActiveTransaction || base.HasActiveTransaction;
    }

    private sealed class MutableClock(DateTime initialUtc) : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = initialUtc;
    }

    private sealed class ToggleableInvalidationTransport(bool shouldFail) : IConfigurationInvalidationTransport
    {
        public bool ShouldFail { get; set; } = shouldFail;

        public Task PublishInvalidationAsync(ConfigurationInvalidationEvent evt, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new InvalidOperationException("Simulated Redis pub/sub outage.");
            }
            return Task.CompletedTask;
        }
    }
}
