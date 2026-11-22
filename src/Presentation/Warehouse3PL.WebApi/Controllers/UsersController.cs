using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Users.Commands.AssignUserFacility;
using Warehouse3PL.Application.Features.Users.Commands.AssignUserRoles;
using Warehouse3PL.Application.Features.Users.Commands.CreateUser;
using Warehouse3PL.Application.Features.Users.Queries.GetUserById;
using Warehouse3PL.Application.Features.Users.Queries.GetUsers;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Authorization;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Users & Security")]
public class UsersController : BaseApiController
{
    /// <summary>
    /// Retrieve paginated list of users. Filtered to caller's tenant unless staff.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    [ProducesResponseType(typeof(PagedResult<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserDto>>> GetUsers([FromQuery] GetUsersQuery query, CancellationToken ct)
    {
        var response = await Mediator.Send(query, ct);
        return Ok(response);
    }

    /// <summary>
    /// Retrieve user by unique UUID identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Users.View)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetUserById([FromRoute] Guid id, CancellationToken ct)
    {
        var response = await Mediator.Send(new GetUserByIdQuery(id), ct);
        return Ok(response);
    }

    /// <summary>
    /// Create a new user (Staff, Client, or Driver) with password hash and initial roles.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Users.Create)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserCommand command, CancellationToken ct)
    {
        var response = await Mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetUserById), new { id = response.Id }, response);
    }

    /// <summary>
    /// Assign security roles to a user.
    /// </summary>
    [HttpPost("{id:guid}/roles")]
    [HasPermission(Permissions.Users.AssignRoles)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRoles([FromRoute] Guid id, [FromBody] List<string> roles, CancellationToken ct)
    {
        await Mediator.Send(new AssignUserRolesCommand { UserId = id, Roles = roles }, ct);
        return Ok(new { message = "User roles updated successfully." });
    }

    /// <summary>
    /// Assign, reassign, or revoke the operational warehouse facility for a Staff user.
    /// </summary>
    [HttpPut("{id:guid}/assigned-facility")]
    [HasPermission(Permissions.Users.Edit)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> AssignFacility(
        [FromRoute] Guid id,
        [FromBody] AssignUserFacilityRequest request,
        CancellationToken ct)
    {
        var response = await Mediator.Send(new AssignUserFacilityCommand(id, request.FacilityId), ct);
        return Ok(response);
    }
}

public record AssignUserFacilityRequest(Guid? FacilityId);

