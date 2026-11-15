using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inventory.Commands.ReserveInventory;
using Warehouse3PL.Application.Features.Inventory.Commands.TransferInventory;
using Warehouse3PL.Application.Features.Inventory.Queries.GetStockOnHand;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Inventory & Ledger")]
public class InventoryController : BaseApiController
{
    [HttpGet("stock")]
    [HasPermission(Permissions.Inventory.View)]
    [ProducesResponseType(typeof(PagedResult<StockOnHandDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStock([FromQuery] GetStockOnHandQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpPost("transfer")]
    [HasPermission(Permissions.Inventory.Transfer)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> TransferStock([FromBody] TransferInventoryCommand command, CancellationToken ct)
    {
        var txId = await Mediator.Send(command, ct);
        return Ok(new { transactionId = txId, message = "Inventory transfer executed and ledger updated." });
    }

    [HttpPost("reserve")]
    [HasPermission(Permissions.Inventory.Adjust)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReserveStock([FromBody] ReserveInventoryCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(new { success = result, message = "Inventory reservation secured." });
    }
}
