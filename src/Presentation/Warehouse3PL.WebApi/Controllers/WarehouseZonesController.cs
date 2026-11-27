using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Tags("Warehouse Master - Zones")]
[Route("api/v1/warehouse-zones")]
public class WarehouseZonesController : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<WarehouseZoneDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetZones(
        [FromQuery] Guid? facilityId,
        [FromQuery] WarehouseZoneType? zoneType,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? dedicatedClientId,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await Mediator.Send(
            new GetWarehouseZonesQuery(facilityId, zoneType, isActive, dedicatedClientId, search, pageNumber, pageSize),
            ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WarehouseZoneDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetZoneById(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetWarehouseZoneByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(WarehouseZoneDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateZone([FromBody] CreateWarehouseZoneCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WarehouseZoneDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateZone(Guid id, [FromBody] UpdateWarehouseZoneCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(WarehouseZoneDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateZoneStatus(Guid id, [FromBody] UpdateZoneStatusRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdateWarehouseZoneStatusCommand(id, request.IsActive, request.Remarks), ct);
        return Ok(result);
    }
}

public sealed record UpdateZoneStatusRequest(bool IsActive, string? Remarks = null);
