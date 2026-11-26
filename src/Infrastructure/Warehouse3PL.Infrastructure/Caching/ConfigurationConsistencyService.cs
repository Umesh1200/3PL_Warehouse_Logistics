using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Infrastructure.Caching;

public sealed class CachedConfigurationEnvelope<T>
{
    public ConfigurationScopeType ScopeType { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public long Epoch { get; set; }
    public string TenantPartition { get; set; } = string.Empty;
    public string FacilityPartition { get; set; } = string.Empty;
    public DateTime CachedAtUtc { get; set; }
    public T Value { get; set; } = default!;
}

public sealed class ConfigurationConsistencyService : IConfigurationConsistencyService
{
    private const int MaxEpochVerificationAttempts = 5;

    private readonly IApplicationDbContext _db;
    private readonly ICacheService _cache;
    private readonly IDateTimeProvider _dateTime;
    private readonly ICurrentUserContext? _currentUser;
    private readonly ILogger<ConfigurationConsistencyService> _logger;

    public ConfigurationConsistencyService(
        IApplicationDbContext db,
        ICacheService cache,
        IDateTimeProvider dateTime,
        ILogger<ConfigurationConsistencyService> logger,
        ICurrentUserContext? currentUser = null)
    {
        _db = db;
        _cache = cache;
        _dateTime = dateTime;
        _logger = logger;
        _currentUser = currentUser;
    }

    public string BuildScopedCacheKey(
        ConfigurationScopeType scopeType,
        string scopeKey,
        string bundleName,
        long epoch,
        Guid? tenantId = null,
        Guid? facilityId = null)
    {
        var normScope = NormalizeScopeKey(scopeKey);
        var (tenantPart, facilityPart) = ResolvePartitions(tenantId, facilityId);
        var normBundle = (bundleName ?? "default").Trim();
        return $"mdm:cfg:{scopeType}:{normScope}:tenant={tenantPart}:facility={facilityPart}:epoch={epoch}:{normBundle}";
    }

    public async Task<long> GetAuthoritativeEpochAsync(
        ConfigurationScopeType scopeType,
        string scopeKey,
        CancellationToken cancellationToken = default)
    {
        var normScope = NormalizeScopeKey(scopeKey);
        try
        {
            var epoch = await _db.ConfigurationScopeEpochs
                .AsNoTracking()
                .Where(e => e.ScopeType == scopeType && e.ScopeKey == normScope)
                .Select(e => (long?)e.CurrentEpoch)
                .FirstOrDefaultAsync(cancellationToken);

            return epoch ?? 0L;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Authoritative configuration epoch check failed for {ScopeType}:{ScopeKey}; failing closed.",
                scopeType,
                normScope);
            throw new DomainException(
                $"Authoritative configuration epoch verification failed for scope '{scopeType}:{normScope}'. Refusing potentially stale cached configuration.",
                ex,
                "CONFIG_EPOCH_VERIFICATION_UNAVAILABLE");
        }
    }

    public async Task<T> GetOrLoadVerifiedAsync<T>(
        ConfigurationScopeType scopeType,
        string scopeKey,
        string bundleName,
        Func<IApplicationDbContext, CancellationToken, Task<T>> loader,
        Guid? tenantId = null,
        Guid? facilityId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await GetOrLoadVerifiedWithDiagnosticsAsync(
            scopeType, scopeKey, bundleName, loader, tenantId, facilityId, cancellationToken);
        return result.Value;
    }

    public async Task<ConfigurationReadDiagnostics<T>> GetOrLoadVerifiedWithDiagnosticsAsync<T>(
        ConfigurationScopeType scopeType,
        string scopeKey,
        string bundleName,
        Func<IApplicationDbContext, CancellationToken, Task<T>> loader,
        Guid? tenantId = null,
        Guid? facilityId = null,
        CancellationToken cancellationToken = default)
    {
        var normScope = NormalizeScopeKey(scopeKey);

        // MDM15-R02: Inside an active database transaction, bypass shared L1/L2 caches completely
        // (neither reading nor publishing) so uncommitted epochs/values cannot leak across transactions or survive rollback.
        bool hasActiveTx;
        try
        {
            hasActiveTx = _db.HasActiveTransaction;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database transaction-state check failed for {ScopeType}:{ScopeKey}; failing closed.", scopeType, normScope);
            throw new DomainException(
                $"Authoritative configuration verification unavailable for scope '{scopeType}:{normScope}'.",
                ex,
                "CONFIG_EPOCH_VERIFICATION_UNAVAILABLE");
        }

        if (hasActiveTx)
        {
            long txEpoch = await GetAuthoritativeEpochAsync(scopeType, normScope, cancellationToken);
            T txValue = await loader(_db, cancellationToken);
            return new ConfigurationReadDiagnostics<T>(
                txValue,
                txEpoch,
                ServedFromCache: false,
                StaleCacheEvicted: false,
                ScopedCacheKey: string.Empty);
        }

        var (tenantPart, facilityPart) = ResolvePartitions(tenantId, facilityId);

        // 1. Always verify PostgreSQL authoritative epoch before trusting any cached bundle.
        long authoritativeEpoch = await GetAuthoritativeEpochAsync(scopeType, normScope, cancellationToken);

        var scopedKey = BuildScopedCacheKey(scopeType, normScope, bundleName, authoritativeEpoch, tenantId, facilityId);
        var pointerKey = $"mdm:cfg:{scopeType}:{normScope}:tenant={tenantPart}:facility={facilityPart}:latest:{bundleName.Trim()}";
        bool staleEvicted = false;

        // 2. Check cached latest-seen epoch pointer for immediate stale-cache detection even when Redis pub/sub is suppressed.
        var latestEnvelope = await _cache.GetAsync<CachedConfigurationEnvelope<T>>(pointerKey, cancellationToken);
        if (latestEnvelope != null)
        {
            bool matchesEpoch = latestEnvelope.Epoch == authoritativeEpoch;
            bool matchesPartition =
                latestEnvelope.ScopeType == scopeType &&
                string.Equals(latestEnvelope.ScopeKey, normScope, StringComparison.Ordinal) &&
                string.Equals(latestEnvelope.TenantPartition, tenantPart, StringComparison.Ordinal) &&
                string.Equals(latestEnvelope.FacilityPartition, facilityPart, StringComparison.Ordinal);

            if (matchesEpoch && matchesPartition)
            {
                return new ConfigurationReadDiagnostics<T>(
                    latestEnvelope.Value,
                    authoritativeEpoch,
                    ServedFromCache: true,
                    StaleCacheEvicted: false,
                    scopedKey);
            }

            // Stale epoch or partition detected without relying on pub/sub: evict scope prefix cleanly.
            staleEvicted = true;
            await _cache.RemoveByPrefixAsync($"mdm:cfg:{scopeType}:{normScope}:", cancellationToken);
        }

        // 3. Also check epoch-stamped key directly in case pointer was evicted independently.
        if (!staleEvicted)
        {
            var epochStamped = await _cache.GetAsync<CachedConfigurationEnvelope<T>>(scopedKey, cancellationToken);
            if (epochStamped != null &&
                epochStamped.Epoch == authoritativeEpoch &&
                epochStamped.ScopeType == scopeType &&
                string.Equals(epochStamped.ScopeKey, normScope, StringComparison.Ordinal) &&
                string.Equals(epochStamped.TenantPartition, tenantPart, StringComparison.Ordinal) &&
                string.Equals(epochStamped.FacilityPartition, facilityPart, StringComparison.Ordinal))
            {
                await _cache.SetAsync(pointerKey, epochStamped, absoluteExpiration: TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);
                return new ConfigurationReadDiagnostics<T>(
                    epochStamped.Value,
                    authoritativeEpoch,
                    ServedFromCache: true,
                    StaleCacheEvicted: false,
                    scopedKey);
            }
        }

        // 4. MDM15-R03: Bounded epoch verification loop.
        // Every loaded configuration snapshot must be confirmed consistent between pre-load and post-load epochs
        // BEFORE publishing to cache; if concurrent mutations keep shifting the epoch past MaxEpochVerificationAttempts,
        // fail closed without caching an unverified snapshot.
        for (int attempt = 1; attempt <= MaxEpochVerificationAttempts; attempt++)
        {
            long preLoadEpoch = await GetAuthoritativeEpochAsync(scopeType, normScope, cancellationToken);
            T freshValue = await loader(_db, cancellationToken);
            long postLoadEpoch = await GetAuthoritativeEpochAsync(scopeType, normScope, cancellationToken);

            if (preLoadEpoch == postLoadEpoch)
            {
                scopedKey = BuildScopedCacheKey(scopeType, normScope, bundleName, postLoadEpoch, tenantId, facilityId);

                var newEnvelope = new CachedConfigurationEnvelope<T>
                {
                    ScopeType = scopeType,
                    ScopeKey = normScope,
                    Epoch = postLoadEpoch,
                    TenantPartition = tenantPart,
                    FacilityPartition = facilityPart,
                    CachedAtUtc = _dateTime.UtcNow,
                    Value = freshValue
                };

                var cappedTtl = TimeSpan.FromSeconds(30);
                await _cache.SetAsync(scopedKey, newEnvelope, absoluteExpiration: cappedTtl, cancellationToken: cancellationToken);
                await _cache.SetAsync(pointerKey, newEnvelope, absoluteExpiration: cappedTtl, cancellationToken: cancellationToken);

                return new ConfigurationReadDiagnostics<T>(
                    freshValue,
                    postLoadEpoch,
                    ServedFromCache: false,
                    StaleCacheEvicted: staleEvicted,
                    scopedKey);
            }

            _logger.LogWarning(
                "Configuration epoch shifted mid-load for {ScopeType}:{ScopeKey} ({PreEpoch} -> {PostEpoch}, attempt {Attempt}/{Max}). Retrying.",
                scopeType, normScope, preLoadEpoch, postLoadEpoch, attempt, MaxEpochVerificationAttempts);
        }

        throw new DomainException(
            $"Configuration scope '{scopeType}:{normScope}' experienced concurrent epoch changes across {MaxEpochVerificationAttempts} verification attempts; refusing to publish or serve an unverified configuration snapshot.",
            "CONFIG_EPOCH_CONTENTION_EXCEEDED");
    }

    public Task<LegalEntity?> GetVerifiedLegalEntityAsync(
        Guid legalEntityId,
        CancellationToken cancellationToken = default)
    {
        return GetOrLoadVerifiedAsync(
            ConfigurationScopeType.LegalEntity,
            legalEntityId.ToString("D"),
            "legal-entity-by-id",
            (db, ct) => db.LegalEntities.AsNoTracking().FirstOrDefaultAsync(le => le.Id == legalEntityId, ct),
            cancellationToken: cancellationToken);
    }

    private (string TenantPart, string FacilityPart) ResolvePartitions(Guid? explicitTenantId, Guid? explicitFacilityId)
    {
        var effTenant = explicitTenantId ?? _currentUser?.TenantId;
        var effFacility = explicitFacilityId ?? _currentUser?.FacilityId;
        var tenantPart = effTenant.HasValue && effTenant.Value != Guid.Empty ? effTenant.Value.ToString("D") : "global";
        var facilityPart = effFacility.HasValue && effFacility.Value != Guid.Empty ? effFacility.Value.ToString("D") : "global";
        return (tenantPart, facilityPart);
    }

    private static string NormalizeScopeKey(string scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKey)) return "GLOBAL";
        var norm = scopeKey.Trim().ToUpperInvariant();
        return norm.Length > 120 ? norm[..120] : norm;
    }
}
