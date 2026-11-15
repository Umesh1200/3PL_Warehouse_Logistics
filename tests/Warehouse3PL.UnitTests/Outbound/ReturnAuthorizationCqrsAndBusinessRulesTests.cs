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

public class ReturnAuthorizationCqrsAndBusinessRulesTests
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
            CompanyName = "Apex Electronics Direct",
            AccountCode = $"APX{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Competitor Goods Inc",
            AccountCode = $"CGI{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var originFacility = new Facility
        {
            Id = fix.OriginFacilityId,
            Name = "Louisville Central Hub",
            FacilityCode = "SDF1"
        };

        var destinationFacility = new Facility
        {
            Id = fix.DestinationFacilityId,
            Name = "Lexington Returns Hub",
            FacilityCode = "LEX1"
        };

        var foreignFacility = new Facility
        {
            Id = fix.ForeignFacilityId,
            Name = "Miami Hub",
            FacilityCode = "MIA1"
        };

        var staffUser = new User
        {
            Id = fix.StaffUserId,
            Email = "returns.lead@warehouse3pl.test",
            PasswordHash = "hash",
            FirstName = "Mark",
            LastName = "Returns",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = $"SKU-RTS-CQRS-{Guid.NewGuid().ToString()[..6]}",
            Description = "Optoelectronic Sensor",
            WeightKg = 1.8m,
            LengthCm = 12m,
            WidthCm = 12m,
            HeightCm = 8m
        };

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.OriginFacilityId,
            LocationCode = "BIN-RTS-CQRS-01",
            Barcode = "BIN-RTS-CQRS-01",
            LocationType = LocationType.Bin
        };

        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            LocationId = loc.Id,
            ItemId = item.Id,
            QtyAvailable = 450m,
            QtyAllocated = 45m
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

    private static async Task<(OutboundOrder Order, Shipment Shipment, Package Package, ShipmentDeliveryException Exception)> CreateDispatchedPackageWithExceptionAsync(
        TestEnvFixture fix,
        DeliveryExceptionStatus status = DeliveryExceptionStatus.PendingReturnAuthorization,
        DeliveryExceptionType exceptionType = DeliveryExceptionType.CustomerRefused)
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
            OrderNumber = $"ORD-CQRS-{Guid.NewGuid().ToString()[..6]}",
            Status = OutboundOrderStatus.Completed,
            DeliveryStatus = OrderDeliveryStatus.DeliveryException
        };
        context.OutboundOrders.Add(order);

        var shipmentId = Guid.NewGuid();
        var shipment = new Shipment
        {
            Id = shipmentId,
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            ShipmentNumber = $"SHP-CQRS-{Guid.NewGuid().ToString()[..6]}",
            CarrierCode = "SIM-FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.DeliveryException,
            DispatchedAtUtc = DateTime.UtcNow.AddHours(-5)
        };
        context.Shipments.Add(shipment);

        var hu = new HandlingUnit
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            LpnCode = $"HU-CQRS-{Guid.NewGuid().ToString()[..6]}",
            UnitType = HandlingUnitType.ShippingCarton,
            Status = HandlingUnitStatus.InUse
        };
        context.HandlingUnits.Add(hu);

        var pkg = new Package
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            OutboundOrderId = orderId,
            ShipmentId = shipmentId,
            HandlingUnitId = hu.Id,
            PackageNumber = $"PKG-CQRS-{Guid.NewGuid().ToString()[..6]}",
            TrackingNumber = $"TRK-CQRS-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            CarrierCode = "SIM-FEDEX",
            Status = PackageStatus.Loaded,
            CurrentTrackingStatus = TrackingMilestoneStatus.DeliveryException,
            TrackingStatusUpdatedAtUtc = DateTime.UtcNow.AddMinutes(-35)
        };
        context.Packages.Add(pkg);

        var deliveryException = new ShipmentDeliveryException
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            PackageId = pkg.Id,
            ShipmentId = shipmentId,
            OrderId = orderId,
            TrackingNumber = pkg.TrackingNumber,
            CarrierCode = "SIM-FEDEX",
            ExceptionType = exceptionType,
            Status = status,
            Severity = DeliveryExceptionSeverity.High,
            Source = DeliveryExceptionSource.CarrierWebhook,
            CarrierExceptionCode = "REFUSED_01",
            CarrierDescription = "Consignee refused package",
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-45),
            ReattemptCount = 0,
            ContactAttemptsCount = 1,
            EventFingerprint = $"FP-CQRS-{Guid.NewGuid():N}"
        };
        context.ShipmentDeliveryExceptions.Add(deliveryException);

        await context.SaveChangesAsync();
        return (order, shipment, pkg, deliveryException);
    }

    // ==============================================================
    // 1. AuthorizeReturnToSenderCommand Tests
    // ==============================================================

    [Fact]
    public async Task AuthorizeReturnToSender_ValidRequest_CreatesAuthorizationAndAtomicallyUpdatesExceptionAndAppendsTracking()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);
        var handler = new AuthorizeReturnToSenderCommandHandler(service);

        var request = new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Consignee refused delivery. Approved return to Lexington hub.",
            RmaNumber = "RMA-APEX-1001",
            ExpiryDays = 21,
            AuthorizedByUserId = fix.StaffUserId
        };

        var result = await handler.Handle(new AuthorizeReturnToSenderCommand(request), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.AuthorizationId);
        Assert.StartsWith("RA-", result.AuthorizationNumber);
        Assert.Equal(ReturnAuthorizationStatus.Authorized, result.Status);

        // 1. Verify ReturnAuthorization record in DB
        var auth = await context.ReturnAuthorizations.FindAsync(result.AuthorizationId);
        Assert.NotNull(auth);
        Assert.Equal(fix.ClientId, auth.ClientId);
        Assert.Equal(fix.OriginFacilityId, auth.OriginFacilityId);
        Assert.Equal(fix.DestinationFacilityId, auth.DestinationFacilityId);
        Assert.Equal(order.Id, auth.OrderId);
        Assert.Equal(pkg.Id, auth.PackageId);
        Assert.Equal(exc.Id, auth.DeliveryExceptionId);
        Assert.Equal("RMA-APEX-1001", auth.RmaNumber);
        Assert.Equal(ReturnReason.CustomerRefused, auth.Reason);
        Assert.Equal(ReturnAuthorizationStatus.Authorized, auth.Status);
        Assert.NotNull(auth.ExpiresAtUtc);

        // 2. Verify DeliveryException updated atomically
        var updatedExc = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updatedExc);
        Assert.Equal(DeliveryExceptionStatus.ReturnToSenderInitiated, updatedExc.Status);
        Assert.Equal(DeliveryExceptionResolution.ReturnToSenderAuthorized, updatedExc.Resolution);
        Assert.Equal(auth.Id, updatedExc.ReturnAuthorizationId);
        Assert.NotNull(updatedExc.ResolvedAtUtc);
        Assert.Equal(fix.StaffUserId, updatedExc.ResolvedByUserId);

        // 3. Verify Tracking Event appended
        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "RETURN_TO_SENDER_AUTHORIZED");
        Assert.NotNull(trackingEvent);
        Assert.Contains(auth.AuthorizationNumber, trackingEvent.CarrierDescription);
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, trackingEvent.MilestoneStatus);

        // 4. Invariant: OutboundOrder status remains Completed
        var updatedOrder = await context.OutboundOrders.FindAsync(order.Id);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OutboundOrderStatus.Completed, updatedOrder.Status);
    }

    [Fact]
    public async Task AuthorizeReturnToSender_VoidedException_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix, status: DeliveryExceptionStatus.Voided);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);
        var handler = new AuthorizeReturnToSenderCommandHandler(service);

        var command = new AuthorizeReturnToSenderCommand(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Authorize voided"
        });

        await Assert.ThrowsAsync<InvalidDomainStateException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task AuthorizeReturnToSender_AlreadyResolvedException_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix, status: DeliveryExceptionStatus.Resolved);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);
        var handler = new AuthorizeReturnToSenderCommandHandler(service);

        var command = new AuthorizeReturnToSenderCommand(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Authorize resolved"
        });

        await Assert.ThrowsAsync<InvalidDomainStateException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task AuthorizeReturnToSender_DuplicateActiveAuthorization_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);
        var handler = new AuthorizeReturnToSenderCommandHandler(service);

        var request = new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "First authorization"
        };

        // First attempt succeeds
        var firstResult = await handler.Handle(new AuthorizeReturnToSenderCommand(request), CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        // Second duplicate authorization attempt must fail
        await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            handler.Handle(new AuthorizeReturnToSenderCommand(request), CancellationToken.None));
    }

    [Fact]
    public async Task AuthorizeReturnToSender_CrossTenantAttempt_ThrowsUnauthorizedException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        // User belongs to foreign client tenant
        var tenantProvider = new TestTenantProvider { TenantId = fix.ForeignClientId, BypassTenantFilter = false };
        var userContext = new TestCurrentUserContext { UserType = UserType.Client, TenantId = fix.ForeignClientId, FacilityId = fix.OriginFacilityId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);
        var handler = new AuthorizeReturnToSenderCommandHandler(service);

        var command = new AuthorizeReturnToSenderCommand(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Cross tenant attempt"
        });

        await Assert.ThrowsAsync<UnauthorizedException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task AuthorizeReturnToSender_UnauthorizedFacilityStaffUser_ThrowsForbiddenException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        // User assigned to foreign facility MIA1, not SDF1 or LEX1, and not superadmin
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.ForeignFacilityId, RolesList = [] };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);
        var handler = new AuthorizeReturnToSenderCommandHandler(service);

        var command = new AuthorizeReturnToSenderCommand(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Unauthorized facility user"
        });

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    // ==============================================================
    // 2. CancelReturnAuthorizationCommand Tests
    // ==============================================================

    [Fact]
    public async Task CancelReturnAuthorization_WhenAuthorized_CancelsAuthRevertsExceptionAndAppendsTracking()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);

        // 1. Authorize return first
        var authResult = await service.AuthorizeReturnToSenderAsync(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Initial return authorization"
        });

        // Verify exception is ReturnToSenderInitiated
        var excBefore = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.Equal(DeliveryExceptionStatus.ReturnToSenderInitiated, excBefore!.Status);

        // 2. Cancel authorization
        var cancelHandler = new CancelReturnAuthorizationCommandHandler(service);
        var cancelResult = await cancelHandler.Handle(new CancelReturnAuthorizationCommand(new CancelReturnAuthorizationRequest
        {
            AuthorizationId = authResult.AuthorizationId,
            Reason = "Customer agreed to accept delivery on re-attempt",
            CancelledByUserId = fix.StaffUserId
        }), CancellationToken.None);

        Assert.True(cancelResult.IsSuccess);
        Assert.Equal(ReturnAuthorizationStatus.Cancelled, cancelResult.Status);

        // Verify authorization in DB
        var updatedAuth = await context.ReturnAuthorizations.FindAsync(authResult.AuthorizationId);
        Assert.NotNull(updatedAuth);
        Assert.Equal(ReturnAuthorizationStatus.Cancelled, updatedAuth.Status);
        Assert.Equal("Customer agreed to accept delivery on re-attempt", updatedAuth.CancellationReason);
        Assert.NotNull(updatedAuth.CancelledAtUtc);

        // Verify exception reverted atomically to UnderReview (Addressing User Point 3)
        var updatedExc = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updatedExc);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, updatedExc.Status);
        Assert.Null(updatedExc.ReturnAuthorizationId);
        Assert.Null(updatedExc.ResolvedAtUtc);
        Assert.Contains("Return authorization cancelled", updatedExc.ResolutionNotes);

        // Verify tracking event
        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "RETURN_AUTHORIZATION_CANCELLED");
        Assert.NotNull(trackingEvent);
    }

    [Fact]
    public async Task CancelReturnAuthorization_WhenShipmentCreated_CancelsBothAuthAndLinkedReturnShipment()
    {
        // (Addressing User Point 2: Ensure cancellation does not leave an active reverse-shipment booking)
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);

        // 1. Authorize return
        var authResult = await service.AuthorizeReturnToSenderAsync(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Initial return authorization"
        });

        // 2. Simulate reverse shipment creation (Step 3B.4.3 mock)
        var returnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            OriginFacilityId = fix.OriginFacilityId,
            DestinationFacilityId = fix.DestinationFacilityId,
            OrderId = order.Id,
            PackageId = pkg.Id,
            DeliveryExceptionId = exc.Id,
            ReturnAuthorizationId = authResult.AuthorizationId,
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = "RET-TRK-9901",
            ReturnRmaNumber = "RMA-9901",
            Status = ReturnShipmentStatus.Authorized,
            CarrierHandoverAtUtc = null, // Pre-handover!
            OriginAddressLine1 = "123 Main St",
            OriginCity = "Louisville",
            OriginState = "KY",
            OriginPostalCode = "40202",
            DestinationAddressLine1 = "456 Warehouse Way",
            DestinationCity = "Lexington",
            DestinationState = "KY",
            DestinationPostalCode = "40509",
            AuthorizedByUserId = fix.StaffUserId,
            AuthorizationNotes = "Reverse shipment booked"
        };
        context.ReturnShipments.Add(returnShipment);

        var auth = await context.ReturnAuthorizations.FindAsync(authResult.AuthorizationId);
        auth!.LinkReturnShipment(returnShipment.Id);
        await context.SaveChangesAsync();

        Assert.Equal(ReturnAuthorizationStatus.ShipmentCreated, auth.Status);

        // 3. Cancel authorization
        var cancelHandler = new CancelReturnAuthorizationCommandHandler(service);
        var cancelResult = await cancelHandler.Handle(new CancelReturnAuthorizationCommand(new CancelReturnAuthorizationRequest
        {
            AuthorizationId = authResult.AuthorizationId,
            Reason = "Merchant cancelled reverse transit prior to carrier pickup",
            CancelledByUserId = fix.StaffUserId
        }), CancellationToken.None);

        Assert.True(cancelResult.IsSuccess);
        Assert.Equal(ReturnAuthorizationStatus.Cancelled, cancelResult.Status);

        // 4. Invariant Assertion: Linked ReturnShipment MUST be cancelled! No dangling label!
        var updatedShipment = await context.ReturnShipments.FindAsync(returnShipment.Id);
        Assert.NotNull(updatedShipment);
        Assert.Equal(ReturnShipmentStatus.Cancelled, updatedShipment.Status);
        Assert.Contains("Linked return authorization cancelled", updatedShipment.DispositionNotes);

        // Exception also reverted
        var updatedExc = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updatedExc);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, updatedExc.Status);
    }

    [Fact]
    public async Task CancelReturnAuthorization_WhenInTransitOrHandoverOccurred_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);

        var authResult = await service.AuthorizeReturnToSenderAsync(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Initial return authorization"
        });

        // Set auth to InTransit
        var auth = await context.ReturnAuthorizations.FindAsync(authResult.AuthorizationId);
        auth!.LinkReturnShipment(Guid.NewGuid());
        auth.RecordInTransit();
        await context.SaveChangesAsync();

        var cancelHandler = new CancelReturnAuthorizationCommandHandler(service);

        // Cancellation must be rejected once carrier has accepted custody
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => cancelHandler.Handle(new CancelReturnAuthorizationCommand(new CancelReturnAuthorizationRequest
        {
            AuthorizationId = authResult.AuthorizationId,
            Reason = "Try cancelling in transit",
            CancelledByUserId = fix.StaffUserId
        }), CancellationToken.None));
    }

    // ==============================================================
    // 3. ExpireReturnAuthorization Tests
    // ==============================================================

    [Fact]
    public async Task ExpireReturnAuthorization_WhenShipmentCreated_ExpiresAuthAndCancelsLinkedReturnShipment()
    {
        // (Addressing User Point 1: Verify expiration from ShipmentCreated and invalidation of linked shipment)
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);

        // 1. Authorize return
        var authResult = await service.AuthorizeReturnToSenderAsync(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Initial return authorization"
        });

        // 2. Link reverse shipment in ShipmentCreated state
        var returnShipment = new ReturnShipment
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            OriginFacilityId = fix.OriginFacilityId,
            DestinationFacilityId = fix.DestinationFacilityId,
            OrderId = order.Id,
            PackageId = pkg.Id,
            DeliveryExceptionId = exc.Id,
            ReturnAuthorizationId = authResult.AuthorizationId,
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = "RET-TRK-EXP-01",
            ReturnRmaNumber = "RMA-EXP-01",
            Status = ReturnShipmentStatus.Authorized,
            CarrierHandoverAtUtc = null,
            OriginAddressLine1 = "123 Main St",
            OriginCity = "Louisville",
            OriginState = "KY",
            OriginPostalCode = "40202",
            DestinationAddressLine1 = "456 Warehouse Way",
            DestinationCity = "Lexington",
            DestinationState = "KY",
            DestinationPostalCode = "40509",
            AuthorizedByUserId = fix.StaffUserId,
            AuthorizationNotes = "Reverse shipment booked"
        };
        context.ReturnShipments.Add(returnShipment);

        var auth = await context.ReturnAuthorizations.FindAsync(authResult.AuthorizationId);
        auth!.LinkReturnShipment(returnShipment.Id);
        await context.SaveChangesAsync();

        Assert.Equal(ReturnAuthorizationStatus.ShipmentCreated, auth.Status);

        // 3. Expire authorization (e.g., 30 days elapsed without carrier pickup)
        var expireHandler = new ExpireReturnAuthorizationCommandHandler(service);
        var expireResult = await expireHandler.Handle(new ExpireReturnAuthorizationCommand(new ExpireReturnAuthorizationRequest
        {
            AuthorizationId = authResult.AuthorizationId,
            Reason = "Carrier never picked up parcel within 30-day window"
        }), CancellationToken.None);

        Assert.True(expireResult.IsSuccess);
        Assert.Equal(ReturnAuthorizationStatus.Expired, expireResult.Status);

        // 4. Invariant Assertion: Linked ReturnShipment is cancelled and cannot be used
        var updatedShipment = await context.ReturnShipments.FindAsync(returnShipment.Id);
        Assert.NotNull(updatedShipment);
        Assert.Equal(ReturnShipmentStatus.Cancelled, updatedShipment.Status);
        Assert.Contains("expired prior to carrier handover", updatedShipment.DispositionNotes);

        // 5. Invariant Assertion: Exception reverted to UnderReview
        var updatedExc = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updatedExc);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, updatedExc.Status);
        Assert.Null(updatedExc.ReturnAuthorizationId);
    }

    [Fact]
    public async Task ExpireReturnAuthorization_WhenInTransit_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);

        var authResult = await service.AuthorizeReturnToSenderAsync(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Initial return authorization"
        });

        var auth = await context.ReturnAuthorizations.FindAsync(authResult.AuthorizationId);
        auth!.LinkReturnShipment(Guid.NewGuid());
        auth.RecordInTransit();
        await context.SaveChangesAsync();

        var expireHandler = new ExpireReturnAuthorizationCommandHandler(service);

        await Assert.ThrowsAsync<InvalidDomainStateException>(() => expireHandler.Handle(new ExpireReturnAuthorizationCommand(new ExpireReturnAuthorizationRequest
        {
            AuthorizationId = authResult.AuthorizationId
        }), CancellationToken.None));
    }

    // ==============================================================
    // 4. Inventory & Outbound Order Fulfillment Invariance
    // ==============================================================

    [Fact]
    public async Task InventoryInvariance_ReturnAuthorizationAndCancellation_LeavesWarehouseStockUnchanged()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var initialStock = await context.StockOnHand.AsNoTracking().FirstOrDefaultAsync(s => s.ItemId == fix.ItemId);
        Assert.NotNull(initialStock);
        var initialQtyAvailable = initialStock.QtyAvailable;
        var initialQtyAllocated = initialStock.QtyAllocated;

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var service = new ReturnAuthorizationService(context, tenantProvider, userContext, NullLogger<ReturnAuthorizationService>.Instance);

        // 1. Authorize return
        var authResult = await service.AuthorizeReturnToSenderAsync(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exc.Id,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "RTS authorization"
        });

        // 2. Cancel authorization
        await service.CancelAuthorizationAsync(new CancelReturnAuthorizationRequest
        {
            AuthorizationId = authResult.AuthorizationId,
            Reason = "Customer agreed to receive shipment on Monday"
        });

        // Assert Stock on Hand remains completely identical: Delta Q_warehouse == 0
        var finalStock = await context.StockOnHand.AsNoTracking().FirstOrDefaultAsync(s => s.ItemId == fix.ItemId);
        Assert.NotNull(finalStock);
        Assert.Equal(initialQtyAvailable, finalStock.QtyAvailable);
        Assert.Equal(initialQtyAllocated, finalStock.QtyAllocated);

        // Assert Outbound Order Status remains Completed
        var finalOrder = await context.OutboundOrders.FindAsync(order.Id);
        Assert.NotNull(finalOrder);
        Assert.Equal(OutboundOrderStatus.Completed, finalOrder.Status);
    }
}

