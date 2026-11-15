using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Analytics - ABC Inventory Classification")]
[Route("api/v1/analytics/abc-classification")]
public class AbcClassificationAnalyticsController : BaseApiController
{
    private readonly IAnalyticsQueryService _analyticsQueryService;
    private readonly IAnalyticsProjectionService _analyticsProjectionService;
    private readonly ICurrentUserContext _currentUser;

    public AbcClassificationAnalyticsController(
        IAnalyticsQueryService analyticsQueryService,
        IAnalyticsProjectionService analyticsProjectionService,
        ICurrentUserContext currentUser)
    {
        _analyticsQueryService = analyticsQueryService;
        _analyticsProjectionService = analyticsProjectionService;
        _currentUser = currentUser;
    }

    [HttpGet("policy")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(AbcClassificationPolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEffectivePolicy(
        [FromQuery] Guid? facilityId,
        [FromQuery] Guid? clientId,
        CancellationToken ct)
    {
        var policy = await _analyticsQueryService.GetAbcClassificationPolicyAsync(facilityId, clientId, ct);
        if (policy == null)
            return NotFound(new { message = "No matching active ABC classification policy found." });

        return Ok(policy);
    }

    [HttpGet("policies")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(IReadOnlyList<AbcClassificationPolicyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPolicies(
        [FromQuery] Guid? facilityId,
        [FromQuery] Guid? clientId,
        CancellationToken ct)
    {
        var policies = await _analyticsQueryService.GetAbcClassificationPoliciesAsync(facilityId, clientId, ct);
        return Ok(policies);
    }

    [HttpPost("policy")]
    [HasPermission(Permissions.Analytics.AbcPolicyManage)]
    [ProducesResponseType(typeof(AbcClassificationPolicyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertPolicy(
        [FromBody] UpsertAbcClassificationPolicyRequest request,
        CancellationToken ct)
    {
        var result = await _analyticsProjectionService.UpsertAbcClassificationPolicyAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("facility/{facilityId}/calculate-batch")]
    [HasPermission(Permissions.Analytics.AbcPolicyManage)]
    [ProducesResponseType(typeof(AbcClassificationBatchResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExecuteBatchCalculation(
        [FromRoute] Guid facilityId,
        [FromBody] ExecuteAbcClassificationBatchRequest request,
        CancellationToken ct)
    {
        var result = await _analyticsProjectionService.CalculateAbcClassificationBatchAsync(
            facilityId,
            request.ClientId,
            request.PolicyId,
            request.CriteriaOverride,
            request.WindowOverride,
            request.AsOfDateUtc,
            ct);

        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/summary")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(AbcClassificationSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(
        [FromRoute] Guid facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] AbcClassificationCriteria? criteria,
        [FromQuery] int? version,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetAbcClassificationSummaryAsync(
            facilityId, clientId, criteria, version, ct);

        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/ranked-skus")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(IReadOnlyList<AbcRankedSkuDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRankedSkus(
        [FromRoute] Guid facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] AbcClassificationCategory? abcClass,
        [FromQuery] AbcClassificationCriteria? criteria,
        [FromQuery] int? version,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _analyticsQueryService.GetAbcRankedSkusAsync(
            facilityId, clientId, abcClass, criteria, version, page, pageSize, ct);

        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/migration")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(AbcMigrationAnalysisDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMigrationAnalysis(
        [FromRoute] Guid facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] int baseVersion,
        [FromQuery] int targetVersion,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetAbcMigrationAnalysisAsync(
            facilityId, clientId, baseVersion, targetVersion, ct);

        return Ok(result);
    }
}

