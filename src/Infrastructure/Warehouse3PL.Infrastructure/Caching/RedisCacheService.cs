using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private const int HardCapLocalFallbackSeconds = 30;
    private const int KeyGateStripes = 128;

    private readonly IDistributedCache? _distributedCache;
    private readonly IMemoryCache _memoryCache;
    private readonly CacheOptions _options;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly ConcurrentDictionary<string, long> _trackedLocalKeys = new(StringComparer.Ordinal);
    private readonly object[] _keyGates;
    private long _nextGeneration;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public RedisCacheService(
        IMemoryCache memoryCache,
        IOptions<CacheOptions> options,
        ILogger<RedisCacheService> logger,
        IDistributedCache? distributedCache = null)
    {
        _memoryCache = memoryCache;
        _options = options.Value;
        _logger = logger;
        _distributedCache = distributedCache;
        _keyGates = Enumerable.Range(0, KeyGateStripes).Select(_ => new object()).ToArray();
    }

    public int TrackedLocalKeyCount => _trackedLocalKeys.Count;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = $"{_options.InstanceName}{key}";

        if (_options.EnableRedis && _distributedCache != null)
        {
            try
            {
                var bytes = await _distributedCache.GetAsync(fullKey, cancellationToken);
                if (bytes != null && bytes.Length > 0)
                {
                    return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis cache GET failed for key {Key}. Falling back to local bounded L1 cache.", fullKey);
            }
        }

        return _memoryCache.TryGetValue(fullKey, out T? value) ? value : default;
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? slidingExpiration = null,
        TimeSpan? absoluteExpiration = null,
        CancellationToken cancellationToken = default)
    {
        var fullKey = $"{_options.InstanceName}{key}";
        var distributedAbsExp = absoluteExpiration ?? TimeSpan.FromMinutes(Math.Max(1, _options.DefaultExpirationMinutes));
        var maxLocalSeconds = Math.Clamp(_options.LocalFallbackMaxTtlSeconds, 1, HardCapLocalFallbackSeconds);
        var localCap = TimeSpan.FromSeconds(maxLocalSeconds);
        var localAbsExp = distributedAbsExp <= localCap ? distributedAbsExp : localCap;

        if (_options.EnableRedis && _distributedCache != null)
        {
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
                var maxEntryBytes = Math.Max(4096, _options.MaxCachedEntrySizeBytes);
                if (bytes.Length <= maxEntryBytes)
                {
                    var entryOptions = new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = distributedAbsExp,
                        SlidingExpiration = slidingExpiration
                    };

                    await _distributedCache.SetAsync(fullKey, bytes, entryOptions, cancellationToken);
                }
                else
                {
                    _logger.LogWarning(
                        "Skipping distributed Redis SET for key {Key}: serialized size {Size} bytes exceeds limit {Limit} bytes.",
                        fullKey, bytes.Length, maxEntryBytes);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis cache SET failed for key {Key}. Storing in bounded L1 cache.", fullKey);
            }
        }

        var memOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = localAbsExp,
            Size = 1
        };
        if (slidingExpiration.HasValue)
        {
            memOptions.SlidingExpiration = slidingExpiration.Value <= localCap ? slidingExpiration.Value : localCap;
        }

        var gate = GetKeyGate(fullKey);
        lock (gate)
        {
            long entryGeneration = Interlocked.Increment(ref _nextGeneration);
            _trackedLocalKeys[fullKey] = entryGeneration;

            memOptions.RegisterPostEvictionCallback((evictedKey, _, _, state) =>
            {
                if (evictedKey is string evictedStr && state is long evictedGen)
                {
                    // Generation-aware compare-and-remove prevents an evicted older entry's callback
                    // from deleting tracking for a newer replacement entry under concurrency.
                    _trackedLocalKeys.TryRemove(new KeyValuePair<string, long>(evictedStr, evictedGen));
                }
            }, entryGeneration);

            _memoryCache.Set(fullKey, value, memOptions);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = $"{_options.InstanceName}{key}";

        if (_options.EnableRedis && _distributedCache != null)
        {
            try
            {
                await _distributedCache.RemoveAsync(fullKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis cache REMOVE failed for key {Key}.", fullKey);
            }
        }

        RemoveLocalKeySynchronized(fullKey);
    }

    public async Task RemoveByPrefixAsync(string prefixKey, CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = $"{_options.InstanceName}{prefixKey}";
        var matched = _trackedLocalKeys
            .Where(kv => kv.Key.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var fullKey in matched)
        {
            RemoveLocalKeySynchronized(fullKey);

            if (_options.EnableRedis && _distributedCache != null)
            {
                try
                {
                    await _distributedCache.RemoveAsync(fullKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Redis prefix eviction failed for key {Key}.", fullKey);
                }
            }
        }

        _logger.LogInformation("Evicted {Count} cache entries matching prefix {Prefix}", matched.Count, prefixKey);
    }

    public Task RemoveLocalByPrefixAsync(string prefixKey, CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = $"{_options.InstanceName}{prefixKey}";
        var matched = _trackedLocalKeys
            .Where(kv => kv.Key.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var fullKey in matched)
        {
            RemoveLocalKeySynchronized(fullKey);
        }

        _logger.LogDebug("Evicted {Count} local L1 cache entries matching prefix {Prefix}", matched.Count, prefixKey);
        return Task.CompletedTask;
    }

    private void RemoveLocalKeySynchronized(string fullKey)
    {
        var gate = GetKeyGate(fullKey);
        lock (gate)
        {
            if (_trackedLocalKeys.TryGetValue(fullKey, out var currentGen))
            {
                _trackedLocalKeys.TryRemove(new KeyValuePair<string, long>(fullKey, currentGen));
            }

            _memoryCache.Remove(fullKey);
        }
    }

    private object GetKeyGate(string fullKey)
        => _keyGates[(uint)StringComparer.Ordinal.GetHashCode(fullKey) % KeyGateStripes];
}
