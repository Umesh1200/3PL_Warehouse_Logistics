using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Putaway.Commands.ConfirmPutaway;
using Warehouse3PL.Application.Features.Putaway.Queries.RecommendPutawayLocation;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Putaway Management")]
public class PutawayController : BaseApiController
{
    [HttpGet("recommendations")]
    [HasPermission(Permissions.Inbound.Putaway)]
    [ProducesResponseType(typeof(List<PutawayRecommendation>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] Guid clientId,
        [FromQuery] Guid facilityId,
        [FromQuery] Guid itemId,
        [FromQuery] decimal quantity,
        [FromQuery] Guid? lotId,
        CancellationToken ct)
    {
        var result = await Mediator.Send(
            new RecommendPutawayLocationQuery(clientId, facilityId, itemId, quantity, lotId), ct);
        return Ok(result);
    }

    [HttpPost("confirm")]
    [HasPermission(Permissions.Inbound.Putaway)]
    [ProducesResponseType(typeof(PutawayTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmPutaway([FromBody] ConfirmPutawayCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }
}
