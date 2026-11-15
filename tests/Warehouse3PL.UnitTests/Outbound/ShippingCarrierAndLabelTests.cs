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

public class ShippingCarrierAndLabelTests
{
    #region Simulated Carrier Service Tests

    [Fact]
    public async Task SimulatedCarrier_ReturnsDeterministicRates_BasedOnWeightAndServiceLevel()
    {
        var carrier = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);

        var request = new CarrierRateRequest
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            Parcel = new CarrierParcel
            {
                PackageNumber = "PKG-TEST-01",
                LengthCm = 30m,
                WidthCm = 20m,
                HeightCm = 15m,
                WeightKg = 4.0m
            },
            OriginAddress = new CarrierAddress { PostalCode = "43215", StateOrProvince = "OH" },
            DestinationAddress = new CarrierAddress { PostalCode = "90210", StateOrProvince = "CA" }
        };

        var quotes = await carrier.GetRatesAsync(request);

        Assert.NotEmpty(quotes);
        // Rates must be available across major carriers
        Assert.Contains(quotes, q => q.CarrierCode == "SIM-FEDEX");
        Assert.Contains(quotes, q => q.CarrierCode == "SIM-UPS");
        Assert.Contains(quotes, q => q.CarrierCode == "SIM-USPS");

        // Priority overnight must cost more than standard ground for same carrier
        var fedexGround = quotes.First(q => q.CarrierCode == "SIM-FEDEX" && q.ServiceLevel == CarrierServiceLevel.StandardGround);
        var fedexOvernight = quotes.First(q => q.CarrierCode == "SIM-FEDEX" && q.ServiceLevel == CarrierServiceLevel.PriorityOvernight);

        Assert.True(fedexOvernight.TotalAmount > fedexGround.TotalAmount,
            $"Expected PriorityOvernight ({fedexOvernight.TotalAmount}) > StandardGround ({fedexGround.TotalAmount})");
    }

    [Fact]
    public async Task SimulatedCarrier_GeneratesDeterministicTrackingNumber_AndLabelData()
    {
        var carrier = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);

        var requestZpl = new CarrierShipmentRequest
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            PackageId = Guid.NewGuid(),
            PackageNumber = "PKG-DETERM-01",
            CarrierCode = "SIM-FEDEX",
            ServiceLevel = CarrierServiceLevel.StandardGround,
            LabelFormat = "ZPL",
            IdempotencyKey = "IDEMP-KEY-100",
            Parcel = new CarrierParcel { PackageNumber = "PKG-DETERM-01", WeightKg = 2.5m },
            DestinationAddress = new CarrierAddress { Name = "Jane Doe", StreetLine1 = "123 Main St", City = "Chicago", StateOrProvince = "IL", PostalCode = "60601" }
        };

        // 1. ZPL generation
        var res1 = await carrier.CreateShipmentAsync(requestZpl);
        Assert.True(res1.IsSuccess);
        Assert.True(res1.IsSimulated);
        Assert.StartsWith("SIM-FEDEX-", res1.TrackingNumber);
        Assert.Contains("^XA", res1.LabelData);
        Assert.Contains(res1.TrackingNumber, res1.LabelData);

        // 2. Determinism check: Identical request produces identical tracking number
        var res2 = await carrier.CreateShipmentAsync(requestZpl);
        Assert.Equal(res1.TrackingNumber, res2.TrackingNumber);

        // 3. PDF format generation
        var requestPdf = new CarrierShipmentRequest
        {
            ClientId = requestZpl.ClientId,
            FacilityId = requestZpl.FacilityId,
            PackageId = requestZpl.PackageId,
            PackageNumber = "PKG-DETERM-01",
            CarrierCode = "SIM-FEDEX",
            ServiceLevel = CarrierServiceLevel.StandardGround,
            LabelFormat = "PDF",
            IdempotencyKey = "IDEMP-KEY-100",
            Parcel = new CarrierParcel { PackageNumber = "PKG-DETERM-01", WeightKg = 2.5m },
            DestinationAddress = requestZpl.DestinationAddress
        };

        var resPdf = await carrier.CreateShipmentAsync(requestPdf);
        Assert.True(resPdf.IsSuccess);
        Assert.Equal("PDF", resPdf.LabelFormat);
        Assert.NotEmpty(resPdf.LabelData); // Base64 encoded
    }

    #endregion

    #region Shipping Label Service & Transaction Decoupling Tests

    [Fact]
    public async Task GenerateLabel_Success_CreatesShippingLabel_AndUpdatesPackageToLabelPrinted()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (packageId, _) = await SeedShippingEnvironmentAsync(dbName, clientA, facilityId, packageStatus: PackageStatus.Packed);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var carrier = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
            var service = new ShippingLabelService(context, carrier, NullLogger<ShippingLabelService>.Instance);

            var result = await service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = packageId,
                CarrierCode = "SIM-FEDEX",
                ServiceLevel = CarrierServiceLevel.StandardGround,
                LabelFormat = "ZPL",
                IdempotencyKey = "IDEMP-SUCCESS-01",
                UserId = Guid.NewGuid()
            });

            Assert.True(result.IsSuccess);
            Assert.NotEmpty(result.TrackingNumber);
            Assert.Equal("ZPL", result.LabelFormat);
            Assert.Equal(PackageStatus.LabelPrinted, result.PackageStatus);
            Assert.True(result.IsSimulated);

            // Verify persistence in DB
            var pkgInDb = await context.Packages.Include(p => p.ShippingLabels).FirstAsync(p => p.Id == packageId);
            Assert.Equal(PackageStatus.LabelPrinted, pkgInDb.Status);
            Assert.Equal(result.TrackingNumber, pkgInDb.TrackingNumber);
            Assert.Equal("SIM-FEDEX", pkgInDb.CarrierCode);

            // Verify ShippingLabel entity
            var label = Assert.Single(pkgInDb.ShippingLabels);
            Assert.Equal(ShippingLabelStatus.Generated, label.Status);
            Assert.Equal(result.TrackingNumber, label.TrackingNumber);
            Assert.Contains("^XA", label.LabelData);

            // Verify CarrierOperationLog persisted
            var log = await context.CarrierOperationLogs.FirstAsync(l => l.PackageId == packageId);
            Assert.Equal("Success", log.Status);
            Assert.Equal("IDEMP-SUCCESS-01", log.IdempotencyKey);
            Assert.Equal(result.TrackingNumber, log.TrackingNumber);
            Assert.NotNull(log.CompletedAtUtc);
        }
    }

    [Fact]
    public async Task GenerateLabel_CarrierFailure_DoesNotCorruptPackage_PackageRemainsPacked()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (packageId, _) = await SeedShippingEnvironmentAsync(dbName, clientA, facilityId, packageStatus: PackageStatus.Packed);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var carrier = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
            var service = new ShippingLabelService(context, carrier, NullLogger<ShippingLabelService>.Instance);

            // Inject simulated carrier failure
            var result = await service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = packageId,
                CarrierCode = "SIM-FEDEX",
                ServiceLevel = CarrierServiceLevel.StandardGround,
                LabelFormat = "ZPL",
                SimulateFailure = true,
                UserId = Guid.NewGuid()
            });

            Assert.False(result.IsSuccess);
            Assert.Contains("SIMULATED_CARRIER_ERROR", result.ErrorMessage);

            // Package MUST remain strictly in Packed status (does not advance)
            var pkgInDb = await context.Packages.Include(p => p.ShippingLabels).FirstAsync(p => p.Id == packageId);
            Assert.Equal(PackageStatus.Packed, pkgInDb.Status);
            Assert.Null(pkgInDb.TrackingNumber);
            Assert.Empty(pkgInDb.ShippingLabels);

            // Audit log records failure
            var log = await context.CarrierOperationLogs.FirstAsync(l => l.PackageId == packageId);
            Assert.Equal("Failed", log.Status);
            Assert.Contains("SIMULATED_CARRIER_ERROR", log.ErrorMessage);
        }
    }

    [Fact]
    public async Task GenerateLabel_IdempotentRetry_DoesNotCreateDuplicateCarrierBooking()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (packageId, _) = await SeedShippingEnvironmentAsync(dbName, clientA, facilityId, packageStatus: PackageStatus.Packed);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var carrier = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
            var service = new ShippingLabelService(context, carrier, NullLogger<ShippingLabelService>.Instance);

            const string idempotencyKey = "IDEMP-RETRY-999";

            // 1. Initial Request
            var result1 = await service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = packageId,
                CarrierCode = "SIM-UPS",
                ServiceLevel = CarrierServiceLevel.SecondDayAir,
                IdempotencyKey = idempotencyKey,
                UserId = Guid.NewGuid()
            });
            Assert.True(result1.IsSuccess);

            // 2. Retry with same IdempotencyKey
            var result2 = await service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = packageId,
                CarrierCode = "SIM-UPS",
                ServiceLevel = CarrierServiceLevel.SecondDayAir,
                IdempotencyKey = idempotencyKey,
                UserId = Guid.NewGuid()
            });

            Assert.True(result2.IsSuccess);
            // Returns exact same label and tracking number without duplicate booking
            Assert.Equal(result1.LabelId, result2.LabelId);
            Assert.Equal(result1.TrackingNumber, result2.TrackingNumber);
            Assert.Contains("Idempotent", result2.Message);

            // Exactly 1 label exists in DB
            var labelCount = await context.ShippingLabels.CountAsync(l => l.PackageId == packageId);
            Assert.Equal(1, labelCount);
        }
    }

    [Fact]
    public async Task GenerateLabel_RejectsPackage_WhenStatusIsNotPacked()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        // Package still in Open status (not sealed yet)
        var (packageId, _) = await SeedShippingEnvironmentAsync(dbName, clientA, facilityId, packageStatus: PackageStatus.Open);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var carrier = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
            var service = new ShippingLabelService(context, carrier, NullLogger<ShippingLabelService>.Instance);

            var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
                service.GenerateLabelAsync(new GenerateShippingLabelRequest
                {
                    PackageId = packageId,
                    CarrierCode = "SIM-FEDEX",
                    ServiceLevel = CarrierServiceLevel.StandardGround,
                    UserId = Guid.NewGuid()
                }));

            Assert.Contains("Package must be Packed", ex.Message);
        }
    }

    [Fact]
    public async Task GenerateLabel_RejectsActiveDuplicateLabel_WithoutIdempotencyKey()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (packageId, _) = await SeedShippingEnvironmentAsync(dbName, clientA, facilityId, packageStatus: PackageStatus.Packed);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var carrier = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
            var service = new ShippingLabelService(context, carrier, NullLogger<ShippingLabelService>.Instance);

            // 1. Generate First Label
            await service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = packageId,
                CarrierCode = "SIM-FEDEX",
                ServiceLevel = CarrierServiceLevel.StandardGround,
                IdempotencyKey = "KEY-A",
                UserId = Guid.NewGuid()
            });

            // 2. Attempt to generate another label without voiding first
            var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
                service.GenerateLabelAsync(new GenerateShippingLabelRequest
                {
                    PackageId = packageId,
                    CarrierCode = "SIM-UPS",
                    ServiceLevel = CarrierServiceLevel.NextDayAir,
                    IdempotencyKey = "KEY-B", // Different key
                    UserId = Guid.NewGuid()
                }));

            Assert.Contains("already has an active shipping label", ex.Message);
            Assert.Contains("Void the existing label first", ex.Message);
        }
    }

    [Fact]
    public async Task VoidLabel_Success_RecordsAudit_AndRevertsPackageToPacked()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (packageId, _) = await SeedShippingEnvironmentAsync(dbName, clientA, facilityId, packageStatus: PackageStatus.Packed);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var carrier = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
            var service = new ShippingLabelService(context, carrier, NullLogger<ShippingLabelService>.Instance);

            // 1. Generate label
            var genResult = await service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = packageId,
                CarrierCode = "SIM-FEDEX",
                ServiceLevel = CarrierServiceLevel.StandardGround,
                UserId = Guid.NewGuid()
            });

            // 2. Void label
            var voidResult = await service.VoidLabelAsync(new VoidShippingLabelRequest
            {
                LabelId = genResult.LabelId,
                Reason = "Customer changed shipping speed to Next Day Air",
                UserId = Guid.NewGuid()
            });

            Assert.True(voidResult.IsSuccess);
            Assert.Equal(ShippingLabelStatus.Voided, voidResult.LabelStatus);
            Assert.Equal(PackageStatus.Packed, voidResult.PackageStatus); // Reverted!
            Assert.NotEmpty(voidResult.VoidConfirmationNumber);

            // Verify in DB: package tracking is cleared and package is ready for new label
            var pkgInDb = await context.Packages.FindAsync(packageId);
            Assert.Equal(PackageStatus.Packed, pkgInDb!.Status);
            Assert.Null(pkgInDb.TrackingNumber);

            var labelInDb = await context.ShippingLabels.FindAsync(genResult.LabelId);
            Assert.Equal(ShippingLabelStatus.Voided, labelInDb!.Status);
            Assert.True(labelInDb.IsVoided);
            Assert.Equal("Customer changed shipping speed to Next Day Air", labelInDb.VoidReason);
        }
    }

    [Fact]
    public async Task VoidLabel_CannotTreatVoidedLabelAsValid()
    {
        var label = new ShippingLabel
        {
            PackageId = Guid.NewGuid(),
            TrackingNumber = "SIM-FEDEX-VOID-99",
            CarrierCode = "SIM-FEDEX",
            Status = ShippingLabelStatus.Generated
        };

        label.Void("Cancelled order");
        Assert.True(label.IsVoided);

        // Cannot print voided label
        var ex = Assert.Throws<InvalidDomainStateException>(() => label.MarkPrinted());
        Assert.Contains("Cannot print voided shipping label", ex.Message);
    }

    [Fact]
    public async Task MultiplePackages_CanBeLabelledIndependently()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();

        var (pkg1Id, orderId) = await SeedShippingEnvironmentAsync(dbName, clientA, facilityId, packageStatus: PackageStatus.Packed);

        Guid pkg2Id = Guid.NewGuid();
        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var hu2 = new HandlingUnit
            {
                ClientId = clientA,
                FacilityId = facilityId,
                LpnCode = "CTN-ORD-002",
                UnitType = HandlingUnitType.ShippingCarton,
                Status = HandlingUnitStatus.InUse
            };
            seed.HandlingUnits.Add(hu2);

            var pkg2 = new Package
            {
                Id = pkg2Id,
                ClientId = clientA,
                FacilityId = facilityId,
                OutboundOrderId = orderId,
                HandlingUnitId = hu2.Id,
                PackageNumber = "PKG-ORD-02",
                GrossWeightKg = 4.2m,
                TareWeightKg = 0.5m,
                NetWeightKg = 3.7m,
                Status = PackageStatus.Packed
            };
            seed.Packages.Add(pkg2);
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var carrier = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
            var service = new ShippingLabelService(context, carrier, NullLogger<ShippingLabelService>.Instance);

            // Label Package 1 with FedEx Ground
            var res1 = await service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = pkg1Id,
                CarrierCode = "SIM-FEDEX",
                ServiceLevel = CarrierServiceLevel.StandardGround,
                UserId = Guid.NewGuid()
            });

            // Label Package 2 with UPS Next Day Air
            var res2 = await service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = pkg2Id,
                CarrierCode = "SIM-UPS",
                ServiceLevel = CarrierServiceLevel.NextDayAir,
                UserId = Guid.NewGuid()
            });

            Assert.True(res1.IsSuccess);
            Assert.True(res2.IsSuccess);
            Assert.NotEqual(res1.TrackingNumber, res2.TrackingNumber);
            Assert.NotEqual(res1.LabelId, res2.LabelId);
            Assert.Equal("SIM-FEDEX", res1.CarrierCode);
            Assert.Equal("SIM-UPS", res2.CarrierCode);
        }
    }

    [Fact]
    public async Task TrackPackage_ReturnsDeterministicMilestones()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (packageId, _) = await SeedShippingEnvironmentAsync(dbName, clientA, facilityId, packageStatus: PackageStatus.Packed);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var carrier = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);
            var service = new ShippingLabelService(context, carrier, NullLogger<ShippingLabelService>.Instance);

            var labelRes = await service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = packageId,
                CarrierCode = "SIM-FEDEX",
                ServiceLevel = CarrierServiceLevel.StandardGround,
                UserId = Guid.NewGuid()
            });

            var tracking = await service.TrackPackageAsync(packageId);

            Assert.True(tracking.IsSuccess);
            Assert.Equal(labelRes.TrackingNumber, tracking.TrackingNumber);
            Assert.NotEmpty(tracking.Events);
            Assert.Contains(tracking.Events, e => e.Status == "ManifestCreated");
        }
    }

    #endregion

    #region Helper Seed Methods

    private static async Task<(Guid packageId, Guid orderId)> SeedShippingEnvironmentAsync(
        string dbName,
        Guid clientId,
        Guid facilityId,
        PackageStatus packageStatus = PackageStatus.Packed)
    {
        var orderId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var boxMaterialId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var handlingUnitId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Shipping Client", AccountCode = "SC" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Columbus Logistics Hub", FacilityCode = "CMH-01" });

            seed.Locations.Add(new Location
            {
                Id = stationId,
                FacilityId = facilityId,
                LocationCode = "PK-BENCH-01",
                Barcode = "LOC-PK-01",
                LocationType = LocationType.PackingStation
            });

            var item = new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "SKU-SHIP-01",
                Description = "Item Ready to Ship",
                WeightKg = 1.2m,
                LengthCm = 20m,
                WidthCm = 15m,
                HeightCm = 10m
            };
            seed.Items.Add(item);

            var boxMaterial = new PackagingMaterial
            {
                Id = boxMaterialId,
                FacilityId = facilityId,
                Code = "BOX-MD",
                Name = "Medium Box",
                LengthCm = 35m,
                WidthCm = 25m,
                HeightCm = 20m,
                MaxWeightKg = 25m,
                TareWeightKg = 0.5m,
                IsActive = true
            };
            seed.PackagingMaterials.Add(boxMaterial);

            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = clientId,
                FacilityId = facilityId,
                OrderNumber = "ORD-SHP-001",
                OrderType = OutboundOrderType.EcommerceFulfillment,
                Status = OutboundOrderStatus.InProgress
            };

            var address = new ShippingAddress
            {
                OutboundOrderId = orderId,
                RecipientName = "Alice Smith",
                CompanyName = "Acme Corp",
                AddressLine1 = "742 Evergreen Terrace",
                City = "Springfield",
                StateOrProvince = "OR",
                PostalCode = "97477",
                CountryCode = "USA",
                PhoneNumber = "555-0199"
            };
            order.ShippingAddress = address;

            seed.OutboundOrders.Add(order);

            var cartonHu = new HandlingUnit
            {
                Id = handlingUnitId,
                ClientId = clientId,
                FacilityId = facilityId,
                LpnCode = "CTN-ORD-001-01",
                UnitType = HandlingUnitType.ShippingCarton,
                Status = HandlingUnitStatus.InUse,
                CurrentLocationId = stationId,
                CurrentOutboundOrderId = orderId
            };
            seed.HandlingUnits.Add(cartonHu);

            var package = new Package
            {
                Id = packageId,
                ClientId = clientId,
                FacilityId = facilityId,
                OutboundOrderId = orderId,
                HandlingUnitId = handlingUnitId,
                HandlingUnit = cartonHu,
                PackagingMaterialId = boxMaterialId,
                PackageNumber = "PKG-ORD-001-01",
                CartonSequence = 1,
                CurrentLocationId = stationId,
                LengthCm = 35m,
                WidthCm = 25m,
                HeightCm = 20m,
                GrossWeightKg = 3.5m,
                TareWeightKg = 0.5m,
                NetWeightKg = 3.0m,
                Status = packageStatus
            };
            seed.Packages.Add(package);

            await seed.SaveChangesAsync();
        }

        return (packageId, orderId);
    }

    #endregion
}

