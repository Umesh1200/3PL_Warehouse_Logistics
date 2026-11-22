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
    private static readonly Guid FacilityA = Guid.Parse("740c0002-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("740c0002-0000-0000-0000-000000000002");
    private static readonly Guid FacilityInactive = Guid.Parse("740c0002-0000-0000-0000-000000000003");

    private static readonly Guid SuperAdminId = Guid.Parse("740c0003-0000-0000-0000-000000000001");
    private static readonly Guid PreExistingUnassignedStaffId = Guid.Parse("740c0003-0000-0000-0000-000000000002");
    private static readonly Guid ClientAdminId = Guid.Parse("740c0003-0000-0000-0000-000000000003");
    private static readonly Guid InactiveStaffId = Guid.Parse("740c0003-0000-0000-0000-000000000004");

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
        db.Clients.Add(new Client { Id = ClientA, CompanyName = "RealJwt Client", AccountCode = "JWT-A" });

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

        db.Roles.AddRange(roleSuper, roleWhOps, roleSupervisor, roleClientAdmin);

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

        db.Users.AddRange(superAdmin, preUnassigned, clientAdmin, inactiveStaff);

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

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }
}
