using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Outbound.Allocation.Commands;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.CancelOutboundOrder;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.CreateOutboundOrder;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.HoldOutboundOrder;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.ImportOutboundOrdersBulk;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.ReleaseOutboundOrder;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.CheckOrderStockAvailability;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.GetOutboundOrderById;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.GetOutboundOrders;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Outbound - Order Management")]
[Route("api/v1/outbound/orders")]
public class OutboundOrdersController : BaseApiController
{
    /// <summary>
    /// Creates a single outbound order (B2B Sales Order, Stock Transfer, or E-Commerce).
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Outbound.OrdersCreate)]
    [ProducesResponseType(typeof(OutboundOrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOutboundOrderCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Ingests a bulk batch of outbound orders, returning per-row success/failure audit logs.
    /// </summary>
    [HttpPost("bulk-import")]
    [HasPermission(Permissions.Outbound.OrdersImport)]
    [ProducesResponseType(typeof(OrderImportBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkImport([FromBody] ImportOutboundOrdersBulkCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Searches and paginates outbound orders with filters for facility, status, type, priority, and date range.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Outbound.OrdersView)]
    [ProducesResponseType(typeof(PagedResult<OutboundOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders([FromQuery] GetOutboundOrdersQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves comprehensive order details including line items, shipping address snapshot, and audit status history.
    /// </summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Outbound.OrdersView)]
    [ProducesResponseType(typeof(OutboundOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetOutboundOrderByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Assesses available on-hand stock for the order's lines across sound storage locations (Read-Only).
    /// </summary>
    [HttpGet("{id:guid}/stock-availability")]
    [HasPermission(Permissions.Outbound.OrdersView)]
    [ProducesResponseType(typeof(StockAvailabilityAssessmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckStockAvailability([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new CheckOrderStockAvailabilityQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Places an active order on hold, recording the reason and operator.
    /// </summary>
    [HttpPatch("{id:guid}/hold")]
    [HasPermission(Permissions.Outbound.OrdersManage)]
    [ProducesResponseType(typeof(OutboundOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HoldOrder([FromRoute] Guid id, [FromBody] HoldOrderRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new HoldOutboundOrderCommand { OrderId = id, Reason = request.Reason }, ct);
        return Ok(result);
    }

    /// <summary>
    /// Releases an on-hold order back to active processing, restoring its pre-hold status.
    /// </summary>
    [HttpPatch("{id:guid}/release")]
    [HasPermission(Permissions.Outbound.OrdersManage)]
    [ProducesResponseType(typeof(OutboundOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReleaseOrder([FromRoute] Guid id, [FromBody] ReleaseOrderRequest? request, CancellationToken ct)
    {
        var result = await Mediator.Send(new ReleaseOutboundOrderCommand { OrderId = id, Notes = request?.Notes }, ct);
        return Ok(result);
    }

    /// <summary>
    /// Cancels an outbound order and all open line items with justification audit logging.
    /// </summary>
    [HttpPatch("{id:guid}/cancel")]
    [HasPermission(Permissions.Outbound.OrdersCancel)]
    [ProducesResponseType(typeof(OutboundOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelOrder([FromRoute] Guid id, [FromBody] CancelOrderRequest request, CancellationToken ct)
    {
        var result = await Mediator.Send(new CancelOutboundOrderCommand { OrderId = id, Reason = request.Reason }, ct);
        return Ok(result);
    }

    /// <summary>
    /// Atomically executes hard inventory allocation for an order with optimistic concurrency and idempotency.
    /// </summary>
    [HttpPost("{id:guid}/allocate")]
    [HasPermission(Permissions.Outbound.OrdersAllocate)]
    [ProducesResponseType(typeof(AllocationExecutionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AllocateOrder(
        [FromRoute] Guid id,
        [FromBody] AllocateOrderRequest? request,
        CancellationToken ct)
    {
        var command = new AllocateOutboundOrderCommand(id, request?.StrategyOverride, request?.IdempotencyKey);
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// De-allocates all active inventory reservations for an order, restoring available on-hand balances.
    /// </summary>
    [HttpPost("{id:guid}/deallocate")]
    [HasPermission(Permissions.Outbound.OrdersAllocate)]
    [ProducesResponseType(typeof(DeallocationExecutionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeallocateOrder(
        [FromRoute] Guid id,
        [FromBody] DeallocateOrderRequest request,
        CancellationToken ct)
    {
        var command = new DeallocateOutboundOrderCommand(id, request.Reason);
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Releases a specific active inventory reservation or partial quantity thereof.
    /// </summary>
    [HttpPost("allocations/{allocationId:guid}/release")]
    [HasPermission(Permissions.Outbound.OrdersAllocate)]
    [ProducesResponseType(typeof(ReleaseAllocationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReleaseAllocation(
        [FromRoute] Guid allocationId,
        [FromBody] ReleaseSpecificAllocationRequest request,
        CancellationToken ct)
    {
        var command = new ReleaseAllocationCommand(allocationId, request?.QuantityToRelease, request?.Reason ?? "Manual release");
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }
}

public record HoldOrderRequest(string Reason);
public record ReleaseOrderRequest(string? Notes);
public record CancelOrderRequest(string Reason);
public record AllocateOrderRequest(AllocationStrategyType? StrategyOverride = null, string? IdempotencyKey = null);
public record DeallocateOrderRequest(string Reason);
public record ReleaseSpecificAllocationRequest(decimal? QuantityToRelease, string Reason);

