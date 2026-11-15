using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Features.Outbound.Picking.Commands;
using Warehouse3PL.Application.Features.Outbound.Picking.DTOs;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Outbound - Picking & RF Task Execution")]
[Route("api/v1/outbound/pick-tasks")]
public class PickTasksController : BaseApiController
{
    private readonly IApplicationDbContext _context;

    public PickTasksController(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Assigns a pick task to an RF warehouse operator.
    /// </summary>
    [HttpPost("{id:guid}/assign")]
    [HasPermission(Permissions.Outbound.TasksManage)]
    [ProducesResponseType(typeof(PickTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignTask([FromRoute] Guid id, [FromBody] AssignPickTaskRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new AssignPickTaskCommand(id, request.OperatorUserId), ct);
        return Ok(result);
    }

    /// <summary>
    /// Confirms physical RF picking of inventory from storage bin into destination tote/cart.
    /// Validates barcodes, executes atomic stock balance movement under allocated state, and records Pick ledger transaction.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [HasPermission(Permissions.Outbound.Pick)]
    [ProducesResponseType(typeof(PickConfirmationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmPick([FromRoute] Guid id, [FromBody] ConfirmPickApiRequest request, CancellationToken ct)
    {
        var command = new ConfirmPickTaskCommand(
            id,
            request.ScannedLocationBarcode,
            request.ScannedItemBarcode,
            request.ScannedLotNumber,
            request.ScannedToteLpn,
            request.Quantity,
            request.IdempotencyKey);

        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Reports a short-pick exception where physical stock cannot satisfy task demand.
    /// Adjusts allocation, creates discrepancy audit, and schedules automated cycle count investigation.
    /// </summary>
    [HttpPost("{id:guid}/short-pick")]
    [HasPermission(Permissions.Outbound.Pick)]
    [ProducesResponseType(typeof(ShortPickExecutionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReportShortPick([FromRoute] Guid id, [FromBody] ReportShortPickApiRequest request, CancellationToken ct)
    {
        var command = new ReportShortPickCommand(
            id,
            request.QuantityPicked,
            request.Reason,
            request.Notes,
            request.ScannedLocationBarcode,
            request.ScannedItemBarcode,
            request.ScannedToteLpn,
            request.IdempotencyKey);

        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves full execution details and status history of a pick task.
    /// </summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Outbound.TasksView)]
    [ProducesResponseType(typeof(PickTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaskById([FromRoute] Guid id, CancellationToken ct)
    {
        var task = await _context.PickTasks
            .Include(t => t.SourceLocation)
            .Include(t => t.DestinationLocation)
            .Include(t => t.Item)
            .Include(t => t.Lot)
            .Include(t => t.OutboundOrder)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (task == null)
        {
            throw new EntityNotFoundException(nameof(task), id);
        }

        return Ok(new PickTaskDto
        {
            Id = task.Id,
            PickWaveId = task.PickWaveId,
            OutboundOrderId = task.OutboundOrderId,
            OrderNumber = task.OutboundOrder?.OrderNumber ?? string.Empty,
            OutboundOrderLineId = task.OutboundOrderLineId,
            InventoryAllocationId = task.InventoryAllocationId,
            StockOnHandId = task.StockOnHandId,
            ItemId = task.ItemId,
            Sku = task.Item?.Sku ?? string.Empty,
            ItemDescription = task.Item?.Description ?? string.Empty,
            SourceLocationId = task.SourceLocationId,
            SourceLocationCode = task.SourceLocation?.LocationCode ?? string.Empty,
            DestinationLocationId = task.DestinationLocationId,
            DestinationLocationCode = task.DestinationLocation?.LocationCode,
            LotId = task.LotId,
            ExpectedLpnCode = task.ExpectedLpnCode,
            TargetHandlingUnitId = task.TargetHandlingUnitId,
            TargetToteBarcode = task.TargetToteBarcode,
            QuantityToPick = task.QuantityToPick,
            QuantityPicked = task.QuantityPicked,
            ShortPickQuantity = task.ShortPickQuantity,
            PickSequence = task.PickSequence,
            Status = task.Status,
            ShortPickReason = task.ShortPickReason,
            AssignedUserId = task.AssignedUserId,
            StartedAtUtc = task.StartedAtUtc,
            CompletedAtUtc = task.CompletedAtUtc
        });
    }
}

public record AssignPickTaskRequest(Guid OperatorUserId);

public record ConfirmPickApiRequest(
    string ScannedLocationBarcode,
    string ScannedItemBarcode,
    string? ScannedLotNumber,
    string ScannedToteLpn,
    decimal Quantity,
    string? IdempotencyKey = null);

public record ReportShortPickApiRequest(
    decimal QuantityPicked,
    Domain.Enums.ShortPickReason Reason,
    string? Notes = null,
    string? ScannedLocationBarcode = null,
    string? ScannedItemBarcode = null,
    string? ScannedToteLpn = null,
    string? IdempotencyKey = null);

