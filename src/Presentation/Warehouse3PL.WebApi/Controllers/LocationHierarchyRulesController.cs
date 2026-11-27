using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Features.WarehouseMaster;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Tags("Warehouse Master - Location Hierarchy Rules")]
[Route("api/v1/location-hierarchy-rules")]
public class LocationHierarchyRulesController : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(List<LocationHierarchyRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRules(
        [FromQuery] Guid? facilityId,
        [FromQuery] bool includeGlobal = true,
        CancellationToken ct = default)
    {
        var result = await Mediator.Send(new GetLocationHierarchyRulesQuery(facilityId, includeGlobal), ct);
        return Ok(result);
    }

    [HttpPut]
    [ProducesResponseType(typeof(LocationHierarchyRuleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertRule([FromBody] UpsertLocationHierarchyRuleCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }
}
