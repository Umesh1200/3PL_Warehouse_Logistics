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

public class DeliveryExceptionIngestionTests
{
    private class TestEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid DriverUserId { get; set; } = Guid.NewGuid();
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
            CompanyName = "Global Freight Logistics",
            AccountCode = $"GFL{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var facility = new Facility
        {
            Id = fix.FacilityId,
            Name = "Chicago Gateway Hub",
            FacilityCode = "ORD2"
        };

        var driverUser = new User
        {
            Id = fix.DriverUserId,
            Email = "driver.dave@warehouse3pl.test",
            PasswordHash = "hash",
            FirstName = "Dave",
            LastName = "Driver",
            UserType = UserType.Driver,
            Status = UserStatus.Active
        };

        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = $"SKU-EXC-{Guid.NewGuid().ToString()[..6]}",
            Description = "Medical Scanner",
            WeightKg = 2.5m,
            LengthCm = 15m,
            WidthCm = 15m,
            HeightCm = 15m
        };

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "BIN-EXC-01",
            Barcode = "BIN-EXC-01",
            LocationType = LocationType.Bin
        };

        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LocationId = loc.Id,
            ItemId = item.Id,
            QtyAvailable = 250m,
            QtyAllocated = 25m
        };

        context.Clients.Add(client);
        context.Facilities.Add(facility);
        context.Users.Add(driverUser);
        context.Items.Add(item);
        context.Locations.Add(loc);
        context.StockOnHand.Add(stock);

        await context.SaveChangesAsync();
        return fix;
    }

    private static async Task<(OutboundOrder Order, Shipment Shipment, Package Package)> CreateDispatchedPackageAsync(
        TestEnvFixture fix,
        string carrierCode = "SIM-FEDEX")
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
            FacilityId = fix.FacilityId,
            OrderNumber = $"ORD-ING-{Guid.NewGuid().ToString()[..6]}",
            Status = OutboundOrderStatus.Completed,
            DeliveryStatus = OrderDeliveryStatus.Dispatched
        };
        context.OutboundOrders.Add(order);

        var shipmentId = Guid.NewGuid();
        var shipment = new Shipment
        {
            Id = shipmentId,
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentNumber = $"SHP-ING-{Guid.NewGuid().ToString()[..6]}",
            CarrierCode = carrierCode,
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.PendingHandover,
            DispatchedAtUtc = DateTime.UtcNow.AddHours(-3)
        };
        context.Shipments.Add(shipment);

        var huId = Guid.NewGuid();
        var hu = new HandlingUnit
        {
            Id = huId,
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LpnCode = $"HU-ING-{Guid.NewGuid().ToString()[..6]}",
            UnitType = HandlingUnitType.ShippingCarton,
            Status = HandlingUnitStatus.InUse
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
            PackageNumber = $"PKG-ING-{Guid.NewGuid().ToString()[..6]}",
            TrackingNumber = $"TRK-ING-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            CarrierCode = carrierCode,
            Status = PackageStatus.Loaded,
            CurrentTrackingStatus = TrackingMilestoneStatus.InTransit,
            TrackingStatusUpdatedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        context.Packages.Add(pkg);

        await context.SaveChangesAsync();
        return (order, shipment, pkg);
    }

    // ==========================================
    // 1. Carrier Exception Ingestion Tests
    // ==========================================

    [Fact]
    public async Task IngestCarrierException_ValidPayload_ClassifiesAndRollsUpOrder()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var occurTime = DateTime.UtcNow.AddMinutes(-20);
        var request = new IngestCarrierExceptionRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventId = "EVT-FEDEX-9901",
            CarrierExceptionCode = "D02",
            CarrierDescription = "Customer unavailable - business closed",
            OccurredAtUtc = occurTime,
            EventCity = "Chicago",
            EventStateOrProvince = "IL",
            RawPayloadJson = "{\"status\":\"D02\",\"desc\":\"Customer unavailable\"}"
        };

        var result = await service.IngestCarrierExceptionAsync(request);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsDuplicate);
        Assert.Equal(DeliveryExceptionType.RecipientUnavailable, result.ExceptionType);
        Assert.Equal(DeliveryExceptionStatus.Open, result.Status);
        Assert.Equal(DeliveryExceptionSeverity.Medium, result.Severity);
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, result.PackageTrackingStatus);
        Assert.Equal(OrderDeliveryStatus.DeliveryException, result.OrderDeliveryStatus);

        // Verify package and tracking event in database
        var savedPkg = await context.Packages.FindAsync(pkg.Id);
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, savedPkg!.CurrentTrackingStatus);

        var savedOrder = await context.OutboundOrders.FindAsync(order.Id);
        Assert.Equal(OrderDeliveryStatus.DeliveryException, savedOrder!.DeliveryStatus);

        var savedException = await context.ShipmentDeliveryExceptions.FindAsync(result.ExceptionId);
        Assert.NotNull(savedException);
        Assert.Equal("EVT-FEDEX-9901", savedException.CarrierEventId);
        Assert.Equal(DeliveryExceptionSource.CarrierWebhook, savedException.Source);
        Assert.NotNull(savedException.EventFingerprint);

        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.MilestoneStatus == TrackingMilestoneStatus.DeliveryException);
        Assert.NotNull(trackingEvent);
        Assert.Equal("D02", trackingEvent.CarrierEventCode);
    }

    [Fact]
    public async Task IngestCarrierException_DuplicateEvent_ReturnsExistingResultIdempotently()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        var occurTime = DateTime.UtcNow.AddMinutes(-30);
        var request = new IngestCarrierExceptionRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierExceptionCode = "W01",
            CarrierDescription = "Severe blizzard road closure",
            OccurredAtUtc = occurTime
        };

        // First call
        var result1 = await service.IngestCarrierExceptionAsync(request);
        Assert.True(result1.IsSuccess);
        Assert.False(result1.IsDuplicate);

        // Repeated call with same payload
        var result2 = await service.IngestCarrierExceptionAsync(request);
        Assert.True(result2.IsSuccess);
        Assert.True(result2.IsDuplicate);
        Assert.Equal(result1.ExceptionId, result2.ExceptionId);

        // Verify only 1 exception record exists in DB
        var totalExceptions = await context.ShipmentDeliveryExceptions.CountAsync(e => e.PackageId == pkg.Id);
        Assert.Equal(1, totalExceptions);
    }

    [Fact]
    public async Task IngestCarrierException_UnknownCarrierCode_FallsBackToOtherClassification()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new DeliveryExceptionService(context, new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        var result = await service.IngestCarrierExceptionAsync(new IngestCarrierExceptionRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierExceptionCode = "UNKNOWN_CODE_999",
            CarrierDescription = "Special unclassified anomaly occurred",
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-10)
        });

        Assert.Equal(DeliveryExceptionType.Other, result.ExceptionType);
        Assert.Equal(DeliveryExceptionSeverity.Medium, result.Severity);

        var savedEx = await context.ShipmentDeliveryExceptions.FindAsync(result.ExceptionId);
        Assert.Equal("UNKNOWN_CODE_999", savedEx!.CarrierExceptionCode);
        Assert.Equal("Special unclassified anomaly occurred", savedEx.CarrierDescription);
    }

    [Fact]
    public async Task IngestCarrierException_CarrierMismatch_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix, carrierCode: "SIM-FEDEX");

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new DeliveryExceptionService(context, new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        // Attempting to ingest FedEx package as UPS
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.IngestCarrierExceptionAsync(new IngestCarrierExceptionRequest
            {
                CarrierCode = "UPS",
                TrackingNumber = pkg.TrackingNumber!,
                CarrierExceptionCode = "D01",
                CarrierDescription = "Closed",
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5)
            }));

        Assert.Contains("does not match package assigned carrier", ex.Message);
    }

    [Fact]
    public async Task IngestCarrierException_CrossTenantAttempt_ThrowsUnauthorizedException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new DeliveryExceptionService(context, new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.IngestCarrierExceptionAsync(new IngestCarrierExceptionRequest
            {
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = pkg.TrackingNumber!,
                CarrierExceptionCode = "D01",
                CarrierDescription = "Closed",
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
                TenantIdOverride = Guid.NewGuid() // Different tenant
            }));
    }

    [Fact]
    public async Task IngestCarrierException_FutureTimestamp_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new DeliveryExceptionService(context, new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.IngestCarrierExceptionAsync(new IngestCarrierExceptionRequest
            {
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = pkg.TrackingNumber!,
                CarrierExceptionCode = "D01",
                CarrierDescription = "Closed",
                OccurredAtUtc = DateTime.UtcNow.AddHours(2) // Future
            }));
    }

    // ==========================================
    // 2. Driver Exception Reporting Tests
    // ==========================================

    [Fact]
    public async Task ReportDriverException_ValidDriverPayload_RecordsCoordinatesAndAudit()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var driverContext = new TestCurrentUserContext
        {
            UserId = fix.DriverUserId,
            UserType = UserType.Driver,
            TenantId = fix.ClientId,
            FacilityId = fix.FacilityId
        };

        var service = new DeliveryExceptionService(context, tenantProvider, driverContext, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        var occurTime = DateTime.UtcNow.AddMinutes(-15);
        var request = new ReportDriverExceptionRequest
        {
            PackageId = pkg.Id,
            ExceptionType = DeliveryExceptionType.AccessRestricted,
            DriverNotes = "Gated community, security keypad not accepting code, recipient not answering call box",
            OccurredAtUtc = occurTime,
            Latitude = 41.8781m,
            Longitude = -87.6298m
        };

        var result = await service.ReportDriverExceptionAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryExceptionType.AccessRestricted, result.ExceptionType);
        Assert.Equal(DeliveryExceptionStatus.Open, result.Status);
        Assert.Equal(DeliveryExceptionSeverity.High, result.Severity);
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, result.PackageTrackingStatus);
        Assert.Equal(OrderDeliveryStatus.DeliveryException, result.OrderDeliveryStatus);

        var savedEx = await context.ShipmentDeliveryExceptions.FindAsync(result.ExceptionId);
        Assert.NotNull(savedEx);
        Assert.Equal(DeliveryExceptionSource.DriverHandheld, savedEx.Source);
        Assert.Equal(fix.DriverUserId, savedEx.ReportedByUserId);
        Assert.Equal(41.8781m, savedEx.Latitude);
        Assert.Equal(-87.6298m, savedEx.Longitude);
        Assert.Contains("security keypad not accepting code", savedEx.CarrierDescription);

        var savedOrder = await context.OutboundOrders.FindAsync(order.Id);
        Assert.Equal(OrderDeliveryStatus.DeliveryException, savedOrder!.DeliveryStatus);
    }

    [Fact]
    public async Task ReportDriverException_UnauthorizedClientUser_ThrowsForbiddenException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var clientContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Client, // Client cannot report driver exceptions
            TenantId = fix.ClientId,
            FacilityId = fix.FacilityId
        };

        var service = new DeliveryExceptionService(context, new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true }, clientContext, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ReportDriverExceptionAsync(new ReportDriverExceptionRequest
            {
                PackageId = pkg.Id,
                ExceptionType = DeliveryExceptionType.RecipientUnavailable,
                DriverNotes = "Attempt failed",
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5)
            }));
    }

    [Fact]
    public async Task ReportDriverException_DriverFromDifferentFacility_ThrowsForbiddenException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var foreignDriverContext = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Driver,
            TenantId = fix.ClientId,
            FacilityId = Guid.NewGuid() // Different facility
        };

        var service = new DeliveryExceptionService(context, new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true }, foreignDriverContext, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ReportDriverExceptionAsync(new ReportDriverExceptionRequest
            {
                PackageId = pkg.Id,
                ExceptionType = DeliveryExceptionType.CustomerRefused,
                DriverNotes = "Customer refused",
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5)
            }));
    }

    [Fact]
    public async Task ReportDriverException_InvalidCoordinates_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var driverContext = new TestCurrentUserContext
        {
            UserId = fix.DriverUserId,
            UserType = UserType.Driver,
            TenantId = fix.ClientId,
            FacilityId = fix.FacilityId
        };

        var service = new DeliveryExceptionService(context, new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true }, driverContext, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        // Latitude > 90
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReportDriverExceptionAsync(new ReportDriverExceptionRequest
            {
                PackageId = pkg.Id,
                ExceptionType = DeliveryExceptionType.RecipientUnavailable,
                DriverNotes = "Closed",
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
                Latitude = 95.0m
            }));
    }

    // ==========================================
    // 3. Domain Model Relationship & State Machine Guards
    // ==========================================

    [Fact]
    public void ValidateRelationships_MismatchedOrderOrShipment_ThrowsValidationException()
    {
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var order1Id = Guid.NewGuid();
        var order2Id = Guid.NewGuid();

        var pkg = new Package
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FacilityId = facilityId,
            OutboundOrderId = order1Id,
            PackageNumber = "PKG-REL-01"
        };

        var mismatchedOrder = new OutboundOrder
        {
            Id = order2Id,
            ClientId = clientId,
            FacilityId = facilityId,
            OrderNumber = "ORD-OTHER"
        };

        var exception = new ShipmentDeliveryException
        {
            ClientId = clientId,
            FacilityId = facilityId,
            PackageId = pkg.Id,
            OrderId = order2Id
        };

        var ex = Assert.Throws<ValidationException>(() =>
            exception.ValidateRelationships(pkg, null, mismatchedOrder));

        Assert.Contains("does not belong to order", ex.Message);
    }

    [Fact]
    public void Lifecycle_VoidedExceptionCannotBeResolved()
    {
        var ex = new ShipmentDeliveryException { Status = DeliveryExceptionStatus.Open };
        ex.Void("Erroneous scan", Guid.NewGuid());
        Assert.Equal(DeliveryExceptionStatus.Voided, ex.Status);

        Assert.Throws<InvalidDomainStateException>(() =>
            ex.Resolve(DeliveryExceptionResolution.Resolved, "Resolving voided exception", Guid.NewGuid()));
    }

    [Fact]
    public void Lifecycle_ResolvedExceptionCannotReattemptWithoutReopening()
    {
        var ex = new ShipmentDeliveryException { Status = DeliveryExceptionStatus.Open };
        ex.Resolve(DeliveryExceptionResolution.Resolved, "Successfully completed", Guid.NewGuid());

        Assert.Throws<InvalidDomainStateException>(() =>
            ex.ScheduleReattempt(DateTime.UtcNow.AddDays(1), "New attempt", Guid.NewGuid()));

        // Explicit Reopen enables reattempt scheduling
        ex.Reopen("Customer called to report they are available after all", Guid.NewGuid());
        Assert.Equal(DeliveryExceptionStatus.Open, ex.Status);
        Assert.False(ex.IsResolved);

        ex.ScheduleReattempt(DateTime.UtcNow.AddDays(1), "Rescheduled following customer contact", Guid.NewGuid());
        Assert.Equal(DeliveryExceptionStatus.ScheduledForReattempt, ex.Status);
    }

    [Fact]
    public void Lifecycle_DoubleReturnAuthorization_ThrowsInvalidDomainStateException()
    {
        var ex = new ShipmentDeliveryException { Status = DeliveryExceptionStatus.Open };
        var rtsId = Guid.NewGuid();
        ex.AuthorizeReturnToSender(rtsId, "First RTS authorization", Guid.NewGuid());
        Assert.Equal(DeliveryExceptionStatus.ReturnToSenderInitiated, ex.Status);

        Assert.Throws<InvalidDomainStateException>(() =>
            ex.AuthorizeReturnToSender(Guid.NewGuid(), "Second RTS authorization attempt", Guid.NewGuid()));
    }

    // ==========================================
    // 4. CQRS Commands & Queries Tests
    // ==========================================

    [Fact]
    public async Task CQRS_IngestCarrierAndReportDriverCommands_ExecuteSuccessfully()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new DeliveryExceptionService(context, new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        var carrierHandler = new IngestCarrierDeliveryExceptionCommandHandler(service);
        var carrierCmdResult = await carrierHandler.Handle(new IngestCarrierDeliveryExceptionCommand(new IngestCarrierExceptionRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierExceptionCode = "DMG01",
            CarrierDescription = "Crushed carton",
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5)
        }), CancellationToken.None);

        Assert.True(carrierCmdResult.IsSuccess);
        Assert.Equal(DeliveryExceptionType.PackageDamagedInTransit, carrierCmdResult.ExceptionType);
        Assert.Equal(DeliveryExceptionSeverity.Critical, carrierCmdResult.Severity);

        var driverHandler = new ReportDriverDeliveryExceptionCommandHandler(service);
        var driverCmdResult = await driverHandler.Handle(new ReportDriverDeliveryExceptionCommand(new ReportDriverExceptionRequest
        {
            PackageId = pkg.Id,
            ExceptionType = DeliveryExceptionType.RecipientUnavailable,
            DriverNotes = "Recipient not answering door",
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-2),
            DriverUserId = fix.DriverUserId
        }), CancellationToken.None);

        Assert.True(driverCmdResult.IsSuccess);
        Assert.Equal(DeliveryExceptionType.RecipientUnavailable, driverCmdResult.ExceptionType);

        var exceptionsList = await service.GetExceptionsForPackageAsync(pkg.Id);
        Assert.Equal(2, exceptionsList.Count);
    }

    // ==========================================
    // 5. Zero Inventory Invariance Test
    // ==========================================

    [Fact]
    public async Task ZeroInventoryMutation_CarrierAndDriverExceptionIngestion_LeavesStockOnHandUntouched()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, pkg) = await CreateDispatchedPackageAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        // Record pre-ingestion stock and transaction state
        var stockBefore = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        var initialTotal = stockBefore.TotalQuantity;
        var initialAllocated = stockBefore.QtyAllocated;
        var initialAvailable = stockBefore.QtyAvailable;
        var initialTxCount = await context.InventoryTransactions.CountAsync();

        var service = new DeliveryExceptionService(context, new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, new CarrierExceptionClassificationService(), NullLogger<DeliveryExceptionService>.Instance);

        // 1. Ingest carrier exception
        await service.IngestCarrierExceptionAsync(new IngestCarrierExceptionRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierExceptionCode = "D02",
            CarrierDescription = "Business closed",
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-10)
        });

        // 2. Report driver exception
        await service.ReportDriverExceptionAsync(new ReportDriverExceptionRequest
        {
            PackageId = pkg.Id,
            ExceptionType = DeliveryExceptionType.AccessRestricted,
            DriverNotes = "Gate locked",
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
            DriverUserId = fix.DriverUserId
        });

        // Verify zero inventory change: Delta Q = 0
        var stockAfter = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        Assert.Equal(initialTotal, stockAfter.TotalQuantity);
        Assert.Equal(initialAllocated, stockAfter.QtyAllocated);
        Assert.Equal(initialAvailable, stockAfter.QtyAvailable);

        var finalTxCount = await context.InventoryTransactions.CountAsync();
        Assert.Equal(initialTxCount, finalTxCount);
    }
}

