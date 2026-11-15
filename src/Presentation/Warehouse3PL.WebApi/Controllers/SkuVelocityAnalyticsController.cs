using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Analytics - SKU Velocity & Inventory Movement")]
[Route("api/v1/analytics/sku-velocity")]
public class SkuVelocityAnalyticsController : BaseApiController
{
    private readonly IAnalyticsQueryService _analyticsQueryService;
    private readonly IAnalyticsProjectionService _analyticsProjectionService;
    private readonly ICurrentUserContext _currentUser;

    public SkuVelocityAnalyticsController(
        IAnalyticsQueryService analyticsQueryService,
        IAnalyticsProjectionService analyticsProjectionService,
        ICurrentUserContext currentUser)
    {
        _analyticsQueryService = analyticsQueryService;
        _analyticsProjectionService = analyticsProjectionService;
        _currentUser = currentUser;
    }

    [HttpGet("facility/{facilityId}")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(IReadOnlyList<SkuVelocityClassificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSkuVelocityClassifications(
        [FromRoute] Guid facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] AbcClassificationCriteria? criteria,
        [FromQuery] AbcClassificationCategory? abcClass,
        [FromQuery] VelocityObservationWindow? window,
        [FromQuery] InventoryMovementVelocityCategory? velocityCategory,
        [FromQuery] Guid? itemId,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetSkuVelocityClassificationsAsync(
            facilityId, clientId, criteria, abcClass, window, velocityCategory, itemId, ct);
        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/summary")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(SkuVelocitySummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSkuVelocitySummary(
        [FromRoute] Guid facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] VelocityObservationWindow window = VelocityObservationWindow.Days90,
        CancellationToken ct = default)
    {
        var result = await _analyticsQueryService.GetSkuVelocitySummaryAsync(
            facilityId, clientId, window, ct);
        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/sku/{itemId}/multi-window")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(SkuVelocityMultiWindowComparisonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSkuVelocityMultiWindow(
        [FromRoute] Guid facilityId,
        [FromRoute] Guid itemId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetSkuVelocityMultiWindowComparisonAsync(
            facilityId, itemId, asOfUtc, ct);

        if (result == null)
            return NotFound(new { message = $"No velocity records or movement history found for item {itemId} at facility {facilityId}." });

        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/turnover")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(InventoryTurnoverAnalysisDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInventoryTurnoverAnalysis(
        [FromRoute] Guid facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] VelocityObservationWindow window = VelocityObservationWindow.Days90,
        [FromQuery] DateTime? asOfUtc = null,
        CancellationToken ct = default)
    {
        var result = await _analyticsQueryService.GetInventoryTurnoverAnalysisAsync(
            facilityId, clientId, window, asOfUtc, ct);
        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/zones")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(IReadOnlyList<ZoneVelocityProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetZoneVelocityProfiles(
        [FromRoute] Guid facilityId,
        [FromQuery] Guid? zoneId,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetZoneVelocityProfilesAsync(
            facilityId, zoneId, ct);
        return Ok(result);
    }

    [HttpPost("movement-events")]
    [HasPermission(Permissions.Inventory.View)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> IngestInventoryMovementEvent(
        [FromBody] ProcessInventoryMovementEventRequest request,
        CancellationToken ct)
    {
        await _analyticsProjectionService.ProcessInventoryMovementEventAsync(
            request.EventId,
            request.SequenceNumber,
            request.FacilityId,
            request.ClientId,
            request.ItemId,
            request.Sku,
            request.EventType,
            request.Quantity,
            request.TimestampUtc,
            request.SourceLocationId,
            request.DestinationLocationId,
            request.ZoneId,
            request.UnitCost,
            request.ReferenceNumber,
            request.IsCanonicalDemand,
            ct);

        return Accepted(new { message = "Inventory movement event accepted for asynchronous velocity projection.", eventId = request.EventId });
    }
}

