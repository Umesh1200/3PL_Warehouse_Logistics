using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Shipping;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public class DeliveryConfirmationTests
{
    private class TestEnvFixture
    {
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; } = Guid.NewGuid();
        public Guid LocationId { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; } = Guid.NewGuid();
        public string DbName { get; set; } = Guid.NewGuid().ToString();
    }

    private static async Task<TestEnvFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestEnvFixture();
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var client = new Client { Id = fix.ClientId, CompanyName = "Acme Global", AccountCode = "ACME" };
        var facility = new Facility { Id = fix.FacilityId, Name = "Chicago Hub", FacilityCode = "ORD1" };
        var loc = new Location
        {
            Id = fix.LocationId,
            FacilityId = fix.FacilityId,
            LocationCode = "LOC-01-A",
            Barcode = "LOC-01-A",
            LocationType = LocationType.Bin
        };
        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-DELIVERY-001",
            Description = "Precision Instrument",
            WeightKg = 1.0m,
            LengthCm = 10m,
            WidthCm = 10m,
            HeightCm = 10m
        };
        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LocationId = fix.LocationId,
            ItemId = fix.ItemId,
            QtyAvailable = 500m,
            QtyAllocated = 50m
        };

        context.Clients.Add(client);
        context.Facilities.Add(facility);
        context.Locations.Add(loc);
        context.Items.Add(item);
        context.StockOnHand.Add(stock);

        await context.SaveChangesAsync();
        return fix;
    }

    private static async Task<(OutboundOrder Order, Shipment Shipment, List<Package> Packages)> CreateDispatchedOrderAsync(
        TestEnvFixture fix,
        int packageCount = 1,
        string carrierCode = "SIM-FEDEX")
    {
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var orderId = Guid.NewGuid();
        var order = new OutboundOrder
        {
            Id = orderId,
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OrderNumber = $"ORD-CONF-{Guid.NewGuid().ToString()[..6]}",
            Status = OutboundOrderStatus.Completed, // Dispatched at warehouse dock
            DeliveryStatus = OrderDeliveryStatus.Dispatched
        };
        context.OutboundOrders.Add(order);

        var shipmentId = Guid.NewGuid();
        var shipment = new Shipment
        {
            Id = shipmentId,
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentNumber = $"SHP-CONF-{Guid.NewGuid().ToString()[..6]}",
            CarrierCode = carrierCode,
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.PendingHandover,
            DispatchedAtUtc = DateTime.UtcNow.AddHours(-3)
        };
        context.Shipments.Add(shipment);

        var packages = new List<Package>();
        for (int i = 1; i <= packageCount; i++)
        {
            var huId = Guid.NewGuid();
            var hu = new HandlingUnit
            {
                Id = huId,
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                LpnCode = $"HU-CONF-{i}-{Guid.NewGuid().ToString()[..4]}",
                UnitType = HandlingUnitType.ShippingCarton,
                Status = HandlingUnitStatus.InUse,
                CurrentLocationId = fix.LocationId,
                CurrentOutboundOrderId = orderId
            };
            context.HandlingUnits.Add(hu);

            var pkg = new Package
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OutboundOrderId = orderId,
                ShipmentId = shipmentId,
                HandlingUnitId = huId,
                PackageNumber = $"PKG-CONF-{i}-{Guid.NewGuid().ToString()[..4]}",
                CartonSequence = i,
                GrossWeightKg = 2.0m,
                Status = PackageStatus.Loaded,
                CarrierCode = carrierCode,
                CarrierServiceCode = "GROUND",
                TrackingNumber = $"TRK-{carrierCode}-{Guid.NewGuid().ToString()[..8]}",
                CurrentTrackingStatus = TrackingMilestoneStatus.PendingHandover
            };
            context.Packages.Add(pkg);
            packages.Add(pkg);
        }

        await context.SaveChangesAsync();
        return (order, shipment, packages);
    }

    private static async Task<ProofOfDelivery> CreateActiveFinalizedPodAsync(
        TestEnvFixture fix,
        Package pkg,
        DateTime? deliveredAtUtc = null,
        bool isStorageFinalized = true)
    {
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var pod = new ProofOfDelivery
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PackageId = pkg.Id,
            ShipmentId = pkg.ShipmentId,
            TrackingNumber = pkg.TrackingNumber!,
            CarrierCode = pkg.CarrierCode!,
            EvidenceSource = PodEvidenceSource.DriverHandheld,
            RecipientName = "John Consignee",
            DeliveryLocationDescription = "Rear Bay Door 2",
            IsSigned = true,
            SignatureObjectKey = $"tenants/{fix.ClientId}/pod/{pkg.Id}/sig.png",
            PhotoObjectKey = $"tenants/{fix.ClientId}/pod/{pkg.Id}/photo.jpg",
            SignatureSha256 = "1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff",
            PhotoSha256 = "aaaabbbbccccddddeeeeffff1111222233334444555566667777888899990000",
            IsStorageFinalized = isStorageFinalized,
            Latitude = 41.8781m,
            Longitude = -87.6298m,
            DeliveredAtUtc = deliveredAtUtc ?? DateTime.UtcNow.AddMinutes(-20),
            CapturedAtUtc = DateTime.UtcNow.AddMinutes(-18)
        };
        context.ProofOfDeliveries.Add(pod);
        await context.SaveChangesAsync();
        return pod;
    }

    [Fact]
    public async Task ConfirmDelivery_ValidFinalizedPod_ConfirmsDeliveryAndAppendsTrackingEvent()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkgs) = await CreateDispatchedOrderAsync(fix, 1);
        var pkg = pkgs[0];
        var deliveryTime = DateTime.UtcNow.AddMinutes(-30);
        var pod = await CreateActiveFinalizedPodAsync(fix, pkg, deliveryTime, isStorageFinalized: true);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        var result = await service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest
        {
            PackageId = pkg.Id,
            ProofOfDeliveryId = pod.Id
        });

        Assert.True(result.IsSuccess);
        Assert.False(result.IsAlreadyDelivered);
        Assert.Equal(pkg.Id, result.PackageId);
        Assert.Equal(TrackingMilestoneStatus.Delivered, result.PackageTrackingStatus);
        Assert.Equal(OrderDeliveryStatus.Delivered, result.OrderDeliveryStatus);
        Assert.Equal(deliveryTime, result.DeliveredAtUtc);

        // Verify package state in database
        var updatedPkg = await context.Packages.FindAsync(pkg.Id);
        Assert.NotNull(updatedPkg);
        Assert.Equal(TrackingMilestoneStatus.Delivered, updatedPkg.CurrentTrackingStatus);
        Assert.Equal(deliveryTime, updatedPkg.DeliveredAtUtc);
        Assert.NotNull(updatedPkg.TrackingStatusUpdatedAtUtc);

        // Verify tracking event created
        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(e => e.PackageId == pkg.Id && e.CarrierEventCode == "POD_CONFIRMED");
        Assert.NotNull(trackingEvent);
        Assert.Equal(TrackingMilestoneStatus.Delivered, trackingEvent.MilestoneStatus);
        Assert.Equal(deliveryTime, trackingEvent.OccurredAtUtc);
        Assert.Contains("DriverHandheld", trackingEvent.CarrierDescription);
        Assert.Contains("John Consignee", trackingEvent.CarrierDescription);

        // Verify shipment status updated
        var updatedShipment = await context.Shipments.FindAsync(shipment.Id);
        Assert.NotNull(updatedShipment);
        Assert.Equal(TrackingMilestoneStatus.Delivered, updatedShipment.DeliveryStatus);

        // Verify order status updated while fulfillment completion status remains intact
        var updatedOrder = await context.OutboundOrders.FindAsync(order.Id);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OutboundOrderStatus.Completed, updatedOrder.Status); // Fulfillment untouched
        Assert.Equal(OrderDeliveryStatus.Delivered, updatedOrder.DeliveryStatus); // Delivery status rolled up
    }

    [Fact]
    public async Task ConfirmDelivery_MissingPod_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkgs) = await CreateDispatchedOrderAsync(fix, 1);
        var pkg = pkgs[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg.Id }));

        Assert.Contains("without an authoritative Proof of Delivery record", ex.Message);
    }

    [Fact]
    public async Task ConfirmDelivery_UnfinalizedPod_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkgs) = await CreateDispatchedOrderAsync(fix, 1);
        var pkg = pkgs[0];
        // POD with IsStorageFinalized = false
        var pod = await CreateActiveFinalizedPodAsync(fix, pkg, isStorageFinalized: false);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg.Id, ProofOfDeliveryId = pod.Id }));

        Assert.Contains("still pending storage finalization", ex.Message);
    }

    [Fact]
    public async Task ConfirmDelivery_CrossTenantAttempt_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkgs) = await CreateDispatchedOrderAsync(fix, 1);
        var pkg = pkgs[0];
        var pod = await CreateActiveFinalizedPodAsync(fix, pkg);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId };

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest
            {
                PackageId = pkg.Id,
                ProofOfDeliveryId = pod.Id,
                TenantIdOverride = Guid.NewGuid() // Different tenant
            }));

        Assert.Contains("different client tenant", ex.Message);
    }

    [Fact]
    public async Task ConfirmDelivery_UnauthorizedFacility_ThrowsForbiddenException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkgs) = await CreateDispatchedOrderAsync(fix, 1);
        var pkg = pkgs[0];
        var pod = await CreateActiveFinalizedPodAsync(fix, pkg);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        // User assigned to a DIFFERENT facility
        var userContext = new TestCurrentUserContext
        {
            UserType = UserType.Staff,
            TenantId = fix.ClientId,
            FacilityId = Guid.NewGuid()
        };

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg.Id, ProofOfDeliveryId = pod.Id }));
    }

    [Fact]
    public async Task ConfirmDelivery_InvalidPackageState_OpenOrCancelled_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkgs) = await CreateDispatchedOrderAsync(fix, 1);
        var pkg = pkgs[0];
        var pod = await CreateActiveFinalizedPodAsync(fix, pkg);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };

        // Set package to Open
        var pkgEntity = await context.Packages.FindAsync(pkg.Id);
        pkgEntity!.Status = PackageStatus.Open;
        await context.SaveChangesAsync();

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg.Id, ProofOfDeliveryId = pod.Id }));

        Assert.Contains("Package must be Packed, Staged, or Loaded", ex.Message);
    }

    [Fact]
    public async Task ConfirmDelivery_UndispatchedShipment_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, shipment, pkgs) = await CreateDispatchedOrderAsync(fix, 1);
        var pkg = pkgs[0];
        var pod = await CreateActiveFinalizedPodAsync(fix, pkg);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };

        // Set shipment back to ReadyForDispatch (not yet dispatched / custody-transferred)
        var shpEntity = await context.Shipments.FindAsync(shipment.Id);
        shpEntity!.Status = ShipmentStatus.ReadyForDispatch;
        await context.SaveChangesAsync();

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg.Id, ProofOfDeliveryId = pod.Id }));

        Assert.Contains("must complete authorized custody-transfer and dispatch", ex.Message);
    }

    [Fact]
    public async Task ConfirmDelivery_IdempotentDuplicate_ReturnsSuccessWithoutDuplicateEvent()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkgs) = await CreateDispatchedOrderAsync(fix, 1);
        var pkg = pkgs[0];
        var pod = await CreateActiveFinalizedPodAsync(fix, pkg);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        // First confirmation
        var result1 = await service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg.Id });
        Assert.True(result1.IsSuccess);
        Assert.False(result1.IsAlreadyDelivered);

        // Second confirmation attempt for same package
        var result2 = await service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg.Id });
        Assert.True(result2.IsSuccess);
        Assert.True(result2.IsAlreadyDelivered);

        // Assert exactly one POD_CONFIRMED tracking event exists
        int eventCount = await context.ShipmentTrackingEvents
            .CountAsync(e => e.PackageId == pkg.Id && e.CarrierEventCode == "POD_CONFIRMED");
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task MultiPackageOrderRollup_PartialThenCompleteDelivery_RollsUpAccurately()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkgs) = await CreateDispatchedOrderAsync(fix, 2);
        var pkg1 = pkgs[0];
        var pkg2 = pkgs[1];

        var pod1 = await CreateActiveFinalizedPodAsync(fix, pkg1);
        var pod2 = await CreateActiveFinalizedPodAsync(fix, pkg2);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        // 1. Confirm package 1
        var result1 = await service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg1.Id });
        Assert.True(result1.IsSuccess);
        Assert.Equal(OrderDeliveryStatus.PartiallyDelivered, result1.OrderDeliveryStatus);

        var orderAfter1 = await context.OutboundOrders.FindAsync(order.Id);
        Assert.Equal(OrderDeliveryStatus.PartiallyDelivered, orderAfter1!.DeliveryStatus);

        // 2. Confirm package 2
        var result2 = await service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg2.Id });
        Assert.True(result2.IsSuccess);
        Assert.Equal(OrderDeliveryStatus.Delivered, result2.OrderDeliveryStatus);

        var orderAfter2 = await context.OutboundOrders.FindAsync(order.Id);
        Assert.Equal(OrderDeliveryStatus.Delivered, orderAfter2!.DeliveryStatus);
        Assert.NotNull(orderAfter2.DeliveredAtUtc);
    }

    [Fact]
    public async Task MultiShipmentOrderRollup_AggregatesPackagesAcrossMultipleShipments()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        // Order fulfilled through TWO separate shipments
        var order = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OrderNumber = "ORD-MULTI-SHP-001",
            Status = OutboundOrderStatus.Completed,
            DeliveryStatus = OrderDeliveryStatus.Dispatched
        };
        context.OutboundOrders.Add(order);

        // Shipment 1 (FedEx)
        var shipment1 = new Shipment
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentNumber = "SHP-FEDEX-01",
            CarrierCode = "FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.PendingHandover
        };
        context.Shipments.Add(shipment1);

        var pkg1 = new Package
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OutboundOrderId = order.Id,
            ShipmentId = shipment1.Id,
            PackageNumber = "PKG-FEDEX-01",
            Status = PackageStatus.Loaded,
            CarrierCode = "FEDEX",
            TrackingNumber = "TRK-FEDEX-001",
            CurrentTrackingStatus = TrackingMilestoneStatus.PendingHandover
        };
        context.Packages.Add(pkg1);

        // Shipment 2 (UPS)
        var shipment2 = new Shipment
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentNumber = "SHP-UPS-02",
            CarrierCode = "UPS",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.PendingHandover
        };
        context.Shipments.Add(shipment2);

        var pkg2 = new Package
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OutboundOrderId = order.Id,
            ShipmentId = shipment2.Id,
            PackageNumber = "PKG-UPS-02",
            Status = PackageStatus.Loaded,
            CarrierCode = "UPS",
            TrackingNumber = "TRK-UPS-002",
            CurrentTrackingStatus = TrackingMilestoneStatus.PendingHandover
        };
        context.Packages.Add(pkg2);

        await context.SaveChangesAsync();

        var pod1 = await CreateActiveFinalizedPodAsync(fix, pkg1);
        var pod2 = await CreateActiveFinalizedPodAsync(fix, pkg2);

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        // Confirm package 1 (Shipment 1)
        var result1 = await service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg1.Id });
        Assert.Equal(OrderDeliveryStatus.PartiallyDelivered, result1.OrderDeliveryStatus);

        var shp1Refreshed = await context.Shipments.FindAsync(shipment1.Id);
        Assert.Equal(TrackingMilestoneStatus.Delivered, shp1Refreshed!.DeliveryStatus);

        var shp2Refreshed = await context.Shipments.FindAsync(shipment2.Id);
        Assert.Equal(TrackingMilestoneStatus.PendingHandover, shp2Refreshed!.DeliveryStatus);

        // Confirm package 2 (Shipment 2)
        var result2 = await service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg2.Id });
        Assert.Equal(OrderDeliveryStatus.Delivered, result2.OrderDeliveryStatus);

        var orderRefreshed = await context.OutboundOrders.FindAsync(order.Id);
        Assert.Equal(OrderDeliveryStatus.Delivered, orderRefreshed!.DeliveryStatus);
    }

    [Fact]
    public async Task MultiPackageOrderRollup_UnresolvedException_PreservesOrderDeliveryException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, _, pkgs) = await CreateDispatchedOrderAsync(fix, 2);
        var pkg1 = pkgs[0];
        var pkg2 = pkgs[1];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        // Package 2 suffers an unresolved delivery exception
        var pkg2Entity = await context.Packages.FindAsync(pkg2.Id);
        pkg2Entity!.CurrentTrackingStatus = TrackingMilestoneStatus.DeliveryException;
        await context.SaveChangesAsync();

        var pod1 = await CreateActiveFinalizedPodAsync(fix, pkg1);

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        // Confirm package 1 delivery
        var result = await service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg1.Id });
        Assert.True(result.IsSuccess);

        // Order delivery status must prioritize unresolved DeliveryException over PartiallyDelivered
        Assert.Equal(OrderDeliveryStatus.DeliveryException, result.OrderDeliveryStatus);

        var orderAfter = await context.OutboundOrders.FindAsync(order.Id);
        Assert.Equal(OrderDeliveryStatus.DeliveryException, orderAfter!.DeliveryStatus);
    }

    [Fact]
    public async Task MonotonicProgression_DeliveredPackageCannotBeRegressedByLateTransitScan()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkgs) = await CreateDispatchedOrderAsync(fix, 1);
        var pkg = pkgs[0];
        var pod = await CreateActiveFinalizedPodAsync(fix, pkg);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        // Confirm delivery
        await service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg.Id });

        // Late InTransit scan arrives
        var confirmedPkg = await context.Packages.FindAsync(pkg.Id);
        confirmedPkg!.UpdateTrackingMilestone(TrackingMilestoneStatus.InTransit, DateTime.UtcNow.AddMinutes(5));
        await context.SaveChangesAsync();

        // Must still be Delivered
        Assert.Equal(TrackingMilestoneStatus.Delivered, confirmedPkg.CurrentTrackingStatus);
    }

    [Fact]
    public async Task Invariant_WarehouseInventoryRemainsUnchanged_ZeroMutation()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkgs) = await CreateDispatchedOrderAsync(fix, 1);
        var pkg = pkgs[0];
        var pod = await CreateActiveFinalizedPodAsync(fix, pkg);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };

        // Record initial inventory state
        var initialStock = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        decimal initialTotal = initialStock.TotalQuantity;
        decimal initialAllocated = initialStock.QtyAllocated;
        decimal initialAvailable = initialStock.QtyAvailable;
        int initialTxCount = await context.InventoryTransactions.CountAsync();

        var service = new DeliveryConfirmationService(context, tenantProvider, userContext, NullLogger<DeliveryConfirmationService>.Instance);

        await service.ConfirmDeliveryAsync(new ConfirmPackageDeliveryRequest { PackageId = pkg.Id });

        // Verify stock balances and transactions underwent zero change (ΔQ = 0)
        var stockAfter = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        Assert.Equal(initialTotal, stockAfter.TotalQuantity);
        Assert.Equal(initialAllocated, stockAfter.QtyAllocated);
        Assert.Equal(initialAvailable, stockAfter.QtyAvailable);

        int txCountAfter = await context.InventoryTransactions.CountAsync();
        Assert.Equal(initialTxCount, txCountAfter);
    }
}

