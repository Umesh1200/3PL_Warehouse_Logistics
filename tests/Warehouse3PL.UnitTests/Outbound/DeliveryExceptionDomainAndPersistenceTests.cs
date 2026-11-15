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

public class DeliveryExceptionDomainAndPersistenceTests
{
    private class TestEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid DestinationFacilityId { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; } = Guid.NewGuid();
        public Guid StaffUserId { get; set; } = Guid.NewGuid();
    }

    private static async Task<TestEnvFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestEnvFixture();
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var client = new Client
        {
            Id = fix.ClientId,
            CompanyName = "Acme Retail Logistics",
            AccountCode = $"ACM{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var originFacility = new Facility
        {
            Id = fix.FacilityId,
            Name = "Chicago Logistics Hub",
            FacilityCode = $"ORD-{Guid.NewGuid().ToString()[..4]}"
        };

        var destFacility = new Facility
        {
            Id = fix.DestinationFacilityId,
            Name = "Dallas Central Return Center",
            FacilityCode = $"DFW-{Guid.NewGuid().ToString()[..4]}"
        };

        var staffUser = new User
        {
            Id = fix.StaffUserId,
            Email = "ops.agent@warehouse3pl.test",
            PasswordHash = "hash",
            FirstName = "Alex",
            LastName = "Agent",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = $"SKU-EXC-{Guid.NewGuid().ToString()[..6]}",
            Description = "Precision Sensor",
            WeightKg = 1.0m,
            LengthCm = 10m,
            WidthCm = 10m,
            HeightCm = 10m
        };

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = $"BIN-EXC-{Guid.NewGuid().ToString()[..4]}",
            Barcode = $"BIN-EXC-{Guid.NewGuid().ToString()[..4]}",
            LocationType = LocationType.Bin
        };

        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LocationId = loc.Id,
            ItemId = item.Id,
            QtyAvailable = 100m,
            QtyAllocated = 0m
        };

        context.Clients.Add(client);
        context.Facilities.AddRange(originFacility, destFacility);
        context.Users.Add(staffUser);
        context.Items.Add(item);
        context.Locations.Add(loc);
        context.StockOnHand.Add(stock);

        await context.SaveChangesAsync();
        return fix;
    }

    private static async Task<(OutboundOrder Order, Shipment Shipment, Package Package)> CreateDispatchedPackageAsync(TestEnvFixture fix)
    {
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var order = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OrderNumber = $"ORD-EXC-{Guid.NewGuid().ToString()[..6]}",
            Status = OutboundOrderStatus.Completed,
            DeliveryStatus = OrderDeliveryStatus.Dispatched
        };
        context.OutboundOrders.Add(order);

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentNumber = $"SHP-EXC-{Guid.NewGuid().ToString()[..6]}",
            CarrierCode = "SIM-FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.PendingHandover,
            DispatchedAtUtc = DateTime.UtcNow.AddHours(-4)
        };
        context.Shipments.Add(shipment);

        var hu = new HandlingUnit
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LpnCode = $"HU-EXC-{Guid.NewGuid().ToString()[..6]}",
            UnitType = HandlingUnitType.ShippingCarton,
            Status = HandlingUnitStatus.InUse
        };
        context.HandlingUnits.Add(hu);

        var pkg = new Package
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            OutboundOrderId = order.Id,
            ShipmentId = shipment.Id,
            HandlingUnitId = hu.Id,
            PackageNumber = $"PKG-EXC-{Guid.NewGuid().ToString()[..6]}",
            TrackingNumber = $"TRK-EXC-{Guid.NewGuid().ToString()[..8]}",
            CarrierCode = "SIM-FEDEX",
            Status = PackageStatus.Loaded,
            CurrentTrackingStatus = TrackingMilestoneStatus.InTransit,
            TrackingStatusUpdatedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        context.Packages.Add(pkg);

        await context.SaveChangesAsync();
        return (order, shipment, pkg);
    }

    // ==========================================
    // 1. ShipmentDeliveryException Domain Tests
    // ==========================================

    [Fact]
    public void DeliveryException_InitialState_IsOpenWithDerivedSeverity()
    {
        var ex = new ShipmentDeliveryException
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            TrackingNumber = "TRK-123456",
            CarrierCode = "FEDEX",
            ExceptionType = DeliveryExceptionType.RecipientUnavailable,
            CarrierExceptionCode = "D02",
            CarrierDescription = "Customer unavailable - business closed",
            OccurredAtUtc = DateTime.UtcNow
        };

        Assert.Equal(DeliveryExceptionStatus.Open, ex.Status);
        Assert.False(ex.IsResolved);
        Assert.Equal(1, ex.AttemptNumber);
        Assert.Equal(0, ex.ContactAttemptsCount);
        Assert.Null(ex.ResolvedAtUtc);
        Assert.Null(ex.ReturnShipmentId);
    }

    [Theory]
    [InlineData(DeliveryExceptionType.PackageDamagedInTransit, DeliveryExceptionSeverity.Critical)]
    [InlineData(DeliveryExceptionType.LostInTransit, DeliveryExceptionSeverity.Critical)]
    [InlineData(DeliveryExceptionType.CustomerRefused, DeliveryExceptionSeverity.High)]
    [InlineData(DeliveryExceptionType.AccessRestricted, DeliveryExceptionSeverity.High)]
    [InlineData(DeliveryExceptionType.CarrierOperationalDelay, DeliveryExceptionSeverity.Low)]
    [InlineData(DeliveryExceptionType.WeatherOrNaturalDisaster, DeliveryExceptionSeverity.Low)]
    [InlineData(DeliveryExceptionType.RecipientUnavailable, DeliveryExceptionSeverity.Medium)]
    [InlineData(DeliveryExceptionType.AddressNotFound, DeliveryExceptionSeverity.Medium)]
    public void DetermineDefaultSeverity_ClassifiesCorrectly(DeliveryExceptionType type, DeliveryExceptionSeverity expectedSeverity)
    {
        var severity = ShipmentDeliveryException.DetermineDefaultSeverity(type);
        Assert.Equal(expectedSeverity, severity);
    }

    [Fact]
    public void ScheduleReattempt_ValidInputs_TransitionsToScheduledForReattempt()
    {
        var ex = new ShipmentDeliveryException
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            ExceptionType = DeliveryExceptionType.RecipientUnavailable
        };

        var reattemptDate = DateTime.UtcNow.AddDays(1);
        var userId = Guid.NewGuid();

        ex.ScheduleReattempt(reattemptDate, "Customer requested delivery after 2 PM tomorrow", userId);

        Assert.Equal(DeliveryExceptionStatus.ScheduledForReattempt, ex.Status);
        Assert.Equal(DeliveryExceptionResolution.RedeliveryScheduled, ex.Resolution);
        Assert.Equal(reattemptDate, ex.ScheduledReattemptDateUtc);
        Assert.Contains("after 2 PM tomorrow", ex.ResolutionNotes);
        Assert.False(ex.IsResolved);
    }

    [Fact]
    public void ScheduleReattempt_EmptyNotes_ThrowsValidationException()
    {
        var ex = new ShipmentDeliveryException();
        Assert.Throws<ValidationException>(() =>
            ex.ScheduleReattempt(DateTime.UtcNow.AddDays(1), "  ", Guid.NewGuid()));
    }

    [Fact]
    public void RecordCustomerContact_IncrementsCountAndTransitionsStatus()
    {
        var ex = new ShipmentDeliveryException
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            ExceptionType = DeliveryExceptionType.AddressNotFound
        };

        ex.RecordCustomerContact("Called consignee phone on file, left voicemail", Guid.NewGuid());

        Assert.Equal(1, ex.ContactAttemptsCount);
        Assert.Equal(DeliveryExceptionStatus.PendingCustomerAction, ex.Status);
        Assert.Equal(DeliveryExceptionResolution.CustomerContacted, ex.Resolution);

        ex.RecordCustomerContact("Consignee responded via SMS with gate code 4921", Guid.NewGuid());
        Assert.Equal(2, ex.ContactAttemptsCount);
        Assert.Contains("gate code 4921", ex.ResolutionNotes);
    }

    [Fact]
    public void UpdateAddress_ValidAddress_TransitionsToUnderReview()
    {
        var ex = new ShipmentDeliveryException
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            ExceptionType = DeliveryExceptionType.AddressNotFound
        };

        ex.UpdateAddress("742 Evergreen Terrace, Apt 4B", "Added missing apartment suite number", Guid.NewGuid());

        Assert.Equal("742 Evergreen Terrace, Apt 4B", ex.CorrectedAddress);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, ex.Status);
        Assert.Equal(DeliveryExceptionResolution.AddressCorrected, ex.Resolution);
    }

    [Fact]
    public void RedirectToDepot_ValidLocation_TransitionsToUnderReview()
    {
        var ex = new ShipmentDeliveryException
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            ExceptionType = DeliveryExceptionType.RecipientUnavailable
        };

        ex.RedirectToDepot("FedEx Ship Center - 100 Main St, Chicago, IL", "Hold for pickup authorized by recipient", Guid.NewGuid());

        Assert.Equal("FedEx Ship Center - 100 Main St, Chicago, IL", ex.HoldDepotLocation);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, ex.Status);
        Assert.Equal(DeliveryExceptionResolution.HoldAtCarrierDepot, ex.Resolution);
    }

    [Fact]
    public void AuthorizeReturnToSender_ValidReturn_TransitionsToReturnToSenderInitiated()
    {
        var ex = new ShipmentDeliveryException
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            ExceptionType = DeliveryExceptionType.CustomerRefused
        };

        var returnShipmentId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        ex.AuthorizeReturnToSender(returnShipmentId, "Customer refused package at doorstep; return authorized by merchant", staffUserId);

        Assert.Equal(returnShipmentId, ex.ReturnShipmentId);
        Assert.Equal(DeliveryExceptionStatus.ReturnToSenderInitiated, ex.Status);
        Assert.Equal(DeliveryExceptionResolution.ReturnToSenderAuthorized, ex.Resolution);
        Assert.Equal(staffUserId, ex.ResolvedByUserId);
        Assert.NotNull(ex.ResolvedAtUtc);
        Assert.True(ex.IsResolved);
    }

    [Fact]
    public void Resolve_ValidResolution_MarksResolved()
    {
        var ex = new ShipmentDeliveryException
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            ExceptionType = DeliveryExceptionType.RecipientUnavailable
        };

        var staffUserId = Guid.NewGuid();
        ex.Resolve(DeliveryExceptionResolution.Resolved, "Reattempt completed successfully, parcel signed for", staffUserId);

        Assert.Equal(DeliveryExceptionStatus.Resolved, ex.Status);
        Assert.Equal(DeliveryExceptionResolution.Resolved, ex.Resolution);
        Assert.Equal(staffUserId, ex.ResolvedByUserId);
        Assert.NotNull(ex.ResolvedAtUtc);
        Assert.True(ex.IsResolved);

        // Attempting to resolve again throws
        Assert.Throws<InvalidDomainStateException>(() =>
            ex.Resolve(DeliveryExceptionResolution.Resolved, "Duplicate resolution attempt", staffUserId));
    }

    [Fact]
    public void Void_ValidReason_MarksVoided()
    {
        var ex = new ShipmentDeliveryException
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            ExceptionType = DeliveryExceptionType.CarrierOperationalDelay
        };

        var staffUserId = Guid.NewGuid();
        ex.Void("Carrier confirmed scan was entered against wrong parcel", staffUserId);

        Assert.Equal(DeliveryExceptionStatus.Voided, ex.Status);
        Assert.Equal(DeliveryExceptionResolution.CarrierErrorRescinded, ex.Resolution);
        Assert.True(ex.IsResolved);
    }

    // ==========================================
    // 2. ReturnShipment Domain Lifecycle Tests
    // ==========================================

    [Fact]
    public void ReturnShipment_InitialState_IsAuthorized()
    {
        var returnShipment = new ReturnShipment
        {
            ClientId = Guid.NewGuid(),
            OriginFacilityId = Guid.NewGuid(),
            DestinationFacilityId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            ReturnRmaNumber = "RMA-20261104-0001",
            Reason = ReturnReason.CustomerRefused,
            AuthorizedByUserId = Guid.NewGuid(),
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = "RET-TRK-990011",
            OriginAddressLine1 = "123 Consignee Lane",
            OriginCity = "Peoria",
            OriginState = "IL",
            OriginPostalCode = "61602",
            DestinationAddressLine1 = "100 Warehouse Way",
            DestinationCity = "Chicago",
            DestinationState = "IL",
            DestinationPostalCode = "60601"
        };

        Assert.Equal(ReturnShipmentStatus.Authorized, returnShipment.Status);
        Assert.Null(returnShipment.CarrierHandoverAtUtc);
        Assert.Null(returnShipment.DeliveredAtWarehouseUtc);
        Assert.Null(returnShipment.ReceivedAndInspectedAtUtc);
        Assert.Null(returnShipment.FinalDisposition);
    }

    [Fact]
    public void ReturnShipment_LifecycleProgression_AdvancesMonotonically()
    {
        var rts = new ReturnShipment
        {
            ClientId = Guid.NewGuid(),
            OriginFacilityId = Guid.NewGuid(),
            DestinationFacilityId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            ReturnRmaNumber = "RMA-20261104-0002",
            Reason = ReturnReason.UndeliverableAddress,
            AuthorizedByUserId = Guid.NewGuid(),
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = "RET-TRK-990022"
        };

        var handoverTime = DateTime.UtcNow.AddDays(-2);
        rts.RecordCarrierHandover(handoverTime);
        Assert.Equal(ReturnShipmentStatus.InTransitToWarehouse, rts.Status);
        Assert.Equal(handoverTime, rts.CarrierHandoverAtUtc);

        var inTransitTime = DateTime.UtcNow.AddDays(-1);
        rts.RecordInTransit(inTransitTime);
        Assert.Equal(ReturnShipmentStatus.InTransitToWarehouse, rts.Status);

        var outForDeliveryTime = DateTime.UtcNow.AddHours(-3);
        rts.RecordOutForDelivery(outForDeliveryTime);
        Assert.Equal(ReturnShipmentStatus.OutForDeliveryToWarehouse, rts.Status);

        var deliveredTime = DateTime.UtcNow.AddMinutes(-30);
        rts.RecordDeliveredAtWarehouse(deliveredTime);
        Assert.Equal(ReturnShipmentStatus.DeliveredAtWarehouse, rts.Status);
        Assert.Equal(deliveredTime, rts.DeliveredAtWarehouseUtc);
    }

    [Fact]
    public void ReturnShipment_Cancel_BeforeHandover_Succeeds()
    {
        var rts = new ReturnShipment
        {
            ReturnRmaNumber = "RMA-CANCEL-01",
            Reason = ReturnReason.CustomerCancelled,
            AuthorizedByUserId = Guid.NewGuid()
        };

        rts.Cancel("Customer decided to accept parcel after all", Guid.NewGuid());
        Assert.Equal(ReturnShipmentStatus.Cancelled, rts.Status);

        // Attempting to record handover on cancelled RTS throws
        Assert.Throws<InvalidDomainStateException>(() =>
            rts.RecordCarrierHandover(DateTime.UtcNow));
    }

    [Fact]
    public void ReturnShipment_Cancel_AfterHandover_ThrowsInvalidDomainStateException()
    {
        var rts = new ReturnShipment();
        rts.RecordCarrierHandover(DateTime.UtcNow.AddHours(-1));

        Assert.Throws<InvalidDomainStateException>(() =>
            rts.Cancel("Too late to cancel", Guid.NewGuid()));
    }

    [Fact]
    public void ReturnShipment_MarkLost_DuringTransit_Succeeds()
    {
        var rts = new ReturnShipment();
        rts.RecordCarrierHandover(DateTime.UtcNow.AddDays(-5));

        rts.MarkLost("Carrier tracing investigation declared reverse freight lost in transit");
        Assert.Equal(ReturnShipmentStatus.LostInReturn, rts.Status);
        Assert.Contains("lost in reverse transit", rts.DispositionNotes, StringComparison.OrdinalIgnoreCase);
    }

    // ==========================================
    // 3. ReturnTrackingEvent Deduplication Tests
    // ==========================================

    [Fact]
    public void ReturnTrackingEvent_ComputeFingerprint_IsDeterministicAndCaseInsensitive()
    {
        var clientId = Guid.NewGuid();
        var occurredTime = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

        var fp1 = ReturnTrackingEvent.ComputeFingerprint(clientId, "SIM-FEDEX", "RET-123456", "IN_TRANSIT", occurredTime);
        var fp2 = ReturnTrackingEvent.ComputeFingerprint(clientId, "sim-fedex", "ret-123456", "in_transit", occurredTime);

        Assert.Equal(fp1, fp2);
        Assert.Equal(64, fp1.Length); // 64 hex characters (256 bits)
    }

    // ==========================================
    // 4. EF Core Persistence & Query Filters
    // ==========================================

    [Fact]
    public async Task Persistence_DeliveryExceptionAndReturnShipment_SavesAndQueriesNavigationGraph()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg) = await CreateDispatchedPackageAsync(fix);

        var exceptionId = Guid.NewGuid();
        var returnShipmentId = Guid.NewGuid();

        // 1. Create and persist Delivery Exception
        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var deliveryException = new ShipmentDeliveryException
            {
                Id = exceptionId,
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg.Id,
                ShipmentId = shipment.Id,
                OrderId = order.Id,
                TrackingNumber = pkg.TrackingNumber!,
                CarrierCode = "SIM-FEDEX",
                ExceptionType = DeliveryExceptionType.CustomerRefused,
                Status = DeliveryExceptionStatus.Open,
                Severity = DeliveryExceptionSeverity.High,
                CarrierExceptionCode = "R01",
                CarrierDescription = "Consignee refused delivery at doorstep",
                OccurredAtUtc = DateTime.UtcNow.AddHours(-1)
            };
            context.ShipmentDeliveryExceptions.Add(deliveryException);

            // 2. Authorize Return-To-Sender Shipment
            var returnShipment = new ReturnShipment
            {
                Id = returnShipmentId,
                ClientId = fix.ClientId,
                OriginFacilityId = fix.FacilityId,
                DestinationFacilityId = fix.DestinationFacilityId,
                OrderId = order.Id,
                OriginalShipmentId = shipment.Id,
                PackageId = pkg.Id,
                DeliveryExceptionId = exceptionId,
                ReturnRmaNumber = $"RMA-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
                Reason = ReturnReason.CustomerRefused,
                AuthorizedByUserId = fix.StaffUserId,
                AuthorizedAtUtc = DateTime.UtcNow,
                AuthorizationNotes = "Consignee refused delivery. Return to centralized Dallas returns center.",
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = $"RTS-TRK-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
                CarrierServiceLevel = "Standard Ground Return",
                OriginAddressLine1 = "555 Residential Way",
                OriginCity = "Peoria",
                OriginState = "IL",
                OriginPostalCode = "61602",
                DestinationAddressLine1 = "999 Logistics Loop",
                DestinationCity = "Dallas",
                DestinationState = "TX",
                DestinationPostalCode = "75201",
                Status = ReturnShipmentStatus.Authorized
            };
            context.ReturnShipments.Add(returnShipment);

            // Link RTS back to exception
            deliveryException.AuthorizeReturnToSender(returnShipmentId, "Return shipment authorized", fix.StaffUserId);

            // Add Return Tracking Event
            var returnEvent = new ReturnTrackingEvent
            {
                ClientId = fix.ClientId,
                ReturnShipmentId = returnShipmentId,
                TrackingNumber = returnShipment.TrackingNumber,
                CarrierCode = returnShipment.CarrierCode,
                EventType = "RETURN_LABEL_CREATED",
                Status = ReturnShipmentStatus.Authorized,
                OccurredAtUtc = DateTime.UtcNow,
                EventFingerprint = ReturnTrackingEvent.ComputeFingerprint(fix.ClientId, returnShipment.CarrierCode, returnShipment.TrackingNumber, "RETURN_LABEL_CREATED", DateTime.UtcNow)
            };
            context.ReturnTrackingEvents.Add(returnEvent);

            await context.SaveChangesAsync();
        }

        // 3. Query back and verify navigation properties
        using (var queryContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = fix.ClientId }, new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId }, fix.DbName))
        {
            var loadedPackage = await queryContext.Packages
                .Include(p => p.DeliveryExceptions)
                .Include(p => p.ReturnShipments)
                    .ThenInclude(r => r.TrackingEvents)
                .FirstAsync(p => p.Id == pkg.Id);

            Assert.Single(loadedPackage.DeliveryExceptions);
            var loadedEx = loadedPackage.DeliveryExceptions.First();
            Assert.Equal(DeliveryExceptionType.CustomerRefused, loadedEx.ExceptionType);
            Assert.Equal(DeliveryExceptionStatus.ReturnToSenderInitiated, loadedEx.Status);
            Assert.Equal(returnShipmentId, loadedEx.ReturnShipmentId);

            Assert.Single(loadedPackage.ReturnShipments);
            var loadedRts = loadedPackage.ReturnShipments.First();
            Assert.Equal(ReturnReason.CustomerRefused, loadedRts.Reason);
            Assert.Equal(fix.DestinationFacilityId, loadedRts.DestinationFacilityId);
            Assert.Single(loadedRts.TrackingEvents);
            Assert.Equal("RETURN_LABEL_CREATED", loadedRts.TrackingEvents.First().EventType);

            var loadedOrder = await queryContext.OutboundOrders
                .Include(o => o.DeliveryExceptions)
                .Include(o => o.ReturnShipments)
                .FirstAsync(o => o.Id == order.Id);

            Assert.Single(loadedOrder.DeliveryExceptions);
            Assert.Single(loadedOrder.ReturnShipments);
        }
    }

    [Fact]
    public async Task MultiTenancy_TenantIsolation_FiltersCrossTenantReturnShipments()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg) = await CreateDispatchedPackageAsync(fix);

        var foreignClientId = Guid.NewGuid();
        var returnShipmentId = Guid.NewGuid();

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var foreignRts = new ReturnShipment
            {
                Id = returnShipmentId,
                ClientId = foreignClientId, // Different tenant
                OriginFacilityId = fix.FacilityId,
                DestinationFacilityId = fix.DestinationFacilityId,
                OrderId = order.Id,
                PackageId = pkg.Id,
                ReturnRmaNumber = "RMA-FOREIGN-01",
                Reason = ReturnReason.UndeliverableAddress,
                AuthorizedByUserId = fix.StaffUserId,
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = "RET-FOREIGN-99",
                OriginAddressLine1 = "100 Unknown St",
                OriginCity = "Chicago",
                OriginState = "IL",
                OriginPostalCode = "60601",
                DestinationAddressLine1 = "200 Hub Way",
                DestinationCity = "Chicago",
                DestinationState = "IL",
                DestinationPostalCode = "60602"
            };
            context.ReturnShipments.Add(foreignRts);
            await context.SaveChangesAsync();
        }

        // Querying under fix.ClientId as Client user should NOT see foreign tenant's ReturnShipment
        using (var queryContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = fix.ClientId }, new TestCurrentUserContext { UserType = UserType.Client, TenantId = fix.ClientId }, fix.DbName))
        {
            var results = await queryContext.ReturnShipments.ToListAsync();
            Assert.DoesNotContain(results, r => r.Id == returnShipmentId);
        }
    }

    // ==========================================
    // 5. Zero Inventory Mutation Invariance
    // ==========================================

    [Fact]
    public async Task ZeroInventoryMutation_DeliveryExceptionAndReturnAuthorization_LeavesStockOnHandUntouched()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg) = await CreateDispatchedPackageAsync(fix);

        // Record initial stock state
        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var stockBefore = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
            var initialTotal = stockBefore.TotalQuantity;
            var initialAllocated = stockBefore.QtyAllocated;
            var initialAvailable = stockBefore.QtyAvailable;
            var initialTxCount = await context.InventoryTransactions.CountAsync();

            // Perform Delivery Exception and Return Authorization
            var ex = new ShipmentDeliveryException
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg.Id,
                ShipmentId = shipment.Id,
                OrderId = order.Id,
                TrackingNumber = pkg.TrackingNumber!,
                CarrierCode = "SIM-FEDEX",
                ExceptionType = DeliveryExceptionType.PackageDamagedInTransit,
                CarrierExceptionCode = "DMG01",
                CarrierDescription = "Package damaged in transit",
                OccurredAtUtc = DateTime.UtcNow
            };
            context.ShipmentDeliveryExceptions.Add(ex);

            var rts = new ReturnShipment
            {
                ClientId = fix.ClientId,
                OriginFacilityId = fix.FacilityId,
                DestinationFacilityId = fix.DestinationFacilityId,
                OrderId = order.Id,
                OriginalShipmentId = shipment.Id,
                PackageId = pkg.Id,
                DeliveryExceptionId = ex.Id,
                ReturnRmaNumber = "RMA-INV-TEST-01",
                Reason = ReturnReason.DamagedInTransit,
                AuthorizedByUserId = fix.StaffUserId,
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = "RTS-INV-TRK-01",
                OriginAddressLine1 = "Damaged Hub",
                OriginCity = "Chicago",
                OriginState = "IL",
                OriginPostalCode = "60601",
                DestinationAddressLine1 = "Returns Center",
                DestinationCity = "Dallas",
                DestinationState = "TX",
                DestinationPostalCode = "75201"
            };
            context.ReturnShipments.Add(rts);

            await context.SaveChangesAsync();

            // Verify strict zero inventory change invariant: Delta Q = 0
            var stockAfter = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
            Assert.Equal(initialTotal, stockAfter.TotalQuantity);
            Assert.Equal(initialAllocated, stockAfter.QtyAllocated);
            Assert.Equal(initialAvailable, stockAfter.QtyAvailable);

            var finalTxCount = await context.InventoryTransactions.CountAsync();
            Assert.Equal(initialTxCount, finalTxCount);
        }
    }
}
