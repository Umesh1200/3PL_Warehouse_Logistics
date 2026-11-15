using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Analytics - Executive Control Tower")]
[Route("api/v1/control-tower")]
public class ControlTowerController : BaseApiController
{
    private readonly IAnalyticsQueryService _analyticsQueryService;

    public ControlTowerController(IAnalyticsQueryService analyticsQueryService)
    {
        _analyticsQueryService = analyticsQueryService;
    }

    [HttpGet("overview")]
    [HasPermission(Permissions.Analytics.ControlTowerView)]
    [ProducesResponseType(typeof(ControlTowerExecutiveOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExecutiveOverview(
        [FromQuery] Guid? facilityId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetExecutiveOverviewAsync(facilityId, asOfUtc, ct);
        return Ok(result);
    }

    [HttpGet("summary")]
    [HasPermission(Permissions.Analytics.ControlTowerView)]
    [ProducesResponseType(typeof(ControlTowerSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid? facilityId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetControlTowerSummaryAsync(facilityId, asOfUtc, ct);
        return Ok(result);
    }

    [HttpGet("hourly-pacing")]
    [HasPermission(Permissions.Analytics.ProductivityView)]
    [ProducesResponseType(typeof(IReadOnlyList<HourlyThroughputPacingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHourlyPacing(
        [FromQuery] Guid facilityId,
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        CancellationToken ct)
    {
        var start = startUtc ?? DateTime.UtcNow.Date;
        var end = endUtc ?? DateTime.UtcNow;
        var result = await _analyticsQueryService.GetHourlyThroughputPacingAsync(facilityId, start, end, ct);
        return Ok(result);
    }

    [HttpGet("dock-yard")]
    [HasPermission(Permissions.Analytics.ControlTowerView)]
    [ProducesResponseType(typeof(DockYardOperationalStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDockYardStatus(
        [FromQuery] Guid facilityId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetDockYardStatusAsync(facilityId, asOfUtc, ct);
        return Ok(result);
    }

    [HttpGet("sla")]
    [HasPermission(Permissions.Analytics.SlaView)]
    [ProducesResponseType(typeof(OrderFulfillmentSlaDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSlaDashboard(
        [FromQuery] Guid? facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetOrderFulfillmentSlaDashboardAsync(facilityId, clientId, asOfUtc, ct);
        return Ok(result);
    }

    [HttpGet("bottlenecks")]
    [HasPermission(Permissions.Analytics.ControlTowerView)]
    [ProducesResponseType(typeof(FacilityBottleneckAnalysisDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBottlenecks(
        [FromQuery] Guid facilityId,
        [FromQuery] DateTime? asOfUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetFacilityBottlenecksAsync(facilityId, asOfUtc, ct);
        return Ok(result);
    }

    [HttpGet("multi-facility")]
    [HasPermission(Permissions.Analytics.MultiFacilityView)]
    [ProducesResponseType(typeof(MultiFacilityComparisonDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMultiFacilityComparison(
        [FromQuery] DateTime? asOfUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetMultiFacilityComparisonAsync(asOfUtc, ct);
        return Ok(result);
    }
}

