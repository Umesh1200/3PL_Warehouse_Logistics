using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Outbound.Dispatch.DTOs;
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
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Allocation;
using Warehouse3PL.Infrastructure.Services.Packing;
using Warehouse3PL.Infrastructure.Services.Picking;
using Warehouse3PL.Infrastructure.Services.Shipping;
using Warehouse3PL.Infrastructure.Services.Storage;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Warehouse3PL.WebApi.Security;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public sealed class Phase740CDispatchAndCourierHttpTests : IAsyncLifetime
{
    private const string TestJwtSecret = "Nexus3PL-Phase740C-RealJwt-SigningKey-2026-Strong!";
    private const string TestIssuer = "nexus3pl-740c-auth";
    private const string TestAudience = "nexus3pl-740c-api";
    private const string FedExSecret = "phase740c-fedex-hmac-secret-key-001";

    private static readonly Guid ClientA = Guid.Parse("740d0001-0000-0000-0000-000000000001");
    private static readonly Guid ClientB = Guid.Parse("740d0001-0000-0000-0000-000000000002");
    private static readonly Guid FacilityA = Guid.Parse("740d0002-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("740d0002-0000-0000-0000-000000000002");

    private static readonly Guid SuperAdminId = Guid.Parse("740d0003-0000-0000-0000-000000000001");
    private static readonly Guid SupervisorFacAId = Guid.Parse("740d0003-0000-0000-0000-000000000002");
    private static readonly Guid SupervisorFacBId = Guid.Parse("740d0003-0000-0000-0000-000000000003");
    private static readonly Guid DriverAId = Guid.Parse("740d0003-0000-0000-0000-000000000004");
    private static readonly Guid DriverBId = Guid.Parse("740d0003-0000-0000-0000-000000000005");
    private static readonly Guid ClientAdminAId = Guid.Parse("740d0003-0000-0000-0000-000000000006");
    private static readonly Guid ClientBUserId = Guid.Parse("740d0003-0000-0000-0000-000000000007");
    private static readonly Guid DriverForeignClientId = Guid.Parse("740d0003-0000-0000-0000-000000000008");

    private static readonly Guid ItemA = Guid.Parse("740d0004-0000-0000-0000-000000000001");
    private static readonly Guid BinA = Guid.Parse("740d0005-0000-0000-0000-000000000001");
    private static readonly Guid OrderA = Guid.Parse("740d0006-0000-0000-0000-000000000001");
    private static readonly Guid OrderB = Guid.Parse("740d0006-0000-0000-0000-000000000002");
    private static readonly Guid OrderClientAFacB = Guid.Parse("740d0006-0000-0000-0000-000000000003");

    private static readonly Guid ShipmentDraftId = Guid.Parse("740d0007-0000-0000-0000-000000000001");
    private static readonly Guid ShipmentDispatchedId = Guid.Parse("740d0007-0000-0000-0000-000000000002");
    private static readonly Guid ShipmentFacBId = Guid.Parse("740d0007-0000-0000-0000-000000000003");

    private static readonly Guid PkgDraftId = Guid.Parse("740d0008-0000-0000-0000-000000000001");
    private static readonly Guid Pkg1DispatchedId = Guid.Parse("740d0008-0000-0000-0000-000000000002");
    private static readonly Guid Pkg2DispatchedId = Guid.Parse("740d0008-0000-0000-0000-000000000003");
    private static readonly Guid PkgForeignBId = Guid.Parse("740d0008-0000-0000-0000-000000000004");
    private static readonly Guid PkgFacBId = Guid.Parse("740d0008-0000-0000-0000-000000000005");

    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private string _storageDir = null!;
    private RaceStorageState _raceStorageState = null!;
    private RaceDbBarrierInterceptor _raceDbBarrier = null!;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public async Task InitializeAsync()
    {
        _storageDir = Path.Combine(Path.GetTempPath(), $"nexus3pl-pod-740c-{Guid.NewGuid():N}");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ObjectStorage:BaseDirectory"] = _storageDir,
            ["CarrierWebhooks:TimestampToleranceSeconds"] = "300",
            ["CarrierWebhooks:MaxPayloadBytes"] = "262144",
            ["CarrierWebhooks:Carriers:SIM-FEDEX:PrimarySecret"] = FedExSecret
        });

        builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
        builder.Services.Configure<CarrierWebhookOptions>(builder.Configuration.GetSection(CarrierWebhookOptions.SectionName));
        builder.Services.Configure<JwtOptions>(o =>
        {
            o.SecretKey = TestJwtSecret;
            o.Issuer = TestIssuer;
            o.Audience = TestAudience;
            o.ExpiryMinutes = 60;
        });

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(DispatchController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();

        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        _raceStorageState = new RaceStorageState();
        builder.Services.AddSingleton(_raceStorageState);
        _raceDbBarrier = new RaceDbBarrierInterceptor();
        builder.Services.AddSingleton(_raceDbBarrier);
        builder.Services.AddSingleton<PrivateObjectStorageService>();
        builder.Services.AddSingleton<IObjectStorageService, RaceControllableObjectStorageService>();
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

        builder.Services.AddScoped<IShippingCarrierService, SimulatedCarrierService>();
        builder.Services.AddScoped<IShippingLabelService, ShippingLabelService>();
        builder.Services.AddScoped<IShipmentConsolidationService, ShipmentConsolidationService>();
        builder.Services.AddScoped<IShipmentTrackingService, ShipmentTrackingService>();
        builder.Services.AddScoped<IProofOfDeliveryService, ProofOfDeliveryService>();
        builder.Services.AddScoped<IDeliveryConfirmationService, DeliveryConfirmationService>();
        builder.Services.AddScoped<ICarrierExceptionClassificationService, CarrierExceptionClassificationService>();
        builder.Services.AddScoped<IDeliveryExceptionService, DeliveryExceptionService>();
        builder.Services.AddScoped<IReturnTrackingService, ReturnTrackingService>();

        var dbName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) => options
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<RaceDbBarrierInterceptor>()));
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

    private static async Task SeedAsync(ApplicationDbContext db, IPasswordHasher hasher)
    {
        db.Clients.AddRange(
            new Client { Id = ClientA, CompanyName = "Client Alpha", AccountCode = "ALP-A", Status = ClientStatus.Active },
            new Client { Id = ClientB, CompanyName = "Client Beta", AccountCode = "BET-B", Status = ClientStatus.Active });

        db.Facilities.AddRange(
            new Facility { Id = FacilityA, FacilityCode = "FAC-A", Name = "Hub Alpha", AddressLine1 = "1 Main", City = "Chicago", CountryCode = "US", IsActive = true },
            new Facility { Id = FacilityB, FacilityCode = "FAC-B", Name = "Hub Beta", AddressLine1 = "2 Main", City = "Dallas", CountryCode = "US", IsActive = true });

        db.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityA, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityB, Status = FacilityAccessStatus.Revoked },
            new ClientFacilityAccess { ClientId = ClientB, FacilityId = FacilityA, Status = FacilityAccessStatus.Active });

        var roleSuper = new Role { RoleName = SystemRoles.SuperAdmin, IsSystemRole = true };
        var roleSupervisor = new Role { RoleName = SystemRoles.OperationsManager, IsSystemRole = true };
        roleSupervisor.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Dispatch.View, AccessLevel = AccessLevel.Read });
        roleSupervisor.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Dispatch.ManageRuns, AccessLevel = AccessLevel.Admin });
        roleSupervisor.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Dispatch.ExecuteDelivery, AccessLevel = AccessLevel.Execute });
        roleSupervisor.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Ship, AccessLevel = AccessLevel.Write });
        roleSupervisor.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Analytics.MultiFacilityView, AccessLevel = AccessLevel.Read });

        var roleClientAdmin = new Role { RoleName = SystemRoles.ClientAdmin, IsSystemRole = true };
        roleClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Dispatch.View, AccessLevel = AccessLevel.Read });
        roleClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.OrdersView, AccessLevel = AccessLevel.Read });

        // Pre-seed Driver role with zero permissions to verify DbInitializer adds dispatch:execute-delivery idempotently
        var roleDriver = new Role { RoleName = SystemRoles.Driver, IsSystemRole = true };

        db.Roles.AddRange(roleSuper, roleSupervisor, roleClientAdmin, roleDriver);
        await db.SaveChangesAsync();

        var pwd = hasher.HashPassword("Password123!");
        var users = new[]
        {
            new User { Id = SuperAdminId, Email = "super@740c.test", FirstName = "Super", LastName = "Admin", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = pwd },
            new User { Id = SupervisorFacAId, Email = "supa@740c.test", FirstName = "Sup", LastName = "FacA", UserType = UserType.Staff, AssignedFacilityId = FacilityA, Status = UserStatus.Active, PasswordHash = pwd },
            new User { Id = SupervisorFacBId, Email = "supb@740c.test", FirstName = "Sup", LastName = "FacB", UserType = UserType.Staff, AssignedFacilityId = FacilityB, Status = UserStatus.Active, PasswordHash = pwd },
            new User { Id = DriverAId, Email = "drivera@740c.test", FirstName = "Driver", LastName = "Alpha", UserType = UserType.Driver, Status = UserStatus.Active, PasswordHash = pwd },
            new User { Id = DriverBId, Email = "driverb@740c.test", FirstName = "Driver", LastName = "Beta", UserType = UserType.Driver, Status = UserStatus.Active, PasswordHash = pwd },
            new User { Id = DriverForeignClientId, Email = "driverbclient@740c.test", FirstName = "Driver", LastName = "ClientB", UserType = UserType.Driver, ClientId = ClientB, Status = UserStatus.Active, PasswordHash = pwd },
            new User { Id = ClientAdminAId, Email = "clienta@740c.test", FirstName = "Client", LastName = "Alpha", UserType = UserType.Client, ClientId = ClientA, Status = UserStatus.Active, PasswordHash = pwd },
            new User { Id = ClientBUserId, Email = "clientb@740c.test", FirstName = "Client", LastName = "Beta", UserType = UserType.Client, ClientId = ClientB, Status = UserStatus.Active, PasswordHash = pwd }
        };
        users[0].UserRoles.Add(new UserRole { User = users[0], Role = roleSuper });
        users[1].UserRoles.Add(new UserRole { User = users[1], Role = roleSupervisor });
        users[2].UserRoles.Add(new UserRole { User = users[2], Role = roleSupervisor });
        users[3].UserRoles.Add(new UserRole { User = users[3], Role = roleDriver });
        users[4].UserRoles.Add(new UserRole { User = users[4], Role = roleDriver });
        users[5].UserRoles.Add(new UserRole { User = users[5], Role = roleDriver });
        users[6].UserRoles.Add(new UserRole { User = users[6], Role = roleClientAdmin });
        users[7].UserRoles.Add(new UserRole { User = users[7], Role = roleClientAdmin });
        db.Users.AddRange(users);

        db.Items.Add(new Item { Id = ItemA, ClientId = ClientA, Sku = "SKU-740C-1", Description = "Parcel Item" });
        db.Locations.Add(new Location { Id = BinA, FacilityId = FacilityA, LocationCode = "B-01", Barcode = "B-01", LocationType = LocationType.Bin, IsActive = true, IsPickable = true });
        db.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = ClientA, FacilityId = FacilityA, LocationId = BinA, ItemId = ItemA, QtyAvailable = 100m, QtyAllocated = 10m });

        db.OutboundOrders.AddRange(
            new OutboundOrder { Id = OrderA, ClientId = ClientA, FacilityId = FacilityA, OrderNumber = "ORD-740C-A", CustomerName = "Buyer A", Status = OutboundOrderStatus.Completed, DeliveryStatus = OrderDeliveryStatus.Dispatched },
            new OutboundOrder { Id = OrderB, ClientId = ClientB, FacilityId = FacilityA, OrderNumber = "ORD-740C-B", CustomerName = "Buyer B", Status = OutboundOrderStatus.Completed, DeliveryStatus = OrderDeliveryStatus.Dispatched },
            new OutboundOrder { Id = OrderClientAFacB, ClientId = ClientA, FacilityId = FacilityB, OrderNumber = "ORD-740C-AB", CustomerName = "Buyer AB", Status = OutboundOrderStatus.Completed, DeliveryStatus = OrderDeliveryStatus.Dispatched });

        db.Shipments.AddRange(
            new Shipment { Id = ShipmentDraftId, ClientId = ClientA, FacilityId = FacilityA, ShipmentNumber = "SHP-740C-DRAFT", CarrierCode = "SIM-FEDEX", Status = ShipmentStatus.ReadyForDispatch, BillOfLadingNumber = "BOL-SHP-740C-DRAFT", CustodyStatus = ShipmentCustodyStatus.Unassigned, Version = 1 },
            new Shipment { Id = ShipmentDispatchedId, ClientId = ClientA, FacilityId = FacilityA, ShipmentNumber = "SHP-740C-DISP", CarrierCode = "SIM-FEDEX", Status = ShipmentStatus.Dispatched, CustodyStatus = ShipmentCustodyStatus.Confirmed, DeliveryStatus = TrackingMilestoneStatus.OutForDelivery, AssignedDriverUserId = DriverAId, DispatchedAtUtc = DateTime.UtcNow.AddHours(-1), Version = 2 },
            new Shipment { Id = ShipmentFacBId, ClientId = ClientA, FacilityId = FacilityB, ShipmentNumber = "SHP-740C-FACB", CarrierCode = "SIM-FEDEX", Status = ShipmentStatus.Dispatched, CustodyStatus = ShipmentCustodyStatus.Confirmed, DeliveryStatus = TrackingMilestoneStatus.InTransit, AssignedDriverUserId = DriverAId, DispatchedAtUtc = DateTime.UtcNow.AddHours(-1), Version = 1 });

        db.BillsOfLading.Add(new BillOfLading
        {
            ClientId = ClientA,
            FacilityId = FacilityA,
            ShipmentId = ShipmentDraftId,
            BolNumber = "BOL-SHP-740C-DRAFT",
            CarrierCode = "SIM-FEDEX",
            CarrierName = "SIM-FEDEX",
            ServiceLevel = nameof(CarrierServiceLevel.StandardGround),
            TotalPackageCount = 1,
            TotalGrossWeightKg = 2m,
            TotalVolumeCm3 = 1000m,
            ShipperName = "Hub Alpha",
            ShipperAddressSnapshot = "1 Main, Chicago, US",
            ConsigneeName = "Buyer A",
            ConsigneeAddressSnapshot = "Buyer A Address",
            PackageTrackingNumbers = "TRK-740C-DRAFT",
            DocumentSnapshotJson = "{}",
            PreparedByUserId = SupervisorFacAId
        });

        db.ShipmentCustodyTransfers.Add(new ShipmentCustodyTransfer
        {
            ClientId = ClientA,
            FacilityId = FacilityA,
            ShipmentId = ShipmentDispatchedId,
            CarrierCode = "SIM-FEDEX",
            DriverName = "Driver Alpha",
            DriverSignatureAcknowledgment = "SIG-INIT",
            TotalPackagesConfirmed = 2,
            TotalWeightKg = 5m,
            AuthorizedByUserId = SupervisorFacAId,
            AssignedDriverUserId = DriverAId,
            TransferredAtUtc = DateTime.UtcNow.AddHours(-1)
        });

        db.Packages.AddRange(
            new Package { Id = PkgDraftId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = OrderA, ShipmentId = ShipmentDraftId, PackageNumber = "PKG-740C-DRAFT", CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-740C-DRAFT", Status = PackageStatus.Loaded, CurrentTrackingStatus = TrackingMilestoneStatus.PendingHandover, GrossWeightKg = 2m, LengthCm = 10, WidthCm = 10, HeightCm = 10 },
            new Package { Id = Pkg1DispatchedId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = OrderA, ShipmentId = ShipmentDispatchedId, PackageNumber = "PKG-740C-1", CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-740C-1", Status = PackageStatus.Loaded, CurrentTrackingStatus = TrackingMilestoneStatus.OutForDelivery, GrossWeightKg = 2m, LengthCm = 10, WidthCm = 10, HeightCm = 10 },
            new Package { Id = Pkg2DispatchedId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = OrderA, ShipmentId = ShipmentDispatchedId, PackageNumber = "PKG-740C-2", CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-740C-2", Status = PackageStatus.Loaded, CurrentTrackingStatus = TrackingMilestoneStatus.OutForDelivery, GrossWeightKg = 3m, LengthCm = 10, WidthCm = 10, HeightCm = 10 },
            new Package { Id = PkgForeignBId, ClientId = ClientB, FacilityId = FacilityA, OutboundOrderId = OrderB, PackageNumber = "PKG-740C-B1", CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-740C-B1", Status = PackageStatus.Loaded, CurrentTrackingStatus = TrackingMilestoneStatus.InTransit, GrossWeightKg = 2m, LengthCm = 10, WidthCm = 10, HeightCm = 10 },
            new Package { Id = PkgFacBId, ClientId = ClientA, FacilityId = FacilityB, OutboundOrderId = OrderClientAFacB, ShipmentId = ShipmentFacBId, PackageNumber = "PKG-740C-FB", CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-740C-FB", Status = PackageStatus.Loaded, CurrentTrackingStatus = TrackingMilestoneStatus.InTransit, GrossWeightKg = 2m, LengthCm = 10, WidthCm = 10, HeightCm = 10 });

        await db.SaveChangesAsync();

        var init = new DbInitializer(db, hasher, Microsoft.Extensions.Logging.Abstractions.NullLogger<DbInitializer>.Instance);
        await init.SeedAsync();
        await init.SeedAsync(); // verify idempotency
    }

    private async Task<string> LoginAsync(string email, string password = "Password123!")
    {
        using var resp = await _http.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static HttpRequestMessage BearerRequest(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    private static byte[] CreateValidPngBytes() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01];

    private static byte[] CreateValidJpgBytes() =>
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9];

    private static HttpRequestMessage SignedWebhook(string url, object payload, string carrierId, string secret, string nonce)
    {
        var rawBody = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var msgPrefix = Encoding.UTF8.GetBytes($"{ts}.{nonce}.");
        var signedBytes = new byte[msgPrefix.Length + rawBody.Length];
        Buffer.BlockCopy(msgPrefix, 0, signedBytes, 0, msgPrefix.Length);
        Buffer.BlockCopy(rawBody, 0, signedBytes, msgPrefix.Length, rawBody.Length);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexString(hmac.ComputeHash(signedBytes)).ToLowerInvariant();

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(rawBody)
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        req.Headers.Add(CarrierWebhookAuthenticationFilter.CarrierIdHeader, carrierId);
        req.Headers.Add(CarrierWebhookAuthenticationFilter.TimestampHeader, ts);
        req.Headers.Add(CarrierWebhookAuthenticationFilter.NonceHeader, nonce);
        req.Headers.Add(CarrierWebhookAuthenticationFilter.SignatureHeader, sig);
        return req;
    }

    [Fact]
    public async Task DriverRoleSeed_GrantsOnlyExecuteDelivery_AndJwtLocksDriverOutOfWarehouseAndManagementRoutes()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var driverPerms = await db.RolePermissions
                .Where(rp => rp.Role.RoleName == SystemRoles.Driver)
                .Select(rp => rp.PermissionKey)
                .ToListAsync();

            Assert.Single(driverPerms);
            Assert.Equal(Permissions.Dispatch.ExecuteDelivery, driverPerms[0]);
            Assert.DoesNotContain(Permissions.Outbound.Pick, driverPerms);
            Assert.DoesNotContain(Permissions.Outbound.Pack, driverPerms);
            Assert.DoesNotContain(Permissions.Outbound.Ship, driverPerms);
            Assert.DoesNotContain(Permissions.Dispatch.ManageRuns, driverPerms);
        }

        var driverToken = await LoginAsync("drivera@740c.test");

        // Driver CANNOT access RF pick queues, shipments, packing, dispatch board, timelines, or supervisor driver-assignment
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, "/api/v1/outbound/pick-tasks"),
            (HttpMethod.Get, "/api/v1/outbound/pick-tasks/next"),
            (HttpMethod.Get, "/api/v1/outbound/packing/sessions"),
            (HttpMethod.Get, "/api/v1/outbound/shipments"),
            (HttpMethod.Get, "/api/v1/outbound/dispatch/board"),
            (HttpMethod.Get, "/api/v1/outbound/dispatch/exceptions"),
            (HttpMethod.Get, $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/timeline"),
            (HttpMethod.Get, $"/api/v1/outbound/dispatch/orders/{OrderA}/delivery-status"),
            (HttpMethod.Get, $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod")
        })
        {
            using var resp = await _http.SendAsync(BearerRequest(method, path, driverToken));
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        using (var rAssign = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDispatchedId}/assign-driver", driverToken, new { driverUserId = DriverAId })))
            Assert.Equal(HttpStatusCode.Forbidden, rAssign.StatusCode);
    }

    [Fact]
    public async Task AssignDriver_CustodyTransfer_Concurrency_Revocation_And_Dispatch_EndToEnd()
    {
        var supAToken = await LoginAsync("supa@740c.test");
        var supBToken = await LoginAsync("supb@740c.test");
        var clientToken = await LoginAsync("clienta@740c.test");
        var driverAToken = await LoginAsync("drivera@740c.test");
        var driverBToken = await LoginAsync("driverb@740c.test");

        // 1. Cross-facility supervisor & client denied 403 even with MultiFacilityView permission
        using (var rCrossFac = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/assign-driver", supBToken, new { driverUserId = DriverAId })))
            Assert.Equal(HttpStatusCode.Forbidden, rCrossFac.StatusCode);

        using (var rClient = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/assign-driver", clientToken, new { driverUserId = DriverAId })))
            Assert.Equal(HttpStatusCode.Forbidden, rClient.StatusCode);

        // 2. Assigning non-Driver user returns 400; assigning driver scoped to foreign client returns 403
        using (var rNotDriver = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/assign-driver", supAToken, new { driverUserId = ClientAdminAId })))
            Assert.Equal(HttpStatusCode.BadRequest, rNotDriver.StatusCode);

        using (var rForeignDriver = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/assign-driver", supAToken, new { driverUserId = DriverForeignClientId })))
            Assert.Equal(HttpStatusCode.Forbidden, rForeignDriver.StatusCode);

        // 3. Stale ExpectedVersion returns 409 Conflict; valid assign bumps Version to 2
        using (var rStale = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/assign-driver", supAToken, new { driverUserId = DriverAId, expectedVersion = 99 })))
            Assert.Equal(HttpStatusCode.Conflict, rStale.StatusCode);

        using (var rOkAssign = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/assign-driver", supAToken, new { driverUserId = DriverAId, expectedVersion = 1, reason = "Route A" })))
        {
            Assert.Equal(HttpStatusCode.OK, rOkAssign.StatusCode);
            var assignRes = await rOkAssign.Content.ReadFromJsonAsync<AssignShipmentDriverResult>(JsonOptions);
            Assert.NotNull(assignRes);
            Assert.Equal(DriverAId, assignRes!.AssignedDriverUserId);
            Assert.Equal(2u, assignRes.Version);
        }

        // Assigned driver CANNOT confirm POD/delivery before Shipment is Dispatched (403 Forbidden)
        using (var rPreDispatchPod = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{PkgDraftId}/confirm-delivery", driverAToken, new { })))
            Assert.Equal(HttpStatusCode.Forbidden, rPreDispatchPod.StatusCode);

        // 4. Dispatch Custody Transfer binds AssignedDriverUserId immutably onto ShipmentCustodyTransfer
        using (var rMismatchDispatch = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/shipments/{ShipmentDraftId}/dispatch", supAToken, new
            {
                driverName = "Driver Beta",
                driverSignature = "SIG-BETA",
                assignedDriverUserId = DriverBId
            })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rMismatchDispatch.StatusCode);

        using (var rDispatch = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/shipments/{ShipmentDraftId}/dispatch", supAToken, new
            {
                driverName = "Driver Alpha",
                driverSignature = "SIG-ALPHA"
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rDispatch.StatusCode);
            var dispRes = await rDispatch.Content.ReadFromJsonAsync<ConfirmDispatchCustodyResult>(JsonOptions);
            Assert.Equal(DriverAId, dispRes!.AssignedDriverUserId);
            Assert.Equal(ShipmentStatus.Dispatched, dispRes.Status);
        }

        // 5. Reassigning/revoking already-dispatched shipment WITHOUT explicit audit reason is rejected (422), WITH reason succeeds and revokes old driver
        using (var rUnsafeRevoke = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/assign-driver", supAToken, new { driverUserId = DriverBId })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rUnsafeRevoke.StatusCode);

        using (var rAuditedReassign = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/assign-driver", supAToken, new
            {
                driverUserId = DriverBId,
                reason = "Vehicle breakdown handover to Driver Beta"
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rAuditedReassign.StatusCode);
            var reassignRes = await rAuditedReassign.Content.ReadFromJsonAsync<AssignShipmentDriverResult>(JsonOptions);
            Assert.Equal(ShipmentCustodyStatus.PendingHandover, reassignRes!.CustodyStatus);
        }

        // Old driver (DriverA) is now 403 Forbidden; new driver (DriverB) ALSO gets 403 until explicit custody handover acknowledgment
        using (var rOldDriver = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{PkgDraftId}/confirm-delivery", driverAToken, new { })))
            Assert.Equal(HttpStatusCode.Forbidden, rOldDriver.StatusCode);

        using (var rPendingNewDriver = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{PkgDraftId}/pod", driverBToken, new { recipientName = "Before Ack" })))
            Assert.Equal(HttpStatusCode.Forbidden, rPendingNewDriver.StatusCode);

        // Non-driver, wrong driver, empty/synthetic signature, and stale version rejected on acknowledge-custody
        using (var rStaffAck = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/acknowledge-custody", supAToken, new { driverSignature = "SIG-STAFF" })))
            Assert.Equal(HttpStatusCode.Forbidden, rStaffAck.StatusCode);

        using (var rWrongDriverAck = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/acknowledge-custody", driverAToken, new { driverSignature = "SIG-WRONG-DRV" })))
            Assert.Equal(HttpStatusCode.Forbidden, rWrongDriverAck.StatusCode);

        using (var rSyntheticSig = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/acknowledge-custody", driverBToken, new { driverSignature = "SUPERVISOR_CUSTODY_REASSIGNMENT:fake" })))
            Assert.Equal(HttpStatusCode.BadRequest, rSyntheticSig.StatusCode);

        using (var rStaleAck = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/acknowledge-custody", driverBToken, new { driverSignature = "SIG-DRIVER-B-REAL", expectedVersion = 999u })))
            Assert.Equal(HttpStatusCode.Conflict, rStaleAck.StatusCode);

        // Authentic DriverB acknowledges handover -> CustodyStatus becomes Confirmed, preserving DriverA's original immutable transfer row
        using (var rAckOk = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/acknowledge-custody", driverBToken, new { driverSignature = "SIG-DRIVER-B-REAL", driverLicenseNumber = "DL-BETA-01" })))
        {
            Assert.Equal(HttpStatusCode.OK, rAckOk.StatusCode);
            var ackRes = await rAckOk.Content.ReadFromJsonAsync<AcknowledgeShipmentCustodyResult>(JsonOptions);
            Assert.Equal(ShipmentCustodyStatus.Confirmed, ackRes!.CustodyStatus);
            Assert.Equal(DriverBId, ackRes.AssignedDriverUserId);
        }

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var xfers = await db.ShipmentCustodyTransfers.IgnoreQueryFilters()
                .Where(t => t.ShipmentId == ShipmentDraftId)
                .OrderBy(t => t.TransferredAtUtc)
                .ToListAsync();
            Assert.Equal(2, xfers.Count);
            Assert.Equal(DriverAId, xfers[0].AssignedDriverUserId);
            Assert.Equal("SIG-ALPHA", xfers[0].DriverSignatureAcknowledgment);
            Assert.Equal(DriverBId, xfers[1].AssignedDriverUserId);
            Assert.Equal("SIG-DRIVER-B-REAL", xfers[1].DriverSignatureAcknowledgment);
            Assert.All(xfers, t => Assert.Equal(ShipmentCustodyStatus.Confirmed, t.CustodyStatus));
        }

        // DriverB can now submit DriverHandheld POD cleanly
        using (var rDriverBPodAfterAck = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{PkgDraftId}/pod", driverBToken, new { recipientName = "Consignee After Handover", evidenceSource = PodEvidenceSource.DriverHandheld })))
            Assert.Equal(HttpStatusCode.OK, rDriverBPodAfterAck.StatusCode);

        // Revoking driver (DriverUserId = null with reason) preserves history but blocks DriverB immediately
        using (var rRevoke = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDraftId}/assign-driver", supAToken, new { driverUserId = (Guid?)null, reason = "Held at depot" })))
        {
            Assert.Equal(HttpStatusCode.OK, rRevoke.StatusCode);
            var revRes = await rRevoke.Content.ReadFromJsonAsync<AssignShipmentDriverResult>(JsonOptions);
            Assert.Equal(ShipmentCustodyStatus.Revoked, revRes!.CustodyStatus);
        }

        using (var rRevokedDriver = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{PkgDraftId}/confirm-delivery", driverBToken, new { })))
            Assert.Equal(HttpStatusCode.Forbidden, rRevokedDriver.StatusCode);

        // Client denied 403 on courier/shipments; revoked DriverB no longer sees ShipmentDraftId
        using (var rClientCourier = await _http.SendAsync(BearerRequest(HttpMethod.Get,
            "/api/v1/outbound/dispatch/courier/shipments", clientToken)))
            Assert.Equal(HttpStatusCode.Forbidden, rClientCourier.StatusCode);

        using (var rDriverBList = await _http.SendAsync(BearerRequest(HttpMethod.Get,
            "/api/v1/outbound/dispatch/courier/shipments", driverBToken)))
        {
            Assert.Equal(HttpStatusCode.OK, rDriverBList.StatusCode);
            var list = await rDriverBList.Content.ReadFromJsonAsync<List<DispatchBoardShipmentDto>>(JsonOptions);
            Assert.NotNull(list);
            Assert.DoesNotContain(list!, s => s.ShipmentId == ShipmentDraftId);
        }

        using (var rDriverAList = await _http.SendAsync(BearerRequest(HttpMethod.Get,
            "/api/v1/outbound/dispatch/courier/shipments", driverAToken)))
        {
            Assert.Equal(HttpStatusCode.OK, rDriverAList.StatusCode);
            var list = await rDriverAList.Content.ReadFromJsonAsync<List<DispatchBoardShipmentDto>>(JsonOptions);
            Assert.NotNull(list);
            Assert.Contains(list!, s => s.ShipmentId == ShipmentDispatchedId);
            Assert.All(list!, s => Assert.Equal(DriverAId, s.AssignedDriverUserId));
        }
    }

    [Fact]
    public async Task CourierPodUpload_AuthBeforeStorage_DeliveryConfirmation_OrderRollup_And_ZeroStockDelta()
    {
        var driverAToken = await LoginAsync("drivera@740c.test");
        var driverBToken = await LoginAsync("driverb@740c.test");
        var clientToken = await LoginAsync("clienta@740c.test");
        var supAToken = await LoginAsync("supa@740c.test");

        // 1. Unassigned DriverB & Client get 403 BEFORE any storage object is created; MultiFacility Staff in FacA still 403 on FacB package
        using (var rClientPod = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", clientToken, new { recipientName = "Spoofed" })))
            Assert.Equal(HttpStatusCode.Forbidden, rClientPod.StatusCode);

        using (var rWrongDriverPod = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverBToken, new { recipientName = "Wrong Driver" })))
            Assert.Equal(HttpStatusCode.Forbidden, rWrongDriverPod.StatusCode);

        using (var rCrossFacStaffPod = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{PkgFacBId}/pod", supAToken, new { evidenceSource = PodEvidenceSource.ManualVerification, verificationReason = "Cross Fac" })))
            Assert.Equal(HttpStatusCode.Forbidden, rCrossFacStaffPod.StatusCode);

        var finalFilesBefore = Directory.GetFiles(_storageDir, "*", SearchOption.AllDirectories).Length;
        Assert.Equal(0, finalFilesBefore);

        // 2. Reject SVG evidence upload from authorized DriverA
        using (var svgForm = new MultipartFormDataContent())
        {
            var svgBytes = new ByteArrayContent(Encoding.UTF8.GetBytes("<svg/>"));
            svgBytes.Headers.ContentType = new MediaTypeHeaderValue("image/svg+xml");
            svgForm.Add(svgBytes, "signature", "sig.svg");
            svgForm.Add(new StringContent("Jane Doe"), "recipientName");
            using var svgReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod") { Content = svgForm };
            svgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverAToken);
            using var rSvg = await _http.SendAsync(svgReq);
            Assert.Equal(HttpStatusCode.BadRequest, rSvg.StatusCode);
        }

        // 3. Valid multipart/form-data POD upload for Pkg1 by assigned DriverA
        Guid pod1Id;
        using (var form = new MultipartFormDataContent())
        {
            var sigContent = new ByteArrayContent(CreateValidPngBytes());
            sigContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(sigContent, "signature", "sig.png");

            var photoContent = new ByteArrayContent(CreateValidJpgBytes());
            photoContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(photoContent, "photo", "door.jpg");

            form.Add(new StringContent("Jane Consignee"), "recipientName");
            form.Add(new StringContent("Front Reception"), "deliveryLocationDescription");
            form.Add(new StringContent("true"), "isSigned");
            form.Add(new StringContent("IDEMP-POD-740C-1"), "idempotencyKey");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod") { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverAToken);
            using var rPodOk = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, rPodOk.StatusCode);
            var podRes = await rPodOk.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            Assert.NotNull(podRes);
            Assert.True(podRes!.IsStorageFinalized);
            Assert.NotEmpty(podRes.PreSignedSignatureUrl!);
            Assert.NotEmpty(podRes.PreSignedPhotoUrl!);
            pod1Id = podRes.ProofOfDeliveryId;
        }

        // Confirm delivery of Pkg1 -> Pkg1 is Delivered, OrderA still PartiallyDelivered / Dispatched because Pkg2 is not yet delivered
        using (var rConf1 = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/confirm-delivery", driverAToken, new { proofOfDeliveryId = pod1Id })))
        {
            Assert.Equal(HttpStatusCode.OK, rConf1.StatusCode);
            var conf1 = await rConf1.Content.ReadFromJsonAsync<DeliveryConfirmationResult>(JsonOptions);
            Assert.Equal(TrackingMilestoneStatus.Delivered, conf1!.PackageTrackingStatus);
            Assert.NotEqual(OrderDeliveryStatus.Delivered, conf1.OrderDeliveryStatus);
        }

        // Duplicate confirm-delivery on Pkg1 is idempotent
        using (var rConf1Dup = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/confirm-delivery", driverAToken, new { })))
        {
            Assert.Equal(HttpStatusCode.OK, rConf1Dup.StatusCode);
            var dup = await rConf1Dup.Content.ReadFromJsonAsync<DeliveryConfirmationResult>(JsonOptions);
            Assert.True(dup!.IsAlreadyDelivered);
        }

        // Submit POD + confirm delivery on Pkg2 + PkgDraft? Wait: PkgDraft is ALSO on OrderA! Let's check: OrderA has PkgDraft (Draft shipment), Pkg1, Pkg2.
        // Client read endpoints verify timeline & POD visibility
        using (var rPodView = await _http.SendAsync(BearerRequest(HttpMethod.Get,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", clientToken)))
        {
            Assert.Equal(HttpStatusCode.OK, rPodView.StatusCode);
            var podDto = await rPodView.Content.ReadFromJsonAsync<ProofOfDeliveryDto>(JsonOptions);
            Assert.Equal("Jane Consignee", podDto!.RecipientName);
        }

        // Verify zero inventory/stock mutation occurred across all courier POD & confirmation writes
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stock = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.ItemId == ItemA);
            Assert.Equal(100m, stock.QtyAvailable);
            Assert.Equal(10m, stock.QtyAllocated);
            Assert.Empty(await db.InventoryTransactions.IgnoreQueryFilters().Where(t => t.ClientId == ClientA).ToListAsync());
        }
    }

    [Fact]
    public async Task ReadScoping_DriverExceptionAttribution_SupervisorWorkflows_And_SignedWebhooks()
    {
        var supAToken = await LoginAsync("supa@740c.test");
        var supBToken = await LoginAsync("supb@740c.test");
        var clientAToken = await LoginAsync("clienta@740c.test");
        var driverAToken = await LoginAsync("drivera@740c.test");

        // 1. ClientA gets 404 concealment on foreign tenant package/order/POD and 403 on revoked FacB
        using (var rForeignPkg = await _http.SendAsync(BearerRequest(HttpMethod.Get,
            $"/api/v1/outbound/dispatch/packages/{PkgForeignBId}/timeline", clientAToken)))
            Assert.Equal(HttpStatusCode.NotFound, rForeignPkg.StatusCode);

        using (var rForeignOrd = await _http.SendAsync(BearerRequest(HttpMethod.Get,
            $"/api/v1/outbound/dispatch/orders/{OrderB}/delivery-status", clientAToken)))
            Assert.Equal(HttpStatusCode.NotFound, rForeignOrd.StatusCode);

        using (var rRevokedFac = await _http.SendAsync(BearerRequest(HttpMethod.Get,
            $"/api/v1/outbound/dispatch/orders/{OrderClientAFacB}/delivery-status", clientAToken)))
            Assert.Equal(HttpStatusCode.Forbidden, rRevokedFac.StatusCode);

        using (var rBoardClient = await _http.SendAsync(BearerRequest(HttpMethod.Get,
            "/api/v1/outbound/dispatch/board", clientAToken)))
        {
            Assert.Equal(HttpStatusCode.OK, rBoardClient.StatusCode);
            var board = await rBoardClient.Content.ReadFromJsonAsync<PagedResult<DispatchBoardShipmentDto>>(JsonOptions);
            Assert.NotNull(board);
            Assert.All(board!.Items, s =>
            {
                Assert.Equal(ClientA, s.ClientId);
                Assert.Equal(FacilityA, s.FacilityId);
            });
        }

        // 2. DriverA reports exception (spoofed driverUserId ignored; authenticated DriverAId attributed)
        Guid excId;
        using (var rDriverExc = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            "/api/v1/outbound/dispatch/exceptions/driver-report", driverAToken, new
            {
                packageId = Pkg2DispatchedId,
                exceptionType = DeliveryExceptionType.RecipientUnavailable,
                driverNotes = "Gate locked, no answer",
                driverUserId = DriverBId
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rDriverExc.StatusCode);
            var excRes = await rDriverExc.Content.ReadFromJsonAsync<DeliveryExceptionResult>(JsonOptions);
            excId = excRes!.ExceptionId;
        }

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var exc = await db.ShipmentDeliveryExceptions.IgnoreQueryFilters().SingleAsync(e => e.Id == excId);
            Assert.Equal(DriverAId, exc.ReportedByUserId);
        }

        // Cross-facility supervisor denied 403 on FacA exception management
        using (var rCrossSup = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/exceptions/{excId}/contact", supBToken, new
            {
                contactMethod = CustomerContactMethod.PhoneCall,
                outcome = CustomerContactOutcome.ReachedAddressCorrected,
                notes = "Cross-fac attempt"
            })))
            Assert.Equal(HttpStatusCode.Forbidden, rCrossSup.StatusCode);

        // Authorized supervisor FacA executes contact -> reattempt -> reschedule -> resolve -> reopen -> void
        using (var rContact = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/exceptions/{excId}/contact", supAToken, new
            {
                contactMethod = CustomerContactMethod.PhoneCall,
                outcome = CustomerContactOutcome.ReachedAddressCorrected,
                notes = "Customer gave gate code"
            })))
            Assert.Equal(HttpStatusCode.OK, rContact.StatusCode);

        using (var rResolve = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/exceptions/{excId}/resolve", supAToken, new
            {
                resolution = DeliveryExceptionResolution.Resolved,
                resolutionNotes = "Gate code confirmed"
            })))
            Assert.Equal(HttpStatusCode.OK, rResolve.StatusCode);

        using (var rSync = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDispatchedId}/sync-tracking", supAToken)))
            Assert.Equal(HttpStatusCode.OK, rSync.StatusCode);

        // 3. Signed Carrier Webhooks: carrier-tracking & carrier-exception + replay + mismatch + bad signature
        var trackPayload = new
        {
            carrierCode = "SIM-FEDEX",
            trackingNumber = "TRK-740C-2",
            carrierEventId = "EVT-740C-TRK-1",
            carrierEventCode = "IT",
            carrierDescription = "Arrived at local sort facility",
            occurredAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        var nonceTrack = $"nonce-trk-{Guid.NewGuid():N}";
        using (var rTrackOk = await _http.SendAsync(SignedWebhook(
            "/api/v1/outbound/dispatch/webhooks/carrier-tracking", trackPayload, "SIM-FEDEX", FedExSecret, nonceTrack)))
            Assert.Equal(HttpStatusCode.OK, rTrackOk.StatusCode);

        // Exact nonce replay is rejected as ReplayRejected (401 Unauthorized)
        using (var rTrackReplay = await _http.SendAsync(SignedWebhook(
            "/api/v1/outbound/dispatch/webhooks/carrier-tracking", trackPayload, "SIM-FEDEX", FedExSecret, nonceTrack)))
            Assert.Equal(HttpStatusCode.Unauthorized, rTrackReplay.StatusCode);

        // Fresh nonce with same carrierEventId is idempotent at domain layer (200 OK, isDuplicate = true)
        using (var rTrackDupEvent = await _http.SendAsync(SignedWebhook(
            "/api/v1/outbound/dispatch/webhooks/carrier-tracking", trackPayload, "SIM-FEDEX", FedExSecret, $"nonce-trk-dup-{Guid.NewGuid():N}")))
        {
            Assert.Equal(HttpStatusCode.OK, rTrackDupEvent.StatusCode);
            var dupRes = await rTrackDupEvent.Content.ReadFromJsonAsync<IngestTrackingEventResult>(JsonOptions);
            Assert.True(dupRes!.IsDuplicate);
        }

        // Invalid signature -> 401
        using (var rBadSig = await _http.SendAsync(SignedWebhook(
            "/api/v1/outbound/dispatch/webhooks/carrier-tracking", trackPayload, "SIM-FEDEX", "wrong-secret", $"nonce-bad-{Guid.NewGuid():N}")))
            Assert.Equal(HttpStatusCode.Unauthorized, rBadSig.StatusCode);

        // Carrier-exception webhook with valid HMAC -> 200; mismatched payload carrier -> 401 (CarrierMismatch)
        var excPayload = new
        {
            carrierCode = "SIM-FEDEX",
            trackingNumber = "TRK-740C-B1",
            carrierEventId = "EVT-740C-EXC-1",
            carrierExceptionCode = "WEA01",
            carrierDescription = "Weather delay in transit",
            occurredAtUtc = DateTime.UtcNow.AddMinutes(-3)
        };
        using (var rExcOk = await _http.SendAsync(SignedWebhook(
            "/api/v1/outbound/dispatch/webhooks/carrier-exception", excPayload, "SIM-FEDEX", FedExSecret, $"nonce-exc-{Guid.NewGuid():N}")))
            Assert.Equal(HttpStatusCode.OK, rExcOk.StatusCode);

        var mismatchedPayload = new
        {
            carrierCode = "SIM-UPS",
            trackingNumber = "TRK-740C-2",
            carrierEventCode = "IT",
            carrierDescription = "Spoofed carrier",
            occurredAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        using (var rMismatchCarrier = await _http.SendAsync(SignedWebhook(
            "/api/v1/outbound/dispatch/webhooks/carrier-tracking", mismatchedPayload, "SIM-FEDEX", FedExSecret, $"nonce-mis-{Guid.NewGuid():N}")))
            Assert.Equal(HttpStatusCode.Unauthorized, rMismatchCarrier.StatusCode);
    }

    [Fact]
    public async Task PodEvidenceSourceAuthorization_DriverCannotSubmitSpoofedSources_And_StaffCannotSubmitIntegrationSourcesInteractively()
    {
        var driverAToken = await LoginAsync("drivera@740c.test");
        var supAToken = await LoginAsync("supa@740c.test");

        // 1. Driver A with confirmed custody CANNOT submit ManualVerification, CarrierApi, or ConsigneePortal via JSON or Multipart
        foreach (var badPayload in new object[]
        {
            new { evidenceSource = PodEvidenceSource.ManualVerification, verificationReason = "Spoofed manual override", recipientName = "Consignee" },
            new { evidenceSource = PodEvidenceSource.CarrierApi, carrierConfirmationNumber = "FDX-SPOOF-1", recipientName = "Consignee" },
            new { evidenceSource = PodEvidenceSource.ConsigneePortal, consigneeAcknowledgmentText = "Spoofed portal ack", recipientName = "Consignee" }
        })
        {
            using var resp = await _http.SendAsync(BearerRequest(HttpMethod.Post,
                $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverAToken, badPayload));
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        using (var mpSpoof = new MultipartFormDataContent())
        {
            var png = new ByteArrayContent(CreateValidPngBytes());
            png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            mpSpoof.Add(png, "signature", "spoof.png");
            mpSpoof.Add(new StringContent(nameof(PodEvidenceSource.ManualVerification)), "evidenceSource");
            mpSpoof.Add(new StringContent("Spoofed via form"), "verificationReason");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod") { Content = mpSpoof };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverAToken);
            using var rMp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, rMp.StatusCode);
        }

        // 2. Staff on interactive courier endpoint CANNOT submit CarrierApi or ConsigneePortal; ManualVerification requires reason
        foreach (var staffBadSource in new object[]
        {
            new { evidenceSource = PodEvidenceSource.CarrierApi, carrierConfirmationNumber = "FDX-STAFF-SPOOF", recipientName = "Consignee" },
            new { evidenceSource = PodEvidenceSource.ConsigneePortal, consigneeAcknowledgmentText = "Portal spoof", recipientName = "Consignee" }
        })
        {
            using var rStaffBad = await _http.SendAsync(BearerRequest(HttpMethod.Post,
                $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", supAToken, staffBadSource));
            Assert.Equal(HttpStatusCode.Forbidden, rStaffBad.StatusCode);
        }

        using (var rMissingReason = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", supAToken,
            new { evidenceSource = PodEvidenceSource.ManualVerification, recipientName = "No reason" })))
            Assert.Equal(HttpStatusCode.BadRequest, rMissingReason.StatusCode);

        // Zero POD rows or storage artifacts created from all rejected attempts
        Assert.Empty(Directory.GetFiles(_storageDir, "*", SearchOption.AllDirectories));

        // Authorized Staff CAN execute ManualVerification with auditable reason
        using (var rStaffManualOk = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", supAToken,
            new { evidenceSource = PodEvidenceSource.ManualVerification, verificationReason = "Dispatcher verified signed paper manifest", recipientName = "Dock Clerk" })))
            Assert.Equal(HttpStatusCode.OK, rStaffManualOk.StatusCode);

        // Service-layer enforcement (direct ProofOfDeliveryService): Driver cannot submit ManualVerification / CarrierApi / ConsigneePortal
        using (var scope = _app.Services.CreateScope())
        {
            var scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
            scopedTenant.SetTenantId(ClientA);
            var svcDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var svcStorage = scope.ServiceProvider.GetRequiredService<IObjectStorageService>();
            var drvCtx = new Warehouse3PL.UnitTests.Common.TestCurrentUserContext { UserId = DriverAId, UserType = UserType.Driver };
            var directSvc = new ProofOfDeliveryService(svcDb, svcStorage, scopedTenant, drvCtx, Microsoft.Extensions.Logging.Abstractions.NullLogger<ProofOfDeliveryService>.Instance);

            foreach (var badSrc in new[] { PodEvidenceSource.ManualVerification, PodEvidenceSource.CarrierApi, PodEvidenceSource.ConsigneePortal })
            {
                await Assert.ThrowsAsync<Warehouse3PL.Domain.Exceptions.ForbiddenException>(() => directSvc.SubmitProofOfDeliveryAsync(new SubmitProofOfDeliveryRequest
                {
                    PackageId = Pkg2DispatchedId,
                    EvidenceSource = badSrc,
                    VerificationReason = "Direct spoof",
                    CarrierConfirmationNumber = "FDX-DIRECT",
                    ConsigneeAcknowledgmentText = "Portal direct spoof",
                    RecipientName = "Direct"
                }));
            }
        }
    }

    [Fact]
    public async Task CustodyRevocationRace_BothCompletionOrders_EnforceFencingAndCleanStagedStorage()
    {
        var supAToken = await LoginAsync("supa@740c.test");
        var driverAToken = await LoginAsync("drivera@740c.test");

        // Order 1: Revocation commits WHILE DriverA POD upload is in-flight after staging temp file -> must fail 403 + delete temp file
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _raceStorageState.NextStageHook = (entered, release);

        Task<HttpResponseMessage> podInFlightTask;
        using (var form = new MultipartFormDataContent())
        {
            var sig = new ByteArrayContent(CreateValidPngBytes());
            sig.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(sig, "signature", "race-sig.png");
            form.Add(new StringContent("Race Consignee"), "recipientName");
            form.Add(new StringContent("true"), "isSigned");
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod") { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverAToken);
            podInFlightTask = _http.SendAsync(req);

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // While DriverA pauses post-staging, supervisor revokes DriverA custody on ShipmentDispatchedId
            using (var rRevoke = await _http.SendAsync(BearerRequest(HttpMethod.Post,
                $"/api/v1/outbound/dispatch/shipments/{ShipmentDispatchedId}/assign-driver", supAToken,
                new { driverUserId = (Guid?)null, reason = "Emergency recall during route" })))
                Assert.Equal(HttpStatusCode.OK, rRevoke.StatusCode);

            release.TrySetResult(true);
            using var podResp = await podInFlightTask;
            Assert.Equal(HttpStatusCode.Forbidden, podResp.StatusCode);
        }

        // Verify zero POD persisted and zero orphaned temp/final storage files remain
        Assert.Empty(Directory.GetFiles(_storageDir, "*", SearchOption.AllDirectories));
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.ProofOfDeliveries.IgnoreQueryFilters().Where(p => p.PackageId == Pkg1DispatchedId).ToListAsync());
        }

        // Order 2 (overlapping reverse race via DB barrier): Re-assign DriverA -> DriverA acknowledges -> POD submitted
        // ConfirmDelivery holds Shipment -> Package locks across barrier WHILE concurrent revocation waits on the lock -> ConfirmDelivery commits -> revocation rejected (422)
        using (var rReassign = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDispatchedId}/assign-driver", supAToken,
            new { driverUserId = DriverAId, reason = "Re-dispatched after recall" })))
            Assert.Equal(HttpStatusCode.OK, rReassign.StatusCode);

        using (var rAck = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDispatchedId}/acknowledge-custody", driverAToken,
            new { driverSignature = "SIG-DRIVER-A-RESUMED" })))
            Assert.Equal(HttpStatusCode.OK, rAck.StatusCode);

        using (var rPodCommit = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverAToken,
            new { recipientName = "Committed Consignee", evidenceSource = PodEvidenceSource.DriverHandheld })))
            Assert.Equal(HttpStatusCode.OK, rPodCommit.StatusCode);

        var confirmEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _raceDbBarrier.ArmOnPodConfirmedSave(confirmEntered, confirmRelease);

        var confirmTask = _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/confirm-delivery", driverAToken, new { }));
        await confirmEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var concurrentRevokeTask = _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/shipments/{ShipmentDispatchedId}/assign-driver", supAToken,
            new { driverUserId = (Guid?)null, reason = "Competing revoke while confirm holds lock" }));
        await Task.Delay(150);
        Assert.False(concurrentRevokeTask.IsCompleted);

        confirmRelease.TrySetResult(true);
        using (var rConfirmCommit = await confirmTask)
            Assert.Equal(HttpStatusCode.OK, rConfirmCommit.StatusCode);

        using (var rCompRevoke = await concurrentRevokeTask)
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rCompRevoke.StatusCode);

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pkg1 = await db.Packages.IgnoreQueryFilters().SingleAsync(p => p.Id == Pkg1DispatchedId);
            Assert.Equal(TrackingMilestoneStatus.Delivered, pkg1.CurrentTrackingStatus);
            Assert.Single(await db.ProofOfDeliveries.IgnoreQueryFilters().Where(p => p.PackageId == Pkg1DispatchedId && !p.IsSuperseded).ToListAsync());
        }
    }

    [Fact]
    public async Task PodSupersession_AtomicUpdateOrdering_Concurrency_Auth_And_RollbackRecovery()
    {
        var supAToken = await LoginAsync("supa@740c.test");
        var supBToken = await LoginAsync("supb@740c.test");
        var driverAToken = await LoginAsync("drivera@740c.test");
        var clientAdminToken = await LoginAsync("clienta@740c.test");

        // 1. Driver creates initial finalized POD1 (with PNG signature)
        Guid pod1Id;
        using (var form = new MultipartFormDataContent())
        {
            var sig = new ByteArrayContent(CreateValidPngBytes());
            sig.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(sig, "signature", "orig-sig.png");
            form.Add(new StringContent("Original Consignee"), "recipientName");
            form.Add(new StringContent("true"), "isSigned");
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod") { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverAToken);
            using var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            pod1Id = doc.RootElement.GetProperty("proofOfDeliveryId").GetGuid();
        }

        // 2. Authorization / facility / evidence-source negative checks on supersede endpoint
        using (var rDrv = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/dispatch/pod/{pod1Id}/supersede", driverAToken,
            new { reason = "Driver attempt", replacementSubmission = new { evidenceSource = PodEvidenceSource.DriverHandheld, recipientName = "X" } })))
            Assert.Equal(HttpStatusCode.Forbidden, rDrv.StatusCode);

        using (var rFacB = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/dispatch/pod/{pod1Id}/supersede", supBToken,
            new { reason = "Wrong fac attempt", replacementSubmission = new { evidenceSource = PodEvidenceSource.ManualVerification, verificationReason = "Wrong fac", recipientName = "X" } })))
            Assert.Equal(HttpStatusCode.Forbidden, rFacB.StatusCode);

        using (var rClient = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/dispatch/pod/{pod1Id}/supersede", clientAdminToken,
            new { reason = "Client attempt", replacementSubmission = new { evidenceSource = PodEvidenceSource.ConsigneePortal, consigneeAcknowledgmentText = "Portal" } })))
            Assert.Equal(HttpStatusCode.Forbidden, rClient.StatusCode);

        using (var rBadSrc = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/dispatch/pod/{pod1Id}/supersede", supAToken,
            new { reason = "CarrierApi spoof", replacementSubmission = new { evidenceSource = PodEvidenceSource.CarrierApi, carrierConfirmationNumber = "FDX-SPOOF" } })))
            Assert.Equal(HttpStatusCode.Forbidden, rBadSrc.StatusCode);

        using (var rMissingVerify = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/dispatch/pod/{pod1Id}/supersede", supAToken,
            new { reason = "Missing verify reason", replacementSubmission = new { evidenceSource = PodEvidenceSource.ManualVerification, recipientName = "Y" } })))
            Assert.Equal(HttpStatusCode.BadRequest, rMissingVerify.StatusCode);

        // 3. Failure recovery: failure on INSERT replacement (save #2 after old POD marked superseded on save #1) restores old POD + cleans temp storage
        var pngB64 = Convert.ToBase64String(CreateValidPngBytes());
        _raceDbBarrier.ThrowOnNthSave(2, new InvalidOperationException("Simulated replacement insert failure"));
        using (var msFail = new MemoryStream(CreateValidPngBytes()))
        {
            using var scope = _app.Services.CreateScope();
            var scopedTenant = scope.ServiceProvider.GetRequiredService<ITenantProvider>();
            scopedTenant.SetTenantId(ClientA);
            var scopedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var scopedStorage = scope.ServiceProvider.GetRequiredService<IObjectStorageService>();
            var staffCtx = new Warehouse3PL.UnitTests.Common.TestCurrentUserContext { UserId = SupervisorFacAId, UserType = UserType.Staff, FacilityId = FacilityA };
            var svc = new ProofOfDeliveryService(scopedDb, scopedStorage, scopedTenant, staffCtx, Microsoft.Extensions.Logging.Abstractions.NullLogger<ProofOfDeliveryService>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SupersedeProofOfDeliveryAsync(new SupersedeProofOfDeliveryRequest
            {
                ExistingPodId = pod1Id,
                Reason = "Rollback test",
                ReplacementSubmission = new SubmitProofOfDeliveryRequest
                {
                    PackageId = Pkg1DispatchedId,
                    EvidenceSource = PodEvidenceSource.ManualVerification,
                    VerificationReason = "Rollback verification",
                    RecipientName = "Should Not Persist",
                    IsSigned = true,
                    SignatureStream = msFail,
                    SignatureContentType = "image/png"
                }
            }));
        }

        Assert.DoesNotContain(Directory.GetFiles(_storageDir, "*", SearchOption.AllDirectories), f => Path.GetFileName(f).StartsWith("temp_", StringComparison.OrdinalIgnoreCase));
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pods = await db.ProofOfDeliveries.IgnoreQueryFilters().Where(p => p.PackageId == Pkg1DispatchedId).ToListAsync();
            var oldRestored = Assert.Single(pods);
            Assert.Equal(pod1Id, oldRestored.Id);
            Assert.False(oldRestored.IsSuperseded);
            Assert.Null(oldRestored.SupersededByPodId);
        }

        // 4. Concurrent overlapping supersessions: pause Worker 1 post-staging -> fire Worker 2 -> release -> exactly 1 succeeds (200), 1 rejected (422)
        var sEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _raceStorageState.NextStageHook = (sEntered, sRelease);

        var t1 = _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/dispatch/pod/{pod1Id}/supersede", supAToken, new
        {
            reason = "Supervisor verified corrected POD #1",
            replacementSubmission = new
            {
                evidenceSource = PodEvidenceSource.ManualVerification,
                verificationReason = "Supervisor paper audit #1",
                recipientName = "Corrected Consignee #1",
                isSigned = true,
                signatureBase64 = pngB64,
                signatureContentType = "image/png"
            }
        }));
        await sEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var t2 = _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/dispatch/pod/{pod1Id}/supersede", supAToken, new
        {
            reason = "Competing supersession #2",
            replacementSubmission = new
            {
                evidenceSource = PodEvidenceSource.ManualVerification,
                verificationReason = "Competing audit #2",
                recipientName = "Competing Consignee #2",
                isSigned = true,
                signatureBase64 = pngB64,
                signatureContentType = "image/png"
            }
        }));

        sRelease.TrySetResult(true);
        var responses = await Task.WhenAll(t1, t2);
        var okResp = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var rejectedResp = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        rejectedResp.Dispose();

        Guid winningPodId;
        using (var winDoc = await JsonDocument.ParseAsync(await okResp.Content.ReadAsStreamAsync()))
        {
            winningPodId = winDoc.RootElement.GetProperty("proofOfDeliveryId").GetGuid();
            Assert.True(winDoc.RootElement.GetProperty("isStorageFinalized").GetBoolean());
        }
        okResp.Dispose();

        // Duplicate sequential supersession rejected
        using (var rDup = await _http.SendAsync(BearerRequest(HttpMethod.Post, $"/api/v1/outbound/dispatch/pod/{pod1Id}/supersede", supAToken, new
        {
            reason = "Sequential duplicate supersession",
            replacementSubmission = new
            {
                evidenceSource = PodEvidenceSource.ManualVerification,
                verificationReason = "Duplicate check",
                recipientName = "Duplicate"
            }
        })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rDup.StatusCode);

        // Verify exact DB state + zero orphaned temp storage files
        Assert.DoesNotContain(Directory.GetFiles(_storageDir, "*", SearchOption.AllDirectories), f => Path.GetFileName(f).StartsWith("temp_", StringComparison.OrdinalIgnoreCase));
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pods = await db.ProofOfDeliveries.IgnoreQueryFilters().Where(p => p.PackageId == Pkg1DispatchedId).ToListAsync();
            Assert.Equal(2, pods.Count);
            var oldRow = Assert.Single(pods, p => p.Id == pod1Id);
            var newRow = Assert.Single(pods, p => p.Id == winningPodId);
            Assert.True(oldRow.IsSuperseded);
            Assert.Equal(winningPodId, oldRow.SupersededByPodId);
            Assert.NotNull(oldRow.SupersededAtUtc);
            Assert.False(string.IsNullOrWhiteSpace(oldRow.SupersededReason));
            Assert.False(newRow.IsSuperseded);
            Assert.True(newRow.IsStorageFinalized);
        }

        // Delivery confirmation rejects superseded POD (422) and accepts replacement POD (200)
        using (var rOldConfirm = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/confirm-delivery", driverAToken,
            new { proofOfDeliveryId = pod1Id })))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rOldConfirm.StatusCode);

        using (var rNewConfirm = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/confirm-delivery", driverAToken,
            new { proofOfDeliveryId = winningPodId })))
        {
            Assert.Equal(HttpStatusCode.OK, rNewConfirm.StatusCode);
            using var cDoc = await JsonDocument.ParseAsync(await rNewConfirm.Content.ReadAsStreamAsync());
            Assert.Equal(winningPodId, cDoc.RootElement.GetProperty("proofOfDeliveryId").GetGuid());
        }
    }

    [Fact]
    public async Task P745R01_R02_PodSignedDerivation_And_IdempotencyPayloadConflict_ReturnExpectedOutcomes()
    {
        var driverAToken = await LoginAsync("drivera@740c.test");
        var clientToken = await LoginAsync("clienta@740c.test");

        // P745-R01: Recipient-only submission (even with spoofed isSigned:true & no signature file) persists IsSigned = false
        Guid unsignedPodId;
        using (var rNameOnly = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Alice Unsigned",
                deliveryLocationDescription = "Front Desk",
                isSigned = true,
                idempotencyKey = "p745-idemp-unsigned"
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rNameOnly.StatusCode);
            var res = await rNameOnly.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            Assert.NotNull(res);
            Assert.False(res!.IsSigned);
            Assert.False(res.IsDuplicate);
            Assert.Null(res.SignatureSha256);
            unsignedPodId = res.ProofOfDeliveryId;
        }

        using (var rGetPod1 = await _http.SendAsync(BearerRequest(HttpMethod.Get,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", clientToken)))
        {
            Assert.Equal(HttpStatusCode.OK, rGetPod1.StatusCode);
            var dto1 = await rGetPod1.Content.ReadFromJsonAsync<ProofOfDeliveryDto>(JsonOptions);
            Assert.NotNull(dto1);
            Assert.False(dto1!.IsSigned);
            Assert.Null(dto1.SignatureSha256);
        }

        // P745-R02: Identical replay on Pkg1 returns 200 idempotently with same POD ID & Alice Unsigned
        using (var rReplayOk = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Alice Unsigned",
                deliveryLocationDescription = "Front Desk",
                idempotencyKey = "p745-idemp-unsigned"
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rReplayOk.StatusCode);
            var replay = await rReplayOk.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            Assert.True(replay!.IsDuplicate);
            Assert.Equal(unsignedPodId, replay.ProofOfDeliveryId);
            Assert.Equal("Alice Unsigned", replay.RecipientName);
        }

        // P745-R02: Retrying same key with changed recipient OR added signature -> 409 Conflict, zero mutation
        using (var rConflictRecipient = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Bob Changed",
                deliveryLocationDescription = "Front Desk",
                idempotencyKey = "p745-idemp-unsigned"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rConflictRecipient.StatusCode);

        using (var rConflictSig = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Alice Unsigned",
                deliveryLocationDescription = "Front Desk",
                signatureBase64 = Convert.ToBase64String(CreateValidPngBytes()),
                signatureContentType = "image/png",
                idempotencyKey = "p745-idemp-unsigned"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rConflictSig.StatusCode);

        // Cross-package reuse of already-consumed IdempotencyKey -> 409 Conflict
        using (var rCrossPkgKey = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Alice Unsigned",
                deliveryLocationDescription = "Front Desk",
                idempotencyKey = "p745-idemp-unsigned"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rCrossPkgKey.StatusCode);

        // P745-R01 + R02 on Pkg2: Genuine signature + photo upload persists IsSigned = true, rejects altered photo on same key with 409
        var png1 = CreateValidPngBytes();
        var jpg1 = CreateValidJpgBytes();
        var jpg2 = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x02, 0xFF, 0xD9 };

        Guid signedPodId;
        using (var rSigned = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Charlie Signed",
                signatureBase64 = Convert.ToBase64String(png1),
                signatureContentType = "image/png",
                photoBase64 = Convert.ToBase64String(jpg1),
                photoContentType = "image/jpeg",
                idempotencyKey = "p745-idemp-signed"
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rSigned.StatusCode);
            var signedRes = await rSigned.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            Assert.True(signedRes!.IsSigned);
            Assert.NotNull(signedRes.SignatureSha256);
            Assert.NotNull(signedRes.PhotoSha256);
            signedPodId = signedRes.ProofOfDeliveryId;
        }

        // Identical binary replay returns 200 idempotently
        using (var rSignedReplay = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Charlie Signed",
                signatureBase64 = Convert.ToBase64String(png1),
                signatureContentType = "image/png",
                photoBase64 = Convert.ToBase64String(jpg1),
                photoContentType = "image/jpeg",
                idempotencyKey = "p745-idemp-signed"
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rSignedReplay.StatusCode);
            var rep = await rSignedReplay.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            Assert.True(rep!.IsDuplicate);
            Assert.Equal(signedPodId, rep.ProofOfDeliveryId);
        }

        // Different photo bytes under same key -> 409 Conflict
        using (var rDiffPhoto = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Charlie Signed",
                signatureBase64 = Convert.ToBase64String(png1),
                signatureContentType = "image/png",
                photoBase64 = Convert.ToBase64String(jpg2),
                photoContentType = "image/jpeg",
                idempotencyKey = "p745-idemp-signed"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rDiffPhoto.StatusCode);
    }

    [Fact]
    public async Task P745R02_CanonicalCaseIdempotency_Timestamps_AuditFields_And_HistoricalKeyPolicy()
    {
        var supAToken = await LoginAsync("supa@740c.test");
        var driverAToken = await LoginAsync("drivera@740c.test");

        // 1. Canonical case-insensitive key & omitted vs explicit deliveredAtUtc on Pkg1
        Guid pkg1PodId;
        using (var rInitialOmittedTs = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Case Replay Alice",
                idempotencyKey = "p745-case-alpha"
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rInitialOmittedTs.StatusCode);
            var created = await rInitialOmittedTs.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            Assert.False(created!.HasExplicitDeliveredAt);
            pkg1PodId = created.ProofOfDeliveryId;
        }

        // Case-variant replay omitting deliveredAtUtc -> 200 OK (IsDuplicate = true)
        using (var rCaseReplay = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Case Replay Alice",
                idempotencyKey = "  P745-CASE-ALPHA  "
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rCaseReplay.StatusCode);
            var replay = await rCaseReplay.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            Assert.True(replay!.IsDuplicate);
            Assert.Equal(pkg1PodId, replay.ProofOfDeliveryId);
        }

        // Same case-variant key retrying with explicit deliveredAtUtc when original omitted it -> 409 Conflict
        using (var rOmittedVsExplicit = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Case Replay Alice",
                deliveredAtUtc = DateTime.UtcNow.AddMinutes(-2),
                idempotencyKey = "P745-Case-Alpha"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rOmittedVsExplicit.StatusCode);

        // Case-variant reuse on different package (Pkg2) -> 409 Conflict
        using (var rCrossPkgCase = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Case Replay Alice",
                idempotencyKey = "P745-CASE-ALPHA"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rCrossPkgCase.StatusCode);

        // 2. ManualVerification: verificationReason + explicit deliveredAtUtc bound to idempotency key on Pkg2
        var explicitDeliveryTs = new DateTime(2026, 11, 24, 9, 30, 0, DateTimeKind.Utc);
        Guid pkg2PodId;
        using (var rManualInit = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", supAToken, new
            {
                evidenceSource = PodEvidenceSource.ManualVerification,
                verificationReason = "Carrier dock manifest #881",
                recipientName = "Dock Clerk Dan",
                deliveredAtUtc = explicitDeliveryTs,
                idempotencyKey = "p745-manual-audit"
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rManualInit.StatusCode);
            var mRes = await rManualInit.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            Assert.True(mRes!.HasExplicitDeliveredAt);
            Assert.Equal("Carrier dock manifest #881", mRes.VerificationReason);
            pkg2PodId = mRes.ProofOfDeliveryId;
        }

        // Identical explicit timestamp + verificationReason with uppercase key -> 200 OK
        using (var rManualReplay = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", supAToken, new
            {
                evidenceSource = PodEvidenceSource.ManualVerification,
                verificationReason = "Carrier dock manifest #881",
                recipientName = "Dock Clerk Dan",
                deliveredAtUtc = explicitDeliveryTs,
                idempotencyKey = "P745-MANUAL-AUDIT"
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rManualReplay.StatusCode);
            var replay = await rManualReplay.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            Assert.True(replay!.IsDuplicate);
            Assert.Equal(pkg2PodId, replay.ProofOfDeliveryId);
        }

        // Changed verificationReason -> 409 Conflict
        using (var rDiffReason = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", supAToken, new
            {
                evidenceSource = PodEvidenceSource.ManualVerification,
                verificationReason = "Altered audit justification #999",
                recipientName = "Dock Clerk Dan",
                deliveredAtUtc = explicitDeliveryTs,
                idempotencyKey = "P745-MANUAL-AUDIT"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rDiffReason.StatusCode);

        // Changed explicit deliveredAtUtc -> 409 Conflict; omitted deliveredAtUtc when original was explicit -> 409 Conflict
        using (var rDiffTs = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", supAToken, new
            {
                evidenceSource = PodEvidenceSource.ManualVerification,
                verificationReason = "Carrier dock manifest #881",
                recipientName = "Dock Clerk Dan",
                deliveredAtUtc = explicitDeliveryTs.AddMinutes(-15),
                idempotencyKey = "P745-MANUAL-AUDIT"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rDiffTs.StatusCode);

        using (var rExplicitToOmitted = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", supAToken, new
            {
                evidenceSource = PodEvidenceSource.ManualVerification,
                verificationReason = "Carrier dock manifest #881",
                recipientName = "Dock Clerk Dan",
                idempotencyKey = "P745-MANUAL-AUDIT"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rExplicitToOmitted.StatusCode);

        // 3. Historical key policy across supersession: keys are permanently consumed in the facility
        // Superseding pkg1PodId with the already-consumed historical/other key must fail with 409 Conflict
        using (var rSupReuseOwnKey = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/pod/{pkg1PodId}/supersede", supAToken, new
            {
                reason = "Attempt to reuse original key",
                replacementSubmission = new
                {
                    evidenceSource = PodEvidenceSource.ManualVerification,
                    verificationReason = "Audit check",
                    recipientName = "New Name",
                    idempotencyKey = "p745-case-alpha"
                }
            })))
            Assert.Equal(HttpStatusCode.Conflict, rSupReuseOwnKey.StatusCode);

        Guid supWinningPodId;
        using (var rSupOk = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/pod/{pkg1PodId}/supersede", supAToken, new
            {
                reason = "Corrected signed manifest",
                replacementSubmission = new
                {
                    evidenceSource = PodEvidenceSource.ManualVerification,
                    verificationReason = "Paper manifest verified",
                    recipientName = "Corrected Alice",
                    deliveredAtUtc = explicitDeliveryTs,
                    idempotencyKey = "p745-sup-key-1"
                }
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rSupOk.StatusCode);
            var supRes = await rSupOk.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            supWinningPodId = supRes!.ProofOfDeliveryId;
        }

        // Retrying the exact supersession idempotently returns 200 OK; changing verificationReason returns 409 Conflict
        using (var rSupReplay = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/pod/{pkg1PodId}/supersede", supAToken, new
            {
                reason = "Corrected signed manifest",
                replacementSubmission = new
                {
                    evidenceSource = PodEvidenceSource.ManualVerification,
                    verificationReason = "Paper manifest verified",
                    recipientName = "Corrected Alice",
                    deliveredAtUtc = explicitDeliveryTs,
                    idempotencyKey = "P745-SUP-KEY-1"
                }
            })))
        {
            Assert.Equal(HttpStatusCode.OK, rSupReplay.StatusCode);
            var replay = await rSupReplay.Content.ReadFromJsonAsync<SubmitProofOfDeliveryResult>(JsonOptions);
            Assert.True(replay!.IsDuplicate);
            Assert.Equal(supWinningPodId, replay.ProofOfDeliveryId);
        }

        using (var rSupConflict = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/pod/{pkg1PodId}/supersede", supAToken, new
            {
                reason = "Corrected signed manifest",
                replacementSubmission = new
                {
                    evidenceSource = PodEvidenceSource.ManualVerification,
                    verificationReason = "Tampered paper manifest",
                    recipientName = "Corrected Alice",
                    deliveredAtUtc = explicitDeliveryTs,
                    idempotencyKey = "P745-SUP-KEY-1"
                }
            })))
            Assert.Equal(HttpStatusCode.Conflict, rSupConflict.StatusCode);

        // Historical key 'p745-case-alpha' from the now-superseded pkg1PodId cannot be reused on Pkg1 or Pkg2 -> 409 Conflict
        using (var rHistSamePkg = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg1DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Case Replay Alice",
                idempotencyKey = "P745-CASE-ALPHA"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rHistSamePkg.StatusCode);

        using (var rHistOtherPkg = await _http.SendAsync(BearerRequest(HttpMethod.Post,
            $"/api/v1/outbound/dispatch/packages/{Pkg2DispatchedId}/pod", driverAToken, new
            {
                recipientName = "Different Pkg",
                idempotencyKey = "P745-CASE-ALPHA"
            })))
            Assert.Equal(HttpStatusCode.Conflict, rHistOtherPkg.StatusCode);
    }

    private sealed class RaceDbBarrierInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private (TaskCompletionSource<bool> Entered, TaskCompletionSource<bool> Release)? _podConfirmPause;
        private int _throwCountdown;
        private Exception? _throwException;

        public void ArmOnPodConfirmedSave(TaskCompletionSource<bool> entered, TaskCompletionSource<bool> release)
            => _podConfirmPause = (entered, release);

        public void ThrowOnNthSave(int nthSave, Exception ex)
        {
            _throwException = ex;
            Volatile.Write(ref _throwCountdown, nthSave);
        }

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var pause = _podConfirmPause;
            if (pause.HasValue && eventData.Context != null &&
                eventData.Context.ChangeTracker.Entries<ShipmentTrackingEvent>()
                    .Any(e => e.State == EntityState.Added && e.Entity.CarrierEventCode == "POD_CONFIRMED"))
            {
                _podConfirmPause = null;
                pause.Value.Entered.TrySetResult(true);
                await pause.Value.Release.Task;
            }

            if (Volatile.Read(ref _throwCountdown) > 0 && Interlocked.Decrement(ref _throwCountdown) == 0 && _throwException != null)
            {
                var ex = _throwException;
                _throwException = null;
                throw ex;
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class RaceStorageState
    {
        public (TaskCompletionSource<bool> Entered, TaskCompletionSource<bool> Release)? NextStageHook { get; set; }
    }

    private sealed class RaceControllableObjectStorageService(
        PrivateObjectStorageService inner,
        RaceStorageState state) : IObjectStorageService
    {
        public async Task<StagedUploadResult> UploadStagedAsync(string tempObjectKey, Stream contentStream, string contentType, CancellationToken cancellationToken = default)
        {
            var res = await inner.UploadStagedAsync(tempObjectKey, contentStream, contentType, cancellationToken);
            var hook = state.NextStageHook;
            if (hook.HasValue)
            {
                state.NextStageHook = null;
                hook.Value.Entered.TrySetResult(true);
                await hook.Value.Release.Task;
            }
            return res;
        }

        public Task<ObjectStorageMetadata> FinalizeObjectAsync(string tempObjectKey, string finalObjectKey, CancellationToken cancellationToken = default)
            => inner.FinalizeObjectAsync(tempObjectKey, finalObjectKey, cancellationToken);
        public Task<Stream?> DownloadAsync(string objectKey, CancellationToken cancellationToken = default)
            => inner.DownloadAsync(objectKey, cancellationToken);
        public Task<PreSignedUrlResult> GeneratePreSignedDownloadUrlAsync(string objectKey, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
            => inner.GeneratePreSignedDownloadUrlAsync(objectKey, expiry, cancellationToken);
        public bool ValidatePreSignedToken(string objectKey, string token, DateTime utcNow)
            => inner.ValidatePreSignedToken(objectKey, token, utcNow);
        public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(objectKey, cancellationToken);
        public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
            => inner.ExistsAsync(objectKey, cancellationToken);
        public Task<ObjectStorageMetadata?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken = default)
            => inner.GetMetadataAsync(objectKey, cancellationToken);
        public Task<int> CleanupOrphanedTempObjectsAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
            => inner.CleanupOrphanedTempObjectsAsync(olderThan, cancellationToken);
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
        if (Directory.Exists(_storageDir))
        {
            try { Directory.Delete(_storageDir, recursive: true); } catch { /* ignore */ }
        }
    }
}
