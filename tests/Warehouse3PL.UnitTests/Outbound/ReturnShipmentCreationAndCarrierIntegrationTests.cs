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

public class ReturnShipmentCreationAndCarrierIntegrationTests
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
            CompanyName = "Global Industrial Supplies",
            AccountCode = $"GIS{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Foreign Trade Co",
            AccountCode = $"FTC{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
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
            Name = "Lexington Returns Processing Facility",
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
            Name = "Atlanta Hub",
            FacilityCode = "ATL1",
            AddressLine1 = "900 Peachtree St",
            City = "Atlanta",
            StateOrProvince = "GA",
            PostalCode = "30301",
            CountryCode = "US"
        };

        var staffUser = new User
        {
            Id = fix.StaffUserId,
            Email = "returns.operator@warehouse3pl.test",
            PasswordHash = "hash",
            FirstName = "Alice",
            LastName = "Operator",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = $"SKU-RTS-CARRIER-{Guid.NewGuid().ToString()[..6]}",
            Description = "Precision Stepper Motor",
            WeightKg = 2.5m,
            LengthCm = 15m,
            WidthCm = 15m,
            HeightCm = 10m
        };

        var loc = new Location
        {
            Id = Guid.NewGuid(),
            FacilityId = fix.OriginFacilityId,
            LocationCode = "BIN-RTS-SHP-01",
            Barcode = "BIN-RTS-SHP-01",
            LocationType = LocationType.Bin
        };

        var stock = new StockOnHand
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            LocationId = loc.Id,
            ItemId = item.Id,
            QtyAvailable = 500m,
            QtyAllocated = 50m
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

    private static async Task<(OutboundOrder Order, Shipment Shipment, Package Package, ShipmentDeliveryException Exception, ReturnAuthorization Auth)> CreateAuthorizedReturnAsync(
        TestEnvFixture fix,
        string? holdDepotLocation = null,
        string? correctedAddress = null)
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
            OrderNumber = $"ORD-RET-{Guid.NewGuid().ToString()[..6]}",
            Status = OutboundOrderStatus.Completed,
            DeliveryStatus = OrderDeliveryStatus.DeliveryException
        };
        context.OutboundOrders.Add(order);

        var shippingAddress = new ShippingAddress
        {
            Id = Guid.NewGuid(),
            OutboundOrderId = orderId,
            RecipientName = "John Doe Customer",
            CompanyName = "Acme Corp",
            AddressLine1 = "789 Delivery Lane",
            City = "Louisville",
            StateOrProvince = "KY",
            PostalCode = "40204",
            CountryCode = "US",
            PhoneNumber = "502-555-0199"
        };
        context.ShippingAddresses.Add(shippingAddress);
        order.SetShippingAddress(shippingAddress);

        var shipmentId = Guid.NewGuid();
        var shipment = new Shipment
        {
            Id = shipmentId,
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            ShipmentNumber = $"SHP-RET-{Guid.NewGuid().ToString()[..6]}",
            CarrierCode = "SIM-FEDEX",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.DeliveryException,
            DispatchedAtUtc = DateTime.UtcNow.AddHours(-6)
        };
        context.Shipments.Add(shipment);

        var pkg = new Package
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            OutboundOrderId = orderId,
            ShipmentId = shipmentId,
            PackageNumber = $"PKG-RET-{Guid.NewGuid().ToString()[..6]}",
            TrackingNumber = $"TRK-ORIG-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            CarrierCode = "SIM-FEDEX",
            Status = PackageStatus.Loaded,
            CurrentTrackingStatus = TrackingMilestoneStatus.DeliveryException,
            TrackingStatusUpdatedAtUtc = DateTime.UtcNow.AddHours(-1)
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
            CarrierExceptionCode = "REFUSED_BY_RECIPIENT",
            ExceptionType = DeliveryExceptionType.CustomerRefused,
            Severity = DeliveryExceptionSeverity.High,
            CarrierDescription = "Recipient refused package delivery.",
            HoldDepotLocation = holdDepotLocation,
            CorrectedAddress = correctedAddress,
            ReattemptCount = 1,
            OccurredAtUtc = DateTime.UtcNow.AddHours(-2),
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
            notes: "Customer refused package. Authorized return to Lexington hub.",
            authorizedByUserId: fix.StaffUserId,
            rmaNumber: "RMA-GIS-999",
            expiresAtUtc: DateTime.UtcNow.AddDays(14));

        context.ReturnAuthorizations.Add(auth);
        exc.AuthorizeReturnToSender(auth.Id, null, "Authorized return to sender.", fix.StaffUserId);

        await context.SaveChangesAsync();
        return (order, shipment, pkg, exc, auth);
    }

    [Fact]
    public async Task CreateReturnShipment_ValidAuthorization_CreatesAndLinksReturnShipmentSuccessfully()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest
        {
            AuthorizationId = auth.Id,
            CarrierCode = "SIM-FEDEX",
            ServiceLevel = CarrierServiceLevel.StandardGround,
            LabelFormat = "ZPL"
        };

        var result = await service.CreateReturnShipmentAsync(request);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsDuplicate);
        Assert.NotEqual(Guid.Empty, result.ReturnShipmentId);
        Assert.Equal(auth.Id, result.ReturnAuthorizationId);
        Assert.StartsWith("SIM-FEDEX-", result.TrackingNumber);
        Assert.Equal("SIM-FEDEX", result.CarrierCode);
        Assert.Equal(CarrierServiceLevel.StandardGround.ToString(), result.ServiceLevel);
        Assert.Equal("ZPL", result.LabelFormat);
        Assert.NotNull(result.LabelData);
        Assert.StartsWith("^XA", result.LabelData);
        Assert.NotNull(result.LabelObjectKey);
        Assert.NotNull(result.RateCharged);
        Assert.True(result.RateCharged > 0);
        Assert.Equal(ReturnShipmentStatus.Authorized, result.Status);

        // Verify Database State
        var persistedShipment = await context.ReturnShipments
            .Include(s => s.ReturnAuthorization)
            .FirstOrDefaultAsync(s => s.Id == result.ReturnShipmentId);

        Assert.NotNull(persistedShipment);
        Assert.Equal(fix.ClientId, persistedShipment.ClientId);
        Assert.Equal(fix.OriginFacilityId, persistedShipment.OriginFacilityId);
        Assert.Equal(fix.DestinationFacilityId, persistedShipment.DestinationFacilityId);
        Assert.Equal(auth.Id, persistedShipment.ReturnAuthorizationId);
        Assert.Equal(pkg.Id, persistedShipment.PackageId);
        Assert.Equal(exc.Id, persistedShipment.DeliveryExceptionId);
        Assert.Equal(result.TrackingNumber, persistedShipment.TrackingNumber);

        // Verify Authorization transitioned to ShipmentCreated
        var reloadedAuth = await context.ReturnAuthorizations.FindAsync(auth.Id);
        Assert.NotNull(reloadedAuth);
        Assert.Equal(ReturnAuthorizationStatus.ShipmentCreated, reloadedAuth.Status);
        Assert.Equal(persistedShipment.Id, reloadedAuth.ReturnShipmentId);

        // Verify Tracking Event Appended
        var trackingEvent = await context.ShipmentTrackingEvents
            .FirstOrDefaultAsync(e => e.TrackingNumber == result.TrackingNumber);
        Assert.NotNull(trackingEvent);
        Assert.Equal("RETURN_SHIPMENT_CREATED", trackingEvent.CarrierEventCode);
        Assert.Equal(TrackingMilestoneStatus.DeliveryException, trackingEvent.MilestoneStatus);
    }

    [Fact]
    public async Task CreateReturnShipment_IdempotentDuplicateCall_ReturnsExistingWithoutRebooking()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest
        {
            AuthorizationId = auth.Id,
            CarrierCode = "SIM-FEDEX",
            ServiceLevel = CarrierServiceLevel.StandardGround
        };

        var firstResult = await service.CreateReturnShipmentAsync(request);
        Assert.True(firstResult.IsSuccess);
        Assert.False(firstResult.IsDuplicate);

        // Second call with same authorization
        var secondResult = await service.CreateReturnShipmentAsync(request);
        Assert.True(secondResult.IsSuccess);
        Assert.True(secondResult.IsDuplicate);
        Assert.Equal(firstResult.ReturnShipmentId, secondResult.ReturnShipmentId);
        Assert.Equal(firstResult.TrackingNumber, secondResult.TrackingNumber);

        // Verify only 1 return shipment in DB
        var totalShipments = await context.ReturnShipments.CountAsync(s => s.ReturnAuthorizationId == auth.Id);
        Assert.Equal(1, totalShipments);
    }

    [Fact]
    public async Task CreateReturnShipment_CancelledAuthorization_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var authToCancel = await context.ReturnAuthorizations.FindAsync(auth.Id);
        authToCancel!.Cancel("Customer changed mind, cancelling return.", fix.StaffUserId);
        await context.SaveChangesAsync();

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest { AuthorizationId = auth.Id };

        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.CreateReturnShipmentAsync(request));
        Assert.Contains("cancelled return authorization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateReturnShipment_ExpiredAuthorization_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var authToExpire = await context.ReturnAuthorizations.FindAsync(auth.Id);
        authToExpire!.Expire();
        await context.SaveChangesAsync();

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest { AuthorizationId = auth.Id };

        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.CreateReturnShipmentAsync(request));
        Assert.Contains("expired return authorization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateReturnShipment_CarrierCustodyAlreadyCommenced_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var authInProgress = await context.ReturnAuthorizations.FindAsync(auth.Id);
        // Simulate authorization already in transit
        authInProgress!.Status = ReturnAuthorizationStatus.InTransit;
        await context.SaveChangesAsync();

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest { AuthorizationId = auth.Id };

        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.CreateReturnShipmentAsync(request));
        Assert.Contains("custody has already commenced", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateReturnShipment_ExistingShipmentAlreadyHandedOverToCarrier_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest { AuthorizationId = auth.Id };
        var firstResult = await service.CreateReturnShipmentAsync(request);

        // Simulate carrier handover
        var shipmentInDb = await context.ReturnShipments.FindAsync(firstResult.ReturnShipmentId);
        shipmentInDb!.CarrierHandoverAtUtc = DateTime.UtcNow;
        shipmentInDb.Status = ReturnShipmentStatus.InTransitToWarehouse;
        await context.SaveChangesAsync();

        // Attempt second creation
        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() => service.CreateReturnShipmentAsync(request));
        Assert.Contains("carrier has already accepted custody", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateReturnShipment_SimulatedCarrierFailure_ThrowsAndLeavesAuthorizationInAuthorizedStatus()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest
        {
            AuthorizationId = auth.Id,
            SimulateCarrierFailure = true
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateReturnShipmentAsync(request));
        Assert.Contains("Carrier failed", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify Authorization remains untouched in Authorized status
        var reloadedAuth = await context.ReturnAuthorizations.FindAsync(auth.Id);
        Assert.NotNull(reloadedAuth);
        Assert.Equal(ReturnAuthorizationStatus.Authorized, reloadedAuth.Status);
        Assert.Null(reloadedAuth.ReturnShipmentId);

        // Verify no return shipment created in DB
        var totalShipments = await context.ReturnShipments.CountAsync(s => s.ReturnAuthorizationId == auth.Id);
        Assert.Equal(0, totalShipments);
    }

    [Fact]
    public async Task CreateReturnShipment_AddressResolution_ConsigneeShippingAddress_UsedWhenNoOverrideOrHoldDepot()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest { AuthorizationId = auth.Id };
        var result = await service.CreateReturnShipmentAsync(request);

        var returnShipment = await context.ReturnShipments.FindAsync(result.ReturnShipmentId);
        Assert.NotNull(returnShipment);
        Assert.Equal("789 Delivery Lane", returnShipment.OriginAddressLine1);
        Assert.Equal("Louisville", returnShipment.OriginCity);
        Assert.Equal("40204", returnShipment.OriginPostalCode);
    }

    [Fact]
    public async Task CreateReturnShipment_AddressResolution_HoldDepotAddress_UsedWhenHoldDepotPopulated()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix, holdDepotLocation: "400 Carrier Hold Terminal, Dock 3");

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest { AuthorizationId = auth.Id };
        var result = await service.CreateReturnShipmentAsync(request);

        var returnShipment = await context.ReturnShipments.FindAsync(result.ReturnShipmentId);
        Assert.NotNull(returnShipment);
        Assert.Equal("400 Carrier Hold Terminal, Dock 3", returnShipment.OriginAddressLine1);
    }

    [Fact]
    public async Task CreateReturnShipment_AddressResolution_CorrectedAddress_OverridesHoldDepot()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(
            fix,
            holdDepotLocation: "400 Carrier Hold Terminal, Dock 3",
            correctedAddress: "888 Corrected Pickup St");

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest { AuthorizationId = auth.Id };
        var result = await service.CreateReturnShipmentAsync(request);

        var returnShipment = await context.ReturnShipments.FindAsync(result.ReturnShipmentId);
        Assert.NotNull(returnShipment);
        Assert.Equal("888 Corrected Pickup St", returnShipment.OriginAddressLine1);
    }

    [Fact]
    public async Task CreateReturnShipment_AddressResolution_ExplicitOriginOverride_TakesPrecedence()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(
            fix,
            holdDepotLocation: "400 Carrier Hold Terminal, Dock 3",
            correctedAddress: "888 Corrected Pickup St");

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var overrideAddr = new CarrierAddress
        {
            Name = "Customer VIP Office",
            StreetLine1 = "999 Executive Suite Blvd",
            City = "Frankfort",
            StateOrProvince = "KY",
            PostalCode = "40601",
            CountryCode = "US"
        };

        var request = new CreateReturnShipmentRequest
        {
            AuthorizationId = auth.Id,
            OriginAddressOverride = overrideAddr
        };
        var result = await service.CreateReturnShipmentAsync(request);

        var returnShipment = await context.ReturnShipments.FindAsync(result.ReturnShipmentId);
        Assert.NotNull(returnShipment);
        Assert.Equal("999 Executive Suite Blvd", returnShipment.OriginAddressLine1);
        Assert.Equal("Frankfort", returnShipment.OriginCity);
        Assert.Equal("40601", returnShipment.OriginPostalCode);
    }

    [Fact]
    public async Task CreateReturnShipment_IdenticalOriginAndDestinationAddresses_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        // Destination facility is Lexington with "500 Reverse Logistics Pkwy" and "40509"
        var identicalOrigin = new CarrierAddress
        {
            Name = "Same Place",
            StreetLine1 = "500 Reverse Logistics Pkwy",
            City = "Lexington",
            StateOrProvince = "KY",
            PostalCode = "40509",
            CountryCode = "US"
        };

        var request = new CreateReturnShipmentRequest
        {
            AuthorizationId = auth.Id,
            OriginAddressOverride = identicalOrigin
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateReturnShipmentAsync(request));
        Assert.Contains("identical", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateReturnShipment_CrossTenantAttempt_ThrowsUnauthorizedException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest
        {
            AuthorizationId = auth.Id,
            TenantIdOverride = fix.ForeignClientId // Attempting to bind to foreign client
        };

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => service.CreateReturnShipmentAsync(request));
        Assert.Contains("different client tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateReturnShipment_UserFacilityAccessDenied_ThrowsForbiddenException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        // User restricted to foreign facility (neither origin nor destination)
        var userContext = new TestCurrentUserContext
        {
            UserType = UserType.Staff,
            TenantId = fix.ClientId,
            FacilityId = fix.ForeignFacilityId,
            UserId = fix.StaffUserId
        };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest { AuthorizationId = auth.Id };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateReturnShipmentAsync(request));
        Assert.Contains("not authorized for return facility", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateReturnShipment_WarehouseInventoryInvariance_StockUnchanged()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var stockBefore = await context.StockOnHand.AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.ItemId == fix.ItemId);
        Assert.NotNull(stockBefore);
        var initialQtyAvailable = stockBefore.QtyAvailable;
        var initialQtyAllocated = stockBefore.QtyAllocated;

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest { AuthorizationId = auth.Id };
        var result = await service.CreateReturnShipmentAsync(request);
        Assert.True(result.IsSuccess);

        // Verify stock is strictly invariant (Delta Q = 0)
        var stockAfter = await context.StockOnHand.AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.ItemId == fix.ItemId);
        Assert.NotNull(stockAfter);
        Assert.Equal(initialQtyAvailable, stockAfter.QtyAvailable);
        Assert.Equal(initialQtyAllocated, stockAfter.QtyAllocated);

        // Verify Outbound Order remains Completed
        var orderAfter = await context.OutboundOrders.FindAsync(order.Id);
        Assert.NotNull(orderAfter);
        Assert.Equal(OutboundOrderStatus.Completed, orderAfter.Status);
    }

    [Fact]
    public async Task CreateReturnShipment_MediatRHandler_ExecutesSuccessfully()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var handler = new CreateReturnShipmentCommandHandler(service);
        var request = new CreateReturnShipmentRequest
        {
            AuthorizationId = auth.Id,
            CarrierCode = "SIM-FEDEX",
            ServiceLevel = CarrierServiceLevel.StandardGround,
            LabelFormat = "PDF"
        };
        var command = new CreateReturnShipmentCommand(request);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.TrackingNumber);
        Assert.Equal("PDF", result.LabelFormat);
        Assert.NotNull(result.LabelData);
        Assert.Contains("JVBERi", result.LabelData); // Base64 encoded %PDF
    }

    [Fact]
    public async Task CancelAuthorization_WithExistingReturnShipment_VoidsCarrierLabelAndAudits()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var shipmentService = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        // 1. Create return shipment
        var createResult = await shipmentService.CreateReturnShipmentAsync(new CreateReturnShipmentRequest { AuthorizationId = auth.Id });
        Assert.True(createResult.IsSuccess);

        // 2. Cancel authorization via ReturnAuthorizationService
        var authService = new ReturnAuthorizationService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnAuthorizationService>.Instance);
        var cancelResult = await authService.CancelAuthorizationAsync(new CancelReturnAuthorizationRequest
        {
            AuthorizationId = auth.Id,
            Reason = "Customer agreed to accept package on reattempt."
        });

        Assert.True(cancelResult.IsSuccess);
        Assert.Equal(ReturnAuthorizationStatus.Cancelled, cancelResult.Status);

        // 3. Verify ReturnShipment was cancelled and carrier cancellation was confirmed and audited
        var reloadedShipment = await context.ReturnShipments.FindAsync(createResult.ReturnShipmentId);
        Assert.NotNull(reloadedShipment);
        Assert.Equal(ReturnShipmentStatus.Cancelled, reloadedShipment.Status);
        Assert.True(reloadedShipment.CarrierCancellationConfirmed);
        Assert.NotNull(reloadedShipment.CarrierCancellationNotes);
        Assert.Contains("Carrier confirmed void", reloadedShipment.CarrierCancellationNotes);
    }

    [Fact]
    public async Task ExpireAuthorization_WithExistingReturnShipment_VoidsCarrierLabelAndCancelsShipment()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var shipmentService = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        // 1. Create return shipment
        var createResult = await shipmentService.CreateReturnShipmentAsync(new CreateReturnShipmentRequest { AuthorizationId = auth.Id });
        Assert.True(createResult.IsSuccess);

        // 2. Manipulate authorization expiry date to simulate expired window
        var authInDb = await context.ReturnAuthorizations.FindAsync(auth.Id);
        authInDb!.ExpiresAtUtc = DateTime.UtcNow.AddHours(-2);
        await context.SaveChangesAsync();

        // 3. Expire authorization via ReturnAuthorizationService
        var authService = new ReturnAuthorizationService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnAuthorizationService>.Instance);
        var expireResult = await authService.ExpireAuthorizationAsync(new ExpireReturnAuthorizationRequest
        {
            AuthorizationId = auth.Id,
            Reason = "14-day return window elapsed with no carrier drop-off."
        });

        Assert.True(expireResult.IsSuccess);
        Assert.Equal(ReturnAuthorizationStatus.Expired, expireResult.Status);

        // 4. Verify ReturnShipment was cancelled and carrier label void confirmed
        var reloadedShipment = await context.ReturnShipments.FindAsync(createResult.ReturnShipmentId);
        Assert.NotNull(reloadedShipment);
        Assert.Equal(ReturnShipmentStatus.Cancelled, reloadedShipment.Status);
        Assert.True(reloadedShipment.CarrierCancellationConfirmed);
    }

    [Fact]
    public async Task GetReturnShipmentById_And_GetReturnShipmentByTrackingNumber_RetrieveAggregate()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, pkg, exc, auth) = await CreateAuthorizedReturnAsync(fix);

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = true };
        var userContext = new TestCurrentUserContext { UserType = UserType.Staff, TenantId = fix.ClientId, FacilityId = fix.OriginFacilityId, UserId = fix.StaffUserId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
        var service = new ReturnShipmentService(context, tenantProvider, userContext, carrierService, NullLogger<ReturnShipmentService>.Instance);

        var request = new CreateReturnShipmentRequest { AuthorizationId = auth.Id };
        var createResult = await service.CreateReturnShipmentAsync(request);

        // Retrieve by ID
        var byId = await service.GetReturnShipmentByIdAsync(createResult.ReturnShipmentId);
        Assert.NotNull(byId);
        Assert.Equal(createResult.ReturnShipmentId, byId.Id);
        Assert.NotNull(byId.Client);
        Assert.NotNull(byId.OriginFacility);
        Assert.NotNull(byId.DestinationFacility);
        Assert.NotNull(byId.Order);
        Assert.NotNull(byId.Package);
        Assert.NotNull(byId.ReturnAuthorization);

        // Retrieve by Tracking Number
        var byTracking = await service.GetReturnShipmentByTrackingNumberAsync(createResult.TrackingNumber, createResult.CarrierCode);
        Assert.NotNull(byTracking);
        Assert.Equal(createResult.ReturnShipmentId, byTracking.Id);
        Assert.NotNull(byTracking.ReturnAuthorization);
    }
}
