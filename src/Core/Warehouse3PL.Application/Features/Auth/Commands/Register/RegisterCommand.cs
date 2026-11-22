using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Auth.Commands.Register;

public record RegisterCommand : IRequest<AuthResponseDto>
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public UserType UserType { get; init; } = UserType.Client;
    public Guid? ClientId { get; init; }
}

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one number.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100);

        RuleFor(x => x.ClientId)
            .NotEmpty().When(x => x.UserType == UserType.Client)
            .WithMessage("Client ID is required for Client user type.");
    }
}

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResponseDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RegisterCommandHandler(
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

    public async Task<AuthResponseDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        if (request.UserType == UserType.Staff)
        {
            throw new ForbiddenException("Self-registration cannot create warehouse Staff or SuperAdmin accounts.");
        }

        var emailNormalized = request.Email.Trim().ToLower();

        var existingUser = await _context.Users
            .AnyAsync(u => u.Email.ToLower() == emailNormalized, cancellationToken);

        if (existingUser)
        {
            throw new ValidationException(nameof(request.Email), "A user with this email address already exists.");
        }

        Client? client = null;
        if (request.ClientId.HasValue)
        {
            client = await _context.Clients
                .FirstOrDefaultAsync(c => c.Id == request.ClientId.Value && !c.IsDeleted, cancellationToken);

            if (client == null)
            {
                throw new EntityNotFoundException(nameof(Client), request.ClientId.Value);
            }
        }

        var user = new User
        {
            Email = emailNormalized,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = request.PhoneNumber,
            UserType = request.UserType,
            Status = UserStatus.Active,
            ClientId = request.ClientId
        };

        // Assign default role based on user type
        var defaultRoleName = request.UserType switch
        {
            UserType.Staff => SystemRoles.SuperAdmin,
            UserType.Client => SystemRoles.ClientAdmin,
            UserType.Driver => SystemRoles.Driver,
            _ => SystemRoles.ClientUser
        };

        var defaultRole = await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.RoleName == defaultRoleName, cancellationToken);

        if (defaultRole != null)
        {
            user.UserRoles.Add(new UserRole
            {
                User = user,
                Role = defaultRole,
                AssignedAt = _dateTimeProvider.UtcNow
            });
        }

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        var roles = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
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
