using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ApproveReceivingSession;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.CompleteReceivingSession;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.RecordReceivingDiscrepancy;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ResolveReceivingDiscrepancy;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ScanReceiptLine;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.StartReceivingSession;
using Warehouse3PL.Application.Features.Inbound.Receiving.Queries.GetReceivingSessionById;
using Warehouse3PL.Application.Features.Inbound.Receiving.Queries.GetReceivingSessions;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Inbound - Goods Receiving")]
[Route("api/v1/receiving")]
public class ReceivingController : BaseApiController
{
    [HttpGet("sessions")]
    [HasPermission(Permissions.Inbound.View)]
    [ProducesResponseType(typeof(PagedResult<ReceivingSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions([FromQuery] GetReceivingSessionsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpGet("sessions/{id:guid}")]
    [HasPermission(Permissions.Inbound.View)]
    [ProducesResponseType(typeof(ReceivingSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSessionById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetReceivingSessionByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost("sessions/start")]
    [HasPermission(Permissions.Inbound.Receive)]
    [ProducesResponseType(typeof(ReceivingSessionDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> StartSession([FromBody] StartReceivingSessionCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("sessions/{id:guid}/lines")]
    [HasPermission(Permissions.Inbound.Receive)]
    [ProducesResponseType(typeof(ReceiptLineDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ScanLine(
        [FromRoute] Guid id,
        [FromBody] ScanReceiptLineRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ScanReceiptLineCommand
        {
            ReceivingSessionId = id,
            ItemId = request.ItemId,
            QuantityReceived = request.QuantityReceived,
            Lpn = request.Lpn,
            PurchaseOrderLineId = request.PurchaseOrderLineId,
            AsnLineItemId = request.AsnLineItemId,
            LotNumber = request.LotNumber,
            SerialNumber = request.SerialNumber,
            CatchWeightKg = request.CatchWeightKg,
            ManufactureDate = request.ManufactureDate,
            ExpirationDate = request.ExpirationDate,
            Condition = request.Condition,
            StagingLocationId = request.StagingLocationId,
            Notes = request.Notes
        }, ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("sessions/{id:guid}/discrepancies")]
    [HasPermission(Permissions.Inbound.Receive)]
    [ProducesResponseType(typeof(ReceivingDiscrepancyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordDiscrepancy(
        [FromRoute] Guid id,
        [FromBody] RecordReceivingDiscrepancyRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new RecordReceivingDiscrepancyCommand
        {
            ReceivingSessionId = id,
            ReceiptLineId = request.ReceiptLineId,
            Type = request.Type,
            DiscrepantQuantity = request.DiscrepantQuantity,
            Notes = request.Notes
        }, ct);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("sessions/{id:guid}/complete")]
    [HasPermission(Permissions.Inbound.Receive)]
    [ProducesResponseType(typeof(ReceivingSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteSession([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new CompleteReceivingSessionCommand { ReceivingSessionId = id }, ct);
        return Ok(result);
    }

    [HttpPost("sessions/{id:guid}/approve")]
    [HasPermission(Permissions.Inbound.Approve)]
    [ProducesResponseType(typeof(ReceivingSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveSession([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new ApproveReceivingSessionCommand(id), ct);
        return Ok(result);
    }

    [HttpPost("sessions/{id:guid}/discrepancies/{discrepancyId:guid}/resolve")]
    [HasPermission(Permissions.Inbound.DiscrepancyResolve)]
    [ProducesResponseType(typeof(ReceivingDiscrepancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveDiscrepancy(
        [FromRoute] Guid id,
        [FromRoute] Guid discrepancyId,
        [FromBody] ResolveDiscrepancyRequest request,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ResolveReceivingDiscrepancyCommand
        {
            ReceivingSessionId = id,
            DiscrepancyId = discrepancyId,
            Resolution = request.Resolution,
            Notes = request.Notes
        }, ct);
        return Ok(result);
    }
}

public record ResolveDiscrepancyRequest
{
    public DiscrepancyResolutionStatus Resolution { get; init; }
    public string? Notes { get; init; }
}

public record ScanReceiptLineRequest
{
    public Guid ItemId { get; init; }
    public decimal QuantityReceived { get; init; }
    public string? Lpn { get; init; }
    public Guid? PurchaseOrderLineId { get; init; }
    public Guid? AsnLineItemId { get; init; }
    public string? LotNumber { get; init; }
    public string? SerialNumber { get; init; }
    public decimal? CatchWeightKg { get; init; }
    public DateTime? ManufactureDate { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public ReceiptConditionStatus Condition { get; init; } = ReceiptConditionStatus.Sound;
    public Guid? StagingLocationId { get; init; }
    public string? Notes { get; init; }
}

public record RecordReceivingDiscrepancyRequest
{
    public Guid? ReceiptLineId { get; init; }
    public DiscrepancyType Type { get; init; }
    public decimal DiscrepantQuantity { get; init; }
    public string Notes { get; init; } = string.Empty;
}

