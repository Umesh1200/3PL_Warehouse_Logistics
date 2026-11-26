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

    private readonly IDistributedCache? _distributedCache;
    private readonly IMemoryCache _memoryCache;
    private readonly CacheOptions _options;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly ConcurrentDictionary<string, byte> _trackedLocalKeys = new(StringComparer.Ordinal);

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
    }

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
                var entryOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = distributedAbsExp,
                    SlidingExpiration = slidingExpiration
                };

                await _distributedCache.SetAsync(fullKey, bytes, entryOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis cache SET failed for key {Key}. Storing in bounded L1 cache.", fullKey);
            }
        }

        var memOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = localAbsExp
        };
        if (slidingExpiration.HasValue)
        {
            memOptions.SlidingExpiration = slidingExpiration.Value <= localCap ? slidingExpiration.Value : localCap;
        }

        memOptions.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
        {
            if (evictedKey is string s)
            {
                _trackedLocalKeys.TryRemove(s, out _);
            }
        });

        _trackedLocalKeys.TryAdd(fullKey, 0);
        _memoryCache.Set(fullKey, value, memOptions);
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

        _memoryCache.Remove(fullKey);
        _trackedLocalKeys.TryRemove(fullKey, out _);
    }

    public async Task RemoveByPrefixAsync(string prefixKey, CancellationToken cancellationToken = default)
    {
        var normalizedPrefix = $"{_options.InstanceName}{prefixKey}";
        var matchedKeys = _trackedLocalKeys.Keys
            .Where(k => k.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            .ToList();

        foreach (var fullKey in matchedKeys)
        {
            _memoryCache.Remove(fullKey);
            _trackedLocalKeys.TryRemove(fullKey, out _);

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

        _logger.LogInformation("Evicted {Count} cache entries matching prefix {Prefix}", matchedKeys.Count, prefixKey);
    }
}
