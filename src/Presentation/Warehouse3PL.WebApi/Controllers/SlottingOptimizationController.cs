using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Analytics - ABC Slotting Optimization")]
[Route("api/v1/analytics/slotting")]
public class SlottingOptimizationController : BaseApiController
{
    private readonly ISlottingOptimizationService _slottingService;
    private readonly IRelocationRecommendationService _relocationService;
    private readonly ICurrentUserContext _currentUser;

    public SlottingOptimizationController(
        ISlottingOptimizationService slottingService,
        IRelocationRecommendationService relocationService,
        ICurrentUserContext currentUser)
    {
        _slottingService = slottingService;
        _relocationService = relocationService;
        _currentUser = currentUser;
    }

    public SlottingOptimizationController(
        ISlottingOptimizationService slottingService,
        ICurrentUserContext currentUser)
        : this(slottingService, null!, currentUser)
    {
    }

    /// <summary>
    /// Creates a configurable warehouse slotting policy governing physical constraints and optimization goals.
    /// Phase 6.3.7.1 Slotting Optimization Domain, Policy &amp; Constraint Foundation.
    /// </summary>
    [HttpPost("policies")]
    [HasPermission(Permissions.Analytics.SlottingManage)]
    [ProducesResponseType(typeof(SlottingPolicyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] CreateSlottingPolicyRequestDto request,
        CancellationToken ct)
    {
        var result = await _slottingService.CreatePolicyAsync(request, ct);
        return CreatedAtAction(nameof(GetPolicy), new { policyId = result.Id }, result);
    }

    /// <summary>
    /// Retrieves a slotting policy by its unique identifier.
    /// </summary>
    [HttpGet("policies/{policyId}")]
    [HasPermission(Permissions.Analytics.SlottingView)]
    [ProducesResponseType(typeof(SlottingPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPolicy(
        [FromRoute] Guid policyId,
        CancellationToken ct)
    {
        var result = await _slottingService.GetPolicyAsync(policyId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the active slotting policy for a facility, client, or zone.
    /// </summary>
    [HttpGet("facility/{facilityId}/active-policy")]
    [HttpGet("policies/active")]
    [HasPermission(Permissions.Analytics.SlottingView)]
    [ProducesResponseType(typeof(SlottingPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetActivePolicy(
        Guid facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] Guid? zoneId,
        CancellationToken ct)
    {
        var result = await _slottingService.GetActivePolicyForFacilityAsync(facilityId, clientId, zoneId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Evaluates physical, volumetric, weight, environmental, and operational compatibility
    /// between an SKU and a candidate warehouse storage location.
    /// </summary>
    [HttpGet("evaluate-compatibility")]
    [HasPermission(Permissions.Analytics.SlottingView)]
    [ProducesResponseType(typeof(SlottingCompatibilityResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EvaluateCompatibility(
        [FromQuery] Guid skuId,
        [FromQuery] Guid locationId,
        [FromQuery] Guid? policyId = null,
        [FromQuery] decimal? quantity = null,
        [FromQuery] DateTime? asOfDateUtc = null,
        CancellationToken ct = default)
    {
        var result = await _slottingService.EvaluateSkuLocationCompatibilityAsync(skuId, locationId, policyId, quantity, asOfDateUtc, ct);
        return Ok(result);
    }

    /// <summary>
    /// Generates a comprehensive SKU-to-location suitability matrix evaluating physical compatibility,
    /// ABC tier affinity, velocity category affinity, pick-volume density, and ergonomics.
    /// Phase 6.3.7.2 ABC and Velocity-Based Slotting Rules.
    /// </summary>
    [HttpPost("suitability-matrix")]
    [HasPermission(Permissions.Analytics.SlottingView)]
    [ProducesResponseType(typeof(SlottingSuitabilityMatrixDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateSuitabilityMatrix(
        [FromBody] GenerateSuitabilityMatrixRequestDto request,
        CancellationToken ct)
    {
        var result = await _slottingService.GenerateSuitabilityMatrixAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Executes a slotting optimization simulation run, generating advisory relocation recommendations
    /// without mutating operational inventory or locations.
    /// </summary>
    [HttpPost("simulation")]
    [HttpPost("simulate")]
    [HasPermission(Permissions.Analytics.SlottingManage)]
    [ProducesResponseType(typeof(SlottingRecommendationBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SimulateSlotting(
        [FromBody] SlottingSimulationRequestDto request,
        CancellationToken ct)
    {
        var result = await _slottingService.SimulateSlottingRecommendationsAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves a slotting recommendation batch with its proposed line-item moves.
    /// </summary>
    [HttpGet("batches/{batchId}")]
    [HasPermission(Permissions.Analytics.SlottingView)]
    [ProducesResponseType(typeof(SlottingRecommendationBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatch(
        [FromRoute] Guid batchId,
        CancellationToken ct)
    {
        var result = await _slottingService.GetRecommendationBatchAsync(batchId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Formally approves all or selected recommendations within a batch.
    /// Invariant: Batch approval transitions the recommendation status to Approved without mutating
    /// operational stock balances (StockOnHand) or locations until physical relocation tasks execute.
    /// </summary>
    [HttpPost("batches/{batchId}/approve")]
    [HasPermission(Permissions.Analytics.SlottingManage)]
    [ProducesResponseType(typeof(SlottingRecommendationBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveBatch(
        [FromRoute] Guid batchId,
        [FromBody] List<Guid>? approvedRecommendationIds,
        CancellationToken ct)
    {
        var result = await _slottingService.ApproveRecommendationBatchAsync(batchId, approvedRecommendationIds, ct);
        return Ok(result);
    }

    /// <summary>
    /// Executes multi-factor relocation recommendation prioritization synthesizing transparent economics,
    /// operational urgency, placement suitability, disruption damping, and negative net benefit gating.
    /// Phase 6.3.7.4 Relocation Recommendation Engine &amp; Multi-Factor Prioritization.
    /// </summary>
    [HttpPost("relocations/prioritize")]
    [HasPermission(Permissions.Analytics.SlottingManage)]
    [ProducesResponseType(typeof(RelocationPrioritizationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PrioritizeRelocations(
        [FromBody] RelocationPrioritizationRequestDto request,
        CancellationToken ct)
    {
        var result = await _relocationService.PrioritizeRelocationsAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Processes administrative approval for a prioritized relocation recommendation plan.
    /// Invariant: Batch approval transitions recommendation statuses to Approved/Rejected without
    /// moving inventory or mutating operational ledger records.
    /// </summary>
    [HttpPost("relocations/approve")]
    [HttpPost("batches/{batchId}/approve-plan")]
    [HasPermission(Permissions.Analytics.SlottingManage)]
    [ProducesResponseType(typeof(RelocationApprovalResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveRelocationPlan(
        [FromBody] RelocationApprovalRequestDto request,
        [FromRoute] Guid? batchId = null,
        CancellationToken ct = default)
    {
        if (request.BatchId == Guid.Empty && batchId.HasValue)
        {
            request = request with { BatchId = batchId.Value };
        }
        var result = await _relocationService.ApproveRelocationPlanAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Submits a simulated relocation recommendation batch for supervisory review.
    /// </summary>
    [HttpPost("relocations/{batchId}/submit-review")]
    [HttpPost("batches/{batchId}/submit-review")]
    [HasPermission(Permissions.Analytics.SlottingManage)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitBatchForReview(
        [FromRoute] Guid batchId,
        CancellationToken ct)
    {
        var status = await _relocationService.SubmitBatchForReviewAsync(batchId, ct);
        return Ok(new { BatchId = batchId, Status = status });
    }

    /// <summary>
    /// Cancels a recommendation batch prior to operational execution.
    /// </summary>
    [HttpPost("relocations/{batchId}/cancel")]
    [HttpPost("batches/{batchId}/cancel")]
    [HasPermission(Permissions.Analytics.SlottingManage)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelBatch(
        [FromRoute] Guid batchId,
        [FromBody] CancelBatchRequestDto request,
        CancellationToken ct)
    {
        var status = await _relocationService.CancelBatchAsync(batchId, request?.Reason ?? string.Empty, ct);
        return Ok(new { BatchId = batchId, Status = status, Reason = request?.Reason });
    }

    /// <summary>
    /// Retrieves a prioritized relocation plan by batch ID.
    /// </summary>
    [HttpGet("relocations/{batchId}")]
    [HttpGet("batches/{batchId}/plan")]
    [HasPermission(Permissions.Analytics.SlottingView)]
    [ProducesResponseType(typeof(RelocationPrioritizationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPrioritizedPlan(
        [FromRoute] Guid batchId,
        CancellationToken ct)
    {
        var result = await _relocationService.GetPrioritizedPlanAsync(batchId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Lists recent slotting recommendation batches for a facility.
    /// </summary>
    [HttpGet("facility/{facilityId}/batches")]
    [HasPermission(Permissions.Analytics.SlottingView)]
    [ProducesResponseType(typeof(IReadOnlyList<SlottingBatchSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFacilityBatches(
        [FromRoute] Guid facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var result = await _relocationService.GetBatchesForFacilityAsync(facilityId, clientId, limit, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves deep algorithmic explainability and destination comparison for a recommendation.
    /// </summary>
    [HttpGet("recommendations/{recommendationId}/inspect")]
    [HasPermission(Permissions.Analytics.SlottingView)]
    [ProducesResponseType(typeof(SlottingRecommendationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> InspectRecommendation(
        [FromRoute] Guid recommendationId,
        CancellationToken ct)
    {
        var result = await _relocationService.InspectRecommendationAsync(recommendationId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }
}
