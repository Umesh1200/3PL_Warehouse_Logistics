using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Infrastructure.Caching;

public sealed class RedisConfigurationInvalidationTransport : IConfigurationInvalidationTransport
{
    private const string ChannelName = RedisConfigurationInvalidationSubscriber.ChannelName;

    private readonly ICacheService _cache;
    private readonly CacheOptions _options;
    private readonly ILogger<RedisConfigurationInvalidationTransport> _logger;
    private readonly IConnectionMultiplexer? _multiplexer;

    public RedisConfigurationInvalidationTransport(
        ICacheService cache,
        IOptions<CacheOptions> options,
        ILogger<RedisConfigurationInvalidationTransport> logger,
        IConnectionMultiplexer? multiplexer = null)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _multiplexer = multiplexer;
    }

    public async Task PublishInvalidationAsync(
        ConfigurationInvalidationEvent evt,
        CancellationToken cancellationToken = default)
    {
        var prefix = $"mdm:cfg:{evt.ScopeType}:{evt.ScopeKey}:";
        await _cache.RemoveByPrefixAsync(prefix, cancellationToken);

        if (_options.EnableRedis && !string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            if (_multiplexer == null || !_multiplexer.IsConnected)
            {
                throw new InvalidOperationException(
                    $"Redis invalidation publish failed for event {evt.Id} ({evt.ScopeType}:{evt.ScopeKey}): Redis connection unavailable.");
            }

            var payload = JsonSerializer.Serialize(new
            {
                evt.Id,
                ScopeType = evt.ScopeType.ToString(),
                evt.ScopeKey,
                evt.NewEpoch,
                evt.EntityType,
                evt.EntityId
            });

            var sub = _multiplexer.GetSubscriber();
            await sub.PublishAsync(RedisChannel.Literal(ChannelName), payload);
        }

        _logger.LogDebug(
            "Published configuration invalidation event {EventId} for {ScopeType}:{ScopeKey} epoch {Epoch}.",
            evt.Id, evt.ScopeType, evt.ScopeKey, evt.NewEpoch);
    }
}
