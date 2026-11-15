using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Auth.Commands.RefreshToken;

public record RefreshTokenCommand : IRequest<AuthResponseDto>
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResponseDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<AuthResponseDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null)
        {
            throw new UnauthorizedException("Invalid access token.");
        }

        var userIdClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Invalid user identifier in token.");
        }

        var user = await _context.Users
            .Include(u => u.Client)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);

        if (user == null || user.RefreshToken != request.RefreshToken || user.RefreshTokenExpiryTime <= _dateTimeProvider.UtcNow)
        {
            throw new UnauthorizedException("Invalid or expired refresh token.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenException($"User account is {user.Status}.");
        }

        var roles = user.UserRoles.Select(ur => ur.Role.RoleName).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.PermissionKey)
            .Distinct()
            .ToList();

        var authResponse = _tokenService.GenerateTokens(user, roles, permissions);

        user.RefreshToken = authResponse.RefreshToken;
        user.RefreshTokenExpiryTime = _dateTimeProvider.UtcNow.AddDays(7);
        await _context.SaveChangesAsync(cancellationToken);

        return authResponse;
    }
}

