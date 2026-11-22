using System.IdentityModel.Tokens.Jwt;
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
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Configuration;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Allocation;
using Warehouse3PL.Infrastructure.Services.Packing;
using Warehouse3PL.Infrastructure.Services.Picking;
using Warehouse3PL.Infrastructure.Services.Shipping;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public sealed class Phase740BRealJwtFacilityProvisioningHttpTests : IAsyncLifetime
{
    private const string TestJwtSecret = "Nexus3PL-Phase740B-RealJwt-SigningKey-2026-Strong!";
    private const string TestIssuer = "nexus3pl-test-auth";
    private const string TestAudience = "nexus3pl-test-api";

    private static readonly Guid ClientA = Guid.Parse("740c0001-0000-0000-0000-000000000001");
    private static readonly Guid ClientB = Guid.Parse("740c0001-0000-0000-0000-000000000002");
    private static readonly Guid FacilityA = Guid.Parse("740c0002-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("740c0002-0000-0000-0000-000000000002");
    private static readonly Guid FacilityInactive = Guid.Parse("740c0002-0000-0000-0000-000000000003");

    private static readonly Guid SuperAdminId = Guid.Parse("740c0003-0000-0000-0000-000000000001");
    private static readonly Guid PreExistingUnassignedStaffId = Guid.Parse("740c0003-0000-0000-0000-000000000002");
    private static readonly Guid ClientAdminId = Guid.Parse("740c0003-0000-0000-0000-000000000003");
    private static readonly Guid InactiveStaffId = Guid.Parse("740c0003-0000-0000-0000-000000000004");
    private static readonly Guid ForeignClientUserId = Guid.Parse("740c0003-0000-0000-0000-000000000005");
    private static readonly Guid StaffNonSuperAdminId = Guid.Parse("740c0003-0000-0000-0000-000000000006");

    private static readonly Guid ItemA = Guid.Parse("740c0004-0000-0000-0000-000000000001");
    private static readonly Guid BinA = Guid.Parse("740c0005-0000-0000-0000-000000000001");
    private static readonly Guid BinB = Guid.Parse("740c0005-0000-0000-0000-000000000002");

    private static readonly Guid OrderA = Guid.Parse("740c0006-0000-0000-0000-000000000001");
    private static readonly Guid OrderLineA = Guid.Parse("740c0006-0000-0000-0000-000000000011");
    private static readonly Guid StockBinA = Guid.Parse("740c0007-0000-0000-0000-000000000001");
    private static readonly Guid Alloc1 = Guid.Parse("740c0008-0000-0000-0000-000000000001");
    private static readonly Guid WaveA = Guid.Parse("740c0009-0000-0000-0000-000000000001");
    private static readonly Guid TaskA1 = Guid.Parse("740c000a-0000-0000-0000-000000000001");
    private static readonly Guid TaskA2 = Guid.Parse("740c000a-0000-0000-0000-000000000002");
    private static readonly Guid ToteA = Guid.Parse("740c000b-0000-0000-0000-000000000001");

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<CarrierWebhookOptions>(builder.Configuration.GetSection(CarrierWebhookOptions.SectionName));
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

        builder.Services.AddScoped<IAllocationCandidateFilter, AllocationCandidateFilter>();
        builder.Services.AddScoped<IAllocationStrategy, FifoAllocationStrategy>();
        builder.Services.AddScoped<IAllocationStrategy, FefoAllocationStrategy>();
        builder.Services.AddScoped<IAllocationStrategy, ManualAllocationStrategy>();
        builder.Services.AddScoped<IAllocationStrategyResolver, AllocationStrategyResolver>();
        builder.Services.AddScoped<IAllocationEngine, AllocationEngine>();
        builder.Services.AddScoped<IAllocationExecutionService, AllocationExecutionService>();
        builder.Services.AddScoped<IPickRouteOptimizer, SerpentineRouteOptimizer>();
        builder.Services.AddScoped<IPickTaskGenerator, PickTaskGenerator>();
        builder.Services.AddScoped<IPickWaveGenerator, PickWaveGenerator>();
        builder.Services.AddScoped<IPickExecutionService, PickExecutionService>();
        builder.Services.AddScoped<ICartonizationService, CartonizationService>();
        builder.Services.AddScoped<IPackingExecutionService, PackingExecutionService>();
        builder.Services.AddScoped<IShippingCarrierService, SimulatedCarrierService>();
        builder.Services.AddScoped<IShippingLabelService, ShippingLabelService>();
        builder.Services.AddScoped<IShipmentConsolidationService, ShipmentConsolidationService>();
        builder.Services.AddScoped<IReturnTrackingService, ReturnTrackingService>();

        var dbName = Guid.NewGuid().ToString();
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
        SeedData(db, hasher);
        await db.SaveChangesAsync();
    }

    private static void SeedData(ApplicationDbContext db, IPasswordHasher hasher)
    {
        db.Clients.AddRange(
            new Client { Id = ClientA, CompanyName = "RealJwt Client", AccountCode = "JWT-A", Status = ClientStatus.Active },
            new Client { Id = ClientB, CompanyName = "Foreign Client B", AccountCode = "JWT-B", Status = ClientStatus.Active },
            new Client { Id = Guid.Parse("740c0001-0000-0000-0000-000000000003"), CompanyName = "Suspended Client C", AccountCode = "JWT-SUSP", Status = ClientStatus.Suspended });

        db.Facilities.AddRange(
            new Facility { Id = FacilityA, FacilityCode = "JWT-FAC-A", Name = "Facility A", AddressLine1 = "1 Main", City = "Chicago", CountryCode = "US", IsActive = true },
            new Facility { Id = FacilityB, FacilityCode = "JWT-FAC-B", Name = "Facility B", AddressLine1 = "2 Main", City = "Dallas", CountryCode = "US", IsActive = true },
            new Facility { Id = FacilityInactive, FacilityCode = "JWT-FAC-OFF", Name = "Inactive Facility", AddressLine1 = "3 Main", City = "Miami", CountryCode = "US", IsActive = false });

        db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityA, Status = FacilityAccessStatus.Active });

        var roleSuper = new Role { RoleName = SystemRoles.SuperAdmin };
        var roleWhOps = new Role { RoleName = SystemRoles.WarehouseOperator };
        roleWhOps.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Pick });

        var roleSupervisor = new Role { RoleName = "PickSupervisor" };
        roleSupervisor.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.TasksView });
        roleSupervisor.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.TasksManage });
        roleSupervisor.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Pick });

        var roleClientAdmin = new Role { RoleName = SystemRoles.ClientAdmin };
        roleClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Users.View });
        roleClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Users.Create });
        roleClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Users.Edit });
        roleClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Users.AssignRoles });

        var roleClientUser = new Role { RoleName = SystemRoles.ClientUser };
        var roleDriver = new Role { RoleName = SystemRoles.Driver };
        var roleStaffUserMgr = new Role { RoleName = SystemRoles.OperationsManager };
        roleStaffUserMgr.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Users.View });
        roleStaffUserMgr.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Users.Create });
        roleStaffUserMgr.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Users.Edit });
        roleStaffUserMgr.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Users.AssignRoles });

        db.Roles.AddRange(roleSuper, roleWhOps, roleSupervisor, roleClientAdmin, roleClientUser, roleDriver, roleStaffUserMgr);

        var passHash = hasher.HashPassword("Password123!");
        var superAdmin = new User
        {
            Id = SuperAdminId,
            Email = "superadmin@jwt.test",
            PasswordHash = passHash,
            FirstName = "Super",
            LastName = "Admin",
            UserType = UserType.Staff,
            Status = UserStatus.Active,
            AssignedFacilityId = null
        };
        superAdmin.UserRoles.Add(new UserRole { User = superAdmin, Role = roleSuper });

        var preUnassigned = new User
        {
            Id = PreExistingUnassignedStaffId,
            Email = "legacy-unassigned@jwt.test",
            PasswordHash = passHash,
            FirstName = "Legacy",
            LastName = "Staff",
            UserType = UserType.Staff,
            Status = UserStatus.Active,
            AssignedFacilityId = null
        };
        preUnassigned.UserRoles.Add(new UserRole { User = preUnassigned, Role = roleWhOps });

        var clientAdmin = new User
        {
            Id = ClientAdminId,
            ClientId = ClientA,
            Email = "client-admin@jwt.test",
            PasswordHash = passHash,
            FirstName = "Client",
            LastName = "Admin",
            UserType = UserType.Client,
            Status = UserStatus.Active
        };
        clientAdmin.UserRoles.Add(new UserRole { User = clientAdmin, Role = roleClientAdmin });

        var inactiveStaff = new User
        {
            Id = InactiveStaffId,
            Email = "inactive-staff@jwt.test",
            PasswordHash = passHash,
            FirstName = "Inactive",
            LastName = "Staff",
            UserType = UserType.Staff,
            Status = UserStatus.Inactive,
            AssignedFacilityId = FacilityA
        };
        inactiveStaff.UserRoles.Add(new UserRole { User = inactiveStaff, Role = roleWhOps });

        var foreignClientUser = new User
        {
            Id = ForeignClientUserId,
            ClientId = ClientB,
            Email = "foreign-client@jwt.test",
            PasswordHash = passHash,
            FirstName = "Foreign",
            LastName = "User",
            UserType = UserType.Client,
            Status = UserStatus.Active
        };
        foreignClientUser.UserRoles.Add(new UserRole { User = foreignClientUser, Role = roleClientUser });

        var staffNonSuperAdmin = new User
        {
            Id = StaffNonSuperAdminId,
            Email = "ops-mgr@jwt.test",
            PasswordHash = passHash,
            FirstName = "Ops",
            LastName = "Manager",
            UserType = UserType.Staff,
            Status = UserStatus.Active,
            AssignedFacilityId = FacilityA
        };
        staffNonSuperAdmin.UserRoles.Add(new UserRole { User = staffNonSuperAdmin, Role = roleStaffUserMgr });

        db.Users.AddRange(superAdmin, preUnassigned, clientAdmin, inactiveStaff, foreignClientUser, staffNonSuperAdmin);

        db.Items.Add(new Item { Id = ItemA, ClientId = ClientA, Sku = "SKU-JWT-01", Description = "JWT Test Item" });
        db.Locations.AddRange(
            new Location { Id = BinA, FacilityId = FacilityA, LocationCode = "BIN-JWT-A", Barcode = "BIN-JWT-A", LocationType = LocationType.Bin, IsActive = true, IsPickable = true },
            new Location { Id = BinB, FacilityId = FacilityB, LocationCode = "BIN-JWT-B", Barcode = "BIN-JWT-B", LocationType = LocationType.Bin, IsActive = true, IsPickable = true });

        var order = new OutboundOrder { Id = OrderA, ClientId = ClientA, FacilityId = FacilityA, OrderNumber = "ORD-JWT-1", CustomerName = "JWT Buyer", Status = OutboundOrderStatus.Allocated };
        order.Lines.Add(new OutboundOrderLine { Id = OrderLineA, OutboundOrderId = OrderA, ItemId = ItemA, LineNumber = 1, QuantityOrdered = 10m, QuantityAllocated = 10m });
        db.OutboundOrders.Add(order);

        db.StockOnHand.Add(new StockOnHand { Id = StockBinA, ClientId = ClientA, FacilityId = FacilityA, ItemId = ItemA, LocationId = BinA, QtyAvailable = 20m, QtyAllocated = 10m, Version = 1 });
        db.InventoryAllocations.Add(new InventoryAllocation { Id = Alloc1, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = OrderA, OutboundOrderLineId = OrderLineA, StockOnHandId = StockBinA, ItemId = ItemA, LocationId = BinA, QuantityRequested = 10m, QuantityAllocated = 10m, Status = AllocationStatus.Allocated });

        db.PickWaves.Add(new PickWave { Id = WaveA, ClientId = ClientA, FacilityId = FacilityA, WaveNumber = "WV-JWT-1", Priority = 5, Status = PickWaveStatus.Released });
        db.PickTasks.AddRange(
            new PickTask { Id = TaskA1, ClientId = ClientA, FacilityId = FacilityA, PickWaveId = WaveA, OutboundOrderId = OrderA, OutboundOrderLineId = OrderLineA, InventoryAllocationId = Alloc1, StockOnHandId = StockBinA, ItemId = ItemA, SourceLocationId = BinA, QuantityToPick = 5m, PickSequence = 1, Status = PickTaskStatus.Pending, Version = 1 },
            new PickTask { Id = TaskA2, ClientId = ClientA, FacilityId = FacilityA, PickWaveId = WaveA, OutboundOrderId = OrderA, OutboundOrderLineId = OrderLineA, InventoryAllocationId = Alloc1, StockOnHandId = StockBinA, ItemId = ItemA, SourceLocationId = BinA, QuantityToPick = 5m, PickSequence = 2, Status = PickTaskStatus.Pending, Version = 1 });

        db.HandlingUnits.Add(new HandlingUnit { Id = ToteA, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "TOTE-JWT-01", UnitType = HandlingUnitType.Tote, Status = HandlingUnitStatus.Available, CurrentOutboundOrderId = OrderA });
    }

    private async Task<(string AccessToken, string RefreshToken, JsonElement User)> LoginAsync(string email, string password)
    {
        using var resp = await _http.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        return (
            root.GetProperty("accessToken").GetString()!,
            root.GetProperty("refreshToken").GetString()!,
            root.GetProperty("user").Clone());
    }

    private static HttpRequestMessage BearerRequest(HttpMethod method, string path, string accessToken, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task RealJwtPipeline_StaffProvisioning_FacilityAssignment_PickWorkflow_AndStaleTokenRevocation()
    {
        // 1. Login as SuperAdmin via real /api/v1/auth/login (AssignedFacilityId = null preserved cleanly)
        var (adminToken, _, adminUser) = await LoginAsync("superadmin@jwt.test", "Password123!");
        Assert.Equal(JsonValueKind.Null, adminUser.GetProperty("assignedFacilityId").ValueKind);

        // SuperAdmin can still retrieve /auth/me and remains strict SuperAdmin without arbitrary facility assignment
        using (var meResp = await _http.SendAsync(BearerRequest(HttpMethod.Get, "/api/v1/auth/me", adminToken)))
            Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

        // 2. Provision supervisor (FacilityA), operator A (FacilityA), and foreign operator B (FacilityB) through real /api/v1/users
        Guid supId;
        Guid opAId;
        Guid foreignOpBId;
        using (var rSup = BearerRequest(HttpMethod.Post, "/api/v1/users", adminToken, new
        {
            email = "sup-a@jwt.test",
            password = "Password123!",
            firstName = "Sup",
            lastName = "FacA",
            userType = UserType.Staff,
            assignedFacilityId = FacilityA,
            roles = new[] { "PickSupervisor" }
        }))
        using (var respSup = await _http.SendAsync(rSup))
        {
            Assert.Equal(HttpStatusCode.Created, respSup.StatusCode);
            using var doc = JsonDocument.Parse(await respSup.Content.ReadAsStringAsync());
            supId = doc.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(FacilityA, doc.RootElement.GetProperty("assignedFacilityId").GetGuid());
        }

        using (var rOpA = BearerRequest(HttpMethod.Post, "/api/v1/users", adminToken, new
        {
            email = "op-a@jwt.test",
            password = "Password123!",
            firstName = "Op",
            lastName = "FacA",
            userType = UserType.Staff,
            assignedFacilityId = FacilityA,
            roles = new[] { SystemRoles.WarehouseOperator }
        }))
        using (var respOpA = await _http.SendAsync(rOpA))
        {
            Assert.Equal(HttpStatusCode.Created, respOpA.StatusCode);
            using var doc = JsonDocument.Parse(await respOpA.Content.ReadAsStringAsync());
            opAId = doc.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(FacilityA, doc.RootElement.GetProperty("assignedFacilityId").GetGuid());
        }

        using (var rOpB = BearerRequest(HttpMethod.Post, "/api/v1/users", adminToken, new
        {
            email = "op-b@jwt.test",
            password = "Password123!",
            firstName = "Op",
            lastName = "FacB",
            userType = UserType.Staff,
            assignedFacilityId = FacilityB,
            roles = new[] { SystemRoles.WarehouseOperator }
        }))
        using (var respOpB = await _http.SendAsync(rOpB))
        {
            Assert.Equal(HttpStatusCode.Created, respOpB.StatusCode);
            using var doc = JsonDocument.Parse(await respOpB.Content.ReadAsStringAsync());
            foreignOpBId = doc.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(FacilityB, doc.RootElement.GetProperty("assignedFacilityId").GetGuid());
        }

        // Reject provisioning with inactive facility (400), non-existent facility (404), and Client user with AssignedFacilityId (400)
        using (var rOff = await _http.SendAsync(BearerRequest(HttpMethod.Post, "/api/v1/users", adminToken, new
        {
            email = "bad1@jwt.test", password = "Password123!", firstName = "X", lastName = "Y",
            userType = UserType.Staff, assignedFacilityId = FacilityInactive
        })))
            Assert.Equal(HttpStatusCode.BadRequest, rOff.StatusCode);

        using (var rMiss = await _http.SendAsync(BearerRequest(HttpMethod.Post, "/api/v1/users", adminToken, new
        {
            email = "bad2@jwt.test", password = "Password123!", firstName = "X", lastName = "Y",
            userType = UserType.Staff, assignedFacilityId = Guid.NewGuid()
        })))
            Assert.Equal(HttpStatusCode.NotFound, rMiss.StatusCode);

        // ClientAdmin cannot configure staff facilities or assign AssignedFacilityId to Client users
        var (clientAdminToken, _, _) = await LoginAsync("client-admin@jwt.test", "Password123!");
        using (var rClientCreateStaff = await _http.SendAsync(BearerRequest(HttpMethod.Post, "/api/v1/users", clientAdminToken, new
        {
            email = "client-escalate@jwt.test", password = "Password123!", firstName = "Esc", lastName = "Alate",
            userType = UserType.Staff, assignedFacilityId = FacilityA
        })))
            Assert.Equal(HttpStatusCode.Forbidden, rClientCreateStaff.StatusCode);

        using (var rClientAssignFac = await _http.SendAsync(BearerRequest(HttpMethod.Put, $"/api/v1/users/{opAId}/assigned-facility", clientAdminToken, new { facilityId = FacilityB })))
            Assert.Equal(HttpStatusCode.Forbidden, rClientAssignFac.StatusCode);

        using (var rAssignClientUser = await _http.SendAsync(BearerRequest(HttpMethod.Put, $"/api/v1/users/{ClientAdminId}/assigned-facility", adminToken, new { facilityId = FacilityA })))
            Assert.Equal(HttpStatusCode.BadRequest, rAssignClientUser.StatusCode);

        // 3. Login as supervisor & operator A; inspect signed JWT claims
        var (supToken, _, _) = await LoginAsync("sup-a@jwt.test", "Password123!");
        var (opAToken1, opARefresh1, opAUser) = await LoginAsync("op-a@jwt.test", "Password123!");
        Assert.Equal(FacilityA, opAUser.GetProperty("assignedFacilityId").GetGuid());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(opAToken1);
        Assert.Equal(FacilityA.ToString(), jwt.Claims.First(c => c.Type == "facility_id").Value);
        Assert.Equal("Staff", jwt.Claims.First(c => c.Type == "user_type").Value);

        // 4. Supervisor assigns TaskA1 -> OpA (200 OK), and rejects foreign/unassigned/inactive staff
        using (var rAssignForeign = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskA1}/assign", supToken, new { operatorUserId = foreignOpBId })))
            Assert.Equal(HttpStatusCode.Forbidden, rAssignForeign.StatusCode);

        using (var rAssignUnassigned = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskA1}/assign", supToken, new { operatorUserId = PreExistingUnassignedStaffId })))
            Assert.Equal(HttpStatusCode.Forbidden, rAssignUnassigned.StatusCode);

        using (var rAssignInactive = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskA1}/assign", supToken, new { operatorUserId = InactiveStaffId })))
            Assert.Equal(HttpStatusCode.BadRequest, rAssignInactive.StatusCode);

        using (var rAssignOk = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskA1}/assign", supToken, new { operatorUserId = opAId })))
            Assert.Equal(HttpStatusCode.OK, rAssignOk.StatusCode);

        // Inactive staff login is denied (403 Forbidden)
        using (var rInactiveLogin = await _http.PostAsJsonAsync("/api/v1/auth/login", new { email = "inactive-staff@jwt.test", password = "Password123!" }))
            Assert.Equal(HttpStatusCode.Forbidden, rInactiveLogin.StatusCode);

        // Pre-existing unassigned staff has no facility_id in JWT and is denied operational access (403 Forbidden)
        var (unassignedToken, _, unassignedUser) = await LoginAsync("legacy-unassigned@jwt.test", "Password123!");
        Assert.Equal(JsonValueKind.Null, unassignedUser.GetProperty("assignedFacilityId").ValueKind);
        var unassignedJwt = new JwtSecurityTokenHandler().ReadJwtToken(unassignedToken);
        Assert.DoesNotContain(unassignedJwt.Claims, c => c.Type == "facility_id");

        using (var rUnassignedNext = await _http.SendAsync(BearerRequest(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityA}", unassignedToken)))
            Assert.Equal(HttpStatusCode.Forbidden, rUnassignedNext.StatusCode);

        using (var rUnassignedClaim = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskA2}/claim", unassignedToken, new { })))
            Assert.Equal(HttpStatusCode.Forbidden, rUnassignedClaim.StatusCode);

        // Foreign operator B (JWT facility_id = FacilityB) is denied FacilityA operations (403 Forbidden)
        var (foreignToken, _, _) = await LoginAsync("op-b@jwt.test", "Password123!");
        using (var rForeignNext = await _http.SendAsync(BearerRequest(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityA}", foreignToken)))
            Assert.Equal(HttpStatusCode.Forbidden, rForeignNext.StatusCode);

        // 5. Same-facility OpA retrieves /next (gets assigned TaskA1), executes /confirm on TaskA1, and claims+confirms TaskA2? Claim TaskA2 and execute!
        using (var rNextA = await _http.SendAsync(BearerRequest(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityA}", opAToken1)))
        {
            Assert.Equal(HttpStatusCode.OK, rNextA.StatusCode);
            using var doc = JsonDocument.Parse(await rNextA.Content.ReadAsStringAsync());
            Assert.Equal(TaskA1, doc.RootElement.GetProperty("id").GetGuid());
        }

        using (var rConfirmA1 = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskA1}/confirm", opAToken1,
            new { scannedLocationBarcode = "BIN-JWT-A", scannedItemBarcode = "SKU-JWT-01", scannedToteLpn = "TOTE-JWT-01", quantity = 5m })))
            Assert.Equal(HttpStatusCode.OK, rConfirmA1.StatusCode);

        using (var rClaimA2 = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskA2}/claim", opAToken1, new { })))
            Assert.Equal(HttpStatusCode.OK, rClaimA2.StatusCode);

        // 6. Facility reassignment: SuperAdmin reassigns OpA from FacilityA -> FacilityB
        using (var rReassignOpA = await _http.SendAsync(BearerRequest(HttpMethod.Put, $"/api/v1/users/{opAId}/assigned-facility", adminToken, new { facilityId = FacilityB })))
        {
            Assert.Equal(HttpStatusCode.OK, rReassignOpA.StatusCode);
            using var doc = JsonDocument.Parse(await rReassignOpA.Content.ReadAsStringAsync());
            Assert.Equal(FacilityB, doc.RootElement.GetProperty("assignedFacilityId").GetGuid());
        }

        // Stale opAToken1 (still carrying facility_id = FacilityA) is IMMEDIATELY rejected (401 Unauthorized) by real JwtBearer validator!
        using (var rStaleConfirm = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskA2}/confirm", opAToken1,
            new { scannedLocationBarcode = "BIN-JWT-A", scannedItemBarcode = "SKU-JWT-01", scannedToteLpn = "TOTE-JWT-01", quantity = 5m })))
            Assert.Equal(HttpStatusCode.Unauthorized, rStaleConfirm.StatusCode);

        // Token refresh issues new signed JWT reflecting FacilityB from authoritative DB -> FacilityA mutation denied with 403 Forbidden!
        string opAToken2;
        string opARefresh2;
        using (var rRefresh = await _http.PostAsJsonAsync("/api/v1/auth/refresh-token", new { accessToken = opAToken1, refreshToken = opARefresh1 }))
        {
            Assert.Equal(HttpStatusCode.OK, rRefresh.StatusCode);
            using var doc = JsonDocument.Parse(await rRefresh.Content.ReadAsStringAsync());
            opAToken2 = doc.RootElement.GetProperty("accessToken").GetString()!;
            opARefresh2 = doc.RootElement.GetProperty("refreshToken").GetString()!;
            var refreshedJwt = new JwtSecurityTokenHandler().ReadJwtToken(opAToken2);
            Assert.Equal(FacilityB.ToString(), refreshedJwt.Claims.First(c => c.Type == "facility_id").Value);
        }

        using (var rConfirmWrongFac = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskA2}/confirm", opAToken2,
            new { scannedLocationBarcode = "BIN-JWT-A", scannedItemBarcode = "SKU-JWT-01", scannedToteLpn = "TOTE-JWT-01", quantity = 5m })))
            Assert.Equal(HttpStatusCode.Forbidden, rConfirmWrongFac.StatusCode);

        // 7. Facility revocation (null): stale opAToken2 rejected (401); refreshed token has no facility_id and gets 403 Forbidden
        using (var rRevoke = await _http.SendAsync(BearerRequest(HttpMethod.Put, $"/api/v1/users/{opAId}/assigned-facility", adminToken, new { facilityId = (Guid?)null })))
        {
            Assert.Equal(HttpStatusCode.OK, rRevoke.StatusCode);
            using var doc = JsonDocument.Parse(await rRevoke.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("assignedFacilityId").ValueKind);
        }

        using (var rStaleAfterRevoke = await _http.SendAsync(BearerRequest(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityB}", opAToken2)))
            Assert.Equal(HttpStatusCode.Unauthorized, rStaleAfterRevoke.StatusCode);

        using (var rRefreshRevoked = await _http.PostAsJsonAsync("/api/v1/auth/refresh-token", new { accessToken = opAToken2, refreshToken = opARefresh2 }))
        {
            Assert.Equal(HttpStatusCode.OK, rRefreshRevoked.StatusCode);
            using var doc = JsonDocument.Parse(await rRefreshRevoked.Content.ReadAsStringAsync());
            var token3 = doc.RootElement.GetProperty("accessToken").GetString()!;
            var revokedJwt = new JwtSecurityTokenHandler().ReadJwtToken(token3);
            Assert.DoesNotContain(revokedJwt.Claims, c => c.Type == "facility_id");

            using var rAfterRevokeNext = await _http.SendAsync(BearerRequest(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityB}", token3));
            Assert.Equal(HttpStatusCode.Forbidden, rAfterRevokeNext.StatusCode);
        }
    }

    [Fact]
    public async Task RealJwtPipeline_RoleEscalationPrevention_RevokedPrivileges_AndUserTypeParity()
    {
        var (adminToken, _, _) = await LoginAsync("superadmin@jwt.test", "Password123!");
        var (clientAdminToken, _, _) = await LoginAsync("client-admin@jwt.test", "Password123!");
        var (opsMgrToken, _, _) = await LoginAsync("ops-mgr@jwt.test", "Password123!");

        // 1. ClientAdmin attempts to create Client user with SuperAdmin / WarehouseOperator / Driver -> 403 Forbidden (none created!)
        foreach (var forbiddenRole in new[] { SystemRoles.SuperAdmin, SystemRoles.WarehouseOperator, SystemRoles.OperationsManager, SystemRoles.Driver, "PickSupervisor" })
        {
            using var rEsc = await _http.SendAsync(BearerRequest(HttpMethod.Post, "/api/v1/users", clientAdminToken, new
            {
                email = $"esc-{forbiddenRole.ToLowerInvariant()}@jwt.test",
                password = "Password123!",
                firstName = "Esc",
                lastName = "Attempt",
                userType = UserType.Client,
                clientId = ClientA,
                roles = new[] { forbiddenRole }
            }));
            Assert.Equal(HttpStatusCode.Forbidden, rEsc.StatusCode);
        }

        // ClientAdmin cannot create foreign-tenant user -> 403 Forbidden
        using (var rForeignCreate = await _http.SendAsync(BearerRequest(HttpMethod.Post, "/api/v1/users", clientAdminToken, new
        {
            email = "foreign-create@jwt.test",
            password = "Password123!",
            firstName = "For",
            lastName = "Create",
            userType = UserType.Client,
            clientId = ClientB,
            roles = new[] { SystemRoles.ClientUser }
        })))
            Assert.Equal(HttpStatusCode.Forbidden, rForeignCreate.StatusCode);

        // 2. ClientAdmin CAN create permitted own-tenant ClientUser and update own-tenant client role to ClientAdmin
        Guid ownClientUserId;
        using (var rValidClientCreate = await _http.SendAsync(BearerRequest(HttpMethod.Post, "/api/v1/users", clientAdminToken, new
        {
            email = "own-client-user@jwt.test",
            password = "Password123!",
            firstName = "Own",
            lastName = "Client",
            userType = UserType.Client,
            clientId = ClientA,
            roles = new[] { SystemRoles.ClientUser }
        })))
        {
            Assert.Equal(HttpStatusCode.Created, rValidClientCreate.StatusCode);
            using var doc = JsonDocument.Parse(await rValidClientCreate.Content.ReadAsStringAsync());
            ownClientUserId = doc.RootElement.GetProperty("id").GetGuid();
        }

        using (var rPromotePermitted = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/users/{ownClientUserId}/roles", clientAdminToken, new[] { SystemRoles.ClientAdmin })))
            Assert.Equal(HttpStatusCode.OK, rPromotePermitted.StatusCode);

        // 3. ClientAdmin attempts to grant SuperAdmin / warehouse-only / unknown roles to existing own-tenant user -> 403 Forbidden
        foreach (var escalatedRole in new[] { SystemRoles.SuperAdmin, SystemRoles.WarehouseOperator, SystemRoles.OperationsManager, SystemRoles.Driver, "UnknownFakeRole" })
        {
            using var rGrantEsc = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/users/{ownClientUserId}/roles", clientAdminToken, new[] { escalatedRole }));
            Assert.Equal(HttpStatusCode.Forbidden, rGrantEsc.StatusCode);
        }

        // ClientAdmin cannot manage foreign-tenant user or staff user roles -> 403 Forbidden
        using (var rForeignRoles = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/users/{ForeignClientUserId}/roles", clientAdminToken, new[] { SystemRoles.ClientUser })))
            Assert.Equal(HttpStatusCode.Forbidden, rForeignRoles.StatusCode);

        using (var rStaffRoles = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/users/{StaffNonSuperAdminId}/roles", clientAdminToken, new[] { SystemRoles.ClientUser })))
            Assert.Equal(HttpStatusCode.Forbidden, rStaffRoles.StatusCode);

        // 4. Non-SuperAdmin staff cannot grant SuperAdmin (403) and unknown role fails closed (400)
        using (var rNonSuperGrant = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/users/{PreExistingUnassignedStaffId}/roles", opsMgrToken, new[] { SystemRoles.SuperAdmin })))
            Assert.Equal(HttpStatusCode.Forbidden, rNonSuperGrant.StatusCode);

        using (var rUnknownRole = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/users/{PreExistingUnassignedStaffId}/roles", opsMgrToken, new[] { "NonExistentRole" })))
            Assert.Equal(HttpStatusCode.BadRequest, rUnknownRole.StatusCode);

        // Even SuperAdmin cannot assign SuperAdmin/platform roles to Client user (403 Forbidden)
        using (var rSuperAssignToClient = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/users/{ownClientUserId}/roles", adminToken, new[] { SystemRoles.SuperAdmin })))
            Assert.Equal(HttpStatusCode.Forbidden, rSuperAssignToClient.StatusCode);

        // 5. Legitimate SuperAdmin provisioning + immediate invalidation of JWT and refresh token when SuperAdmin is revoked
        Guid tempSuperStaffId;
        using (var rCreateSuperStaff = await _http.SendAsync(BearerRequest(HttpMethod.Post, "/api/v1/users", adminToken, new
        {
            email = "temp-super@jwt.test",
            password = "Password123!",
            firstName = "Temp",
            lastName = "Super",
            userType = UserType.Staff,
            assignedFacilityId = FacilityA,
            roles = new[] { SystemRoles.SuperAdmin }
        })))
        {
            Assert.Equal(HttpStatusCode.Created, rCreateSuperStaff.StatusCode);
            using var doc = JsonDocument.Parse(await rCreateSuperStaff.Content.ReadAsStringAsync());
            tempSuperStaffId = doc.RootElement.GetProperty("id").GetGuid();
        }

        var (tempSuperToken, tempSuperRefresh, _) = await LoginAsync("temp-super@jwt.test", "Password123!");
        using (var rSuperMe = await _http.SendAsync(BearerRequest(HttpMethod.Get, "/api/v1/auth/me", tempSuperToken)))
            Assert.Equal(HttpStatusCode.OK, rSuperMe.StatusCode);

        // Non-SuperAdmin staff cannot revoke SuperAdmin from tempSuperStaffId -> 403 Forbidden
        using (var rOpsRevokeSuper = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/users/{tempSuperStaffId}/roles", opsMgrToken, new[] { SystemRoles.WarehouseOperator })))
            Assert.Equal(HttpStatusCode.Forbidden, rOpsRevokeSuper.StatusCode);

        // Platform SuperAdmin revokes SuperAdmin -> replaced with WarehouseOperator
        using (var rRevokeSuper = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/users/{tempSuperStaffId}/roles", adminToken, new[] { SystemRoles.WarehouseOperator })))
            Assert.Equal(HttpStatusCode.OK, rRevokeSuper.StatusCode);

        // Old SuperAdmin JWT immediately rejected (401 Unauthorized) AND old refresh token invalidated (401 Unauthorized)
        using (var rStaleSuperUse = await _http.SendAsync(BearerRequest(HttpMethod.Get, $"/api/v1/outbound/pick-tasks?facilityId={FacilityB}", tempSuperToken)))
            Assert.Equal(HttpStatusCode.Unauthorized, rStaleSuperUse.StatusCode);

        using (var rStaleRefreshUse = await _http.PostAsJsonAsync("/api/v1/auth/refresh-token", new { accessToken = tempSuperToken, refreshToken = tempSuperRefresh }))
            Assert.Equal(HttpStatusCode.Unauthorized, rStaleRefreshUse.StatusCode);

        // 6. Forged/stale JWT where signed user_type ("Staff") + role ("SuperAdmin") mismatch DB (UserType.Client) -> 401 Unauthorized
        var forgedCreds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret)),
            SecurityAlgorithms.HmacSha256);
        var forgedJwt = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: new[]
            {
                new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, ownClientUserId.ToString()),
                new System.Security.Claims.Claim("user_type", "Staff"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, SystemRoles.SuperAdmin),
                new System.Security.Claims.Claim("facility_id", FacilityA.ToString())
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: forgedCreds);
        var forgedTokenString = new JwtSecurityTokenHandler().WriteToken(forgedJwt);

        using (var rForgedUse = await _http.SendAsync(BearerRequest(HttpMethod.Get, $"/api/v1/outbound/pick-tasks?facilityId={FacilityA}", forgedTokenString)))
            Assert.Equal(HttpStatusCode.Unauthorized, rForgedUse.StatusCode);

        // 7. Regression: Client user holding a privileged SuperAdmin role cannot bypass CurrentUserContext or OutboundOperationalAuthorization
        var spoofedClientPrincipal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, ownClientUserId.ToString()),
            new System.Security.Claims.Claim("user_type", "Client"),
            new System.Security.Claims.Claim("tenant_id", ClientA.ToString()),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, SystemRoles.SuperAdmin),
            new System.Security.Claims.Claim("permissions", Permissions.Outbound.Pick)
        }, "Test"));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = spoofedClientPrincipal };
        var ctxAccessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext };
        var curUser = new CurrentUserContext(ctxAccessor);
        Assert.False(curUser.IsSuperAdmin);
        Assert.False(curUser.IsStaff);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var tenantProv = new TenantProvider(ctxAccessor);
        tenantProv.SetTenantId(ClientA);
        var outAuth = new Warehouse3PL.Application.Common.Security.OutboundOperationalAuthorization(db, curUser, tenantProv);

        Assert.Throws<Warehouse3PL.Domain.Exceptions.ForbiddenException>(() => outAuth.EnsureStaffOperationalIdentity("ClientSpoofedSuperAdmin"));
        Assert.Throws<Warehouse3PL.Domain.Exceptions.ForbiddenException>(() => outAuth.ValidateStaffMutationFacilityAccess(FacilityA, "ClientSpoofedSuperAdmin"));
        await Assert.ThrowsAsync<Warehouse3PL.Domain.Exceptions.ForbiddenException>(async () =>
            await outAuth.ResolveOutboundQueryScopeAsync(null, FacilityA, allowCrossFacilityRead: true, allowClient: false));
    }

    [Fact]
    public async Task AnonymousRegistration_DisabledAndRejected_ZeroUsersRolesOrTokensCreated()
    {
        var clientAEmail = "anon-clientadmin@jwt.test";
        var clientBEmail = "anon-foreignclient@jwt.test";
        var staffEmail = "anon-staff@jwt.test";
        var driverEmail = "anon-driver@jwt.test";
        var (adminToken, _, _) = await LoginAsync("superadmin@jwt.test", "Password123!");

        // 1. Anonymous attempts (Client+ClientA, Client+ForeignClientB, Staff, Driver) are ALL rejected (403 Forbidden) with zero tokens issued
        foreach (var payload in new object[]
        {
            new { email = clientAEmail, password = "Password123!", firstName = "Anon", lastName = "Admin", userType = UserType.Client, clientId = ClientA },
            new { email = clientBEmail, password = "Password123!", firstName = "Anon", lastName = "Foreign", userType = UserType.Client, clientId = ClientB },
            new { email = staffEmail, password = "Password123!", firstName = "Anon", lastName = "Staff", userType = UserType.Staff },
            new { email = driverEmail, password = "Password123!", firstName = "Anon", lastName = "Driver", userType = UserType.Driver }
        })
        {
            using var resp = await _http.PostAsJsonAsync("/api/v1/auth/register", payload);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
        }

        // 2. Verify zero persisted Users or UserRoles from rejected attempts, and login fails 401
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var emails = new[] { clientAEmail, clientBEmail, staffEmail, driverEmail };
            Assert.Empty(await db.Users.Where(u => emails.Contains(u.Email)).ToListAsync());
        }
        using (var rLoginFail = await _http.PostAsJsonAsync("/api/v1/auth/login", new { email = clientAEmail, password = "Password123!" }))
            Assert.Equal(HttpStatusCode.Unauthorized, rLoginFail.StatusCode);

        // 3. Provisioning via POST /api/v1/users enforces active tenant status (400 Suspended, 404 NonExistent, 400 ClientId on Staff)
        var suspendedClientId = Guid.Parse("740c0001-0000-0000-0000-000000000003");
        using (var rSusp = await _http.SendAsync(BearerRequest(HttpMethod.Post, "/api/v1/users", adminToken, new
        {
            email = "susp-client@jwt.test", password = "Password123!", firstName = "S", lastName = "C",
            userType = UserType.Client, clientId = suspendedClientId, roles = new[] { SystemRoles.ClientUser }
        })))
            Assert.Equal(HttpStatusCode.BadRequest, rSusp.StatusCode);

        using (var rMissTenant = await _http.SendAsync(BearerRequest(HttpMethod.Post, "/api/v1/users", adminToken, new
        {
            email = "miss-client@jwt.test", password = "Password123!", firstName = "M", lastName = "C",
            userType = UserType.Client, clientId = Guid.NewGuid(), roles = new[] { SystemRoles.ClientUser }
        })))
            Assert.Equal(HttpStatusCode.NotFound, rMissTenant.StatusCode);

        using (var rStaffWithClient = await _http.SendAsync(BearerRequest(HttpMethod.Post, "/api/v1/users", adminToken, new
        {
            email = "staff-with-client@jwt.test", password = "Password123!", firstName = "St", lastName = "Cl",
            userType = UserType.Staff, clientId = ClientA, assignedFacilityId = FacilityA
        })))
            Assert.Equal(HttpStatusCode.BadRequest, rStaffWithClient.StatusCode);
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }
}
