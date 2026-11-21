using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Features.Schemas;
using Warehouse3PL.Application.Features.Schemas.Queries.GetEntitySchema;

namespace Warehouse3PL.WebApi.Controllers;

[Authorize]
[Tags("Metadata Schema Engine")]
public class SchemasController : BaseApiController
{
    /// <summary>
    /// Retrieve authoritative, permission-resolved EntityDefinition schema for a given entity.
    /// </summary>
    /// <param name="entityName">Canonical entity name (e.g. 'Client', 'Facility').</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Resolved EntitySchemaDto tailored to the authenticated caller's RBAC scope.</returns>
    [HttpGet("{entityName}")]
    [ProducesResponseType(typeof(EntitySchemaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EntitySchemaDto>> GetSchema([FromRoute] string entityName, CancellationToken ct)
    {
        var response = await Mediator.Send(new GetEntitySchemaQuery(entityName), ct);
        return Ok(response);
    }
}
