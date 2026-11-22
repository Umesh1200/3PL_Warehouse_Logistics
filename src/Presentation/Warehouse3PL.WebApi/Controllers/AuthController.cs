using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Auth.Commands.Login;
using Warehouse3PL.Application.Features.Auth.Commands.RefreshToken;
using Warehouse3PL.Application.Features.Auth.Commands.Register;
using Warehouse3PL.Application.Features.Auth.Queries.GetCurrentUser;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.WebApi.Controllers;

[Tags("Authentication & Identity")]
public class AuthController : BaseApiController
{
    /// <summary>
    /// Authenticate user credentials and issue JWT bearer tokens.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginCommand command, CancellationToken ct)
    {
        var response = await Mediator.Send(command, ct);
        return Ok(response);
    }

    /// <summary>
    /// Disabled: anonymous self-registration is not permitted. Provision accounts via authorized POST /api/v1/users.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterCommand command, CancellationToken ct)
    {
        throw new ForbiddenException(
            "Public anonymous registration is disabled. All user accounts must be provisioned by an authorized administrator via POST /api/v1/users.");
    }

    /// <summary>
    /// Refresh an expired JWT access token using a valid refresh token.
    /// </summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> RefreshToken([FromBody] RefreshTokenCommand command, CancellationToken ct)
    {
        var response = await Mediator.Send(command, ct);
        return Ok(response);
    }

    /// <summary>
    /// Retrieve current authenticated user profile, tenant info, roles, and fine-grained permissions.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserDto>> GetCurrentUser(CancellationToken ct)
    {
        var response = await Mediator.Send(new GetCurrentUserQuery(), ct);
        return Ok(response);
    }
}

