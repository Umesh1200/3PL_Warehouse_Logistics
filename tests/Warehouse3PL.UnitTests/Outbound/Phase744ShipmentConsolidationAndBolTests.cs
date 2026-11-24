using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
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

public sealed class Phase744ShipmentConsolidationAndBolTests : IAsyncLifetime
{
    private const string TestJwtSecret = "Nexus3PL-Phase744-Ship-BOL-Custody-Key-2026!";
    private const string TestIssuer = "nexus3pl-744-auth";
    private const string TestAudience = "nexus3pl-744-api";

    private static readonly Guid ClientA = Guid.Parse("744a0001-0000-0000-0000-000000000001");
    private static readonly Guid ClientB = Guid.Parse("744a0001-0000-0000-0000-000000000002");
    private static readonly Guid FacilityA = Guid.Parse("744a0002-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("744a0002-0000-0000-0000-000000000002");

    private static readonly Guid ShipStaffAId = Guid.Parse("744a0003-0000-0000-0000-000000000001");
    private static readonly Guid ShipStaffBId = Guid.Parse("744a0003-0000-0000-0000-000000000002");
    private static readonly Guid PackOnlyStaffId = Guid.Parse("744a0003-0000-0000-0000-000000000003");
    private static readonly Guid ClientAdminId = Guid.Parse("744a0003-0000-0000-0000-000000000004");
    private static readonly Guid DriverAId = Guid.Parse("744a0003-0000-0000-0000-000000000005");
    private static readonly Guid DriverBId = Guid.Parse("744a0003-0000-0000-0000-000000000006");

    private static readonly Guid StagingLaneAId = Guid.Parse("744a0004-0000-0000-0000-000000000001");
    private static readonly Guid DockDoorAId = Guid.Parse("744a0004-0000-0000-0000-000000000002");
    private static readonly Guid StorageBinAId = Guid.Parse("744a0004-0000-0000-0000-000000000003");
    private static readonly Guid StagingLaneBId = Guid.Parse("744a0004-0000-0000-0000-000000000004");

    private static readonly Guid Order1Id = Guid.Parse("744a0005-0000-0000-0000-000000000001");
    private static readonly Guid Order2Id = Guid.Parse("744a0005-0000-0000-0000-000000000002");
    private static readonly Guid OrderFacBId = Guid.Parse("744a0005-0000-0000-0000-000000000003");

    private static readonly Guid Pkg1FedExId = Guid.Parse("744a0006-0000-0000-0000-000000000001");
    private static readonly Guid Pkg2FedExId = Guid.Parse("744a0006-0000-0000-0000-000000000002");
    private static readonly Guid Pkg3NoLabelId = Guid.Parse("744a0006-0000-0000-0000-000000000003");
    private static readonly Guid Pkg4UpsId = Guid.Parse("744a0006-0000-0000-0000-000000000004");
    private static readonly Guid PkgFacBId = Guid.Parse("744a0006-0000-0000-0000-000000000005");
    private static readonly Guid ItemAId = Guid.Parse("744a0007-0000-0000-0000-000000000001");
    private static readonly Guid Stock1Id = Guid.Parse("744a0008-0000-0000-0000-000000000001");

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
            .AddApplicationPart(typeof(ShipmentsController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();

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

        builder.Services.AddScoped<IPackingExecutionService, PackingExecutionService>();
        builder.Services.AddScoped<IShippingCarrierService, SimulatedCarrierService>();
        builder.Services.AddScoped<IShippingLabelService, ShippingLabelService>();
        builder.Services.AddScoped<IShipmentConsolidationService, ShipmentConsolidationService>();

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
        await SeedAsync(db);
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Clients.AddRange(
            new Client { Id = ClientA, CompanyName = "Client Alpha 744", AccountCode = "ALP-744", Status = ClientStatus.Active },
            new Client { Id = ClientB, CompanyName = "Client Beta Unauthorized", AccountCode = "BET-744", Status = ClientStatus.Active });

        db.Facilities.AddRange(
            new Facility { Id = FacilityA, FacilityCode = "FAC-744-A", Name = "Hub Alpha 744", AddressLine1 = "100 Freight Way", City = "Chicago", CountryCode = "US", IsActive = true },
            new Facility { Id = FacilityB, FacilityCode = "FAC-744-B", Name = "Hub Beta 744", AddressLine1 = "200 Dock Ave", City = "Dallas", CountryCode = "US", IsActive = true });

        db.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityA, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityB, Status = FacilityAccessStatus.Active });

        db.Locations.AddRange(
            new Location { Id = StagingLaneAId, FacilityId = FacilityA, LocationCode = "STG-OUT-01", Barcode = "STG-OUT-01", LocationType = LocationType.StagingLane, IsActive = true },
            new Location { Id = DockDoorAId, FacilityId = FacilityA, LocationCode = "DOCK-OUT-02", Barcode = "DOCK-OUT-02", LocationType = LocationType.DockDoor, IsActive = true },
            new Location { Id = StorageBinAId, FacilityId = FacilityA, LocationCode = "BIN-A-01", Barcode = "BIN-A-01", LocationType = LocationType.Bin, IsActive = true },
            new Location { Id = StagingLaneBId, FacilityId = FacilityB, LocationCode = "STG-B-01", Barcode = "STG-B-01", LocationType = LocationType.StagingLane, IsActive = true });

        var roleShip = new Role { RoleName = "ShipOperator744", IsSystemRole = false };
        roleShip.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Ship, AccessLevel = AccessLevel.Write });

        var rolePack = new Role { RoleName = "PackOnly744", IsSystemRole = false };
        rolePack.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Pack, AccessLevel = AccessLevel.Write });

        var roleClient = new Role { RoleName = SystemRoles.ClientAdmin, IsSystemRole = true };
        roleClient.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Ship, AccessLevel = AccessLevel.Write });
        roleClient.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.OrdersView, AccessLevel = AccessLevel.Read });

        var roleDriver = new Role { RoleName = SystemRoles.Driver, IsSystemRole = true };
        roleDriver.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Dispatch.ExecuteDelivery, AccessLevel = AccessLevel.Execute });

        db.Roles.AddRange(roleShip, rolePack, roleClient, roleDriver);

        db.Users.AddRange(
            new User { Id = ShipStaffAId, Email = "shipa@744.test", FirstName = "Sam", LastName = "ShipA", UserType = UserType.Staff, AssignedFacilityId = FacilityA, Status = UserStatus.Active, UserRoles = [new UserRole { Role = roleShip }] },
            new User { Id = ShipStaffBId, Email = "shipb@744.test", FirstName = "Sue", LastName = "ShipB", UserType = UserType.Staff, AssignedFacilityId = FacilityB, Status = UserStatus.Active, UserRoles = [new UserRole { Role = roleShip }] },
            new User { Id = PackOnlyStaffId, Email = "packonly@744.test", FirstName = "Pat", LastName = "Pack", UserType = UserType.Staff, AssignedFacilityId = FacilityA, Status = UserStatus.Active, UserRoles = [new UserRole { Role = rolePack }] },
            new User { Id = ClientAdminId, Email = "client@744.test", FirstName = "Carl", LastName = "Client", UserType = UserType.Client, ClientId = ClientA, Status = UserStatus.Active, UserRoles = [new UserRole { Role = roleClient }] },
            new User { Id = DriverAId, Email = "drivera@744.test", FirstName = "Dan", LastName = "DriverA", UserType = UserType.Driver, AssignedFacilityId = FacilityA, Status = UserStatus.Active, UserRoles = [new UserRole { Role = roleDriver }] },
            new User { Id = DriverBId, Email = "driverb@744.test", FirstName = "Dave", LastName = "DriverB", UserType = UserType.Driver, AssignedFacilityId = FacilityB, Status = UserStatus.Active, UserRoles = [new UserRole { Role = roleDriver }] });

        db.Items.Add(new Item { Id = ItemAId, ClientId = ClientA, Sku = "SKU-744-A", Description = "Consolidation Unit", WeightKg = 2.0m, LengthCm = 20m, WidthCm = 15m, HeightCm = 10m, VolumeCm3 = 3000m, Status = ItemStatus.Active });
        db.StockOnHand.Add(new StockOnHand { Id = Stock1Id, ClientId = ClientA, FacilityId = FacilityA, ItemId = ItemAId, LocationId = StorageBinAId, QtyAvailable = 50m, QtyAllocated = 0m, Version = 1 });
        db.InventoryTransactions.Add(new InventoryTransaction { ClientId = ClientA, FacilityId = FacilityA, ItemId = ItemAId, FromLocationId = StorageBinAId, TxType = InventoryTxType.Pack, Quantity = 4m, Notes = "Pre-consolidation pack baseline", UserId = PackOnlyStaffId, TxTimestamp = DateTime.UtcNow });

        var ord1 = new OutboundOrder
        {
            Id = Order1Id,
            ClientId = ClientA,
            FacilityId = FacilityA,
            OrderNumber = "SO-744-001",
            CustomerName = "Apex Logistics Consignee",
            Status = OutboundOrderStatus.InProgress,
            ShippingAddress = new ShippingAddress { OutboundOrderId = Order1Id, RecipientName = "Apex Receiving Dock", AddressLine1 = "500 Commerce Blvd", City = "Columbus", StateOrProvince = "OH", PostalCode = "43215", CountryCode = "US" }
        };
        var ord2 = new OutboundOrder { Id = Order2Id, ClientId = ClientA, FacilityId = FacilityA, OrderNumber = "SO-744-002", CustomerName = "Beta Receiver", Status = OutboundOrderStatus.InProgress };
        var ordB = new OutboundOrder { Id = OrderFacBId, ClientId = ClientA, FacilityId = FacilityB, OrderNumber = "SO-744-FACB", CustomerName = "Dallas Receiver", Status = OutboundOrderStatus.InProgress };
        db.OutboundOrders.AddRange(ord1, ord2, ordB);

        var hu1 = Guid.NewGuid();
        var hu2 = Guid.NewGuid();
        var hu3 = Guid.NewGuid();
        var hu4 = Guid.NewGuid();
        var huB = Guid.NewGuid();
        db.HandlingUnits.AddRange(
            new HandlingUnit { Id = hu1, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-744-01", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
            new HandlingUnit { Id = hu2, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-744-02", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
            new HandlingUnit { Id = hu3, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-744-03", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
            new HandlingUnit { Id = hu4, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-744-04", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
            new HandlingUnit { Id = huB, ClientId = ClientA, FacilityId = FacilityB, LpnCode = "CTN-744-B1", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse });

        db.Packages.AddRange(
            new Package { Id = Pkg1FedExId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = Order1Id, HandlingUnitId = hu1, PackageNumber = "PKG-744-001", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-744-001", GrossWeightKg = 5.5m, TareWeightKg = 0.5m, NetWeightKg = 5.0m, LengthCm = 40m, WidthCm = 30m, HeightCm = 20m, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FEDEX", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "TRK-744-001", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] },
            new Package { Id = Pkg2FedExId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = Order1Id, HandlingUnitId = hu2, PackageNumber = "PKG-744-002", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-744-002", GrossWeightKg = 4.5m, TareWeightKg = 0.5m, NetWeightKg = 4.0m, LengthCm = 30m, WidthCm = 20m, HeightCm = 20m, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FEDEX", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "TRK-744-002", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] },
            new Package { Id = Pkg3NoLabelId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = Order2Id, HandlingUnitId = hu3, PackageNumber = "PKG-744-003", Status = PackageStatus.Packed, CarrierCode = null, TrackingNumber = null, GrossWeightKg = 3.0m, TareWeightKg = 0.4m, NetWeightKg = 2.6m, LengthCm = 30m, WidthCm = 20m, HeightCm = 15m },
            new Package { Id = Pkg4UpsId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = Order2Id, HandlingUnitId = hu4, PackageNumber = "PKG-744-004", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-UPS", TrackingNumber = "TRK-744-UPS", GrossWeightKg = 6.0m, TareWeightKg = 0.5m, NetWeightKg = 5.5m, LengthCm = 40m, WidthCm = 30m, HeightCm = 25m, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-UPS", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "TRK-744-UPS", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] },
            new Package { Id = PkgFacBId, ClientId = ClientA, FacilityId = FacilityB, OutboundOrderId = OrderFacBId, HandlingUnitId = huB, PackageNumber = "PKG-744-FACB", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-744-FACB", GrossWeightKg = 2.5m, TareWeightKg = 0.5m, NetWeightKg = 2.0m, LengthCm = 20m, WidthCm = 20m, HeightCm = 20m, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FEDEX", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "TRK-744-FACB", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_app != null) await _app.DisposeAsync();
    }

    private string CreateJwt(Guid userId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tokenSvc = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions).Single(u => u.Id == userId);

        var roles = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
        var permissions = user.UserRoles.SelectMany(ur => ur.Role.RolePermissions).Select(rp => rp.PermissionKey).Distinct().ToList();
        return tokenSvc.GenerateTokens(user, roles, permissions).AccessToken;
    }

    private HttpRequestMessage JsonReq(HttpMethod method, string uri, Guid userId, object? body = null)
    {
        var req = new HttpRequestMessage(method, uri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(userId));
        if (body != null)
        {
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        return req;
    }

    [Fact]
    public async Task ReferenceEndpoints_And_CreateShipment_EnforceRbacAndClientFacilityPairing()
    {
        // ClientAdmin (even carrying outbound:ship claim), Driver, and PackOnly Staff are denied 403
        foreach (var deniedUser in new[] { ClientAdminId, DriverAId, PackOnlyStaffId })
        {
            foreach (var path in new[]
            {
                $"/api/v1/outbound/shipments?facilityId={FacilityA}",
                $"/api/v1/outbound/shipments/eligible-packages?facilityId={FacilityA}",
                $"/api/v1/outbound/shipments/staging-locations?facilityId={FacilityA}",
                $"/api/v1/outbound/shipments/eligible-drivers?facilityId={FacilityA}",
                $"/api/v1/outbound/shipments/reference-clients?facilityId={FacilityA}"
            })
            {
                using var resp = await _http.SendAsync(JsonReq(HttpMethod.Get, path, deniedUser));
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            }
        }

        // ShipStaffA receives facility-scoped staging lanes, drivers, and active clients
        using (var rStg = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/shipments/staging-locations?facilityId={FacilityA}", ShipStaffAId)))
        {
            Assert.Equal(HttpStatusCode.OK, rStg.StatusCode);
            var arr = JsonDocument.Parse(await rStg.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(2, arr.GetArrayLength());
        }

        using (var rDrv = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/shipments/eligible-drivers?facilityId={FacilityA}", ShipStaffAId)))
        {
            Assert.Equal(HttpStatusCode.OK, rDrv.StatusCode);
            var arr = JsonDocument.Parse(await rDrv.Content.ReadAsStringAsync()).RootElement;
            var ids = arr.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
            Assert.Contains(DriverAId, ids);
            Assert.DoesNotContain(DriverBId, ids);
        }

        // Staff creating shipment for client without active ClientFacilityAccess in FacilityA -> 403 Forbidden
        using (var rUnauthClient = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipments", ShipStaffAId,
            new { clientId = ClientB, facilityId = FacilityA, carrierCode = "SIM-FEDEX", serviceLevel = "StandardGround" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, rUnauthClient.StatusCode);
        }

        // Staff from FacilityA creating shipment in FacilityB -> 403 Forbidden
        using (var rCrossFac = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipments", ShipStaffAId,
            new { clientId = ClientA, facilityId = FacilityB, carrierCode = "SIM-FEDEX", serviceLevel = "StandardGround" })))
        {
            Assert.Equal(HttpStatusCode.Forbidden, rCrossFac.StatusCode);
        }

        // Invalid staging location type (StorageBin) -> 400 BadRequest
        using (var rBadLoc = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipments", ShipStaffAId,
            new { clientId = ClientA, facilityId = FacilityA, carrierCode = "SIM-FEDEX", serviceLevel = "StandardGround", stagingLocationId = StorageBinAId })))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rBadLoc.StatusCode);
        }
    }

    [Fact]
    public async Task FullConsolidationLifecycle_DraftToReadyToDispatched_ImmutableBol_OrderCompletion_AndZeroStockMutation()
    {
        // Snapshot stock and ledger before Phase 7.4.4 workflow
        decimal initialAvail;
        decimal initialAlloc;
        int initialTxCount;
        using (var preScope = _app.Services.CreateScope())
        {
            var db = preScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stk = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.Id == Stock1Id);
            initialAvail = stk.QtyAvailable;
            initialAlloc = stk.QtyAllocated;
            initialTxCount = await db.InventoryTransactions.IgnoreQueryFilters().CountAsync();
        }

        // 1. Eligible packages query shows Facility A eligible cartons only
        using (var rElig = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/shipments/eligible-packages?facilityId={FacilityA}", ShipStaffAId)))
        {
            Assert.Equal(HttpStatusCode.OK, rElig.StatusCode);
            var items = JsonDocument.Parse(await rElig.Content.ReadAsStringAsync()).RootElement.GetProperty("items");
            var pkgIds = items.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
            Assert.Contains(Pkg1FedExId, pkgIds);
            Assert.Contains(Pkg2FedExId, pkgIds);
            Assert.DoesNotContain(PkgFacBId, pkgIds);
        }

        // 2. Create Draft shipment
        Guid shipmentId;
        using (var rCreate = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipments", ShipStaffAId,
            new { clientId = ClientA, facilityId = FacilityA, carrierCode = "SIM-FEDEX", serviceLevel = "StandardGround", stagingLocationId = StagingLaneAId })))
        {
            Assert.Equal(HttpStatusCode.OK, rCreate.StatusCode);
            var root = JsonDocument.Parse(await rCreate.Content.ReadAsStringAsync()).RootElement;
            shipmentId = root.GetProperty("shipmentId").GetGuid();
            Assert.Equal("Draft", root.GetProperty("status").GetString());
        }

        // Negative checks: missing active label (400), carrier mismatch (400), cross-facility read/mutate (403)
        using (var rNoLbl = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/packages", ShipStaffAId, new { packageId = Pkg3NoLabelId })))
            Assert.Equal(HttpStatusCode.BadRequest, rNoLbl.StatusCode);
        using (var rWrongCarrier = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/packages", ShipStaffAId, new { packageId = Pkg4UpsId })))
            Assert.Equal(HttpStatusCode.BadRequest, rWrongCarrier.StatusCode);
        using (var rCrossGet = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/shipments/{shipmentId}", ShipStaffBId)))
            Assert.Equal(HttpStatusCode.Forbidden, rCrossGet.StatusCode);

        // 3. Add Pkg1 & Pkg2, remove Pkg2 with reason, re-add Pkg2
        using (var rAdd1 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/packages", ShipStaffAId, new { packageId = Pkg1FedExId })))
            Assert.Equal(HttpStatusCode.OK, rAdd1.StatusCode);
        using (var rAdd2 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/packages", ShipStaffAId, new { packageId = Pkg2FedExId })))
        {
            Assert.Equal(HttpStatusCode.OK, rAdd2.StatusCode);
            Assert.Equal(2, JsonDocument.Parse(await rAdd2.Content.ReadAsStringAsync()).RootElement.GetProperty("totalPackagesCount").GetInt32());
        }
        using (var rRem2 = await _http.SendAsync(JsonReq(HttpMethod.Delete, $"/api/v1/outbound/shipments/{shipmentId}/packages/{Pkg2FedExId}", ShipStaffAId, new { reason = "Re-weighing carton" })))
        {
            Assert.Equal(HttpStatusCode.OK, rRem2.StatusCode);
            Assert.Equal(1, JsonDocument.Parse(await rRem2.Content.ReadAsStringAsync()).RootElement.GetProperty("totalPackagesCount").GetInt32());
        }
        using (var rReAdd2 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/packages", ShipStaffAId, new { packageId = Pkg2FedExId })))
            Assert.Equal(HttpStatusCode.OK, rReAdd2.StatusCode);

        // 4. Physical staging before load guard: load before stage -> 422; stage in StorageBin -> 400
        using (var rLoadEarly = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{Pkg1FedExId}/load", ShipStaffAId, new { shipmentId })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rLoadEarly.StatusCode);
        using (var rBadStage = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{Pkg1FedExId}/stage", ShipStaffAId, new { stagingLocationId = StorageBinAId })))
            Assert.Equal(HttpStatusCode.BadRequest, rBadStage.StatusCode);

        // Stage both cartons cleanly
        using (var rStg1 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{Pkg1FedExId}/stage", ShipStaffAId, new { stagingLocationId = StagingLaneAId })))
            Assert.Equal(HttpStatusCode.OK, rStg1.StatusCode);
        using (var rStg2 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{Pkg2FedExId}/stage", ShipStaffAId, new { stagingLocationId = DockDoorAId })))
            Assert.Equal(HttpStatusCode.OK, rStg2.StatusCode);

        // 5. Load Pkg1 (idempotent repeat + stays Draft while Pkg2 still Staged), then load Pkg2 -> ReadyForDispatch
        using (var rWrongShip = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{Pkg1FedExId}/load", ShipStaffAId, new { shipmentId = Guid.NewGuid() })))
            Assert.Equal(HttpStatusCode.BadRequest, rWrongShip.StatusCode);

        using (var rLoad1 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{Pkg1FedExId}/load", ShipStaffAId, new { shipmentId, trailerNumber = "TRL-744-A" })))
        {
            Assert.Equal(HttpStatusCode.OK, rLoad1.StatusCode);
            Assert.Equal("Draft", JsonDocument.Parse(await rLoad1.Content.ReadAsStringAsync()).RootElement.GetProperty("shipmentStatus").GetString());
        }
        using (var rLoad1Dup = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{Pkg1FedExId}/load", ShipStaffAId, new { shipmentId, trailerNumber = "TRL-744-A" })))
            Assert.Equal(HttpStatusCode.OK, rLoad1Dup.StatusCode);

        // Attempt dispatch while Pkg2 is not loaded -> 422 UnprocessableEntity
        using (var rDispEarly = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/dispatch", ShipStaffAId,
            new { driverName = "Dan DriverA", driverSignature = "SIG-DAN", assignedDriverUserId = DriverAId })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rDispEarly.StatusCode);

        using (var rLoad2 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{Pkg2FedExId}/load", ShipStaffAId, new { shipmentId, trailerNumber = "TRL-744-A", dockLocationId = DockDoorAId })))
        {
            Assert.Equal(HttpStatusCode.OK, rLoad2.StatusCode);
            Assert.Equal("ReadyForDispatch", JsonDocument.Parse(await rLoad2.Content.ReadAsStringAsync()).RootElement.GetProperty("shipmentStatus").GetString());
        }

        // 6. Immutable BOL snapshot issuance & preserved snapshot re-read
        Guid bolId;
        string bolSnapshotJson;
        using (var rBol1 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/bol", ShipStaffAId,
            new { trailerNumber = "TRL-744-A", sealNumber = "SEAL-744-1", specialInstructions = "Keep upright" })))
        {
            Assert.Equal(HttpStatusCode.OK, rBol1.StatusCode);
            var root = JsonDocument.Parse(await rBol1.Content.ReadAsStringAsync()).RootElement;
            bolId = root.GetProperty("bolId").GetGuid();
            bolSnapshotJson = root.GetProperty("documentSnapshotJson").GetString()!;
            Assert.Equal(2, root.GetProperty("totalPackageCount").GetInt32());
        }

        // Repeat POST /bol with modified inputs MUST return preserved snapshot
        using (var rBol2 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/bol", ShipStaffAId,
            new { trailerNumber = "TRL-SPOOF", sealNumber = "SEAL-SPOOF", specialInstructions = "Altered" })))
        {
            Assert.Equal(HttpStatusCode.OK, rBol2.StatusCode);
            var root = JsonDocument.Parse(await rBol2.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(bolId, root.GetProperty("bolId").GetGuid());
            Assert.Equal(bolSnapshotJson, root.GetProperty("documentSnapshotJson").GetString());
        }

        // GET /shipments/{id} exposes BillOfLadingId and GET /shipments/bol/{bolId} matches snapshot
        using (var rGetShp = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/shipments/{shipmentId}", ShipStaffAId)))
        {
            Assert.Equal(HttpStatusCode.OK, rGetShp.StatusCode);
            var root = JsonDocument.Parse(await rGetShp.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(bolId, root.GetProperty("billOfLadingId").GetGuid());
            Assert.Equal("ReadyForDispatch", root.GetProperty("status").GetString());
        }

        using (var rGetBol = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/shipments/bol/{bolId}", ShipStaffAId)))
        {
            Assert.Equal(HttpStatusCode.OK, rGetBol.StatusCode);
            var root = JsonDocument.Parse(await rGetBol.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(bolSnapshotJson, root.GetProperty("documentSnapshotJson").GetString());
        }

        // Cross-facility staff denied on GET /bol/{id} -> 403
        using (var rBolCross = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/shipments/bol/{bolId}", ShipStaffBId)))
            Assert.Equal(HttpStatusCode.Forbidden, rBolCross.StatusCode);

        // 7. Driver custody dispatch handover (Facility B driver rejected 403; DriverA succeeds -> Dispatched + Order Completed)
        using (var rWrongDriver = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/dispatch", ShipStaffAId,
            new { driverName = "Dave DriverB", driverSignature = "SIG-DAVE", assignedDriverUserId = DriverBId })))
            Assert.Equal(HttpStatusCode.Forbidden, rWrongDriver.StatusCode);

        using (var rDispOk = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/dispatch", ShipStaffAId,
            new { driverName = "Dan DriverA", driverLicenseNumber = "DL-744-IL", driverSignature = "SIG-DAN-744", trailerNumber = "TRL-744-A", sealNumber = "SEAL-744-1", notes = "Sealed at dock 2", idempotencyKey = "disp-744-1", assignedDriverUserId = DriverAId })))
        {
            Assert.Equal(HttpStatusCode.OK, rDispOk.StatusCode);
            var root = JsonDocument.Parse(await rDispOk.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("Dispatched", root.GetProperty("status").GetString());
            Assert.Equal(DriverAId, root.GetProperty("assignedDriverUserId").GetGuid());
            var completedOrders = root.GetProperty("completedOrderIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
            Assert.Contains(Order1Id, completedOrders);
        }

        // Idempotent second dispatch call
        using (var rDispIdemp = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/dispatch", ShipStaffAId,
            new { driverName = "Dan DriverA", driverSignature = "SIG-DAN-744", idempotencyKey = "disp-744-1", assignedDriverUserId = DriverAId })))
        {
            Assert.Equal(HttpStatusCode.OK, rDispIdemp.StatusCode);
            Assert.Equal("Dispatched", JsonDocument.Parse(await rDispIdemp.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetString());
        }

        // 8. Verify persistence: Order1 Completed, custody record created, ZERO secondary stock or ledger mutation
        using (var postScope = _app.Services.CreateScope())
        {
            var db = postScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ord1 = await db.OutboundOrders.IgnoreQueryFilters().SingleAsync(o => o.Id == Order1Id);
            var shp = await db.Shipments.IgnoreQueryFilters().SingleAsync(s => s.Id == shipmentId);
            var custodies = await db.ShipmentCustodyTransfers.IgnoreQueryFilters().Where(c => c.ShipmentId == shipmentId).ToListAsync();
            var stk = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.Id == Stock1Id);
            var postTxCount = await db.InventoryTransactions.IgnoreQueryFilters().CountAsync();

            Assert.Equal(OutboundOrderStatus.Completed, ord1.Status);
            Assert.Equal(ShipmentStatus.Dispatched, shp.Status);
            Assert.Single(custodies);
            Assert.Equal(ShipmentCustodyStatus.Confirmed, custodies[0].CustodyStatus);
            Assert.Equal(initialAvail, stk.QtyAvailable);
            Assert.Equal(initialAlloc, stk.QtyAllocated);
            Assert.Equal(initialTxCount, postTxCount);
        }
    }
}
