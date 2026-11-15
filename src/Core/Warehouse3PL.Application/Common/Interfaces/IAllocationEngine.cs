using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Core coordination engine for outbound inventory allocation planning.
/// Coordinates policy lookup, strategy resolution, multi-bin candidate allocation, and shortage detection.
/// Strictly read-only in Step 2; physical reservations and mutations are executed in Step 3.
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public interface IAllocationEngine
{
    /// <summary>
    /// Computes a multi-bin allocation plan and shortage analysis for a single order line request.
    /// </summary>
    Task<LineAllocationPlan> PlanLineAllocationAsync(
        AllocationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes a comprehensive allocation plan across all active lines of an outbound order.
    /// </summary>
    Task<OrderAllocationPlan> PlanOrderAllocationAsync(
        Guid outboundOrderId,
        AllocationStrategyType? strategyOverride = null,
        CancellationToken cancellationToken = default);
}

