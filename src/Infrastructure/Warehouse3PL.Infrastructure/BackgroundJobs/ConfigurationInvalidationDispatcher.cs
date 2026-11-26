using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Infrastructure.Caching;
using Warehouse3PL.Infrastructure.Persistence;

namespace Warehouse3PL.Infrastructure.BackgroundJobs;

public sealed record ConfigurationDispatchSweepResult(
    int Attempted,
    int Dispatched,
    int Failed,
    int DeferredByBackoff = 0,
    int PurgedExpired = 0);

public sealed record ConfigurationOutboxDiagnostics(
    int PendingCount,
    int ReadyForRetryCount,
    int FailedRetryCount,
    int DispatchedCount,
    DateTime? OldestPendingCreatedAtUtc);

/// <summary>
/// Outbox processor delivering pending <see cref="ConfigurationInvalidationEvent"/> entries
/// with exponential retry backoff, zero false completion, bounded dispatched retention pruning,
/// and operational diagnostics.
/// </summary>
public sealed class ConfigurationInvalidationProcessor
{
    private readonly ApplicationDbContext _db;
    private readonly IConfigurationInvalidationTransport _transport;
    private readonly IDateTimeProvider _dateTime;
    private readonly CacheOptions _cacheOptions;
    private readonly ILogger<ConfigurationInvalidationProcessor> _logger;

    public ConfigurationInvalidationProcessor(
        ApplicationDbContext db,
        IConfigurationInvalidationTransport transport,
        IDateTimeProvider dateTime,
        ILogger<ConfigurationInvalidationProcessor> logger,
        IOptions<CacheOptions>? cacheOptions = null)
    {
        _db = db;
        _transport = transport;
        _dateTime = dateTime;
        _logger = logger;
        _cacheOptions = cacheOptions?.Value ?? new CacheOptions();
    }

    public async Task<ConfigurationDispatchSweepResult> DispatchPendingAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var now = _dateTime.UtcNow;

        if (_db.Database.IsRelational())
        {
            return await _db.ExecuteInTransactionAsync(async () =>
            {
                var deferredCount = await _db.ConfigurationInvalidationEvents
                    .AsNoTracking()
                    .CountAsync(e => e.DispatchedToRedisAtUtc == null &&
                                     e.NextRetryAtUtc != null &&
                                     e.NextRetryAtUtc > now, cancellationToken);

                var pending = await _db.ConfigurationInvalidationEvents
                    .FromSqlInterpolated(
                        $"SELECT * FROM configuration_invalidation_events WHERE dispatched_to_redis_at_utc IS NULL AND (next_retry_at_utc IS NULL OR next_retry_at_utc <= {now}) ORDER BY event_id FOR UPDATE SKIP LOCKED LIMIT {batchSize}")
                    .ToListAsync(cancellationToken);

                var sweep = await ProcessBatchAsync(pending, now, cancellationToken);
                var purged = await PurgeDispatchedRetentionAsync(now, cancellationToken);
                return sweep with { DeferredByBackoff = deferredCount, PurgedExpired = purged };
            }, cancellationToken);
        }

        var inMemDeferred = await _db.ConfigurationInvalidationEvents
            .AsNoTracking()
            .CountAsync(e => e.DispatchedToRedisAtUtc == null &&
                             e.NextRetryAtUtc != null &&
                             e.NextRetryAtUtc > now, cancellationToken);

        var inMemPending = await _db.ConfigurationInvalidationEvents
            .Where(e => e.DispatchedToRedisAtUtc == null &&
                        (e.NextRetryAtUtc == null || e.NextRetryAtUtc <= now))
            .OrderBy(e => e.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var res = await ProcessBatchAsync(inMemPending, now, cancellationToken);
        var inMemPurged = await PurgeDispatchedRetentionAsync(now, cancellationToken);
        return res with { DeferredByBackoff = inMemDeferred, PurgedExpired = inMemPurged };
    }

    public async Task<ConfigurationOutboxDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var now = _dateTime.UtcNow;
        var pendingCount = await _db.ConfigurationInvalidationEvents
            .AsNoTracking()
            .CountAsync(e => e.DispatchedToRedisAtUtc == null, cancellationToken);
        var readyCount = await _db.ConfigurationInvalidationEvents
            .AsNoTracking()
            .CountAsync(e => e.DispatchedToRedisAtUtc == null && (e.NextRetryAtUtc == null || e.NextRetryAtUtc <= now), cancellationToken);
        var failedRetryCount = await _db.ConfigurationInvalidationEvents
            .AsNoTracking()
            .CountAsync(e => e.DispatchedToRedisAtUtc == null && e.DispatchAttempts > 0, cancellationToken);
        var dispatchedCount = await _db.ConfigurationInvalidationEvents
            .AsNoTracking()
            .CountAsync(e => e.DispatchedToRedisAtUtc != null, cancellationToken);
        var oldestPending = await _db.ConfigurationInvalidationEvents
            .AsNoTracking()
            .Where(e => e.DispatchedToRedisAtUtc == null)
            .OrderBy(e => e.Id)
            .Select(e => (DateTime?)e.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new ConfigurationOutboxDiagnostics(
            pendingCount,
            readyCount,
            failedRetryCount,
            dispatchedCount,
            oldestPending);
    }

    private async Task<ConfigurationDispatchSweepResult> ProcessBatchAsync(
        IReadOnlyList<ConfigurationInvalidationEvent> batch,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return new ConfigurationDispatchSweepResult(0, 0, 0);
        }

        int dispatched = 0;
        int failed = 0;

        foreach (var evt in batch)
        {
            try
            {
                await _transport.PublishInvalidationAsync(evt, cancellationToken);
                evt.MarkDispatched(now);
                dispatched++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                evt.RecordDispatchFailure(ex.Message, now);
                _logger.LogWarning(
                    ex,
                    "Failed to dispatch ConfigurationInvalidationEvent {EventId} ({ScopeType}:{ScopeKey}, attempts={Attempts}, nextRetry={NextRetry:O}); remaining undispatched for retry.",
                    evt.Id, evt.ScopeType, evt.ScopeKey, evt.DispatchAttempts, evt.NextRetryAtUtc);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new ConfigurationDispatchSweepResult(batch.Count, dispatched, failed);
    }

    private async Task<int> PurgeDispatchedRetentionAsync(DateTime now, CancellationToken cancellationToken)
    {
        var retentionDays = Math.Max(1, _cacheOptions.OutboxDispatchedRetentionDays);
        var cutoff = now.AddDays(-retentionDays);

        if (_db.Database.IsRelational())
        {
            return await _db.ConfigurationInvalidationEvents
                .Where(e => e.DispatchedToRedisAtUtc != null && e.DispatchedToRedisAtUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var expired = await _db.ConfigurationInvalidationEvents
            .Where(e => e.DispatchedToRedisAtUtc != null && e.DispatchedToRedisAtUtc < cutoff)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0) return 0;

        _db.ConfigurationInvalidationEvents.RemoveRange(expired);
        await _db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}

public sealed class ConfigurationInvalidationDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ConfigurationInvalidationDispatcher> _logger;

    public ConfigurationInvalidationDispatcher(
        IServiceScopeFactory scopes,
        ILogger<ConfigurationInvalidationDispatcher> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ConfigurationInvalidationProcessor>();
                await processor.DispatchPendingAsync(cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConfigurationInvalidationDispatcher sweep failed; pending events preserved in outbox.");
            }
        }
    }
}
