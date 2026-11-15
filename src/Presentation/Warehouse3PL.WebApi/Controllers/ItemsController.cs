using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Items.Commands.CreateItem;
using Warehouse3PL.Application.Features.Items.Queries.GetItems;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Product & SKU Catalog")]
public class ItemsController : BaseApiController
{
    [HttpGet]
    [HasPermission(Permissions.Items.View)]
    [ProducesResponseType(typeof(PagedResult<ItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetItems([FromQuery] GetItemsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpPost]
    [HasPermission(Permissions.Items.Create)]
    [ProducesResponseType(typeof(ItemDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateItem([FromBody] CreateItemCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
