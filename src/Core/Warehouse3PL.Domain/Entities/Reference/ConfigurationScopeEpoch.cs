using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// PostgreSQL-authoritative monotonic epoch counter per configuration scope.
/// Every committed MDM configuration mutation atomically increments the affected scope epoch(s)
/// within the same database transaction so remote instances detect stale caches without relying on Redis pub/sub.
/// </summary>
public class ConfigurationScopeEpoch : BaseEntity<Guid>
{
    public ConfigurationScopeType ScopeType { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public long CurrentEpoch { get; set; } = 1;
    public DateTime UpdatedAtUtc { get; set; }

    public ConfigurationScopeEpoch()
    {
        Id = Guid.NewGuid();
    }

    public static ConfigurationScopeEpoch Create(
        ConfigurationScopeType scopeType,
        string scopeKey,
        DateTime updatedAtUtc,
        long initialEpoch = 1)
    {
        if (string.IsNullOrWhiteSpace(scopeKey) || scopeKey.Trim().Length > 120)
        {
            throw new DomainException("ScopeKey is required and must not exceed 120 characters.", "CONFIG_EPOCH_INVALID_SCOPE_KEY");
        }

        if (initialEpoch < 1)
        {
            throw new DomainException("CurrentEpoch must be >= 1.", "CONFIG_EPOCH_INVALID_VALUE");
        }

        return new ConfigurationScopeEpoch
        {
            ScopeType = scopeType,
            ScopeKey = scopeKey.Trim().ToUpperInvariant(),
            CurrentEpoch = initialEpoch,
            UpdatedAtUtc = updatedAtUtc.Kind == DateTimeKind.Utc ? updatedAtUtc : updatedAtUtc.ToUniversalTime()
        };
    }

    public long Increment(DateTime updatedAtUtc)
    {
        CurrentEpoch++;
        UpdatedAtUtc = updatedAtUtc.Kind == DateTimeKind.Utc ? updatedAtUtc : updatedAtUtc.ToUniversalTime();
        return CurrentEpoch;
    }
}
