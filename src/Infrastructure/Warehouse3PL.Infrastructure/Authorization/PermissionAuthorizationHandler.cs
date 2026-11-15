using Microsoft.AspNetCore.Authorization;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Infrastructure.Authorization;

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ICurrentUserContext _currentUser;

    public PermissionAuthorizationHandler(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Task.CompletedTask;
        }

        if (_currentUser.IsSuperAdmin || _currentUser.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

