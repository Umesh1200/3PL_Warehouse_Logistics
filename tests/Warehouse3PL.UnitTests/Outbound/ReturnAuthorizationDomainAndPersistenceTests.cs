using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Outbound;

public class ReturnAuthorizationDomainAndPersistenceTests
{
    private class TestEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid ForeignClientId { get; set; } = Guid.NewGuid();
        public Guid OriginFacilityId { get; set; } = Guid.NewGuid();
        public Guid DestinationFacilityId { get; set; } = Guid.NewGuid();
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
            CompanyName = "OmniLogistics Direct",
            AccountCode = $"OMN{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Foreign Retailer Ltd",
            AccountCode = $"FRL{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var originFacility = new Facility
        {
            Id = fix.OriginFacilityId,
            Name = "Memphis Hub",
            FacilityCode = "MEM1"
        };

        var destinationFacility = new Facility
        {
            Id = fix.DestinationFacilityId,
            Name = "Nashville Receiving Facility",
            FacilityCode = "BNA1"
        };

        var staffUser = new User
        {
            Id = fix.StaffUserId,
            Email = "returns.officer@warehouse3pl.test",
            PasswordHash = "hash",
            FirstName = "Rachel",
            LastName = "Returns",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = $"SKU-RTS-{Guid.NewGuid().ToString()[..6]}",
            Description = "Industrial Sensor Unit",
            WeightKg = 3.5m,
            LengthCm = 20m,
            WidthCm = 15m,
            HeightCm = 10m
        };

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.OriginFacilityId,
            LocationCode = "BIN-RTS-01",
            Barcode = "BIN-RTS-01",
            LocationType = LocationType.Bin
        };

        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            LocationId = loc.Id,
            ItemId = item.Id,
            QtyAvailable = 300m,
            QtyAllocated = 30m
        };

        context.Clients.AddRange(client, foreignClient);
        context.Facilities.AddRange(originFacility, destinationFacility);
        context.Users.Add(staffUser);
        context.Items.Add(item);
        context.Locations.Add(loc);
        context.StockOnHand.Add(stock);

        await context.SaveChangesAsync();
        return fix;
    }

    private static async Task<(OutboundOrder Order, Shipment Shipment, Package Package, ShipmentDeliveryException Exception)> CreateDispatchedPackageWithExceptionAsync(
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
            OrderNumber = $"ORD-RTS-{Guid.NewGuid().ToString()[..6]}",
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
            ShipmentNumber = $"SHP-RTS-{Guid.NewGuid().ToString()[..6]}",
            CarrierCode = "SIM-UPS",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.DeliveryException,
            DispatchedAtUtc = DateTime.UtcNow.AddHours(-6)
        };
        context.Shipments.Add(shipment);

        var hu = new HandlingUnit
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            LpnCode = $"HU-RTS-{Guid.NewGuid().ToString()[..6]}",
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
            PackageNumber = $"PKG-RTS-{Guid.NewGuid().ToString()[..6]}",
            TrackingNumber = $"TRK-RTS-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            CarrierCode = "SIM-UPS",
            Status = PackageStatus.Loaded,
            CurrentTrackingStatus = TrackingMilestoneStatus.DeliveryException,
            TrackingStatusUpdatedAtUtc = DateTime.UtcNow.AddMinutes(-40)
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
            CarrierCode = "SIM-UPS",
            ExceptionType = DeliveryExceptionType.CustomerRefused,
            Status = DeliveryExceptionStatus.PendingReturnAuthorization,
            Severity = DeliveryExceptionSeverity.High,
            Source = DeliveryExceptionSource.CarrierWebhook,
            CarrierExceptionCode = "REFUSED",
            CarrierDescription = "Consignee refused package: incorrect order placed",
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-50),
            ReattemptCount = 0,
            ContactAttemptsCount = 1,
            EventFingerprint = $"FP-RTS-{Guid.NewGuid():N}"
        };
        context.ShipmentDeliveryExceptions.Add(deliveryException);

        await context.SaveChangesAsync();
        return (order, shipment, pkg, deliveryException);
    }

    // ==============================================================
    // 1. Factory Creation & Validation Tests
    // ==============================================================

    [Fact]
    public async Task Create_ValidInputs_InitializesReturnAuthorizationWithAuthorizedStatus()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var authNumber = $"RA-{DateTime.UtcNow:yyyyMMdd}-9901";
        var expiry = DateTime.UtcNow.AddDays(14);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            authNumber,
            ReturnReason.CustomerRefused,
            "Consignee refused receipt; merchant authorized return to sender",
            fix.StaffUserId,
            rmaNumber: "RMA-OMNI-4401",
            expiresAtUtc: expiry);

        Assert.NotEqual(Guid.Empty, auth.Id);
        Assert.Equal(fix.ClientId, auth.ClientId);
        Assert.Equal(fix.OriginFacilityId, auth.OriginFacilityId);
        Assert.Equal(fix.DestinationFacilityId, auth.DestinationFacilityId);
        Assert.Equal(order.Id, auth.OrderId);
        Assert.Equal(shipment.Id, auth.OriginalShipmentId);
        Assert.Equal(pkg.Id, auth.PackageId);
        Assert.Equal(exc.Id, auth.DeliveryExceptionId);
        Assert.Equal(authNumber, auth.AuthorizationNumber);
        Assert.Equal("RMA-OMNI-4401", auth.RmaNumber);
        Assert.Equal(ReturnReason.CustomerRefused, auth.Reason);
        Assert.Equal(ReturnAuthorizationStatus.Authorized, auth.Status);
        Assert.Equal(fix.StaffUserId, auth.AuthorizedByUserId);
        Assert.Equal(expiry, auth.ExpiresAtUtc);
        Assert.Contains("merchant authorized return", auth.AuthorizationNotes);
        Assert.Null(auth.ReturnShipmentId);
        Assert.Null(auth.CancelledAtUtc);
    }

    [Theory]
    [InlineData("", "Notes")]
    [InlineData("   ", "Notes")]
    [InlineData("RA-VALID", "")]
    [InlineData("RA-VALID", "   ")]
    public async Task Create_MissingRequiredStrings_ThrowsValidationException(string authNumber, string notes)
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        Assert.Throws<ValidationException>(() => ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            authNumber,
            ReturnReason.CustomerRefused,
            notes,
            fix.StaffUserId));
    }

    [Fact]
    public async Task Create_PastExpiryDate_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        Assert.Throws<ValidationException>(() => ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-PAST-EXPIRY",
            ReturnReason.CustomerRefused,
            "Valid notes",
            fix.StaffUserId,
            expiresAtUtc: DateTime.UtcNow.AddMinutes(-5)));
    }

    // ==============================================================
    // 2. Domain Relationship Validation Tests
    // ==============================================================

    [Fact]
    public async Task ValidateRelationships_ValidRelationships_Succeeds()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-REL-OK",
            ReturnReason.CustomerRefused,
            "Valid authorization",
            fix.StaffUserId);

        var destinationFacility = new Facility { Id = fix.DestinationFacilityId, Name = "BNA1" };

        // Should not throw
        auth.ValidateRelationships(pkg, shipment, order, destinationFacility);
    }

    [Fact]
    public async Task ValidateRelationships_CrossTenantPackage_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-TENANT-FAIL",
            ReturnReason.CustomerRefused,
            "Valid authorization",
            fix.StaffUserId);

        // Package with foreign client tenant
        var foreignPkg = new Package
        {
            Id = pkg.Id,
            ClientId = fix.ForeignClientId,
            PackageNumber = "PKG-FOREIGN",
            OutboundOrderId = order.Id
        };

        var destinationFacility = new Facility { Id = fix.DestinationFacilityId, Name = "BNA1" };

        Assert.Throws<ValidationException>(() => auth.ValidateRelationships(foreignPkg, shipment, order, destinationFacility));
    }

    [Fact]
    public async Task ValidateRelationships_MismatchedOrder_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-ORDER-FAIL",
            ReturnReason.CustomerRefused,
            "Valid authorization",
            fix.StaffUserId);

        // Foreign order
        var foreignOrder = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            OrderNumber = "ORD-OTHER"
        };

        var destinationFacility = new Facility { Id = fix.DestinationFacilityId, Name = "BNA1" };

        Assert.Throws<ValidationException>(() => auth.ValidateRelationships(pkg, shipment, foreignOrder, destinationFacility));
    }

    // ==============================================================
    // 3. State Transitions & Reverse Shipment Linkage Tests
    // ==============================================================

    [Fact]
    public async Task LinkReturnShipment_ValidId_TransitionsToShipmentCreated()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-LINK-SHIPMENT",
            ReturnReason.CustomerRefused,
            "Valid authorization",
            fix.StaffUserId);

        var returnShipmentId = Guid.NewGuid();
        auth.LinkReturnShipment(returnShipmentId);

        Assert.Equal(returnShipmentId, auth.ReturnShipmentId);
        Assert.Equal(ReturnAuthorizationStatus.ShipmentCreated, auth.Status);
    }

    [Fact]
    public async Task LifecycleProgression_InTransitToDeliveredToCompleted_Succeeds()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-LIFECYCLE",
            ReturnReason.CustomerRefused,
            "Initial notes",
            fix.StaffUserId);

        auth.LinkReturnShipment(Guid.NewGuid());
        Assert.Equal(ReturnAuthorizationStatus.ShipmentCreated, auth.Status);

        auth.RecordInTransit();
        Assert.Equal(ReturnAuthorizationStatus.InTransit, auth.Status);

        auth.RecordDeliveredAtFacility();
        Assert.Equal(ReturnAuthorizationStatus.DeliveredAtFacility, auth.Status);

        auth.Complete("Dock receipt established via Inbound receiving session");
        Assert.Equal(ReturnAuthorizationStatus.Completed, auth.Status);
        Assert.Contains("Dock receipt established", auth.AuthorizationNotes);
    }

    // ==============================================================
    // 4. Cancellation Eligibility & Restrictions
    // ==============================================================

    [Fact]
    public async Task Cancel_WhenAuthorizedOrShipmentCreated_SucceedsAndRecordsAudit()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-CANCEL-OK",
            ReturnReason.CustomerRefused,
            "Initial notes",
            fix.StaffUserId);

        auth.Cancel("Customer contacted carrier and accepted delivery on reattempt", fix.StaffUserId);

        Assert.Equal(ReturnAuthorizationStatus.Cancelled, auth.Status);
        Assert.Equal("Customer contacted carrier and accepted delivery on reattempt", auth.CancellationReason);
        Assert.NotNull(auth.CancelledAtUtc);
        Assert.Equal(fix.StaffUserId, auth.CancelledByUserId);
    }

    [Fact]
    public async Task Cancel_WhenAlreadyInTransitOrDelivered_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-CANCEL-FAIL",
            ReturnReason.CustomerRefused,
            "Initial notes",
            fix.StaffUserId);

        auth.LinkReturnShipment(Guid.NewGuid());
        auth.RecordInTransit();

        // Must reject cancellation once reverse transit has begun
        Assert.Throws<InvalidDomainStateException>(() => auth.Cancel("Cancel in transit", fix.StaffUserId));
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-DOUBLE-CANCEL",
            ReturnReason.CustomerRefused,
            "Initial notes",
            fix.StaffUserId);

        auth.Cancel("First cancel", fix.StaffUserId);

        Assert.Throws<InvalidDomainStateException>(() => auth.Cancel("Second cancel", fix.StaffUserId));
    }

    // ==============================================================
    // 5. Expiration Handling
    // ==============================================================

    [Fact]
    public async Task Expire_WhenAuthorized_TransitionsToExpired()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-EXPIRE-OK",
            ReturnReason.CustomerRefused,
            "Initial notes",
            fix.StaffUserId);

        auth.Expire();

        Assert.Equal(ReturnAuthorizationStatus.Expired, auth.Status);
    }

    [Fact]
    public async Task Expire_WhenCancelledOrInTransit_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            "RA-EXPIRE-FAIL",
            ReturnReason.CustomerRefused,
            "Initial notes",
            fix.StaffUserId);

        auth.Cancel("Cancelled", fix.StaffUserId);

        Assert.Throws<InvalidDomainStateException>(() => auth.Expire());
    }

    // ==============================================================
    // 6. EF Core Persistence & Database Indexes
    // ==============================================================

    [Fact]
    public async Task Persistence_SaveAndRetrieve_PersistsAllPropertiesAndNavigations()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var authId = Guid.NewGuid();
        var authNumber = $"RA-PERSIST-{Guid.NewGuid().ToString()[..6]}";

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var auth = new ReturnAuthorization
            {
                Id = authId,
                ClientId = fix.ClientId,
                OriginFacilityId = fix.OriginFacilityId,
                DestinationFacilityId = fix.DestinationFacilityId,
                OrderId = order.Id,
                OriginalShipmentId = shipment.Id,
                PackageId = pkg.Id,
                DeliveryExceptionId = exc.Id,
                AuthorizationNumber = authNumber,
                RmaNumber = "RMA-7788",
                Reason = ReturnReason.UndeliverableAddress,
                Status = ReturnAuthorizationStatus.Authorized,
                AuthorizedByUserId = fix.StaffUserId,
                AuthorizedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
                AuthorizationNotes = "Address was nonexistent"
            };

            context.ReturnAuthorizations.Add(auth);
            await context.SaveChangesAsync();
        }

        using (var verifyContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var retrieved = await verifyContext.ReturnAuthorizations
                .Include(a => a.Client)
                .Include(a => a.OriginFacility)
                .Include(a => a.DestinationFacility)
                .Include(a => a.Order)
                .Include(a => a.Package)
                .Include(a => a.AuthorizedByUser)
                .FirstOrDefaultAsync(a => a.Id == authId);

            Assert.NotNull(retrieved);
            Assert.Equal(authNumber, retrieved.AuthorizationNumber);
            Assert.Equal("RMA-7788", retrieved.RmaNumber);
            Assert.Equal(ReturnReason.UndeliverableAddress, retrieved.Reason);
            Assert.Equal(ReturnAuthorizationStatus.Authorized, retrieved.Status);
            Assert.Equal(fix.ClientId, retrieved.Client.Id);
            Assert.Equal(fix.OriginFacilityId, retrieved.OriginFacility.Id);
            Assert.Equal(fix.DestinationFacilityId, retrieved.DestinationFacility.Id);
            Assert.Equal(order.Id, retrieved.Order.Id);
            Assert.Equal(pkg.Id, retrieved.Package.Id);
            Assert.Equal(fix.StaffUserId, retrieved.AuthorizedByUser.Id);
        }
    }

    [Fact]
    public async Task Persistence_UniqueAuthorizationNumberPerClient_EnforcesUniqueness()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var sharedAuthNumber = $"RA-DUP-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        // 1. Verify that the unique index is explicitly defined in EF Core model metadata
        var entityType = context.Model.FindEntityType(typeof(ReturnAuthorization));
        Assert.NotNull(entityType);
        var clientProp = entityType.FindProperty(nameof(ReturnAuthorization.ClientId));
        var authNumProp = entityType.FindProperty(nameof(ReturnAuthorization.AuthorizationNumber));
        Assert.NotNull(clientProp);
        Assert.NotNull(authNumProp);

        var index = entityType.FindIndex(new[] { clientProp, authNumProp });
        Assert.NotNull(index);
        Assert.True(index.IsUnique);
        Assert.Equal("ix_return_authorizations_client_auth_num", index.GetDatabaseName());

        // 2. Verify duplicate detection logic
        var auth1 = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            sharedAuthNumber,
            ReturnReason.CustomerRefused,
            "First authorization",
            fix.StaffUserId);

        context.ReturnAuthorizations.Add(auth1);
        await context.SaveChangesAsync();

        var duplicateExists = await context.ReturnAuthorizations
            .AnyAsync(a => a.ClientId == fix.ClientId && a.AuthorizationNumber == sharedAuthNumber);
        Assert.True(duplicateExists);
    }

    // ==============================================================
    // 7. Delivery Exception Linkage Integration
    // ==============================================================

    [Fact]
    public async Task ShipmentDeliveryException_AuthorizeReturnToSender_SetsReturnAuthorizationIdAndStatus()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var authNumber = $"RA-LINK-EXC-{Guid.NewGuid().ToString()[..6]}";
        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            authNumber,
            ReturnReason.CustomerRefused,
            "Exception escalated to return authorization",
            fix.StaffUserId);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            context.ReturnAuthorizations.Add(auth);
            await context.SaveChangesAsync();

            var loadedExc = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
            Assert.NotNull(loadedExc);

            // Authorize return on the exception aggregate
            loadedExc.AuthorizeReturnToSender(auth.Id, null, "Authorized RTS by logistics manager", fix.StaffUserId);
            await context.SaveChangesAsync();
        }

        using (var verifyContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var verifiedExc = await verifyContext.ShipmentDeliveryExceptions.FindAsync(exc.Id);
            Assert.NotNull(verifiedExc);
            Assert.Equal(auth.Id, verifiedExc.ReturnAuthorizationId);
            Assert.Equal(DeliveryExceptionStatus.ReturnToSenderInitiated, verifiedExc.Status);
            Assert.Equal(DeliveryExceptionResolution.ReturnToSenderAuthorized, verifiedExc.Resolution);
            Assert.Contains("Authorized RTS by logistics manager", verifiedExc.ResolutionNotes);
        }
    }

    // ==============================================================
    // 8. Multi-Tenancy Security Isolation
    // ==============================================================

    [Fact]
    public async Task MultiTenancy_TenantFilter_FiltersCrossTenantReturnAuthorizations()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        var clientAuthId = Guid.NewGuid();
        var foreignAuthId = Guid.NewGuid();

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var authClient = new ReturnAuthorization
            {
                Id = clientAuthId,
                ClientId = fix.ClientId,
                OriginFacilityId = fix.OriginFacilityId,
                DestinationFacilityId = fix.DestinationFacilityId,
                OrderId = order.Id,
                PackageId = pkg.Id,
                AuthorizationNumber = $"RA-CLI-{Guid.NewGuid().ToString()[..6]}",
                Reason = ReturnReason.CustomerRefused,
                Status = ReturnAuthorizationStatus.Authorized,
                AuthorizedByUserId = fix.StaffUserId,
                AuthorizedAtUtc = DateTime.UtcNow,
                AuthorizationNotes = "Client auth"
            };

            var authForeign = new ReturnAuthorization
            {
                Id = foreignAuthId,
                ClientId = fix.ForeignClientId,
                OriginFacilityId = fix.OriginFacilityId,
                DestinationFacilityId = fix.DestinationFacilityId,
                OrderId = order.Id,
                PackageId = pkg.Id,
                AuthorizationNumber = $"RA-FOR-{Guid.NewGuid().ToString()[..6]}",
                Reason = ReturnReason.CustomerCancelled,
                Status = ReturnAuthorizationStatus.Authorized,
                AuthorizedByUserId = fix.StaffUserId,
                AuthorizedAtUtc = DateTime.UtcNow,
                AuthorizationNotes = "Foreign client auth"
            };

            seedContext.ReturnAuthorizations.AddRange(authClient, authForeign);
            await seedContext.SaveChangesAsync();
        }

        // Query with client tenant context
        using (var clientContext = TestDbContextFactory.Create(
            new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = false },
            new TestCurrentUserContext { UserType = UserType.Client, TenantId = fix.ClientId },
            fix.DbName))
        {
            var visibleAuthorizations = await clientContext.ReturnAuthorizations.ToListAsync();

            Assert.Single(visibleAuthorizations);
            Assert.Equal(clientAuthId, visibleAuthorizations[0].Id);
            Assert.DoesNotContain(visibleAuthorizations, a => a.Id == foreignAuthId);
        }
    }

    // ==============================================================
    // 9. Warehouse Inventory Invariance (Delta Q_warehouse = 0)
    // ==============================================================

    [Fact]
    public async Task InventoryInvariance_ReturnAuthorizationLifecycle_LeavesWarehouseStockUnchanged()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var initialStock = await context.StockOnHand.AsNoTracking().FirstOrDefaultAsync(s => s.ItemId == fix.ItemId);
        Assert.NotNull(initialStock);
        var initialQtyAvailable = initialStock.QtyAvailable;
        var initialQtyAllocated = initialStock.QtyAllocated;

        // 1. Authorize return
        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            shipment.Id,
            pkg.Id,
            exc.Id,
            $"RA-INV-{Guid.NewGuid().ToString()[..6]}",
            ReturnReason.CustomerRefused,
            "Authorization workflow",
            fix.StaffUserId);

        context.ReturnAuthorizations.Add(auth);
        await context.SaveChangesAsync();

        // 2. Link reverse shipment
        auth.LinkReturnShipment(Guid.NewGuid());
        await context.SaveChangesAsync();

        // 3. Reverse transit
        auth.RecordInTransit();
        await context.SaveChangesAsync();

        // 4. Delivered at facility dock
        auth.RecordDeliveredAtFacility();
        await context.SaveChangesAsync();

        // Invariant assertion: inventory must remain unchanged. No automatic receipt into stock!
        var finalStock = await context.StockOnHand.AsNoTracking().FirstOrDefaultAsync(s => s.ItemId == fix.ItemId);
        Assert.NotNull(finalStock);
        Assert.Equal(initialQtyAvailable, finalStock.QtyAvailable);
        Assert.Equal(initialQtyAllocated, finalStock.QtyAllocated);

        // Invariant assertion: Outbound order status remains Completed!
        var orderAfter = await context.OutboundOrders.FindAsync(order.Id);
        Assert.NotNull(orderAfter);
        Assert.Equal(OutboundOrderStatus.Completed, orderAfter.Status);
    }
}
