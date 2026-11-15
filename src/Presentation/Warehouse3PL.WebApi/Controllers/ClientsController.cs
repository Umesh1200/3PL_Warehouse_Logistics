using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Clients.Commands.CreateClient;
using Warehouse3PL.Application.Features.Clients.Commands.DeactivateClient;
using Warehouse3PL.Application.Features.Clients.Commands.UpdateClient;
using Warehouse3PL.Application.Features.Clients.Queries.GetClientById;
using Warehouse3PL.Application.Features.Clients.Queries.GetClients;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Clients & Multi-Tenancy Master")]
public class ClientsController : BaseApiController
{
    /// <summary>
    /// Retrieve paginated list of client tenants. Staff sees all; clients see only their tenant partition.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Clients.View)]
    [ProducesResponseType(typeof(PagedResult<ClientDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ClientDto>>> GetClients([FromQuery] GetClientsQuery query, CancellationToken ct)
    {
        var response = await Mediator.Send(query, ct);
        return Ok(response);
    }

    /// <summary>
    /// Retrieve client tenant by unique UUID identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Clients.View)]
    [ProducesResponseType(typeof(ClientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientDto>> GetClientById([FromRoute] Guid id, CancellationToken ct)
    {
        var response = await Mediator.Send(new GetClientByIdQuery(id), ct);
        return Ok(response);
    }

    /// <summary>
    /// Create a new 3PL client tenant with account code, SLA tier, and currency.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Clients.Create)]
    [ProducesResponseType(typeof(ClientDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClientDto>> CreateClient([FromBody] CreateClientCommand command, CancellationToken ct)
    {
        var response = await Mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetClientById), new { id = response.Id }, response);
    }

    /// <summary>
    /// Update client tenant details, SLA tier, or status.
    /// </summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Clients.Edit)]
    [ProducesResponseType(typeof(ClientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClientDto>> UpdateClient([FromRoute] Guid id, [FromBody] UpdateClientCommand command, CancellationToken ct)
    {
        if (id != command.Id)
        {
            return BadRequest("Route id does not match request body id.");
        }

        var response = await Mediator.Send(command, ct);
        return Ok(response);
    }

    /// <summary>
    /// Deactivate / soft delete a client tenant.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Clients.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateClient([FromRoute] Guid id, CancellationToken ct)
    {
        await Mediator.Send(new DeactivateClientCommand(id), ct);
        return NoContent();
    }
}

