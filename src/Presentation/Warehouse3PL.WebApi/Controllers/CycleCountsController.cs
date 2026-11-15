using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.CycleCounts.Commands.CreateCycleCount;
using Warehouse3PL.Application.Features.CycleCounts.Commands.RecordCycleCountItems;
using Warehouse3PL.Application.Features.CycleCounts.Commands.ReconcileCycleCount;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Inventory Control & Cycle Counting")]
public class CycleCountsController : BaseApiController
{
    [HttpPost]
    [HasPermission(Permissions.Inventory.Count)]
    [ProducesResponseType(typeof(CycleCountDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCycleCount([FromBody] CreateCycleCountCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{id:guid}/record")]
    [HasPermission(Permissions.Inventory.Count)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordCount(Guid id, [FromBody] List<CountObservationDto> observations, CancellationToken ct)
    {
        var result = await Mediator.Send(new RecordCycleCountItemsCommand(id, observations), ct);
        return Ok(new { success = result, message = "Cycle count items recorded." });
    }

    [HttpPost("{id:guid}/reconcile")]
    [HasPermission(Permissions.Inventory.Adjust)]
    [ProducesResponseType(typeof(CycleCountDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reconcile(Guid id, [FromBody] ReconcileCycleCountCommand command, CancellationToken ct)
    {
        var cmd = command with { CycleCountId = id };
        var result = await Mediator.Send(cmd, ct);
        return Ok(result);
    }
}
