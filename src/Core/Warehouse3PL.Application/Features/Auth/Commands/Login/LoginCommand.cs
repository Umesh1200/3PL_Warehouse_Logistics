using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Auth.Commands.Login;

public record LoginCommand : IRequest<AuthResponseDto>
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponseDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LoginCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<AuthResponseDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .Include(u => u.Client)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower().Trim() && !u.IsDeleted, cancellationToken);

        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenException($"User account is {user.Status}. Please contact support.");
        }

        if (user.ClientId.HasValue && user.Client != null && user.Client.Status == ClientStatus.Suspended)
        {
            throw new ForbiddenException("Client tenant account is currently suspended.");
        }

        var roles = user.UserRoles.Select(ur => ur.Role.RoleName).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.PermissionKey)
            .Distinct()
            .ToList();

        var authResponse = _tokenService.GenerateTokens(user, roles, permissions);

        // Update user refresh token
        user.RefreshToken = authResponse.RefreshToken;
        user.RefreshTokenExpiryTime = _dateTimeProvider.UtcNow.AddDays(7);
        await _context.SaveChangesAsync(cancellationToken);

        return authResponse;
    }
}

