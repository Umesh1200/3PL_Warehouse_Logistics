using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Domain service interface for ABC-based intelligent warehouse slotting optimization (Phase 6.3.7.1).
/// Evaluates physical and operational location feasibility, enforces ergonomic golden zones,
/// models relocation costs and disruption dampening, and generates advisory recommendation batches.
/// Invariant: All recommendations are strictly advisory simulation models. No changes to physical
/// inventory balances (StockOnHand) or locations occur until approved and executed through WMS tasks.
/// </summary>
public interface ISlottingOptimizationService
{
    /// <summary>
    /// Creates or updates a configurable warehouse slotting policy.
    /// </summary>
    Task<SlottingPolicyDto> CreatePolicyAsync(
        CreateSlottingPolicyRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a slotting policy by its unique identifier.
    /// </summary>
    Task<SlottingPolicyDto?> GetPolicyAsync(
        Guid policyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the active slotting policy for a facility, client, or zone scope.
    /// </summary>
    Task<SlottingPolicyDto> GetActivePolicyForFacilityAsync(
        Guid facilityId,
        Guid? clientId = null,
        Guid? zoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates physical, volumetric, weight, environmental, and operational compatibility
    /// between an SKU and a candidate warehouse storage location.
    /// </summary>
    Task<SlottingCompatibilityResultDto> EvaluateSkuLocationCompatibilityAsync(
        Guid skuId,
        Guid locationId,
        Guid? policyId = null,
        decimal? quantityToSlot = null,
        DateTime? asOfDateUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a slotting optimization simulation run, evaluating candidate relocations
    /// against policy goals, ergonomic golden zones, ABC velocity, and relocation disruption penalties.
    /// </summary>
    Task<SlottingRecommendationBatchDto> SimulateSlottingRecommendationsAsync(
        SlottingSimulationRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a simulated or approved slotting recommendation batch with its line-item proposals.
    /// </summary>
    Task<SlottingRecommendationBatchDto?> GetRecommendationBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a comprehensive SKU-to-location suitability matrix for candidate items and locations
    /// evaluating physical compatibility, ABC tier affinity, velocity category affinity, pick density, and ergonomics.
    /// Phase 6.3.7.2 ABC and Velocity-Based Slotting Rules.
    /// </summary>
    Task<SlottingSuitabilityMatrixDto> GenerateSuitabilityMatrixAsync(
        GenerateSuitabilityMatrixRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Formally approves all or selected recommendations in a batch, transitioning the batch
    /// to Approved / PartiallyApproved status without mutating the inventory ledger.
    /// </summary>
    Task<SlottingRecommendationBatchDto> ApproveRecommendationBatchAsync(
        Guid batchId,
        IReadOnlyList<Guid>? approvedRecommendationIds = null,
        CancellationToken cancellationToken = default);
}
