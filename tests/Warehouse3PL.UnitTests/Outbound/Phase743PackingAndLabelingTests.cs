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
            new Facility { Id = FacilityA, FacilityCode = "FAC-743-A", Name = "Chicago Packing Hub", CountryCode = "USA", IsActive = true },
            new Facility { Id = FacilityB, FacilityCode = "FAC-743-B", Name = "Dallas Packing Hub", CountryCode = "USA", IsActive = true });

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
}
