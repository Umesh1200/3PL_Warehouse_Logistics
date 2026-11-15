using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Events.Transportation;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Service managing transportation and carrier SLA read-model projections, stream-partitioned checkpoints,
/// deduplication, and deterministic offline replay from durable operational stores.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public interface ITransportationProjectionService
{
    // Stream Checkpointing & Deduplication
    Task<ProjectionCheckpoint> GetOrCreateStreamCheckpointAsync(
        string projectionName,
        string sourceStreamId,
        CancellationToken cancellationToken = default);

    Task UpdateStreamCheckpointWatermarkAsync(
        string projectionName,
        string sourceStreamId,
        Guid eventId,
        long sequenceNumber,
        DateTime timestampUtc,
        CancellationToken cancellationToken = default);

    Task ResetStreamCheckpointAsync(
        string projectionName,
        string sourceStreamId,
        ProjectionReplayMode replayMode,
        DateTime? fromDateUtc = null,
        CancellationToken cancellationToken = default);

    Task<bool> HasEventBeenProcessedAsync(
        string projectionName,
        Guid eventId,
        CancellationToken cancellationToken = default);

    // Event Ingestion & Projection Handlers
    Task ProcessShipmentDispatchedAsync(
        ShipmentDispatchedEvent @event,
        CancellationToken cancellationToken = default);

    Task ProcessShipmentTrackingUpdatedAsync(
        ShipmentTrackingUpdatedEvent @event,
        CancellationToken cancellationToken = default);

    Task ProcessDeliveryAttemptRecordedAsync(
        DeliveryAttemptRecordedEvent @event,
        CancellationToken cancellationToken = default);

    Task ProcessShipmentDeliveredAsync(
        ShipmentDeliveredEvent @event,
        CancellationToken cancellationToken = default);

    Task ProcessProofOfDeliveryRecordedAsync(
        ProofOfDeliveryRecordedEvent @event,
        CancellationToken cancellationToken = default);

    Task ProcessDeliveryExceptionRecordedAsync(
        DeliveryExceptionRecordedEvent @event,
        CancellationToken cancellationToken = default);

    Task ProcessShipmentLostThresholdReachedAsync(
        ShipmentLostThresholdReachedEvent @event,
        CancellationToken cancellationToken = default);

    Task ProcessCarrierCostRecordedAsync(
        CarrierCostRecordedEvent @event,
        CancellationToken cancellationToken = default);

    Task ProcessCarrierInvoiceReconciledAsync(
        CarrierInvoiceReconciledEvent @event,
        CancellationToken cancellationToken = default);

    // Deterministic Offline Replay from Durable Operational Tables
    Task<int> ReplayTransportationProjectionsFromDurableStoresAsync(
        string? sourceStreamId = null,
        DateTime? fromDateUtc = null,
        DateTime? toDateUtc = null,
        CancellationToken cancellationToken = default);
}
