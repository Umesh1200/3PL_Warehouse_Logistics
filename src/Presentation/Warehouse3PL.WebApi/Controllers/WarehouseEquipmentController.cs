using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Tags("Warehouse Master - Equipment")]
[Route("api/v1/warehouse-equipment")]
public class WarehouseEquipmentController : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<WarehouseEquipmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEquipment(
        [FromQuery] Guid? facilityId,
        [FromQuery] WarehouseEquipmentType? equipmentType,
        [FromQuery] WarehouseEquipmentStatus? status,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await Mediator.Send(
            new GetWarehouseEquipmentQuery(facilityId, equipmentType, status, search, pageNumber, pageSize),
            ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WarehouseEquipmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEquipmentById(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetWarehouseEquipmentByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(WarehouseEquipmentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateEquipment([FromBody] CreateWarehouseEquipmentCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WarehouseEquipmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEquipment(Guid id, [FromBody] UpdateWarehouseEquipmentCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(WarehouseEquipmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEquipmentStatus(Guid id, [FromBody] UpdateEquipmentStatusRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdateWarehouseEquipmentStatusCommand(id, request.Status, request.Remarks), ct);
        return Ok(result);
    }
}

public sealed record UpdateEquipmentStatusRequest(WarehouseEquipmentStatus Status, string? Remarks = null);
