using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Authoritative transactional execution service for hard inventory reservation, optimistic concurrency retry,
/// and de-allocation lifecycles.
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public interface IAllocationExecutionService
{
    /// <summary>
    /// Atomically executes hard inventory reservation across candidate bins with optimistic concurrency protection,
    /// bounded retry loops, and idempotency tracking.
    /// </summary>
    Task<AllocationExecutionResultDto> ExecuteOrderAllocationAsync(
        Guid outboundOrderId,
        AllocationStrategyType? strategyOverride = null,
        string? idempotencyKey = null,
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically releases all active reservations for an order, restores available stock on StockOnHand,
    /// and recalculates order/line statuses.
    /// </summary>
    Task<DeallocationExecutionResultDto> ExecuteOrderDeallocationAsync(
        Guid outboundOrderId,
        string reason,
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a specific active allocation or partial quantity thereof.
    /// </summary>
    Task<ReleaseAllocationResultDto> ReleaseAllocationAsync(
        Guid allocationId,
        decimal? quantityToRelease,
        string reason,
        Guid? userId = null,
        CancellationToken cancellationToken = default);
}

