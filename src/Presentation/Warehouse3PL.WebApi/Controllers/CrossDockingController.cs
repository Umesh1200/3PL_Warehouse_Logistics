using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.CrossDock.Opportunities.Queries.DetectCrossDockOpportunities;
using Warehouse3PL.Application.Features.CrossDock.Plans.Commands.CancelCrossDockPlan;
using Warehouse3PL.Application.Features.CrossDock.Plans.Commands.GenerateCrossDockPlan;
using Warehouse3PL.Application.Features.CrossDock.Plans.Queries.GetCrossDockPlanById;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.AssignCrossDockTask;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.ConfirmCrossDockTask;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.ReportCrossDockException;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Queries.GetNextCrossDockTask;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Cross-Docking & Flow-Through")]
[Route("api/v1/cross-dock")]
public class CrossDockingController : BaseApiController
{
    /// <summary>
    /// Detects cross-docking opportunities by matching staging inventory against unfulfilled outbound demand.
    /// </summary>
    [HttpGet("opportunities")]
    [HasPermission(Permissions.Inbound.CrossDockView)]
    [ProducesResponseType(typeof(List<CrossDockOpportunityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DetectOpportunities(
        [FromQuery] Guid facilityId,
        [FromQuery] Guid? clientId,
        [FromQuery] Guid? receivingSessionId,
        CancellationToken ct)
    {
        var result = await Mediator.Send(
            new DetectCrossDockOpportunitiesQuery(facilityId, clientId, receivingSessionId), ct);
        return Ok(result);
    }

    /// <summary>
    /// Generates a cross-dock plan and reserves stock from staging to outbound orders.
    /// </summary>
    [HttpPost("plans/generate")]
    [HasPermission(Permissions.Inbound.CrossDockPlan)]
    [ProducesResponseType(typeof(CrossDockPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GeneratePlan(
        [FromBody] GenerateCrossDockPlanCommand command,
        CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves full details of a specific cross-dock plan.
    /// </summary>
    [HttpGet("plans/{id:guid}")]
    [HasPermission(Permissions.Inbound.CrossDockView)]
    [ProducesResponseType(typeof(CrossDockPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlanById(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetCrossDockPlanByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Cancels a cross-dock plan, releasing all reserved allocations back to available putaway stock.
    /// </summary>
    [HttpPost("plans/{id:guid}/cancel")]
    [HasPermission(Permissions.Inbound.CrossDockPlan)]
    [ProducesResponseType(typeof(CrossDockPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelPlan(Guid id, [FromBody] CancelPlanRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new CancelCrossDockPlanCommand(id, request.Reason), ct);
        return Ok(result);
    }

    /// <summary>
    /// Fetches the highest-priority pending cross-dock task for RF operators (Cold-chain and urgent orders first).
    /// </summary>
    [HttpGet("tasks/next")]
    [HasPermission(Permissions.Inbound.CrossDockExecute)]
    [ProducesResponseType(typeof(CrossDockTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetNextTask(
        [FromQuery] Guid facilityId,
        [FromQuery] Guid? operatorUserId,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new GetNextCrossDockTaskQuery(facilityId, operatorUserId), ct);
        if (result == null)
        {
            return NoContent();
        }
        return Ok(result);
    }

    /// <summary>
    /// Claims or assigns a cross-dock task to a warehouse worker.
    /// </summary>
    [HttpPost("tasks/{id:guid}/assign")]
    [HasPermission(Permissions.Inbound.CrossDockExecute)]
    [ProducesResponseType(typeof(CrossDockTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignTask(Guid id, [FromBody] AssignCrossDockTaskRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new AssignCrossDockTaskCommand
        {
            TaskId = id,
            UserId = request.UserId
        }, ct);
        return Ok(result);
    }

    /// <summary>
    /// Confirms dual-scans (LPN + destination) and commits fast-path inventory transfer to the ledger.
    /// </summary>
    [HttpPost("tasks/{id:guid}/confirm")]
    [HasPermission(Permissions.Inbound.CrossDockExecute)]
    [ProducesResponseType(typeof(CrossDockTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmTask(Guid id, [FromBody] ConfirmCrossDockTaskRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new ConfirmCrossDockTaskCommand
        {
            TaskId = id,
            ScannedLpn = request.ScannedLpn,
            ScannedDestinationBarcode = request.ScannedDestinationBarcode,
            AllowOverride = request.AllowOverride
        }, ct);
        return Ok(result);
    }

    /// <summary>
    /// Reports a cross-dock execution exception (shortage, damage, blockage) and releases discrepant stock.
    /// </summary>
    [HttpPost("tasks/{id:guid}/exception")]
    [HasPermission(Permissions.Inbound.CrossDockExecute)]
    [ProducesResponseType(typeof(CrossDockExceptionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReportException(Guid id, [FromBody] ReportCrossDockExceptionRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new ReportCrossDockExceptionCommand
        {
            TaskId = id,
            Type = request.Type,
            DiscrepantQuantity = request.DiscrepantQuantity,
            Reason = request.Reason,
            ReleaseRemainingStock = request.ReleaseRemainingStock
        }, ct);
        return Ok(result);
    }
}

public record CancelPlanRequest(string Reason);
public record AssignCrossDockTaskRequest(Guid? UserId);
public record ConfirmCrossDockTaskRequest(string ScannedLpn, string ScannedDestinationBarcode, bool AllowOverride = false);
public record ReportCrossDockExceptionRequest(
    CrossDockExceptionType Type,
    decimal DiscrepantQuantity,
    string Reason,
    bool ReleaseRemainingStock = true);

