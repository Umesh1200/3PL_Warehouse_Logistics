using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Roles.Commands.AssignRolePermissions;

public record AssignRolePermissionsCommand : IRequest<bool>
{
    public int RoleId { get; init; }
    public List<PermissionAssignmentDto> Permissions { get; init; } = [];
}

public record PermissionAssignmentDto
{
    public string PermissionKey { get; init; } = string.Empty;
    public AccessLevel AccessLevel { get; init; } = AccessLevel.Read;
}

public class AssignRolePermissionsCommandValidator : AbstractValidator<AssignRolePermissionsCommand>
{
    public AssignRolePermissionsCommandValidator()
    {
        RuleFor(x => x.RoleId).GreaterThan(0);
        RuleForEach(x => x.Permissions).ChildRules(p =>
        {
            p.RuleFor(x => x.PermissionKey).NotEmpty();
        });
    }
}

public class AssignRolePermissionsCommandHandler : IRequestHandler<AssignRolePermissionsCommand, bool>
{
    private readonly IApplicationDbContext _context;

    public AssignRolePermissionsCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(AssignRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

        if (role == null)
        {
            throw new EntityNotFoundException(nameof(Role), request.RoleId);
        }

        // System roles like SuperAdmin shouldn't have their core permissions wiped
        // Remove existing role permissions
        role.RolePermissions.Clear();

        foreach (var p in request.Permissions)
        {
            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionKey = p.PermissionKey.Trim(),
                AccessLevel = p.AccessLevel
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

