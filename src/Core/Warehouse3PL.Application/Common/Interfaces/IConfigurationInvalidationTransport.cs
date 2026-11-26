using Warehouse3PL.Domain.Entities.Reference;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Dispatches committed <see cref="ConfigurationInvalidationEvent"/> notifications to local/distributed caches.
/// Failure must throw so outbox dispatch remains retryable without false completion.
/// </summary>
public interface IConfigurationInvalidationTransport
{
    Task PublishInvalidationAsync(
        ConfigurationInvalidationEvent evt,
        CancellationToken cancellationToken = default);
}
