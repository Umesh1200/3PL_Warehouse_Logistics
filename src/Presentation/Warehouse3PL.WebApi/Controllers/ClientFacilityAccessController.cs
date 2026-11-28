using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Clients;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/clients/{clientId:guid}/facilities")]
public class ClientFacilityAccessController : ControllerBase
{
    private readonly ISender _mediator;

    public ClientFacilityAccessController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientFacilityAccessDto>>> GetFacilityAccesses(Guid clientId)
    {
        var result = await _mediator.Send(new GetClientFacilityAccessesQuery(clientId));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ClientFacilityAccessDto>> GrantAccess(
        Guid clientId,
        [FromBody] GrantClientFacilityAccessCommand command)
    {
        if (command.ClientId != Guid.Empty && command.ClientId != clientId)
        {
            return BadRequest("Route clientId does not match body ClientId.");
        }

        var result = await _mediator.Send(command with { ClientId = clientId });
        return Created($"/api/v1/clients/{clientId}/facilities", result);
    }

    [HttpPatch("{facilityId:guid}/status")]
    public async Task<ActionResult<ClientFacilityAccessDto>> UpdateAccessStatus(
        Guid clientId,
        Guid facilityId,
        [FromBody] UpdateClientFacilityAccessStatusRequest request)
    {
        var result = await _mediator.Send(new UpdateClientFacilityAccessStatusCommand
        {
            ClientId = clientId,
            FacilityId = facilityId,
            Status = request.Status,
            PrimaryOperatingFacility = request.PrimaryOperatingFacility,
            Remarks = request.Remarks,
            ExpectedConfigRevision = request.ExpectedConfigRevision
        });
        return Ok(result);
    }
}

public record UpdateClientFacilityAccessStatusRequest(
    FacilityAccessStatus Status,
    bool? PrimaryOperatingFacility = null,
    string? Remarks = null,
    int? ExpectedConfigRevision = null);
