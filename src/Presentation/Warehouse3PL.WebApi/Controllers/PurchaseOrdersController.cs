using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Commands.CancelPurchaseOrder;
using Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Commands.CreatePurchaseOrder;
using Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Commands.SubmitPurchaseOrder;
using Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Queries.GetPurchaseOrderById;
using Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Queries.GetPurchaseOrders;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Inbound - Purchase Orders")]
public class PurchaseOrdersController : BaseApiController
{
    [HttpGet]
    [HasPermission(Permissions.Inbound.OrdersView)]
    [ProducesResponseType(typeof(PagedResult<PurchaseOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPurchaseOrders([FromQuery] GetPurchaseOrdersQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Inbound.OrdersView)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPurchaseOrderById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetPurchaseOrderByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    [HasPermission(Permissions.Inbound.OrdersCreate)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePurchaseOrder([FromBody] CreatePurchaseOrderCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.Inbound.OrdersEdit)]
    [ProducesResponseType(typeof(PurchaseOrderDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitPurchaseOrder([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new SubmitPurchaseOrderCommand(id), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Inbound.OrdersCancel)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelPurchaseOrder([FromRoute] Guid id, [FromBody] CancelPoRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new CancelPurchaseOrderCommand(id, request.Reason), ct);
        return Ok(result);
    }
}

public record CancelPoRequest(string Reason);

