using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Caching;

/// <summary>
/// Subscribes to Redis pub/sub configuration invalidation events and evicts local L1 cache entries
/// on remote application instances. Redis pub/sub accelerates cache convergence, while DB epoch verification
/// remains the authoritative correctness guarantee even across Redis outages or duplicate notifications.
/// </summary>
public sealed class RedisConfigurationInvalidationSubscriber : BackgroundService
{
    public const string ChannelName = "nexus3pl:config-invalidations";

    private readonly ICacheService _cache;
    private readonly CacheOptions _options;
    private readonly ILogger<RedisConfigurationInvalidationSubscriber> _logger;
    private readonly IConnectionMultiplexer? _multiplexer;
    private long _receivedNotificationCount;

    public RedisConfigurationInvalidationSubscriber(
        ICacheService cache,
        IOptions<CacheOptions> options,
        ILogger<RedisConfigurationInvalidationSubscriber> logger,
        IConnectionMultiplexer? multiplexer = null)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _multiplexer = multiplexer;
    }

    public long ReceivedNotificationCount => Interlocked.Read(ref _receivedNotificationCount);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableRedis || _multiplexer == null)
        {
            return;
        }

        _multiplexer.ConnectionRestored += OnConnectionRestored;

        var subscriber = _multiplexer.GetSubscriber();
        var channel = RedisChannel.Literal(ChannelName);

        await subscriber.SubscribeAsync(channel, async (_, message) =>
        {
            if (message.IsNullOrEmpty)
            {
                return;
            }

            try
            {
                await HandleInvalidationPayloadAsync(message.ToString(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing Redis configuration invalidation notification.");
            }
        });

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _multiplexer.ConnectionRestored -= OnConnectionRestored;
            try
            {
                await subscriber.UnsubscribeAllAsync();
            }
            catch
            {
                // Ignore shutdown cleanup errors when multiplexer is closing
            }
        }
    }

    public async Task HandleInvalidationPayloadAsync(string payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ScopeType", out var scopeTypeEl) ||
            !root.TryGetProperty("ScopeKey", out var scopeKeyEl))
        {
            return;
        }

        var scopeTypeRaw = scopeTypeEl.GetString();
        var scopeKeyRaw = scopeKeyEl.GetString();
        if (string.IsNullOrWhiteSpace(scopeTypeRaw) ||
            string.IsNullOrWhiteSpace(scopeKeyRaw) ||
            !Enum.TryParse<ConfigurationScopeType>(scopeTypeRaw, ignoreCase: true, out var scopeType))
        {
            return;
        }

        var normScope = scopeKeyRaw.Trim().ToUpperInvariant();
        var prefix = $"mdm:cfg:{scopeType}:{normScope}:";

        // Idempotent local L1 prefix eviction: safe under out-of-order or duplicate notifications.
        await _cache.RemoveLocalByPrefixAsync(prefix, cancellationToken);
        Interlocked.Increment(ref _receivedNotificationCount);
    }

    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Flush local MDM L1 entries after Redis reconnection so notifications missed during outage cannot linger.
                await _cache.RemoveLocalByPrefixAsync("mdm:cfg:");
                _logger.LogInformation("Redis connection restored; flushed local MDM L1 cache entries.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to flush local MDM L1 cache entries upon Redis reconnect.");
            }
        });
    }
}
