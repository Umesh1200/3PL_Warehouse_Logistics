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
    private const int MaxBackoffSeconds = 30;

    private readonly ICacheService _cache;
    private readonly CacheOptions _options;
    private readonly ILogger<RedisConfigurationInvalidationSubscriber> _logger;
    private readonly IConnectionMultiplexer? _multiplexer;
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

    private long _receivedNotificationCount;
    private long _reconnectFlushCount;
    private bool _isSubscribed;
    private bool _needsReconnectFlush;
    private bool _hadSubscriptionAttemptFailureOrDisconnect;
    private bool _handlersAttached;
    private ChannelMessageQueue? _channelQueue;
    private int _disposed;

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
    public long ReconnectFlushCount => Interlocked.Read(ref _reconnectFlushCount);
    public bool IsSubscribed => Volatile.Read(ref _isSubscribed);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableRedis || _multiplexer == null)
        {
            return;
        }

        var channel = RedisChannel.Literal(ChannelName);

        _multiplexer.ConnectionRestored += OnConnectionRestored;
        _multiplexer.ConnectionFailed += OnConnectionFailed;
        _handlersAttached = true;

        int failureAttempt = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool ready = false;
                try
                {
                    ready = await EnsureSubscribedAsync(channel, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref _hadSubscriptionAttemptFailureOrDisconnect, true);
                    Volatile.Write(ref _isSubscribed, false);
                    _logger.LogWarning(
                        ex,
                        "Redis configuration invalidation subscription attempt failed; continuing with PostgreSQL-authoritative reads.");
                }

                if (ready)
                {
                    failureAttempt = 0;
                    await _signal.WaitAsync(stoppingToken);
                }
                else
                {
                    Volatile.Write(ref _hadSubscriptionAttemptFailureOrDisconnect, true);
                    int backoffSeconds = Math.Min(MaxBackoffSeconds, 1 << Math.Min(failureAttempt, 5));
                    failureAttempt++;
                    await _signal.WaitAsync(TimeSpan.FromSeconds(backoffSeconds), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_handlersAttached)
            {
                _multiplexer.ConnectionRestored -= OnConnectionRestored;
                _multiplexer.ConnectionFailed -= OnConnectionFailed;
                _handlersAttached = false;
            }

            await UnsubscribeSafeAsync();
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

    private async Task<bool> EnsureSubscribedAsync(RedisChannel channel, CancellationToken stoppingToken)
    {
        if (_multiplexer == null || !_multiplexer.IsConnected)
        {
            return false;
        }

        await _subscriptionLock.WaitAsync(stoppingToken);
        try
        {
            if (!_multiplexer.IsConnected)
            {
                return false;
            }

            bool needsFlush = Volatile.Read(ref _needsReconnectFlush) ||
                              Volatile.Read(ref _hadSubscriptionAttemptFailureOrDisconnect);

            if (!Volatile.Read(ref _isSubscribed) || _channelQueue == null || needsFlush)
            {
                // Tear down any previous or partially-registered queue instance BEFORE subscribing cleanly.
                // Using an isolated ChannelMessageQueue guarantees zero duplicate delegate registrations across retries/reconnects.
                if (_channelQueue != null)
                {
                    try
                    {
                        await _channelQueue.UnsubscribeAsync();
                    }
                    catch
                    {
                        // Ignore stale queue cleanup errors during reconnect
                    }

                    _channelQueue = null;
                    Volatile.Write(ref _isSubscribed, false);
                }

                var subscriber = _multiplexer.GetSubscriber();
                var queue = await subscriber.SubscribeAsync(channel);
                queue.OnMessage(async msg =>
                {
                    if (!msg.Message.IsNullOrEmpty && !stoppingToken.IsCancellationRequested)
                    {
                        await ProcessNotificationSafeAsync(msg.Message.ToString(), stoppingToken);
                    }
                });

                _channelQueue = queue;
                Volatile.Write(ref _isSubscribed, true);

                if (needsFlush)
                {
                    await _cache.RemoveLocalByPrefixAsync("mdm:cfg:", stoppingToken);
                    Volatile.Write(ref _needsReconnectFlush, false);
                    Volatile.Write(ref _hadSubscriptionAttemptFailureOrDisconnect, false);
                    Interlocked.Increment(ref _reconnectFlushCount);
                    _logger.LogInformation("Redis invalidation subscription active after recovery; flushed local MDM L1 cache entries.");
                }
            }

            return true;
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    private async Task ProcessNotificationSafeAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            await HandleInvalidationPayloadAsync(payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing Redis configuration invalidation notification.");
        }
    }

    private async Task UnsubscribeSafeAsync()
    {
        try
        {
            await _subscriptionLock.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            return;
        }

        try
        {
            if (_channelQueue != null)
            {
                await _channelQueue.UnsubscribeAsync();
                _channelQueue = null;
            }
        }
        catch
        {
            // Ignore shutdown cleanup errors when multiplexer is closing
        }
        finally
        {
            Volatile.Write(ref _isSubscribed, false);
            _subscriptionLock.Release();
        }
    }

    private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        Volatile.Write(ref _hadSubscriptionAttemptFailureOrDisconnect, true);
        Volatile.Write(ref _needsReconnectFlush, true);
    }

    private void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        Volatile.Write(ref _needsReconnectFlush, true);
        ReleaseSignalSafe();
    }

    private void ReleaseSignalSafe()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (_handlersAttached && _multiplexer != null)
            {
                _multiplexer.ConnectionRestored -= OnConnectionRestored;
                _multiplexer.ConnectionFailed -= OnConnectionFailed;
                _handlersAttached = false;
            }

            _signal.Dispose();
            _subscriptionLock.Dispose();
        }

        base.Dispose();
    }
}
