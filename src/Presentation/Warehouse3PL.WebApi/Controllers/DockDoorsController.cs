using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.CreateDockDoor;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.UpdateDockDoorStatus;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Queries.GetDockDoors;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Inbound - Dock Doors")]
[Route("api/v1/dock-doors")]
public class DockDoorsController : BaseApiController
{
    [HttpGet]
    [HasPermission(Permissions.Facilities.DockDoorsView)]
    [ProducesResponseType(typeof(PagedResult<DockDoorDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDockDoors([FromQuery] GetDockDoorsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpPost]
    [HasPermission(Permissions.Facilities.DockDoorsManage)]
    [ProducesResponseType(typeof(DockDoorDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDockDoor([FromBody] CreateDockDoorCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("{id:guid}/status")]
    [HasPermission(Permissions.Facilities.DockDoorsManage)]
    [ProducesResponseType(typeof(DockDoorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDockDoorStatus(
        [FromRoute] Guid id,
        [FromBody] UpdateDockDoorStatusRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdateDockDoorStatusCommand
        {
            Id = id,
            Status = request.Status,
            Notes = request.Notes
        }, ct);

        return Ok(result);
    }
}

public record UpdateDockDoorStatusRequest
{
    public DockDoorStatus Status { get; init; }
    public string? Notes { get; init; }
}

