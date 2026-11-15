using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inbound.Suppliers.Commands.CreateSupplier;
using Warehouse3PL.Application.Features.Inbound.Suppliers.Queries.GetSuppliers;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Inbound - Suppliers")]
public class SuppliersController : BaseApiController
{
    [HttpGet]
    [HasPermission(Permissions.Inbound.SuppliersView)]
    [ProducesResponseType(typeof(PagedResult<SupplierDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSuppliers([FromQuery] GetSuppliersQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpPost]
    [HasPermission(Permissions.Inbound.SuppliersCreate)]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}

