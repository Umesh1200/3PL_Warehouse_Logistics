using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Clients;

namespace Warehouse3PL.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/clients/{clientId:guid}/contacts")]
public class ClientContactsController : ControllerBase
{
    private readonly ISender _mediator;

    public ClientContactsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientContactDto>>> GetContacts(
        Guid clientId,
        [FromQuery] bool includeInactive = false)
    {
        var result = await _mediator.Send(new GetClientContactsQuery(clientId, includeInactive));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ClientContactDto>> CreateContact(
        Guid clientId,
        [FromBody] CreateClientContactCommand command)
    {
        if (command.ClientId != Guid.Empty && command.ClientId != clientId)
        {
            return BadRequest("Route clientId does not match body ClientId.");
        }

        var result = await _mediator.Send(command with { ClientId = clientId });
        return Created($"/api/v1/clients/{clientId}/contacts/{result.Id}", result);
    }

    [HttpPut("{contactId:guid}")]
    public async Task<ActionResult<ClientContactDto>> UpdateContact(
        Guid clientId,
        Guid contactId,
        [FromBody] UpdateClientContactCommand command)
    {
        if (command.ClientId != Guid.Empty && command.ClientId != clientId)
        {
            return BadRequest("Route clientId does not match body ClientId.");
        }
        if (command.ContactId != Guid.Empty && command.ContactId != contactId)
        {
            return BadRequest("Route contactId does not match body ContactId.");
        }

        var result = await _mediator.Send(command with { ClientId = clientId, ContactId = contactId });
        return Ok(result);
    }

    [HttpDelete("{contactId:guid}")]
    public async Task<IActionResult> DeleteContact(Guid clientId, Guid contactId)
    {
        await _mediator.Send(new DeleteClientContactCommand(clientId, contactId));
        return NoContent();
    }
}
