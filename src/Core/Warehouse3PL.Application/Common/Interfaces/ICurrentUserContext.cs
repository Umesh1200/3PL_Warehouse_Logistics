using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Interfaces;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    string? Email { get; }
    Guid? TenantId { get; }
    Guid? RequestedTenantId => null;
    Guid? FacilityId { get; }
    Guid? RequestedFacilityId { get; }
    UserType? UserType { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool IsAuthenticated { get; }
    bool IsStaff { get; }
    bool IsSuperAdmin { get; }

    bool HasPermission(string permission);
    bool IsInRole(string role);
}

