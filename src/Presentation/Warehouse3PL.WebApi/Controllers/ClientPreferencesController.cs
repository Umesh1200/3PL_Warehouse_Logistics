using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Clients;

namespace Warehouse3PL.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/clients/{clientId:guid}/preferences")]
public class ClientPreferencesController : ControllerBase
{
    private readonly ISender _mediator;

    public ClientPreferencesController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientOperationalPreferenceDto>>> GetPreferences(Guid clientId)
    {
        var result = await _mediator.Send(new GetClientOperationalPreferencesQuery(clientId));
        return Ok(result);
    }

    [HttpGet("effective/{facilityId:guid}")]
    public async Task<ActionResult<ClientOperationalPreferenceDto>> GetEffectivePreference(Guid clientId, Guid facilityId)
    {
        var result = await _mediator.Send(new GetEffectiveClientOperationalPreferenceQuery(clientId, facilityId));
        if (result == null)
        {
            return NotFound();
        }
        return Ok(result);
    }

    [HttpPut]
    public async Task<ActionResult<ClientOperationalPreferenceDto>> UpsertPreference(
        Guid clientId,
        [FromBody] UpsertClientOperationalPreferenceCommand command)
    {
        if (command.ClientId != Guid.Empty && command.ClientId != clientId)
        {
            return BadRequest("Route clientId does not match body ClientId.");
        }

        var result = await _mediator.Send(command with { ClientId = clientId });
        return Ok(result);
    }

    [HttpDelete("{preferenceId:guid}")]
    public async Task<IActionResult> DeletePreference(Guid clientId, Guid preferenceId)
    {
        await _mediator.Send(new DeleteClientOperationalPreferenceCommand(clientId, preferenceId));
        return NoContent();
    }
}
