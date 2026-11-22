using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
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

public sealed class Phase740BReadQueuesAndRfClaimHttpTests : IAsyncLifetime
{
    private static readonly Guid ClientA = Guid.Parse("740b0001-0000-0000-0000-000000000001");
    private static readonly Guid ClientB = Guid.Parse("740b0001-0000-0000-0000-000000000002");
    private static readonly Guid FacilityA = Guid.Parse("740b0002-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("740b0002-0000-0000-0000-000000000002");

    private static readonly Guid OpAId = Guid.Parse("740b0003-0000-0000-0000-000000000001");
    private static readonly Guid OpBId = Guid.Parse("740b0003-0000-0000-0000-000000000002");
    private static readonly Guid SupervisorId = Guid.Parse("740b0003-0000-0000-0000-000000000003");
    private static readonly Guid ViewOnlyUserId = Guid.Parse("740b0003-0000-0000-0000-000000000004");
    private static readonly Guid ClientUserId = Guid.Parse("740b0003-0000-0000-0000-000000000005");

    private static readonly Guid ItemA = Guid.Parse("740b0004-0000-0000-0000-000000000001");
    private static readonly Guid BinA = Guid.Parse("740b0005-0000-0000-0000-000000000001");
    private static readonly Guid PackStationA = Guid.Parse("740b0005-0000-0000-0000-000000000002");
    private static readonly Guid BinB = Guid.Parse("740b0005-0000-0000-0000-000000000003");

    private static readonly Guid OrderA1 = Guid.Parse("740b0006-0000-0000-0000-000000000001");
    private static readonly Guid OrderLineA1 = Guid.Parse("740b0006-0000-0000-0000-000000000011");
    private static readonly Guid OrderB1 = Guid.Parse("740b0006-0000-0000-0000-000000000002");
    private static readonly Guid OrderClientBFacA = Guid.Parse("740b0006-0000-0000-0000-000000000003");

    private static readonly Guid StockBinAId = Guid.Parse("740b0007-0000-0000-0000-000000000001");
    private static readonly Guid AllocPendingAId = Guid.Parse("740b0008-0000-0000-0000-000000000001");
    private static readonly Guid AllocShortAId = Guid.Parse("740b0008-0000-0000-0000-000000000002");

    private static readonly Guid WaveFacA = Guid.Parse("740b0009-0000-0000-0000-000000000001");
    private static readonly Guid WaveFacB = Guid.Parse("740b0009-0000-0000-0000-000000000002");

    private static readonly Guid TaskPendingA = Guid.Parse("740b000a-0000-0000-0000-000000000001");
    private static readonly Guid TaskAssignedOpA = Guid.Parse("740b000a-0000-0000-0000-000000000002");
    private static readonly Guid TaskAssignedOpB = Guid.Parse("740b000a-0000-0000-0000-000000000003");
    private static readonly Guid TaskUnclaimedShortA = Guid.Parse("740b000a-0000-0000-0000-000000000004");
    private static readonly Guid TaskPendingFacB = Guid.Parse("740b000a-0000-0000-0000-000000000005");

    private static readonly Guid ToteAId = Guid.Parse("740b000b-0000-0000-0000-000000000001");
    private static readonly Guid PackingSessionAId = Guid.Parse("740b000c-0000-0000-0000-000000000001");
    private static readonly Guid PackingSessionBId = Guid.Parse("740b000c-0000-0000-0000-000000000002");

    private static readonly Guid PkgOpenId = Guid.Parse("740b000d-0000-0000-0000-000000000001");
    private static readonly Guid PkgEligiblePackedId = Guid.Parse("740b000d-0000-0000-0000-000000000002");
    private static readonly Guid PkgEligibleLabeledId = Guid.Parse("740b000d-0000-0000-0000-000000000003");
    private static readonly Guid PkgAlreadyConsolidatedId = Guid.Parse("740b000d-0000-0000-0000-000000000004");
    private static readonly Guid PkgEligibleFacBId = Guid.Parse("740b000d-0000-0000-0000-000000000005");

    private static readonly Guid ShipmentFacA = Guid.Parse("740b000e-0000-0000-0000-000000000001");
    private static readonly Guid ShipmentFacB = Guid.Parse("740b000e-0000-0000-0000-000000000002");

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<CarrierWebhookOptions>(
            builder.Configuration.GetSection(CarrierWebhookOptions.SectionName));
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(PickTasksController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();
        builder.Services.AddAuthentication("WarehouseTest")
            .AddScheme<AuthenticationSchemeOptions, Phase740BTestAuthentication>("WarehouseTest", _ => { });
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
            new Client { Id = ClientA, CompanyName = "Client Alpha", AccountCode = "ALP" },
            new Client { Id = ClientB, CompanyName = "Client Beta", AccountCode = "BET" });

        db.Facilities.AddRange(
            new Facility { Id = FacilityA, FacilityCode = "ORD-740B", Name = "Chicago A", AddressLine1 = "1 Main", City = "Chicago", CountryCode = "US", IsActive = true },
            new Facility { Id = FacilityB, FacilityCode = "DFW-740B", Name = "Dallas B", AddressLine1 = "2 Main", City = "Dallas", CountryCode = "US", IsActive = true });

        db.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityA, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = FacilityB, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = ClientB, FacilityId = FacilityA, Status = FacilityAccessStatus.Active });

        db.Users.AddRange(
            new User { Id = OpAId, Email = "opa@740b.test", FirstName = "Op", LastName = "Alpha", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" },
            new User { Id = OpBId, Email = "opb@740b.test", FirstName = "Op", LastName = "Beta", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" },
            new User { Id = SupervisorId, Email = "sup@740b.test", FirstName = "Sup", LastName = "Lead", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" },
            new User { Id = ClientUserId, ClientId = ClientA, Email = "client@740b.test", FirstName = "Cli", LastName = "Ent", UserType = UserType.Client, Status = UserStatus.Active, PasswordHash = "x" });

        db.Items.Add(new Item { Id = ItemA, ClientId = ClientA, Sku = "SKU-740B-01", Description = "Servo Unit" });
        db.Locations.AddRange(
            new Location { Id = BinA, FacilityId = FacilityA, LocationCode = "BIN-740B-A1", Barcode = "BIN-740B-A1", LocationType = LocationType.Bin, IsActive = true, IsPickable = true },
            new Location { Id = PackStationA, FacilityId = FacilityA, LocationCode = "PK-740B-01", Barcode = "PK-740B-01", LocationType = LocationType.PackingStation, IsActive = true },
            new Location { Id = BinB, FacilityId = FacilityB, LocationCode = "BIN-740B-B1", Barcode = "BIN-740B-B1", LocationType = LocationType.Bin, IsActive = true, IsPickable = true });

        var orderA1 = new OutboundOrder { Id = OrderA1, ClientId = ClientA, FacilityId = FacilityA, OrderNumber = "SO-740B-A1", CustomerName = "Alpha Buyer", Status = OutboundOrderStatus.Allocated };
        var lineA1 = new OutboundOrderLine { Id = OrderLineA1, OutboundOrderId = OrderA1, ItemId = ItemA, LineNumber = 1, QuantityOrdered = 10m, QuantityAllocated = 10m };
        orderA1.Lines.Add(lineA1);

        var orderB1 = new OutboundOrder { Id = OrderB1, ClientId = ClientA, FacilityId = FacilityB, OrderNumber = "SO-740B-B1", CustomerName = "Beta Hub Buyer", Status = OutboundOrderStatus.Allocated };
        var orderC2A = new OutboundOrder { Id = OrderClientBFacA, ClientId = ClientB, FacilityId = FacilityA, OrderNumber = "SO-740B-C2A", CustomerName = "ClientB Buyer", Status = OutboundOrderStatus.Released };
        db.OutboundOrders.AddRange(orderA1, orderB1, orderC2A);

        db.StockOnHand.Add(new StockOnHand
        {
            Id = StockBinAId,
            ClientId = ClientA,
            FacilityId = FacilityA,
            ItemId = ItemA,
            LocationId = BinA,
            QtyAvailable = 20m,
            QtyAllocated = 10m,
            Version = 1
        });

        db.InventoryAllocations.AddRange(
            new InventoryAllocation
            {
                Id = AllocPendingAId, ClientId = ClientA, FacilityId = FacilityA,
                OutboundOrderId = OrderA1, OutboundOrderLineId = OrderLineA1, StockOnHandId = StockBinAId,
                ItemId = ItemA, LocationId = BinA, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated
            },
            new InventoryAllocation
            {
                Id = AllocShortAId, ClientId = ClientA, FacilityId = FacilityA,
                OutboundOrderId = OrderA1, OutboundOrderLineId = OrderLineA1, StockOnHandId = StockBinAId,
                ItemId = ItemA, LocationId = BinA, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated
            });

        db.PickWaves.AddRange(
            new PickWave { Id = WaveFacA, ClientId = ClientA, FacilityId = FacilityA, WaveNumber = "WV-740B-A", Priority = 2, Status = PickWaveStatus.Released },
            new PickWave { Id = WaveFacB, ClientId = ClientA, FacilityId = FacilityB, WaveNumber = "WV-740B-B", Priority = 1, Status = PickWaveStatus.Released });

        db.PickTasks.AddRange(
            new PickTask
            {
                Id = TaskPendingA, ClientId = ClientA, FacilityId = FacilityA, PickWaveId = WaveFacA,
                OutboundOrderId = OrderA1, OutboundOrderLineId = OrderLineA1, InventoryAllocationId = AllocPendingAId,
                StockOnHandId = StockBinAId, ItemId = ItemA, SourceLocationId = BinA,
                QuantityToPick = 5m, PickSequence = 2, Status = PickTaskStatus.Pending, Version = 1
            },
            new PickTask
            {
                Id = TaskAssignedOpA, ClientId = ClientA, FacilityId = FacilityA, PickWaveId = WaveFacA,
                OutboundOrderId = OrderA1, OutboundOrderLineId = OrderLineA1, InventoryAllocationId = AllocPendingAId,
                StockOnHandId = StockBinAId, ItemId = ItemA, SourceLocationId = BinA,
                QuantityToPick = 5m, PickSequence = 1, Status = PickTaskStatus.Assigned, AssignedUserId = OpAId, Version = 1
            },
            new PickTask
            {
                Id = TaskAssignedOpB, ClientId = ClientA, FacilityId = FacilityA, PickWaveId = WaveFacA,
                OutboundOrderId = OrderA1, OutboundOrderLineId = OrderLineA1, InventoryAllocationId = AllocPendingAId,
                StockOnHandId = StockBinAId, ItemId = ItemA, SourceLocationId = BinA,
                QuantityToPick = 5m, PickSequence = 3, Status = PickTaskStatus.Assigned, AssignedUserId = OpBId, Version = 1
            },
            new PickTask
            {
                Id = TaskUnclaimedShortA, ClientId = ClientA, FacilityId = FacilityA, PickWaveId = WaveFacA,
                OutboundOrderId = OrderA1, OutboundOrderLineId = OrderLineA1, InventoryAllocationId = AllocShortAId,
                StockOnHandId = StockBinAId, ItemId = ItemA, SourceLocationId = BinA,
                QuantityToPick = 5m, PickSequence = 4, Status = PickTaskStatus.Pending, Version = 1
            },
            new PickTask
            {
                Id = TaskPendingFacB, ClientId = ClientA, FacilityId = FacilityB, PickWaveId = WaveFacB,
                OutboundOrderId = OrderB1, OutboundOrderLineId = OrderLineA1, InventoryAllocationId = AllocPendingAId,
                StockOnHandId = StockBinAId, ItemId = ItemA, SourceLocationId = BinB,
                QuantityToPick = 5m, PickSequence = 1, Status = PickTaskStatus.Pending, Version = 1
            });

        db.HandlingUnits.Add(new HandlingUnit
        {
            Id = ToteAId, ClientId = ClientA, FacilityId = FacilityA,
            LpnCode = "TOTE-740B-01", UnitType = HandlingUnitType.Tote, Status = HandlingUnitStatus.Available,
            CurrentOutboundOrderId = OrderA1
        });

        var huCarton1 = Guid.NewGuid();
        var huCarton2 = Guid.NewGuid();
        var huCarton3 = Guid.NewGuid();
        var huCarton4 = Guid.NewGuid();
        var huCarton5 = Guid.NewGuid();
        db.HandlingUnits.AddRange(
            new HandlingUnit { Id = huCarton1, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-OPEN", UnitType = HandlingUnitType.ShippingCarton },
            new HandlingUnit { Id = huCarton2, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-PACKED", UnitType = HandlingUnitType.ShippingCarton },
            new HandlingUnit { Id = huCarton3, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-LABELED", UnitType = HandlingUnitType.ShippingCarton },
            new HandlingUnit { Id = huCarton4, ClientId = ClientA, FacilityId = FacilityA, LpnCode = "CTN-CONSOL", UnitType = HandlingUnitType.ShippingCarton },
            new HandlingUnit { Id = huCarton5, ClientId = ClientA, FacilityId = FacilityB, LpnCode = "CTN-FACB", UnitType = HandlingUnitType.ShippingCarton });

        db.Shipments.AddRange(
            new Shipment { Id = ShipmentFacA, ClientId = ClientA, FacilityId = FacilityA, ShipmentNumber = "SHP-740B-A", CarrierCode = "SIM-FEDEX", Status = ShipmentStatus.Draft },
            new Shipment { Id = ShipmentFacB, ClientId = ClientA, FacilityId = FacilityB, ShipmentNumber = "SHP-740B-B", CarrierCode = "SIM-UPS", Status = ShipmentStatus.ReadyForDispatch });

        db.PackingSessions.AddRange(
            new PackingSession { Id = PackingSessionAId, ClientId = ClientA, FacilityId = FacilityA, PackingStationLocationId = PackStationA, OutboundOrderId = OrderA1, OperatorUserId = OpAId, Status = PackingSessionStatus.Active },
            new PackingSession { Id = PackingSessionBId, ClientId = ClientA, FacilityId = FacilityB, PackingStationLocationId = BinB, OutboundOrderId = OrderB1, OperatorUserId = OpAId, Status = PackingSessionStatus.Active });

        db.Packages.AddRange(
            new Package { Id = PkgOpenId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = OrderA1, HandlingUnitId = huCarton1, PackageNumber = "PKG-OPEN", CartonSequence = 1, Status = PackageStatus.Open },
            new Package { Id = PkgEligiblePackedId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = OrderA1, HandlingUnitId = huCarton2, PackageNumber = "PKG-PACKED", CartonSequence = 2, Status = PackageStatus.Packed, CarrierCode = "SIM-FEDEX", PackedAtUtc = DateTime.UtcNow.AddMinutes(-5) },
            new Package { Id = PkgEligibleLabeledId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = OrderA1, HandlingUnitId = huCarton3, PackageNumber = "PKG-LABELED", CartonSequence = 3, Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-740B-ELIG", PackedAtUtc = DateTime.UtcNow.AddMinutes(-3) },
            new Package { Id = PkgAlreadyConsolidatedId, ClientId = ClientA, FacilityId = FacilityA, OutboundOrderId = OrderA1, ShipmentId = ShipmentFacA, HandlingUnitId = huCarton4, PackageNumber = "PKG-IN-SHP", CartonSequence = 4, Status = PackageStatus.LabelPrinted, CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-740B-SHP" },
            new Package { Id = PkgEligibleFacBId, ClientId = ClientA, FacilityId = FacilityB, OutboundOrderId = OrderB1, HandlingUnitId = huCarton5, PackageNumber = "PKG-FACB", CartonSequence = 1, Status = PackageStatus.Packed, CarrierCode = "SIM-UPS" });

        db.PackageLines.Add(new PackageLine
        {
            PackageId = PkgEligiblePackedId,
            OutboundOrderLineId = OrderLineA1,
            ItemId = ItemA,
            Quantity = 4m,
            InventoryAllocationId = AllocPendingAId
        });

        db.ShippingLabels.Add(new ShippingLabel
        {
            PackageId = PkgEligibleLabeledId,
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = "TRK-740B-ELIG",
            LabelData = "^XA^XZ",
            Status = ShippingLabelStatus.Generated
        });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. Permission Separation & Boundary Matrix
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RbacPermissionMatrix_EnforcesViewExecManageClientDriverAndAdminSeparation()
    {
        // View-only (`outbound:tasks:view`): allowed on supervisor queue, denied on /next, /claim, /confirm, /short-pick, /assign
        using (var r = Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks?facilityId={FacilityA}", "view-only"))
        using (var resp = await _http.SendAsync(r))
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        foreach (var req in new[]
        {
            Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityA}", "view-only"),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/claim", "view-only", new { }),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskAssignedOpA}/confirm", "view-only", new { scannedLocationBarcode = "BIN-740B-A1", scannedItemBarcode = "SKU-740B-01", scannedToteLpn = "TOTE-740B-01", quantity = 1m }),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskUnclaimedShortA}/short-pick", "view-only", new { quantityPicked = 0m, reason = 1, notes = "x" }),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/assign", "view-only", new { operatorUserId = OpAId })
        })
        {
            using (req)
            using (var resp = await _http.SendAsync(req))
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // Execution-only (`outbound:pick`): allowed on /next & /claim, denied on supervisor queue, detail, /assign
        using (var r = Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityA}", "exec-only-a"))
        using (var resp = await _http.SendAsync(r))
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        foreach (var req in new[]
        {
            Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks?facilityId={FacilityA}", "exec-only-a"),
            Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/{TaskPendingA}", "exec-only-a"),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/assign", "exec-only-a", new { operatorUserId = OpAId })
        })
        {
            using (req)
            using (var resp = await _http.SendAsync(req))
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // Manage-only (`outbound:tasks:view` + `outbound:tasks:manage`, NO `outbound:pick`): cannot call /next, /claim, /confirm, /short-pick
        using (var rAssign = JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/assign", "manage-only", new { operatorUserId = OpAId }))
        using (var respAssign = await _http.SendAsync(rAssign))
            Assert.Equal(HttpStatusCode.OK, respAssign.StatusCode);

        // Refuses assigning non-staff (Client) user
        using (var rBadAssign = JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/assign", "manage-only", new { operatorUserId = ClientUserId }))
        using (var respBadAssign = await _http.SendAsync(rBadAssign))
            Assert.Equal(HttpStatusCode.BadRequest, respBadAssign.StatusCode);

        foreach (var req in new[]
        {
            Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityA}", "manage-only"),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/claim", "manage-only", new { }),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskAssignedOpA}/confirm", "manage-only", new { scannedLocationBarcode = "BIN-740B-A1", scannedItemBarcode = "SKU-740B-01", scannedToteLpn = "TOTE-740B-01", quantity = 1m }),
            JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskUnclaimedShortA}/short-pick", "manage-only", new { quantityPicked = 0m, reason = 1, notes = "x" })
        })
        {
            using (req)
            using (var resp = await _http.SendAsync(req))
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // ClientAdmin (even holding legacy physical claims) & Driver denied all 7.4.0B read & claim endpoints; ordinary Admin w/o grants denied; unassigned staff denied
        foreach (var actor in new[] { "client-admin-legacy", "driver", "unassigned-staff", "ordinary-admin-no-grants" })
        {
            foreach (var req in new[]
            {
                Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks?facilityId={FacilityA}", actor),
                Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityA}", actor),
                JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/claim", actor, new { }),
                Request(HttpMethod.Get, $"/api/v1/outbound/shipments?facilityId={FacilityA}", actor),
                Request(HttpMethod.Get, $"/api/v1/outbound/shipments/eligible-packages?facilityId={FacilityA}", actor),
                Request(HttpMethod.Get, $"/api/v1/outbound/packing/sessions?facilityId={FacilityA}", actor),
                Request(HttpMethod.Get, $"/api/v1/outbound/packing/sessions/{PackingSessionAId}", actor),
                Request(HttpMethod.Get, $"/api/v1/outbound/packing/packages?facilityId={FacilityA}", actor)
            })
            {
                using (req)
                using (var resp = await _http.SendAsync(req))
                    Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            }
        }
    }

    [Fact]
    public async Task CrossFacility_MultiFacilityViewAllowsReads_StillForbidsClaimNextAndMutations()
    {
        foreach (var path in new[]
        {
            $"/api/v1/outbound/pick-tasks?facilityId={FacilityB}",
            $"/api/v1/outbound/shipments?facilityId={FacilityB}",
            $"/api/v1/outbound/shipments/eligible-packages?facilityId={FacilityB}",
            $"/api/v1/outbound/packing/sessions?facilityId={FacilityB}",
            $"/api/v1/outbound/packing/sessions/{PackingSessionBId}",
            $"/api/v1/outbound/packing/packages?facilityId={FacilityB}"
        })
        {
            using var req = Request(HttpMethod.Get, path, "multifac-staff-facA");
            using var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // Foreign facility /next and /claim still strictly forbidden
        using (var rNext = Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityB}", "multifac-staff-facA"))
        using (var respNext = await _http.SendAsync(rNext))
            Assert.Equal(HttpStatusCode.Forbidden, respNext.StatusCode);

        using (var rClaim = JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingFacB}/claim", "multifac-staff-facA", new { }))
        using (var respClaim = await _http.SendAsync(rClaim))
            Assert.Equal(HttpStatusCode.Forbidden, respClaim.StatusCode);

        // Strict SuperAdmin bypasses facility scoping
        using (var rSuper = JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingFacB}/claim", "superadmin", new { }))
        using (var respSuper = await _http.SendAsync(rSuper))
            Assert.Equal(HttpStatusCode.OK, respSuper.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. Strictly Read-Only /next, Atomic /claim Concurrency, and Ownership Execution Gate
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PickTasksNext_IsStrictlyReadOnly_PrioritizesCallerOwnAndExcludesForeignOperator()
    {
        // OpA already has TaskAssignedOpA (Assigned) -> /next returns TaskAssignedOpA first without mutating DB
        using (var rA = Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityA}", "exec-only-a"))
        using (var respA = await _http.SendAsync(rA))
        {
            Assert.Equal(HttpStatusCode.OK, respA.StatusCode);
            using var doc = JsonDocument.Parse(await respA.Content.ReadAsStringAsync());
            Assert.Equal(TaskAssignedOpA, doc.RootElement.GetProperty("id").GetGuid());
        }

        // OpB has TaskAssignedOpB -> returns TaskAssignedOpB (never TaskAssignedOpA!)
        using (var rB = Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={FacilityA}", "exec-only-b"))
        using (var respB = await _http.SendAsync(rB))
        {
            Assert.Equal(HttpStatusCode.OK, respB.StatusCode);
            using var doc = JsonDocument.Parse(await respB.Content.ReadAsStringAsync());
            Assert.Equal(TaskAssignedOpB, doc.RootElement.GetProperty("id").GetGuid());
        }

        // Verify TaskPendingA & TaskUnclaimedShortA remain strictly Pending / AssignedUserId == null (0 implicit mutations)
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pending = await db.PickTasks.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == TaskPendingA);
            Assert.Equal(PickTaskStatus.Pending, pending.Status);
            Assert.Null(pending.AssignedUserId);
            Assert.Empty(await db.PickTaskStatusHistories.ToListAsync());
        }
    }

    [Fact]
    public async Task AtomicClaim_ConcurrentRace_OneSucceedsOneGets409_RepeatedSameOperatorIsIdempotent()
    {
        // Competing concurrent claims by OpA and OpB on TaskPendingA -> 1x 200 + 1x 409
        var tA = _http.SendAsync(JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/claim", "exec-only-a", new { operatorUserId = OpBId }));
        var tB = _http.SendAsync(JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/claim", "exec-only-b", new { }));
        var responses = await Task.WhenAll(tA, tB);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var winnerResp = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
        Guid winnerId;
        uint winnerVersion;
        using (var doc = JsonDocument.Parse(await winnerResp.Content.ReadAsStringAsync()))
        {
            winnerId = doc.RootElement.GetProperty("assignedUserId").GetGuid();
            winnerVersion = doc.RootElement.GetProperty("version").GetUInt32();
            Assert.Equal("Assigned", doc.RootElement.GetProperty("status").GetString());
        }
        foreach (var r in responses) r.Dispose();

        // Repeated claim by winner is idempotent -> 200 OK without bumping Version or duplicating history
        var winnerActor = winnerId == OpAId ? "exec-only-a" : "exec-only-b";
        var loserActor = winnerId == OpAId ? "exec-only-b" : "exec-only-a";

        using (var repeatReq = JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/claim", winnerActor, new { }))
        using (var repeatResp = await _http.SendAsync(repeatReq))
        {
            Assert.Equal(HttpStatusCode.OK, repeatResp.StatusCode);
            using var doc = JsonDocument.Parse(await repeatResp.Content.ReadAsStringAsync());
            Assert.Equal(winnerId, doc.RootElement.GetProperty("assignedUserId").GetGuid());
            Assert.Equal(winnerVersion, doc.RootElement.GetProperty("version").GetUInt32());
        }

        // Subsequent claim by loser -> 409 Conflict
        using (var loserReq = JsonRequest(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{TaskPendingA}/claim", loserActor, new { }))
        using (var loserResp = await _http.SendAsync(loserReq))
        {
            Assert.Equal(HttpStatusCode.Conflict, loserResp.StatusCode);
        }

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.PickTaskStatusHistories.Where(h => h.PickTaskId == TaskPendingA).ToListAsync());
    }

    [Fact]
    public async Task RfExecutionOwnership_RequiresClaim_EnforcesForeign403_PreservesStockAndAuditsOverride()
    {
        // 1. Ordinary OpA CANNOT execute unclaimed Pending task directly -> 403 Forbidden
        using (var rUnclaimedConfirm = JsonRequest(
            HttpMethod.Post,
            $"/api/v1/outbound/pick-tasks/{TaskUnclaimedShortA}/confirm",
            "exec-only-a",
            new { scannedLocationBarcode = "BIN-740B-A1", scannedItemBarcode = "SKU-740B-01", scannedToteLpn = "TOTE-740B-01", quantity = 5m }))
        using (var resp = await _http.SendAsync(rUnclaimedConfirm))
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        // 2. Foreign OpB CANNOT execute TaskAssignedOpA -> 403 Forbidden
        using (var rForeignConfirm = JsonRequest(
            HttpMethod.Post,
            $"/api/v1/outbound/pick-tasks/{TaskAssignedOpA}/confirm",
            "exec-only-b",
            new { scannedLocationBarcode = "BIN-740B-A1", scannedItemBarcode = "SKU-740B-01", scannedToteLpn = "TOTE-740B-01", quantity = 5m }))
        using (var resp = await _http.SendAsync(rForeignConfirm))
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        // 3. Assigned OpA confirms TaskAssignedOpA -> 200 OK + atomic stock movement (source QtyAllocated 10->5, tote QtyAllocated +5)
        using (var rOwnerConfirm = JsonRequest(
            HttpMethod.Post,
            $"/api/v1/outbound/pick-tasks/{TaskAssignedOpA}/confirm",
            "exec-only-a",
            new { scannedLocationBarcode = "BIN-740B-A1", scannedItemBarcode = "SKU-740B-01", scannedToteLpn = "TOTE-740B-01", quantity = 5m, idempotencyKey = "idem-confirm-1" }))
        using (var respConfirm = await _http.SendAsync(rOwnerConfirm))
        {
            Assert.Equal(HttpStatusCode.OK, respConfirm.StatusCode);
        }

        // Duplicate confirm by OpA -> idempotent 200 OK without double-decrementing inventory!
        using (var rDupConfirm = JsonRequest(
            HttpMethod.Post,
            $"/api/v1/outbound/pick-tasks/{TaskAssignedOpA}/confirm",
            "exec-only-a",
            new { scannedLocationBarcode = "BIN-740B-A1", scannedItemBarcode = "SKU-740B-01", scannedToteLpn = "TOTE-740B-01", quantity = 5m, idempotencyKey = "idem-confirm-1" }))
        using (var respDup = await _http.SendAsync(rDupConfirm))
        {
            Assert.Equal(HttpStatusCode.OK, respDup.StatusCode);
        }

        // Terminal task short-pick -> 422 UnprocessableEntity (never double decrements)
        using (var rTermShort = JsonRequest(
            HttpMethod.Post,
            $"/api/v1/outbound/pick-tasks/{TaskAssignedOpA}/short-pick",
            "exec-only-a",
            new { quantityPicked = 0m, reason = 1, notes = "dup" }))
        using (var respTermShort = await _http.SendAsync(rTermShort))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, respTermShort.StatusCode);

        // 4. Supervisor holding Pick + TasksManage performs auditable override on TaskUnclaimedShortA (short-pick 0 units)
        using (var rOverride = JsonRequest(
            HttpMethod.Post,
            $"/api/v1/outbound/pick-tasks/{TaskUnclaimedShortA}/short-pick",
            "supervisor-override",
            new { quantityPicked = 0m, reason = 1, notes = "Verified empty bin on floor check" }))
        using (var respOverride = await _http.SendAsync(rOverride))
        {
            Assert.Equal(HttpStatusCode.OK, respOverride.StatusCode);
        }

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Verify inventory conservation: source bin QtyAvailable=20, QtyAllocated=0 (5 picked + 5 released on short-pick); tote QtyAllocated=5; Pick tx count = 1
        var srcStock = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.Id == StockBinAId);
        Assert.Equal(20m, srcStock.QtyAvailable);
        Assert.Equal(0m, srcStock.QtyAllocated);

        var toteStock = await db.StockOnHand.IgnoreQueryFilters().SingleAsync(s => s.HandlingUnitId == ToteAId);
        Assert.Equal(5m, toteStock.QtyAllocated);
        Assert.Equal(0m, toteStock.QtyAvailable);

        var pickTxs = await db.InventoryTransactions.IgnoreQueryFilters().Where(t => t.ReferenceId == TaskAssignedOpA && t.TxType == InventoryTxType.Pick).ToListAsync();
        Assert.Single(pickTxs);

        var overrideHistories = await db.PickTaskStatusHistories
            .Where(h => h.PickTaskId == TaskUnclaimedShortA && h.Reason != null && h.Reason.Contains("Task-management override"))
            .ToListAsync();
        Assert.Single(overrideHistories);
        Assert.Equal(SupervisorId, overrideHistories[0].ChangedByUserId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. Shipments, Eligible Packages, and Packing Workbench Read Contracts
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAndQueueEndpoints_ReturnFilteredPaginatedAccurateResults()
    {
        // A. GET /api/v1/outbound/pick-tasks with filter & pagination
        using (var r = Request(HttpMethod.Get, $"/api/v1/outbound/pick-tasks?facilityId={FacilityA}&status=Assigned&pageNumber=1&pageSize=10", "view-only"))
        using (var resp = await _http.SendAsync(r))
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal(2, doc.RootElement.GetProperty("totalCount").GetInt32());
        }

        // B. GET /api/v1/outbound/shipments filtered by carrier
        using (var r = Request(HttpMethod.Get, $"/api/v1/outbound/shipments?facilityId={FacilityA}&carrierCode=SIM-FEDEX", "ship-staff"))
        using (var resp = await _http.SendAsync(r))
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.Single(items);
            Assert.Equal("SHP-740B-A", items[0].GetProperty("shipmentNumber").GetString());
        }

        // C. GET /api/v1/outbound/shipments/eligible-packages -> ONLY Packed & LabelPrinted with ShipmentId == null
        using (var r = Request(HttpMethod.Get, $"/api/v1/outbound/shipments/eligible-packages?facilityId={FacilityA}", "ship-staff"))
        using (var resp = await _http.SendAsync(r))
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var ids = doc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
            Assert.Equal(2, ids.Count);
            Assert.Contains(PkgEligiblePackedId, ids);
            Assert.Contains(PkgEligibleLabeledId, ids);
            Assert.DoesNotContain(PkgOpenId, ids);
            Assert.DoesNotContain(PkgAlreadyConsolidatedId, ids);
        }

        // D. GET /api/v1/outbound/packing/sessions + /sessions/{id} + /packages
        using (var rSess = Request(HttpMethod.Get, $"/api/v1/outbound/packing/sessions?facilityId={FacilityA}", "pack-staff"))
        using (var respSess = await _http.SendAsync(rSess))
        {
            Assert.Equal(HttpStatusCode.OK, respSess.StatusCode);
            using var doc = JsonDocument.Parse(await respSess.Content.ReadAsStringAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        }

        using (var rDetail = Request(HttpMethod.Get, $"/api/v1/outbound/packing/sessions/{PackingSessionAId}", "pack-staff"))
        using (var respDetail = await _http.SendAsync(rDetail))
        {
            Assert.Equal(HttpStatusCode.OK, respDetail.StatusCode);
            using var doc = JsonDocument.Parse(await respDetail.Content.ReadAsStringAsync());
            Assert.Equal("SO-740B-A1", doc.RootElement.GetProperty("orderNumber").GetString());
            var line = Assert.Single(doc.RootElement.GetProperty("orderLines").EnumerateArray());
            Assert.Equal(10m, line.GetProperty("quantityOrdered").GetDecimal());
            Assert.Equal(4m, line.GetProperty("quantityPacked").GetDecimal());
            Assert.Equal(6m, line.GetProperty("remainingToPack").GetDecimal());
            Assert.Equal(4, doc.RootElement.GetProperty("packages").GetArrayLength());
            Assert.Single(doc.RootElement.GetProperty("pickedTotes").EnumerateArray());
        }

        using (var rPkgs = Request(HttpMethod.Get, $"/api/v1/outbound/packing/packages?packingSessionId={PackingSessionAId}&status=LabelPrinted", "pack-staff"))
        using (var respPkgs = await _http.SendAsync(rPkgs))
        {
            Assert.Equal(HttpStatusCode.OK, respPkgs.StatusCode);
            using var doc = JsonDocument.Parse(await respPkgs.Content.ReadAsStringAsync());
            Assert.Equal(2, doc.RootElement.GetProperty("totalCount").GetInt32());
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string actor)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("X-Test-Warehouse-Actor", actor);
        return req;
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, string actor, object body)
    {
        var req = Request(method, path, actor);
        req.Content = JsonContent.Create(body);
        return req;
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    private sealed class Phase740BTestAuthentication(
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
                case "view-only":
                    claims.Add(new(ClaimTypes.NameIdentifier, ViewOnlyUserId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new("permissions", Permissions.Outbound.TasksView));
                    break;

                case "exec-only-a":
                    claims.Add(new(ClaimTypes.NameIdentifier, OpAId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new("permissions", Permissions.Outbound.Pick));
                    break;

                case "exec-only-b":
                    claims.Add(new(ClaimTypes.NameIdentifier, OpBId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new("permissions", Permissions.Outbound.Pick));
                    break;

                case "manage-only":
                    claims.Add(new(ClaimTypes.NameIdentifier, SupervisorId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new("permissions", Permissions.Outbound.TasksView));
                    claims.Add(new("permissions", Permissions.Outbound.TasksManage));
                    break;

                case "supervisor-override":
                    claims.Add(new(ClaimTypes.NameIdentifier, SupervisorId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new("permissions", Permissions.Outbound.TasksView));
                    claims.Add(new("permissions", Permissions.Outbound.TasksManage));
                    claims.Add(new("permissions", Permissions.Outbound.Pick));
                    break;

                case "ship-staff":
                    claims.Add(new(ClaimTypes.NameIdentifier, SupervisorId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new("permissions", Permissions.Outbound.Ship));
                    break;

                case "pack-staff":
                    claims.Add(new(ClaimTypes.NameIdentifier, OpAId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new("permissions", Permissions.Outbound.Pack));
                    break;

                case "multifac-staff-facA":
                    claims.Add(new(ClaimTypes.NameIdentifier, SupervisorId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new("permissions", Permissions.Analytics.MultiFacilityView));
                    claims.Add(new("permissions", Permissions.Outbound.TasksView));
                    claims.Add(new("permissions", Permissions.Outbound.Pick));
                    claims.Add(new("permissions", Permissions.Outbound.Ship));
                    claims.Add(new("permissions", Permissions.Outbound.Pack));
                    break;

                case "client-admin-legacy":
                    claims.Add(new(ClaimTypes.NameIdentifier, ClientUserId.ToString()));
                    claims.Add(new("user_type", "Client"));
                    claims.Add(new("tenant_id", ClientA.ToString()));
                    claims.Add(new(ClaimTypes.Role, SystemRoles.ClientAdmin));
                    foreach (var p in new[]
                    {
                        Permissions.Outbound.TasksView, Permissions.Outbound.Pick,
                        Permissions.Outbound.Ship, Permissions.Outbound.Pack, Permissions.Dispatch.View
                    })
                    {
                        claims.Add(new("permissions", p));
                    }
                    break;

                case "driver":
                    claims.Add(new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
                    claims.Add(new("user_type", "Driver"));
                    claims.Add(new("facility_id", FacilityA.ToString()));
                    claims.Add(new("permissions", Permissions.Outbound.TasksView));
                    claims.Add(new("permissions", Permissions.Outbound.Pick));
                    claims.Add(new("permissions", Permissions.Outbound.Ship));
                    claims.Add(new("permissions", Permissions.Outbound.Pack));
                    break;

                case "unassigned-staff":
                    claims.Add(new(ClaimTypes.NameIdentifier, OpAId.ToString()));
                    claims.Add(new("user_type", "Staff"));
                    claims.Add(new("permissions", Permissions.Outbound.TasksView));
                    claims.Add(new("permissions", Permissions.Outbound.Pick));
                    claims.Add(new("permissions", Permissions.Outbound.Ship));
                    claims.Add(new("permissions", Permissions.Outbound.Pack));
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
