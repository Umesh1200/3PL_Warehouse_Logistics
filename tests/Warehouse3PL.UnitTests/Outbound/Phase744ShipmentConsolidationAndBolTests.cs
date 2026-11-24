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

        // Idempotent second dispatch call with identical custody payload
        using (var rDispIdemp = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shipmentId}/dispatch", ShipStaffAId,
            new { driverName = "Dan DriverA", driverLicenseNumber = "DL-744-IL", driverSignature = "SIG-DAN-744", trailerNumber = "TRL-744-A", sealNumber = "SEAL-744-1", idempotencyKey = "disp-744-1", assignedDriverUserId = DriverAId })))
        {
            Assert.Equal(HttpStatusCode.OK, rDispIdemp.StatusCode);
            var repRoot = JsonDocument.Parse(await rDispIdemp.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("Dispatched", repRoot.GetProperty("status").GetString());
            Assert.Equal("Dan DriverA", repRoot.GetProperty("driverName").GetString());
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

    [Fact]
    public async Task P744R01_KathmanduFacility_And_MultiConsignee_BolSnapshot_UsesPersistedAddressesWithoutFallback()
    {
        var facKtmId = Guid.NewGuid();
        var facIncompleteId = Guid.NewGuid();
        var ktmStaffId = Guid.NewGuid();
        var incStaffId = Guid.NewGuid();
        var ktmStageId = Guid.NewGuid();
        var incStageId = Guid.NewGuid();
        var ordPatanId = Guid.NewGuid();
        var ordPokharaId = Guid.NewGuid();
        var ordIncId = Guid.NewGuid();
        var pkgPatanId = Guid.NewGuid();
        var pkgPokharaId = Guid.NewGuid();
        var pkgIncId = Guid.NewGuid();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var shipRole = await db.Roles.SingleAsync(r => r.RoleName == "ShipOperator744");

            db.Facilities.AddRange(
                new Facility
                {
                    Id = facKtmId,
                    FacilityCode = "KTM-NP-01",
                    Name = "Kathmandu Valley Logistics Hub",
                    AddressLine1 = "Durbar Marg, Ward 1",
                    AddressLine2 = "Ring Road Cargo Complex",
                    City = "Kathmandu",
                    StateOrProvince = "Bagmati",
                    PostalCode = "44600",
                    CountryCode = "NP",
                    Timezone = "Asia/Kathmandu",
                    IsActive = true
                },
                new Facility
                {
                    Id = facIncompleteId,
                    FacilityCode = "INC-01",
                    Name = "Unconfigured Address Hub",
                    AddressLine1 = null,
                    City = null,
                    CountryCode = "NP",
                    IsActive = true
                });

            db.ClientFacilityAccesses.AddRange(
                new ClientFacilityAccess { ClientId = ClientA, FacilityId = facKtmId, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = ClientA, FacilityId = facIncompleteId, Status = FacilityAccessStatus.Active });

            db.Users.AddRange(
                new User { Id = ktmStaffId, Email = "ktm@744.test", FirstName = "Kiran", LastName = "Ktm", UserType = UserType.Staff, AssignedFacilityId = facKtmId, Status = UserStatus.Active, UserRoles = [new UserRole { Role = shipRole }] },
                new User { Id = incStaffId, Email = "inc@744.test", FirstName = "Ian", LastName = "Inc", UserType = UserType.Staff, AssignedFacilityId = facIncompleteId, Status = UserStatus.Active, UserRoles = [new UserRole { Role = shipRole }] });

            db.Locations.AddRange(
                new Location { Id = ktmStageId, FacilityId = facKtmId, LocationCode = "KTM-STG-01", Barcode = "KTM-STG-01", LocationType = LocationType.StagingLane, IsActive = true },
                new Location { Id = incStageId, FacilityId = facIncompleteId, LocationCode = "INC-STG-01", Barcode = "INC-STG-01", LocationType = LocationType.StagingLane, IsActive = true });

            db.OutboundOrders.AddRange(
                new OutboundOrder
                {
                    Id = ordPatanId, ClientId = ClientA, FacilityId = facKtmId, OrderNumber = "SO-NP-PATAN-1", CustomerName = "Patan Medical Supply", Status = OutboundOrderStatus.InProgress,
                    ShippingAddress = new ShippingAddress { OutboundOrderId = ordPatanId, RecipientName = "Patan Receiving Depot", AddressLine1 = "Jawalakhel Chowk", City = "Lalitpur", StateOrProvince = "Bagmati", PostalCode = "44700", CountryCode = "NP" }
                },
                new OutboundOrder
                {
                    Id = ordPokharaId, ClientId = ClientA, FacilityId = facKtmId, OrderNumber = "SO-NP-PKR-2", CustomerName = "Pokhara Trade Co", Status = OutboundOrderStatus.InProgress,
                    ShippingAddress = new ShippingAddress { OutboundOrderId = ordPokharaId, RecipientName = "Lakeside Freight Dock", AddressLine1 = "Prithvi Chowk", City = "Pokhara", StateOrProvince = "Gandaki", PostalCode = "33700", CountryCode = "NP" }
                },
                new OutboundOrder { Id = ordIncId, ClientId = ClientA, FacilityId = facIncompleteId, OrderNumber = "SO-INC-1", CustomerName = "Test", Status = OutboundOrderStatus.InProgress });

            var huP = Guid.NewGuid();
            var huK = Guid.NewGuid();
            var huI = Guid.NewGuid();
            db.HandlingUnits.AddRange(
                new HandlingUnit { Id = huP, ClientId = ClientA, FacilityId = facKtmId, LpnCode = "KTM-CTN-1", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
                new HandlingUnit { Id = huK, ClientId = ClientA, FacilityId = facKtmId, LpnCode = "KTM-CTN-2", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
                new HandlingUnit { Id = huI, ClientId = ClientA, FacilityId = facIncompleteId, LpnCode = "INC-CTN-1", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse });

            db.Packages.AddRange(
                new Package { Id = pkgPatanId, ClientId = ClientA, FacilityId = facKtmId, OutboundOrderId = ordPatanId, HandlingUnitId = huP, PackageNumber = "PKG-KTM-01", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FREIGHT", TrackingNumber = "NP-TRK-01", GrossWeightKg = 5m, TareWeightKg = 0.5m, NetWeightKg = 4.5m, LengthCm = 30, WidthCm = 20, HeightCm = 20, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FREIGHT", ServiceLevel = CarrierServiceLevel.FreightLTL, TrackingNumber = "NP-TRK-01", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] },
                new Package { Id = pkgPokharaId, ClientId = ClientA, FacilityId = facKtmId, OutboundOrderId = ordPokharaId, HandlingUnitId = huK, PackageNumber = "PKG-KTM-02", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FREIGHT", TrackingNumber = "NP-TRK-02", GrossWeightKg = 7m, TareWeightKg = 0.5m, NetWeightKg = 6.5m, LengthCm = 35, WidthCm = 25, HeightCm = 20, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FREIGHT", ServiceLevel = CarrierServiceLevel.FreightLTL, TrackingNumber = "NP-TRK-02", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] },
                new Package { Id = pkgIncId, ClientId = ClientA, FacilityId = facIncompleteId, OutboundOrderId = ordIncId, HandlingUnitId = huI, PackageNumber = "PKG-INC-01", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "INC-TRK-01", GrossWeightKg = 2m, TareWeightKg = 0.2m, NetWeightKg = 1.8m, LengthCm = 20, WidthCm = 20, HeightCm = 20, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FEDEX", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "INC-TRK-01", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] });

            await db.SaveChangesAsync();
        }

        // 1. Facility with missing address rejects BOL issuance (400 BadRequest) instead of fabricating Columbus fallback
        using var rIncShp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipments", incStaffId,
            new { clientId = ClientA, facilityId = facIncompleteId, carrierCode = "SIM-FEDEX", serviceLevel = "StandardGround" }));
        var incShpId = JsonDocument.Parse(await rIncShp.Content.ReadAsStringAsync()).RootElement.GetProperty("shipmentId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{incShpId}/packages", incStaffId, new { packageId = pkgIncId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgIncId}/stage", incStaffId, new { stagingLocationId = incStageId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgIncId}/load", incStaffId, new { shipmentId = incShpId }))).StatusCode);
        using (var rIncBol = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{incShpId}/bol", incStaffId, new { })))
            Assert.Equal(HttpStatusCode.BadRequest, rIncBol.StatusCode);

        // 2. Kathmandu hub multi-consignee shipment produces Nepal shipper address and multiple consignee destinations
        using var rKtmShp = await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipments", ktmStaffId,
            new { clientId = ClientA, facilityId = facKtmId, carrierCode = "SIM-FREIGHT", serviceLevel = "FreightLTL", stagingLocationId = ktmStageId }));
        var ktmShpId = JsonDocument.Parse(await rKtmShp.Content.ReadAsStringAsync()).RootElement.GetProperty("shipmentId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{ktmShpId}/packages", ktmStaffId, new { packageId = pkgPatanId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{ktmShpId}/packages", ktmStaffId, new { packageId = pkgPokharaId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgPatanId}/stage", ktmStaffId, new { stagingLocationId = ktmStageId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgPokharaId}/stage", ktmStaffId, new { stagingLocationId = ktmStageId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgPatanId}/load", ktmStaffId, new { shipmentId = ktmShpId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgPokharaId}/load", ktmStaffId, new { shipmentId = ktmShpId }))).StatusCode);

        using var rKtmBol = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{ktmShpId}/bol", ktmStaffId,
            new { trailerNumber = "NP-TRK-BA1", sealNumber = "NP-SEAL-09" }));
        Assert.Equal(HttpStatusCode.OK, rKtmBol.StatusCode);
        var bolId = JsonDocument.Parse(await rKtmBol.Content.ReadAsStringAsync()).RootElement.GetProperty("bolId").GetGuid();

        using var rBolView = await _http.SendAsync(JsonReq(HttpMethod.Get, $"/api/v1/outbound/shipments/bol/{bolId}", ktmStaffId));
        Assert.Equal(HttpStatusCode.OK, rBolView.StatusCode);
        var bolDto = JsonDocument.Parse(await rBolView.Content.ReadAsStringAsync()).RootElement;

        var shipperAddr = bolDto.GetProperty("shipperAddressSnapshot").GetString()!;
        Assert.Contains("Durbar Marg, Ward 1", shipperAddr);
        Assert.Contains("Kathmandu", shipperAddr);
        Assert.Contains("Bagmati 44600", shipperAddr);
        Assert.Contains("NP", shipperAddr);
        Assert.DoesNotContain("Columbus", shipperAddr);
        Assert.DoesNotContain("43215", shipperAddr);

        var consigneeName = bolDto.GetProperty("consigneeName").GetString()!;
        var consigneeAddr = bolDto.GetProperty("consigneeAddressSnapshot").GetString()!;
        Assert.Contains("Multiple Consignees (2 Destinations)", consigneeName);
        Assert.Contains("Lalitpur", consigneeAddr);
        Assert.Contains("Pokhara", consigneeAddr);

        var snapshotRoot = JsonDocument.Parse(bolDto.GetProperty("documentSnapshotJson").GetString()!).RootElement;
        Assert.Equal("Kathmandu", snapshotRoot.GetProperty("Shipper").GetProperty("City").GetString());
        Assert.Equal("NP", snapshotRoot.GetProperty("Shipper").GetProperty("CountryCode").GetString());
        Assert.Equal(2, snapshotRoot.GetProperty("Consignees").GetArrayLength());
    }

    [Fact]
    public async Task P744R02_R03_R04_NegativeWorkflowGuards_VoidReissue_DockValidation_And_DispatchConcurrencyConflict()
    {
        var inactiveStageId = Guid.NewGuid();
        var inactiveDockId = Guid.NewGuid();
        var wrongFacDockId = Guid.NewGuid();
        var ordExtraId = Guid.NewGuid();
        var pkgAId = Guid.NewGuid();
        var pkgBId = Guid.NewGuid();
        var pkgOtherShpId = Guid.NewGuid();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Locations.AddRange(
                new Location { Id = inactiveStageId, FacilityId = FacilityA, LocationCode = "STG-INACT", Barcode = "STG-INACT", LocationType = LocationType.StagingLane, IsActive = false },
                new Location { Id = inactiveDockId, FacilityId = FacilityA, LocationCode = "DOCK-INACT", Barcode = "DOCK-INACT", LocationType = LocationType.DockDoor, IsActive = false },
                new Location { Id = wrongFacDockId, FacilityId = FacilityB, LocationCode = "DOCK-FACB", Barcode = "DOCK-FACB", LocationType = LocationType.DockDoor, IsActive = true });

            db.OutboundOrders.Add(new OutboundOrder { Id = ordExtraId, ClientId = ClientA, FacilityId = FacilityA, OrderNumber = "SO-744-NEG", CustomerName = "Neg Buyer", Status = OutboundOrderStatus.InProgress });

            var huA = Guid.NewGuid();
            var huB = Guid.NewGuid();
            var huC = Guid.NewGuid();
            db.HandlingUnits.AddRange(
                new HandlingUnit { Id = huA, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "NEG-CTN-A", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
                new HandlingUnit { Id = huB, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "NEG-CTN-B", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
                new HandlingUnit { Id = huC, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "NEG-CTN-C", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse });

            db.Packages.AddRange(
                new Package { Id = pkgAId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = ordExtraId, HandlingUnitId = huA, PackageNumber = "PKG-NEG-A", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-NEG-A", GrossWeightKg = 3m, TareWeightKg = 0.4m, NetWeightKg = 2.6m, LengthCm = 20, WidthCm = 20, HeightCm = 20, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FEDEX", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "TRK-NEG-A", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] },
                new Package { Id = pkgBId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = ordExtraId, HandlingUnitId = huB, PackageNumber = "PKG-NEG-B", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-NEG-B", GrossWeightKg = 3m, TareWeightKg = 0.4m, NetWeightKg = 2.6m, LengthCm = 20, WidthCm = 20, HeightCm = 20, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FEDEX", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "TRK-NEG-B", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] },
                new Package { Id = pkgOtherShpId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = Order2Id, HandlingUnitId = huC, PackageNumber = "PKG-NEG-C", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-NEG-C", GrossWeightKg = 3m, TareWeightKg = 0.4m, NetWeightKg = 2.6m, LengthCm = 20, WidthCm = 20, HeightCm = 20, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FEDEX", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "TRK-NEG-C", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] });
            await db.SaveChangesAsync();
        }

        // Create Shipment 1
        var shp1Id = JsonDocument.Parse(await (await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipments", ShipStaffAId,
            new { clientId = ClientA, facilityId = FacilityA, carrierCode = "SIM-FEDEX", serviceLevel = "StandardGround" }))).Content.ReadAsStringAsync()).RootElement.GetProperty("shipmentId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp1Id}/packages", ShipStaffAId, new { packageId = pkgAId }))).StatusCode);

        // P744-R02: Early BOL generation in Draft status -> 422 UnprocessableEntity
        using (var rEarlyBol = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp1Id}/bol", ShipStaffAId, new { })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rEarlyBol.StatusCode);

        // P744-R04: Inactive staging lane -> 400; invalid DockLocationId on Load -> 400
        using (var rInactStg = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgAId}/stage", ShipStaffAId, new { stagingLocationId = inactiveStageId })))
            Assert.Equal(HttpStatusCode.BadRequest, rInactStg.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgAId}/stage", ShipStaffAId, new { stagingLocationId = StagingLaneAId }))).StatusCode);

        foreach (var badDockId in new[] { Guid.Empty, inactiveDockId, wrongFacDockId, StorageBinAId })
        {
            using var rBadDock = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgAId}/load", ShipStaffAId,
                new { shipmentId = shp1Id, trailerNumber = "TRL-NEG-1", dockLocationId = badDockId }));
            Assert.Equal(HttpStatusCode.BadRequest, rBadDock.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgAId}/load", ShipStaffAId,
            new { shipmentId = shp1Id, trailerNumber = "TRL-NEG-1", dockLocationId = DockDoorAId }))).StatusCode);

        // P744-R02: Dispatch without active BOL -> 422
        using (var rNoBolDisp = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp1Id}/dispatch", ShipStaffAId,
            new { driverName = "Dan DriverA", driverSignature = "SIG-1", trailerNumber = "TRL-NEG-1" })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rNoBolDisp.StatusCode);

        // Issue BOL R1
        var rBol1 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp1Id}/bol", ShipStaffAId,
            new { trailerNumber = "TRL-NEG-1", sealNumber = "SEAL-NEG-1" }));
        Assert.Equal(HttpStatusCode.OK, rBol1.StatusCode);
        var bol1Id = JsonDocument.Parse(await rBol1.Content.ReadAsStringAsync()).RootElement.GetProperty("bolId").GetGuid();

        // P744-R02: Manifest locked once active BOL issued -> add & remove rejected 422
        using (var rAddLocked = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp1Id}/packages", ShipStaffAId, new { packageId = pkgBId })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rAddLocked.StatusCode);
        using (var rRemLocked = await _http.SendAsync(JsonReq(HttpMethod.Delete, $"/api/v1/outbound/shipments/{shp1Id}/packages/{pkgAId}", ShipStaffAId, new { reason = "Tamper" })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rRemLocked.StatusCode);

        // P744-R02: Explicit void unlocks manifest & enables reissue (-R2)
        using (var rVoidCross = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/bol/{bol1Id}/void", ShipStaffBId, new { reason = "Cross facility" })))
            Assert.Equal(HttpStatusCode.Forbidden, rVoidCross.StatusCode);
        using (var rVoidOk = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/bol/{bol1Id}/void", ShipStaffAId, new { reason = "Need updated seal" })))
            Assert.Equal(HttpStatusCode.OK, rVoidOk.StatusCode);

        var rBol2 = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp1Id}/bol", ShipStaffAId,
            new { trailerNumber = "TRL-NEG-1", sealNumber = "SEAL-NEG-R2" }));
        Assert.Equal(HttpStatusCode.OK, rBol2.StatusCode);
        var bol2Root = JsonDocument.Parse(await rBol2.Content.ReadAsStringAsync()).RootElement;
        var bol2Id = bol2Root.GetProperty("bolId").GetGuid();
        Assert.EndsWith("-R2", bol2Root.GetProperty("bolNumber").GetString()!);

        // P744-R02: Conflicting trailer or seal on dispatch vs active BOL -> 422
        using (var rBadTrailer = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp1Id}/dispatch", ShipStaffAId,
            new { driverName = "Dan DriverA", driverSignature = "SIG-1", trailerNumber = "TRL-SPOOF", sealNumber = "SEAL-NEG-R2" })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rBadTrailer.StatusCode);
        using (var rBadSeal = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp1Id}/dispatch", ShipStaffAId,
            new { driverName = "Dan DriverA", driverSignature = "SIG-1", trailerNumber = "TRL-NEG-1", sealNumber = "SEAL-SPOOF" })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rBadSeal.StatusCode);

        // P744-R03: Concurrent conflicting dispatch calls -> 1x 200 OK, 1x 409 Conflict
        var concResps = await Task.WhenAll(
            _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp1Id}/dispatch", ShipStaffAId,
                new { driverName = "Dan DriverA", driverSignature = "SIG-DAN", trailerNumber = "TRL-NEG-1", sealNumber = "SEAL-NEG-R2", idempotencyKey = "disp-neg-key-1" })),
            _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp1Id}/dispatch", ShipStaffAId,
                new { driverName = "Impostor Driver", driverSignature = "SIG-IMP", trailerNumber = "TRL-NEG-1", sealNumber = "SEAL-NEG-R2", idempotencyKey = "disp-neg-key-2" })));
        var concStatuses = concResps.Select(r => r.StatusCode).ToArray();
        foreach (var r in concResps) r.Dispose();

        Assert.Single(concStatuses, s => s == HttpStatusCode.OK);
        Assert.Single(concStatuses, s => s == HttpStatusCode.Conflict);

        // Dispatched BOL cannot be voided -> 422
        using (var rVoidAfterDisp = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/bol/{bol2Id}/void", ShipStaffAId, new { reason = "Too late" })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rVoidAfterDisp.StatusCode);

        // Cross-shipment reuse of an already-consumed dispatch IdempotencyKey -> 409 Conflict
        var shp2Id = JsonDocument.Parse(await (await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipments", ShipStaffAId,
            new { clientId = ClientA, facilityId = FacilityA, carrierCode = "SIM-FEDEX", serviceLevel = "StandardGround" }))).Content.ReadAsStringAsync()).RootElement.GetProperty("shipmentId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp2Id}/packages", ShipStaffAId, new { packageId = pkgOtherShpId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgOtherShpId}/stage", ShipStaffAId, new { stagingLocationId = StagingLaneAId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgOtherShpId}/load", ShipStaffAId, new { shipmentId = shp2Id }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp2Id}/bol", ShipStaffAId, new { trailerNumber = "TRL-2", sealNumber = "SEAL-2" }))).StatusCode);

        var winnerKey = concStatuses[0] == HttpStatusCode.OK ? "disp-neg-key-1" : "disp-neg-key-2";
        using (var rCrossKeyReuse = await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shp2Id}/dispatch", ShipStaffAId,
            new { driverName = "Dan DriverA", driverSignature = "SIG-DAN", trailerNumber = "TRL-2", sealNumber = "SEAL-2", idempotencyKey = winnerKey })))
        {
            Assert.Equal(HttpStatusCode.Conflict, rCrossKeyReuse.StatusCode);
        }
    }

    [Fact]
    public async Task P744R05_ConcurrentManifestModification_BolIssuance_VoidAndDispatch_RemainConsistent()
    {
        var ordAId = Guid.NewGuid();
        var ordBId = Guid.NewGuid();
        var pkgAId = Guid.NewGuid();
        var pkgBId = Guid.NewGuid();
        var pkgCId = Guid.NewGuid();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.OutboundOrders.AddRange(
                new OutboundOrder { Id = ordAId, ClientId = ClientA, FacilityId = FacilityA, OrderNumber = "SO-744-R05-A", CustomerName = "Race Alpha", Status = OutboundOrderStatus.InProgress },
                new OutboundOrder { Id = ordBId, ClientId = ClientA, FacilityId = FacilityA, OrderNumber = "SO-744-R05-B", CustomerName = "Race Beta", Status = OutboundOrderStatus.InProgress });
            var huA = Guid.NewGuid();
            var huB = Guid.NewGuid();
            var huC = Guid.NewGuid();
            db.HandlingUnits.AddRange(
                new HandlingUnit { Id = huA, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-744-R05-A", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
                new HandlingUnit { Id = huB, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-744-R05-B", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse },
                new HandlingUnit { Id = huC, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-744-R05-C", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.InUse });
            db.Packages.AddRange(
                new Package { Id = pkgAId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = ordAId, HandlingUnitId = huA, PackageNumber = "PKG-744-R05-A", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-R05-A", GrossWeightKg = 3m, TareWeightKg = 0.2m, NetWeightKg = 2.8m, LengthCm = 20, WidthCm = 20, HeightCm = 20, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FEDEX", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "TRK-R05-A", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] },
                new Package { Id = pkgBId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = ordBId, HandlingUnitId = huB, PackageNumber = "PKG-744-R05-B", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-R05-B", GrossWeightKg = 4m, TareWeightKg = 0.2m, NetWeightKg = 3.8m, LengthCm = 20, WidthCm = 20, HeightCm = 20, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FEDEX", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "TRK-R05-B", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] },
                new Package { Id = pkgCId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = ordBId, HandlingUnitId = huC, PackageNumber = "PKG-744-R05-C", Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-R05-C", GrossWeightKg = 2m, TareWeightKg = 0.2m, NetWeightKg = 1.8m, LengthCm = 20, WidthCm = 20, HeightCm = 20, ShippingLabels = [new ShippingLabel { CarrierCode = "SIM-FEDEX", ServiceLevel = CarrierServiceLevel.StandardGround, TrackingNumber = "TRK-R05-C", LabelData = "^XA^XZ", Status = ShippingLabelStatus.Generated }] });
            await db.SaveChangesAsync();
        }

        // Create 2-package shipment and stage both
        var shpId = JsonDocument.Parse(await (await _http.SendAsync(JsonReq(HttpMethod.Post, "/api/v1/outbound/shipments", ShipStaffAId,
            new { clientId = ClientA, facilityId = FacilityA, carrierCode = "SIM-FEDEX", serviceLevel = "StandardGround" }))).Content.ReadAsStringAsync()).RootElement.GetProperty("shipmentId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shpId}/packages", ShipStaffAId, new { packageId = pkgAId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shpId}/packages", ShipStaffAId, new { packageId = pkgBId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgAId}/stage", ShipStaffAId, new { stagingLocationId = StagingLaneAId }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgBId}/stage", ShipStaffAId, new { stagingLocationId = StagingLaneAId }))).StatusCode);

        // Concurrent Load + Load -> reliably ReadyForDispatch
        var loadRace = await Task.WhenAll(
            _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgAId}/load", ShipStaffAId, new { shipmentId = shpId, trailerNumber = "TRL-R05" })),
            _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/packages/{pkgBId}/load", ShipStaffAId, new { shipmentId = shpId, trailerNumber = "TRL-R05" })));
        Assert.All(loadRace, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        foreach (var r in loadRace) r.Dispose();

        // Concurrent Remove + BOL issuance -> BOL manifest matches persisted shipment packages
        var removeBolRace = await Task.WhenAll(
            _http.SendAsync(JsonReq(HttpMethod.Delete, $"/api/v1/outbound/shipments/{shpId}/packages/{pkgBId}", ShipStaffAId, new { reason = "Concurrent pull" })),
            _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shpId}/bol", ShipStaffAId, new { trailerNumber = "TRL-R05", sealNumber = "SEAL-R05" })));
        var removeResp = removeBolRace[0];
        var bolResp = removeBolRace[1];
        Assert.Equal(HttpStatusCode.OK, bolResp.StatusCode);
        Assert.True(removeResp.StatusCode == HttpStatusCode.UnprocessableEntity || removeResp.StatusCode == HttpStatusCode.OK);
        removeResp.Dispose();
        bolResp.Dispose();

        Guid activeBolId;
        using (var checkScope = _app.Services.CreateScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persistedPkgNums = await db.Packages.IgnoreQueryFilters()
                .Where(p => p.ShipmentId == shpId && !p.IsDeleted)
                .OrderBy(p => p.PackageNumber)
                .Select(p => p.PackageNumber)
                .ToListAsync();
            var bol = await db.BillsOfLading.IgnoreQueryFilters()
                .SingleAsync(b => b.ShipmentId == shpId && !b.IsVoided && !b.IsDeleted);
            activeBolId = bol.Id;
            var snapNums = JsonDocument.Parse(bol.DocumentSnapshotJson).RootElement
                .GetProperty("Packages").EnumerateArray()
                .Select(e => e.GetProperty("PackageNumber").GetString()!)
                .OrderBy(x => x)
                .ToList();
            Assert.Equal(persistedPkgNums.Count, bol.TotalPackageCount);
            Assert.Equal(persistedPkgNums, snapNums);
        }

        // Concurrent Void + Dispatch -> strictly mutually exclusive outcomes
        var voidDispRace = await Task.WhenAll(
            _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/bol/{activeBolId}/void", ShipStaffAId, new { reason = "Concurrent void" })),
            _http.SendAsync(JsonReq(HttpMethod.Post, $"/api/v1/outbound/shipments/{shpId}/dispatch", ShipStaffAId,
                new { driverName = "Dan DriverA", driverSignature = "SIG-R05", trailerNumber = "TRL-R05", sealNumber = "SEAL-R05", idempotencyKey = "r05-disp-1" })));
        var voidStatus = voidDispRace[0].StatusCode;
        var dispStatus = voidDispRace[1].StatusCode;
        foreach (var r in voidDispRace) r.Dispose();

        Assert.NotEqual(voidStatus, dispStatus);
        Assert.Contains(HttpStatusCode.OK, new[] { voidStatus, dispStatus });
        Assert.Contains(HttpStatusCode.UnprocessableEntity, new[] { voidStatus, dispStatus });
    }
}
