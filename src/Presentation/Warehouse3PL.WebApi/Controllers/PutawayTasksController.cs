using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.AssignPutawayTask;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ConfirmPutawayTask;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.GeneratePutawayTasks;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ReportPutawayException;
using Warehouse3PL.Application.Features.Putaway.Tasks.Queries.GetNextPutawayTask;
using Warehouse3PL.Application.Features.Putaway.Tasks.Queries.GetPutawayTasks;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Putaway Tasks Management")]
[Route("api/v1/putaway/tasks")]
public class PutawayTasksController : BaseApiController
{
    /// <summary>
    /// Lists putaway tasks with pagination, facility, status, and tenant confinement.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Inbound.View)]
    [ProducesResponseType(typeof(PagedResult<PutawayTaskDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTasks([FromQuery] GetPutawayTasksQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Generates directed putaway tasks for all unassigned LPNs in transient staging.
    /// </summary>
    [HttpPost("generate")]
    [HasPermission(Permissions.Inbound.PutawayManage)]
    [ProducesResponseType(typeof(List<PutawayTaskDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateTasks([FromBody] GeneratePutawayTasksCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Fetches the highest-priority pending putaway task for RF mobile operators.
    /// </summary>
    [HttpGet("next")]
    [HasPermission(Permissions.Inbound.PutawayExecute)]
    [ProducesResponseType(typeof(PutawayTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetNextTask(
        [FromQuery] Guid facilityId,
        [FromQuery] Guid? operatorUserId,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new GetNextPutawayTaskQuery(facilityId, operatorUserId), ct);
        if (result == null)
        {
            return NoContent();
        }
        return Ok(result);
    }

    /// <summary>
    /// Assigns or claims a directed putaway task for an RF mobile operator.
    /// </summary>
    [HttpPost("{id:guid}/assign")]
    [HasPermission(Permissions.Inbound.PutawayExecute)]
    [ProducesResponseType(typeof(PutawayTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignTask(Guid id, [FromBody] AssignTaskRequest request, CancellationToken ct)
    {
        var command = new AssignPutawayTaskCommand
        {
            TaskId = id,
            UserId = request.UserId
        };
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Validates dual-scans (LPN container + Target bin) and atomically relocates inventory in ledger.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [HasPermission(Permissions.Inbound.PutawayExecute)]
    [ProducesResponseType(typeof(PutawayTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmTask(Guid id, [FromBody] ConfirmPutawayTaskRequest request, CancellationToken ct)
    {
        var command = new ConfirmPutawayTaskCommand
        {
            TaskId = id,
            ScannedLpn = request.ScannedLpn,
            ScannedLocationBarcode = request.ScannedLocationBarcode,
            AllowOverride = request.AllowOverride
        };
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Reports an obstructed/damaged bin exception and triggers dynamic location re-routing.
    /// </summary>
    [HttpPost("{id:guid}/exception")]
    [HasPermission(Permissions.Inbound.PutawayExecute)]
    [ProducesResponseType(typeof(PutawayTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReportException(Guid id, [FromBody] ReportExceptionRequest request, CancellationToken ct)
    {
        var command = new ReportPutawayExceptionCommand
        {
            TaskId = id,
            ExceptionReason = request.ExceptionReason,
            AutoReRoute = request.AutoReRoute
        };
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }
}

public record AssignTaskRequest(Guid? UserId);
public record ConfirmPutawayTaskRequest(string ScannedLpn, string ScannedLocationBarcode, bool AllowOverride = false);
public record ReportExceptionRequest(string ExceptionReason, bool AutoReRoute = true);

