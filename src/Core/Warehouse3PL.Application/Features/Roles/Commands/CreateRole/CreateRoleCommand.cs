using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Exceptions;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.Application.Features.Roles.Commands.CreateRole;

public record CreateRoleCommand : IRequest<RoleDto>
{
    public string RoleName { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.RoleName)
            .NotEmpty().WithMessage("Role name is required.")
            .MaximumLength(100);
    }
}

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, RoleDto>
{
    private readonly IApplicationDbContext _context;

    public CreateRoleCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RoleDto> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var normalizedName = request.RoleName.Trim();

        var existing = await _context.Roles
            .AnyAsync(r => r.RoleName.ToLower() == normalizedName.ToLower(), cancellationToken);

        if (existing)
        {
            throw new ValidationException(nameof(request.RoleName), $"A role with name '{normalizedName}' already exists.");
        }

        var role = new Role
        {
            RoleName = normalizedName,
            Description = request.Description?.Trim(),
            IsSystemRole = false
        };

        _context.Roles.Add(role);
        await _context.SaveChangesAsync(cancellationToken);

        return role.Adapt<RoleDto>();
    }
}
