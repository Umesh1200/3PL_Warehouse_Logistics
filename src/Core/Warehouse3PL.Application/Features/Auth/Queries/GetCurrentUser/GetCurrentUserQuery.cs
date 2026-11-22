using MediatR;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Application.Features.Auth.Queries.GetCurrentUser;

public record GetCurrentUserQuery : IRequest<CurrentUserDto>;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserContext _currentUserContext;

    public GetCurrentUserQueryHandler(IApplicationDbContext context, ICurrentUserContext currentUserContext)
    {
        _context = context;
        _currentUserContext = currentUserContext;
    }

    public async Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserContext.UserId.HasValue)
        {
            throw new UnauthorizedException("User is not authenticated.");
        }

        var user = await _context.Users
            .Include(u => u.Client)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
            .FirstOrDefaultAsync(u => u.Id == _currentUserContext.UserId.Value && !u.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new EntityNotFoundException(nameof(User), _currentUserContext.UserId.Value);
        }

        var roles = user.UserRoles.Select(ur => ur.Role.RoleName).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.PermissionKey)
            .Distinct()
            .ToList();

        return new CurrentUserDto
        {
            UserId = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            UserType = user.UserType,
            TenantId = user.ClientId,
            AssignedFacilityId = user.UserType == UserType.Staff ? user.AssignedFacilityId : null,
            CompanyName = user.Client?.CompanyName,
            Roles = roles,
            Permissions = permissions
        };
    }
}

