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

public class DeliveryExceptionResolutionAndReattemptTests
{
    private class TestEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid ForeignFacilityId { get; set; } = Guid.NewGuid();
        public Guid ForeignClientId { get; set; } = Guid.NewGuid();
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
            CompanyName = "Vertex Logistics Direct",
            AccountCode = $"VTX{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Foreign Competitor Corp",
            AccountCode = $"FCC{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var facility = new Facility
        {
            Id = fix.FacilityId,
            Name = "Dallas Central Hub",
            FacilityCode = "DFW1"
        };

        var foreignFacility = new Facility
        {
            Id = fix.ForeignFacilityId,
            Name = "Atlanta Hub",
            FacilityCode = "ATL1"
        };

        var staffUser = new User
        {
            Id = fix.StaffUserId,
            Email = "ops.sarah@warehouse3pl.test",
            PasswordHash = "hash",
            FirstName = "Sarah",
            LastName = "Ops",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = $"SKU-RES-{Guid.NewGuid().ToString()[..6]}",
            Description = "Precision Electronics",
            WeightKg = 1.2m,
            LengthCm = 10m,
            WidthCm = 10m,
            HeightCm = 10m
        };

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.FacilityId,
            LocationCode = "BIN-RES-01",
            Barcode = "BIN-RES-01",
            LocationType = LocationType.Bin
        };

        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LocationId = loc.Id,
            ItemId = item.Id,
            QtyAvailable = 500m,
            QtyAllocated = 50m
        };

        context.Clients.AddRange(client, foreignClient);
        context.Facilities.AddRange(facility, foreignFacility);
        context.Users.Add(staffUser);
        context.Items.Add(item);
        context.Locations.Add(loc);
        context.StockOnHand.Add(stock);

        await context.SaveChangesAsync();
        return fix;
    }

    private static async Task<(OutboundOrder Order, Shipment Shipment, Package Package, ShipmentDeliveryException Exception)> CreateDispatchedPackageWithExceptionAsync(
        TestEnvFixture fix,
        DeliveryExceptionType exceptionType = DeliveryExceptionType.RecipientUnavailable,
        DeliveryExceptionStatus status = DeliveryExceptionStatus.Open)
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
            OrderNumber = $"ORD-RES-{Guid.NewGuid().ToString()[..6]}",
            Status = OutboundOrderStatus.Completed,
            DeliveryStatus = OrderDeliveryStatus.DeliveryException
        };
        context.OutboundOrders.Add(order);

        var shipmentId = Guid.NewGuid();
        var shipment = new Shipment
        {
            Id = shipmentId,
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            ShipmentNumber = $"SHP-RES-{Guid.NewGuid().ToString()[..6]}",
            CarrierCode = "SIM-FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.DeliveryException,
            DispatchedAtUtc = DateTime.UtcNow.AddHours(-4)
        };
        context.Shipments.Add(shipment);

        var hu = new HandlingUnit
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LpnCode = $"HU-RES-{Guid.NewGuid().ToString()[..6]}",
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
            HandlingUnitId = hu.Id,
            PackageNumber = $"PKG-RES-{Guid.NewGuid().ToString()[..6]}",
            TrackingNumber = $"TRK-RES-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            CarrierCode = "SIM-FEDEX",
            Status = PackageStatus.Loaded,
            CurrentTrackingStatus = TrackingMilestoneStatus.DeliveryException,
            TrackingStatusUpdatedAtUtc = DateTime.UtcNow.AddMinutes(-30)
        };
        context.Packages.Add(pkg);

        var deliveryException = new ShipmentDeliveryException
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PackageId = pkg.Id,
            ShipmentId = shipmentId,
            OrderId = orderId,
            TrackingNumber = pkg.TrackingNumber,
            CarrierCode = "SIM-FEDEX",
            ExceptionType = exceptionType,
            Status = status,
            Severity = DeliveryExceptionSeverity.Medium,
            Source = DeliveryExceptionSource.CarrierWebhook,
            CarrierExceptionCode = "D02",
            CarrierDescription = "Consignee not available",
            OccurredAtUtc = DateTime.UtcNow.AddHours(-1),
            ReattemptCount = 0,
            ContactAttemptsCount = 0,
            EventFingerprint = $"FP-RES-{Guid.NewGuid():N}"
        };
        context.ShipmentDeliveryExceptions.Add(deliveryException);

        await context.SaveChangesAsync();
        return (order, shipment, pkg, deliveryException);
    }

    // ==============================================================
    // 1. Reattempt Scheduling Engine Tests
    // ==============================================================

    [Fact]
    public async Task ScheduleReattempt_ValidDate_IncrementsReattemptCountAndAppendsTrackingEvent()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var targetDate = DateTime.UtcNow.AddDays(1);
        var request = new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            ScheduledDateUtc = targetDate,
            Notes = "Customer confirmed home after 10 AM",
            UserId = fix.StaffUserId
        };

        var result = await service.ScheduleReattemptAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.ScheduledForReattempt, result.Status);

        // Verify entity updates in DB
        var updated = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updated);
        Assert.Equal(DeliveryExceptionStatus.ScheduledForReattempt, updated.Status);
        Assert.Equal(1, updated.ReattemptCount);
        Assert.Equal(targetDate, updated.ScheduledReattemptDateUtc);
        Assert.Contains("Customer confirmed home after 10 AM", updated.ResolutionNotes);

        // Verify tracking event appended
        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "REATTEMPT_SCHEDULED");
        Assert.NotNull(trackingEvent);
        Assert.Contains(targetDate.ToString("yyyy-MM-dd"), trackingEvent.CarrierDescription);
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, trackingEvent.MilestoneStatus);
    }

    [Fact]
    public async Task ScheduleReattempt_PastDate_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var pastDate = DateTime.UtcNow.AddMinutes(-30);
        var request = new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            ScheduledDateUtc = pastDate,
            Notes = "Retroactive scheduling"
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.ScheduleReattemptAsync(request));
    }

    [Fact]
    public async Task ScheduleReattempt_TooFarInFuture_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var distantDate = DateTime.UtcNow.AddDays(35); // Horizon > 30 days
        var request = new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            ScheduledDateUtc = distantDate,
            Notes = "Reattempt next month"
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.ScheduleReattemptAsync(request));
    }

    [Fact]
    public async Task ScheduleReattempt_AlreadyActiveReattempt_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // Schedule first reattempt
        await service.ScheduleReattemptAsync(new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            ScheduledDateUtc = DateTime.UtcNow.AddDays(1),
            Notes = "First reattempt"
        });

        // Attempting to schedule second reattempt without cancel/reschedule must fail
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.ScheduleReattemptAsync(new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            ScheduledDateUtc = DateTime.UtcNow.AddDays(2),
            Notes = "Second duplicate reattempt"
        }));
    }

    // ==============================================================
    // 2. Reattempt Rescheduling & Cancellation Tests
    // ==============================================================

    [Fact]
    public async Task RescheduleReattempt_ActiveReattempt_UpdatesDatePreservesCountAndAppendsTracking()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // Schedule initial reattempt
        await service.ScheduleReattemptAsync(new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            ScheduledDateUtc = DateTime.UtcNow.AddDays(1),
            Notes = "Initial reattempt"
        });

        // Reschedule
        var newDate = DateTime.UtcNow.AddDays(3);
        var rescheduleResult = await service.RescheduleReattemptAsync(new RescheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            NewScheduledDateUtc = newDate,
            Reason = "Consignee requested delay to Thursday",
            UserId = fix.StaffUserId
        });

        Assert.True(rescheduleResult.IsSuccess);

        var updated = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updated);
        Assert.Equal(newDate, updated.ScheduledReattemptDateUtc);
        Assert.Equal(2, updated.ReattemptCount); // Domain increments reattempt count on rescheduling
        Assert.Contains("Consignee requested delay to Thursday", updated.ResolutionNotes);

        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "REATTEMPT_RESCHEDULED");
        Assert.NotNull(trackingEvent);
        Assert.Contains(newDate.ToString("yyyy-MM-dd"), trackingEvent.CarrierDescription);
    }

    [Fact]
    public async Task RescheduleReattempt_NotScheduled_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // Exception is in Open status (no active reattempt scheduled)
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.RescheduleReattemptAsync(new RescheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            NewScheduledDateUtc = DateTime.UtcNow.AddDays(2),
            Reason = "Try rescheduling"
        }));
    }

    [Fact]
    public async Task CancelReattempt_ActiveReattempt_ClearsScheduleSetsStatusUnderReviewAndAppendsTracking()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // Schedule first
        await service.ScheduleReattemptAsync(new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            ScheduledDateUtc = DateTime.UtcNow.AddDays(1),
            Notes = "Initial reattempt"
        });

        // Cancel
        var cancelResult = await service.CancelReattemptAsync(new CancelDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            Reason = "Consignee moved to depot pickup instead",
            UserId = fix.StaffUserId
        });

        Assert.True(cancelResult.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, cancelResult.Status);

        var updated = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updated);
        Assert.Null(updated.ScheduledReattemptDateUtc);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, updated.Status);
        Assert.Contains("Consignee moved to depot pickup instead", updated.ResolutionNotes);

        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "REATTEMPT_CANCELLED");
        Assert.NotNull(trackingEvent);
    }

    [Fact]
    public async Task CancelReattempt_NotScheduled_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.CancelReattemptAsync(new CancelDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            Reason = "Cancel non-scheduled"
        }));
    }

    // ==============================================================
    // 3. Customer Contact Management Tests
    // ==============================================================

    [Fact]
    public async Task RecordCustomerContact_CreatesContactLogIncrementsCounterAndAppendsTracking()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var request = new RecordCustomerContactRequest
        {
            ExceptionId = exc.Id,
            ContactMethod = CustomerContactMethod.PhoneCall,
            Outcome = CustomerContactOutcome.ReachedAgreedToReattempt,
            Notes = "Called customer, confirmed gate code 4491",
            ResolutionAction = "Schedule reattempt for tomorrow morning",
            StaffUserId = fix.StaffUserId
        };

        var result = await service.RecordCustomerContactAsync(request);

        Assert.True(result.IsSuccess);

        // Verify exception counter and status
        var updated = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.ContactAttemptsCount);
        Assert.Equal(DeliveryExceptionStatus.PendingCustomerAction, updated.Status);
        Assert.Contains("Called customer, confirmed gate code 4491", updated.ResolutionNotes);

        // Verify contact log entry
        var log = await context.DeliveryExceptionContactLogs
            .FirstOrDefaultAsync(l => l.ExceptionId == exc.Id);
        Assert.NotNull(log);
        Assert.Equal(CustomerContactMethod.PhoneCall, log.ContactMethod);
        Assert.Equal(CustomerContactOutcome.ReachedAgreedToReattempt, log.Outcome);
        Assert.Equal(fix.StaffUserId, log.StaffUserId);
        Assert.Contains("gate code 4491", log.Notes);

        // Verify tracking event
        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "CUSTOMER_CONTACTED");
        Assert.NotNull(trackingEvent);
        Assert.Contains("ReachedAgreedToReattempt", trackingEvent.CarrierDescription);
    }

    [Fact]
    public async Task GetContactLogsForException_ReturnsLogsInReverseChronologicalOrder()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // Record first contact
        await service.RecordCustomerContactAsync(new RecordCustomerContactRequest
        {
            ExceptionId = exc.Id,
            ContactMethod = CustomerContactMethod.PhoneCall,
            Outcome = CustomerContactOutcome.LeftVoicemail,
            Notes = "Left voicemail #1",
            StaffUserId = fix.StaffUserId
        });

        // Small delay to ensure timestamp progression
        await Task.Delay(15);

        // Record second contact
        await service.RecordCustomerContactAsync(new RecordCustomerContactRequest
        {
            ExceptionId = exc.Id,
            ContactMethod = CustomerContactMethod.Sms,
            Outcome = CustomerContactOutcome.AwaitingCustomerCallback,
            Notes = "Sent automated SMS with rebooking link",
            StaffUserId = fix.StaffUserId
        });

        var logs = await service.GetContactLogsForExceptionAsync(exc.Id);

        Assert.Equal(2, logs.Count);
        Assert.Equal(CustomerContactMethod.Sms, logs[0].ContactMethod);
        Assert.Equal(CustomerContactMethod.PhoneCall, logs[1].ContactMethod);
        Assert.True(logs[0].ContactedAtUtc >= logs[1].ContactedAtUtc);
    }

    // ==============================================================
    // 4. Address Update & Carrier Depot Redirection Tests
    // ==============================================================

    [Fact]
    public async Task UpdateAddress_ValidAddress_TransitionsToUnderReviewAndAppendsTracking()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var request = new UpdateDeliveryAddressRequest
        {
            ExceptionId = exc.Id,
            CorrectedAddress = "742 Evergreen Terrace, Suite 200, Springfield, OR 97477",
            Notes = "Suite number was missing on original label",
            UserId = fix.StaffUserId
        };

        var result = await service.UpdateAddressAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, result.Status);

        var updated = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updated);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, updated.Status);
        Assert.Equal("742 Evergreen Terrace, Suite 200, Springfield, OR 97477", updated.CorrectedAddress);
        Assert.Contains("Suite number was missing on original label", updated.ResolutionNotes);

        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "ADDRESS_CORRECTED");
        Assert.NotNull(trackingEvent);
        Assert.Contains("742 Evergreen Terrace", trackingEvent.CarrierDescription);
    }

    [Fact]
    public async Task RedirectToDepot_ValidDepot_TransitionsToUnderReviewAndAppendsTracking()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var request = new RedirectToCarrierDepotRequest
        {
            ExceptionId = exc.Id,
            DepotLocation = "FedEx Ship Center - 8500 DFW Airport Rd, Irving, TX 75261",
            Notes = "Customer will collect in person with government ID",
            UserId = fix.StaffUserId
        };

        var result = await service.RedirectToDepotAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, result.Status);

        var updated = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updated);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, updated.Status);
        Assert.Equal("FedEx Ship Center - 8500 DFW Airport Rd, Irving, TX 75261", updated.HoldDepotLocation);
        Assert.Contains("Customer will collect in person", updated.ResolutionNotes);

        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "HOLD_AT_DEPOT");
        Assert.NotNull(trackingEvent);
        Assert.Contains("FedEx Ship Center", trackingEvent.CarrierDescription);
    }

    // ==============================================================
    // 5. Exception Resolution & Legitimate Tracking Recovery Tests
    // ==============================================================

    [Fact]
    public async Task ResolveException_SingleException_RecoversPackageToInTransitAndRollsUpOrder()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // Pre-condition: Package is DeliveryException, Order delivery status is DeliveryException, Order status is Completed
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, pkg.CurrentTrackingStatus);
        Assert.Equal(OrderDeliveryStatus.DeliveryException, order.DeliveryStatus);
        Assert.Equal(OutboundOrderStatus.Completed, order.Status);

        var request = new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "Driver contacted customer and delivery was rescheduled and completed",
            ResolvedByUserId = fix.StaffUserId
        };

        var result = await service.ResolveExceptionAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.Resolved, result.Status);

        // Verify entity updates
        var updated = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsResolved);
        Assert.Equal(DeliveryExceptionStatus.Resolved, updated.Status);
        Assert.NotNull(updated.ResolvedAtUtc);
        Assert.Equal(fix.StaffUserId, updated.ResolvedByUserId);
        Assert.Contains("Driver contacted customer and delivery was rescheduled", updated.ResolutionNotes);

        // Verify package recovered to InTransit
        var updatedPkg = await context.Packages.FindAsync(pkg.Id);
        Assert.NotNull(updatedPkg);
        Assert.Equal(TrackingMilestoneStatus.InTransit, updatedPkg.CurrentTrackingStatus);

        // Verify shipment and order delivery status recovered
        var updatedShipment = await context.Shipments.FindAsync(shipment.Id);
        Assert.NotNull(updatedShipment);
        Assert.Equal(TrackingMilestoneStatus.InTransit, updatedShipment.DeliveryStatus);

        var updatedOrder = await context.OutboundOrders.FindAsync(order.Id);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderDeliveryStatus.InTransit, updatedOrder.DeliveryStatus);
        // INVARIANT: OutboundOrder.Status remains Completed!
        Assert.Equal(OutboundOrderStatus.Completed, updatedOrder.Status);

        // Verify tracking event
        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "EXCEPTION_RESOLVED");
        Assert.NotNull(trackingEvent);
        Assert.Equal(TrackingMilestoneStatus.InTransit, trackingEvent.MilestoneStatus);
    }

    [Fact]
    public async Task ResolveException_MultipleExceptionsOnPackage_PackageRemainsInExceptionUntilAllResolved()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc1) = await CreateDispatchedPackageWithExceptionAsync(fix);

        // Add a second concurrent exception on the same package
        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var exc2 = new ShipmentDeliveryException
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg.Id,
                ShipmentId = shipment.Id,
                OrderId = order.Id,
                TrackingNumber = pkg.TrackingNumber,
                CarrierCode = "SIM-FEDEX",
                ExceptionType = DeliveryExceptionType.WeatherOrNaturalDisaster,
                Status = DeliveryExceptionStatus.Open,
                Severity = DeliveryExceptionSeverity.Medium,
                Source = DeliveryExceptionSource.CarrierWebhook,
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-20),
                ReattemptCount = 0,
                ContactAttemptsCount = 0,
                EventFingerprint = $"FP-RES-2-{Guid.NewGuid():N}"
            };
            seedContext.ShipmentDeliveryExceptions.Add(exc2);
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // Resolve first exception
        await service.ResolveExceptionAsync(new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc1.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "First exception cleared"
        });

        // Package must REMAIN in DeliveryException because exc2 is still unresolved!
        var pkgAfterFirst = await context.Packages.FindAsync(pkg.Id);
        Assert.NotNull(pkgAfterFirst);
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, pkgAfterFirst.CurrentTrackingStatus);

        // Find exc2 id
        var exc2Entity = await context.ShipmentDeliveryExceptions.FirstAsync(e => e.Id != exc1.Id);

        // Resolve second exception
        await service.ResolveExceptionAsync(new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc2Entity.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "Second exception cleared - weather cleared"
        });

        // Now package recovers to InTransit
        var pkgAfterSecond = await context.Packages.FindAsync(pkg.Id);
        Assert.NotNull(pkgAfterSecond);
        Assert.Equal(TrackingMilestoneStatus.InTransit, pkgAfterSecond.CurrentTrackingStatus);
    }

    [Fact]
    public async Task ResolveException_EmptyResolutionNotes_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        await Assert.ThrowsAsync<ValidationException>(() => service.ResolveExceptionAsync(new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "   "
        }));
    }

    [Fact]
    public async Task ResolveException_AlreadyResolved_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // Resolve once
        await service.ResolveExceptionAsync(new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "Initial resolution notes"
        });

        // Resolve again -> must fail
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.ResolveExceptionAsync(new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "Already cleared"
        }));
    }

    // ==============================================================
    // 6. Exception Reopening & Voiding Tests
    // ==============================================================

    [Fact]
    public async Task ReopenException_ResolvedException_ResetsStatusToOpenAndPackageToDeliveryException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // 1. Resolve exception first
        await service.ResolveExceptionAsync(new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "Resolved initially"
        });

        // Verify package was in InTransit
        var pkgResolved = await context.Packages.FindAsync(pkg.Id);
        Assert.Equal(TrackingMilestoneStatus.InTransit, pkgResolved!.CurrentTrackingStatus);

        // 2. Reopen exception
        var reopenResult = await service.ReopenExceptionAsync(new ReopenDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Reason = "Customer called back; driver failed to deliver at gate",
            UserId = fix.StaffUserId
        });

        Assert.True(reopenResult.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.Open, reopenResult.Status);

        var updated = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsResolved);
        Assert.Null(updated.ResolvedAtUtc);
        Assert.Equal(DeliveryExceptionStatus.Open, updated.Status);
        Assert.Contains("Customer called back", updated.ResolutionNotes);

        // Verify package rolled back to DeliveryException
        var updatedPkg = await context.Packages.FindAsync(pkg.Id);
        Assert.NotNull(updatedPkg);
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, updatedPkg.CurrentTrackingStatus);

        // Verify order rolls up to DeliveryException
        var updatedOrder = await context.OutboundOrders.FindAsync(order.Id);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderDeliveryStatus.DeliveryException, updatedOrder.DeliveryStatus);
        // Invariant: Status remains Completed
        Assert.Equal(OutboundOrderStatus.Completed, updatedOrder.Status);

        // Verify tracking event
        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "EXCEPTION_REOPENED");
        Assert.NotNull(trackingEvent);
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, trackingEvent.MilestoneStatus);
    }

    [Fact]
    public async Task ReopenException_NotResolved_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // Attempt to reopen an exception that is already Open
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.ReopenExceptionAsync(new ReopenDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Reason = "Reopen already open"
        }));
    }

    [Fact]
    public async Task VoidException_OpenException_SetsVoidedRecoversPackageAndAppendsTracking()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var request = new VoidDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Reason = "Carrier reported false positive scan code",
            UserId = fix.StaffUserId
        };

        var result = await service.VoidExceptionAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.Voided, result.Status);

        var updated = await context.ShipmentDeliveryExceptions.FindAsync(exc.Id);
        Assert.NotNull(updated);
        Assert.Equal(DeliveryExceptionStatus.Voided, updated.Status);
        Assert.Contains("Carrier reported false positive scan code", updated.ResolutionNotes);

        // Package recovers to InTransit because no other exceptions exist
        var updatedPkg = await context.Packages.FindAsync(pkg.Id);
        Assert.NotNull(updatedPkg);
        Assert.Equal(TrackingMilestoneStatus.InTransit, updatedPkg.CurrentTrackingStatus);

        // Tracking event appended
        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(t => t.PackageId == pkg.Id && t.CarrierEventCode == "EXCEPTION_VOIDED");
        Assert.NotNull(trackingEvent);
        Assert.Equal(TrackingMilestoneStatus.InTransit, trackingEvent.MilestoneStatus);
    }

    [Fact]
    public async Task VoidException_ResolvedException_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // Resolve first
        await service.ResolveExceptionAsync(new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "Notes"
        });

        // Voiding resolved exception must fail
        await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.VoidExceptionAsync(new VoidDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Reason = "Try voiding resolved exception"
        }));
    }

    // ==============================================================
    // 7. CQRS Commands End-to-End Tests
    // ==============================================================

    [Fact]
    public async Task CQRS_ResolveDeliveryExceptionCommandHandler_ExecutesSuccessfully()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var request = new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "Verified by dispatcher"
        };
        var command = new ResolveDeliveryExceptionCommand(request);
        var handler = new ResolveDeliveryExceptionCommandHandler(service);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.Resolved, result.Status);
    }

    [Fact]
    public async Task CQRS_ScheduleDeliveryReattemptCommandHandler_ExecutesSuccessfully()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var request = new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            ScheduledDateUtc = DateTime.UtcNow.AddDays(2),
            Notes = "Reattempt via CQRS"
        };
        var command = new ScheduleDeliveryReattemptCommand(request);
        var handler = new ScheduleDeliveryReattemptCommandHandler(service);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.ScheduledForReattempt, result.Status);
    }

    [Fact]
    public async Task CQRS_RecordCustomerContactCommandHandler_ExecutesSuccessfully()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var request = new RecordCustomerContactRequest
        {
            ExceptionId = exc.Id,
            ContactMethod = CustomerContactMethod.Email,
            Outcome = CustomerContactOutcome.ReachedAddressCorrected,
            Notes = "Customer replied with unit #304",
            ResolutionAction = "Update unit number on carrier portal"
        };
        var command = new RecordCustomerContactCommand(request);
        var handler = new RecordCustomerContactCommandHandler(service);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var logs = await service.GetContactLogsForExceptionAsync(exc.Id);
        Assert.Single(logs);
        Assert.Equal(CustomerContactMethod.Email, logs[0].ContactMethod);
    }

    [Fact]
    public async Task CQRS_UpdateDeliveryAddressCommandHandler_ExecutesSuccessfully()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var request = new UpdateDeliveryAddressRequest
        {
            ExceptionId = exc.Id,
            CorrectedAddress = "100 Main St, Suite 400",
            Notes = "Updated via CQRS command"
        };
        var command = new UpdateDeliveryAddressCommand(request);
        var handler = new UpdateDeliveryAddressCommandHandler(service);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, result.Status);
    }

    [Fact]
    public async Task CQRS_RedirectToCarrierDepotCommandHandler_ExecutesSuccessfully()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        var request = new RedirectToCarrierDepotRequest
        {
            ExceptionId = exc.Id,
            DepotLocation = "Main Freight Terminal North",
            Notes = "Redirected via CQRS command"
        };
        var command = new RedirectToCarrierDepotCommand(request);
        var handler = new RedirectToCarrierDepotCommandHandler(service);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryExceptionStatus.UnderReview, result.Status);
    }

    // ==============================================================
    // 8. Multi-Tenancy & Facility Security Isolation Tests
    // ==============================================================

    [Fact]
    public async Task TenantIsolation_CrossTenantResolution_ThrowsUnauthorizedException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        // Tenant is different client and filter not bypassed
        var tenantProvider = new TestTenantProvider { TenantId = fix.ForeignClientId, BypassTenantFilter = false };
        var userContext = new TestCurrentUserContext { UserType = UserType.Client, TenantId = fix.ForeignClientId, FacilityId = fix.FacilityId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.ResolveExceptionAsync(new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "Notes"
        }));
    }

    [Fact]
    public async Task FacilityIsolation_CrossFacilityStaffUser_ThrowsForbiddenException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        // User is staff assigned to foreign facility (ATL1) rather than exception facility (DFW1), and NOT superadmin
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.ForeignFacilityId, RolesList = [] };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ScheduleReattemptAsync(new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            ScheduledDateUtc = DateTime.UtcNow.AddDays(1),
            Notes = "Unauthorized facility user"
        }));
    }

    // ==============================================================
    // 9. Warehouse Inventory Invariance Tests (Delta Q_warehouse = 0)
    // ==============================================================

    [Fact]
    public async Task InventoryInvariance_AllResolutionAndReattemptWorkflows_LeaveWarehouseStockUnchanged()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc) = await CreateDispatchedPackageWithExceptionAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var initialStock = await context.StockOnHand.AsNoTracking().FirstOrDefaultAsync(s => s.ItemId == fix.ItemId);
        Assert.NotNull(initialStock);
        var initialQtyAvailable = initialStock.QtyAvailable;
        var initialQtyAllocated = initialStock.QtyAllocated;

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.FacilityId, UserId = fix.StaffUserId };
        var classificationService = new CarrierExceptionClassificationService();
        var service = new DeliveryExceptionService(context, tenantProvider, userContext, classificationService, NullLogger<DeliveryExceptionService>.Instance);

        // 1. Record contact
        await service.RecordCustomerContactAsync(new RecordCustomerContactRequest
        {
            ExceptionId = exc.Id,
            ContactMethod = CustomerContactMethod.PhoneCall,
            Outcome = CustomerContactOutcome.ReachedAgreedToReattempt,
            Notes = "Contact made"
        });

        // 2. Schedule reattempt
        await service.ScheduleReattemptAsync(new ScheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            ScheduledDateUtc = DateTime.UtcNow.AddDays(1),
            Notes = "Reattempt"
        });

        // 3. Reschedule reattempt
        await service.RescheduleReattemptAsync(new RescheduleDeliveryReattemptRequest
        {
            ExceptionId = exc.Id,
            NewScheduledDateUtc = DateTime.UtcNow.AddDays(2),
            Reason = "Reschedule"
        });

        // 4. Update address
        await service.UpdateAddressAsync(new UpdateDeliveryAddressRequest
        {
            ExceptionId = exc.Id,
            CorrectedAddress = "Updated Street 123",
            Notes = "Address change"
        });

        // 5. Resolve exception
        await service.ResolveExceptionAsync(new ResolveDeliveryExceptionRequest
        {
            ExceptionId = exc.Id,
            Resolution = DeliveryExceptionResolution.Resolved,
            ResolutionNotes = "Complete"
        });

        // Invariant Assertion: Inventory stock on hand must remain perfectly identical: Delta Q_warehouse == 0
        var finalStock = await context.StockOnHand.AsNoTracking().FirstOrDefaultAsync(s => s.ItemId == fix.ItemId);
        Assert.NotNull(finalStock);
        Assert.Equal(initialQtyAvailable, finalStock.QtyAvailable);
        Assert.Equal(initialQtyAllocated, finalStock.QtyAllocated);
    }
}
