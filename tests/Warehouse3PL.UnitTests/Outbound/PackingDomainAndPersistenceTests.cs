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
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Outbound;

public class PackingDomainAndPersistenceTests
{
    #region PackagingMaterial Domain Tests

    [Fact]
    public void PackagingMaterial_CalculatesVolumeAndVolumetricWeight()
    {
        var material = new PackagingMaterial
        {
            Code = "BOX-M1",
            Name = "Medium Corrugated Box",
            MaterialType = PackagingMaterialType.CorrugatedBox,
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 20m,
            MaxWeightKg = 25m,
            TareWeightKg = 0.45m
        };

        // Volume: 40 * 30 * 20 = 24,000 cm3
        Assert.Equal(24000m, material.CalculatedVolumeCm3);

        // Standard Divisor 5000: 24,000 / 5000 = 4.80 kg
        Assert.Equal(4.80m, material.CalculateVolumetricWeightKg(5000m));

        // Courier Divisor 4000: 24,000 / 4000 = 6.00 kg
        Assert.Equal(6.00m, material.CalculateVolumetricWeightKg(4000m));
    }

    [Fact]
    public void PackagingMaterial_SupportsGlobalAndClientSpecificPartitions()
    {
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();

        // Facility global packaging (ClientId = null)
        var globalBox = new PackagingMaterial
        {
            FacilityId = facilityId,
            ClientId = null,
            Code = "STD-BOX-1",
            Name = "Facility Standard Box 1"
        };
        Assert.Null(globalBox.ClientId);

        // Client branded packaging (ClientId set)
        var brandedBox = new PackagingMaterial
        {
            FacilityId = facilityId,
            ClientId = clientId,
            Code = "BRANDED-BOX-1",
            Name = "Custom Branded Retail Box"
        };
        Assert.Equal(clientId, brandedBox.ClientId);
    }

    #endregion

    #region PackingSession Domain Tests

    [Fact]
    public void PackingSession_Lifecycle_TransitionsThroughStates()
    {
        var userId = Guid.NewGuid();
        var session = new PackingSession
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            PackingStationLocationId = Guid.NewGuid(),
            OutboundOrderId = Guid.NewGuid(),
            OperatorUserId = userId
        };

        Assert.Equal(PackingSessionStatus.Active, session.Status);
        Assert.NotEqual(default, session.StartedAtUtc);
        Assert.Null(session.CompletedAtUtc);

        // Suspend session (e.g. break)
        session.Suspend("Operator lunch break", userId);
        Assert.Equal(PackingSessionStatus.Suspended, session.Status);
        Assert.Equal("Operator lunch break", session.Notes);

        // Cannot suspend already suspended session
        Assert.Throws<InvalidDomainStateException>(() => session.Suspend("Already suspended", userId));

        // Resume session
        session.Resume(userId);
        Assert.Equal(PackingSessionStatus.Active, session.Status);

        // Complete session
        session.Complete(userId);
        Assert.Equal(PackingSessionStatus.Completed, session.Status);
        Assert.NotNull(session.CompletedAtUtc);

        // Cannot abort completed session
        Assert.Throws<InvalidDomainStateException>(() => session.Abort("Too late", userId));
    }

    #endregion

    #region Package and PackageLine Domain Tests

    [Fact]
    public void Package_ApplyPackagingMaterial_SnapshotsDimensionsAndTareWeight()
    {
        var material = new PackagingMaterial
        {
            Code = "BOX-S",
            LengthCm = 25m,
            WidthCm = 20m,
            HeightCm = 15m,
            TareWeightKg = 0.30m
        };

        var package = new Package
        {
            PackageNumber = "PKG-001"
        };

        package.ApplyPackagingMaterial(material);

        Assert.Equal(material.Id, package.PackagingMaterialId);
        Assert.Equal(25m, package.LengthCm);
        Assert.Equal(20m, package.WidthCm);
        Assert.Equal(15m, package.HeightCm);
        Assert.Equal(0.30m, package.TareWeightKg);
        Assert.Equal(1.50m, package.VolumetricWeightKg); // (25*20*15)/5000 = 1.5 kg
    }

    [Fact]
    public void Package_AddLineAndClose_CalculatesNetWeightAndSealsPackage()
    {
        var userId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var toteId = Guid.NewGuid();

        var package = new Package
        {
            PackageNumber = "PKG-100",
            TareWeightKg = 0.50m,
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 20m
        };

        // Add 3 units of Item
        package.AddLine(orderLineId, itemId, null, 3m, toteId, allocId);
        Assert.Single(package.Lines);
        Assert.Equal(3m, package.Lines.First().Quantity);

        // Add 2 more units of same Item/Line -> should accumulate to 5
        package.AddLine(orderLineId, itemId, null, 2m, toteId, allocId);
        Assert.Single(package.Lines);
        Assert.Equal(5m, package.Lines.First().Quantity);

        // Close package with scale weight 4.20 kg
        // Gross = 4.20 kg, Tare = 0.50 kg -> Net = 3.70 kg
        package.Close(4.20m, userId);

        Assert.Equal(PackageStatus.Packed, package.Status);
        Assert.Equal(4.20m, package.GrossWeightKg);
        Assert.Equal(0.50m, package.TareWeightKg);
        Assert.Equal(3.70m, package.NetWeightKg);
        Assert.NotNull(package.PackedAtUtc);
        Assert.Equal(userId, package.PackedByUserId);

        // Cannot add more items once closed
        Assert.Throws<InvalidDomainStateException>(() => package.AddLine(orderLineId, itemId, null, 1m, toteId, allocId));
    }

    [Fact]
    public void Package_Close_RejectsGrossWeightLessThanTareWeight_OrZero()
    {
        var userId = Guid.NewGuid();
        var package = new Package
        {
            PackageNumber = "PKG-ERR",
            TareWeightKg = 1.0m
        };
        package.Lines.Add(new PackageLine { Quantity = 1m });

        // Zero weight rejected
        Assert.Throws<ValidationException>(() => package.Close(0m, userId));

        // Weight less than tare rejected (scale error)
        Assert.Throws<ValidationException>(() => package.Close(0.80m, userId));
    }

    [Fact]
    public void Package_Lifecycle_LabelStageLoadAndCancel()
    {
        var package = new Package
        {
            PackageNumber = "PKG-FLOW",
            TareWeightKg = 0.5m,
            Status = PackageStatus.Packed
        };
        package.Lines.Add(new PackageLine { Quantity = 2m });

        var stagingLocationId = Guid.NewGuid();

        // 1. Label package
        package.SetShippingLabel("1Z9999999999999999", "UPS", "StandardGround");
        Assert.Equal(PackageStatus.LabelPrinted, package.Status);
        Assert.Equal("1Z9999999999999999", package.TrackingNumber);
        Assert.Equal("UPS", package.CarrierCode);

        // 2. Stage package at dock lane
        package.Stage(stagingLocationId);
        Assert.Equal(PackageStatus.Staged, package.Status);
        Assert.Equal(stagingLocationId, package.CurrentLocationId);

        // 3. Load onto trailer
        package.Load();
        Assert.Equal(PackageStatus.Loaded, package.Status);

        // 4. Cannot cancel once loaded
        Assert.Throws<InvalidDomainStateException>(() => package.Cancel("Recall"));
    }

    #endregion

    #region ShippingLabel Domain Tests

    [Fact]
    public void ShippingLabel_Lifecycle_MarkPrintedAndVoid()
    {
        var label = new ShippingLabel
        {
            PackageId = Guid.NewGuid(),
            TrackingNumber = "TRK-2026-001",
            CarrierCode = "FEDEX",
            LabelFormat = "ZPL",
            LabelData = "^XA^FDHello^XZ"
        };

        Assert.Equal(ShippingLabelStatus.Generated, label.Status);
        Assert.False(label.IsVoided);

        label.MarkPrinted();
        Assert.Equal(ShippingLabelStatus.Printed, label.Status);
        Assert.NotNull(label.PrintedAtUtc);

        label.Void("Address correction required");
        Assert.Equal(ShippingLabelStatus.Voided, label.Status);
        Assert.True(label.IsVoided);
        Assert.NotNull(label.VoidedAtUtc);
        Assert.Equal("Address correction required", label.VoidReason);

        // Cannot print voided label
        Assert.Throws<InvalidDomainStateException>(() => label.MarkPrinted());
    }

    #endregion

    #region Shipment Domain Tests

    [Fact]
    public void Shipment_AddPackage_EnforcesTenantAndFacilitySafety_AndRecalculatesTotals()
    {
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityA = Guid.NewGuid();
        var facilityB = Guid.NewGuid();

        var shipment = new Shipment
        {
            ClientId = clientA,
            FacilityId = facilityA,
            ShipmentNumber = "SHP-2026-001",
            CarrierCode = "UPS"
        };

        var package1 = new Package
        {
            ClientId = clientA,
            FacilityId = facilityA,
            PackageNumber = "PKG-01",
            GrossWeightKg = 5.0m,
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 20m,
            Status = PackageStatus.Packed
        };

        var packageCrossTenant = new Package
        {
            ClientId = clientB,
            FacilityId = facilityA,
            PackageNumber = "PKG-CLB",
            Status = PackageStatus.Packed
        };

        var packageCrossFacility = new Package
        {
            ClientId = clientA,
            FacilityId = facilityB,
            PackageNumber = "PKG-FB",
            Status = PackageStatus.Packed
        };

        // Cross-tenant rejected
        Assert.Throws<DomainException>(() => shipment.AddPackage(packageCrossTenant));

        // Cross-facility rejected
        Assert.Throws<DomainException>(() => shipment.AddPackage(packageCrossFacility));

        // Valid package added
        shipment.AddPackage(package1);
        Assert.Single(shipment.Packages);
        Assert.Equal(1, shipment.TotalPackagesCount);
        Assert.Equal(5.0m, shipment.TotalWeightKg);
        Assert.Equal(12000m, shipment.TotalVolumeCm3); // 30*20*20
    }

    [Fact]
    public void Shipment_Lifecycle_MarkReadyForDispatchAndDispatch()
    {
        var clientA = Guid.NewGuid();
        var facilityA = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var shipment = new Shipment
        {
            ClientId = clientA,
            FacilityId = facilityA,
            ShipmentNumber = "SHP-DISP-01",
            CarrierCode = "FEDEX"
        };

        var pkg1 = new Package
        {
            ClientId = clientA,
            FacilityId = facilityA,
            PackageNumber = "PKG-RDY-1",
            GrossWeightKg = 3.5m,
            Status = PackageStatus.LabelPrinted
        };
        shipment.AddPackage(pkg1);

        // 1. Transition to ReadyForDispatch
        shipment.MarkReadyForDispatch();
        Assert.Equal(ShipmentStatus.ReadyForDispatch, shipment.Status);

        // 2. Dispatch carrier trailer
        shipment.Dispatch("TR-5301", "SEAL-9988", "BOL-10023", userId);
        Assert.Equal(ShipmentStatus.Dispatched, shipment.Status);
        Assert.Equal("TR-5301", shipment.TrailerNumber);
        Assert.Equal("SEAL-9988", shipment.SealNumber);
        Assert.Equal("BOL-10023", shipment.BillOfLadingNumber);
        Assert.NotNull(shipment.DispatchedAtUtc);

        // Packages in shipment automatically marked Loaded
        Assert.Equal(PackageStatus.Loaded, pkg1.Status);
    }

    #endregion

    #region EF Core Persistence, Relationships & Multi-Tenancy Tests

    [Fact]
    public async Task Packing_EFCorePersistence_HandlingUnit1To1AndTenantIsolation_WorkCorrectly()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityId = Guid.NewGuid();

        var stationLocationId = Guid.NewGuid();
        var stagingLocationId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var toteId = Guid.NewGuid();
        var cartonHuId = Guid.NewGuid();
        var materialId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var labelId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientA, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientA, UserType = UserType.Client };

        // 1. Seed data
        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Packing Client A", AccountCode = "PCA" });
            seed.Clients.Add(new Client { Id = clientB, CompanyName = "Packing Client B", AccountCode = "PCB" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Columbus Logistics Hub", FacilityCode = "CMH-01" });

            seed.Locations.Add(new Location
            {
                Id = stationLocationId,
                FacilityId = facilityId,
                LocationCode = "PK-BENCH-01",
                Barcode = "LOC-PK-01",
                LocationType = LocationType.PackingStation
            });

            seed.Locations.Add(new Location
            {
                Id = stagingLocationId,
                FacilityId = facilityId,
                LocationCode = "STAGE-LANE-01",
                Barcode = "LOC-STG-01",
                LocationType = LocationType.StagingLane
            });

            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientA,
                Sku = "SKU-PACK-01",
                Description = "Item to be packed"
            });

            // Reusable pick tote
            seed.HandlingUnits.Add(new HandlingUnit
            {
                Id = toteId,
                ClientId = clientA,
                FacilityId = facilityId,
                LpnCode = "TOTE-99",
                UnitType = HandlingUnitType.Tote,
                Status = HandlingUnitStatus.AtPackingStation,
                CurrentLocationId = stationLocationId
            });

            // Specialized shipping carton handling unit (1:1 with Package)
            var cartonHu = new HandlingUnit
            {
                Id = cartonHuId,
                ClientId = clientA,
                FacilityId = facilityId,
                LpnCode = "BOX-LPN-001",
                UnitType = HandlingUnitType.ShippingCarton,
                Status = HandlingUnitStatus.InTransit,
                CurrentLocationId = stationLocationId
            };
            seed.HandlingUnits.Add(cartonHu);

            // Global packaging material (ClientId = null)
            var material = new PackagingMaterial
            {
                Id = materialId,
                FacilityId = facilityId,
                ClientId = null,
                Code = "BOX-MD",
                Name = "Medium Box",
                LengthCm = 35m,
                WidthCm = 25m,
                HeightCm = 20m,
                TareWeightKg = 0.40m,
                MaxWeightKg = 20m
            };
            seed.PackagingMaterials.Add(material);

            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = clientA,
                FacilityId = facilityId,
                OrderNumber = "ORD-PACK-01",
                CustomerName = "Retailer Co",
                Status = OutboundOrderStatus.InProgress
            };
            var orderLine = new OutboundOrderLine
            {
                Id = lineId,
                OutboundOrderId = orderId,
                ItemId = itemId,
                LineNumber = 1,
                QuantityOrdered = 10m,
                QuantityAllocated = 10m,
                Status = OutboundOrderLineStatus.Allocated
            };
            order.Lines.Add(orderLine);
            seed.OutboundOrders.Add(order);

            var allocation = new InventoryAllocation
            {
                Id = allocId,
                ClientId = clientA,
                FacilityId = facilityId,
                OutboundOrderId = orderId,
                OutboundOrderLineId = lineId,
                StockOnHandId = Guid.NewGuid(),
                ItemId = itemId,
                LocationId = stationLocationId,
                QuantityRequested = 10m,
                QuantityAllocated = 10m,
                QuantityPicked = 10m,
                Status = AllocationStatus.Picked
            };
            seed.InventoryAllocations.Add(allocation);

            // Packing Session
            var session = new PackingSession
            {
                Id = sessionId,
                ClientId = clientA,
                FacilityId = facilityId,
                PackingStationLocationId = stationLocationId,
                OutboundOrderId = orderId,
                OperatorUserId = Guid.NewGuid(),
                Status = PackingSessionStatus.Active
            };
            seed.PackingSessions.Add(session);

            // Shipment Header
            var shipment = new Shipment
            {
                Id = shipmentId,
                ClientId = clientA,
                FacilityId = facilityId,
                ShipmentNumber = "SHP-CMH-001",
                CarrierCode = "UPS",
                CarrierServiceLevel = CarrierServiceLevel.StandardGround,
                Status = ShipmentStatus.Draft,
                StagingLocationId = stagingLocationId
            };
            seed.Shipments.Add(shipment);

            // Package (1:1 with cartonHu)
            var package = new Package
            {
                Id = packageId,
                ClientId = clientA,
                FacilityId = facilityId,
                OutboundOrderId = orderId,
                ShipmentId = shipmentId,
                HandlingUnitId = cartonHuId,
                PackagingMaterialId = materialId,
                PackageNumber = "BOX-LPN-001",
                CartonSequence = 1,
                GrossWeightKg = 5.4m,
                TareWeightKg = 0.40m,
                NetWeightKg = 5.0m,
                LengthCm = 35m,
                WidthCm = 25m,
                HeightCm = 20m,
                VolumetricWeightKg = 3.5m,
                TrackingNumber = "1Z9999999999999999",
                CarrierCode = "UPS",
                Status = PackageStatus.LabelPrinted,
                CurrentLocationId = stationLocationId
            };

            var line = new PackageLine
            {
                PackageId = packageId,
                OutboundOrderLineId = lineId,
                ItemId = itemId,
                Quantity = 10m,
                SourceHandlingUnitId = toteId,
                InventoryAllocationId = allocId
            };
            package.Lines.Add(line);

            var label = new ShippingLabel
            {
                Id = labelId,
                PackageId = packageId,
                TrackingNumber = "1Z9999999999999999",
                CarrierCode = "UPS",
                LabelFormat = "ZPL",
                LabelData = "^XA^FDLabel Content^XZ",
                Status = ShippingLabelStatus.Printed
            };
            package.ShippingLabels.Add(label);

            seed.Packages.Add(package);

            // Client B Data to verify tenant isolation
            var packageClientB = new Package
            {
                ClientId = clientB,
                FacilityId = facilityId,
                OutboundOrderId = Guid.NewGuid(),
                HandlingUnitId = Guid.NewGuid(),
                PackageNumber = "PKG-CLB-99",
                Status = PackageStatus.Packed
            };
            seed.Packages.Add(packageClientB);

            await seed.SaveChangesAsync();
        }

        // 2. Query under Client A context (Tenant Filter Active)
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            // Verify Package retrieval & Tenant Isolation
            var packages = await context.Packages
                .Include(p => p.HandlingUnit)
                .Include(p => p.Lines)
                .Include(p => p.ShippingLabels)
                .Include(p => p.PackagingMaterial)
                .ToListAsync();

            Assert.Single(packages); // Client B package must be excluded by global query filter
            var pkg = packages[0];
            Assert.Equal("BOX-LPN-001", pkg.PackageNumber);
            Assert.Equal(PackageStatus.LabelPrinted, pkg.Status);

            // Verify 1:1 HandlingUnit mapping
            Assert.NotNull(pkg.HandlingUnit);
            Assert.Equal(cartonHuId, pkg.HandlingUnit.Id);
            Assert.Equal(HandlingUnitType.ShippingCarton, pkg.HandlingUnit.UnitType);

            // Verify PackageLine details & traceability
            Assert.Single(pkg.Lines);
            var pkgLine = pkg.Lines.First();
            Assert.Equal(10m, pkgLine.Quantity);
            Assert.Equal(allocId, pkgLine.InventoryAllocationId);
            Assert.Equal(toteId, pkgLine.SourceHandlingUnitId);

            // Verify ShippingLabel details
            Assert.Single(pkg.ShippingLabels);
            Assert.Equal("1Z9999999999999999", pkg.ShippingLabels.First().TrackingNumber);

            // Verify PackingSession retrieval
            var sessions = await context.PackingSessions.ToListAsync();
            Assert.Single(sessions);
            Assert.Equal(PackingSessionStatus.Active, sessions[0].Status);

            // Verify Shipment retrieval
            var shipments = await context.Shipments.ToListAsync();
            Assert.Single(shipments);
            Assert.Equal("SHP-CMH-001", shipments[0].ShipmentNumber);

            // Verify PackagingMaterial retrieval (global material is visible)
            var material = await context.PackagingMaterials.FirstOrDefaultAsync(m => m.Id == materialId);
            Assert.NotNull(material);
            Assert.Null(material.ClientId);
            Assert.Equal("BOX-MD", material.Code);
        }

        // 3. Verify Soft Deletion on Package and Shipment
        using (var deleteContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var packageToDelete = await deleteContext.Packages.FirstAsync(p => p.Id == packageId);
            var shipmentToDelete = await deleteContext.Shipments.FirstAsync(s => s.Id == shipmentId);

            deleteContext.Packages.Remove(packageToDelete);
            deleteContext.Shipments.Remove(shipmentToDelete);
            await deleteContext.SaveChangesAsync();
        }

        using (var verifyContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            // Standard query filter hides soft-deleted records
            Assert.Null(await verifyContext.Packages.FirstOrDefaultAsync(p => p.Id == packageId));
            Assert.Null(await verifyContext.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId));

            // Raw query confirms soft deletion
            var rawPkg = await verifyContext.Packages.IgnoreQueryFilters().FirstAsync(p => p.Id == packageId);
            Assert.True(rawPkg.IsDeleted);
            Assert.NotNull(rawPkg.DeletedAt);

            var rawShp = await verifyContext.Shipments.IgnoreQueryFilters().FirstAsync(s => s.Id == shipmentId);
            Assert.True(rawShp.IsDeleted);
            Assert.NotNull(rawShp.DeletedAt);

            // HandlingUnit and OutboundOrder remain untouched (DeleteBehavior.Restrict)
            Assert.NotNull(await verifyContext.HandlingUnits.FindAsync(cartonHuId));
            Assert.NotNull(await verifyContext.OutboundOrders.FindAsync(orderId));
        }
    }

    #endregion
}
