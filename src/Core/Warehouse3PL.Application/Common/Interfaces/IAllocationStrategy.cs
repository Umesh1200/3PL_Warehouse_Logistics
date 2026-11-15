using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Strategy abstraction for selecting and sorting eligible on-hand inventory candidates.
/// Strictly read-only; does NOT mutate inventory quantities.
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public interface IAllocationStrategy
{
    AllocationStrategyType StrategyType { get; }

    /// <summary>
    /// Queries, filters, and deterministically orders eligible inventory candidates.
    /// </summary>
    Task<IReadOnlyList<StockOnHand>> SelectCandidatesAsync(
        AllocationRequest request,
        CancellationToken cancellationToken = default);
}

