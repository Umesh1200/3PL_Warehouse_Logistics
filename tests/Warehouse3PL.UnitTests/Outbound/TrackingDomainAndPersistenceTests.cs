using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public class TrackingDomainAndPersistenceTests
{
    private class TestTrackingFixture
    {
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; } = Guid.NewGuid();
        public Guid LocationId { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; } = Guid.NewGuid();
        public string DbName { get; set; } = Guid.NewGuid().ToString();
    }

    private async Task<TestTrackingFixture> SeedTestEnvironmentAsync(string? dbName = null)
    {
        var fix = new TestTrackingFixture();
        if (dbName != null) fix.DbName = dbName;

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        context.Clients.Add(new Client
        {
            Id = fix.ClientId,
            CompanyName = "Global Apex Transport Client",
            AccountCode = "GATC"
        });

        context.Facilities.Add(new Facility
        {
            Id = fix.FacilityId,
            Name = "Apex Logistics Hub",
            FacilityCode = "APX-HUB-01"
        });

        context.Locations.Add(new Location
        {
            Id = fix.LocationId,
            FacilityId = fix.FacilityId,
            LocationCode = "AISLE-01-BIN-01",
            Barcode = "LOC-BIN-01",
            LocationType = LocationType.Bin
        });

        context.Items.Add(new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-TRK-01",
            Description = "Apex Tracked Electronics Unit",
            WeightKg = 1.5m,
            LengthCm = 20m,
            WidthCm = 15m,
            HeightCm = 10m
        });

        // Add physical stock on hand to verify zero mutation invariant
        context.StockOnHand.Add(new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LocationId = fix.LocationId,
            ItemId = fix.ItemId,
            QtyAvailable = 100m,
            QtyAllocated = 20m
        });

        await context.SaveChangesAsync();
        return fix;
    }

    private async Task<(OutboundOrder Order, Shipment Shipment, List<Package> Packages)> CreateDispatchedOrderAndShipmentAsync(
        TestTrackingFixture fix,
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
            CustomerName = "Jane Consignee",
            ShippingAddress = new ShippingAddress
            {
                OutboundOrderId = orderId,
                RecipientName = "Jane Consignee",
                AddressLine1 = "100 Maple St",
                City = "Chicago",
                StateOrProvince = "IL",
                PostalCode = "60601",
                CountryCode = "USA",
                PhoneNumber = "312-555-0199"
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
                LpnCode = $"HU-TRK-{i}-{Guid.NewGuid().ToString()[..4]}",
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
                GrossWeightKg = 3.0m,
                TareWeightKg = 0.5m,
                NetWeightKg = 2.5m,
                LengthCm = 30m,
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

    #region 1. Tenant Isolation & Query Filter Verification

    [Fact]
    public async Task TenantIsolation_TrackingEventsAndPod_EnforcesClientIdFilter()
    {
        var fix = await SeedTestEnvironmentAsync();
        var clientB = Guid.NewGuid();
        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            seed.Clients.Add(new Client { Id = clientB, CompanyName = "Tenant Beta Corp", AccountCode = "TBC" });
            await seed.SaveChangesAsync();
        }

        var (_, shpA, pkgsA) = await CreateDispatchedOrderAndShipmentAsync(fix, 1, "SIM-FEDEX", clientIdOverride: fix.ClientId);
        var (_, shpB, pkgsB) = await CreateDispatchedOrderAndShipmentAsync(fix, 1, "SIM-FEDEX", clientIdOverride: clientB);

        var pkgA = pkgsA[0];
        var pkgB = pkgsB[0];

        // Seed Tracking Events and POD for both tenants
        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            seedContext.ShipmentTrackingEvents.AddRange(
                new ShipmentTrackingEvent
                {
                    ClientId = fix.ClientId,
                    FacilityId = fix.FacilityId,
                    PackageId = pkgA.Id,
                    ShipmentId = shpA.Id,
                    CarrierCode = "SIM-FEDEX",
                    TrackingNumber = pkgA.TrackingNumber!,
                    MilestoneStatus = TrackingMilestoneStatus.InTransit,
                    CarrierEventCode = "IT",
                    CarrierDescription = "In Transit - Hub A",
                    OccurredAtUtc = DateTime.UtcNow,
                    EventFingerprint = "FP-TENANT-A-01"
                },
                new ShipmentTrackingEvent
                {
                    ClientId = clientB,
                    FacilityId = fix.FacilityId,
                    PackageId = pkgB.Id,
                    ShipmentId = shpB.Id,
                    CarrierCode = "SIM-FEDEX",
                    TrackingNumber = pkgB.TrackingNumber!,
                    MilestoneStatus = TrackingMilestoneStatus.InTransit,
                    CarrierEventCode = "IT",
                    CarrierDescription = "In Transit - Hub B",
                    OccurredAtUtc = DateTime.UtcNow,
                    EventFingerprint = "FP-TENANT-B-01"
                }
            );

            seedContext.ProofOfDeliveries.AddRange(
                new ProofOfDelivery
                {
                    ClientId = fix.ClientId,
                    FacilityId = fix.FacilityId,
                    PackageId = pkgA.Id,
                    ShipmentId = shpA.Id,
                    TrackingNumber = pkgA.TrackingNumber!,
                    CarrierCode = "SIM-FEDEX",
                    RecipientName = "Alice A",
                    IsSigned = true,
                    DeliveredAtUtc = DateTime.UtcNow
                },
                new ProofOfDelivery
                {
                    ClientId = clientB,
                    FacilityId = fix.FacilityId,
                    PackageId = pkgB.Id,
                    ShipmentId = shpB.Id,
                    TrackingNumber = pkgB.TrackingNumber!,
                    CarrierCode = "SIM-FEDEX",
                    RecipientName = "Bob B",
                    IsSigned = true,
                    DeliveredAtUtc = DateTime.UtcNow
                }
            );

            await seedContext.SaveChangesAsync();
        }

        // Query with TenantProvider scoped to Client A (BypassTenantFilter = false)
        using (var queryContext = TestDbContextFactory.Create(
            new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = false },
            new TestCurrentUserContext { UserType = UserType.Client, TenantId = fix.ClientId },
            fix.DbName))
        {
            var trackingEvents = await queryContext.ShipmentTrackingEvents.ToListAsync();
            var pods = await queryContext.ProofOfDeliveries.ToListAsync();

            // Client A context MUST see ONLY Tenant A data
            Assert.Single(trackingEvents);
            Assert.Equal(fix.ClientId, trackingEvents[0].ClientId);
            Assert.Equal("FP-TENANT-A-01", trackingEvents[0].EventFingerprint);

            Assert.Single(pods);
            Assert.Equal(fix.ClientId, pods[0].ClientId);
            Assert.Equal("Alice A", pods[0].RecipientName);
        }
    }

    #endregion

    #region 2. Append-Only Event History & Navigation Relationships

    [Fact]
    public async Task ShipmentPackageOrderRelationships_NavigationsLoadCorrectly()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, shipment, packages) = await CreateDispatchedOrderAndShipmentAsync(fix, 2, "SIM-FEDEX");
        var pkg1 = packages[0];
        var pkg2 = packages[1];

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg1.Id,
                ShipmentId = shipment.Id,
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = pkg1.TrackingNumber!,
                MilestoneStatus = TrackingMilestoneStatus.InTransit,
                CarrierEventCode = "IT",
                CarrierDescription = "Departed Origin Sort Facility",
                OccurredAtUtc = DateTime.UtcNow,
                EventFingerprint = "FP-NAV-01"
            });

            context.ProofOfDeliveries.Add(new ProofOfDelivery
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg1.Id,
                ShipmentId = shipment.Id,
                TrackingNumber = pkg1.TrackingNumber!,
                CarrierCode = "SIM-FEDEX",
                RecipientName = "Sarah Connor",
                DeliveryLocationDescription = "Front Door",
                IsSigned = true,
                SignatureObjectKey = "signatures/2026/pkg1-sig.png",
                PhotoObjectKey = "photos/2026/pkg1-photo.jpg",
                SignatureContentType = "image/png",
                PhotoContentType = "image/jpeg",
                SignatureSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                DeliveredAtUtc = DateTime.UtcNow
            });

            context.ShipmentDeliveryExceptions.Add(new ShipmentDeliveryException
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg2.Id,
                ShipmentId = shipment.Id,
                TrackingNumber = pkg2.TrackingNumber!,
                CarrierCode = "SIM-FEDEX",
                ExceptionType = DeliveryExceptionType.RecipientUnavailable,
                CarrierExceptionCode = "D02",
                CarrierDescription = "Customer unavailable - business closed",
                OccurredAtUtc = DateTime.UtcNow
            });

            context.PublicTrackingShares.Add(new PublicTrackingShare
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg1.Id,
                ShipmentId = shipment.Id,
                PublicToken = "PUB-TOKEN-00112233445566778899AABB",
                TrackingNumber = pkg1.TrackingNumber!,
                CarrierCode = "SIM-FEDEX",
                MaskedRecipientName = "S*** C****",
                DestinationCity = "Chicago",
                DestinationState = "IL",
                MaskedPostalCode = "606**",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
            });

            await context.SaveChangesAsync();
        }

        // Query and verify navigation graph
        using (var queryContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var loadedShipment = await queryContext.Shipments
                .Include(s => s.Packages)
                    .ThenInclude(p => p.ProofOfDeliveries)
                .Include(s => s.Packages)
                    .ThenInclude(p => p.TrackingEvents)
                .Include(s => s.Packages)
                    .ThenInclude(p => p.DeliveryExceptions)
                .Include(s => s.Packages)
                    .ThenInclude(p => p.PublicTrackingShares)
                .FirstAsync(s => s.Id == shipment.Id);

            Assert.Equal(2, loadedShipment.Packages.Count);

            var p1 = loadedShipment.Packages.First(p => p.Id == pkg1.Id);
            Assert.NotNull(p1.ProofOfDelivery);
            Assert.Equal("Sarah Connor", p1.ProofOfDelivery.RecipientName);
            Assert.Equal("signatures/2026/pkg1-sig.png", p1.ProofOfDelivery.SignatureObjectKey);
            Assert.Single(p1.TrackingEvents);
            Assert.Single(p1.PublicTrackingShares);

            var p2 = loadedShipment.Packages.First(p => p.Id == pkg2.Id);
            Assert.Single(p2.DeliveryExceptions);
            Assert.Equal(DeliveryExceptionType.RecipientUnavailable, p2.DeliveryExceptions.First().ExceptionType);
        }
    }

    [Fact]
    public async Task TrackingEvents_AppendOnlyHistory_PreservesAllChronologicalEvents()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, shp, pkgs) = await CreateDispatchedOrderAndShipmentAsync(fix, 1, "SIM-FEDEX");
        var pkg = pkgs[0];

        var t0 = DateTime.UtcNow.AddHours(-4);
        var t1 = DateTime.UtcNow.AddHours(-3);
        var t2 = DateTime.UtcNow.AddHours(-2);
        var t3 = DateTime.UtcNow.AddHours(-1);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            // Event 1: Accepted
            context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg.Id,
                ShipmentId = shp.Id,
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = pkg.TrackingNumber!,
                MilestoneStatus = TrackingMilestoneStatus.Accepted,
                CarrierEventCode = "PU",
                CarrierDescription = "Picked up by carrier",
                OccurredAtUtc = t0,
                EventFingerprint = "FP-CHRONO-01"
            });

            // Event 2: InTransit
            context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg.Id,
                ShipmentId = shp.Id,
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = pkg.TrackingNumber!,
                MilestoneStatus = TrackingMilestoneStatus.InTransit,
                CarrierEventCode = "IT",
                CarrierDescription = "Arrived at Memphis Sort Hub",
                OccurredAtUtc = t1,
                EventFingerprint = "FP-CHRONO-02"
            });

            // Event 3: OutForDelivery
            context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg.Id,
                ShipmentId = shp.Id,
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = pkg.TrackingNumber!,
                MilestoneStatus = TrackingMilestoneStatus.OutForDelivery,
                CarrierEventCode = "OD",
                CarrierDescription = "On FedEx vehicle for delivery",
                OccurredAtUtc = t2,
                EventFingerprint = "FP-CHRONO-03"
            });

            // Event 4: Delivered
            context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg.Id,
                ShipmentId = shp.Id,
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = pkg.TrackingNumber!,
                MilestoneStatus = TrackingMilestoneStatus.Delivered,
                CarrierEventCode = "DL",
                CarrierDescription = "Delivered to front porch",
                OccurredAtUtc = t3,
                EventFingerprint = "FP-CHRONO-04"
            });

            await context.SaveChangesAsync();
        }

        // Verify chronological preservation without overwriting
        using (var queryContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var events = await queryContext.ShipmentTrackingEvents
                .Where(e => e.PackageId == pkg.Id)
                .OrderBy(e => e.OccurredAtUtc)
                .ToListAsync();

            Assert.Equal(4, events.Count);
            Assert.Equal(TrackingMilestoneStatus.Accepted, events[0].MilestoneStatus);
            Assert.Equal(TrackingMilestoneStatus.InTransit, events[1].MilestoneStatus);
            Assert.Equal(TrackingMilestoneStatus.OutForDelivery, events[2].MilestoneStatus);
            Assert.Equal(TrackingMilestoneStatus.Delivered, events[3].MilestoneStatus);
        }
    }

    [Fact]
    public async Task EventFingerprint_UniqueConstraint_RejectsDuplicateIngestion()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, shp, pkgs) = await CreateDispatchedOrderAndShipmentAsync(fix, 1, "SIM-FEDEX");
        var pkg = pkgs[0];

        const string duplicateFingerprint = "FP-DUP-VERIFY-999999";

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PackageId = pkg.Id,
            ShipmentId = shp.Id,
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            MilestoneStatus = TrackingMilestoneStatus.InTransit,
            CarrierEventCode = "IT",
            CarrierDescription = "In Transit - Scan 1",
            OccurredAtUtc = DateTime.UtcNow,
            EventFingerprint = duplicateFingerprint
        });
        await context.SaveChangesAsync();

        // Verify unique index constraint configuration in EF Core model metadata (since InMemory DB does not enforce unique constraints at runtime)
        var entityType = context.Model.FindEntityType(typeof(ShipmentTrackingEvent));
        Assert.NotNull(entityType);
        var fingerprintIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(ShipmentTrackingEvent.EventFingerprint)));
        Assert.NotNull(fingerprintIndex);
        Assert.True(fingerprintIndex.IsUnique);
        Assert.Equal("ix_shipment_tracking_events_tenant_fingerprint", fingerprintIndex.GetDatabaseName());
        var propNames = fingerprintIndex.Properties.Select(p => p.Name).ToList();
        Assert.Contains(nameof(ShipmentTrackingEvent.ClientId), propNames);
        Assert.Contains(nameof(ShipmentTrackingEvent.CarrierCode), propNames);
        Assert.Contains(nameof(ShipmentTrackingEvent.EventFingerprint), propNames);
    }

    #endregion

    #region 3. Lifecycle & Monotonic Status Rules

    [Fact]
    public async Task SeparateWarehouseAndDeliveryStatuses_PreservesPhase4Contract()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, _, packages) = await CreateDispatchedOrderAndShipmentAsync(fix, 2, "SIM-FEDEX");

        // OutboundOrder.Status was set to Completed at warehouse custody handover
        Assert.Equal(OutboundOrderStatus.Completed, order.Status);
        Assert.Equal(OrderDeliveryStatus.Dispatched, order.DeliveryStatus);

        // 1. Advance package 1 to InTransit
        packages[0].UpdateTrackingMilestone(TrackingMilestoneStatus.InTransit, DateTime.UtcNow);
        order.RecalculateDeliveryStatus();

        // Warehouse status remains Completed; Delivery status becomes InTransit
        Assert.Equal(OutboundOrderStatus.Completed, order.Status);
        Assert.Equal(OrderDeliveryStatus.InTransit, order.DeliveryStatus);

        // 2. Deliver package 1 (partial delivery)
        packages[0].ConfirmDelivery(DateTime.UtcNow);
        order.RecalculateDeliveryStatus();

        Assert.Equal(OutboundOrderStatus.Completed, order.Status);
        Assert.Equal(OrderDeliveryStatus.PartiallyDelivered, order.DeliveryStatus);

        // 3. Deliver package 2 (full delivery)
        packages[1].ConfirmDelivery(DateTime.UtcNow);
        order.RecalculateDeliveryStatus();

        Assert.Equal(OutboundOrderStatus.Completed, order.Status);
        Assert.Equal(OrderDeliveryStatus.Delivered, order.DeliveryStatus);
        Assert.NotNull(order.DeliveredAtUtc);
    }

    [Fact]
    public async Task MonotonicMilestoneProgression_OlderEventsDoNotRegressDeliveredPackage()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, _, packages) = await CreateDispatchedOrderAndShipmentAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        var deliveredTime = DateTime.UtcNow;
        pkg.ConfirmDelivery(deliveredTime);

        Assert.Equal(TrackingMilestoneStatus.Delivered, pkg.CurrentTrackingStatus);
        Assert.Equal(deliveredTime, pkg.DeliveredAtUtc);

        // An older OutForDelivery event arrives late
        var earlierTime = deliveredTime.AddHours(-1);
        pkg.UpdateTrackingMilestone(TrackingMilestoneStatus.OutForDelivery, earlierTime);

        // Monotonic guard ensures status stays Delivered and DeliveredAtUtc is preserved
        Assert.Equal(TrackingMilestoneStatus.Delivered, pkg.CurrentTrackingStatus);
        Assert.Equal(deliveredTime, pkg.DeliveredAtUtc);

        // An even older InTransit scan arrives
        pkg.UpdateTrackingMilestone(TrackingMilestoneStatus.InTransit, deliveredTime.AddHours(-3));
        Assert.Equal(TrackingMilestoneStatus.Delivered, pkg.CurrentTrackingStatus);
    }

    [Fact]
    public async Task DeliveryRollup_MultiPackageOrder_CalculatesExpectedStatuses()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (order, _, packages) = await CreateDispatchedOrderAndShipmentAsync(fix, 3, "SIM-FEDEX");
        Assert.Equal(3, packages.Count);

        // Scenario 1: One carton encounters an exception
        packages[0].UpdateTrackingMilestone(TrackingMilestoneStatus.DeliveryException, DateTime.UtcNow);
        packages[1].UpdateTrackingMilestone(TrackingMilestoneStatus.InTransit, DateTime.UtcNow);
        packages[2].UpdateTrackingMilestone(TrackingMilestoneStatus.PendingHandover, DateTime.UtcNow);
        order.RecalculateDeliveryStatus();

        Assert.Equal(OrderDeliveryStatus.DeliveryException, order.DeliveryStatus);

        // Scenario 2: Exception resolved, carton 1 & 2 delivered, carton 3 in transit
        packages[0].ConfirmDelivery(DateTime.UtcNow);
        packages[1].ConfirmDelivery(DateTime.UtcNow);
        packages[2].UpdateTrackingMilestone(TrackingMilestoneStatus.InTransit, DateTime.UtcNow);
        order.RecalculateDeliveryStatus();

        Assert.Equal(OrderDeliveryStatus.PartiallyDelivered, order.DeliveryStatus);

        // Scenario 3: All cartons delivered
        var finalDeliveryTime = DateTime.UtcNow.AddMinutes(5);
        packages[2].ConfirmDelivery(finalDeliveryTime);
        order.RecalculateDeliveryStatus();

        Assert.Equal(OrderDeliveryStatus.Delivered, order.DeliveryStatus);
        Assert.Equal(finalDeliveryTime, order.DeliveredAtUtc);
    }

    #endregion

    #region 4. Proof of Delivery & Exception Workflows

    [Fact]
    public async Task ProofOfDelivery_UniqueConstraintPerPackage_AndObjectStorageIntegrity()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, shp, packages) = await CreateDispatchedOrderAndShipmentAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        var pod1 = new ProofOfDelivery
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PackageId = pkg.Id,
            ShipmentId = shp.Id,
            TrackingNumber = pkg.TrackingNumber!,
            CarrierCode = "SIM-FEDEX",
            RecipientName = "Robert Langdon",
            DeliveryLocationDescription = "Rear Receiving Bay",
            IsSigned = true,
            SignatureObjectKey = "storage/signatures/pod-001.png",
            PhotoObjectKey = "storage/photos/pod-001.jpg",
            SignatureContentType = "image/png",
            PhotoContentType = "image/jpeg",
            SignatureSha256 = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
            PhotoSha256 = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
            EvidenceSource = PodEvidenceSource.CarrierApi,
            DeliveredAtUtc = DateTime.UtcNow
        };
        context.ProofOfDeliveries.Add(pod1);
        await context.SaveChangesAsync();

        // Verify unique index constraint configuration in EF Core model metadata (since InMemory DB does not enforce unique constraints at runtime)
        var entityType = context.Model.FindEntityType(typeof(ProofOfDelivery));
        Assert.NotNull(entityType);
        var packageIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(ProofOfDelivery.PackageId)));
        Assert.NotNull(packageIndex);
        Assert.True(packageIndex.IsUnique);
        Assert.Equal("ix_proof_of_deliveries_package_unique", packageIndex.GetDatabaseName());
    }

    [Fact]
    public async Task DeliveryException_ResolutionWorkflow_UpdatesStateAndUserAudit()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, shp, packages) = await CreateDispatchedOrderAndShipmentAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        var exception = new ShipmentDeliveryException
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PackageId = pkg.Id,
            ShipmentId = shp.Id,
            TrackingNumber = pkg.TrackingNumber!,
            CarrierCode = "SIM-FEDEX",
            ExceptionType = DeliveryExceptionType.AddressNotFound,
            CarrierExceptionCode = "A01",
            CarrierDescription = "Suite number missing from commercial building",
            OccurredAtUtc = DateTime.UtcNow
        };

        Assert.False(exception.IsResolved);
        Assert.Null(exception.ResolvedAtUtc);

        // Resolution without notes rejected
        Assert.Throws<ValidationException>(() =>
            exception.Resolve(DeliveryExceptionResolution.AddressCorrected, "  ", fix.UserId));

        // Resolve exception with valid reason
        const string notes = "Customer support contacted consignee; suite updated to Suite 400";
        exception.Resolve(DeliveryExceptionResolution.AddressCorrected, notes, fix.UserId);

        Assert.True(exception.IsResolved);
        Assert.Equal(DeliveryExceptionResolution.AddressCorrected, exception.Resolution);
        Assert.Equal(notes, exception.ResolutionNotes);
        Assert.Equal(fix.UserId, exception.ResolvedByUserId);
        Assert.NotNull(exception.ResolvedAtUtc);

        // Attempting to resolve an already resolved exception throws
        Assert.Throws<InvalidDomainStateException>(() =>
            exception.Resolve(DeliveryExceptionResolution.Resolved, "Re-resolving", fix.UserId));
    }

    #endregion

    #region 5. Public Tracking Share & Security

    [Fact]
    public async Task PublicTrackingToken_UniqueConstraint_AndExpiryRevocationBehavior()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, shp, packages) = await CreateDispatchedOrderAndShipmentAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        const string tokenStr = "SECURE-PUBLIC-TOKEN-ABC123456789";

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var share = new PublicTrackingShare
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg.Id,
                ShipmentId = shp.Id,
                PublicToken = tokenStr,
                TrackingNumber = pkg.TrackingNumber!,
                CarrierCode = "SIM-FEDEX",
                MaskedRecipientName = "J*** D**",
                DestinationCity = "Chicago",
                DestinationState = "IL",
                MaskedPostalCode = "606**",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                MaxAccessCount = 3
            };

            context.PublicTrackingShares.Add(share);
            await context.SaveChangesAsync();

            // 1. Valid access increments counter
            Assert.True(share.IsActive);
            share.RecordAccess();
            share.RecordAccess();
            Assert.Equal(2, share.AccessCount);

            // 2. Exceeding max access count invalidates token
            share.RecordAccess();
            Assert.Equal(3, share.AccessCount);
            Assert.False(share.IsActive);
            Assert.Throws<InvalidDomainStateException>(() => share.RecordAccess());

            // 3. Revocation invalidates token
            var share2 = new PublicTrackingShare
            {
                ClientId = fix.ClientId,
                FacilityId = fix.FacilityId,
                PackageId = pkg.Id,
                ShipmentId = shp.Id,
                PublicToken = "TOKEN-TO-REVOKE-9999",
                TrackingNumber = pkg.TrackingNumber!,
                CarrierCode = "SIM-FEDEX",
                MaskedRecipientName = "M*** K***",
                DestinationCity = "Springfield",
                DestinationState = "IL",
                MaskedPostalCode = "627**",
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
            };
            context.PublicTrackingShares.Add(share2);
            await context.SaveChangesAsync();

            Assert.True(share2.IsActive);
            share2.Revoke();
            Assert.False(share2.IsActive);
            Assert.NotNull(share2.RevokedAtUtc);
            Assert.Throws<InvalidDomainStateException>(() => share2.Revoke());
            Assert.Throws<InvalidDomainStateException>(() => share2.RecordAccess());
        }

        // 4. Duplicate token constraint check via EF Core model metadata
        using (var context2 = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            var entityType = context2.Model.FindEntityType(typeof(PublicTrackingShare));
            Assert.NotNull(entityType);
            var tokenIndex = entityType.GetIndexes()
                .FirstOrDefault(i => i.Properties.Any(p => p.Name == nameof(PublicTrackingShare.PublicToken)));
            Assert.NotNull(tokenIndex);
            Assert.True(tokenIndex.IsUnique);
            Assert.Equal("ix_public_tracking_shares_token_unique", tokenIndex.GetDatabaseName());
        }
    }

    #endregion

    #region 6. Invariant: Zero Stock Balance Mutation (ΔQ = 0)

    [Fact]
    public async Task ZeroStockBalanceMutation_InvariantVerification()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (_, shp, packages) = await CreateDispatchedOrderAndShipmentAsync(fix, 1, "SIM-FEDEX");
        var pkg = packages[0];

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName);

        // Capture initial stock state before tracking operations
        var initialStock = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        decimal initialTotal = initialStock.TotalQuantity;
        decimal initialAllocated = initialStock.QtyAllocated;
        decimal initialAvailable = initialStock.QtyAvailable;
        int initialTxCount = await context.InventoryTransactions.CountAsync();

        // 1. Ingest tracking events
        context.ShipmentTrackingEvents.Add(new ShipmentTrackingEvent
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PackageId = pkg.Id,
            ShipmentId = shp.Id,
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = pkg.TrackingNumber!,
            MilestoneStatus = TrackingMilestoneStatus.InTransit,
            CarrierEventCode = "IT",
            CarrierDescription = "In transit across state lines",
            OccurredAtUtc = DateTime.UtcNow,
            EventFingerprint = "FP-ZERO-MUT-01"
        });

        // 2. Ingest delivery exception
        context.ShipmentDeliveryExceptions.Add(new ShipmentDeliveryException
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PackageId = pkg.Id,
            ShipmentId = shp.Id,
            TrackingNumber = pkg.TrackingNumber!,
            CarrierCode = "SIM-FEDEX",
            ExceptionType = DeliveryExceptionType.WeatherOrNaturalDisaster,
            CarrierExceptionCode = "W01",
            CarrierDescription = "Blizzard warning delay",
            OccurredAtUtc = DateTime.UtcNow
        });

        // 3. Confirm POD
        context.ProofOfDeliveries.Add(new ProofOfDelivery
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PackageId = pkg.Id,
            ShipmentId = shp.Id,
            TrackingNumber = pkg.TrackingNumber!,
            CarrierCode = "SIM-FEDEX",
            RecipientName = "Final Consignee",
            IsSigned = true,
            DeliveredAtUtc = DateTime.UtcNow
        });

        // 4. Create Public Tracking Share
        context.PublicTrackingShares.Add(new PublicTrackingShare
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            PackageId = pkg.Id,
            ShipmentId = shp.Id,
            PublicToken = "ZERO-MUT-TOKEN-12345",
            TrackingNumber = pkg.TrackingNumber!,
            CarrierCode = "SIM-FEDEX",
            MaskedRecipientName = "F*** C****",
            DestinationCity = "Denver",
            DestinationState = "CO",
            MaskedPostalCode = "802**",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(14)
        });

        await context.SaveChangesAsync();

        // Verify that StockOnHand and InventoryTransactions are strictly untouched (ΔQ = 0)
        var finalStock = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        Assert.Equal(initialTotal, finalStock.TotalQuantity);
        Assert.Equal(initialAllocated, finalStock.QtyAllocated);
        Assert.Equal(initialAvailable, finalStock.QtyAvailable);

        int finalTxCount = await context.InventoryTransactions.CountAsync();
        Assert.Equal(initialTxCount, finalTxCount);
    }

    #endregion
}
