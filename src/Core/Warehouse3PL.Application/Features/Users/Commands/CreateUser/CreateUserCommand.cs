using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Users.Commands.CreateUser;

public record CreateUserCommand : IRequest<UserDto>
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public UserType UserType { get; init; } = UserType.Client;
    public Guid? ClientId { get; init; }
    public Guid? AssignedFacilityId { get; init; }
    public List<string> Roles { get; init; } = [];
}

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.");

        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);

        When(x => x.UserType == UserType.Client, () =>
        {
            RuleFor(x => x.ClientId).NotEmpty().WithMessage("Client ID is required for Client user type.");
            RuleFor(x => x.AssignedFacilityId).Null()
                .WithMessage("AssignedFacilityId is restricted to warehouse Staff users.");
        });

        When(x => x.UserType == UserType.Driver, () =>
        {
            RuleFor(x => x.AssignedFacilityId).Null()
                .WithMessage("AssignedFacilityId is restricted to warehouse Staff users.");
        });
    }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateUserCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        // Enforce tenant boundary: non-staff can only create Client users in their own tenant
        var targetClientId = request.ClientId;
        if (!_currentUser.IsStaff)
        {
            if (!_currentUser.TenantId.HasValue)
            {
                throw new ForbiddenException("Cannot create users without a tenant context.");
            }
            if (request.UserType != UserType.Client || request.AssignedFacilityId.HasValue)
            {
                throw new ForbiddenException("Only authorized warehouse staff administrators can create Staff/Driver accounts or assign facilities.");
            }
            targetClientId = _currentUser.TenantId.Value;
        }

        Guid? validatedFacilityId = null;
        if (request.AssignedFacilityId.HasValue)
        {
            if (request.UserType != UserType.Staff)
            {
                throw new ValidationException(nameof(request.AssignedFacilityId),
                    "AssignedFacilityId can only be configured for warehouse Staff users.");
            }

            if (request.AssignedFacilityId.Value == Guid.Empty)
            {
                throw new ValidationException(nameof(request.AssignedFacilityId),
                    "AssignedFacilityId must be a valid facility identifier.");
            }

            var facility = await _context.Facilities
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.AssignedFacilityId.Value && !f.IsDeleted, cancellationToken);

            if (facility == null)
            {
                throw new EntityNotFoundException(nameof(Facility), request.AssignedFacilityId.Value);
            }

            if (!facility.IsActive)
            {
                throw new ValidationException(nameof(request.AssignedFacilityId),
                    $"Facility '{facility.Id}' is inactive and cannot be assigned.");
            }

            validatedFacilityId = facility.Id;
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _context.Users
            .AnyAsync(u => u.Email.ToLower() == normalizedEmail && !u.IsDeleted, cancellationToken);

        if (existingUser)
        {
            throw new ValidationException(nameof(request.Email), "A user with this email address already exists.");
        }

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = request.PhoneNumber,
            UserType = request.UserType,
            Status = UserStatus.Active,
            ClientId = request.UserType == UserType.Staff ? null : targetClientId,
            AssignedFacilityId = request.UserType == UserType.Staff ? validatedFacilityId : null
        };

        // Assign roles
        if (request.Roles.Count != 0)
        {
            var requestedRoles = await _context.Roles
                .Where(r => request.Roles.Contains(r.RoleName))
                .ToListAsync(cancellationToken);

            foreach (var role in requestedRoles)
            {
                user.UserRoles.Add(new UserRole
                {
                    User = user,
                    Role = role,
                    AssignedAt = _dateTimeProvider.UtcNow,
                    AssignedBy = _currentUser.UserId
                });
            }
        }

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        // Fetch back with relations
        var created = await _context.Users
            .Include(u => u.Client)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstAsync(u => u.Id == user.Id, cancellationToken);

        return created.Adapt<UserDto>();
    }
}
