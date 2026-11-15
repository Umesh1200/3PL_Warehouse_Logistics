using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Domain service interface for Phase 6.3.7.4: Relocation Recommendation Engine & Multi-Factor Prioritization.
/// Synthesizes transparent economics, operational urgency, placement suitability, and disruption risk
/// into an auditable, deterministic multi-factor prioritization engine.
/// Strict Invariant: Recommendations are advisory; zero automated inventory movements or operational ledger mutations.
/// </summary>
public interface IRelocationRecommendationService
{
    /// <summary>
    /// Evaluates transparent economics for a candidate relocation proposal:
    /// gross monthly savings, handling cost, movement labor cost, disruption cost, amortized monthly cost, and net benefit.
    /// </summary>
    (decimal grossSavings, decimal handlingCost, decimal movementCost, decimal disruptionCost, decimal amortizedMonthlyCost, decimal netBenefit)
        CalculateTransparentEconomics(
            decimal roundTripTravelSavedMeters,
            decimal monthlyPicks,
            decimal quantity,
            decimal weightKg,
            Location currentLocation,
            Location destinationLocation,
            SlottingPolicy policy,
            string shiftType = "Standard",
            bool isOpportunityGain = false);

    /// <summary>
    /// Evaluates multi-factor operational urgency metric (0 to 100) reflecting velocity mismatch,
    /// golden zone congestion, seasonal spike urgency, and ergonomic lifting hazards.
    /// </summary>
    (decimal urgencyScore, string urgencyReason) EvaluateOperationalUrgency(
        Item item,
        Location currentLocation,
        SlottingPolicy policy,
        string abcClass,
        string velocityCategory,
        string velocityTrend);

    /// <summary>
    /// Calculates the normalized multi-factor priority score (0.0 to 100.0) combining financial return (w_b * S_b),
    /// operational urgency (w_u * U), placement suitability (w_s * S_suit), and disruption mitigation (w_d * (100 - S_disrupt)),
    /// strictly subject to hard-gate supremacy.
    /// Invariant: Infeasible proposals strictly score 0.0.
    /// </summary>
    decimal CalculatePriorityScore(
        decimal netMonthlyBenefitUsd,
        decimal urgencyScore,
        decimal suitabilityScore,
        decimal costPenaltyUsd,
        SlottingPolicy policy,
        bool isFeasible = true);

    /// <summary>
    /// Submits a simulated relocation recommendation batch for supervisory review.
    /// Status transitions: Draft/Simulated -> PendingApproval.
    /// Strict Invariant: Advisory only; zero physical inventory movements or operational ledger mutations.
    /// </summary>
    Task<SlottingBatchStatus> SubmitBatchForReviewAsync(
        Guid batchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a recommendation batch prior to operational execution.
    /// Status transitions: Draft/Simulated/PendingApproval -> Cancelled.
    /// Strict Invariant: Advisory only; zero physical inventory movements or operational ledger mutations.
    /// </summary>
    Task<SlottingBatchStatus> CancelBatchAsync(
        Guid batchId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prioritizes candidate relocation proposals within a facility based on transparent economics,
    /// multi-factor operational urgency, placement suitability, disruption damping, and negative net benefit policy.
    /// </summary>
    Task<RelocationPrioritizationResultDto> PrioritizeRelocationsAsync(
        RelocationPrioritizationRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes administrative approval for a prioritized relocation recommendation batch.
    /// STRICT INVARIANT: Records approval audit metadata and updates recommendation statuses without
    /// moving inventory or mutating any operational ledger records.
    /// </summary>
    Task<RelocationApprovalResultDto> ApproveRelocationPlanAsync(
        RelocationApprovalRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a prioritized relocation plan by batch ID.
    /// </summary>
    Task<RelocationPrioritizationResultDto?> GetPrioritizedPlanAsync(
        Guid batchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists recent slotting recommendation batches for a facility, with optional tenant filtering.
    /// </summary>
    Task<IReadOnlyList<SlottingBatchSummaryDto>> GetBatchesForFacilityAsync(
        Guid facilityId,
        Guid? clientId = null,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves deep algorithmic explainability and destination comparison for a specific recommendation.
    /// Includes 6D suitability breakdown, 4-factor urgency breakdown, transparent costs, and location comparison.
    /// </summary>
    Task<SlottingRecommendationDetailDto?> InspectRecommendationAsync(
        Guid recommendationId,
        CancellationToken cancellationToken = default);
}
