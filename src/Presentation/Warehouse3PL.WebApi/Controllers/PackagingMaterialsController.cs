using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Tags("Warehouse Master - Packaging Materials")]
[Route("api/v1/packaging-materials")]
public class PackagingMaterialsController : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PackagingMaterialDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPackagingMaterials(
        [FromQuery] Guid? facilityId = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] bool includeFacilityWide = true,
        [FromQuery] PackagingMaterialType? materialType = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await Mediator.Send(
            new GetPackagingMaterialsQuery(facilityId, clientId, includeFacilityWide, materialType, isActive, search, pageNumber, pageSize),
            ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PackagingMaterialDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPackagingMaterialById(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetPackagingMaterialByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(PackagingMaterialDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePackagingMaterial([FromBody] CreatePackagingMaterialCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PackagingMaterialDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePackagingMaterial(Guid id, [FromBody] UpdatePackagingMaterialCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(PackagingMaterialDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePackagingMaterialStatus(Guid id, [FromBody] UpdatePackagingMaterialStatusRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdatePackagingMaterialStatusCommand(id, request.IsActive), ct);
        return Ok(result);
    }
}

public sealed record UpdatePackagingMaterialStatusRequest(bool IsActive);
