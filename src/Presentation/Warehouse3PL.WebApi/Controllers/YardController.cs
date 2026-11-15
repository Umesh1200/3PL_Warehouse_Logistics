using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.AssignDockDoor;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.CheckInVehicle;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.ConfirmAtDock;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.RecordVehicleDeparture;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Commands.ReleaseDockDoor;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Queries.GetReceivingQueue;
using Warehouse3PL.Application.Features.Inbound.DockManagement.Yard.Queries.GetYardVisits;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Inbound - Yard & Dock Management")]
[Route("api/v1/yard")]
public class YardController : BaseApiController
{
    [HttpGet("visits")]
    [HasPermission(Permissions.Inbound.YardView)]
    [ProducesResponseType(typeof(PagedResult<YardVisitDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVisits([FromQuery] GetYardVisitsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpGet("queue")]
    [HasPermission(Permissions.Inbound.YardView)]
    [ProducesResponseType(typeof(List<ReceivingQueueItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReceivingQueue([FromQuery] Guid facilityId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetReceivingQueueQuery { FacilityId = facilityId }, ct);
        return Ok(result);
    }

    [HttpPost("check-in")]
    [HasPermission(Permissions.Inbound.YardCheckIn)]
    [ProducesResponseType(typeof(YardVisitDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CheckInVehicle([FromBody] CheckInVehicleCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("assign-dock")]
    [HasPermission(Permissions.Inbound.DockAssign)]
    [ProducesResponseType(typeof(DockAssignmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignDock([FromBody] AssignDockDoorCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("assignments/{id:guid}/confirm-dock")]
    [HasPermission(Permissions.Inbound.DockAssign)]
    [ProducesResponseType(typeof(DockAssignmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmAtDock([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new ConfirmAtDockCommand { DockAssignmentId = id }, ct);
        return Ok(result);
    }

    [HttpPost("assignments/{id:guid}/release-dock")]
    [HasPermission(Permissions.Inbound.DockRelease)]
    [ProducesResponseType(typeof(DockAssignmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReleaseDock(
        [FromRoute] Guid id,
        [FromBody] ReleaseDockDoorRequest? request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ReleaseDockDoorCommand
        {
            DockAssignmentId = id,
            Notes = request?.Notes
        }, ct);

        return Ok(result);
    }

    [HttpPost("visits/{id:guid}/depart")]
    [HasPermission(Permissions.Inbound.YardCheckIn)]
    [ProducesResponseType(typeof(YardVisitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordDeparture([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new RecordVehicleDepartureCommand { YardVisitId = id }, ct);
        return Ok(result);
    }
}

public record ReleaseDockDoorRequest
{
    public string? Notes { get; init; }
}

