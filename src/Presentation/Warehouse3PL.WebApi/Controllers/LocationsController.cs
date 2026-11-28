using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Locations.Commands.BatchGenerateLocations;
using Warehouse3PL.Application.Features.Locations.Commands.CreateLocation;
using Warehouse3PL.Application.Features.Locations.Commands.LockLocation;
using Warehouse3PL.Application.Features.Locations.Commands.UnlockLocation;
using Warehouse3PL.Application.Features.Locations.Queries.GetLocationTree;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Tags("Warehouse Layout & Locations")]
public class LocationsController : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<LocationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLocations(
        [FromQuery] Guid? facilityId,
        [FromQuery] Guid? zoneId,
        [FromQuery] Guid? parentLocationId,
        [FromQuery] LocationType? locationType,
        [FromQuery] LocationStatus? status,
        [FromQuery] bool? isStorable,
        [FromQuery] bool? isLocked,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await Mediator.Send(
            new GetLocationsQuery(facilityId, zoneId, parentLocationId, locationType, status, isStorable, isLocked, search, pageNumber, pageSize),
            ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(LocationDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLocationById(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetLocationByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateLocation([FromBody] CreateLocationCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLocation(Guid id, [FromBody] UpdateLocationCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/move")]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> MoveLocation(Guid id, [FromBody] MoveLocationRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new MoveLocationCommand(id, request.NewParentLocationId, request.NewZoneId), ct);
        return Ok(result);
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLocationStatus(Guid id, [FromBody] UpdateLocationStatusRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdateLocationStatusCommand(id, request.Status, request.Remarks), ct);
        return Ok(result);
    }

    [HttpPost("batch-generate")]
    [ProducesResponseType(typeof(BatchGenerateLocationsResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchGenerate([FromBody] BatchGenerateLocationsCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpGet("tree")]
    [ProducesResponseType(typeof(List<LocationTreeNodeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLocationTree([FromQuery] Guid? facilityId, [FromQuery] Guid? zoneId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetLocationTreeQuery(facilityId, zoneId), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/locks")]
    [ProducesResponseType(typeof(List<LocationLockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLocationLocks(Guid id, [FromQuery] bool activeOnly = false, CancellationToken ct = default)
    {
        var result = await Mediator.Send(new GetLocationLocksQuery(id, activeOnly), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/lock")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public async Task<IActionResult> LockLocation(Guid id, [FromBody] LockLocationCommand command, CancellationToken ct)
    {
        var cmd = command with { LocationId = id };
        var lockId = await Mediator.Send(cmd, ct);
        return Ok(new { lockId, message = "Location has been locked successfully." });
    }

    [HttpPost("{id:guid}/unlock")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlockLocation(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new UnlockLocationCommand(id), ct);
        return Ok(new { success = result, message = "Location has been unlocked." });
    }

    [HttpPost("{id:guid}/locks/{lockId:guid}/release")]
    [ProducesResponseType(typeof(LocationLockDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReleaseLocationLock(Guid id, Guid lockId, CancellationToken ct)
    {
        var result = await Mediator.Send(new ReleaseLocationLockCommand(id, lockId), ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/storage-profile")]
    [ProducesResponseType(typeof(LocationStorageProfileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertStorageProfile(Guid id, [FromBody] UpsertLocationStorageProfileCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command with { LocationId = id }, ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/operational-profile")]
    [ProducesResponseType(typeof(LocationOperationalProfileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertOperationalProfile(Guid id, [FromBody] UpsertLocationOperationalProfileCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command with { LocationId = id }, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/equipment-requirements")]
    [ProducesResponseType(typeof(LocationEquipmentRequirementDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddEquipmentRequirement(Guid id, [FromBody] AddEquipmentRequirementRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new AddLocationEquipmentRequirementCommand(id, request.EquipmentType, request.IsMandatory), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpDelete("{id:guid}/equipment-requirements/{requirementId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveEquipmentRequirement(Guid id, Guid requirementId, CancellationToken ct)
    {
        await Mediator.Send(new RemoveLocationEquipmentRequirementCommand(id, requirementId), ct);
        return NoContent();
    }
}

public sealed record MoveLocationRequest(Guid? NewParentLocationId, Guid? NewZoneId = null);
public sealed record UpdateLocationStatusRequest(LocationStatus Status, string? Remarks = null);
public sealed record AddEquipmentRequirementRequest(WarehouseEquipmentType EquipmentType, bool IsMandatory = false);
