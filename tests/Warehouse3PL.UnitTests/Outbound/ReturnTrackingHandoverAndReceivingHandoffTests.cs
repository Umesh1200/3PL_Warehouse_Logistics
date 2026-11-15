using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Features.Outbound.Tracking.Commands;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Shipping;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Outbound;

public class ReturnTrackingHandoverAndReceivingHandoffTests
{
    private class TestEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid ForeignClientId { get; set; } = Guid.NewGuid();
        public Guid OriginFacilityId { get; set; } = Guid.NewGuid();
        public Guid DestinationFacilityId { get; set; } = Guid.NewGuid();
        public Guid ForeignFacilityId { get; set; } = Guid.NewGuid();
        public Guid StaffUserId { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; } = Guid.NewGuid();
    }

    private static async Task<TestEnvFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestEnvFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var client = new Client
        {
            Id = fix.ClientId,
            CompanyName = "Apex Precision Components",
            AccountCode = $"APC{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Rival Distribution",
            AccountCode = $"RVD{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var originFacility = new Facility
        {
            Id = fix.OriginFacilityId,
            Name = "Louisville Central Hub",
            FacilityCode = "SDF1",
            AddressLine1 = "100 Logistics Blvd",
            City = "Louisville",
            StateOrProvince = "KY",
            PostalCode = "40202",
            CountryCode = "US"
        };

        var destinationFacility = new Facility
        {
            Id = fix.DestinationFacilityId,
            Name = "Lexington Returns Center",
            FacilityCode = "LEX1",
            AddressLine1 = "500 Reverse Logistics Pkwy",
            City = "Lexington",
            StateOrProvince = "KY",
            PostalCode = "40509",
            CountryCode = "US"
        };

        var foreignFacility = new Facility
        {
            Id = fix.ForeignFacilityId,
            Name = "Cincinnati Hub",
            FacilityCode = "CVG1",
            AddressLine1 = "200 Ohio River Rd",
            City = "Cincinnati",
            StateOrProvince = "OH",
            PostalCode = "45202",
            CountryCode = "US"
        };

        var staffUser = new User
        {
            Id = fix.StaffUserId,
            Email = "returns.lead@warehouse3pl.test",
            PasswordHash = "hash",
            FirstName = "Marcus",
            LastName = "Vance",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = $"SKU-REV-TRK-{Guid.NewGuid().ToString()[..6]}",
            Description = "High-Torque Servo Motor",
            WeightKg = 3.2m,
            LengthCm = 20m,
            WidthCm = 15m,
            HeightCm = 12m
        };

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.OriginFacilityId,
            LocationCode = "BIN-REV-TRK-01",
            Barcode = "BIN-REV-TRK-01",
            LocationType = LocationType.Bin
        };

        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            LocationId = loc.Id,
            ItemId = item.Id,
            QtyAvailable = 250m,
            QtyAllocated = 25m
        };

        context.Clients.AddRange(client, foreignClient);
        context.Facilities.AddRange(originFacility, destinationFacility, foreignFacility);
        context.Users.Add(staffUser);
        context.Items.Add(item);
        context.Locations.Add(loc);
        context.StockOnHand.Add(stock);

        await context.SaveChangesAsync();
        return fix;
    }

    private static async Task<(OutboundOrder Order, Shipment Shipment, Package Package, ShipmentDeliveryException Exception, ReturnAuthorization Auth, ReturnShipment ReturnShipment)> CreateReturnShipmentFixtureAsync(
        TestEnvFixture fix)
    {
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var orderId = Guid.NewGuid();
        var order = new OutboundOrder
        {
            Id = orderId,
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            OrderNumber = $"ORD-REV-{Guid.NewGuid().ToString()[..6]}",
            Status = OutboundOrderStatus.Completed,
            DeliveryStatus = OrderDeliveryStatus.DeliveryException
        };
        context.OutboundOrders.Add(order);

        var shippingAddress = new ShippingAddress
        {
            Id = Guid.NewGuid(),
            OutboundOrderId = orderId,
            RecipientName = "Jane Recipient",
            CompanyName = "Customer Industries",
            AddressLine1 = "777 Consignee Blvd",
            City = "Louisville",
            StateOrProvince = "KY",
            PostalCode = "40202",
            CountryCode = "US"
        };
        context.ShippingAddresses.Add(shippingAddress);
        order.SetShippingAddress(shippingAddress);

        var shipmentId = Guid.NewGuid();
        var shipment = new Shipment
        {
            Id = shipmentId,
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            ShipmentNumber = $"SHP-REV-{Guid.NewGuid().ToString()[..6]}",
            CarrierCode = "SIM-FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.DeliveryException,
            DispatchedAtUtc = DateTime.UtcNow.AddDays(-2)
        };
        context.Shipments.Add(shipment);

        var pkg = new Package
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            OutboundOrderId = orderId,
            ShipmentId = shipmentId,
            PackageNumber = $"PKG-REV-{Guid.NewGuid().ToString()[..6]}",
            TrackingNumber = $"TRK-OUT-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            CarrierCode = "SIM-FEDEX",
            Status = PackageStatus.Loaded,
            CurrentTrackingStatus = TrackingMilestoneStatus.DeliveryException,
            TrackingStatusUpdatedAtUtc = DateTime.UtcNow.AddHours(-12)
        };
        context.Packages.Add(pkg);

        var exc = new ShipmentDeliveryException
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            PackageId = pkg.Id,
            ShipmentId = shipmentId,
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber,
            CarrierExceptionCode = "CUSTOMER_REFUSED",
            ExceptionType = DeliveryExceptionType.CustomerRefused,
            Severity = DeliveryExceptionSeverity.High,
            CarrierDescription = "Customer refused delivery on site.",
            ReattemptCount = 1,
            OccurredAtUtc = DateTime.UtcNow.AddHours(-10),
            Status = DeliveryExceptionStatus.PendingReturnAuthorization
        };
        context.ShipmentDeliveryExceptions.Add(exc);

        var authNumber = $"RTS-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
        var auth = ReturnAuthorization.Create(
            clientId: fix.ClientId,
            originFacilityId: fix.OriginFacilityId,
            destinationFacilityId: fix.DestinationFacilityId,
            orderId: orderId,
            originalShipmentId: shipmentId,
            packageId: pkg.Id,
            deliveryExceptionId: exc.Id,
            authorizationNumber: authNumber,
            reason: ReturnReason.CustomerRefused,
            notes: "Authorized reverse transit to Lexington returns center.",
            authorizedByUserId: fix.StaffUserId,
            rmaNumber: "RMA-APEX-888",
            expiresAtUtc: DateTime.UtcNow.AddDays(14));

        context.ReturnAuthorizations.Add(auth);
        exc.AuthorizeReturnToSender(auth.Id, null, "Authorized RTS", fix.StaffUserId);

        var returnTrackingNumber = $"TRK-REV-FEDEX-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
        var returnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            OriginFacilityId = fix.OriginFacilityId,
            DestinationFacilityId = fix.DestinationFacilityId,
            OrderId = orderId,
            OriginalShipmentId = shipmentId,
            PackageId = pkg.Id,
            DeliveryExceptionId = exc.Id,
            ReturnAuthorizationId = auth.Id,
            ReturnRmaNumber = auth.RmaNumber!,
            Reason = ReturnReason.CustomerRefused,
            AuthorizedByUserId = fix.StaffUserId,
            AuthorizedAtUtc = DateTime.UtcNow,
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = returnTrackingNumber,
            CarrierServiceLevel = "StandardGround",
            LabelFormat = "ZPL",
            LabelData = "^XA^FDRETURN^XZ",
            RateCharged = 15.50m,
            Currency = "USD",
            OriginAddressLine1 = "777 Consignee Blvd",
            OriginCity = "Louisville",
            OriginState = "KY",
            OriginPostalCode = "40202",
            OriginCountry = "US",
            DestinationAddressLine1 = "500 Reverse Logistics Pkwy",
            DestinationCity = "Lexington",
            DestinationState = "KY",
            DestinationPostalCode = "40509",
            DestinationCountry = "US",
            Status = ReturnShipmentStatus.Authorized
        };
        context.ReturnShipments.Add(returnShipment);

        auth.LinkReturnShipment(returnShipment.Id);

        await context.SaveChangesAsync();
        return (order, shipment, pkg, exc, auth, returnShipment);
    }

    [Fact]
    public async Task IngestReturnTracking_FirstInTransitEvent_RecordsCarrierHandoverAndTransitionsToInTransit()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var handoverTime = DateTime.UtcNow.AddHours(-3);
        var request = new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "IN_TRANSIT",
            CarrierDescription = "Carrier accepted parcel and departed origin sorting hub.",
            OccurredAtUtc = handoverTime,
            EventCity = "Louisville",
            EventStateOrProvince = "KY",
            EventPostalCode = "40202"
        };

        var result = await service.IngestReturnTrackingEventAsync(request);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsDuplicate);
        Assert.Equal(retShipment.Id, result.ReturnShipmentId);
        Assert.Equal(auth.Id, result.ReturnAuthorizationId);
        Assert.Equal(ReturnShipmentStatus.InTransitToWarehouse, result.ShipmentStatus);
        Assert.Equal(ReturnAuthorizationStatus.InTransit, result.AuthorizationStatus);
        Assert.Equal(handoverTime, result.CarrierHandoverAtUtc);
        Assert.Null(result.DeliveredAtWarehouseUtc);
        Assert.Null(result.InboundHandoffId);

        // Verify DB State
        var reloadedShipment = await context.ReturnShipments.FindAsync(retShipment.Id);
        Assert.NotNull(reloadedShipment);
        Assert.Equal(ReturnShipmentStatus.InTransitToWarehouse, reloadedShipment.Status);
        Assert.Equal(handoverTime, reloadedShipment.CarrierHandoverAtUtc);

        var reloadedAuth = await context.ReturnAuthorizations.FindAsync(auth.Id);
        Assert.NotNull(reloadedAuth);
        Assert.Equal(ReturnAuthorizationStatus.InTransit, reloadedAuth.Status);
        Assert.Equal(handoverTime, reloadedAuth.InTransitAtUtc);

        // Verify Audit Event Appended
        var count = await context.ReturnTrackingEvents.CountAsync(e => e.ReturnShipmentId == retShipment.Id);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task IngestReturnTracking_DelayedPickupAfterInTransit_DoesNotOverwriteHandoverOrRegressStatus()
    {
        // Concurrency / Out-of-order scenario from user specification:
        // 1. Carrier sends first IN_TRANSIT event
        // 2. System records carrier handover
        // 3. Delayed PICKUP event arrives
        // 4. System stores delayed event without regressing state or overwriting handover timestamp
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var inTransitTime = DateTime.UtcNow.AddHours(-2);
        var pickupTime = DateTime.UtcNow.AddHours(-4); // Delayed event occurred earlier

        // Step 1: Ingest first IN_TRANSIT event
        var inTransitResult = await service.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "IN_TRANSIT",
            CarrierDescription = "Departed local carrier hub",
            OccurredAtUtc = inTransitTime
        });
        Assert.True(inTransitResult.IsSuccess);
        Assert.Equal(inTransitTime, inTransitResult.CarrierHandoverAtUtc);

        // Step 2: Delayed PICKUP arrives
        var pickupResult = await service.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "PICKUP",
            CarrierDescription = "Driver picked up package from customer",
            OccurredAtUtc = pickupTime
        });

        Assert.True(pickupResult.IsSuccess);
        Assert.False(pickupResult.IsDuplicate);

        // Invariant: Handover timestamp must remain the first verified timestamp (inTransitTime)
        Assert.Equal(inTransitTime, pickupResult.CarrierHandoverAtUtc);
        Assert.Equal(ReturnShipmentStatus.InTransitToWarehouse, pickupResult.ShipmentStatus);
        Assert.Equal(ReturnAuthorizationStatus.InTransit, pickupResult.AuthorizationStatus);

        // Verify both events persisted in DB timeline
        var totalEvents = await context.ReturnTrackingEvents.CountAsync(e => e.ReturnShipmentId == retShipment.Id);
        Assert.Equal(2, totalEvents);
    }

    [Fact]
    public async Task IngestReturnTracking_OutForDelivery_AdvancesShipmentToOutForDelivery()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var ofdTime = DateTime.UtcNow.AddMinutes(-45);
        var result = await service.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "OUT_FOR_DELIVERY",
            CarrierDescription = "Loaded on delivery vehicle for destination warehouse delivery",
            OccurredAtUtc = ofdTime
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(ReturnShipmentStatus.OutForDeliveryToWarehouse, result.ShipmentStatus);
        Assert.Equal(ReturnAuthorizationStatus.InTransit, result.AuthorizationStatus);
        Assert.Equal(ofdTime, result.CarrierHandoverAtUtc);
    }

    [Fact]
    public async Task IngestReturnTracking_DeliveredEvent_TransitionsToDeliveredAndCreatesInboundHandoff()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var deliveryTime = DateTime.UtcNow.AddMinutes(-10);
        var request = new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "DELIVERED",
            CarrierDescription = "Delivered to warehouse dock door 4.",
            OccurredAtUtc = deliveryTime,
            DestinationFacilityCode = "LEX1",
            EventPostalCode = "40509",
            DockDoorLocation = "DOCK-DOOR-04"
        };

        var result = await service.IngestReturnTrackingEventAsync(request);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsDuplicate);
        Assert.Equal(ReturnShipmentStatus.DeliveredAtWarehouse, result.ShipmentStatus);
        Assert.Equal(ReturnAuthorizationStatus.DeliveredAtFacility, result.AuthorizationStatus);
        Assert.Equal(deliveryTime, result.DeliveredAtWarehouseUtc);
        Assert.NotNull(result.InboundHandoffId);
        Assert.NotNull(result.InboundHandoffReference);
        Assert.StartsWith("HND-RMA-APEX-888", result.InboundHandoffReference);

        // Verify Inbound Handoff persisted in DB
        var handoff = await context.ReturnInboundHandoffs
            .Include(h => h.DestinationFacility)
            .FirstOrDefaultAsync(h => h.Id == result.InboundHandoffId);

        Assert.NotNull(handoff);
        Assert.Equal(fix.ClientId, handoff.ClientId);
        Assert.Equal(fix.DestinationFacilityId, handoff.DestinationFacilityId);
        Assert.Equal(retShipment.Id, handoff.ReturnShipmentId);
        Assert.Equal(auth.Id, handoff.ReturnAuthorizationId);
        Assert.Equal(pkg.Id, handoff.OriginalPackageId);
        Assert.Equal(order.Id, handoff.OriginalOrderId);
        Assert.Equal(retShipment.TrackingNumber, handoff.TrackingNumber);
        Assert.Equal("SIM-FEDEX", handoff.CarrierCode);
        Assert.Equal(ReturnInboundHandoffStatus.PendingReceiving, handoff.Status);
        Assert.Equal(deliveryTime, handoff.CarrierDeliveredAtUtc);
        Assert.Equal("DOCK-DOOR-04", handoff.DockDoorLocation);

        // Verify Navigations on ReturnShipment and ReturnAuthorization
        var reloadedShipment = await context.ReturnShipments.FindAsync(retShipment.Id);
        Assert.NotNull(reloadedShipment);
        Assert.Equal(handoff.Id, reloadedShipment.InboundHandoffId);
        Assert.Equal(ReturnShipmentStatus.DeliveredAtWarehouse, reloadedShipment.Status);

        var reloadedAuth = await context.ReturnAuthorizations.FindAsync(auth.Id);
        Assert.NotNull(reloadedAuth);
        Assert.Equal(handoff.Id, reloadedAuth.InboundHandoffId);
        Assert.Equal(ReturnAuthorizationStatus.DeliveredAtFacility, reloadedAuth.Status);
        Assert.Equal(deliveryTime, reloadedAuth.DeliveredAtFacilityAtUtc);
    }

    [Fact]
    public async Task IngestReturnTracking_DuplicateDeliveredEvent_IsIdempotentAndDoesNotDuplicateHandoff()
    {
        // Scenario from user review:
        // Duplicate DELIVERED event arrives -> only one receiving handoff is generated.
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var deliveryTime = DateTime.UtcNow.AddMinutes(-15);
        var request = new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "DELIVERED",
            CarrierDescription = "Delivered to receiving dock.",
            OccurredAtUtc = deliveryTime,
            DestinationFacilityCode = "LEX1",
            EventPostalCode = "40509"
        };

        // First delivery ingestion
        var firstResult = await service.IngestReturnTrackingEventAsync(request);
        Assert.True(firstResult.IsSuccess);
        Assert.False(firstResult.IsDuplicate);
        Assert.NotNull(firstResult.InboundHandoffId);

        // Second delivery ingestion (Duplicate)
        var secondResult = await service.IngestReturnTrackingEventAsync(request);
        Assert.True(secondResult.IsSuccess);
        Assert.True(secondResult.IsDuplicate);
        Assert.Equal(firstResult.InboundHandoffId, secondResult.InboundHandoffId);
        Assert.Equal(firstResult.InboundHandoffReference, secondResult.InboundHandoffReference);

        // Critical Check: Exactly ONE receiving handoff exists in DB
        var totalHandoffs = await context.ReturnInboundHandoffs.CountAsync(h => h.ReturnShipmentId == retShipment.Id);
        Assert.Equal(1, totalHandoffs);
    }

    [Fact]
    public async Task IngestReturnTracking_DeliveredDirectlyFromAuthorizedWithoutShipment_ThrowsInvalidDomainStateException()
    {
        // Scenario from user review:
        // A DELIVERED event cannot move an authorization directly from Authorized to DeliveredAtFacility
        // without the required shipment and custody relationship.
        var fix = await SeedTestEnvironmentAsync();

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        // 1. Test direct aggregate guard on ReturnAuthorization
        var auth = ReturnAuthorization.Create(
            clientId: fix.ClientId,
            originFacilityId: fix.OriginFacilityId,
            destinationFacilityId: fix.DestinationFacilityId,
            orderId: Guid.NewGuid(),
            originalShipmentId: Guid.NewGuid(),
            packageId: Guid.NewGuid(),
            deliveryExceptionId: Guid.NewGuid(),
            authorizationNumber: "RTS-DIRECT-TEST",
            reason: ReturnReason.CustomerRefused,
            notes: "Direct test",
            authorizedByUserId: fix.StaffUserId);

        Assert.Equal(ReturnAuthorizationStatus.Authorized, auth.Status);

        var ex = Assert.Throws<InvalidDomainStateException>(() => auth.RecordDeliveredAtFacility());
        Assert.Contains("when no return shipment has been created", ex.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Test RecordInTransit on unexecuted Authorized status
        var exTransit = Assert.Throws<InvalidDomainStateException>(() => auth.RecordInTransit());
        Assert.Contains("when no return shipment has been created", exTransit.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestReturnTracking_DestinationFacilityMismatch_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var request = new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "DELIVERED",
            CarrierDescription = "Delivered to Cincinnati facility by mistake",
            OccurredAtUtc = DateTime.UtcNow,
            DestinationFacilityCode = "CVG1" // Mismatched destination facility (authorized is LEX1)
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.IngestReturnTrackingEventAsync(request));
        Assert.Contains("does not match authorized return destination facility", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestReturnTracking_DestinationPostalCodeMismatch_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var request = new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "DELIVERED",
            CarrierDescription = "Delivered to unknown postal code",
            OccurredAtUtc = DateTime.UtcNow,
            EventPostalCode = "90210" // Destination facility is 40509
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.IngestReturnTrackingEventAsync(request));
        Assert.Contains("does not match authorized destination facility postal code", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestReturnTracking_CancelledShipment_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var shipInDb = await context.ReturnShipments.FindAsync(retShipment.Id);
        shipInDb!.Cancel("Cancelled prior to handover", fix.StaffUserId);
        await context.SaveChangesAsync();

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var request = new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "IN_TRANSIT",
            OccurredAtUtc = DateTime.UtcNow
        };

        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.IngestReturnTrackingEventAsync(request));
        Assert.Contains("cancelled return shipment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelAuthorization_AfterCarrierHandoverCommenced_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var trackingService = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        // 1. Establish carrier handover
        await trackingService.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "PICKUP",
            OccurredAtUtc = DateTime.UtcNow.AddHours(-1)
        });

        // 2. Attempt to cancel authorization after custody handover
        var authService = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);
        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() => authService.CancelAuthorizationAsync(new CancelReturnAuthorizationRequest
        {
            AuthorizationId = auth.Id,
            Reason = "Late cancellation attempt after carrier pickup"
        }));

        Assert.Contains("custody", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExpireAuthorization_AfterCarrierHandoverCommenced_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var trackingService = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        // 1. Establish carrier handover
        await trackingService.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "IN_TRANSIT",
            OccurredAtUtc = DateTime.UtcNow.AddHours(-1)
        });

        // 2. Attempt to expire authorization after custody handover
        var authService = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);
        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() => authService.ExpireAuthorizationAsync(new ExpireReturnAuthorizationRequest
        {
            AuthorizationId = auth.Id,
            Reason = "Attempted expiry while in transit"
        }));

        Assert.Contains("custody handover", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestReturnTracking_OutboundIndependence_DoesNotMutateOutboundPackageOrOrder()
    {
        // Architectural Invariant B:
        // Reverse tracking must be independent of outbound tracking.
        // Do not update original outbound package to an in-transit or delivered state when processing reverse tracking.
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        // Ingest DELIVERED reverse event
        var result = await service.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "DELIVERED",
            CarrierDescription = "Delivered to returns facility dock.",
            OccurredAtUtc = DateTime.UtcNow,
            DestinationFacilityCode = "LEX1"
        });
        Assert.True(result.IsSuccess);

        // Verify Original Outbound Package status remains DeliveryException (NOT Delivered!)
        var originalPkg = await context.Packages.FindAsync(pkg.Id);
        Assert.NotNull(originalPkg);
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, originalPkg.CurrentTrackingStatus);

        // Verify Original Outbound Order remains Completed
        var originalOrder = await context.OutboundOrders.FindAsync(order.Id);
        Assert.NotNull(originalOrder);
        Assert.Equal(OutboundOrderStatus.Completed, originalOrder.Status);
        Assert.Equal(OrderDeliveryStatus.DeliveryException, originalOrder.DeliveryStatus);
    }

    [Fact]
    public async Task IngestReturnTracking_InventoryInvariance_StockUnchanged()
    {
        // Architectural Invariant: Delta Q_warehouse = 0
        // Carrier delivery creates receiving handoff, but does NOT post to stock or increase inventory.
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var stockBefore = await context.StockOnHand.AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.ItemId == fix.ItemId);
        Assert.NotNull(stockBefore);
        var availableBefore = stockBefore.QtyAvailable;
        var allocatedBefore = stockBefore.QtyAllocated;

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        // Ingest Delivery and create Handoff
        var result = await service.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "DELIVERED",
            CarrierDescription = "Delivered at warehouse dock",
            OccurredAtUtc = DateTime.UtcNow,
            DestinationFacilityCode = "LEX1"
        });
        Assert.True(result.IsSuccess);

        // Verify Stock is Strictly Invariant
        var stockAfter = await context.StockOnHand.AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.ItemId == fix.ItemId);
        Assert.NotNull(stockAfter);
        Assert.Equal(availableBefore, stockAfter.QtyAvailable);
        Assert.Equal(allocatedBefore, stockAfter.QtyAllocated);
    }

    [Fact]
    public async Task IngestReturnTracking_CrossTenantAccess_ThrowsUnauthorizedException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var request = new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "IN_TRANSIT",
            OccurredAtUtc = DateTime.UtcNow,
            TenantIdOverride = fix.ForeignClientId // Attempting cross-tenant injection
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => service.IngestReturnTrackingEventAsync(request));
        Assert.Contains("different client tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestReturnTracking_UnknownTrackingNumber_ThrowsEntityNotFoundException()
    {
        var fix = await SeedTestEnvironmentAsync();

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var request = new IngestReturnTrackingRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = "UNKNOWN-TRACKING-12345",
            CarrierEventCode = "IN_TRANSIT",
            OccurredAtUtc = DateTime.UtcNow
        };

        var ex = await Assert.ThrowsAsync<EntityNotFoundException>(() => service.IngestReturnTrackingEventAsync(request));
        Assert.Equal(nameof(ReturnShipment), ex.EntityName);
    }

    [Fact]
    public async Task GetInboundHandoffByReturnShipmentId_And_AuthorizationId_ReturnsDto()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        // Deliver package to create handoff
        await service.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "DELIVERED",
            CarrierDescription = "Delivered at receiving dock",
            OccurredAtUtc = DateTime.UtcNow,
            DestinationFacilityCode = "LEX1"
        });

        // Query by ShipmentId
        var byShipment = await service.GetInboundHandoffByReturnShipmentIdAsync(retShipment.Id);
        Assert.NotNull(byShipment);
        Assert.Equal(retShipment.Id, byShipment.ReturnShipmentId);
        Assert.Equal(auth.Id, byShipment.ReturnAuthorizationId);
        Assert.Equal(ReturnInboundHandoffStatus.PendingReceiving, byShipment.Status);
        Assert.Equal("Lexington Returns Center", byShipment.DestinationFacilityName);

        // Query by AuthorizationId
        var byAuth = await service.GetInboundHandoffByAuthorizationIdAsync(auth.Id);
        Assert.NotNull(byAuth);
        Assert.Equal(byShipment.Id, byAuth.Id);
    }

    [Fact]
    public async Task GetReturnTrackingHistory_ReturnsChronologicalTimeline()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var t0 = DateTime.UtcNow.AddHours(-5);
        var t1 = DateTime.UtcNow.AddHours(-3);
        var t2 = DateTime.UtcNow.AddHours(-1);

        await service.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "PICKUP",
            CarrierDescription = "Picked up by courier",
            OccurredAtUtc = t0
        });

        await service.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "IN_TRANSIT",
            CarrierDescription = "Departed sort facility",
            OccurredAtUtc = t1
        });

        await service.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "OUT_FOR_DELIVERY",
            CarrierDescription = "Out for warehouse delivery",
            OccurredAtUtc = t2
        });

        var history = await service.GetReturnTrackingHistoryAsync(retShipment.Id);
        Assert.Equal(3, history.Count);
        Assert.Equal("PICKUP", history[0].EventType);
        Assert.Equal("IN_TRANSIT", history[1].EventType);
        Assert.Equal("OUT_FOR_DELIVERY", history[2].EventType);
        Assert.True(history[0].OccurredAtUtc < history[1].OccurredAtUtc);
        Assert.True(history[1].OccurredAtUtc < history[2].OccurredAtUtc);
    }

    [Fact]
    public async Task IngestReturnTrackingEventCommand_MediatRHandler_ExecutesSuccessfully()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        var handler = new IngestReturnTrackingEventCommandHandler(service);
        var command = new IngestReturnTrackingEventCommand(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "IN_TRANSIT",
            CarrierDescription = "MediatR transit event",
            OccurredAtUtc = DateTime.UtcNow
        });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsDuplicate);
        Assert.Equal(ReturnShipmentStatus.InTransitToWarehouse, result.ShipmentStatus);
    }

    [Fact]
    public async Task ReturnInboundHandoff_Lifecycle_MarkProcessingAndMarkReceived()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth, retShipment) = await CreateReturnShipmentFixtureAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnTrackingService(context, tenantProvider, userContext, NullLogger<ReturnTrackingService>.Instance);

        // Deliver package to generate handoff
        var delivResult = await service.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = retShipment.CarrierCode,
            TrackingNumber = retShipment.TrackingNumber,
            CarrierEventCode = "DELIVERED",
            CarrierDescription = "Delivered to receiving dock",
            OccurredAtUtc = DateTime.UtcNow,
            DestinationFacilityCode = "LEX1"
        });

        var handoff = await context.ReturnInboundHandoffs.FindAsync(delivResult.InboundHandoffId);
        Assert.NotNull(handoff);
        Assert.Equal(ReturnInboundHandoffStatus.PendingReceiving, handoff.Status);

        // Transition: Processing
        handoff.MarkProcessing("DOCK-DOOR-02");
        Assert.Equal(ReturnInboundHandoffStatus.Processing, handoff.Status);
        Assert.Equal("DOCK-DOOR-02", handoff.DockDoorLocation);

        // Transition: Received
        handoff.MarkReceived("Physical receiving complete. Routed to inspection bay.");
        Assert.Equal(ReturnInboundHandoffStatus.Received, handoff.Status);
        Assert.Contains("Routed to inspection bay", handoff.Notes);

        // Attempt cancellation on received handoff -> rejected
        var ex = Assert.Throws<InvalidDomainStateException>(() => handoff.Cancel("Cannot cancel received"));
        Assert.Contains("already been physically received", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
