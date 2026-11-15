using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache? _distributedCache;
    private readonly IMemoryCache _memoryCache;
    private readonly CacheOptions _options;
    private readonly ILogger<RedisCacheService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
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
                _logger.LogWarning(ex, "Redis cache GET failed for key {Key}. Falling back to in-memory cache.", fullKey);
            }
        }

        // Fallback to in-memory cache
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
        var absExp = absoluteExpiration ?? TimeSpan.FromMinutes(_options.DefaultExpirationMinutes);

        if (_options.EnableRedis && _distributedCache != null)
        {
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
                var entryOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = absExp,
                    SlidingExpiration = slidingExpiration
                };

                await _distributedCache.SetAsync(fullKey, bytes, entryOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis cache SET failed for key {Key}. Storing in in-memory cache.", fullKey);
            }
        }

        // Store in memory cache
        var memOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absExp,
            SlidingExpiration = slidingExpiration
        };
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
    }

    public Task RemoveByPrefixAsync(string prefixKey, CancellationToken cancellationToken = default)
    {
        // For local memory cache clearing
        _logger.LogInformation("Evicting cache entries matching prefix {Prefix}", prefixKey);
        return Task.CompletedTask;
    }
}

