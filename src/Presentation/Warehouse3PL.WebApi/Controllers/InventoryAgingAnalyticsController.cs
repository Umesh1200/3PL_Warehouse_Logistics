using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Analytics - Inventory Aging & Health")]
[Route("api/v1/analytics/inventory-aging")]
public class InventoryAgingAnalyticsController : BaseApiController
{
    private readonly IAnalyticsQueryService _analyticsQueryService;
    private readonly IAnalyticsProjectionService _analyticsProjectionService;
    private readonly ICurrentUserContext _currentUser;

    public InventoryAgingAnalyticsController(
        IAnalyticsQueryService analyticsQueryService,
        IAnalyticsProjectionService analyticsProjectionService,
        ICurrentUserContext currentUser)
    {
        _analyticsQueryService = analyticsQueryService;
        _analyticsProjectionService = analyticsProjectionService;
        _currentUser = currentUser;
    }

    [HttpGet("facility/{facilityId}/summary")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(InventoryHealthSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInventoryHealthSummary(
        [FromRoute] Guid facilityId,
        [FromQuery] DateTime? asOfDateUtc,
        [FromQuery] Guid? clientId,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetInventoryHealthSummaryAsync(
            facilityId, asOfDateUtc, clientId, ct);
        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/records")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(IReadOnlyList<DailyInventoryAgingRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDailyInventoryAgingRecords(
        [FromRoute] Guid facilityId,
        [FromQuery] DateTime? asOfDateUtc,
        [FromQuery] Guid? clientId,
        [FromQuery] InventoryAgingBucket? bucket,
        [FromQuery] bool? isDeadStock,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetDailyInventoryAgingRecordsAsync(
            facilityId, asOfDateUtc, clientId, bucket, isDeadStock, ct);
        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/sku/{itemId}/lots")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(IReadOnlyList<LotAgingRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLotAgingBreakdown(
        [FromRoute] Guid facilityId,
        [FromRoute] Guid itemId,
        [FromQuery] DateTime? asOfDateUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetLotAgingBreakdownAsync(
            facilityId, itemId, asOfDateUtc, ct);
        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/dead-stock")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(DeadStockAnalysisDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeadStockAnalysis(
        [FromRoute] Guid facilityId,
        [FromQuery] DateTime? asOfDateUtc,
        [FromQuery] Guid? clientId,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetDeadStockAnalysisAsync(
            facilityId, clientId, asOfDateUtc, ct);
        return Ok(result);
    }

    [HttpGet("policy")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(InventoryAgingPolicyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInventoryAgingPolicy(
        [FromQuery] Guid? facilityId,
        [FromQuery] Guid? clientId,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetInventoryAgingPolicyAsync(
            facilityId, clientId, ct);
        return Ok(result);
    }

    [HttpPost("policy")]
    [HasPermission(Permissions.Analytics.InventoryAgingManage)]
    [ProducesResponseType(typeof(InventoryAgingPolicyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertInventoryAgingPolicy(
        [FromBody] UpsertInventoryAgingPolicyRequest request,
        CancellationToken ct)
    {
        var policy = InventoryAgingPolicy.Create(
            name: request.Name,
            facilityId: request.FacilityId,
            clientId: request.ClientId,
            slowMovingDays: request.SlowMovingThresholdDays,
            dormantDays: request.DormantThresholdDays,
            deadStockDays: request.DeadStockThresholdDays,
            nearExpiryDays: request.NearExpiryThresholdDays,
            criticalExpiryDays: request.CriticalExpiryThresholdDays,
            defaultBasis: request.DefaultBasis,
            version: request.Version,
            effectiveFromUtc: request.EffectiveFromUtc,
            currency: request.Currency,
            dailyHoldingCostPerCbm: request.DailyHoldingCostPerCbm,
            dailyHoldingCostPerPallet: request.DailyHoldingCostPerPallet);

        if (request.Id.HasValue && request.Id.Value != Guid.Empty)
        {
            policy.Id = request.Id.Value;
        }

        policy.EffectiveToUtc = request.EffectiveToUtc;
        policy.IsActive = request.IsActive;

        await _analyticsProjectionService.UpsertInventoryAgingPolicyAsync(policy, ct);

        var result = await _analyticsQueryService.GetInventoryAgingPolicyAsync(
            request.FacilityId, request.ClientId, ct);
        return Ok(result);
    }
}

