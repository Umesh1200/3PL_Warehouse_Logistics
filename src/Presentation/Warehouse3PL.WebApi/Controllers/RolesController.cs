using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Roles.Commands.AssignRolePermissions;
using Warehouse3PL.Application.Features.Roles.Commands.CreateRole;
using Warehouse3PL.Application.Features.Roles.Queries.GetPermissions;
using Warehouse3PL.Application.Features.Roles.Queries.GetRoles;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("RBAC Roles & Permissions")]
public class RolesController : BaseApiController
{
    /// <summary>
    /// Retrieve all configured roles along with their permission grants.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Roles.View)]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> GetRoles(CancellationToken ct)
    {
        var response = await Mediator.Send(new GetRolesQuery(), ct);
        return Ok(response);
    }

    /// <summary>
    /// Retrieve the comprehensive catalog of all fine-grained system permissions.
    /// </summary>
    [HttpGet("permissions")]
    [HasPermission(Permissions.Roles.View)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetPermissions(CancellationToken ct)
    {
        var response = await Mediator.Send(new GetPermissionsQuery(), ct);
        return Ok(response);
    }

    /// <summary>
    /// Create a new custom role.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Roles.Create)]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoleDto>> CreateRole([FromBody] CreateRoleCommand command, CancellationToken ct)
    {
        var response = await Mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetRoles), response);
    }

    /// <summary>
    /// Update and assign fine-grained permissions to a role.
    /// </summary>
    [HttpPost("{id:int}/permissions")]
    [HasPermission(Permissions.Roles.ManagePermissions)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignPermissions([FromRoute] int id, [FromBody] List<PermissionAssignmentDto> permissions, CancellationToken ct)
    {
        await Mediator.Send(new AssignRolePermissionsCommand { RoleId = id, Permissions = permissions }, ct);
        return Ok(new { message = "Role permissions updated successfully." });
    }
}

