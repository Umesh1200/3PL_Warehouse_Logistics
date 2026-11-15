using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inbound.Inspections.Commands.RecordInspectionDecision;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Inbound - Quality & Inspection")]
[Route("api/v1/inspections")]
public class InspectionsController : BaseApiController
{
    [HttpPost("decision")]
    [HasPermission(Permissions.Inbound.QualityInspect)]
    [ProducesResponseType(typeof(InspectionDecisionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordDecision([FromBody] RecordInspectionDecisionCommand command, CancellationToken ct)
    {
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }
}

