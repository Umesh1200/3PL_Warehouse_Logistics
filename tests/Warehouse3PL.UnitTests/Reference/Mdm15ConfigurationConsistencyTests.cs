using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        pV1.Supersede(Guid.NewGuid(), effFrom);
        var pV2 = CountryCompliancePolicy.CreateDraft(
            "NP", "NP-M15-RULE", 2, "Policy V2", "Nepal Gazette 2083 Rev2", "Notice-V2",
            """{"RequiredBusinessClassifications":["WHOLESALE","MICRO_RETAIL"]}""",
            ComplianceEnforcementMode.MandatoryFailClosed, effFrom, null);
        pV2.Approve(approverId, effFrom.AddDays(1), "Verified V2 mandate update");
        dbA.CountryCompliancePolicies.Add(pV2);

        fxV1.Supersede(Guid.NewGuid(), effFrom.AddDays(1));
        var fxV2 = FxRateRecord.CreateDraft(
            "USD", "NPR", effFrom, null, 1, 135.5m, 136.5m, 136.0m,
            FxRateSourceAuthority.ManualApproved, "BOARD-V2", effFrom.AddDays(1), evidenceReference: "BOARD_MINUTES_V2");
        fxV2.Approve(approverId, effFrom.AddDays(1), "Approved V2 USD/NPR", evidenceReference: "BOARD_MINUTES_V2");
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

        // 3. Dispatcher retryable delivery without false completion
        var transport = new ToggleableInvalidationTransport(shouldFail: true);
        var processor = new ConfigurationInvalidationProcessor(
            dbA, transport, new DateTimeProvider(), NullLogger<ConfigurationInvalidationProcessor>.Instance);

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
            });

        transport.ShouldFail = false;
        var okSweep = await processor.DispatchPendingAsync();
        Assert.Equal(0, okSweep.Failed);
        Assert.Equal(failSweep.Attempted, okSweep.Dispatched);
        Assert.All(
            await dbA.ConfigurationInvalidationEvents.AsNoTracking().ToListAsync(),
            e =>
            {
                Assert.NotNull(e.DispatchedToRedisAtUtc);
                Assert.Null(e.LastDispatchError);
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
