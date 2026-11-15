using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Putaway.Strategies.Commands.CreatePutawayStrategy;
using Warehouse3PL.Application.Features.Putaway.Strategies.Queries.GetPutawayStrategies;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Putaway Strategy Rules")]
[Route("api/v1/putaway/strategies")]
public class PutawayStrategyController : BaseApiController
{
    /// <summary>
    /// Lists active putaway strategy rules configured for a facility.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Inbound.PutawayRules)]
    [ProducesResponseType(typeof(List<PutawayStrategyRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStrategies([FromQuery] Guid facilityId, [FromQuery] Guid? clientId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetPutawayStrategiesQuery(facilityId, clientId), ct);
        return Ok(result);
    }

    /// <summary>
    /// Configures a new directed putaway strategy rule.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Inbound.PutawayRules)]
    [ProducesResponseType(typeof(PutawayStrategyRuleDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateStrategy([FromBody] CreatePutawayStrategyCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetStrategies), new { facilityId = result.FacilityId }, result);
    }
}

