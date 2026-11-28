using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Clients.Commands.CreateClient;
using Warehouse3PL.Application.Features.Clients.Commands.DeactivateClient;
using Warehouse3PL.Application.Features.Clients.Commands.UpdateClient;
using Warehouse3PL.Application.Features.Clients.Queries.GetClientById;
using Warehouse3PL.Application.Features.Clients.Queries.GetClients;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/clients")]
public class ClientsController : ControllerBase
{
    private readonly ISender _mediator;

    public ClientsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ClientDto>>> GetClients([FromQuery] GetClientsQuery query)
    {
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientDto>> GetClientById(Guid id)
    {
        var result = await _mediator.Send(new GetClientByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Clients.Create)]
    public async Task<ActionResult<ClientDto>> CreateClient([FromBody] CreateClientCommand command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetClientById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Clients.Edit)]
    public async Task<ActionResult<ClientDto>> UpdateClient(Guid id, [FromBody] UpdateClientCommand command)
    {
        if (command.Id != Guid.Empty && id != command.Id)
        {
            return BadRequest("Route ID does not match body ID.");
        }

        var result = await _mediator.Send(command with { Id = id });
        return Ok(result);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ClientDto>> TransitionClientStatus(
        Guid id,
        [FromBody] TransitionClientStatusRequest request)
    {
        var result = await _mediator.Send(new TransitionClientStatusCommand(id, request.TargetStatus, request.ExpectedConfigRevision));
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Clients.Delete)]
    public async Task<IActionResult> DeactivateClient(Guid id)
    {
        await _mediator.Send(new DeactivateClientCommand(id));
        return NoContent();
    }
}

public record TransitionClientStatusRequest(ClientStatus TargetStatus, int? ExpectedConfigRevision = null);
