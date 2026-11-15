using Warehouse3PL.Application.Common.Models;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Domain service interface for market-basket product affinity mining and co-locational slotting rules (Phase 6.3.7.3).
/// Mines canonical order-line co-occurrence data, computes statistically sound support, confidence, and lift metrics,
/// isolates tenant and facility boundaries, clusters high-affinity products, and advises spatial co-location.
/// Invariant: All affinity outputs and cluster formations are strictly advisory models.
/// No mutations occur on operational inventory ledgers or physical locations.
/// </summary>
public interface IProductAffinityMiningService
{
    /// <summary>
    /// Executes a market-basket product affinity mining analysis for a facility and client over an observation window.
    /// Extracts canonical order itemsets, computes support/confidence/lift, filters noise, and forms deterministic co-location clusters.
    /// </summary>
    Task<ProductAffinityMiningResultDto> MineAffinityAsync(
        MineProductAffinityRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all statistically significant affinity pairs for a given facility and client.
    /// </summary>
    Task<IReadOnlyList<ProductAffinityPairDto>> GetSignificantPairsAsync(
        Guid facilityId,
        Guid clientId,
        decimal? minLift = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves active co-location affinity clusters for a facility and client.
    /// </summary>
    Task<IReadOnlyList<ProductAffinityClusterDto>> GetClustersAsync(
        Guid facilityId,
        Guid clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the co-location cluster (if any) containing the specified SKU item.
    /// </summary>
    Task<ProductAffinityClusterDto?> GetClusterForItemAsync(
        Guid facilityId,
        Guid clientId,
        Guid itemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves or creates the active product affinity policy for a facility and client scope.
    /// </summary>
    Task<ProductAffinityPolicyDto> GetPolicyAsync(
        Guid facilityId,
        Guid? clientId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates or creates a product affinity policy.
    /// </summary>
    Task<ProductAffinityPolicyDto> SavePolicyAsync(
        ProductAffinityPolicyDto policyDto,
        CancellationToken cancellationToken = default);
}
