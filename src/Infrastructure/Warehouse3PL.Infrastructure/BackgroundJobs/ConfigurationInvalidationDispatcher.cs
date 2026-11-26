using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Infrastructure.Persistence;

namespace Warehouse3PL.Infrastructure.BackgroundJobs;

public sealed record ConfigurationDispatchSweepResult(int Attempted, int Dispatched, int Failed);

/// <summary>
/// Outbox processor delivering pending <see cref="ConfigurationInvalidationEvent"/> entries
/// with retryable delivery and zero false completion when transport fails.
/// </summary>
public sealed class ConfigurationInvalidationProcessor
{
    private readonly ApplicationDbContext _db;
    private readonly IConfigurationInvalidationTransport _transport;
    private readonly IDateTimeProvider _dateTime;
    private readonly ILogger<ConfigurationInvalidationProcessor> _logger;

    public ConfigurationInvalidationProcessor(
        ApplicationDbContext db,
        IConfigurationInvalidationTransport transport,
        IDateTimeProvider dateTime,
        ILogger<ConfigurationInvalidationProcessor> logger)
    {
        _db = db;
        _transport = transport;
        _dateTime = dateTime;
        _logger = logger;
    }

    public async Task<ConfigurationDispatchSweepResult> DispatchPendingAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (_db.Database.IsRelational())
        {
            return await _db.ExecuteInTransactionAsync(async () =>
            {
                var pending = await _db.ConfigurationInvalidationEvents
                    .FromSqlInterpolated(
                        $"SELECT * FROM configuration_invalidation_events WHERE dispatched_to_redis_at_utc IS NULL ORDER BY event_id FOR UPDATE SKIP LOCKED LIMIT {batchSize}")
                    .ToListAsync(cancellationToken);

                return await ProcessBatchAsync(pending, cancellationToken);
            }, cancellationToken);
        }

        var inMemPending = await _db.ConfigurationInvalidationEvents
            .Where(e => e.DispatchedToRedisAtUtc == null)
            .OrderBy(e => e.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return await ProcessBatchAsync(inMemPending, cancellationToken);
    }

    private async Task<ConfigurationDispatchSweepResult> ProcessBatchAsync(
        IReadOnlyList<ConfigurationInvalidationEvent> batch,
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
                evt.MarkDispatched(_dateTime.UtcNow);
                dispatched++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                evt.RecordDispatchFailure(ex.Message);
                _logger.LogWarning(
                    ex,
                    "Failed to dispatch ConfigurationInvalidationEvent {EventId} ({ScopeType}:{ScopeKey}); remaining undispatched for retry.",
                    evt.Id, evt.ScopeType, evt.ScopeKey);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new ConfigurationDispatchSweepResult(batch.Count, dispatched, failed);
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
