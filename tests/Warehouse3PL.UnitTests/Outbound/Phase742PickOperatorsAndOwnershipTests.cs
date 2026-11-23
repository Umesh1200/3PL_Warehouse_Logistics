using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Picking;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Warehouse3PL.WebApi.Security;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public sealed class Phase742PickOperatorsAndOwnershipTests : IAsyncLifetime
{
    private const string TestJwtSecret = "Nexus3PL-Phase742-PickOperators-SigningKey-2026!";
    private const string TestIssuer = "nexus3pl-742-auth";
    private const string TestAudience = "nexus3pl-742-api";

    private static readonly Guid ClientA = Guid.Parse("742a0001-0000-0000-0000-000000000001");
    private static readonly Guid FacilityA = Guid.Parse("742a0002-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("742a0002-0000-0000-0000-000000000002");

    private static readonly Guid SupervisorFacAId = Guid.Parse("742a0003-0000-0000-0000-000000000001");
    private static readonly Guid PickerFacAId = Guid.Parse("742a0003-0000-0000-0000-000000000002");
    private static readonly Guid StaffFacBId = Guid.Parse("742a0003-0000-0000-0000-000000000003");
    private static readonly Guid InactiveStaffFacAId = Guid.Parse("742a0003-0000-0000-0000-000000000004");
    private static readonly Guid ClientUserId = Guid.Parse("742a0003-0000-0000-0000-000000000005");
    private static readonly Guid DriverUserId = Guid.Parse("742a0003-0000-0000-0000-000000000006");

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<JwtOptions>(o =>
        {
            o.SecretKey = TestJwtSecret;
            o.Issuer = TestIssuer;
            o.Audience = TestAudience;
            o.ExpiryMinutes = 60;
        });

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(PickTasksController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();

        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddScoped<IPickExecutionService, PickExecutionService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = TestIssuer,
                    ValidAudience = TestAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret)),
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = JwtStaffFacilityValidator.ValidateAsync
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        builder.Services.AddScoped<IDateTimeProvider, DateTimeProvider>();
        builder.Services.AddScoped<AuditableEntityInterceptor>();

        var dbName = $"phase742-ops-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) => options
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        _app = builder.Build();
        _app.UseMiddleware<ExceptionHandlingMiddleware>();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthenticatedTenantResolution();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _http = _app.GetTestClient();

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await SeedAsync(db, hasher);
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static async Task SeedAsync(ApplicationDbContext db, IPasswordHasher hasher)
    {
        db.Facilities.AddRange(
            new Facility { Id = FacilityA, FacilityCode = "FAC-742-A", Name = "Chicago Hub", CountryCode = "USA", IsActive = true },
            new Facility { Id = FacilityB, FacilityCode = "FAC-742-B", Name = "Dallas Hub", CountryCode = "USA", IsActive = true });

        db.Clients.Add(new Client { Id = ClientA, AccountCode = "CL-742", CompanyName = "Delta Corp", Status = ClientStatus.Active });

        var supervisorRole = new Role { RoleName = "PickSupervisor", Description = "Supervisor without users:view", IsSystemRole = false };
        supervisorRole.RolePermissions.Add(new RolePermission { Role = supervisorRole, PermissionKey = Permissions.Outbound.TasksView, AccessLevel = AccessLevel.Read });
        supervisorRole.RolePermissions.Add(new RolePermission { Role = supervisorRole, PermissionKey = Permissions.Outbound.TasksManage, AccessLevel = AccessLevel.Write });

        var pickerRole = new Role { RoleName = "RfPickerOnly", Description = "Pick-only operator", IsSystemRole = false };
        pickerRole.RolePermissions.Add(new RolePermission { Role = pickerRole, PermissionKey = Permissions.Outbound.Pick, AccessLevel = AccessLevel.Execute });

        var legacyClientRole = new Role { RoleName = SystemRoles.ClientAdmin, Description = "Client with stale claim", IsSystemRole = true };
        legacyClientRole.RolePermissions.Add(new RolePermission { Role = legacyClientRole, PermissionKey = Permissions.Outbound.TasksManage, AccessLevel = AccessLevel.Write });

        var driverRole = new Role { RoleName = SystemRoles.Driver, Description = "Driver", IsSystemRole = true };
        driverRole.RolePermissions.Add(new RolePermission { Role = driverRole, PermissionKey = Permissions.Dispatch.ExecuteDelivery, AccessLevel = AccessLevel.Execute });

        db.Roles.AddRange(supervisorRole, pickerRole, legacyClientRole, driverRole);

        var pw = hasher.HashPassword("StrongPass#742");
        db.Users.AddRange(
            new User
            {
                Id = SupervisorFacAId,
                Email = "sup.a@test.local",
                FirstName = "Sam",
                LastName = "Supervisor",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = SupervisorFacAId, Role = supervisorRole } }
            },
            new User
            {
                Id = PickerFacAId,
                Email = "picker.a@test.local",
                FirstName = "Pat",
                LastName = "Picker",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = PickerFacAId, Role = pickerRole } }
            },
            new User
            {
                Id = StaffFacBId,
                Email = "picker.b@test.local",
                FirstName = "Bob",
                LastName = "DallasStaff",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityB,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = StaffFacBId, Role = pickerRole } }
            },
            new User
            {
                Id = InactiveStaffFacAId,
                Email = "inactive.a@test.local",
                FirstName = "Ina",
                LastName = "Inactive",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityA,
                Status = UserStatus.Inactive
            },
            new User
            {
                Id = ClientUserId,
                Email = "client@test.local",
                FirstName = "Cli",
                LastName = "User",
                PasswordHash = pw,
                UserType = UserType.Client,
                ClientId = ClientA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = ClientUserId, Role = legacyClientRole } }
            },
            new User
            {
                Id = DriverUserId,
                Email = "driver@test.local",
                FirstName = "Dan",
                LastName = "Driver",
                PasswordHash = pw,
                UserType = UserType.Driver,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = DriverUserId, Role = driverRole } }
            });

        await db.SaveChangesAsync();
    }

    private string MintToken(Guid userId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions)
            .Single(u => u.Id == userId);
        var roles = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
        var perms = user.UserRoles.SelectMany(ur => ur.Role.RolePermissions).Select(rp => rp.PermissionKey).Distinct().ToList();
        return tokenService.GenerateTokens(user, roles, perms).AccessToken;
    }

    private HttpRequestMessage AuthedReq(HttpMethod method, string url, Guid userId)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(userId));
        return req;
    }

    [Fact]
    public async Task SupervisorWithoutUsersView_CanQueryFacilityScopedEligiblePickOperators()
    {
        // Confirm Supervisor lacks broad users:view directory access
        using (var usersResp = await _http.SendAsync(AuthedReq(HttpMethod.Get, "/api/v1/users", SupervisorFacAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, usersResp.StatusCode);
        }

        // Eligible operators query returns ONLY active UserType.Staff assigned to FacilityA
        using (var opsResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-tasks/eligible-operators?facilityId={FacilityA}", SupervisorFacAId)))
        {
            Assert.Equal(HttpStatusCode.OK, opsResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await opsResp.Content.ReadAsStreamAsync());
            var ids = doc.RootElement.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("id").GetGuid())
                .ToList();

            Assert.Contains(SupervisorFacAId, ids);
            Assert.Contains(PickerFacAId, ids);
            Assert.DoesNotContain(StaffFacBId, ids);
            Assert.DoesNotContain(InactiveStaffFacAId, ids);
            Assert.DoesNotContain(ClientUserId, ids);
            Assert.DoesNotContain(DriverUserId, ids);
        }
    }

    [Fact]
    public async Task CrossFacilitySupervisor_PickOnly_Client_And_Driver_AreForbiddenFromEligibleOperators()
    {
        using (var crossFac = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-tasks/eligible-operators?facilityId={FacilityB}", SupervisorFacAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossFac.StatusCode);
        }

        using (var pickOnly = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-tasks/eligible-operators?facilityId={FacilityA}", PickerFacAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, pickOnly.StatusCode);
        }

        using (var clientForbidden = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-tasks/eligible-operators?facilityId={FacilityA}", ClientUserId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, clientForbidden.StatusCode);
        }

        using (var driverForbidden = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, "/api/v1/outbound/pick-tasks/eligible-operators", DriverUserId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, driverForbidden.StatusCode);
        }
    }
}
