using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;

namespace Warehouse3PL.UnitTests.Common;

public class TestTenantProvider : ITenantProvider
{
    public Guid? TenantId { get; set; }
    public bool HasTenant => TenantId.HasValue;
    public bool BypassTenantFilter { get; set; }

    public void SetTenantId(Guid tenantId)
    {
        TenantId = tenantId;
    }
}

public class TestCurrentUserContext : ICurrentUserContext
{
    public Guid? UserId { get; set; } = Guid.NewGuid();
    public string? Email { get; set; } = "test@example.com";
    public Guid? TenantId { get; set; }
    public Guid? FacilityId { get; set; }
    public UserType? UserType { get; set; } = Domain.Enums.UserType.Client;
    public List<string> RolesList { get; set; } = [];
    public List<string> PermissionsList { get; set; } = [];

    public IReadOnlyList<string> Roles => RolesList.AsReadOnly();
    public IReadOnlyList<string> Permissions => PermissionsList.AsReadOnly();
    public bool IsAuthenticated => UserId.HasValue;
    public bool IsStaff => UserType == Domain.Enums.UserType.Staff || RolesList.Contains("SuperAdmin");
    public bool IsSuperAdmin => RolesList.Contains("SuperAdmin");

    public bool HasPermission(string permission) =>
        IsSuperAdmin || PermissionsList.Contains(permission, StringComparer.OrdinalIgnoreCase);

    public bool IsInRole(string role) =>
        RolesList.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public static class TestDbContextFactory
{
    public static ApplicationDbContext Create(
        TestTenantProvider tenantProvider,
        TestCurrentUserContext currentUser,
        string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .Options;

        var dateTimeProvider = new DateTimeProvider();
        var interceptor = new AuditableEntityInterceptor(currentUser, dateTimeProvider);

        return new ApplicationDbContext(options, tenantProvider, currentUser, interceptor);
    }
}

