using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Outbound.Picking.Commands;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Application.Features.Outbound.Picking.Queries;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Outbound - Picking & Wave Execution")]
[Route("api/v1/outbound/pick-waves")]
public class PickWavesController : BaseApiController
{
    /// <summary>
    /// Generates a new pick wave, tasks from active allocations, and optimized serpentine pick paths.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Outbound.WavesManage)]
    [ProducesResponseType(typeof(PickWaveDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GenerateWave([FromBody] GeneratePickWaveCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Retrieves paginated pick waves for a facility.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Outbound.WavesView)]
    [ProducesResponseType(typeof(PagedResult<PickWaveDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWaves([FromQuery] GetPickWavesQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves full details of a pick wave by ID, including assigned orders and sequenced pick tasks.
    /// </summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Outbound.WavesView)]
    [ProducesResponseType(typeof(PickWaveDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWaveById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetPickWaveByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Releases a planning pick wave to the warehouse floor for operator execution.
    /// </summary>
    [HttpPost("{id:guid}/release")]
    [HasPermission(Permissions.Outbound.WavesManage)]
    [ProducesResponseType(typeof(PickWaveDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReleaseWave([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new ReleasePickWaveCommand(id), ct);
        return Ok(result);
    }
}

