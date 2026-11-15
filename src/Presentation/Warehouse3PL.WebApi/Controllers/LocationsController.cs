using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Locations.Commands.BatchGenerateLocations;
using Warehouse3PL.Application.Features.Locations.Commands.CreateLocation;
using Warehouse3PL.Application.Features.Locations.Commands.LockLocation;
using Warehouse3PL.Application.Features.Locations.Commands.UnlockLocation;
using Warehouse3PL.Application.Features.Locations.Queries.GetLocationTree;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Warehouse Layout & Locations")]
public class LocationsController : BaseApiController
{
    [HttpPost]
    [HasPermission(Permissions.Facilities.Manage)]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateLocation([FromBody] CreateLocationCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("batch-generate")]
    [HasPermission(Permissions.Facilities.Manage)]
    [ProducesResponseType(typeof(BatchGenerateLocationsResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchGenerate([FromBody] BatchGenerateLocationsCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpGet("tree")]
    [HasPermission(Permissions.Facilities.View)]
    [ProducesResponseType(typeof(List<LocationTreeNodeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLocationTree([FromQuery] Guid facilityId, [FromQuery] Guid? zoneId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetLocationTreeQuery(facilityId, zoneId), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/lock")]
    [HasPermission(Permissions.Facilities.Manage)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<IActionResult> LockLocation(Guid id, [FromBody] LockLocationCommand command, CancellationToken ct)
    {
        var cmd = command with { LocationId = id };
        var lockId = await Mediator.Send(cmd, ct);
        return Ok(new { lockId, message = "Location has been locked successfully." });
    }

    [HttpPost("{id:guid}/unlock")]
    [HasPermission(Permissions.Facilities.Manage)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlockLocation(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new UnlockLocationCommand(id), ct);
        return Ok(new { success = result, message = "Location has been unlocked." });
    }
}
