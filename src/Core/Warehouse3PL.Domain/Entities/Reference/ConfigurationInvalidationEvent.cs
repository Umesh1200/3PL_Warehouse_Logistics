using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Reference;

/// <summary>
/// Transactional outbox record emitted inside the same database transaction as an MDM configuration change.
/// Processed asynchronously by the invalidation dispatcher with retryable delivery and zero false completion.
/// </summary>
public class ConfigurationInvalidationEvent
{
    public long Id { get; set; }
    public ConfigurationScopeType ScopeType { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public long NewEpoch { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DispatchedToRedisAtUtc { get; set; }
    public int DispatchAttempts { get; set; }
    public string? LastDispatchError { get; set; }

    public static ConfigurationInvalidationEvent Create(
        ConfigurationScopeType scopeType,
        string scopeKey,
        long newEpoch,
        string entityType,
        string entityId,
        DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            throw new DomainException("ScopeKey is required for ConfigurationInvalidationEvent.", "CONFIG_OUTBOX_INVALID_SCOPE");
        }

        if (newEpoch < 1)
        {
            throw new DomainException("NewEpoch must be >= 1.", "CONFIG_OUTBOX_INVALID_EPOCH");
        }

        return new ConfigurationInvalidationEvent
        {
            ScopeType = scopeType,
            ScopeKey = scopeKey.Trim().ToUpperInvariant(),
            NewEpoch = newEpoch,
            EntityType = string.IsNullOrWhiteSpace(entityType) ? "Unknown" : entityType.Trim(),
            EntityId = string.IsNullOrWhiteSpace(entityId) ? string.Empty : entityId.Trim(),
            CreatedAtUtc = createdAtUtc.Kind == DateTimeKind.Utc ? createdAtUtc : createdAtUtc.ToUniversalTime(),
            DispatchedToRedisAtUtc = null,
            DispatchAttempts = 0,
            LastDispatchError = null
        };
    }

    public void MarkDispatched(DateTime dispatchedAtUtc)
    {
        DispatchAttempts++;
        LastDispatchError = null;
        DispatchedToRedisAtUtc = dispatchedAtUtc.Kind == DateTimeKind.Utc
            ? dispatchedAtUtc
            : dispatchedAtUtc.ToUniversalTime();
    }

    public void RecordDispatchFailure(string errorMessage)
    {
        DispatchAttempts++;
        var trimmed = string.IsNullOrWhiteSpace(errorMessage) ? "Unknown dispatch error." : errorMessage.Trim();
        LastDispatchError = trimmed.Length > 500 ? trimmed[..500] : trimmed;
        // Never mark DispatchedToRedisAtUtc on failure (prevents false completion / lost invalidations).
    }
}
