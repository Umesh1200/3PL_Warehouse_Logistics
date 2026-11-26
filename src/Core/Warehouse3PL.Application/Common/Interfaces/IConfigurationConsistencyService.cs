using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public sealed record ConfigurationReadDiagnostics<T>(
    T Value,
    long AuthoritativeEpoch,
    bool ServedFromCache,
    bool StaleCacheEvicted,
    string ScopedCacheKey);

/// <summary>
/// DB-authoritative configuration consistency resolver.
/// Always validates cached entries against PostgreSQL <see cref="ConfigurationScopeEpoch"/> before serving critical reads,
/// partitions keys strictly by scope, tenant, facility, and epoch, and fails closed if verification fails.
/// </summary>
public interface IConfigurationConsistencyService
{
    string BuildScopedCacheKey(
        ConfigurationScopeType scopeType,
        string scopeKey,
        string bundleName,
        long epoch,
        Guid? tenantId = null,
        Guid? facilityId = null);

    Task<long> GetAuthoritativeEpochAsync(
        ConfigurationScopeType scopeType,
        string scopeKey,
        CancellationToken cancellationToken = default);

    Task<T> GetOrLoadVerifiedAsync<T>(
        ConfigurationScopeType scopeType,
        string scopeKey,
        string bundleName,
        Func<IApplicationDbContext, CancellationToken, Task<T>> loader,
        Guid? tenantId = null,
        Guid? facilityId = null,
        CancellationToken cancellationToken = default);

    Task<ConfigurationReadDiagnostics<T>> GetOrLoadVerifiedWithDiagnosticsAsync<T>(
        ConfigurationScopeType scopeType,
        string scopeKey,
        string bundleName,
        Func<IApplicationDbContext, CancellationToken, Task<T>> loader,
        Guid? tenantId = null,
        Guid? facilityId = null,
        CancellationToken cancellationToken = default);

    Task<LegalEntity?> GetVerifiedLegalEntityAsync(
        Guid legalEntityId,
        CancellationToken cancellationToken = default);
}
