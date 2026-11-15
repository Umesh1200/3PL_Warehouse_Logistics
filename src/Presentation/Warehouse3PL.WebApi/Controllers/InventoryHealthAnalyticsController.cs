using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Analytics - Unified Inventory Health & Heatmaps")]
[Route("api/v1/analytics/inventory-health")]
public class InventoryHealthAnalyticsController : BaseApiController
{
    private readonly IAnalyticsQueryService _analyticsQueryService;
    private readonly ICurrentUserContext _currentUser;

    public InventoryHealthAnalyticsController(
        IAnalyticsQueryService analyticsQueryService,
        ICurrentUserContext currentUser)
    {
        _analyticsQueryService = analyticsQueryService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Retrieves the authoritative unified inventory health dashboard for a facility and optional client.
    /// Consolidates Aging, Space Utilization, SKU Velocity, ABC Pareto Stratification, and Stock Risk Alerts
    /// with composite health scoring and freshness metadata.
    /// DB-SPEC-3PL-2026 Phase 6.3.6.1 Unified Inventory Health Dashboard Foundation.
    /// </summary>
    [HttpGet("facility/{facilityId}/unified-dashboard")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(UnifiedInventoryHealthDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUnifiedInventoryHealthDashboard(
        [FromRoute] Guid facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] DateTime? asOfDateUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetUnifiedInventoryHealthDashboardAsync(
            facilityId, clientId, asOfDateUtc, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves dynamic spatial warehouse heatmap cell visualization states for a facility.
    /// Supports independent layer filtering (Cube, Pallets, Velocity, Aging, Overcapacity, Blocked),
    /// multi-condition mapping, zoom/pan bounds, and client portal masking.
    /// DB-SPEC-3PL-2026 Phase 6.3.6.2 Dynamic Warehouse Heatmap Domain.
    /// </summary>
    [HttpGet("facility/{facilityId}/heatmap")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(WarehouseHeatmapResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWarehouseHeatmap(
        [FromRoute] Guid facilityId,
        [FromQuery] HeatmapQueryFilterDto? filter,
        [FromQuery] DateTime? asOfDateUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetWarehouseHeatmapAsync(
            facilityId, filter, asOfDateUtc, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves documented, standardized color scale definitions for all warehouse heatmap layers.
    /// DB-SPEC-3PL-2026 Phase 6.3.6.2 Dynamic Warehouse Heatmap Domain.
    /// </summary>
    [HttpGet("heatmap/color-scales")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(IReadOnlyList<HeatmapColorScaleDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHeatmapColorScales(CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetHeatmapColorScalesAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves the physical hierarchical warehouse topology (Facility -> Zone -> Aisle -> Rack -> Location).
    /// DB-SPEC-3PL-2026 Phase 6.3.6.2 Dynamic Warehouse Heatmap Domain.
    /// </summary>
    [HttpGet("facility/{facilityId}/topology")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(IReadOnlyList<WarehouseTopologyNodeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWarehouseTopology(
        [FromRoute] Guid facilityId,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetWarehouseTopologyAsync(facilityId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves multi-tier spatial heatmap projections (Facility -> Zone -> Aisle -> Rack),
    /// spatial grid binning, and operational hotspots with weighted utilization rollups.
    /// DB-SPEC-3PL-2026 Phase 6.3.6.3 Heatmap Projection Engine &amp; Aggregations.
    /// </summary>
    [HttpGet("facility/{facilityId}/spatial-projection")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(SpatialHeatmapProjectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSpatialHeatmapProjection(
        [FromRoute] Guid facilityId,
        [FromQuery] HeatmapQueryFilterDto? filter,
        [FromQuery] decimal? gridCellSizeMeters,
        [FromQuery] DateTime? asOfDateUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetSpatialHeatmapProjectionAsync(
            facilityId, filter, gridCellSizeMeters ?? 2.0m, asOfDateUtc, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves detected operational spatial hotspots (contiguous clusters of overcapacity, dead stock, and locks).
    /// DB-SPEC-3PL-2026 Phase 6.3.6.3 Heatmap Projection Engine &amp; Aggregations.
    /// </summary>
    [HttpGet("facility/{facilityId}/hotspots")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(IReadOnlyList<SpatialHotspotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSpatialHotspots(
        [FromRoute] Guid facilityId,
        [FromQuery] DateTime? asOfDateUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetSpatialHotspotsAsync(facilityId, asOfDateUtc, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves multi-tier warehouse zone and aisle spatial rollups with strictly weighted utilization calculations.
    /// DB-SPEC-3PL-2026 Phase 6.3.6.3 Heatmap Projection Engine &amp; Aggregations.
    /// </summary>
    [HttpGet("facility/{facilityId}/zone-rollups")]
    [HasPermission(Permissions.Analytics.InventoryHealthView)]
    [ProducesResponseType(typeof(IReadOnlyList<ZoneSpatialRollupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetZoneSpatialRollups(
        [FromRoute] Guid facilityId,
        [FromQuery] DateTime? asOfDateUtc,
        CancellationToken ct)
    {
        var result = await _analyticsQueryService.GetZoneSpatialRollupsAsync(facilityId, asOfDateUtc, ct);
        return Ok(result);
    }
}
