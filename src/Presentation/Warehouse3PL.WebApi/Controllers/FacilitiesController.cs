using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Facilities.Queries.GetAuthorizedFacilities;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Facilities & Physical Topography")]
public class FacilitiesController : BaseApiController
{
    /// <summary>
    /// Retrieve authorized facilities. Filtered to caller's tenant unless staff/admin.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Facilities.View)]
    [ProducesResponseType(typeof(List<FacilityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FacilityDto>>> GetFacilities(CancellationToken ct)
    {
        var response = await Mediator.Send(new GetAuthorizedFacilitiesQuery(), ct);
        return Ok(response);
    }
}
