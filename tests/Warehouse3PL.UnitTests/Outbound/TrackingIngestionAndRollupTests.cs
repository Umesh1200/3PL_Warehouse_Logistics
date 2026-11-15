using System.Collections.Concurrent;
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

public class TrackingIngestionAndRollupTests
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

    private class MockCarrierService : IShippingCarrierService
    {
        public string CarrierIdentifier => "MOCK-CARRIER";
        public bool ForceFailure { get; set; }
        public CarrierTrackingResponse? CustomTrackingResponse { get; set; }

        public Task<IReadOnlyList<CarrierRateQuote>> GetRatesAsync(CarrierRateRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CarrierRateQuote>>([]);

        public Task<CarrierShipmentResponse> CreateShipmentAsync(CarrierShipmentRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new CarrierShipmentResponse { IsSuccess = true });

        public Task<CarrierVoidResponse> VoidShipmentAsync(CarrierVoidRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new CarrierVoidResponse { IsSuccess = true });

        public Task<CarrierTrackingResponse> TrackShipmentAsync(string trackingNumber, string carrierCode, CancellationToken cancellationToken = default)
        {
            if (ForceFailure)
            {
                return Task.FromResult(new CarrierTrackingResponse
                {
                    IsSuccess = false,
                    TrackingNumber = trackingNumber,
                    CarrierCode = carrierCode,
                    ErrorMessage = "Carrier tracking service unavailable (Timeout)"
                });
            }

            if (CustomTrackingResponse != null)
            {
                return Task.FromResult(CustomTrackingResponse);
            }

            return Task.FromResult(new CarrierTrackingResponse
            {
                IsSuccess = true,
                TrackingNumber = trackingNumber,
                CarrierCode = carrierCode,
                CurrentStatus = "InTransit",
                Events =
                [
                    new CarrierTrackingEvent
                    {
                        TimestampUtc = DateTime.UtcNow.AddHours(-2),
                        Status = "IT",
                        Description = "In transit at regional sort hub",
                        Location = "Chicago, IL"
                    }
                ]
            });
        }
    }

    private async Task<TestEnvFixture> SeedTestEnvironmentAsync(string? dbName = null)
    {
        var fix = new TestEnvFixture();
        if (dbName != null) fix.DbName = dbName;

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        context.Clients.Add(new Client { Id = fix.ClientId, CompanyName = "Apex Logistics Client", AccountCode = "ALC" });
        context.Facilities.Add(new Facility { Id = fix.FacilityId, Name = "Chicago Hub", FacilityCode = "ORD-01" });

        context.Locations.Add(new Location
        {
            Id = fix.LocationId,
            FacilityId = fix.FacilityId,
            LocationCode = "LOC-01-A",
            Barcode = "LOC-01",
            LocationType = LocationType.Bin
        });

        context.Items.Add(new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-TRACK-UNIT-01",
            Description = "Precision Sensor",
            WeightKg = 1.0m,
            LengthCm = 10m,
            WidthCm = 10m,
            HeightCm = 10m
        });

        context.StockOnHand.Add(new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LocationId = fix.LocationId,
            ItemId = fix.ItemId,
            QtyAvailable = 500m,
            QtyAllocated = 50m
        });

        await context.SaveChangesAsync();
        return fix;
    }

    private async Task<(OutboundOrder Order, Shipment Shipment, List<Package> Packages)> CreateDispatchedOrderAsync(
        TestEnvFixture fix,
        int packageCount = 2,
        string carrierCode = "SIM-FEDEX",
        Guid? clientIdOverride = null,
        Guid? facilityIdOverride = null)
    {
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var clientId = clientIdOverride ?? fix.ClientId;
        var facilityId = facilityIdOverride ?? fix.FacilityId;

        var orderId = Guid.NewGuid();
        var order = new OutboundOrder
        {
            Id = orderId,
            ClientId = clientId,
            FacilityId = facilityId,
            OrderNumber = $"ORD-TRK-{Guid.NewGuid().ToString()[..6]}",
            OrderType = OutboundOrderType.EcommerceFulfillment,
            Status = OutboundOrderStatus.Completed, // Dispatched at warehouse dock
            DeliveryStatus = OrderDeliveryStatus.Dispatched,
            CustomerName = "John Customer",
            ShippingAddress = new ShippingAddress
            {
                OutboundOrderId = orderId,
                RecipientName = "John Customer",
                AddressLine1 = "123 Michigan Ave",
                City = "Chicago",
                StateOrProvince = "IL",
                PostalCode = "60611",
                CountryCode = "USA",
                PhoneNumber = "312-555-0100"
            }
        };
        context.OutboundOrders.Add(order);

        var shipmentId = Guid.NewGuid();
        var shipment = new Shipment
        {
            Id = shipmentId,
            ClientId = clientId,
            FacilityId = facilityId,
            ShipmentNumber = $"SHP-TRK-{Guid.NewGuid().ToString()[..6]}",
            CarrierCode = carrierCode,
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            Status = ShipmentStatus.Dispatched,
            DeliveryStatus = TrackingMilestoneStatus.PendingHandover,
            DispatchedAtUtc = DateTime.UtcNow
        };
        context.Shipments.Add(shipment);

        var packages = new List<Package>();
        for (int i = 1; i <= packageCount; i++)
        {
            var huId = Guid.NewGuid();
            var hu = new HandlingUnit
            {
                Id = huId,
                ClientId = clientId,
                FacilityId = facilityId,
                LpnCode = $"HU-CTN-{i}-{Guid.NewGuid().ToString()[..4]}",
                UnitType = HandlingUnitType.ShippingCarton,
                Status = HandlingUnitStatus.InUse,
                CurrentLocationId = fix.LocationId,
                CurrentOutboundOrderId = orderId
            };
            context.HandlingUnits.Add(hu);

            var pkgId = Guid.NewGuid();
            var pkg = new Package
            {
                Id = pkgId,
                ClientId = clientId,
                FacilityId = facilityId,
                OutboundOrderId = orderId,
                ShipmentId = shipmentId,
                HandlingUnitId = huId,
                PackageNumber = $"PKG-TRK-{i}-{Guid.NewGuid().ToString()[..4]}",
                CartonSequence = i,
                GrossWeightKg = 2.5m,
                TareWeightKg = 0.5m,
                NetWeightKg = 2.0m,
                LengthCm = 25m,
                WidthCm = 20m,
                HeightCm = 15m,
                Status = PackageStatus.Loaded,
                CarrierCode = carrierCode,
                CarrierServiceCode = CarrierServiceLevel.StandardGround.ToString(),
                TrackingNumber = $"TRK-{carrierCode}-{Guid.NewGuid().ToString()[..8]}",
                CurrentTrackingStatus = TrackingMilestoneStatus.PendingHandover
            };
            pkg.RecalculateVolumetricWeight();
            context.Packages.Add(pkg);
            packages.Add(pkg);
        }

        shipment.TotalPackagesCount = packages.Count;
        shipment.TotalWeightKg = packages.Sum(p => p.GrossWeightKg);
        shipment.TotalVolumeCm3 = packages.Sum(p => p.LengthCm * p.WidthCm * p.HeightCm);

        await context.SaveChangesAsync();
        return (order, shipment, packages);
    }

    #region 1. Idempotency & Ingestion Tests

    [Fact]
    public async Task IngestTrackingEvent_DuplicateWebhookDelivery_ReturnsIdempotentSuccess()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, packages) = await CreateDispatchedOrderAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new ShipmentTrackingService(context, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);

        var eventTime = DateTime.UtcNow.AddHours(-1);
        var request = new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventCode = "IT",
            CarrierDescription = "In transit through sort hub",
            OccurredAtUtc = eventTime,
            EventCity = "Memphis",
            EventStateOrProvince = "TN"
        };

        // First delivery
        var res1 = await service.IngestTrackingEventAsync(request);
        Assert.True(res1.IsSuccess);
        Assert.False(res1.IsDuplicate);
        Assert.Equal(TrackingMilestoneStatus.InTransit, res1.PackageTrackingStatus);

        // Duplicate delivery (redelivered webhook)
        var res2 = await service.IngestTrackingEventAsync(request);
        Assert.True(res2.IsSuccess);
        Assert.True(res2.IsDuplicate);
        Assert.Contains("Duplicate tracking event ignored", res2.Message);

        // Exactly 1 event stored in database
        var eventCount = await context.ShipmentTrackingEvents.CountAsync(e => e.PackageId == pkg.Id);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task IngestTrackingEvent_PreferCarrierEventId_DeduplicatesAcrossVaryingDescriptions()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, packages) = await CreateDispatchedOrderAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new ShipmentTrackingService(context, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);

        const string carrierEventId = "EVT-FEDEX-UNIQUE-889922";

        var res1 = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventId = carrierEventId,
            CarrierEventCode = "IT",
            CarrierDescription = "Departed FedEx location Memphis TN",
            OccurredAtUtc = DateTime.UtcNow
        });
        Assert.True(res1.IsSuccess);
        Assert.False(res1.IsDuplicate);

        // Redelivery with slightly altered description but same CarrierEventId
        var res2 = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventId = carrierEventId,
            CarrierEventCode = "IT",
            CarrierDescription = "Updated: Departed FedEx location",
            OccurredAtUtc = DateTime.UtcNow
        });
        Assert.True(res2.IsSuccess);
        Assert.True(res2.IsDuplicate);
    }

    [Fact]
    public async Task IngestTrackingEvent_UnknownTrackingNumber_ThrowsEntityNotFoundException()
    {
        var fix = await SeedTestEnvironmentAsync();
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new ShipmentTrackingService(context, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.IngestTrackingEventAsync(new IngestTrackingEventRequest
            {
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = "TRK-NONEXISTENT-9999",
                CarrierEventCode = "IT",
                CarrierDescription = "In transit",
                OccurredAtUtc = DateTime.UtcNow
            }));
    }

    [Fact]
    public async Task IngestTrackingEvent_CrossTenantAccess_ThrowsValidationException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, packages) = await CreateDispatchedOrderAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new ShipmentTrackingService(context, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);

        var differentTenantId = Guid.NewGuid();

        // Enforce tenant override mismatch
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.IngestTrackingEventAsync(new IngestTrackingEventRequest
            {
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = pkg.TrackingNumber!,
                CarrierEventCode = "IT",
                CarrierDescription = "In transit",
                OccurredAtUtc = DateTime.UtcNow,
                TenantIdOverride = differentTenantId
            }));

        Assert.Contains("belongs to a different client tenant", ex.Message);
    }

    #endregion

    #region 2. Monotonic State Machine & Milestone Progression

    [Fact]
    public async Task IngestTrackingEvent_OutOfOrderTrackingEvents_PreservesEventTimelineWithoutRegressingState()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, packages) = await CreateDispatchedOrderAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new ShipmentTrackingService(context, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);

        var deliveredTime = DateTime.UtcNow;

        // 1. Ingest Delivered first (e.g. webhook for delivery arrived promptly)
        var delivRes = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventCode = "DL",
            CarrierDescription = "Delivered to front porch",
            OccurredAtUtc = deliveredTime
        });
        Assert.Equal(TrackingMilestoneStatus.Delivered, delivRes.PackageTrackingStatus);

        // 2. An older OutForDelivery event arrives late
        var outForDeliveryRes = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventCode = "OD",
            CarrierDescription = "On vehicle for delivery",
            OccurredAtUtc = deliveredTime.AddHours(-2)
        });

        // 3. An even older InTransit scan arrives late
        var inTransitRes = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventCode = "IT",
            CarrierDescription = "Arrived at local distribution depot",
            OccurredAtUtc = deliveredTime.AddHours(-4)
        });

        // Current status MUST remain Delivered!
        var pkgInDb = await context.Packages.FindAsync(pkg.Id);
        Assert.Equal(TrackingMilestoneStatus.Delivered, pkgInDb!.CurrentTrackingStatus);
        Assert.Equal(deliveredTime, pkgInDb.DeliveredAtUtc);

        // All 3 events preserved in chronological order
        var events = await context.ShipmentTrackingEvents
            .Where(e => e.PackageId == pkg.Id)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(3, events.Count);
        Assert.Equal(TrackingMilestoneStatus.InTransit, events[0].MilestoneStatus);
        Assert.Equal(TrackingMilestoneStatus.OutForDelivery, events[1].MilestoneStatus);
        Assert.Equal(TrackingMilestoneStatus.Delivered, events[2].MilestoneStatus);
    }

    [Fact]
    public async Task IngestTrackingEvent_ValidSkippedMilestones_DirectlyAdvancesToDelivered()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, packages) = await CreateDispatchedOrderAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new ShipmentTrackingService(context, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);

        // Package starts at PendingHandover
        Assert.Equal(TrackingMilestoneStatus.PendingHandover, pkg.CurrentTrackingStatus);

        // Carrier reports Delivered directly without any intermediate InTransit or OutForDelivery scans
        var res = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventCode = "DELIVERED",
            CarrierDescription = "Left at garage",
            OccurredAtUtc = DateTime.UtcNow
        });

        Assert.True(res.IsSuccess);
        Assert.Equal(TrackingMilestoneStatus.Delivered, res.PackageTrackingStatus);

        var pkgInDb = await context.Packages.FindAsync(pkg.Id);
        Assert.Equal(TrackingMilestoneStatus.Delivered, pkgInDb!.CurrentTrackingStatus);
        Assert.NotNull(pkgInDb.DeliveredAtUtc);
    }

    [Fact]
    public async Task IngestTrackingEvent_UnknownCarrierCodeMapping_GracefullyFallsBackToInTransit()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, packages) = await CreateDispatchedOrderAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new ShipmentTrackingService(context, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);

        // Completely unrecognized carrier event code
        var res = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventCode = "UNKNOWN_CODE_XYZ",
            CarrierDescription = "Custom carrier event description",
            OccurredAtUtc = DateTime.UtcNow
        });

        Assert.True(res.IsSuccess);
        // Falls back safely to InTransit without crashing or corrupting data
        Assert.Equal(TrackingMilestoneStatus.InTransit, res.NormalizedMilestone);
    }

    #endregion

    #region 3. Multi-Package Order Delivery Rollup Tests

    [Fact]
    public async Task MultiPackageOrder_RollupLifecycle_AccuratelyComputesStatuses()
    {
        var fix = await SeedTestEnvironmentAsync();
        // Create 1 order fulfilled across 3 packages in 1 shipment
        var (order, shipment, packages) = await CreateDispatchedOrderAsync(fix, 3, "SIM-FEDEX");
        var p1 = packages[0];
        var p2 = packages[1];
        var p3 = packages[2];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new ShipmentTrackingService(context, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);

        // Initial state: Dispatched
        var initialRollup = await service.EvaluateOrderDeliveryRollupAsync(order.Id);
        Assert.Equal(OrderDeliveryStatus.Dispatched, initialRollup.DeliveryStatus);

        // 1. Package 1 is scanned InTransit -> Order becomes InTransit
        var r1 = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = p1.TrackingNumber!,
            CarrierEventCode = "IT",
            CarrierDescription = "Departed Hub",
            OccurredAtUtc = DateTime.UtcNow
        });
        Assert.Equal(OrderDeliveryStatus.InTransit, r1.OrderDeliveryStatus);

        // 2. Package 1 is Delivered (p2 and p3 still pending) -> Order becomes PartiallyDelivered
        var r2 = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = p1.TrackingNumber!,
            CarrierEventCode = "DL",
            CarrierDescription = "Delivered",
            OccurredAtUtc = DateTime.UtcNow
        });
        Assert.Equal(OrderDeliveryStatus.PartiallyDelivered, r2.OrderDeliveryStatus);

        // 3. Package 2 experiences an Exception -> Order becomes DeliveryException
        var r3 = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = p2.TrackingNumber!,
            CarrierEventCode = "EX",
            CarrierDescription = "Delivery delay - gate code required",
            OccurredAtUtc = DateTime.UtcNow
        });
        Assert.Equal(OrderDeliveryStatus.DeliveryException, r3.OrderDeliveryStatus);

        // 4. Package 2 exception resolved and Delivered; Package 3 Delivered -> Order becomes Delivered
        await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = p2.TrackingNumber!,
            CarrierEventCode = "DL",
            CarrierDescription = "Delivered to reception",
            OccurredAtUtc = DateTime.UtcNow
        });

        var finalDeliveryTime = DateTime.UtcNow.AddMinutes(10);
        var rFinal = await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = p3.TrackingNumber!,
            CarrierEventCode = "DL",
            CarrierDescription = "Delivered to reception",
            OccurredAtUtc = finalDeliveryTime
        });

        Assert.Equal(OrderDeliveryStatus.Delivered, rFinal.OrderDeliveryStatus);

        var orderInDb = await context.OutboundOrders.FindAsync(order.Id);
        Assert.Equal(OrderDeliveryStatus.Delivered, orderInDb!.DeliveryStatus);
        Assert.Equal(finalDeliveryTime, orderInDb.DeliveredAtUtc);
        // Warehouse status remained Completed throughout!
        Assert.Equal(OutboundOrderStatus.Completed, orderInDb.Status);
    }

    [Fact]
    public async Task MultiShipmentOrder_RollupAcrossMultipleShipments_EvaluatesCorrectly()
    {
        var fix = await SeedTestEnvironmentAsync();

        // One order with 2 packages split across 2 separate shipments
        var sharedOrderId = Guid.NewGuid();
        var shpAId = Guid.NewGuid();
        var shpBId = Guid.NewGuid();
        var pAId = Guid.NewGuid();
        var pBId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var order = new OutboundOrder
            {
                Id = sharedOrderId,
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OrderNumber = "ORD-MULTI-SHP-01",
                Status = OutboundOrderStatus.Completed,
                DeliveryStatus = OrderDeliveryStatus.Dispatched
            };
            seed.OutboundOrders.Add(order);

            var shpA = new Shipment
            {
                Id = shpAId,
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                ShipmentNumber = "SHP-SPLIT-A",
                CarrierCode = "SIM-FEDEX",
                Status = ShipmentStatus.Dispatched,
                DeliveryStatus = TrackingMilestoneStatus.PendingHandover
            };
            var shpB = new Shipment
            {
                Id = shpBId,
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                ShipmentNumber = "SHP-SPLIT-B",
                CarrierCode = "SIM-UPS",
                Status = ShipmentStatus.Dispatched,
                DeliveryStatus = TrackingMilestoneStatus.PendingHandover
            };
            seed.Shipments.AddRange(shpA, shpB);

            var huA = new HandlingUnit { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, LpnCode = "HU-A", CurrentLocationId = fix.LocationId };
            var huB = new HandlingUnit { Id = Guid.NewGuid(), ClientId = fix.ClientId, FacilityId = fix.FacilityId, LpnCode = "HU-B", CurrentLocationId = fix.LocationId };
            seed.HandlingUnits.AddRange(huA, huB);

            var pA = new Package
            {
                Id = pAId,
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OutboundOrderId = sharedOrderId,
                ShipmentId = shpAId,
                HandlingUnitId = huA.Id,
                PackageNumber = "PKG-SPLIT-A",
                TrackingNumber = "TRK-FEDEX-SPLIT-A",
                CarrierCode = "SIM-FEDEX",
                Status = PackageStatus.Loaded,
                CurrentTrackingStatus = TrackingMilestoneStatus.PendingHandover
            };
            var pB = new Package
            {
                Id = pBId,
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                OutboundOrderId = sharedOrderId,
                ShipmentId = shpBId,
                HandlingUnitId = huB.Id,
                PackageNumber = "PKG-SPLIT-B",
                TrackingNumber = "TRK-UPS-SPLIT-B",
                CarrierCode = "SIM-UPS",
                Status = PackageStatus.Loaded,
                CurrentTrackingStatus = TrackingMilestoneStatus.PendingHandover
            };
            seed.Packages.AddRange(pA, pB);
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
        var service = new ShipmentTrackingService(context, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);

        // Deliver Package A (Shipment A)
        await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = "TRK-FEDEX-SPLIT-A",
            CarrierEventCode = "DL",
            CarrierDescription = "Delivered",
            OccurredAtUtc = DateTime.UtcNow
        });

        // Order is PartiallyDelivered because Package B (Shipment B) is still pending
        var status1 = await service.GetOrderDeliveryStatusAsync(sharedOrderId);
        Assert.NotNull(status1);
        Assert.Equal(OrderDeliveryStatus.PartiallyDelivered, status1.DeliveryStatus);

        // Deliver Package B (Shipment B)
        await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-UPS",
            TrackingNumber = "TRK-UPS-SPLIT-B",
            CarrierEventCode = "DL",
            CarrierDescription = "Delivered",
            OccurredAtUtc = DateTime.UtcNow
        });

        // Now Order is Delivered across both shipments!
        var status2 = await service.GetOrderDeliveryStatusAsync(sharedOrderId);
        Assert.NotNull(status2);
        Assert.Equal(OrderDeliveryStatus.Delivered, status2.DeliveryStatus);
    }

    #endregion

    #region 4. Polling Synchronization & Failure Isolation

    [Fact]
    public async Task SyncShipmentTracking_IngestsCarrierEvents_AndSkipsDuplicates()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, shipment, packages) = await CreateDispatchedOrderAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var mockCarrier = new MockCarrierService
        {
            CustomTrackingResponse = new CarrierTrackingResponse
            {
                IsSuccess = true,
                TrackingNumber = pkg.TrackingNumber!,
                CarrierCode = "SIM-FEDEX",
                CurrentStatus = "InTransit",
                Events =
                [
                    new CarrierTrackingEvent { TimestampUtc = DateTime.UtcNow.AddHours(-3), Status = "PU", Description = "Picked up" },
                    new CarrierTrackingEvent { TimestampUtc = DateTime.UtcNow.AddHours(-1), Status = "IT", Description = "Arrived at sort facility" }
                ]
            }
        };

        var service = new ShipmentTrackingService(context, mockCarrier, NullLogger<ShipmentTrackingService>.Instance);

        // First sync: 2 new events ingested
        var syncResult1 = await service.SyncShipmentTrackingAsync(new SyncShipmentTrackingRequest { ShipmentId = shipment.Id });
        Assert.True(syncResult1.IsSuccess);
        Assert.Equal(1, syncResult1.PackagesEvaluated);
        Assert.Equal(2, syncResult1.NewEventsIngested);
        Assert.Equal(0, syncResult1.DuplicateEventsSkipped);

        // Second sync with identical carrier response: 2 duplicates skipped
        var syncResult2 = await service.SyncShipmentTrackingAsync(new SyncShipmentTrackingRequest { ShipmentId = shipment.Id });
        Assert.True(syncResult2.IsSuccess);
        Assert.Equal(1, syncResult2.PackagesEvaluated);
        Assert.Equal(0, syncResult2.NewEventsIngested);
        Assert.Equal(2, syncResult2.DuplicateEventsSkipped);
    }

    [Fact]
    public async Task SyncShipmentTracking_CarrierFailure_IsolatesErrorAndPreservesLastValidStatus()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, shipment, packages) = await CreateDispatchedOrderAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var mockCarrier = new MockCarrierService();
        var service = new ShipmentTrackingService(context, mockCarrier, NullLogger<ShipmentTrackingService>.Instance);

        // 1. Ingest an InTransit event so package has known valid status
        await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventCode = "IT",
            CarrierDescription = "In transit",
            OccurredAtUtc = DateTime.UtcNow.AddHours(-1)
        });

        var pkgBeforeSync = await context.Packages.FindAsync(pkg.Id);
        Assert.Equal(TrackingMilestoneStatus.InTransit, pkgBeforeSync!.CurrentTrackingStatus);

        // 2. Force carrier network failure on polling sync
        mockCarrier.ForceFailure = true;

        var syncResult = await service.SyncShipmentTrackingAsync(new SyncShipmentTrackingRequest { ShipmentId = shipment.Id });
        Assert.True(syncResult.IsSuccess);
        Assert.Equal(0, syncResult.NewEventsIngested);

        // Status MUST NOT be altered or cleared upon carrier network failure
        var pkgAfterSync = await context.Packages.FindAsync(pkg.Id);
        Assert.Equal(TrackingMilestoneStatus.InTransit, pkgAfterSync!.CurrentTrackingStatus);
    }

    #endregion

    #region 5. Concurrency & Invariant Verification

    [Fact]
    public async Task IngestTrackingEvent_ConcurrentDuplicateWebhooks_HandlesRaceConditionSafely()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, packages) = await CreateDispatchedOrderAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        var eventTime = DateTime.UtcNow.AddHours(-2);
        var request = new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierEventCode = "IT",
            CarrierDescription = "Concurrent webhook test",
            OccurredAtUtc = eventTime
        };

        // Fire 5 concurrent requests simultaneously for the exact same event
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            using var threadContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);
            var service = new ShipmentTrackingService(threadContext, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);
            return await service.IngestTrackingEventAsync(request);
        });

        var results = await Task.WhenAll(tasks);

        // All 5 must succeed without uncaught exceptions
        Assert.All(results, r => Assert.True(r.IsSuccess));

        // At least 4 must be flagged as duplicate
        var duplicates = results.Count(r => r.IsDuplicate);
        Assert.True(duplicates >= 4, $"Expected at least 4 duplicates, found {duplicates}");
    }

    [Fact]
    public async Task ZeroStockBalanceMutation_InvariantVerification_DuringTrackingAndRollup()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, packages) = await CreateDispatchedOrderAsync(fix, 2, "SIM-FEDEX");
        var p1 = packages[0];
        var p2 = packages[1];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        // Capture initial stock state
        var initialStock = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        decimal initialTotal = initialStock.TotalQuantity;
        decimal initialAllocated = initialStock.QtyAllocated;
        decimal initialAvailable = initialStock.QtyAvailable;
        int initialTxCount = await context.InventoryTransactions.CountAsync();

        var service = new ShipmentTrackingService(context, new MockCarrierService(), NullLogger<ShipmentTrackingService>.Instance);

        // Ingest events
        await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = p1.TrackingNumber!,
            CarrierEventCode = "IT",
            CarrierDescription = "In transit",
            OccurredAtUtc = DateTime.UtcNow
        });

        await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = p1.TrackingNumber!,
            CarrierEventCode = "DL",
            CarrierDescription = "Delivered",
            OccurredAtUtc = DateTime.UtcNow
        });

        await service.IngestTrackingEventAsync(new IngestTrackingEventRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = p2.TrackingNumber!,
            CarrierEventCode = "DL",
            CarrierDescription = "Delivered",
            OccurredAtUtc = DateTime.UtcNow
        });

        // Polling sync
        await service.SyncShipmentTrackingAsync(new SyncShipmentTrackingRequest { ShipmentId = shipment.Id });

        // Order rollup
        await service.EvaluateOrderDeliveryRollupAsync(order.Id);

        // Verify that StockOnHand and InventoryTransactions remain strictly untouched (ΔQ = 0)
        var finalStock = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        Assert.Equal(initialTotal, finalStock.TotalQuantity);
        Assert.Equal(initialAllocated, finalStock.QtyAllocated);
        Assert.Equal(initialAvailable, finalStock.QtyAvailable);

        int finalTxCount = await context.InventoryTransactions.CountAsync();
        Assert.Equal(initialTxCount, finalTxCount);
    }

    #endregion
}

