using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
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
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Warehouse3PL.WebApi.Security;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public sealed class Phase741OutboundCatalogAndClientTests : IAsyncLifetime
{
    private const string TestJwtSecret = "Nexus3PL-Phase741-CatalogClient-SigningKey-2026!";
    private const string TestIssuer = "nexus3pl-741-auth";
    private const string TestAudience = "nexus3pl-741-api";

    private static readonly Guid ClientA = Guid.Parse("741a0001-0000-0000-0000-000000000001");
    private static readonly Guid ClientB = Guid.Parse("741a0001-0000-0000-0000-000000000002");
    private static readonly Guid FacilityA = Guid.Parse("741a0002-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("741a0002-0000-0000-0000-000000000002");
    private static readonly Guid FacilityC = Guid.Parse("741a0002-0000-0000-0000-000000000003");

    private static readonly Guid ClientAdminAId = Guid.Parse("741a0003-0000-0000-0000-000000000001");
    private static readonly Guid StaffOrderCreatorAId = Guid.Parse("741a0003-0000-0000-0000-000000000002");
    private static readonly Guid StaffWavePlannerAId = Guid.Parse("741a0003-0000-0000-0000-000000000003");
    private static readonly Guid DriverId = Guid.Parse("741a0003-0000-0000-0000-000000000004");
    private static readonly Guid StaffMultiFacReaderFacAId = Guid.Parse("741a0003-0000-0000-0000-000000000005");

    private static readonly Guid ItemClientAActiveId = Guid.Parse("741a0004-0000-0000-0000-000000000001");
    private static readonly Guid ItemClientAInactiveId = Guid.Parse("741a0004-0000-0000-0000-000000000002");
    private static readonly Guid ItemClientBActiveId = Guid.Parse("741a0004-0000-0000-0000-000000000003");

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
            .AddApplicationPart(typeof(OutboundOrdersController).Assembly)
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

        var dbName = $"phase741-p741-01-{Guid.NewGuid():N}";
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
        var facA = new Facility { Id = FacilityA, FacilityCode = "FAC-741-A", Name = "Chicago Hub", CountryCode = "USA", IsActive = true };
        var facB = new Facility { Id = FacilityB, FacilityCode = "FAC-741-B", Name = "Dallas Hub", CountryCode = "USA", IsActive = true };
        var facC = new Facility { Id = FacilityC, FacilityCode = "FAC-741-C", Name = "Newark Hub", CountryCode = "USA", IsActive = true };
        db.Facilities.AddRange(facA, facB, facC);

        var clientA = new Client { Id = ClientA, AccountCode = "CL-A", CompanyName = "Alpha Logistics Co", Status = ClientStatus.Active };
        var clientB = new Client { Id = ClientB, AccountCode = "CL-B", CompanyName = "Beta Medical Supply", Status = ClientStatus.Active };
        db.Clients.AddRange(clientA, clientB);

        db.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityA, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityC, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = ClientB, FacilityId = FacilityB, Status = FacilityAccessStatus.Active });

        db.Items.AddRange(
            new Item
            {
                Id = ItemClientAActiveId,
                ClientId = ClientA,
                Sku = "ALPHA-SKU-01",
                Description = "Alpha Servo Valve",
                ShortName = "Servo Valve",
                StorageTempClass = StorageTempClass.Ambient,
                Status = ItemStatus.Active
            },
            new Item
            {
                Id = ItemClientAInactiveId,
                ClientId = ClientA,
                Sku = "ALPHA-SKU-INACTIVE",
                Description = "Retired Servo",
                Status = ItemStatus.Inactive
            },
            new Item
            {
                Id = ItemClientBActiveId,
                ClientId = ClientB,
                Sku = "BETA-CONFIDENTIAL-99",
                Description = "Foreign Tenant SKU",
                Status = ItemStatus.Active
            });

        // Exact seeded ClientAdmin permissions from DbInitializer.cs (NO items:view, NO orders:allocate, NO waves:manage)
        var clientAdminRole = new Role
        {
            RoleName = SystemRoles.ClientAdmin,
            Description = "ClientAdmin",
            IsSystemRole = true
        };
        string[] seededClientAdminPermissions =
        [
            Permissions.Clients.View,
            Permissions.Users.View, Permissions.Users.Create, Permissions.Users.Edit, Permissions.Users.AssignRoles,
            Permissions.Facilities.View,
            Permissions.Inventory.View, Permissions.Inventory.Adjust, Permissions.Inventory.Count,
            Permissions.Inbound.View, Permissions.Inbound.Receive,
            Permissions.Outbound.View, Permissions.Outbound.Create,
            Permissions.Outbound.OrdersView, Permissions.Outbound.OrdersCreate,
            Permissions.Outbound.OrdersImport, Permissions.Outbound.OrdersCancel,
            Permissions.Billing.View,
            Permissions.Dispatch.View
        ];
        foreach (var perm in seededClientAdminPermissions)
        {
            clientAdminRole.RolePermissions.Add(new RolePermission { Role = clientAdminRole, PermissionKey = perm, AccessLevel = AccessLevel.Admin });
        }

        var orderCreatorRole = new Role { RoleName = "OrderCoordinator", Description = "Staff order creator", IsSystemRole = false };
        orderCreatorRole.RolePermissions.Add(new RolePermission { Role = orderCreatorRole, PermissionKey = Permissions.Outbound.OrdersCreate, AccessLevel = AccessLevel.Write });
        orderCreatorRole.RolePermissions.Add(new RolePermission { Role = orderCreatorRole, PermissionKey = Permissions.Outbound.OrdersImport, AccessLevel = AccessLevel.Write });

        var multiFacReaderRole = new Role { RoleName = "CrossFacilityOrderReader", Description = "Cross-facility reader with order create", IsSystemRole = false };
        multiFacReaderRole.RolePermissions.Add(new RolePermission { Role = multiFacReaderRole, PermissionKey = Permissions.Outbound.OrdersCreate, AccessLevel = AccessLevel.Write });
        multiFacReaderRole.RolePermissions.Add(new RolePermission { Role = multiFacReaderRole, PermissionKey = Permissions.Outbound.OrdersView, AccessLevel = AccessLevel.Read });
        multiFacReaderRole.RolePermissions.Add(new RolePermission { Role = multiFacReaderRole, PermissionKey = Permissions.Analytics.MultiFacilityView, AccessLevel = AccessLevel.Read });

        var wavePlannerRole = new Role { RoleName = "WavePlanner", Description = "Staff wave planner", IsSystemRole = false };
        wavePlannerRole.RolePermissions.Add(new RolePermission { Role = wavePlannerRole, PermissionKey = Permissions.Outbound.WavesManage, AccessLevel = AccessLevel.Write });

        var driverRole = new Role { RoleName = SystemRoles.Driver, Description = "Driver", IsSystemRole = true };
        driverRole.RolePermissions.Add(new RolePermission { Role = driverRole, PermissionKey = Permissions.Dispatch.ExecuteDelivery, AccessLevel = AccessLevel.Execute });

        db.Roles.AddRange(clientAdminRole, orderCreatorRole, multiFacReaderRole, wavePlannerRole, driverRole);

        var pw = hasher.HashPassword("StrongPass#741");
        db.Users.AddRange(
            new User
            {
                Id = ClientAdminAId,
                Email = "clientadmin.a@test.local",
                FirstName = "Client",
                LastName = "AdminA",
                PasswordHash = pw,
                UserType = UserType.Client,
                ClientId = ClientA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = ClientAdminAId, Role = clientAdminRole } }
            },
            new User
            {
                Id = StaffOrderCreatorAId,
                Email = "orderstaff.a@test.local",
                FirstName = "Staff",
                LastName = "CreatorA",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = StaffOrderCreatorAId, Role = orderCreatorRole } }
            },
            new User
            {
                Id = StaffWavePlannerAId,
                Email = "waveplanner.a@test.local",
                FirstName = "Wave",
                LastName = "PlannerA",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = StaffWavePlannerAId, Role = wavePlannerRole } }
            },
            new User
            {
                Id = StaffMultiFacReaderFacAId,
                Email = "multifac.a@test.local",
                FirstName = "Multi",
                LastName = "ReaderA",
                PasswordHash = pw,
                UserType = UserType.Staff,
                AssignedFacilityId = FacilityA,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = StaffMultiFacReaderFacAId, Role = multiFacReaderRole } }
            },
            new User
            {
                Id = DriverId,
                Email = "driver@test.local",
                FirstName = "Fleet",
                LastName = "Driver",
                PasswordHash = pw,
                UserType = UserType.Driver,
                Status = UserStatus.Active,
                UserRoles = { new UserRole { UserId = DriverId, Role = driverRole } }
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

    private HttpRequestMessage AuthedReq(HttpMethod method, string url, Guid userId, object? body = null, Guid? tenantHeader = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(userId));
        if (tenantHeader.HasValue)
        {
            req.Headers.Add("X-Tenant-Id", tenantHeader.Value.ToString());
        }
        if (body != null)
        {
            req.Content = JsonContent.Create(body);
        }
        return req;
    }

    [Fact]
    public async Task SeededClientAdmin_LacksItemsView_CanQueryOwnCatalogItems_AndCreateOrder()
    {
        // Confirm seeded ClientAdmin still lacks broad items:view on /api/v1/items
        using (var itemsResp = await _http.SendAsync(AuthedReq(HttpMethod.Get, "/api/v1/items", ClientAdminAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, itemsResp.StatusCode);
        }

        // Purpose-built outbound catalog lookup returns ONLY active SKUs of ClientA
        using (var catResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?facilityId={FacilityA}", ClientAdminAId)))
        {
            Assert.Equal(HttpStatusCode.OK, catResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await catResp.Content.ReadAsStreamAsync());
            var items = doc.RootElement.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal(ItemClientAActiveId, items[0].GetProperty("id").GetGuid());
            Assert.Equal("ALPHA-SKU-01", items[0].GetProperty("sku").GetString());
        }

        // Submit valid order using catalog item from seeded ClientAdmin account
        var orderPayload = new
        {
            facilityId = FacilityA,
            orderType = "B2BSalesOrder",
            priority = 5,
            serviceLevel = "StandardGround",
            shippingAddress = new
            {
                recipientName = "Client Admin Receiver",
                companyName = "Alpha Logistics Co",
                addressLine1 = "100 Main",
                city = "Chicago",
                stateOrProvince = "IL",
                postalCode = "60601",
                countryCode = "USA"
            },
            lines = new[]
            {
                new { itemId = ItemClientAActiveId, quantityOrdered = 4m }
            }
        };

        using var createResp = await _http.SendAsync(AuthedReq(HttpMethod.Post, "/api/v1/outbound/orders", ClientAdminAId, orderPayload));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        using var orderDoc = await JsonDocument.ParseAsync(await createResp.Content.ReadAsStreamAsync());
        Assert.Equal(ClientA, orderDoc.RootElement.GetProperty("clientId").GetGuid());
        Assert.Equal(ItemClientAActiveId, orderDoc.RootElement.GetProperty("lines")[0].GetProperty("itemId").GetGuid());
    }

    [Fact]
    public async Task ClientAdmin_ForeignTenantSkuLookup_UnauthorizedFacility_AndStaffDirectories_ReturnForbidden()
    {
        // Foreign clientId query parameter -> 403 Forbidden
        using (var resp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?clientId={ClientB}", ClientAdminAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // Spoofed X-Tenant-Id header -> 403 Forbidden
        using (var resp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, "/api/v1/outbound/orders/catalog-items", ClientAdminAId, tenantHeader: ClientB)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // Facility without active ClientFacilityAccess for ClientA -> 403 Forbidden
        using (var resp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?facilityId={FacilityB}", ClientAdminAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // Staff-only client directories must forbid ClientAdmin and Driver
        using (var refResp = await _http.SendAsync(AuthedReq(HttpMethod.Get, "/api/v1/outbound/orders/reference-clients", ClientAdminAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refResp.StatusCode);
        }

        using (var waveCliResp = await _http.SendAsync(AuthedReq(HttpMethod.Get, "/api/v1/outbound/pick-waves/eligible-clients", ClientAdminAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, waveCliResp.StatusCode);
        }

        using (var driverCatResp = await _http.SendAsync(AuthedReq(HttpMethod.Get, "/api/v1/outbound/orders/catalog-items", DriverId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, driverCatResp.StatusCode);
        }
    }

    [Fact]
    public async Task StaffOrderCreatorAndWavePlanner_WithoutClientsView_CanLookupFacilityScopedClientsAndItems()
    {
        // Confirm StaffOrderCreatorAId lacks broad clients:view
        using (var adminClients = await _http.SendAsync(AuthedReq(HttpMethod.Get, "/api/v1/clients", StaffOrderCreatorAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, adminClients.StatusCode);
        }

        // StaffOrderCreatorAId can list only FacilityA-authorized active clients via outbound reference-clients
        using (var refResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/reference-clients?facilityId={FacilityA}", StaffOrderCreatorAId)))
        {
            Assert.Equal(HttpStatusCode.OK, refResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await refResp.Content.ReadAsStreamAsync());
            var items = doc.RootElement.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal(ClientA, items[0].GetProperty("id").GetGuid());
        }

        // StaffOrderCreatorAId cannot bypass facility scope onto FacilityB
        using (var foreignFacResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/reference-clients?facilityId={FacilityB}", StaffOrderCreatorAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, foreignFacResp.StatusCode);
        }

        // StaffWavePlannerAId (only outbound:waves:manage) can query eligible-clients for FacilityA
        using (var waveCliResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/pick-waves/eligible-clients?facilityId={FacilityA}", StaffWavePlannerAId)))
        {
            Assert.Equal(HttpStatusCode.OK, waveCliResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await waveCliResp.Content.ReadAsStreamAsync());
            var items = doc.RootElement.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal(ClientA, items[0].GetProperty("id").GetGuid());
        }
    }

    [Fact]
    public async Task P741_04_StaffCatalogLookup_EnforcesClientFacilityRelationship_AcrossExplicitOmittedAndHeaderCases()
    {
        // 1. Staff at Facility A can list Client A's authorized SKUs (with explicit or omitted facilityId)
        using (var okExplicit = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?clientId={ClientA}&facilityId={FacilityA}", StaffOrderCreatorAId)))
        {
            Assert.Equal(HttpStatusCode.OK, okExplicit.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await okExplicit.Content.ReadAsStreamAsync());
            var items = doc.RootElement.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal(ItemClientAActiveId, items[0].GetProperty("id").GetGuid());
        }

        using (var okOmittedFac = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?clientId={ClientA}", StaffOrderCreatorAId)))
        {
            Assert.Equal(HttpStatusCode.OK, okOmittedFac.StatusCode);
        }

        // 2. Staff at Facility A cannot list Client B's SKUs (Client B is authorized only at Facility B)
        using (var deniedExplicit = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?clientId={ClientB}&facilityId={FacilityA}", StaffOrderCreatorAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedExplicit.StatusCode);
        }

        // 3. Staff cannot bypass facility/client check by omitting facilityId
        using (var deniedOmittedFac = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?clientId={ClientB}", StaffOrderCreatorAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedOmittedFac.StatusCode);
        }

        // 4. Staff cannot bypass using X-Tenant-Id header (ClientB or arbitrary GUID)
        using (var deniedHeaderClientB = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, "/api/v1/outbound/orders/catalog-items", StaffOrderCreatorAId, tenantHeader: ClientB)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedHeaderClientB.StatusCode);
        }

        using (var deniedArbitraryHeader = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?facilityId={FacilityA}", StaffOrderCreatorAId, tenantHeader: Guid.NewGuid())))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedArbitraryHeader.StatusCode);
        }

        // Cross-facility read permission preserves authorized Facility B lookup while rejecting Facility A for ClientB
        using (var crossReadAllowed = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?clientId={ClientB}&facilityId={FacilityB}", StaffMultiFacReaderFacAId)))
        {
            Assert.Equal(HttpStatusCode.OK, crossReadAllowed.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await crossReadAllowed.Content.ReadAsStreamAsync());
            Assert.Equal(ItemClientBActiveId, doc.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        }

        using (var crossReadBadPair = await _http.SendAsync(AuthedReq(
            HttpMethod.Get, $"/api/v1/outbound/orders/catalog-items?clientId={ClientB}&facilityId={FacilityA}", StaffMultiFacReaderFacAId)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossReadBadPair.StatusCode);
        }
    }

    [Fact]
    public async Task P741_04_StaffOrderCreateAndBulkImport_EnforceClientFacilityAccess_WithoutUnauthorizedPersistence()
    {
        var address = new
        {
            recipientName = "Staff Recipient",
            companyName = "Partner Dock",
            addressLine1 = "500 Logistics Blvd",
            city = "Chicago",
            stateOrProvince = "IL",
            postalCode = "60607",
            countryCode = "USA"
        };

        // 5. Authorized Staff order creation & bulk import succeed for valid (ClientA, FacilityA)
        var validOrderPayload = new
        {
            facilityId = FacilityA,
            externalOrderReference = "STAFF-OK-ORD-1",
            orderType = "B2BSalesOrder",
            priority = 6,
            serviceLevel = "StandardGround",
            shippingAddress = address,
            lines = new[] { new { itemId = ItemClientAActiveId, quantityOrdered = 3m } }
        };
        using (var okOrdResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders", StaffOrderCreatorAId, validOrderPayload, tenantHeader: ClientA)))
        {
            Assert.Equal(HttpStatusCode.Created, okOrdResp.StatusCode);
        }

        var validImportPayload = new
        {
            facilityId = FacilityA,
            sourceFileName = "staff-valid-import.json",
            orders = new[]
            {
                new
                {
                    facilityId = FacilityA,
                    externalOrderReference = "STAFF-OK-IMP-1",
                    orderType = "B2BSalesOrder",
                    priority = 5,
                    serviceLevel = "StandardGround",
                    shippingAddress = address,
                    lines = new[] { new { itemId = ItemClientAActiveId, quantityOrdered = 2m } }
                }
            }
        };
        using (var okImpResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders/bulk-import", StaffOrderCreatorAId, validImportPayload, tenantHeader: ClientA)))
        {
            Assert.Equal(HttpStatusCode.OK, okImpResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await okImpResp.Content.ReadAsStreamAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("successfulRows").GetInt32());
        }

        // 6. Unauthorized client/facility order creation and bulk import (ClientB at FacilityA) fail with 403 and zero persistence
        var unauthOrderPayload = new
        {
            facilityId = FacilityA,
            externalOrderReference = "STAFF-UNAUTH-ORD-B",
            orderType = "B2BSalesOrder",
            priority = 6,
            serviceLevel = "StandardGround",
            shippingAddress = address,
            lines = new[] { new { itemId = ItemClientBActiveId, quantityOrdered = 3m } }
        };
        using (var unauthOrdResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders", StaffOrderCreatorAId, unauthOrderPayload, tenantHeader: ClientB)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, unauthOrdResp.StatusCode);
        }

        var unauthImportPayload = new
        {
            facilityId = FacilityA,
            sourceFileName = "staff-unauth-import.json",
            orders = new[]
            {
                new
                {
                    facilityId = FacilityA,
                    externalOrderReference = "STAFF-UNAUTH-IMP-B",
                    orderType = "B2BSalesOrder",
                    priority = 5,
                    serviceLevel = "StandardGround",
                    shippingAddress = address,
                    lines = new[] { new { itemId = ItemClientBActiveId, quantityOrdered = 2m } }
                }
            }
        };
        using (var unauthImpResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders/bulk-import", StaffOrderCreatorAId, unauthImportPayload, tenantHeader: ClientB)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, unauthImpResp.StatusCode);
        }

        // Cross-facility reader (Facility A assigned + Analytics.MultiFacilityView) still cannot mutate Facility B
        var mutFacBPayload = new
        {
            facilityId = FacilityB,
            externalOrderReference = "STAFF-MUTATE-FACB",
            orderType = "B2BSalesOrder",
            priority = 6,
            serviceLevel = "StandardGround",
            shippingAddress = address,
            lines = new[] { new { itemId = ItemClientBActiveId, quantityOrdered = 1m } }
        };
        using (var mutFacBResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders", StaffMultiFacReaderFacAId, mutFacBPayload, tenantHeader: ClientB)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, mutFacBResp.StatusCode);
        }

        using var verifyScope = _app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.OutboundOrders.IgnoreQueryFilters().CountAsync(o => o.ClientId == ClientB));
        Assert.Equal(0, await db.OrderImportBatches.IgnoreQueryFilters().CountAsync(b => b.ClientId == ClientB));
    }

    [Fact]
    public async Task P741_04_BulkImportStockTransfer_EnforcesClientFacilityAccessForDestinationFacility()
    {
        var address = new
        {
            recipientName = "Transfer Receiving Dock",
            companyName = "Alpha Logistics Co",
            addressLine1 = "700 Transfer Pkwy",
            city = "Newark",
            stateOrProvince = "NJ",
            postalCode = "07102",
            countryCode = "USA"
        };

        // 1. Authorized Staff bulk stock-transfer import (ClientA: FacilityA -> FacilityC, both active for ClientA) succeeds
        var validStaffXfer = new
        {
            facilityId = FacilityA,
            sourceFileName = "staff-valid-xfer.json",
            orders = new[]
            {
                new
                {
                    facilityId = FacilityA,
                    destinationFacilityId = FacilityC,
                    externalOrderReference = "XFER-STAFF-OK-1",
                    orderType = "StockTransferOrder",
                    priority = 6,
                    serviceLevel = "FreightLTL",
                    shippingAddress = address,
                    lines = new[] { new { itemId = ItemClientAActiveId, quantityOrdered = 4m } }
                }
            }
        };
        using (var okStaffResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders/bulk-import", StaffOrderCreatorAId, validStaffXfer, tenantHeader: ClientA)))
        {
            Assert.Equal(HttpStatusCode.OK, okStaffResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await okStaffResp.Content.ReadAsStreamAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("successfulRows").GetInt32());
        }

        // 2. Authorized ClientAdmin bulk stock-transfer import (ClientA: FacilityA -> FacilityC) succeeds
        var validClientXfer = new
        {
            facilityId = FacilityA,
            sourceFileName = "client-valid-xfer.json",
            orders = new[]
            {
                new
                {
                    facilityId = FacilityA,
                    destinationFacilityId = FacilityC,
                    externalOrderReference = "XFER-CLIENT-OK-1",
                    orderType = "StockTransferOrder",
                    priority = 5,
                    serviceLevel = "FreightLTL",
                    shippingAddress = address,
                    lines = new[] { new { itemId = ItemClientAActiveId, quantityOrdered = 2m } }
                }
            }
        };
        using (var okClientResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders/bulk-import", ClientAdminAId, validClientXfer)))
        {
            Assert.Equal(HttpStatusCode.OK, okClientResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await okClientResp.Content.ReadAsStreamAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("successfulRows").GetInt32());
        }

        // 3. Unauthorized Staff bulk stock-transfer import (ClientA lacks access to FacilityB) returns 403 without persistence
        var unauthStaffXfer = new
        {
            facilityId = FacilityA,
            sourceFileName = "staff-unauth-xfer.json",
            orders = new object[]
            {
                new
                {
                    facilityId = FacilityA,
                    externalOrderReference = "XFER-MIX-SHOULD-ROLLBACK",
                    orderType = "B2BSalesOrder",
                    priority = 5,
                    serviceLevel = "StandardGround",
                    shippingAddress = address,
                    lines = new[] { new { itemId = ItemClientAActiveId, quantityOrdered = 1m } }
                },
                new
                {
                    facilityId = FacilityA,
                    destinationFacilityId = FacilityB,
                    externalOrderReference = "XFER-STAFF-UNAUTH-B",
                    orderType = "StockTransferOrder",
                    priority = 5,
                    serviceLevel = "FreightLTL",
                    shippingAddress = address,
                    lines = new[] { new { itemId = ItemClientAActiveId, quantityOrdered = 3m } }
                }
            }
        };
        using (var unauthStaffResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders/bulk-import", StaffOrderCreatorAId, unauthStaffXfer, tenantHeader: ClientA)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, unauthStaffResp.StatusCode);
        }

        // 4. Unauthorized ClientAdmin bulk stock-transfer import (FacilityB) returns 403 without persistence
        var unauthClientXfer = new
        {
            facilityId = FacilityA,
            sourceFileName = "client-unauth-xfer.json",
            orders = new[]
            {
                new
                {
                    facilityId = FacilityA,
                    destinationFacilityId = FacilityB,
                    externalOrderReference = "XFER-CLIENT-UNAUTH-B",
                    orderType = "StockTransferOrder",
                    priority = 5,
                    serviceLevel = "FreightLTL",
                    shippingAddress = address,
                    lines = new[] { new { itemId = ItemClientAActiveId, quantityOrdered = 3m } }
                }
            }
        };
        using (var unauthClientResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders/bulk-import", ClientAdminAId, unauthClientXfer)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, unauthClientResp.StatusCode);
        }

        // 5. Same-facility stock-transfer row is rejected as failed row with zero OutboundOrder persisted
        var sameFacXfer = new
        {
            facilityId = FacilityA,
            sourceFileName = "same-fac-xfer.json",
            orders = new[]
            {
                new
                {
                    facilityId = FacilityA,
                    destinationFacilityId = FacilityA,
                    externalOrderReference = "XFER-SAME-FAC-FAIL",
                    orderType = "StockTransferOrder",
                    priority = 5,
                    serviceLevel = "FreightLTL",
                    shippingAddress = address,
                    lines = new[] { new { itemId = ItemClientAActiveId, quantityOrdered = 2m } }
                }
            }
        };
        using (var sameFacResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders/bulk-import", StaffOrderCreatorAId, sameFacXfer, tenantHeader: ClientA)))
        {
            Assert.Equal(HttpStatusCode.OK, sameFacResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await sameFacResp.Content.ReadAsStreamAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("successfulRows").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("failedRows").GetInt32());
        }

        using var verifyScope = _app.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.OutboundOrders.IgnoreQueryFilters().CountAsync(o =>
            o.DestinationFacilityId == FacilityB ||
            o.ExternalOrderReference == "XFER-MIX-SHOULD-ROLLBACK" ||
            o.ExternalOrderReference == "XFER-SAME-FAC-FAIL"));
        Assert.Equal(0, await db.OrderImportBatches.IgnoreQueryFilters().CountAsync(b =>
            b.SourceFileName == "staff-unauth-xfer.json" ||
            b.SourceFileName == "client-unauth-xfer.json"));

        // 6. Ordinary sales orders with an irrelevant/unused DestinationFacilityId succeed and persist DestinationFacilityId = null
        var salesWithUnusedDest = new
        {
            facilityId = FacilityA,
            sourceFileName = "sales-unused-dest.json",
            orders = new[]
            {
                new
                {
                    facilityId = FacilityA,
                    destinationFacilityId = FacilityB,
                    externalOrderReference = "SALES-UNUSED-DEST-1",
                    orderType = "B2BSalesOrder",
                    priority = 5,
                    serviceLevel = "StandardGround",
                    shippingAddress = address,
                    lines = new[] { new { itemId = ItemClientAActiveId, quantityOrdered = 2m } }
                }
            }
        };
        using (var salesResp = await _http.SendAsync(AuthedReq(
            HttpMethod.Post, "/api/v1/outbound/orders/bulk-import", StaffOrderCreatorAId, salesWithUnusedDest, tenantHeader: ClientA)))
        {
            Assert.Equal(HttpStatusCode.OK, salesResp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await salesResp.Content.ReadAsStreamAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("successfulRows").GetInt32());
        }

        using var verifySalesScope = _app.Services.CreateScope();
        var salesDb = verifySalesScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var importedSales = await salesDb.OutboundOrders.IgnoreQueryFilters()
            .SingleAsync(o => o.ExternalOrderReference == "SALES-UNUSED-DEST-1");
        Assert.Null(importedSales.DestinationFacilityId);
    }
}
