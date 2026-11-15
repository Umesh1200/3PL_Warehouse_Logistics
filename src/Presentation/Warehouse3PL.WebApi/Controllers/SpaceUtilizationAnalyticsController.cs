using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Analytics - Warehouse Space & Volume Utilization")]
[Route("api/v1/analytics/space-utilization")]
public class SpaceUtilizationAnalyticsController : BaseApiController
{
    private readonly IAnalyticsQueryService _analyticsQueryService;
    private readonly IAnalyticsProjectionService _analyticsProjectionService;
    private readonly ICurrentUserContext _currentUser;

    public SpaceUtilizationAnalyticsController(
        IAnalyticsQueryService analyticsQueryService,
        IAnalyticsProjectionService analyticsProjectionService,
        ICurrentUserContext currentUser)
    {
        _analyticsQueryService = analyticsQueryService;
        _analyticsProjectionService = analyticsProjectionService;
        _currentUser = currentUser;
    }

    [HttpGet("facility/{facilityId}/summary")]
    [HasPermission(Permissions.Analytics.WarehouseHeatmapView)]
    [ProducesResponseType(typeof(FacilitySpaceUtilizationSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFacilitySpaceUtilizationSummary(
        [FromRoute] Guid facilityId,
        [FromQuery] DateTime? asOfDateUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetFacilitySpaceUtilizationSummaryAsync(
            facilityId, asOfDateUtc, ct);
        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/locations")]
    [HasPermission(Permissions.Analytics.WarehouseHeatmapView)]
    [ProducesResponseType(typeof(IReadOnlyList<LocationSpaceUtilizationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLocationSpaceUtilization(
        [FromRoute] Guid facilityId,
        [FromQuery] DateTime? asOfDateUtc,
        [FromQuery] Guid? zoneId,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetLocationSpaceUtilizationAsync(
            facilityId, asOfDateUtc, zoneId, ct);
        return Ok(result);
    }

    [HttpGet("facility/{facilityId}/alerts/over-capacity")]
    [HasPermission(Permissions.Analytics.WarehouseHeatmapView)]
    [ProducesResponseType(typeof(IReadOnlyList<OverCapacityAlertDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverCapacityAlerts(
        [FromRoute] Guid facilityId,
        [FromQuery] DateTime? asOfDateUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetOverCapacityAlertsAsync(
            facilityId, asOfDateUtc, ct);
        return Ok(result);
    }
}

