using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Users.Commands.AssignUserRoles;

public record AssignUserRolesCommand : IRequest<bool>
{
    public Guid UserId { get; init; }
    public List<string> Roles { get; init; } = [];
}

public class AssignUserRolesCommandHandler : IRequestHandler<AssignUserRolesCommand, bool>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AssignUserRolesCommandHandler(
        IApplicationDbContext context,
        ICurrentUserContext currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<bool> Handle(AssignUserRolesCommand request, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == request.UserId && !u.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new EntityNotFoundException(nameof(User), request.UserId);
        }

        if (!_currentUser.IsStaff && user.ClientId != _currentUser.TenantId)
        {
            throw new ForbiddenException("Cannot manage roles for users of another client tenant.");
        }

        var matchingRoles = await _context.Roles
            .Where(r => request.Roles.Contains(r.RoleName))
            .ToListAsync(cancellationToken);

        // Remove old roles not in the new list
        var existingRoleIds = matchingRoles.Select(r => r.Id).ToHashSet();
        var rolesToRemove = user.UserRoles.Where(ur => !existingRoleIds.Contains(ur.RoleId)).ToList();
        foreach (var r in rolesToRemove)
        {
            user.UserRoles.Remove(r);
        }

        // Add newly assigned roles
        var currentRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToHashSet();
        foreach (var role in matchingRoles.Where(r => !currentRoleIds.Contains(r.Id)))
        {
            user.UserRoles.Add(new UserRole
            {
                User = user,
                Role = role,
                AssignedAt = _dateTimeProvider.UtcNow,
                AssignedBy = _currentUser.UserId
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

