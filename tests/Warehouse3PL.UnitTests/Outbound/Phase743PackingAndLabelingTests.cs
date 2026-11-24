using System.Net;
using System.Net.Http.Headers;
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
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Packing;
using Warehouse3PL.Infrastructure.Services.Shipping;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Warehouse3PL.WebApi.Security;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public sealed class Phase743PackingAndLabelingTests : IAsyncLifetime
{
    private const string TestJwtSecret = "Nexus3PL-Phase743-Packing-Label-SigningKey-2026!";
    private const string TestIssuer = "nexus3pl-743-auth";
    private const string TestAudience = "nexus3pl-743-api";

    private static readonly Guid ClientA = Guid.Parse("743a0001-0000-0000-0000-000000000001");
    private static readonly Guid FacilityA = Guid.Parse("743a0002-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("743a0002-0000-0000-0000-000000000002");

    private static readonly Guid PackOnlyStaffId = Guid.Parse("743a0003-0000-0000-0000-000000000001");
    private static readonly Guid ShipOnlyStaffId = Guid.Parse("743a0003-0000-0000-0000-000000000002");
    private static readonly Guid PackAndShipStaffId = Guid.Parse("743a0003-0000-0000-0000-000000000003");
    private static readonly Guid CrossFacStaffId = Guid.Parse("743a0003-0000-0000-0000-000000000004");
    private static readonly Guid ClientAdminId = Guid.Parse("743a0003-0000-0000-0000-000000000005");
    private static readonly Guid DriverUserId = Guid.Parse("743a0003-0000-0000-0000-000000000006");

    private static readonly Guid StationAId = Guid.Parse("743a0004-0000-0000-0000-000000000001");
    private static readonly Guid StationBId = Guid.Parse("743a0004-0000-0000-0000-000000000002");
    private static readonly Guid BoxMediumId = Guid.Parse("743a0005-0000-0000-0000-000000000001");
    private static readonly Guid BoxFacBId = Guid.Parse("743a0005-0000-0000-0000-000000000002");
    private static readonly Guid OrderAId = Guid.Parse("743a0006-0000-0000-0000-000000000001");
    private static readonly Guid OrderBId = Guid.Parse("743a0006-0000-0000-0000-000000000002");
    private static readonly Guid ToteAId = Guid.Parse("743a0007-0000-0000-0000-000000000001");
    private static readonly Guid ToteStockId = Guid.Parse("743a0008-0000-0000-0000-000000000001");
    private static readonly Guid ItemAId = Guid.Parse("743a0009-0000-0000-0000-000000000001");

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
            .AddApplicationPart(typeof(PackingController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();

        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddScoped<ICartonizationService, CartonizationService>();
        builder.Services.AddScoped<IPackingExecutionService, PackingExecutionService>();
        builder.Services.AddScoped<IShippingCarrierService, SimulatedCarrierService>();
        builder.Services.AddScoped<IShippingLabelService, ShippingLabelService>();

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

        var dbName = $"phase743-packing-{Guid.NewGuid():N}";
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
            new Facility
            {
                Id = FacilityA,
                FacilityCode = "FAC-743-A",
                Name = "Chicago Packing Hub",
                AddressLine1 = "1000 Logistics Parkway",
                City = "Chicago",
                StateOrProvince = "IL",
                PostalCode = "60601",
                CountryCode = "USA",
                IsActive = true
            },
            new Facility
            {
                Id = FacilityB,
                FacilityCode = "FAC-743-B",
                Name = "Dallas Packing Hub",
                AddressLine1 = "2200 Freight Blvd",
                City = "Dallas",
                StateOrProvince = "TX",
                PostalCode = "75201",
                CountryCode = "USA",
                IsActive = true
            });

        db.Clients.Add(new Client { Id = ClientA, AccountCode = "CL-743", CompanyName = "Vanguard Logistics", Status = ClientStatus.Active });

        var packOnlyRole = new Role { RoleName = "PackBenchOnly", IsSystemRole = false };
        packOnlyRole.RolePermissions.Add(new RolePermission { Role = packOnlyRole, PermissionKey = Permissions.Outbound.Pack, AccessLevel = AccessLevel.Execute });

        var shipOnlyRole = new Role { RoleName = "ShippingLabelOnly", IsSystemRole = false };
        shipOnlyRole.RolePermissions.Add(new RolePermission { Role = shipOnlyRole, PermissionKey = Permissions.Outbound.Ship, AccessLevel = AccessLevel.Execute });

        var packAndShipRole = new Role { RoleName = "PackAndShipLead", IsSystemRole = false };
        packAndShipRole.RolePermissions.Add(new RolePermission { Role = packAndShipRole, PermissionKey = Permissions.Outbound.Pack, AccessLevel = AccessLevel.Execute });
        packAndShipRole.RolePermissions.Add(new RolePermission { Role = packAndShipRole, PermissionKey = Permissions.Outbound.Ship, AccessLevel = AccessLevel.Execute });
        packAndShipRole.RolePermissions.Add(new RolePermission { Role = packAndShipRole, PermissionKey = Permissions.Analytics.MultiFacilityView, AccessLevel = AccessLevel.Read });

        var clientLegacyRole = new Role { RoleName = SystemRoles.ClientAdmin, IsSystemRole = true };
        clientLegacyRole.RolePermissions.Add(new RolePermission { Role = clientLegacyRole, PermissionKey = Permissions.Outbound.Pack, AccessLevel = AccessLevel.Admin });
        clientLegacyRole.RolePermissions.Add(new RolePermission { Role = clientLegacyRole, PermissionKey = Permissions.Outbound.Ship, AccessLevel = AccessLevel.Admin });

        var driverRole = new Role { RoleName = SystemRoles.Driver, IsSystemRole = true };
        driverRole.RolePermissions.Add(new RolePermission { Role = driverRole, PermissionKey = Permissions.Dispatch.ExecuteDelivery, AccessLevel = AccessLevel.Execute });

        db.Roles.AddRange(packOnlyRole, shipOnlyRole, packAndShipRole, clientLegacyRole, driverRole);

        var pw = hasher.HashPassword("StrongPass#743");
        db.Users.AddRange(
            new User { Id = PackOnlyStaffId, Email = "packonly@test.local", FirstName = "Pam", LastName = "Packer", PasswordHash = pw, UserType = UserType.Staff, AssignedFacilityId = FacilityA, Status = UserStatus.Active, UserRoles = { new UserRole { UserId = PackOnlyStaffId, Role = packOnlyRole } } },
            new User { Id = ShipOnlyStaffId, Email = "shiponly@test.local", FirstName = "Sam", LastName = "Shipper", PasswordHash = pw, UserType = UserType.Staff, AssignedFacilityId = FacilityA, Status = UserStatus.Active, UserRoles = { new UserRole { UserId = ShipOnlyStaffId, Role = shipOnlyRole } } },
            new User { Id = PackAndShipStaffId, Email = "packship@test.local", FirstName = "Pat", LastName = "BenchLead", PasswordHash = pw, UserType = UserType.Staff, AssignedFacilityId = FacilityA, Status = UserStatus.Active, UserRoles = { new UserRole { UserId = PackAndShipStaffId, Role = packAndShipRole } } },
            new User { Id = CrossFacStaffId, Email = "cross@test.local", FirstName = "Carl", LastName = "Dallas", PasswordHash = pw, UserType = UserType.Staff, AssignedFacilityId = FacilityB, Status = UserStatus.Active, UserRoles = { new UserRole { UserId = CrossFacStaffId, Role = packAndShipRole } } },
            new User { Id = ClientAdminId, Email = "client@test.local", FirstName = "Cli", LastName = "Admin", PasswordHash = pw, UserType = UserType.Client, ClientId = ClientA, Status = UserStatus.Active, UserRoles = { new UserRole { UserId = ClientAdminId, Role = clientLegacyRole } } },
            new User { Id = DriverUserId, Email = "driver@test.local", FirstName = "Dan", LastName = "Driver", PasswordHash = pw, UserType = UserType.Driver, Status = UserStatus.Active, UserRoles = { new UserRole { UserId = DriverUserId, Role = driverRole } } });

        db.Locations.AddRange(
            new Location { Id = StationAId, FacilityId = FacilityA, LocationCode = "PACK-STN-01", Barcode = "PACK-STN-01", AisleCode = "PK", RackCode = "01", ShelfLevel = 1, LocationType = LocationType.PackingStation, IsActive = true },
            new Location { Id = StationBId, FacilityId = FacilityB, LocationCode = "PACK-STN-B1", Barcode = "PACK-STN-B1", AisleCode = "PK", RackCode = "01", ShelfLevel = 1, LocationType = LocationType.PackingStation, IsActive = true });

        db.PackagingMaterials.AddRange(
            new PackagingMaterial { Id = BoxMediumId, FacilityId = FacilityA, ClientId = null, Code = "BOX-MED-01", Name = "Medium Corrugated Box", MaterialType = PackagingMaterialType.CorrugatedBox, LengthCm = 40m, WidthCm = 30m, HeightCm = 25m, MaxWeightKg = 20m, TareWeightKg = 0.45m, IsActive = true },
            new PackagingMaterial { Id = BoxFacBId, FacilityId = FacilityB, ClientId = null, Code = "BOX-FACB-01", Name = "Dallas Box", MaterialType = PackagingMaterialType.CorrugatedBox, LengthCm = 40m, WidthCm = 30m, HeightCm = 25m, MaxWeightKg = 20m, TareWeightKg = 0.45m, IsActive = true });

        var item = new Item
        {
            Id = ItemAId,
            ClientId = ClientA,
            Sku = "SKU-PACK-743",
            Description = "Industrial Servo Actuator",
            LengthCm = 15m,
            WidthCm = 10m,
            HeightCm = 8m,
            WeightKg = 1.5m,
            IsLotTracked = false
        };
        db.Items.Add(item);

        var binId = Guid.NewGuid();
        db.Locations.Add(new Location { Id = binId, FacilityId = FacilityA, LocationCode = "BIN-743-01", Barcode = "BIN-743-01", AisleCode = "01", RackCode = "01", ShelfLevel = 1, LocationType = LocationType.Bin, IsActive = true, IsPickable = true });

        var orderA = new OutboundOrder
        {
            Id = OrderAId,
            ClientId = ClientA,
            FacilityId = FacilityA,
            OrderNumber = "SO-743-BENCH-1",
            CustomerName = "Precision Robotics",
            Status = OutboundOrderStatus.InProgress,
            Priority = 8,
            ServiceLevel = CarrierServiceLevel.StandardGround,
            ShippingAddress = new ShippingAddress
            {
                RecipientName = "Jane Receiving",
                CompanyName = "Precision Robotics",
                AddressLine1 = "500 Tech Pkwy",
                City = "Austin",
                StateOrProvince = "TX",
                PostalCode = "78701",
                CountryCode = "US"
            }
        };
        var lineA = new OutboundOrderLine
        {
            Id = Guid.NewGuid(),
            OutboundOrderId = OrderAId,
            ItemId = ItemAId,
            LineNumber = 1,
            QuantityOrdered = 4m,
            QuantityAllocated = 4m,
            Status = OutboundOrderLineStatus.Allocated
        };
        orderA.Lines.Add(lineA);

        var orderB = new OutboundOrder
        {
            Id = OrderBId,
            ClientId = ClientA,
            FacilityId = FacilityB,
            OrderNumber = "SO-743-FACB-1",
            CustomerName = "Dallas Buyer",
            Status = OutboundOrderStatus.Allocated
        };
        orderB.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = OrderBId, ItemId = ItemAId, LineNumber = 1, QuantityOrdered = 2m, QuantityAllocated = 2m, Status = OutboundOrderLineStatus.Allocated });
        db.OutboundOrders.AddRange(orderA, orderB);

        db.HandlingUnits.Add(new HandlingUnit
        {
            Id = ToteAId,
            ClientId = ClientA,
            FacilityId = FacilityA,
            LpnCode = "TOTE-743-01",
            UnitType = HandlingUnitType.Tote,
            Status = HandlingUnitStatus.InUse,
            CurrentLocationId = StationAId,
            CurrentOutboundOrderId = OrderAId
        });

        db.StockOnHand.Add(new StockOnHand
        {
            Id = ToteStockId,
            ClientId = ClientA,
            FacilityId = FacilityA,
            ItemId = ItemAId,
            LocationId = StationAId,
            HandlingUnitId = ToteAId,
            LpnCode = "TOTE-743-01",
            QtyAvailable = 0m,
            QtyAllocated = 4m,
            ReceivedAtUtc = DateTime.UtcNow.AddDays(-1),
            Version = 1
        });

        var waveId = Guid.NewGuid();
        var wave = new PickWave { Id = waveId, ClientId = ClientA, FacilityId = FacilityA, WaveNumber = "WAVE-743-01", Status = PickWaveStatus.Completed };
        var allocId = Guid.NewGuid();
        db.InventoryAllocations.Add(new InventoryAllocation
        {
            Id = allocId,
            ClientId = ClientA,
            FacilityId = FacilityA,
            OutboundOrderId = OrderAId,
            OutboundOrderLineId = lineA.Id,
            StockOnHandId = ToteStockId,
            TargetHandlingUnitId = ToteAId,
            ItemId = ItemAId,
            LocationId = binId,
            QuantityRequested = 4m,
            QuantityAllocated = 4m,
            QuantityPicked = 4m,
            Status = AllocationStatus.Picked
        });
        wave.Tasks.Add(new PickTask
        {
            Id = Guid.NewGuid(),
            ClientId = ClientA,
            FacilityId = FacilityA,
            PickWaveId = waveId,
            OutboundOrderId = OrderAId,
            OutboundOrderLineId = lineA.Id,
            InventoryAllocationId = allocId,
            StockOnHandId = ToteStockId,
            ItemId = ItemAId,
            SourceLocationId = binId,
            TargetHandlingUnitId = ToteAId,
            TargetToteBarcode = "TOTE-743-01",
            QuantityToPick = 4m,
            QuantityPicked = 4m,
            Status = PickTaskStatus.Completed,
            Version = 1
        });
        db.PickWaves.Add(wave);
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

    private HttpRequestMessage JsonReq(HttpMethod method, string url, Guid userId, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(userId));
        if (body != null)
        {
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        return req;
    }

    [Fact]
    public async Task PackingReferenceEndpoints_EnforceStaffOnlyAndStrictAssignedFacilityScope()
    {
        // PackOnly staff without facilities:view or outbound:orders:view loads FacilityA workbench references
        using (var stnResp = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/packing/stations?facilityId={FacilityA}", PackOnlyStaffId)))
        {
            Assert.Equal(HttpStatusCode.OK, stnResp.StatusCode);
            var stns = JsonSerializer.Deserialize<JsonElement>(await stnResp.Content.ReadAsStringAsync());
            Assert.Single(stns.EnumerateArray());
        }

        using (var matResp = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/packing/packaging-materials?facilityId={FacilityA}&clientId={ClientA}", PackOnlyStaffId)))
        {
            Assert.Equal(HttpStatusCode.OK, matResp.StatusCode);
            var mats = JsonSerializer.Deserialize<JsonElement>(await matResp.Content.ReadAsStringAsync());
            Assert.Single(mats.EnumerateArray());
        }

        using (var ordResp = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/packing/packable-orders?facilityId={FacilityA}", PackOnlyStaffId)))
        {
            Assert.Equal(HttpStatusCode.OK, ordResp.StatusCode);
            var doc = JsonSerializer.Deserialize<JsonElement>(await ordResp.Content.ReadAsStringAsync());
            var items = doc.GetProperty("items").EnumerateArray().ToList();
            Assert.Single(items);
            Assert.Equal("SO-743-BENCH-1", items[0].GetProperty("orderNumber").GetString());
        }

        // Combined outbound:pack + Analytics.MultiFacilityView MUST STILL BE DENIED FacilityB reference lookups
        foreach (var crossUrl in new[]
        {
            $"/api/v1/outbound/packing/stations?facilityId={FacilityB}",
            $"/api/v1/outbound/packing/packaging-materials?facilityId={FacilityB}",
            $"/api/v1/outbound/packing/packable-orders?facilityId={FacilityB}"
        })
        {
            using var resp = await _http.SendAsync(JsonReq(HttpMethod.Get, crossUrl, PackAndShipStaffId));
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // Foreign order on cartonization recommend rejected 403 even if FacilityA passed in query body
        using (var badCarton = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/cartonization/recommend", PackAndShipStaffId, new
        {
            clientId = ClientA,
            facilityId = FacilityA,
            outboundOrderId = OrderBId
        })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, badCarton.StatusCode);
        }

        // Combined Outbound.Pack + Analytics.MultiFacilityView MUST BE DENIED FacilityB cartonization (both WITH and WITHOUT OutboundOrderId)
        using (var crossFacCartonNoOrder = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/cartonization/recommend", PackAndShipStaffId, new
        {
            clientId = ClientA,
            facilityId = FacilityB,
            items = new[]
            {
                new { itemId = ItemAId, sku = "SKU-PACK-743", quantity = 1m, lengthCm = 10m, widthCm = 10m, heightCm = 8m, weightKg = 1.5m }
            }
        })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossFacCartonNoOrder.StatusCode);
        }

        using (var crossFacCartonWithOrder = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/cartonization/recommend", PackAndShipStaffId, new
        {
            clientId = ClientA,
            facilityId = FacilityB,
            outboundOrderId = OrderBId
        })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossFacCartonWithOrder.StatusCode);
        }

        // ShipOnly, ClientAdmin (legacy pack/ship claims), and Driver denied from packing reference & session endpoints
        foreach (var forbiddenUser in new[] { ShipOnlyStaffId, ClientAdminId, DriverUserId })
        {
            using var resp = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/packing/stations?facilityId={FacilityA}", forbiddenUser));
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }
    }

    [Fact]
    public async Task FullPackingCartonizationIdempotencyAndLabelVoidLifecycle_EnforcesPermissionBoundaries()
    {
        // 1. Cartonization recommendation
        using (var recResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/cartonization/recommend", PackOnlyStaffId, new
        {
            clientId = ClientA,
            facilityId = FacilityA,
            outboundOrderId = OrderAId,
            preferredPackagingMaterialId = BoxMediumId,
            volumeUtilizationFactor = 0.85m
        })))
        {
            Assert.Equal(HttpStatusCode.OK, recResp.StatusCode);
            var recJson = JsonSerializer.Deserialize<JsonElement>(await recResp.Content.ReadAsStringAsync());
            Assert.True(recJson.GetProperty("isSuccess").GetBoolean());
            Assert.Equal(1, recJson.GetProperty("recommendedPackageCount").GetInt32());
        }

        // 2. Start packing session (and verify ShipOnly cannot start session -> 403)
        using (var shipStartDenied = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/sessions/start", ShipOnlyStaffId, new
        {
            facilityId = FacilityA,
            packingStationLocationId = StationAId,
            outboundOrderId = OrderAId
        })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, shipStartDenied.StatusCode);
        }

        Guid sessionId;
        using (var startResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/sessions/start", PackOnlyStaffId, new
        {
            facilityId = FacilityA,
            packingStationLocationId = StationAId,
            outboundOrderId = OrderAId
        })))
        {
            Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
            var startJson = JsonSerializer.Deserialize<JsonElement>(await startResp.Content.ReadAsStringAsync());
            sessionId = startJson.GetProperty("sessionId").GetGuid();
        }

        // 3. Create carton paired 1:1 with HandlingUnit
        Guid packageId;
        using (var pkgResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/packages/create", PackOnlyStaffId, new
        {
            packingSessionId = sessionId,
            packagingMaterialId = BoxMediumId
        })))
        {
            Assert.Equal(HttpStatusCode.OK, pkgResp.StatusCode);
            packageId = JsonSerializer.Deserialize<JsonElement>(await pkgResp.Content.ReadAsStringAsync()).GetProperty("packageId").GetGuid();
        }

        // 4. Pack item with IdempotencyKey retry -> EXACT single stock transfer & ledger row
        const string idempKey = "pack-idemp-743-abc";
        using (var pack1 = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/items/pack", PackOnlyStaffId, new
        {
            packingSessionId = sessionId,
            packageId,
            scannedToteLpn = "TOTE-743-01",
            scannedItemBarcode = "SKU-PACK-743",
            quantity = 4m,
            idempotencyKey = idempKey
        })))
        {
            Assert.Equal(HttpStatusCode.OK, pack1.StatusCode);
        }

        using (var pack2Retry = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/items/pack", PackOnlyStaffId, new
        {
            packingSessionId = sessionId,
            packageId,
            scannedToteLpn = "TOTE-743-01",
            scannedItemBarcode = "SKU-PACK-743",
            quantity = 4m,
            idempotencyKey = idempKey
        })))
        {
            Assert.Equal(HttpStatusCode.OK, pack2Retry.StatusCode);
            var retryJson = JsonSerializer.Deserialize<JsonElement>(await pack2Retry.Content.ReadAsStringAsync());
            Assert.True(retryJson.GetProperty("isSuccess").GetBoolean());
            Assert.Equal(4m, retryJson.GetProperty("quantityPacked").GetDecimal());
        }

        using (var stockScope = _app.Services.CreateScope())
        {
            var db = stockScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var toteStock = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.Id == ToteStockId);
            var packTxs = await db.InventoryTransactions.IgnoreQueryFilters().Where(t => t.ReferenceId == packageId && t.TxType == InventoryTxType.Pack).ToListAsync();
            Assert.Equal(0m, toteStock.QtyAllocated);
            Assert.Single(packTxs);
        }

        // 5. Close package with scale weight & Release empty tote
        using (var closeResp = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/packing/packages/{packageId}/close", PackOnlyStaffId, new
        {
            scaleGrossWeightKg = 6.45m
        })))
        {
            Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);
        }

        using (var releaseResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/totes/release", PackOnlyStaffId, new
        {
            toteLpn = "TOTE-743-01",
            facilityId = FacilityA
        })))
        {
            Assert.Equal(HttpStatusCode.OK, releaseResp.StatusCode);
        }

        // 6. Complete packing session
        using (var completeResp = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/packing/sessions/{sessionId}/complete", PackOnlyStaffId)))
        {
            Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);
        }

        // 7. PackOnly denied from shipping rates/labels (403); ShipOnly authorized!
        using (var packRateDenied = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipping/rates", PackOnlyStaffId, new { packageId })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, packRateDenied.StatusCode);
        }

        using (var ratesResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipping/rates", ShipOnlyStaffId, new { packageId })))
        {
            Assert.Equal(HttpStatusCode.OK, ratesResp.StatusCode);
            var rates = JsonSerializer.Deserialize<JsonElement>(await ratesResp.Content.ReadAsStringAsync());
            Assert.True(rates.GetArrayLength() >= 4);
        }

        // 8. Decoupled label failure leaves status Packed; real generation -> LabelPrinted; idempotent repeat returns same LabelId; Void -> Packed
        using (var failLabelResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipping/labels/generate", ShipOnlyStaffId, new
        {
            packageId,
            carrierCode = "SIM-FEDEX",
            serviceLevel = CarrierServiceLevel.StandardGround,
            labelFormat = "ZPL",
            idempotencyKey = "lbl-fail-743",
            simulateFailure = true
        })))
        {
            Assert.Equal(HttpStatusCode.OK, failLabelResp.StatusCode);
            var json = JsonSerializer.Deserialize<JsonElement>(await failLabelResp.Content.ReadAsStringAsync());
            Assert.False(json.GetProperty("isSuccess").GetBoolean());
            Assert.Equal("Packed", json.GetProperty("packageStatus").GetString());
        }

        Guid labelId;
        using (var okLabelResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipping/labels/generate", ShipOnlyStaffId, new
        {
            packageId,
            carrierCode = "SIM-FEDEX",
            serviceLevel = CarrierServiceLevel.StandardGround,
            labelFormat = "ZPL",
            idempotencyKey = "lbl-ok-743",
            simulateFailure = false
        })))
        {
            Assert.Equal(HttpStatusCode.OK, okLabelResp.StatusCode);
            var json = JsonSerializer.Deserialize<JsonElement>(await okLabelResp.Content.ReadAsStringAsync());
            Assert.True(json.GetProperty("isSuccess").GetBoolean());
            Assert.Equal("LabelPrinted", json.GetProperty("packageStatus").GetString());
            labelId = json.GetProperty("labelId").GetGuid();
        }

        using (var dupLabelResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipping/labels/generate", ShipOnlyStaffId, new
        {
            packageId,
            carrierCode = "SIM-FEDEX",
            serviceLevel = CarrierServiceLevel.StandardGround,
            labelFormat = "ZPL",
            idempotencyKey = "lbl-ok-743",
            simulateFailure = false
        })))
        {
            Assert.Equal(HttpStatusCode.OK, dupLabelResp.StatusCode);
            var json = JsonSerializer.Deserialize<JsonElement>(await dupLabelResp.Content.ReadAsStringAsync());
            Assert.Equal(labelId, json.GetProperty("labelId").GetGuid());
        }

        using (var voidResp = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipping/labels/{labelId}/void", ShipOnlyStaffId, new
        {
            reason = "Customer upgraded to express",
            idempotencyKey = "void-ok-743",
            simulateFailure = false
        })))
        {
            Assert.Equal(HttpStatusCode.OK, voidResp.StatusCode);
            var json = JsonSerializer.Deserialize<JsonElement>(await voidResp.Content.ReadAsStringAsync());
            Assert.True(json.GetProperty("isSuccess").GetBoolean());
            Assert.Equal("Packed", json.GetProperty("packageStatus").GetString());
            Assert.Equal("Voided", json.GetProperty("labelStatus").GetString());
        }
    }

    [Fact]
    public async Task MixedSkuPackingAndScaleClose_EnforcesActualPerLineWeightAndMaterialMaxWeight()
    {
        var heavyItemId = Guid.NewGuid();
        var lightItemId = Guid.NewGuid();
        var mixedOrderId = Guid.NewGuid();
        var heavyLineId = Guid.NewGuid();
        var lightLineId = Guid.NewGuid();
        var mixedToteId = Guid.NewGuid();
        var heavyStockId = Guid.NewGuid();
        var lightStockId = Guid.NewGuid();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Items.AddRange(
                new Item { Id = heavyItemId, ClientId = ClientA, Sku = "SKU-HEAVY-18", Description = "18kg Pump", LengthCm = 20m, WidthCm = 20m, HeightCm = 15m, WeightKg = 18m },
                new Item { Id = lightItemId, ClientId = ClientA, Sku = "SKU-LIGHT-03", Description = "3kg Valve", LengthCm = 10m, WidthCm = 10m, HeightCm = 10m, WeightKg = 3m });

            var mixedOrder = new OutboundOrder
            {
                Id = mixedOrderId,
                ClientId = ClientA,
                FacilityId = FacilityA,
                OrderNumber = "SO-743-MIXED-WT",
                CustomerName = "Heavy Industrial Buyer",
                Status = OutboundOrderStatus.InProgress
            };
            mixedOrder.Lines.Add(new OutboundOrderLine { Id = heavyLineId, OutboundOrderId = mixedOrderId, ItemId = heavyItemId, LineNumber = 1, QuantityOrdered = 1m, QuantityAllocated = 1m, Status = OutboundOrderLineStatus.Allocated });
            mixedOrder.Lines.Add(new OutboundOrderLine { Id = lightLineId, OutboundOrderId = mixedOrderId, ItemId = lightItemId, LineNumber = 2, QuantityOrdered = 1m, QuantityAllocated = 1m, Status = OutboundOrderLineStatus.Allocated });
            db.OutboundOrders.Add(mixedOrder);

            db.HandlingUnits.Add(new HandlingUnit
            {
                Id = mixedToteId,
                ClientId = ClientA,
                FacilityId = FacilityA,
                LpnCode = "TOTE-MIXED-WT",
                UnitType = HandlingUnitType.Tote,
                Status = HandlingUnitStatus.InUse,
                CurrentLocationId = StationAId,
                CurrentOutboundOrderId = mixedOrderId
            });

            db.StockOnHand.AddRange(
                new StockOnHand { Id = heavyStockId, ClientId = ClientA, FacilityId = FacilityA, ItemId = heavyItemId, LocationId = StationAId, HandlingUnitId = mixedToteId, LpnCode = "TOTE-MIXED-WT", QtyAvailable = 0m, QtyAllocated = 1m, Version = 1 },
                new StockOnHand { Id = lightStockId, ClientId = ClientA, FacilityId = FacilityA, ItemId = lightItemId, LocationId = StationAId, HandlingUnitId = mixedToteId, LpnCode = "TOTE-MIXED-WT", QtyAvailable = 0m, QtyAllocated = 1m, Version = 1 });

            var dummyLocId = db.Locations.IgnoreQueryFilters().First(l => l.FacilityId == FacilityA).Id;
            db.InventoryAllocations.AddRange(
                new InventoryAllocation { Id = Guid.NewGuid(), ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = mixedOrderId, OutboundOrderLineId = heavyLineId, StockOnHandId = heavyStockId, TargetHandlingUnitId = mixedToteId, ItemId = heavyItemId, LocationId = dummyLocId, QuantityRequested = 1m, QuantityAllocated = 1m, QuantityPicked = 1m, Status = AllocationStatus.Picked },
                new InventoryAllocation { Id = Guid.NewGuid(), ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = mixedOrderId, OutboundOrderLineId = lightLineId, StockOnHandId = lightStockId, TargetHandlingUnitId = mixedToteId, ItemId = lightItemId, LocationId = dummyLocId, QuantityRequested = 1m, QuantityAllocated = 1m, QuantityPicked = 1m, Status = AllocationStatus.Picked });

            await db.SaveChangesAsync();
        }

        // Start session & create BoxMediumId (MaxWeightKg = 20kg, TareWeightKg = 0.45kg)
        Guid sessionId;
        using (var startResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/sessions/start", PackOnlyStaffId, new
        {
            facilityId = FacilityA,
            packingStationLocationId = StationAId,
            outboundOrderId = mixedOrderId
        })))
        {
            Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
            sessionId = JsonSerializer.Deserialize<JsonElement>(await startResp.Content.ReadAsStringAsync()).GetProperty("sessionId").GetGuid();
        }

        Guid packageId;
        using (var pkgResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/packages/create", PackOnlyStaffId, new
        {
            packingSessionId = sessionId,
            packagingMaterialId = BoxMediumId
        })))
        {
            Assert.Equal(HttpStatusCode.OK, pkgResp.StatusCode);
            packageId = JsonSerializer.Deserialize<JsonElement>(await pkgResp.Content.ReadAsStringAsync()).GetProperty("packageId").GetGuid();
        }

        // Pack heavy SKU (18 kg <= 20 kg) -> 200 OK
        using (var packHeavy = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/items/pack", PackOnlyStaffId, new
        {
            packingSessionId = sessionId,
            packageId,
            scannedToteLpn = "TOTE-MIXED-WT",
            scannedItemBarcode = "SKU-HEAVY-18",
            quantity = 1m,
            idempotencyKey = "pack-heavy-1"
        })))
        {
            Assert.Equal(HttpStatusCode.OK, packHeavy.StatusCode);
        }

        // Attempt light SKU (3 kg -> actual cumulative 18 + 3 = 21 kg > 20 kg) -> MUST fail 400 BadRequest!
        using (var packLightOverweight = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/items/pack", PackOnlyStaffId, new
        {
            packingSessionId = sessionId,
            packageId,
            scannedToteLpn = "TOTE-MIXED-WT",
            scannedItemBarcode = "SKU-LIGHT-03",
            quantity = 1m,
            idempotencyKey = "pack-light-overweight"
        })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, packLightOverweight.StatusCode);
        }

        // Verify SKU-LIGHT-03 remained unmoved in tote
        using (var verifyScope = _app.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var lightStock = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.Id == lightStockId);
            Assert.Equal(1m, lightStock.QtyAllocated);
        }

        // Overweight scale close (21.50 kg gross - 0.45 kg tare = 21.05 kg net > 20 kg MaxWeightKg) -> MUST fail 400 BadRequest!
        using (var overweightClose = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/packing/packages/{packageId}/close", PackOnlyStaffId, new
        {
            scaleGrossWeightKg = 21.50m
        })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, overweightClose.StatusCode);
        }

        // Valid scale close (18.45 kg gross - 0.45 kg tare = 18.00 kg net <= 20 kg) -> 200 OK
        using (var validClose = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/packing/packages/{packageId}/close", PackOnlyStaffId, new
        {
            scaleGrossWeightKg = 18.45m
        })))
        {
            Assert.Equal(HttpStatusCode.OK, validClose.StatusCode);
        }
    }

    [Fact]
    public async Task PackItemIdempotency_EnforcesPayloadFingerprintConflict409_AndConcurrentSameKeyDeduplication()
    {
        var concOrderId = Guid.NewGuid();
        var concLineId = Guid.NewGuid();
        var concToteId = Guid.NewGuid();
        var concStockId = Guid.NewGuid();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ord = new OutboundOrder
            {
                Id = concOrderId,
                ClientId = ClientA,
                FacilityId = FacilityA,
                OrderNumber = "SO-743-IDEMP-CONC",
                CustomerName = "Idempotency Buyer",
                Status = OutboundOrderStatus.InProgress
            };
            ord.Lines.Add(new OutboundOrderLine { Id = concLineId, OutboundOrderId = concOrderId, ItemId = ItemAId, LineNumber = 1, QuantityOrdered = 10m, QuantityAllocated = 10m, Status = OutboundOrderLineStatus.Allocated });
            db.OutboundOrders.Add(ord);

            db.HandlingUnits.Add(new HandlingUnit { Id = concToteId, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "TOTE-IDEMP-CONC", UnitType = HandlingUnitType.Tote, Status = HandlingUnitStatus.InUse, CurrentLocationId = StationAId, CurrentOutboundOrderId = concOrderId });
            db.StockOnHand.Add(new StockOnHand { Id = concStockId, ClientId = ClientA, FacilityId = FacilityA, ItemId = ItemAId, LocationId = StationAId, HandlingUnitId = concToteId, LpnCode = "TOTE-IDEMP-CONC", QtyAvailable = 0m, QtyAllocated = 10m, Version = 1 });

            var locId = db.Locations.IgnoreQueryFilters().First(l => l.FacilityId == FacilityA).Id;
            db.InventoryAllocations.Add(new InventoryAllocation { Id = Guid.NewGuid(), ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = concOrderId, OutboundOrderLineId = concLineId, StockOnHandId = concStockId, TargetHandlingUnitId = concToteId, ItemId = ItemAId, LocationId = locId, QuantityRequested = 10m, QuantityAllocated = 10m, QuantityPicked = 10m, Status = AllocationStatus.Picked });
            await db.SaveChangesAsync();
        }

        Guid sessionId;
        using (var startResp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/sessions/start", PackOnlyStaffId, new
        {
            facilityId = FacilityA,
            packingStationLocationId = StationAId,
            outboundOrderId = concOrderId
        })))
        {
            Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
            sessionId = JsonSerializer.Deserialize<JsonElement>(await startResp.Content.ReadAsStringAsync()).GetProperty("sessionId").GetGuid();
        }

        Guid pkg1Id;
        Guid pkg2Id;
        using (var r1 = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/packages/create", PackOnlyStaffId, new { packingSessionId = sessionId, packagingMaterialId = BoxMediumId })))
        using (var r2 = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/packages/create", PackOnlyStaffId, new { packingSessionId = sessionId, packagingMaterialId = BoxMediumId })))
        {
            pkg1Id = JsonSerializer.Deserialize<JsonElement>(await r1.Content.ReadAsStringAsync()).GetProperty("packageId").GetGuid();
            pkg2Id = JsonSerializer.Deserialize<JsonElement>(await r2.Content.ReadAsStringAsync()).GetProperty("packageId").GetGuid();
        }

        // Concurrent duplicate request with identical IdempotencyKey & payload -> both return 200, stock deducted ONCE (10 -> 7)
        const string sharedKey = "pack-conc-key-743";
        var payload = new
        {
            packingSessionId = sessionId,
            packageId = pkg1Id,
            scannedToteLpn = "TOTE-IDEMP-CONC",
            scannedItemBarcode = "SKU-PACK-743",
            quantity = 3m,
            idempotencyKey = sharedKey
        };

        var concurrentResps = await Task.WhenAll(
            _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/items/pack", PackOnlyStaffId, payload)),
            _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/items/pack", PackOnlyStaffId, payload)));
        foreach (var r in concurrentResps)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            r.Dispose();
        }

        // Reusing same IdempotencyKey with different quantity, item, tote, OR packageId MUST fail with 409 Conflict!
        var conflictPayloads = new object[]
        {
            new { packingSessionId = sessionId, packageId = pkg1Id, scannedToteLpn = "TOTE-IDEMP-CONC", scannedItemBarcode = "SKU-PACK-743", quantity = 4m, idempotencyKey = sharedKey },
            new { packingSessionId = sessionId, packageId = pkg1Id, scannedToteLpn = "TOTE-IDEMP-CONC", scannedItemBarcode = "SKU-OTHER", quantity = 3m, idempotencyKey = sharedKey },
            new { packingSessionId = sessionId, packageId = pkg1Id, scannedToteLpn = "TOTE-743-01", scannedItemBarcode = "SKU-PACK-743", quantity = 3m, idempotencyKey = sharedKey },
            new { packingSessionId = sessionId, packageId = pkg2Id, scannedToteLpn = "TOTE-IDEMP-CONC", scannedItemBarcode = "SKU-PACK-743", quantity = 3m, idempotencyKey = sharedKey }
        };

        foreach (var bad in conflictPayloads)
        {
            using var resp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/items/pack", PackOnlyStaffId, bad));
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        }

        using (var verifyScope = _app.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var toteStk = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.Id == concStockId);
            var packTxs = await db.InventoryTransactions.IgnoreQueryFilters().Where(t => t.ReferenceId == pkg1Id && t.TxType == InventoryTxType.Pack).ToListAsync();
            Assert.Equal(7m, toteStk.QtyAllocated);
            Assert.Single(packTxs);
            Assert.Equal(3m, packTxs[0].Quantity);
        }
    }

    [Fact]
    public async Task ConcurrentCrossPackagePack_WithSameIdempotencyKey_OnlyOneCommitsAndOtherReturns409Conflict()
    {
        var itemId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var tote1Id = Guid.NewGuid();
        var tote2Id = Guid.NewGuid();
        var stock1Id = Guid.NewGuid();
        var stock2Id = Guid.NewGuid();
        var binId = Guid.NewGuid();
        const string skuBarcode = "BC-743-CROSS-CONC";

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Items.Add(new Item
            {
                Id = itemId,
                ClientId = ClientA,
                Sku = "SKU-743-CROSS-CONC",
                Description = "Cross-package concurrency item",
                WeightKg = 1.0m,
                LengthCm = 10m,
                WidthCm = 10m,
                HeightCm = 10m,
                VolumeCm3 = 1000m,
                Status = ItemStatus.Active
            });
            db.ItemBarcodes.Add(new ItemBarcode
            {
                Id = Guid.NewGuid(),
                ItemId = itemId,
                BarcodeValue = skuBarcode,
                IsPrimary = true
            });
            var ord = new OutboundOrder
            {
                Id = orderId,
                ClientId = ClientA,
                FacilityId = FacilityA,
                OrderNumber = "SO-743-CROSS-CONC",
                CustomerName = "Cross Package Corp",
                Status = OutboundOrderStatus.Allocated
            };
            ord.Lines.Add(new OutboundOrderLine
            {
                Id = lineId,
                OutboundOrderId = orderId,
                ItemId = itemId,
                LineNumber = 1,
                QuantityOrdered = 10m,
                QuantityAllocated = 10m,
                Status = OutboundOrderLineStatus.Allocated
            });
            db.OutboundOrders.Add(ord);
            db.HandlingUnits.AddRange(
                new HandlingUnit { Id = tote1Id, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "TOTE-743-C1", UnitType = HandlingUnitType.Tote, Status = HandlingUnitStatus.InUse, CurrentLocationId = StationAId, CurrentOutboundOrderId = orderId },
                new HandlingUnit { Id = tote2Id, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "TOTE-743-C2", UnitType = HandlingUnitType.Tote, Status = HandlingUnitStatus.InUse, CurrentLocationId = StationAId, CurrentOutboundOrderId = orderId });
            db.StockOnHand.AddRange(
                new StockOnHand { Id = stock1Id, ClientId = ClientA, FacilityId = FacilityA, ItemId = itemId, LocationId = StationAId, HandlingUnitId = tote1Id, LpnCode = "TOTE-743-C1", QtyAvailable = 0m, QtyAllocated = 5m, Version = 1 },
                new StockOnHand { Id = stock2Id, ClientId = ClientA, FacilityId = FacilityA, ItemId = itemId, LocationId = StationAId, HandlingUnitId = tote2Id, LpnCode = "TOTE-743-C2", QtyAvailable = 0m, QtyAllocated = 5m, Version = 1 });
            db.InventoryAllocations.AddRange(
                new InventoryAllocation { Id = Guid.NewGuid(), ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = orderId, OutboundOrderLineId = lineId, StockOnHandId = stock1Id, TargetHandlingUnitId = tote1Id, ItemId = itemId, LocationId = binId, QuantityRequested = 5m, QuantityAllocated = 5m, QuantityPicked = 5m, Status = AllocationStatus.Picked },
                new InventoryAllocation { Id = Guid.NewGuid(), ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = orderId, OutboundOrderLineId = lineId, StockOnHandId = stock2Id, TargetHandlingUnitId = tote2Id, ItemId = itemId, LocationId = binId, QuantityRequested = 5m, QuantityAllocated = 5m, QuantityPicked = 5m, Status = AllocationStatus.Picked });
            await db.SaveChangesAsync();
        }

        Guid sessionId;
        using (var sr = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/sessions/start", PackOnlyStaffId,
            new { facilityId = FacilityA, packingStationLocationId = StationAId, outboundOrderId = orderId })))
        {
            Assert.Equal(HttpStatusCode.OK, sr.StatusCode);
            sessionId = JsonDocument.Parse(await sr.Content.ReadAsStringAsync()).RootElement.GetProperty("sessionId").GetGuid();
        }

        Guid pkg1Id;
        using (var p1 = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/packages/create", PackOnlyStaffId,
            new { packingSessionId = sessionId, packagingMaterialId = BoxMediumId })))
        {
            Assert.Equal(HttpStatusCode.OK, p1.StatusCode);
            pkg1Id = JsonDocument.Parse(await p1.Content.ReadAsStringAsync()).RootElement.GetProperty("packageId").GetGuid();
        }

        Guid pkg2Id;
        using (var p2 = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/packages/create", PackOnlyStaffId,
            new { packingSessionId = sessionId, packagingMaterialId = BoxMediumId })))
        {
            Assert.Equal(HttpStatusCode.OK, p2.StatusCode);
            pkg2Id = JsonDocument.Parse(await p2.Content.ReadAsStringAsync()).RootElement.GetProperty("packageId").GetGuid();
        }

        const string sharedIdempKey = "p743-cross-pkg-conc-key";
        var responses = await Task.WhenAll(
            _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/items/pack", PackOnlyStaffId,
                new { packingSessionId = sessionId, packageId = pkg1Id, scannedToteLpn = "TOTE-743-C1", scannedItemBarcode = skuBarcode, quantity = 2m, idempotencyKey = sharedIdempKey })),
            _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/packing/items/pack", PackOnlyStaffId,
                new { packingSessionId = sessionId, packageId = pkg2Id, scannedToteLpn = "TOTE-743-C2", scannedItemBarcode = skuBarcode, quantity = 2m, idempotencyKey = sharedIdempKey })));

        var statuses = responses.Select(r => r.StatusCode).ToArray();
        foreach (var r in responses) r.Dispose();

        Assert.Single(statuses, s => s == HttpStatusCode.OK);
        Assert.Single(statuses, s => s == HttpStatusCode.Conflict);

        using (var verifyScope = _app.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stk1 = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.Id == stock1Id);
            var stk2 = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.Id == stock2Id);
            var packTxs = await db.InventoryTransactions.IgnoreQueryFilters()
                .Where(t => (t.ReferenceId == pkg1Id || t.ReferenceId == pkg2Id) && t.TxType == InventoryTxType.Pack)
                .ToListAsync();
            var pkgLines = await db.PackageLines.IgnoreQueryFilters()
                .Where(l => l.PackageId == pkg1Id || l.PackageId == pkg2Id)
                .ToListAsync();

            Assert.Equal(8m, stk1.QtyAllocated + stk2.QtyAllocated);
            Assert.Single(packTxs);
            Assert.Equal(2m, packTxs[0].Quantity);
            Assert.Single(pkgLines);
            Assert.Equal(2m, pkgLines[0].Quantity);
            Assert.Equal(packTxs[0].ReferenceId, pkgLines[0].PackageId);
        }
    }
}
