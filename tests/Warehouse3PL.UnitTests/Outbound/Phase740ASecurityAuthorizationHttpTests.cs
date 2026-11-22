using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
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
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public sealed class Phase740ASecurityAuthorizationHttpTests : IAsyncLifetime
{
    private static readonly Guid ClientA = Guid.Parse("740a0001-0000-0000-0000-000000000001");
    private static readonly Guid ClientB = Guid.Parse("740a0001-0000-0000-0000-000000000002");
    private static readonly Guid FacilityA = Guid.Parse("740a0002-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("740a0002-0000-0000-0000-000000000002");

    private static readonly Guid StaffOpAId = Guid.Parse("740a0003-0000-0000-0000-000000000001");
    private static readonly Guid StaffOpBId = Guid.Parse("740a0003-0000-0000-0000-000000000002");

    private static readonly Guid ItemA = Guid.Parse("740a0004-0000-0000-0000-000000000001");
    private static readonly Guid BinA = Guid.Parse("740a0005-0000-0000-0000-000000000001");
    private static readonly Guid OrderClientAFacA = Guid.Parse("740a0006-0000-0000-0000-000000000001");
    private static readonly Guid OrderClientAFacB = Guid.Parse("740a0006-0000-0000-0000-000000000002");
    private static readonly Guid OrderClientBFacA = Guid.Parse("740a0006-0000-0000-0000-000000000003");
    private static readonly Guid OrderCancelableClientA = Guid.Parse("740a0006-0000-0000-0000-000000000004");

    private static readonly Guid WaveFacA = Guid.Parse("740a0007-0000-0000-0000-000000000001");
    private static readonly Guid WaveFacB = Guid.Parse("740a0007-0000-0000-0000-000000000002");
    private static readonly Guid TaskAssignedOpAFacA = Guid.Parse("740a0008-0000-0000-0000-000000000001");
    private static readonly Guid TaskFacB = Guid.Parse("740a0008-0000-0000-0000-000000000002");

    private static readonly Guid PackageClientAFacA = Guid.Parse("740a0009-0000-0000-0000-000000000001");
    private static readonly Guid PackageClientAFacB = Guid.Parse("740a0009-0000-0000-0000-000000000002");
    private static readonly Guid PackageClientBFacA = Guid.Parse("740a0009-0000-0000-0000-000000000003");

    private static readonly Guid ShipmentFacA = Guid.Parse("740a000a-0000-0000-0000-000000000001");
    private static readonly Guid ShipmentFacB = Guid.Parse("740a000a-0000-0000-0000-000000000002");
    private static readonly Guid BolFacA = Guid.Parse("740a000b-0000-0000-0000-000000000001");
    private static readonly Guid BolFacB = Guid.Parse("740a000b-0000-0000-0000-000000000002");
    private static readonly Guid LabelFacA = Guid.Parse("740a000c-0000-0000-0000-000000000001");
    private static readonly Guid LabelFacB = Guid.Parse("740a000c-0000-0000-0000-000000000002");

    private const string PrimarySecretActiveCarrier = "unit-test-fedex-primary-secret-001";
    private const string SecondarySecretActiveCarrier = "unit-test-fedex-secondary-secret-002";
    private const string ExpiredSecondarySecret = "unit-test-ups-expired-secondary-999";
    private const string ReturnTrackingNumber = "TRK-RTS-740A-0001";

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CarrierWebhooks:TimestampToleranceSeconds"] = "300",
            ["CarrierWebhooks:MaxPayloadBytes"] = "1024",
            ["CarrierWebhooks:Carriers:SIM-FEDEX:PrimarySecret"] = PrimarySecretActiveCarrier,
            ["CarrierWebhooks:Carriers:SIM-FEDEX:SecondarySecret"] = SecondarySecretActiveCarrier,
            ["CarrierWebhooks:Carriers:SIM-FEDEX:SecondarySecretExpiresAtUtc"] = DateTime.UtcNow.AddHours(2).ToString("O"),
            ["CarrierWebhooks:Carriers:SIM-UPS:PrimarySecret"] = "unit-test-ups-primary-secret-001",
            ["CarrierWebhooks:Carriers:SIM-UPS:SecondarySecret"] = ExpiredSecondarySecret,
            ["CarrierWebhooks:Carriers:SIM-UPS:SecondarySecretExpiresAtUtc"] = DateTime.UtcNow.AddMinutes(-10).ToString("O")
        });

        builder.Services.Configure<CarrierWebhookOptions>(
            builder.Configuration.GetSection(CarrierWebhookOptions.SectionName));

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(OutboundOrdersController).Assembly)
            .AddApplicationPart(typeof(Phase740ASecurityAuthorizationHttpTests).Assembly);
        builder.Services.AddApplicationServices();
        builder.Services.AddAuthentication("WarehouseTest")
            .AddScheme<AuthenticationSchemeOptions, OutboundTestAuthentication>("WarehouseTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        var testClock = new ControllableClock();
        builder.Services.AddSingleton(testClock);
        builder.Services.AddSingleton<IDateTimeProvider>(testClock);
        builder.Services.AddScoped<AuditableEntityInterceptor>();

        // Outbound infrastructure services exercised by controllers/handlers
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
        builder.Services.AddScoped<ReturnTrackingService>();
        builder.Services.AddScoped<IReturnTrackingService, FaultInjectableReturnTrackingService>();
        builder.Services.AddSingleton<WebhookProbeState>();

        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) => options
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        _app = builder.Build();
        _app.UseMiddleware<ExceptionHandlingMiddleware>();
        _app.UseRouting();
        _app.UseAuthenticatedTenantResolution();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _http = _app.GetTestClient();

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedData(db);
        await db.SaveChangesAsync();
    }

    private static void SeedData(ApplicationDbContext db)
    {
        db.Clients.AddRange(
            new Client { Id = ClientA, CompanyName = "Apex Logistics Client", AccountCode = "APX-A" },
            new Client { Id = ClientB, CompanyName = "Beacon Global Client", AccountCode = "BCN-B" });

        db.Facilities.AddRange(
            new Facility { Id = FacilityA, FacilityCode = "ORD-A", Name = "Chicago Hub A", AddressLine1 = "100 Main", City = "Chicago", CountryCode = "US", IsActive = true },
            new Facility { Id = FacilityB, FacilityCode = "DFW-B", Name = "Dallas Hub B", AddressLine1 = "200 South", City = "Dallas", CountryCode = "US", IsActive = true });

        // Client A has Active access to Facility A ONLY (revoked in Facility B)
        db.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityA, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityB, Status = FacilityAccessStatus.Revoked },
            new ClientFacilityAccess { ClientId = ClientB, FacilityId = FacilityA, Status = FacilityAccessStatus.Active });

        db.Items.Add(new Item { Id = ItemA, ClientId = ClientA, Sku = "SKU-740A-1", Description = "Widget A" });
        db.Locations.Add(new Location { Id = BinA, FacilityId = FacilityA, LocationCode = "A-01-01", Barcode = "BIN-A-01", LocationType = LocationType.Bin, IsActive = true, IsPickable = true });

        db.OutboundOrders.AddRange(
            new OutboundOrder { Id = OrderClientAFacA, ClientId = ClientA, FacilityId = FacilityA, OrderNumber = "SO-740A-AA", CustomerName = "Cust A", Status = OutboundOrderStatus.Released },
            new OutboundOrder { Id = OrderCancelableClientA, ClientId = ClientA, FacilityId = FacilityA, OrderNumber = "SO-740A-CAN", CustomerName = "Cust Cancel", Status = OutboundOrderStatus.Draft },
            new OutboundOrder { Id = OrderClientAFacB, ClientId = ClientA, FacilityId = FacilityB, OrderNumber = "SO-740A-AB", CustomerName = "Cust AB", Status = OutboundOrderStatus.Released },
            new OutboundOrder { Id = OrderClientBFacA, ClientId = ClientB, FacilityId = FacilityA, OrderNumber = "SO-740A-BA", CustomerName = "Cust BA", Status = OutboundOrderStatus.Released });

        db.PickWaves.AddRange(
            new PickWave { Id = WaveFacA, ClientId = ClientA, FacilityId = FacilityA, WaveNumber = "WV-A-01", Status = PickWaveStatus.Planning },
            new PickWave { Id = WaveFacB, ClientId = ClientA, FacilityId = FacilityB, WaveNumber = "WV-B-01", Status = PickWaveStatus.Planning });

        db.PickTasks.AddRange(
            new PickTask
            {
                Id = TaskAssignedOpAFacA,
                ClientId = ClientA,
                FacilityId = FacilityA,
                PickWaveId = WaveFacA,
                OutboundOrderId = OrderClientAFacA,
                ItemId = ItemA,
                SourceLocationId = BinA,
                QuantityToPick = 5m,
                PickSequence = 1,
                Status = PickTaskStatus.Assigned,
                AssignedUserId = StaffOpAId
            },
            new PickTask
            {
                Id = TaskFacB,
                ClientId = ClientA,
                FacilityId = FacilityB,
                PickWaveId = WaveFacB,
                OutboundOrderId = OrderClientAFacB,
                ItemId = ItemA,
                SourceLocationId = BinA,
                QuantityToPick = 5m,
                PickSequence = 1,
                Status = PickTaskStatus.Pending
            });

        db.Shipments.AddRange(
            new Shipment { Id = ShipmentFacA, ClientId = ClientA, FacilityId = FacilityA, ShipmentNumber = "SHP-740A-A", CarrierCode = "SIM-FEDEX", Status = ShipmentStatus.Draft },
            new Shipment { Id = ShipmentFacB, ClientId = ClientA, FacilityId = FacilityB, ShipmentNumber = "SHP-740A-B", CarrierCode = "SIM-FEDEX", Status = ShipmentStatus.Draft });

        db.BillsOfLading.AddRange(
            new BillOfLading { Id = BolFacA, ClientId = ClientA, FacilityId = FacilityA, ShipmentId = ShipmentFacA, BolNumber = "BOL-740A-A", CarrierCode = "SIM-FEDEX", CarrierName = "SimFedEx", ServiceLevel = "StandardGround" },
            new BillOfLading { Id = BolFacB, ClientId = ClientA, FacilityId = FacilityB, ShipmentId = ShipmentFacB, BolNumber = "BOL-740A-B", CarrierCode = "SIM-FEDEX", CarrierName = "SimFedEx", ServiceLevel = "StandardGround" });

        db.Packages.AddRange(
            new Package { Id = PackageClientAFacA, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = OrderClientAFacA, ShipmentId = ShipmentFacA, PackageNumber = "PKG-740A-AA", CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-PKG-AA", Status = PackageStatus.LabelPrinted },
            new Package { Id = PackageClientAFacB, ClientId = ClientA, FacilityId = FacilityB, OutboundOrderId = OrderClientAFacB, ShipmentId = ShipmentFacB, PackageNumber = "PKG-740A-AB", CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-PKG-AB", Status = PackageStatus.LabelPrinted },
            new Package { Id = PackageClientBFacA, ClientId = ClientB, FacilityId = FacilityA, OutboundOrderId = OrderClientBFacA, PackageNumber = "PKG-740A-BA", CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-PKG-BA", Status = PackageStatus.LabelPrinted });

        db.ShippingLabels.AddRange(
            new ShippingLabel { Id = LabelFacA, PackageId = PackageClientAFacA, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-PKG-AA", LabelData = "^XA^XZ" },
            new ShippingLabel { Id = LabelFacB, PackageId = PackageClientAFacB, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-PKG-AB", LabelData = "^XA^XZ" });

        var authId = Guid.NewGuid();
        var auth = ReturnAuthorization.Create(
            clientId: ClientA,
            originFacilityId: FacilityA,
            destinationFacilityId: FacilityA,
            orderId: OrderClientAFacA,
            originalShipmentId: ShipmentFacA,
            packageId: PackageClientAFacA,
            deliveryExceptionId: Guid.NewGuid(),
            authorizationNumber: "RTS-740A-001",
            reason: ReturnReason.CustomerRefused,
            notes: "Test RTS",
            authorizedByUserId: StaffOpAId,
            rmaNumber: "RMA-740A-01");
        var returnShipmentId = Guid.NewGuid();
        auth.LinkReturnShipment(returnShipmentId);
        db.ReturnAuthorizations.Add(auth);

        db.ReturnShipments.Add(new ReturnShipment
        {
            Id = returnShipmentId,
            ClientId = ClientA,
            OriginFacilityId = FacilityA,
            DestinationFacilityId = FacilityA,
            OrderId = OrderClientAFacA,
            OriginalShipmentId = ShipmentFacA,
            PackageId = PackageClientAFacA,
            DeliveryExceptionId = auth.DeliveryExceptionId,
            ReturnAuthorizationId = auth.Id,
            ReturnRmaNumber = auth.RmaNumber!,
            Reason = ReturnReason.CustomerRefused,
            AuthorizedByUserId = StaffOpAId,
            AuthorizedAtUtc = DateTime.UtcNow,
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = ReturnTrackingNumber,
            CarrierServiceLevel = "StandardGround",
            Status = ReturnShipmentStatus.Authorized
        });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. Staff-only Physical Execution Enforcement (ClientAdmin w/ legacy claims & Driver denied)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LegacyClientAdmin_HoldingPhysicalClaims_IsRejectedOnWarehouseExecution_ThroughHttp()
    {
        var requests = new[]
        {
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/orders/{OrderClientAFacA}/allocate", "client-admin-legacy", new { }),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/orders/{OrderClientAFacA}/deallocate", "client-admin-legacy", new { reason = "test" }),
            JsonRequest(HttpMethod.Patch, $"/api/v1/outbound/orders/{OrderClientAFacA}/hold", "client-admin-legacy", new { reason = "test" }),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/pick-waves", "client-admin-legacy", new { request = new { clientId = ClientA, facilityId = FacilityA, orderIds = new[] { OrderClientAFacA } } }),
            Request(HttpMethod.Get, $"/api/v1/outbound/pick-waves?facilityId={FacilityA}", "client-admin-legacy"),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskAssignedOpAFacA}/confirm", "client-admin-legacy", new { scannedLocationBarcode = "BIN-A-01", scannedItemBarcode = "SKU-740A-1", scannedToteLpn = "TOTE-1", quantity = 1m }),
            Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/{TaskAssignedOpAFacA}", "client-admin-legacy"),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/packing/sessions/start", "client-admin-legacy", new { facilityId = FacilityA, packingStationLocationId = BinA, outboundOrderId = OrderClientAFacA }),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/packing/cartonization/recommend", "client-admin-legacy", new { clientId = ClientA, facilityId = FacilityA, outboundOrderId = OrderClientAFacA }),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/shipments", "client-admin-legacy", new { clientId = ClientA, facilityId = FacilityA, carrierCode = "SIM-FEDEX", serviceLevel = 1 }),
            Request(HttpMethod.Get, $"/api/v1/outbound/shipments/{ShipmentFacA}", "client-admin-legacy"),
            Request(HttpMethod.Get, $"/api/v1/outbound/shipments/bol/{BolFacA}", "client-admin-legacy"),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/shipping/rates", "client-admin-legacy", new { packageId = PackageClientAFacA }),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/shipping/labels/generate", "client-admin-legacy", new { packageId = PackageClientAFacA, carrierCode = "SIM-FEDEX", serviceLevel = 1 }),
            Request(HttpMethod.Get, $"/api/v1/outbound/shipping/labels/{LabelFacA}", "client-admin-legacy"),
        };

        foreach (var req in requests)
        {
            using (req)
            using (var resp = await _http.SendAsync(req))
            {
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            }
        }
    }

    [Fact]
    public async Task DriverIdentity_EvenWithLegacyOutboundClaims_IsRejectedAcrossOutboundEndpoints_ThroughHttp()
    {
        var requests = new[]
        {
            Request(HttpMethod.Get, $"/api/v1/outbound/orders?facilityId={FacilityA}", "driver-legacy"),
            Request(HttpMethod.Get, $"/api/v1/outbound/orders/{OrderClientAFacA}", "driver-legacy"),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskAssignedOpAFacA}/confirm", "driver-legacy", new { scannedLocationBarcode = "BIN-A-01", scannedItemBarcode = "SKU-740A-1", scannedToteLpn = "TOTE-1", quantity = 1m }),
            Request(HttpMethod.Get, $"/api/v1/outbound/shipments/{ShipmentFacA}", "driver-legacy"),
            Request(HttpMethod.Get, $"/api/v1/outbound/shipping/packages/{PackageClientAFacA}/track", "driver-legacy"),
        };

        foreach (var req in requests)
        {
            using (req)
            using (var resp = await _http.SendAsync(req))
            {
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            }
        }
    }

    [Fact]
    public async Task LegacyClientAdmin_PackageTrackingTransition_AllowsOwnActiveFacilityAndConcealsForeignTenant()
    {
        // Own active facility package -> 200 OK (temporary transition until 7.4.0C dispatch:view)
        using (var ownReq = Request(HttpMethod.Get, $"/api/v1/outbound/shipping/packages/{PackageClientAFacA}/track", "client-admin-legacy"))
        using (var ownResp = await _http.SendAsync(ownReq))
        {
            Assert.Equal(HttpStatusCode.OK, ownResp.StatusCode);
        }

        // Foreign tenant package -> 404 NotFound concealment
        using (var foreignReq = Request(HttpMethod.Get, $"/api/v1/outbound/shipping/packages/{PackageClientBFacA}/track", "client-admin-legacy"))
        using (var foreignResp = await _http.SendAsync(foreignReq))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignResp.StatusCode);
        }

        // Suspended ClientFacilityAccess (FacilityB) -> 403 Forbidden
        using (var suspendedReq = Request(HttpMethod.Get, $"/api/v1/outbound/shipping/packages/{PackageClientAFacB}/track", "client-admin-legacy"))
        using (var suspendedResp = await _http.SendAsync(suspendedReq))
        {
            Assert.Equal(HttpStatusCode.Forbidden, suspendedResp.StatusCode);
        }
    }

    [Fact]
    public async Task PostReconciliationClientAdmin_TracksOwnPackageViaDispatchView_AndIsDeniedShippingMutations()
    {
        // Reconciled ClientAdmin holds dispatch:view + outbound:orders:* WITHOUT outbound:ship
        using (var ownReq = Request(HttpMethod.Get, $"/api/v1/outbound/shipping/packages/{PackageClientAFacA}/track", "client-admin-reconciled"))
        using (var ownResp = await _http.SendAsync(ownReq))
        {
            Assert.Equal(HttpStatusCode.OK, ownResp.StatusCode);
        }

        // Foreign tenant package -> 404 NotFound concealment
        using (var foreignReq = Request(HttpMethod.Get, $"/api/v1/outbound/shipping/packages/{PackageClientBFacA}/track", "client-admin-reconciled"))
        using (var foreignResp = await _http.SendAsync(foreignReq))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignResp.StatusCode);
        }

        // Revoked facility package -> 403 Forbidden
        using (var revokedReq = Request(HttpMethod.Get, $"/api/v1/outbound/shipping/packages/{PackageClientAFacB}/track", "client-admin-reconciled"))
        using (var revokedResp = await _http.SendAsync(revokedReq))
        {
            Assert.Equal(HttpStatusCode.Forbidden, revokedResp.StatusCode);
        }

        // dispatch:view MUST NOT grant shipping mutations, rates, labels, or shipments
        var deniedShippingRequests = new[]
        {
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/shipping/rates", "client-admin-reconciled", new { packageId = PackageClientAFacA }),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/shipping/labels/generate", "client-admin-reconciled", new { packageId = PackageClientAFacA, carrierCode = "SIM-FEDEX", serviceLevel = 1 }),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/shipping/labels/{LabelFacA}/void", "client-admin-reconciled", new { reason = "test" }),
            Request(HttpMethod.Get, $"/api/v1/outbound/shipping/labels/{LabelFacA}", "client-admin-reconciled"),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/shipments", "client-admin-reconciled", new { clientId = ClientA, facilityId = FacilityA, carrierCode = "SIM-FEDEX", serviceLevel = 1 }),
            Request(HttpMethod.Get, $"/api/v1/outbound/shipments/{ShipmentFacA}", "client-admin-reconciled"),
            Request(HttpMethod.Get, $"/api/v1/outbound/shipments/bol/{BolFacA}", "client-admin-reconciled")
        };

        foreach (var req in deniedShippingRequests)
        {
            using (req)
            using (var resp = await _http.SendAsync(req))
            {
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            }
        }

        // Staff with outbound:ship retains tracking capability in assigned facility
        using (var staffTrackReq = Request(HttpMethod.Get, $"/api/v1/outbound/shipping/packages/{PackageClientAFacA}/track", "staff-supervisor-facA"))
        using (var staffTrackResp = await _http.SendAsync(staffTrackReq))
        {
            Assert.Equal(HttpStatusCode.OK, staffTrackResp.StatusCode);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. Facility Scoping, Unassigned Staff, MultiFacilityView Read-Only vs Mutation, Strict SuperAdmin
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Staff_WithoutAssignedFacility_IsRejectedOnOutboundEndpoints_ThroughHttp()
    {
        using var req1 = Request(HttpMethod.Get, $"/api/v1/outbound/orders/{OrderClientAFacA}", "staff-unassigned");
        using var resp1 = await _http.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Forbidden, resp1.StatusCode);

        using var req2 = JsonRequest(HttpMethod.Post, $"/api/v1/outbound/orders/{OrderClientAFacA}/allocate", "staff-unassigned", new { });
        using var resp2 = await _http.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Forbidden, resp2.StatusCode);
    }

    [Fact]
    public async Task Staff_MultiFacilityView_PermitsCrossFacilityReads_ButStillRejectsCrossFacilityMutations()
    {
        // Staff WITHOUT MultiFacilityView is denied FacilityB reads -> 403
        foreach (var path in new[]
        {
            $"/api/v1/outbound/orders?facilityId={FacilityB}",
            $"/api/v1/outbound/orders/{OrderClientAFacB}",
            $"/api/v1/outbound/pick-waves/{WaveFacB}",
            $"/api/v1/outbound/pick-tasks/{TaskFacB}",
            $"/api/v1/outbound/shipments/{ShipmentFacB}",
            $"/api/v1/outbound/shipments/bol/{BolFacB}",
            $"/api/v1/outbound/shipping/labels/{LabelFacB}"
        })
        {
            using var deniedReq = Request(HttpMethod.Get, path, "staff-supervisor-facA");
            using var deniedResp = await _http.SendAsync(deniedReq);
            Assert.Equal(HttpStatusCode.Forbidden, deniedResp.StatusCode);
        }

        // Staff WITH MultiFacilityView IS allowed FacilityB reads -> 200
        foreach (var path in new[]
        {
            $"/api/v1/outbound/orders?facilityId={FacilityB}",
            $"/api/v1/outbound/orders/{OrderClientAFacB}",
            $"/api/v1/outbound/pick-waves/{WaveFacB}",
            $"/api/v1/outbound/pick-tasks/{TaskFacB}",
            $"/api/v1/outbound/shipments/{ShipmentFacB}",
            $"/api/v1/outbound/shipments/bol/{BolFacB}",
            $"/api/v1/outbound/shipping/labels/{LabelFacB}"
        })
        {
            using var allowedReq = Request(HttpMethod.Get, path, "staff-multifac-reader-facA");
            using var allowedResp = await _http.SendAsync(allowedReq);
            Assert.Equal(HttpStatusCode.OK, allowedResp.StatusCode);
        }

        // Staff WITH MultiFacilityView is STILL REJECTED on FacilityB mutations -> 403
        var mutationRequests = new[]
        {
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/orders/{OrderClientAFacB}/allocate", "staff-multifac-reader-facA", new { }),
            JsonRequest(HttpMethod.Patch, $"/api/v1/outbound/orders/{OrderClientAFacB}/hold", "staff-multifac-reader-facA", new { reason = "hold" }),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/pick-waves", "staff-multifac-reader-facA", new { request = new { clientId = ClientA, facilityId = FacilityB, orderIds = new[] { OrderClientAFacB } } }),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-waves/{WaveFacB}/release", "staff-multifac-reader-facA", new { }),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskFacB}/confirm", "staff-multifac-reader-facA", new { scannedLocationBarcode = "BIN-A-01", scannedItemBarcode = "SKU-740A-1", scannedToteLpn = "TOTE-1", quantity = 1m }),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/shipments", "staff-multifac-reader-facA", new { clientId = ClientA, facilityId = FacilityB, carrierCode = "SIM-FEDEX", serviceLevel = 1 }),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/shipping/rates", "staff-multifac-reader-facA", new { packageId = PackageClientAFacB }),
            JsonRequest(HttpMethod.Post, "/api/v1/outbound/shipping/labels/generate", "staff-multifac-reader-facA", new { packageId = PackageClientAFacB, carrierCode = "SIM-FEDEX", serviceLevel = 1 }),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/shipping/labels/{LabelFacB}/void", "staff-multifac-reader-facA", new { reason = "wrong" })
        };

        foreach (var req in mutationRequests)
        {
            using (req)
            using (var resp = await _http.SendAsync(req))
            {
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            }
        }
    }

    [Fact]
    public async Task OrdinaryAdminRole_WithoutExplicitPermissionOrFacility_DoesNotReceiveSuperAdminBypass()
    {
        using var req = Request(HttpMethod.Get, $"/api/v1/outbound/orders/{OrderClientAFacB}", "ordinary-admin-no-grants");
        using var resp = await _http.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        using var superReq = Request(HttpMethod.Get, $"/api/v1/outbound/orders/{OrderClientAFacB}", "superadmin");
        using var superResp = await _http.SendAsync(superReq);
        Assert.Equal(HttpStatusCode.OK, superResp.StatusCode);
    }

    [Fact]
    public async Task PickTaskExecution_OtherOperatorWithoutTasksManage_IsRejected_WithForbidden()
    {
        // TaskAssignedOpAFacA is assigned to StaffOpAId; StaffOpBId lacks Outbound.TasksManage -> 403
        using var confirmReq = JsonRequest(
            HttpMethod.Post,
            $"/api/v1/outbound/pick-tasks/{TaskAssignedOpAFacA}/confirm",
            "staff-operator-b",
            new { scannedLocationBarcode = "BIN-A-01", scannedItemBarcode = "SKU-740A-1", scannedToteLpn = "TOTE-1", quantity = 1m });
        using var confirmResp = await _http.SendAsync(confirmReq);
        Assert.Equal(HttpStatusCode.Forbidden, confirmResp.StatusCode);

        using var shortReq = JsonRequest(
            HttpMethod.Post,
            $"/api/v1/outbound/pick-tasks/{TaskAssignedOpAFacA}/short-pick",
            "staff-operator-b",
            new { quantityPicked = 0m, reason = 1, notes = "empty" });
        using var shortResp = await _http.SendAsync(shortReq);
        Assert.Equal(HttpStatusCode.Forbidden, shortResp.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. Tenant Concealment (404), Cross-Tenant Spoof (403), Allocation 403 (No 500)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClientTenantIsolation_ForeignOrderConcealedAsNotFound_ExplicitSpoofForbidden()
    {
        // Foreign order by opaque ID -> 404 NotFound concealment
        using (var getForeign = Request(HttpMethod.Get, $"/api/v1/outbound/orders/{OrderClientBFacA}", "client-admin-legacy"))
        using (var getResp = await _http.SendAsync(getForeign))
        {
            Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
        }

        using (var cancelForeign = JsonRequest(HttpMethod.Patch, $"/api/v1/outbound/orders/{OrderClientBFacA}/cancel", "client-admin-legacy", new { reason = "x" }))
        using (var cancelResp = await _http.SendAsync(cancelForeign))
        {
            Assert.Equal(HttpStatusCode.NotFound, cancelResp.StatusCode);
        }

        // Explicit clientId / X-Tenant-Id spoofing -> 403 Forbidden
        using (var listSpoof = Request(HttpMethod.Get, $"/api/v1/outbound/orders?clientId={ClientB}", "client-admin-legacy"))
        using (var listResp = await _http.SendAsync(listSpoof))
        {
            Assert.Equal(HttpStatusCode.Forbidden, listResp.StatusCode);
        }

        // Own cancelable order -> 200 OK
        using (var cancelOwn = JsonRequest(HttpMethod.Patch, $"/api/v1/outbound/orders/{OrderCancelableClientA}/cancel", "client-admin-legacy", new { reason = "Changed mind" }))
        using (var cancelOwnResp = await _http.SendAsync(cancelOwn))
        {
            Assert.Equal(HttpStatusCode.OK, cancelOwnResp.StatusCode);
        }
    }

    [Fact]
    public async Task AllocationExecutionService_CrossTenantMismatch_ThrowsForbiddenExceptionInsteadOf500()
    {
        var dbName = Guid.NewGuid().ToString();
        using var seed = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        seed.OutboundOrders.Add(new OutboundOrder
        {
            Id = OrderClientBFacA,
            ClientId = ClientB,
            FacilityId = FacilityA,
            OrderNumber = "SO-ALLOC-FOREIGN",
            CustomerName = "Foreign",
            Status = OutboundOrderStatus.Released
        });
        await seed.SaveChangesAsync();

        var tenantProvider = new TestTenantProvider { TenantId = ClientA, BypassTenantFilter = false };
        using var ctx = TestDbContextFactory.Create(tenantProvider, new TestCurrentUserContext { UserType = UserType.Staff, FacilityId = FacilityA }, dbName);
        var filter = new AllocationCandidateFilter();
        var engine = new AllocationEngine(ctx, new AllocationStrategyResolver([new FifoAllocationStrategy(ctx, filter)]), filter);
        var service = new AllocationExecutionService(ctx, engine, filter, tenantProvider);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ExecuteOrderAllocationAsync(OrderClientBFacA));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4. Carrier Webhook HMAC-SHA256 Authentication, Replay Prevention & Audit Logging
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CarrierWebhook_AuthSecurityMatrix_EnforcesHmacReplayRotationAndAudit()
    {
        var payloadObj = new
        {
            carrierCode = "SIM-FEDEX",
            trackingNumber = ReturnTrackingNumber,
            carrierEventId = "EVT-740A-1",
            carrierEventCode = "IN_TRANSIT",
            carrierDescription = "Departed FedEx hub",
            occurredAtUtc = DateTime.UtcNow
        };
        var bodyJson = JsonSerializer.Serialize(payloadObj);

        // A. Missing HMAC headers -> 401
        using (var missingReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/outbound/shipping/returns/tracking/webhook")
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        })
        using (var missingResp = await _http.SendAsync(missingReq))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, missingResp.StatusCode);
        }

        // B. Invalid HMAC signature -> 401
        using (var badSigReq = CreateSignedWebhookRequest("SIM-FEDEX", "wrong-secret", bodyJson, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "nonce-bad-sig-1"))
        using (var badSigResp = await _http.SendAsync(badSigReq))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, badSigResp.StatusCode);
        }

        // C. Expired timestamp (> 300s skew) -> 401
        var expiredTs = DateTimeOffset.UtcNow.AddSeconds(-360).ToUnixTimeSeconds().ToString();
        using (var expiredReq = CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, bodyJson, expiredTs, "nonce-expired-1"))
        using (var expiredResp = await _http.SendAsync(expiredReq))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, expiredResp.StatusCode);
        }

        // Verify zero ReturnTrackingEvent mutations happened during rejected requests
        using (var checkScope = _app.Services.CreateScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.ReturnTrackingEvents.IgnoreQueryFilters().ToListAsync());
        }

        // D. Valid primary secret webhook -> 200 OK + persists nonce & event
        var validTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using (var validReq = CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, bodyJson, validTs, "nonce-valid-1"))
        using (var validResp = await _http.SendAsync(validReq))
        {
            Assert.Equal(HttpStatusCode.OK, validResp.StatusCode);
        }

        // E. Replay same nonce (even with new timestamp & valid signature) -> 401 + no extra event
        var replayTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using (var replayReq = CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, bodyJson, replayTs, "nonce-valid-1"))
        using (var replayResp = await _http.SendAsync(replayReq))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, replayResp.StatusCode);
        }

        // F. Valid secondary rotation secret (unexpired) -> 200 OK
        var rotPayload = JsonSerializer.Serialize(new
        {
            carrierCode = "SIM-FEDEX",
            trackingNumber = ReturnTrackingNumber,
            carrierEventId = "EVT-740A-2",
            carrierEventCode = "OUT_FOR_DELIVERY",
            carrierDescription = "Out for delivery to dock",
            occurredAtUtc = DateTime.UtcNow
        });
        using (var secReq = CreateSignedWebhookRequest("SIM-FEDEX", SecondarySecretActiveCarrier, rotPayload, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "nonce-rot-2"))
        using (var secResp = await _http.SendAsync(secReq))
        {
            Assert.Equal(HttpStatusCode.OK, secResp.StatusCode);
        }

        // G. Expired secondary secret -> 401
        var upsPayload = JsonSerializer.Serialize(new
        {
            carrierCode = "SIM-UPS",
            trackingNumber = ReturnTrackingNumber,
            carrierEventCode = "IN_TRANSIT",
            carrierDescription = "UPS",
            occurredAtUtc = DateTime.UtcNow
        });
        using (var expSecReq = CreateSignedWebhookRequest("SIM-UPS", ExpiredSecondarySecret, upsPayload, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "nonce-exp-sec-3"))
        using (var expSecResp = await _http.SendAsync(expSecReq))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, expSecResp.StatusCode);
        }

        // H. Cross-tenant TenantIdOverride after valid HMAC signature -> 403 Forbidden
        var crossTenantPayload = JsonSerializer.Serialize(new
        {
            carrierCode = "SIM-FEDEX",
            trackingNumber = ReturnTrackingNumber,
            carrierEventId = "EVT-740A-3",
            carrierEventCode = "IN_TRANSIT",
            carrierDescription = "Cross tenant",
            occurredAtUtc = DateTime.UtcNow,
            tenantIdOverride = ClientB
        });
        using (var crossReq = CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, crossTenantPayload, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "nonce-cross-4"))
        using (var crossResp = await _http.SendAsync(crossReq))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossResp.StatusCode);
        }

        // Assert exact DB state: 2 ReturnTrackingEvents, 3 nonces (including crossTenant after auth), accurate Received/Processed/Failed/ReplayRejected audits
        using (var verifyScope = _app.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, await db.ReturnTrackingEvents.IgnoreQueryFilters().CountAsync());
            Assert.Equal(3, await db.CarrierWebhookNonces.CountAsync());
            var audits = await db.CarrierWebhookAuditLogs.ToListAsync();
            Assert.Contains(audits, a => a.Outcome == "MissingHeader" && a.HttpStatusCode == 401);
            Assert.Contains(audits, a => a.Outcome == "InvalidSignature" && a.HttpStatusCode == 401);
            Assert.Contains(audits, a => a.Outcome == "ExpiredTimestamp" && a.HttpStatusCode == 401);
            Assert.Contains(audits, a => a.Outcome == "ReplayRejected" && a.HttpStatusCode == 401);
            Assert.Contains(audits, a => a.Outcome == "Failed" && a.HttpStatusCode == 403 && a.FailureReason != null && a.FailureReason.Contains("CrossTenantOrScopeRejected"));
            Assert.Equal(3, audits.Count(a => a.Outcome == "Received"));
            Assert.Equal(2, audits.Count(a => a.Outcome == "Processed" && a.HttpStatusCode == 200));
        }
    }

    [Fact]
    public async Task CarrierWebhook_RetryRecoveryAndConcurrentSafety_PreservesIdempotency()
    {
        var probeState = _app.Services.GetRequiredService<WebhookProbeState>();
        var retryPayload = JsonSerializer.Serialize(new
        {
            carrierCode = "SIM-FEDEX",
            trackingNumber = ReturnTrackingNumber,
            carrierEventId = "EVT-RETRY-1",
            carrierEventCode = "IN_TRANSIT",
            carrierDescription = "Retry recovery event",
            occurredAtUtc = DateTime.UtcNow
        });
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        const string retryNonce = "nonce-retry-recoverable-1";

        // 1. Simulate transient downstream failure (500) on attempt 1
        probeState.FailNextNonce = retryNonce;
        using (var failReq = CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, retryPayload, ts, retryNonce))
        using (var failResp = await _http.SendAsync(failReq))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, failResp.StatusCode);
        }

        // Verify zero ReturnTrackingEvent created, nonce recorded as Failed
        using (var checkScope1 = _app.Services.CreateScope())
        {
            var db = checkScope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.ReturnTrackingEvents.IgnoreQueryFilters().ToListAsync());
            var nonceEntity = await db.CarrierWebhookNonces.SingleAsync(x => x.Nonce == retryNonce);
            Assert.Equal(CarrierWebhookNonceStatus.Failed, nonceEntity.Status);
            Assert.Equal(500, nonceEntity.LastHttpStatusCode);
        }

        // 2. Identical signed retry attempt 2 recovers cleanly and processes once -> 200 OK
        using (var retryReq = CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, retryPayload, ts, retryNonce))
        using (var retryResp = await _http.SendAsync(retryReq))
        {
            Assert.Equal(HttpStatusCode.OK, retryResp.StatusCode);
        }

        // 3. Subsequent identical replay after Succeeded MUST still be rejected -> 401 ReplayRejected
        using (var replayReq = CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, retryPayload, ts, retryNonce))
        using (var replayResp = await _http.SendAsync(replayReq))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, replayResp.StatusCode);
        }

        // 4. Concurrent duplicate race with unseen nonce must never duplicate business events
        var concurrentPayload = JsonSerializer.Serialize(new
        {
            carrierCode = "SIM-FEDEX",
            trackingNumber = ReturnTrackingNumber,
            carrierEventId = "EVT-CONCURRENT-1",
            carrierEventCode = "OUT_FOR_DELIVERY",
            carrierDescription = "Concurrent test event",
            occurredAtUtc = DateTime.UtcNow
        });
        var cTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        const string cNonce = "nonce-concurrent-1";

        var t1 = _http.SendAsync(CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, concurrentPayload, cTs, cNonce));
        var t2 = _http.SendAsync(CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, concurrentPayload, cTs, cNonce));
        var responses = await Task.WhenAll(t1, t2);
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized || r.StatusCode == HttpStatusCode.Conflict));
        foreach (var r in responses) r.Dispose();

        using (var finalScope = _app.Services.CreateScope())
        {
            var db = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, await db.ReturnTrackingEvents.IgnoreQueryFilters().CountAsync());
            var finalRetryNonce = await db.CarrierWebhookNonces.SingleAsync(x => x.Nonce == retryNonce);
            Assert.Equal(CarrierWebhookNonceStatus.Succeeded, finalRetryNonce.Status);
            Assert.Equal(2, finalRetryNonce.AttemptCount);

            var retryAudits = await db.CarrierWebhookAuditLogs.Where(a => a.Nonce == retryNonce).ToListAsync();
            Assert.Equal(2, retryAudits.Count(a => a.Outcome == "Received"));
            Assert.Single(retryAudits, a => a.Outcome == "Failed" && a.HttpStatusCode == 500);
            Assert.Single(retryAudits, a => a.Outcome == "Processed" && a.HttpStatusCode == 200);
            Assert.Single(retryAudits, a => a.Outcome == "ReplayRejected" && a.HttpStatusCode == 401);
        }
    }

    [Fact]
    public async Task CarrierWebhook_ExpiredLeaseFencing_BlocksStaleCompletionInBothOrders()
    {
        var clock = _app.Services.GetRequiredService<ControllableClock>();
        var probeState = _app.Services.GetRequiredService<WebhookProbeState>();

        // Case 1: Worker A stalls -> lease expires -> Worker B reclaims and SUCCEEDS FIRST -> stale Worker A FAILS afterward
        const string nonceForwardFail = "nonce-fence-fwd-fail";
        var payload1 = JsonSerializer.Serialize(new
        {
            carrierCode = "SIM-FEDEX",
            trackingNumber = ReturnTrackingNumber,
            carrierEventId = "EVT-FENCE-1",
            carrierEventCode = "IN_TRANSIT",
            carrierDescription = "Fencing forward-fail test",
            occurredAtUtc = DateTime.UtcNow
        });

        var ctrlA1 = new WebhookAttemptControl { FailOnProceed = true };
        var ctrlB1 = new WebhookAttemptControl { FailOnProceed = false };
        probeState.Enqueue(nonceForwardFail, ctrlA1);
        probeState.Enqueue(nonceForwardFail, ctrlB1);

        var ts1 = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds().ToString();
        var workerA1Task = _http.SendAsync(CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, payload1, ts1, nonceForwardFail));
        await ctrlA1.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Expire Worker A's 60s lease while A remains stalled
        clock.UtcNow = clock.UtcNow.AddSeconds(70);
        var tsB1 = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds().ToString();
        var workerB1Task = _http.SendAsync(CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, payload1, tsB1, nonceForwardFail));
        await ctrlB1.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ctrlB1.Proceed.TrySetResult(true);
        using var respB1 = await workerB1Task;
        Assert.Equal(HttpStatusCode.OK, respB1.StatusCode);

        // Release stale Worker A to fail afterward -> must NOT overwrite Succeeded
        ctrlA1.Proceed.TrySetResult(true);
        using var respA1 = await workerA1Task;
        Assert.Equal(HttpStatusCode.InternalServerError, respA1.StatusCode);

        // Case 2: Reverse order — Worker A stalls -> lease expires -> Worker B reclaims -> stale Worker A COMPLETES FIRST -> Worker B completes SECOND
        const string nonceReverseOk = "nonce-fence-rev-ok";
        var payload2 = JsonSerializer.Serialize(new
        {
            carrierCode = "SIM-FEDEX",
            trackingNumber = ReturnTrackingNumber,
            carrierEventId = "EVT-FENCE-2",
            carrierEventCode = "OUT_FOR_DELIVERY",
            carrierDescription = "Fencing reverse-order test",
            occurredAtUtc = DateTime.UtcNow
        });

        var ctrlA2 = new WebhookAttemptControl { FailOnProceed = false };
        var ctrlB2 = new WebhookAttemptControl { FailOnProceed = false };
        probeState.Enqueue(nonceReverseOk, ctrlA2);
        probeState.Enqueue(nonceReverseOk, ctrlB2);

        var tsA2 = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds().ToString();
        var workerA2Task = _http.SendAsync(CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, payload2, tsA2, nonceReverseOk));
        await ctrlA2.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        clock.UtcNow = clock.UtcNow.AddSeconds(70);
        var tsB2 = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds().ToString();
        var workerB2Task = _http.SendAsync(CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, payload2, tsB2, nonceReverseOk));
        await ctrlB2.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Unblock stale Worker A FIRST while Worker B holds active ownership
        ctrlA2.Proceed.TrySetResult(true);
        using var respA2 = await workerA2Task;
        Assert.Equal(HttpStatusCode.OK, respA2.StatusCode);

        using (var midScope = _app.Services.CreateScope())
        {
            var midDb = midScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var midNonce = await midDb.CarrierWebhookNonces.SingleAsync(x => x.Nonce == nonceReverseOk);
            Assert.Equal(CarrierWebhookNonceStatus.Processing, midNonce.Status);
            Assert.NotNull(midNonce.ClaimToken);
            Assert.Equal(2, midNonce.AttemptCount);
        }

        // Unblock legitimate Worker B SECOND -> transitions nonce cleanly to Succeeded with 1 total business event
        ctrlB2.Proceed.TrySetResult(true);
        using var respB2 = await workerB2Task;
        Assert.Equal(HttpStatusCode.OK, respB2.StatusCode);

        using (var verifyScope = _app.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, await db.ReturnTrackingEvents.IgnoreQueryFilters().CountAsync());

            foreach (var n in new[] { nonceForwardFail, nonceReverseOk })
            {
                var row = await db.CarrierWebhookNonces.SingleAsync(x => x.Nonce == n);
                Assert.Equal(CarrierWebhookNonceStatus.Succeeded, row.Status);
                Assert.Null(row.ClaimToken);
                Assert.Equal(2, row.AttemptCount);
                Assert.Equal(200, row.LastHttpStatusCode);

                var audits = await db.CarrierWebhookAuditLogs.Where(a => a.Nonce == n).ToListAsync();
                Assert.Equal(2, audits.Count(a => a.Outcome == "Received"));
                Assert.Single(audits, a => a.Outcome == "StaleAttemptIgnored");
                Assert.Single(audits, a => a.Outcome == "Processed" && a.HttpStatusCode == 200);
                Assert.DoesNotContain(audits, a => a.Outcome == "Failed");
            }
        }
    }

    [Fact]
    public async Task CarrierWebhook_InvalidExtremeMalformedTimestamps_FailClosedWithUnauthorizedAndAudit()
    {
        var payload = JsonSerializer.Serialize(new
        {
            carrierCode = "SIM-FEDEX",
            trackingNumber = ReturnTrackingNumber,
            carrierEventId = "EVT-TS-BAD",
            carrierEventCode = "IN_TRANSIT",
            carrierDescription = "Timestamp fail-closed test",
            occurredAtUtc = DateTime.UtcNow
        });

        var invalidTimestamps = new[]
        {
            long.MaxValue.ToString(),
            long.MinValue.ToString(),
            "253402300800",
            "-62135596801",
            "not-a-timestamp",
            "2026-99-99T00:00:00Z",
            "9999-12-31T23:59:59Z",
            "0001-01-01T00:00:00Z"
        };

        for (var i = 0; i < invalidTimestamps.Length; i++)
        {
            var ts = invalidTimestamps[i];
            using var req = CreateSignedWebhookRequest("SIM-FEDEX", PrimarySecretActiveCarrier, payload, ts, $"nonce-bad-ts-{i}");
            using var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.ReturnTrackingEvents.IgnoreQueryFilters().ToListAsync());
        var audits = await db.CarrierWebhookAuditLogs.ToListAsync();
        Assert.Equal(invalidTimestamps.Length, audits.Count(a =>
            (a.Outcome == "InvalidTimestamp" || a.Outcome == "ExpiredTimestamp") && a.HttpStatusCode == 401));
    }

    [Fact]
    public async Task CarrierWebhook_OversizedPayloadReturns413_AndAuditRecordsActualDownstreamStatuses()
    {
        // 1. Oversized request with explicit Content-Length (> 1024 bytes) -> 413 Payload Too Large before auth
        var oversizedBody = new string('A', 2048);
        using (var largeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/outbound/shipping/returns/tracking/webhook")
        {
            Content = new StringContent(oversizedBody, Encoding.UTF8, "application/json")
        })
        using (var largeResp = await _http.SendAsync(largeReq))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, largeResp.StatusCode);
        }

        // 2. Oversized chunked/unknown-length stream without Content-Length header -> 413 Payload Too Large
        using (var chunkedReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/outbound/shipping/returns/tracking/webhook")
        {
            Content = new ChunkedNoLengthContent(Encoding.UTF8.GetBytes(new string('B', 2048)))
        })
        using (var chunkedResp = await _http.SendAsync(chunkedReq))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, chunkedResp.StatusCode);
        }

        // 3. Downstream returning non-200 IActionResult statuses (202 Accepted, 503 ServiceUnavailable) accurately audited
        var probeBody = JsonSerializer.Serialize(new { carrierCode = "SIM-FEDEX", trackingNumber = ReturnTrackingNumber });
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        using (var accReq = CreateSignedWebhookRequestToPath("/api/v1/test/webhook-probe/status/202", "SIM-FEDEX", PrimarySecretActiveCarrier, probeBody, ts, "nonce-probe-202"))
        using (var accResp = await _http.SendAsync(accReq))
        {
            Assert.Equal(HttpStatusCode.Accepted, accResp.StatusCode);
        }

        // 503 non-exceptional result -> Failed (503) + nonce Marked Failed -> safe retry allowed with 200 OK
        using (var unavailReq = CreateSignedWebhookRequestToPath("/api/v1/test/webhook-probe/status/503", "SIM-FEDEX", PrimarySecretActiveCarrier, probeBody, ts, "nonce-probe-503"))
        using (var unavailResp = await _http.SendAsync(unavailReq))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailResp.StatusCode);
        }

        using (var retryProbeReq = CreateSignedWebhookRequestToPath("/api/v1/test/webhook-probe/status/200", "SIM-FEDEX", PrimarySecretActiveCarrier, probeBody, ts, "nonce-probe-503"))
        using (var retryProbeResp = await _http.SendAsync(retryProbeReq))
        {
            Assert.Equal(HttpStatusCode.OK, retryProbeResp.StatusCode);
        }

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await db.CarrierWebhookAuditLogs.ToListAsync();
        Assert.Equal(2, audits.Count(a => a.Outcome == "PayloadTooLarge" && a.HttpStatusCode == 413));
        Assert.Contains(audits, a => a.Nonce == "nonce-probe-202" && a.Outcome == "Processed" && a.HttpStatusCode == 202);
        Assert.Contains(audits, a => a.Nonce == "nonce-probe-503" && a.Outcome == "Failed" && a.HttpStatusCode == 503);
        Assert.Contains(audits, a => a.Nonce == "nonce-probe-503" && a.Outcome == "Processed" && a.HttpStatusCode == 200);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 5. Idempotent ClientAdmin Role Permission Reconciliation
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DbInitializer_ReconcilesLegacyClientAdminPermissions_Idempotently()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        // Pre-seed legacy ClientAdmin role containing forbidden physical floor execution grants
        var legacyClientAdmin = new Role
        {
            RoleName = SystemRoles.ClientAdmin,
            Description = "Legacy ClientAdmin",
            IsSystemRole = true
        };
        legacyClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Pick, AccessLevel = AccessLevel.Admin });
        legacyClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Ship, AccessLevel = AccessLevel.Admin });
        legacyClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Pack, AccessLevel = AccessLevel.Admin });
        legacyClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.OrdersAllocate, AccessLevel = AccessLevel.Admin });
        legacyClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.WavesManage, AccessLevel = AccessLevel.Admin });
        legacyClientAdmin.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.TasksManage, AccessLevel = AccessLevel.Admin });
        context.Roles.Add(legacyClientAdmin);
        await context.SaveChangesAsync();

        var initializer = new DbInitializer(context, new PasswordHasher(), NullLogger<DbInitializer>.Instance);
        await initializer.SeedAsync();
        await initializer.SeedAsync(); // Second pass verifies idempotency

        var reconciled = await context.Roles
            .Include(r => r.RolePermissions)
            .SingleAsync(r => r.RoleName == SystemRoles.ClientAdmin);

        var perms = reconciled.RolePermissions.Select(p => p.PermissionKey).ToList();
        Assert.DoesNotContain(Permissions.Outbound.Pick, perms);
        Assert.DoesNotContain(Permissions.Outbound.Ship, perms);
        Assert.DoesNotContain(Permissions.Outbound.Pack, perms);
        Assert.DoesNotContain(Permissions.Outbound.OrdersAllocate, perms);
        Assert.DoesNotContain(Permissions.Outbound.WavesManage, perms);
        Assert.DoesNotContain(Permissions.Outbound.TasksManage, perms);

        Assert.Contains(Permissions.Outbound.OrdersView, perms);
        Assert.Contains(Permissions.Outbound.OrdersCreate, perms);
        Assert.Contains(Permissions.Outbound.OrdersImport, perms);
        Assert.Contains(Permissions.Outbound.OrdersCancel, perms);
        Assert.Contains(Permissions.Dispatch.View, perms);
        Assert.Equal(perms.Distinct(StringComparer.OrdinalIgnoreCase).Count(), perms.Count);
    }

    private static HttpRequestMessage CreateSignedWebhookRequest(
        string carrierId,
        string secret,
        string bodyJson,
        string timestamp,
        string nonce)
        => CreateSignedWebhookRequestToPath("/api/v1/outbound/shipping/returns/tracking/webhook", carrierId, secret, bodyJson, timestamp, nonce);

    private static HttpRequestMessage CreateSignedWebhookRequestToPath(
        string path,
        string carrierId,
        string secret,
        string bodyJson,
        string timestamp,
        string nonce)
    {
        var canonical = $"{timestamp}.{nonce}.{bodyJson}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Carrier-Id", carrierId);
        request.Headers.Add("X-Webhook-Timestamp", timestamp);
        request.Headers.Add("X-Webhook-Nonce", nonce);
        request.Headers.Add("X-Webhook-Signature", signature);
        return request;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string actor)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Warehouse-Actor", actor);
        return request;
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, string actor, object body)
    {
        var request = Request(method, path, actor);
        request.Content = JsonContent.Create(body);
        return request;
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    private sealed class OutboundTestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var actor = Request.Headers["X-Test-Warehouse-Actor"].ToString();
            if (string.IsNullOrWhiteSpace(actor))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>();

            switch (actor)
            {
                case "client-admin-legacy":
                    claims.Add(new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
                    claims.Add(new("user_type", "Client"));
                    claims.Add(new("tenant_id", ClientA.ToString()));
                    claims.Add(new(ClaimTypes.Role, SystemRoles.ClientAdmin));
                    // Deliberately include legacy floor claims to prove handler-level UserType rejection
                    foreach (var p in new[]
                    {
                        Permissions.Outbound.OrdersView,
                        Permissions.Outbound.OrdersCreate,
                        Permissions.Outbound.OrdersImport,
                        Permissions.Outbound.OrdersCancel,
                        Permissions.Outbound.OrdersManage,
                        Permissions.Outbound.OrdersAllocate,
                        Permissions.Outbound.WavesView,
                        Permissions.Outbound.WavesManage,
                        Permissions.Outbound.TasksView,
                        Permissions.Outbound.TasksManage,
                        Permissions.Outbound.Pick,
                        Permissions.Outbound.Pack,
                        Permissions.Outbound.Ship
                    })
                    {
                        claims.Add(new("permissions", p));
                    }
                    break;

                case "client-admin-reconciled":
                    claims.Add(new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
                    claims.Add(new("user_type", "Client"));
                    claims.Add(new("tenant_id", ClientA.ToString()));
                    claims.Add(new(ClaimTypes.Role, SystemRoles.ClientAdmin));
                    foreach (var p in new[]
                    {
                        Permissions.Outbound.View,
                        Permissions.Outbound.Create,
                        Permissions.Outbound.OrdersView,
                        Permissions.Outbound.OrdersCreate,
                        Permissions.Outbound.OrdersImport,
                        Permissions.Outbound.OrdersCancel,
                        Permissions.Dispatch.View
                    })
                    {
                        claims.Add(new("permissions", p));
                    }
                    break;

                case "driver-legacy":
                    claims.Add(new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
                    claims.Add(new("user_type", "Driver"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new(ClaimTypes.Role, SystemRoles.Driver));
                    foreach (var p in new[]
                    {
                        Permissions.Outbound.OrdersView,
                        Permissions.Outbound.TasksView,
                        Permissions.Outbound.Pick,
                        Permissions.Outbound.Ship,
                        Permissions.Dispatch.View
                    })
                    {
                        claims.Add(new("permissions", p));
                    }
                    break;

                case "staff-unassigned":
                    claims.Add(new(ClaimTypes.NameIdentifier, StaffOpAId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("permissions", Permissions.Outbound.OrdersView));
                    claims.Add(new("permissions", Permissions.Outbound.OrdersAllocate));
                    break;

                case "staff-supervisor-facA":
                    claims.Add(new(ClaimTypes.NameIdentifier, StaffOpAId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    foreach (var p in new[]
                    {
                        Permissions.Outbound.OrdersView, Permissions.Outbound.OrdersManage, Permissions.Outbound.OrdersAllocate,
                        Permissions.Outbound.WavesView, Permissions.Outbound.WavesManage,
                        Permissions.Outbound.TasksView, Permissions.Outbound.TasksManage,
                        Permissions.Outbound.Pick, Permissions.Outbound.Pack, Permissions.Outbound.Ship
                    })
                    {
                        claims.Add(new("permissions", p));
                    }
                    break;

                case "staff-multifac-reader-facA":
                    claims.Add(new(ClaimTypes.NameIdentifier, StaffOpAId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    foreach (var p in new[]
                    {
                        Permissions.Analytics.MultiFacilityView,
                        Permissions.Outbound.OrdersView, Permissions.Outbound.OrdersManage, Permissions.Outbound.OrdersAllocate,
                        Permissions.Outbound.WavesView, Permissions.Outbound.WavesManage,
                        Permissions.Outbound.TasksView, Permissions.Outbound.TasksManage,
                        Permissions.Outbound.Pick, Permissions.Outbound.Pack, Permissions.Outbound.Ship
                    })
                    {
                        claims.Add(new("permissions", p));
                    }
                    break;

                case "staff-operator-b":
                    claims.Add(new(ClaimTypes.NameIdentifier, StaffOpBId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new("permissions", Permissions.Outbound.Pick));
                    claims.Add(new("permissions", Permissions.Outbound.TasksView));
                    break;

                case "ordinary-admin-no-grants":
                    claims.Add(new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new(ClaimTypes.Role, "Admin"));
                    break;

                case "superadmin":
                    claims.Add(new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new(ClaimTypes.Role, SystemRoles.SuperAdmin));
                    break;

                default:
                    return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

public sealed class ControllableClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}

public sealed class WebhookAttemptControl
{
    public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Proceed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool FailOnProceed { get; init; }
}

public sealed class WebhookProbeState
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentQueue<WebhookAttemptControl>> _controls = new(StringComparer.Ordinal);
    public string? FailNextNonce { get; set; }

    public void Enqueue(string nonce, WebhookAttemptControl control)
    {
        var q = _controls.GetOrAdd(nonce, _ => new System.Collections.Concurrent.ConcurrentQueue<WebhookAttemptControl>());
        q.Enqueue(control);
    }

    public bool TryDequeue(string? nonce, out WebhookAttemptControl? control)
    {
        control = null;
        if (string.IsNullOrEmpty(nonce)) return false;
        return _controls.TryGetValue(nonce, out var q) && q.TryDequeue(out control);
    }
}

public sealed class FaultInjectableReturnTrackingService(
    ReturnTrackingService inner,
    WebhookProbeState probeState,
    Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor) : IReturnTrackingService
{
    public async Task<IngestReturnTrackingResult> IngestReturnTrackingEventAsync(
        IngestReturnTrackingRequest request,
        CancellationToken cancellationToken = default)
    {
        var nonce = httpContextAccessor.HttpContext?.Request.Headers["X-Webhook-Nonce"].FirstOrDefault();
        if (probeState.TryDequeue(nonce, out var control) && control != null)
        {
            control.Entered.TrySetResult(true);
            await control.Proceed.Task;
            if (control.FailOnProceed)
            {
                throw new TimeoutException("Simulated delayed worker failure after lease expiry.");
            }
        }
        else if (!string.IsNullOrEmpty(probeState.FailNextNonce) &&
                 string.Equals(probeState.FailNextNonce, nonce, StringComparison.Ordinal))
        {
            probeState.FailNextNonce = null;
            throw new TimeoutException("Simulated transient carrier downstream timeout.");
        }

        return await inner.IngestReturnTrackingEventAsync(request, cancellationToken);
    }

    public Task<ReturnInboundHandoffDto?> GetInboundHandoffByReturnShipmentIdAsync(
        Guid returnShipmentId,
        CancellationToken cancellationToken = default)
        => inner.GetInboundHandoffByReturnShipmentIdAsync(returnShipmentId, cancellationToken);

    public Task<ReturnInboundHandoffDto?> GetInboundHandoffByAuthorizationIdAsync(
        Guid returnAuthorizationId,
        CancellationToken cancellationToken = default)
        => inner.GetInboundHandoffByAuthorizationIdAsync(returnAuthorizationId, cancellationToken);

    public Task<IReadOnlyList<ReturnTrackingTimelineEventDto>> GetReturnTrackingHistoryAsync(
        Guid returnShipmentId,
        CancellationToken cancellationToken = default)
        => inner.GetReturnTrackingHistoryAsync(returnShipmentId, cancellationToken);
}

[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Mvc.Route("api/v1/test/webhook-probe")]
public sealed class WebhookProbeTestController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    [Microsoft.AspNetCore.Mvc.HttpPost("status/{code:int}")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [Microsoft.AspNetCore.Mvc.RequestSizeLimit(262_144)]
    [Warehouse3PL.WebApi.Security.CarrierWebhookHmacAuth]
    public Microsoft.AspNetCore.Mvc.IActionResult ReturnExplicitStatus([Microsoft.AspNetCore.Mvc.FromRoute] int code)
        => StatusCode(code, new { status = code });
}

internal sealed class ChunkedNoLengthContent(byte[] payload) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => stream.WriteAsync(payload, 0, payload.Length);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
