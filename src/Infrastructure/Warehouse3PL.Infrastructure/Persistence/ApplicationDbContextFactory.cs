using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;

namespace Warehouse3PL.Infrastructure.Persistence;

/// <summary>
/// Design-time DbContext factory enabling fast, isolated EF Core migration tooling execution
/// without launching the WebApi application host or requiring an active PostgreSQL database connection.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=warehouse3pl_design;Username=postgres;Password=postgres",
            b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention();

        var tenantProvider = new DesignTimeTenantProvider();
        var currentUser = new DesignTimeCurrentUserContext();
        var dateTimeProvider = new DesignTimeDateTimeProvider();
        var interceptor = new AuditableEntityInterceptor(currentUser, dateTimeProvider);

        return new ApplicationDbContext(optionsBuilder.Options, tenantProvider, currentUser, interceptor);
    }

    private class DesignTimeTenantProvider : ITenantProvider
    {
        public Guid? TenantId => null;
        public bool HasTenant => false;
        public bool BypassTenantFilter { get; set; } = true;

        public void SetTenantId(Guid tenantId) { }
    }

    private class DesignTimeCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId => null;
        public string? Email => null;
        public Guid? TenantId => null;
        public Guid? FacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles => Array.Empty<string>();
        public IReadOnlyList<string> Permissions => Array.Empty<string>();
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => true;

        public bool HasPermission(string permission) => true;
        public bool IsInRole(string role) => true;
    }

    private class DesignTimeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
