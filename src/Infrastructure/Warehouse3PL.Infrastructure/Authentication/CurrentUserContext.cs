using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Authentication;

public class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var claim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User?.FindFirst("sub")?.Value;

            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    public string? Email => User?.FindFirst(ClaimTypes.Email)?.Value
        ?? User?.FindFirst("email")?.Value;

    public Guid? TenantId
    {
        get
        {
            var claim = User?.FindFirst("tenant_id")?.Value
                ?? User?.FindFirst("client_id")?.Value;

            return Guid.TryParse(claim, out var tenantId) ? tenantId : null;
        }
    }

    public Guid? FacilityId
    {
        get
        {
            var claim = User?.FindFirst("facility_id")?.Value;
            return Guid.TryParse(claim, out var facilityId) ? facilityId : null;
        }
    }

    public Guid? RequestedFacilityId
    {
        get
        {
            var header = _httpContextAccessor.HttpContext?.Request.Headers["X-Facility-Id"].FirstOrDefault();
            return Guid.TryParse(header, out var headerFacilityId) ? headerFacilityId : null;
        }
    }

    public UserType? UserType
    {
        get
        {
            var claim = User?.FindFirst("user_type")?.Value
                ?? User?.FindFirst("UserType")?.Value;
            return Enum.TryParse<UserType>(claim, true, out var type) ? type : null;
        }
    }

    public IReadOnlyList<string> Roles => User?.FindAll(ClaimTypes.Role)
        .Concat(User?.FindAll("roles") ?? [])
        .Concat(User?.FindAll("role") ?? [])
        .Select(c => c.Value)
        .Distinct()
        .ToList() ?? [];

    public IReadOnlyList<string> Permissions => User?.FindAll("permissions")
        .Concat(User?.FindAll("permission") ?? [])
        .Concat(User?.FindAll("Permission") ?? [])
        .Select(c => c.Value)
        .Distinct()
        .ToList() ?? [];

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public bool IsStaff => UserType == Domain.Enums.UserType.Staff || IsInRole(SystemRoles.SuperAdmin);

    public bool IsSuperAdmin => IsInRole(SystemRoles.SuperAdmin);

    public bool HasPermission(string permission)
    {
        if (IsSuperAdmin) return true;
        return Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsInRole(string role)
    {
        return Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }
}

