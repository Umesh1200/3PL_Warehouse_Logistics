using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Auth.Commands.Login;
using Warehouse3PL.Application.Features.Auth.Commands.RefreshToken;
using Warehouse3PL.Application.Features.Auth.Commands.Register;
using Warehouse3PL.Application.Features.Auth.Queries.GetCurrentUser;

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
    /// Register a new user under a specific client tenant or as staff.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterCommand command, CancellationToken ct)
    {
        var response = await Mediator.Send(command, ct);
        return Ok(response);
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

