using System.Text.Json;
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

public class ShipmentConsolidationAndDispatchTests
{
    private class TestContextFixture
    {
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid FacilityId { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; } = Guid.NewGuid();
        public Guid PackingStationId { get; set; } = Guid.NewGuid();
        public Guid StagingLaneId { get; set; } = Guid.NewGuid();
        public Guid DockDoorId { get; set; } = Guid.NewGuid();
        public Guid StorageBinId { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; } = Guid.NewGuid();
        public Guid MaterialId { get; set; } = Guid.NewGuid();
        public string DbName { get; set; } = Guid.NewGuid().ToString();
    }

    private async Task<TestContextFixture> SeedTestEnvironmentAsync(string? dbName = null)
    {
        var fix = new TestContextFixture();
        if (dbName != null) fix.DbName = dbName;

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        context.Clients.Add(new Client
        {
            Id = fix.ClientId,
            CompanyName = "Global Apex Logistics Client",
            AccountCode = "GALC"
        });

        context.Facilities.Add(new Facility
        {
            Id = fix.FacilityId,
            Name = "Apex Central Gateway Hub",
            FacilityCode = "APX-HUB-01",
            AddressLine1 = "100 Gateway Pkwy",
            City = "Columbus",
            StateOrProvince = "OH",
            PostalCode = "43215",
            CountryCode = "USA"
        });

        context.Locations.AddRange(
            new Location
            {
                Id = fix.PackingStationId,
                FacilityId = fix.FacilityId,
                LocationCode = "PACK-STN-01",
                Barcode = "LOC-PACK-01",
                LocationType = LocationType.PackingStation
            },
            new Location
            {
                Id = fix.StagingLaneId,
                FacilityId = fix.FacilityId,
                LocationCode = "STAGE-LANE-01",
                Barcode = "LOC-STAGE-01",
                LocationType = LocationType.StagingLane
            },
            new Location
            {
                Id = fix.DockDoorId,
                FacilityId = fix.FacilityId,
                LocationCode = "DOCK-DOOR-04",
                Barcode = "LOC-DOCK-04",
                LocationType = LocationType.DockDoor
            },
            new Location
            {
                Id = fix.StorageBinId,
                FacilityId = fix.FacilityId,
                LocationCode = "AISLE-01-BIN-01",
                Barcode = "LOC-BIN-01",
                LocationType = LocationType.Bin
            }
        );

        context.PackagingMaterials.Add(new PackagingMaterial
        {
            Id = fix.MaterialId,
            FacilityId = fix.FacilityId,
            Code = "BOX-LG",
            Name = "Large Shipping Box",
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 20m,
            TareWeightKg = 0.5m,
            MaxWeightKg = 30m,
            IsActive = true
        });

        context.Items.Add(new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-DISPATCH-01",
            Description = "Apex Industrial Sensor Unit",
            WeightKg = 2.0m,
            LengthCm = 15m,
            WidthCm = 10m,
            HeightCm = 8m
        });

        // Add physical stock on hand to verify zero mutation
        context.StockOnHand.Add(new StockOnHand
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            LocationId = fix.StorageBinId,
            ItemId = fix.ItemId,
            QtyAvailable = 90m,
            QtyAllocated = 10m
        });

        await context.SaveChangesAsync();
        return fix;
    }

    private async Task<(Package Package, OutboundOrder Order)> CreateTestPackageAsync(
        TestContextFixture fix,
        string packageSuffix,
        string carrierCode = "SIM-FEDEX",
        PackageStatus status = PackageStatus.LabelPrinted,
        bool withActiveLabel = true,
        Guid? clientIdOverride = null,
        Guid? facilityIdOverride = null,
        Guid? orderIdOverride = null)
    {
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var clientId = clientIdOverride ?? fix.ClientId;
        var facilityId = facilityIdOverride ?? fix.FacilityId;
        var orderId = orderIdOverride ?? Guid.NewGuid();

        var existingOrder = await context.OutboundOrders.FindAsync(orderId);
        if (existingOrder == null)
        {
            existingOrder = new OutboundOrder
            {
                Id = orderId,
                ClientId = clientId,
                FacilityId = facilityId,
                OrderNumber = $"ORD-DISP-{packageSuffix}",
                OrderType = OutboundOrderType.EcommerceFulfillment,
                Status = OutboundOrderStatus.InProgress,
                ShippingAddress = new ShippingAddress
                {
                    OutboundOrderId = orderId,
                    RecipientName = "Acme Distribution Corp",
                    AddressLine1 = "500 Logistics Way",
                    City = "Chicago",
                    StateOrProvince = "IL",
                    PostalCode = "60607",
                    CountryCode = "USA",
                    PhoneNumber = "312-555-0144"
                }
            };
            context.OutboundOrders.Add(existingOrder);
        }

        var huId = Guid.NewGuid();
        var hu = new HandlingUnit
        {
            Id = huId,
            ClientId = clientId,
            FacilityId = facilityId,
            LpnCode = $"HU-CTN-{packageSuffix}",
            UnitType = HandlingUnitType.ShippingCarton,
            Status = HandlingUnitStatus.InUse,
            CurrentLocationId = fix.PackingStationId,
            CurrentOutboundOrderId = orderId
        };
        context.HandlingUnits.Add(hu);

        var packageId = Guid.NewGuid();
        var package = new Package
        {
            Id = packageId,
            ClientId = clientId,
            FacilityId = facilityId,
            OutboundOrderId = orderId,
            HandlingUnitId = huId,
            PackagingMaterialId = fix.MaterialId,
            PackageNumber = $"PKG-{packageSuffix}",
            CartonSequence = 1,
            GrossWeightKg = 5.0m,
            TareWeightKg = 0.5m,
            NetWeightKg = 4.5m,
            LengthCm = 40m,
            WidthCm = 30m,
            HeightCm = 20m,
            CurrentLocationId = fix.PackingStationId,
            Status = status,
            CarrierCode = carrierCode,
            CarrierServiceCode = CarrierServiceLevel.StandardGround.ToString(),
            TrackingNumber = withActiveLabel ? $"TRK-{carrierCode}-{packageSuffix}" : null
        };
        package.RecalculateVolumetricWeight();
        context.Packages.Add(package);

        if (withActiveLabel)
        {
            context.ShippingLabels.Add(new ShippingLabel
            {
                Id = Guid.NewGuid(),
                PackageId = packageId,
                CarrierCode = carrierCode,
                ServiceLevel = CarrierServiceLevel.StandardGround,
                TrackingNumber = package.TrackingNumber!,
                LabelFormat = "ZPL",
                LabelData = "^XA^FO50,50^FDApex Parcel^FS^XZ",
                Status = ShippingLabelStatus.Generated,
                GeneratedAtUtc = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return (package, existingOrder);
    }

    #region 1. Consolidation & Compatibility Guards

    [Fact]
    public async Task ConsolidateShipment_MultiCarton_CalculatesTotalsAndAssociatesPackages()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg1, _) = await CreateTestPackageAsync(fix, "01", "SIM-FEDEX");
        var (pkg2, _) = await CreateTestPackageAsync(fix, "02", "SIM-FEDEX");

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        // 1. Create Shipment
        var createResult = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            ServiceLevel = CarrierServiceLevel.StandardGround,
            UserId = fix.UserId
        });

        Assert.True(createResult.IsSuccess);
        Assert.NotEqual(Guid.Empty, createResult.ShipmentId);

        // 2. Add Package 1
        var addResult1 = await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
        {
            ShipmentId = createResult.ShipmentId,
            PackageId = pkg1.Id,
            UserId = fix.UserId
        });
        Assert.True(addResult1.IsSuccess);
        Assert.Equal(1, addResult1.TotalPackagesCount);
        Assert.Equal(5.0m, addResult1.TotalGrossWeightKg);

        // 3. Add Package 2
        var addResult2 = await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
        {
            ShipmentId = createResult.ShipmentId,
            PackageId = pkg2.Id,
            UserId = fix.UserId
        });
        Assert.True(addResult2.IsSuccess);
        Assert.Equal(2, addResult2.TotalPackagesCount);
        Assert.Equal(10.0m, addResult2.TotalGrossWeightKg);

        // Verify DB State
        var shipmentInDb = await context.Shipments
            .Include(s => s.Packages)
            .FirstAsync(s => s.Id == createResult.ShipmentId);

        Assert.Equal(2, shipmentInDb.TotalPackagesCount);
        Assert.Equal(10.0m, shipmentInDb.TotalWeightKg);
        Assert.Equal(48000m, shipmentInDb.TotalVolumeCm3); // 2 * (40 * 30 * 20)
        Assert.All(shipmentInDb.Packages, p => Assert.Equal(shipmentInDb.Id, p.ShipmentId));

        // Verify Audit Logs
        var auditLogs = await context.ShipmentAuditLogs
            .Where(a => a.ShipmentId == createResult.ShipmentId)
            .ToListAsync();
        Assert.Contains(auditLogs, a => a.Action == "ShipmentCreated");
        Assert.Equal(2, auditLogs.Count(a => a.Action == "PackageAdded"));
    }

    [Fact]
    public async Task ConsolidateShipment_RejectsPackage_FromDifferentTenant()
    {
        var fix = await SeedTestEnvironmentAsync();
        var clientB = Guid.NewGuid();
        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            seed.Clients.Add(new Client { Id = clientB, CompanyName = "Tenant B Inc", AccountCode = "TB" });
            await seed.SaveChangesAsync();
        }

        var (pkgTenantB, _) = await CreateTestPackageAsync(fix, "TENANT-B", "SIM-FEDEX", clientIdOverride: clientB);

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);
        var createResult = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId, // Tenant A
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
            {
                ShipmentId = createResult.ShipmentId,
                PackageId = pkgTenantB.Id,
                UserId = fix.UserId
            }));

        Assert.Contains("different client tenant", ex.Message);
    }

    [Fact]
    public async Task ConsolidateShipment_RejectsPackage_FromDifferentFacility()
    {
        var fix = await SeedTestEnvironmentAsync();
        var facilityB = Guid.NewGuid();
        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, fix.DbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityB, Name = "Facility East", FacilityCode = "FAC-E" });
            await seed.SaveChangesAsync();
        }

        var (pkgFacilityB, _) = await CreateTestPackageAsync(fix, "FAC-B", "SIM-FEDEX", facilityIdOverride: facilityB);

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);
        var createResult = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId, // Facility A
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
            {
                ShipmentId = createResult.ShipmentId,
                PackageId = pkgFacilityB.Id,
                UserId = fix.UserId
            }));

        Assert.Contains("different facility", ex.Message);
    }

    [Fact]
    public async Task ConsolidateShipment_RejectsPackage_WithMissingOrVoidedShippingLabel()
    {
        var fix = await SeedTestEnvironmentAsync();
        // Unlabeled package (no active label)
        var (pkgNoLabel, _) = await CreateTestPackageAsync(fix, "NOLABEL", "SIM-FEDEX", withActiveLabel: false);

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);
        var createResult = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
            {
                ShipmentId = createResult.ShipmentId,
                PackageId = pkgNoLabel.Id,
                UserId = fix.UserId
            }));

        Assert.Contains("valid, active shipping label", ex.Message);
    }

    [Fact]
    public async Task ConsolidateShipment_RejectsPackage_WithIncompatibleCarrier()
    {
        var fix = await SeedTestEnvironmentAsync();
        // Package labeled for SIM-UPS
        var (pkgUps, _) = await CreateTestPackageAsync(fix, "UPS", "SIM-UPS");

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);
        // Shipment created for SIM-FEDEX
        var createResult = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
            {
                ShipmentId = createResult.ShipmentId,
                PackageId = pkgUps.Id,
                UserId = fix.UserId
            }));

        Assert.Contains("incompatible with shipment carrier", ex.Message);
    }

    [Fact]
    public async Task ConsolidateShipment_RejectsPackage_AlreadyAssignedToAnotherActiveShipment()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg, _) = await CreateTestPackageAsync(fix, "DUP-MEM", "SIM-FEDEX");

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var shpA = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });

        var shpB = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });

        // Add to Shipment A
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
        {
            ShipmentId = shpA.ShipmentId,
            PackageId = pkg.Id,
            UserId = fix.UserId
        });

        // Attempting to add to Shipment B must fail
        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
            {
                ShipmentId = shpB.ShipmentId,
                PackageId = pkg.Id,
                UserId = fix.UserId
            }));

        Assert.Contains("Duplicate shipment membership rejected", ex.Message);
    }

    [Fact]
    public async Task RemovePackage_UpdatesTotals_AndRecordsMandatoryAuditReason()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg1, _) = await CreateTestPackageAsync(fix, "RM-01", "SIM-FEDEX");
        var (pkg2, _) = await CreateTestPackageAsync(fix, "RM-02", "SIM-FEDEX");

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });

        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg1.Id, UserId = fix.UserId });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg2.Id, UserId = fix.UserId });

        // Removal without reason must be rejected
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RemovePackageFromShipmentAsync(new RemovePackageFromShipmentRequest
            {
                ShipmentId = shp.ShipmentId,
                PackageId = pkg1.Id,
                Reason = "   ",
                UserId = fix.UserId
            }));

        // Removal with reason succeeds
        const string reason = "Customer requested next-day air upgrade via alternate flight";
        var removeResult = await service.RemovePackageFromShipmentAsync(new RemovePackageFromShipmentRequest
        {
            ShipmentId = shp.ShipmentId,
            PackageId = pkg1.Id,
            Reason = reason,
            UserId = fix.UserId
        });

        Assert.True(removeResult.IsSuccess);
        Assert.Equal(1, removeResult.TotalPackagesCount);

        // Verify package decoupled
        var pkgInDb = await context.Packages.FindAsync(pkg1.Id);
        Assert.Null(pkgInDb!.ShipmentId);

        // Verify audit log has reason
        var audit = await context.ShipmentAuditLogs
            .FirstOrDefaultAsync(a => a.ShipmentId == shp.ShipmentId && a.Action == "PackageRemoved");
        Assert.NotNull(audit);
        Assert.Contains(reason, audit.Details);
    }

    #endregion

    #region 2. Dock Staging & Trailer Loading Workflow

    [Fact]
    public async Task StagePackage_ValidLocation_UpdatesPackageStatusToStaged()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg, _) = await CreateTestPackageAsync(fix, "STG-01", "SIM-FEDEX", PackageStatus.LabelPrinted);

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var stageResult = await service.StagePackageAsync(new StagePackageRequest
        {
            PackageId = pkg.Id,
            StagingLocationId = fix.StagingLaneId,
            UserId = fix.UserId
        });

        Assert.True(stageResult.IsSuccess);
        Assert.Equal(PackageStatus.Staged, stageResult.PackageStatus);
        Assert.Equal("STAGE-LANE-01", stageResult.StagingLocationCode);

        var pkgInDb = await context.Packages
            .Include(p => p.HandlingUnit)
            .FirstAsync(p => p.Id == pkg.Id);

        Assert.Equal(PackageStatus.Staged, pkgInDb.Status);
        Assert.Equal(fix.StagingLaneId, pkgInDb.CurrentLocationId);
        Assert.Equal(fix.StagingLaneId, pkgInDb.HandlingUnit.CurrentLocationId);
    }

    [Fact]
    public async Task StagePackage_DockDoorLocation_UpdatesPackageStatusToStaged()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg, _) = await CreateTestPackageAsync(fix, "STG-DOCK", "SIM-FEDEX", PackageStatus.LabelPrinted);

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var stageResult = await service.StagePackageAsync(new StagePackageRequest
        {
            PackageId = pkg.Id,
            StagingLocationId = fix.DockDoorId,
            UserId = fix.UserId
        });

        Assert.True(stageResult.IsSuccess);
        Assert.Equal(PackageStatus.Staged, stageResult.PackageStatus);
        Assert.Equal("DOCK-DOOR-04", stageResult.StagingLocationCode);
    }

    [Fact]
    public async Task StagePackage_RejectsInvalidLocationType()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg, _) = await CreateTestPackageAsync(fix, "STG-INV", "SIM-FEDEX", PackageStatus.LabelPrinted);

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        // Staging into a storage bin must be rejected
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.StagePackageAsync(new StagePackageRequest
            {
                PackageId = pkg.Id,
                StagingLocationId = fix.StorageBinId,
                UserId = fix.UserId
            }));

        Assert.Contains("Staging is only permitted in StagingLane or DockDoor", ex.Message);
    }

    [Fact]
    public async Task LoadPackage_FromStagedStatus_UpdatesPackageStatusToLoaded()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg, _) = await CreateTestPackageAsync(fix, "LOAD-01", "SIM-FEDEX", PackageStatus.LabelPrinted);

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        // 1. Create shipment and add package
        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
        {
            ShipmentId = shp.ShipmentId,
            PackageId = pkg.Id,
            UserId = fix.UserId
        });

        // 2. Stage package
        await service.StagePackageAsync(new StagePackageRequest
        {
            PackageId = pkg.Id,
            StagingLocationId = fix.StagingLaneId,
            UserId = fix.UserId
        });

        // 3. Load package onto trailer
        var loadResult = await service.LoadPackageAsync(new LoadPackageRequest
        {
            PackageId = pkg.Id,
            TrailerNumber = "TR-9988",
            UserId = fix.UserId
        });

        Assert.True(loadResult.IsSuccess);
        Assert.Equal(PackageStatus.Loaded, loadResult.PackageStatus);
        Assert.Equal(ShipmentStatus.ReadyForDispatch, loadResult.ShipmentStatus);

        var pkgInDb = await context.Packages.FindAsync(pkg.Id);
        Assert.Equal(PackageStatus.Loaded, pkgInDb!.Status);
    }

    [Fact]
    public async Task LoadPackage_RejectsUnstagedPackage()
    {
        var fix = await SeedTestEnvironmentAsync();
        // Package is LabelPrinted, but has NOT been staged
        var (pkg, _) = await CreateTestPackageAsync(fix, "UNSTAGED", "SIM-FEDEX", PackageStatus.LabelPrinted);

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest
        {
            ShipmentId = shp.ShipmentId,
            PackageId = pkg.Id,
            UserId = fix.UserId
        });

        // Attempting to load unstaged package must throw
        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            service.LoadPackageAsync(new LoadPackageRequest
            {
                PackageId = pkg.Id,
                UserId = fix.UserId
            }));

        Assert.Contains("Package must be Staged at dock staging lane before loading onto trailer", ex.Message);
    }

    [Fact]
    public async Task LoadPackage_IdempotentDuplicateScan_SucceedsWithoutError()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg, _) = await CreateTestPackageAsync(fix, "IDEMP-LOAD", "SIM-FEDEX", PackageStatus.LabelPrinted);

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg.Id, UserId = fix.UserId });
        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkg.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });

        // First scan
        var res1 = await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkg.Id, UserId = fix.UserId });
        Assert.True(res1.IsSuccess);
        Assert.Equal(PackageStatus.Loaded, res1.PackageStatus);

        // Duplicate scan: Idempotent return
        var res2 = await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkg.Id, UserId = fix.UserId });
        Assert.True(res2.IsSuccess);
        Assert.Equal(PackageStatus.Loaded, res2.PackageStatus);
        Assert.Contains("Idempotent", res2.Message);
    }

    #endregion

    #region 3. Bill of Lading (BOL) & Trailer Manifest

    [Fact]
    public async Task GenerateBillOfLading_CreatesImmutableDocumentSnapshot()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg1, _) = await CreateTestPackageAsync(fix, "BOL-01", "SIM-FEDEX");
        var (pkg2, _) = await CreateTestPackageAsync(fix, "BOL-02", "SIM-FEDEX");

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg1.Id, UserId = fix.UserId });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg2.Id, UserId = fix.UserId });

        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkg1.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });
        await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkg1.Id, UserId = fix.UserId });
        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkg2.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });
        await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkg2.Id, UserId = fix.UserId });

        var bolResult = await service.GenerateBillOfLadingAsync(new GenerateBolRequest
        {
            ShipmentId = shp.ShipmentId,
            TrailerNumber = "TR-FEDEX-44",
            SealNumber = "SL-998811",
            SpecialInstructions = "Maintain upright. Do not double stack.",
            UserId = fix.UserId
        });

        Assert.True(bolResult.IsSuccess);
        Assert.NotEqual(Guid.Empty, bolResult.BolId);
        Assert.StartsWith("BOL-", bolResult.BolNumber);
        Assert.Equal(2, bolResult.TotalPackageCount);
        Assert.Equal(10.0m, bolResult.TotalGrossWeightKg);
        Assert.NotEmpty(bolResult.DocumentSnapshotJson);

        // Verify JSON contents
        using var jsonDoc = JsonDocument.Parse(bolResult.DocumentSnapshotJson);
        var root = jsonDoc.RootElement;
        Assert.Equal(2, root.GetProperty("TotalPackages").GetInt32());
        Assert.Equal(10.0m, root.GetProperty("TotalWeightKg").GetDecimal());
        Assert.Equal("TR-FEDEX-44", root.GetProperty("TrailerNumber").GetString());
        Assert.Equal("SL-998811", root.GetProperty("SealNumber").GetString());

        // Verify Shipment linked to BOL
        var shipmentInDb = await context.Shipments.FindAsync(shp.ShipmentId);
        Assert.Equal(bolResult.BolNumber, shipmentInDb!.BillOfLadingNumber);
    }

    [Fact]
    public async Task GenerateBillOfLading_RejectsEmptyShipment()
    {
        var fix = await SeedTestEnvironmentAsync();

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });

        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            service.GenerateBillOfLadingAsync(new GenerateBolRequest
            {
                ShipmentId = shp.ShipmentId,
                UserId = fix.UserId
            }));

        Assert.Contains("Cannot generate Bill of Lading for empty shipment", ex.Message);
    }

    [Fact]
    public async Task GenerateBillOfLading_PreservesExistingIssuedSnapshot_OnSubsequentCalls()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg1, _) = await CreateTestPackageAsync(fix, "BOL-SNAP", "SIM-FEDEX");

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg1.Id, UserId = fix.UserId });
        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkg1.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });
        await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkg1.Id, UserId = fix.UserId });

        // Initial Generation
        var bol1 = await service.GenerateBillOfLadingAsync(new GenerateBolRequest
        {
            ShipmentId = shp.ShipmentId,
            TrailerNumber = "TR-INIT",
            UserId = fix.UserId
        });

        // Repeat call
        var bol2 = await service.GenerateBillOfLadingAsync(new GenerateBolRequest
        {
            ShipmentId = shp.ShipmentId,
            TrailerNumber = "TR-CHANGED", // Repeat call should preserve original snapshot
            UserId = fix.UserId
        });

        Assert.Equal(bol1.BolId, bol2.BolId);
        Assert.Equal(bol1.BolNumber, bol2.BolNumber);
        Assert.Equal(bol1.DocumentSnapshotJson, bol2.DocumentSnapshotJson);
        Assert.Contains("Preserved Snapshot", bol2.Message);

        // Verify only 1 BOL in DB
        var count = await context.BillsOfLading.CountAsync(b => b.ShipmentId == shp.ShipmentId);
        Assert.Equal(1, count);
    }

    #endregion

    #region 4. Authorized Custody Transfer & Dispatch

    [Fact]
    public async Task ConfirmDispatch_RejectsWhenPackagesNotLoaded()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg1, _) = await CreateTestPackageAsync(fix, "DISP-L1", "SIM-FEDEX");
        var (pkg2, _) = await CreateTestPackageAsync(fix, "DISP-L2", "SIM-FEDEX");

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg1.Id, UserId = fix.UserId });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg2.Id, UserId = fix.UserId });

        // Stage and Load ONLY package 1
        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkg1.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });
        await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkg1.Id, UserId = fix.UserId });

        // Stage package 2 (but do NOT load it)
        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkg2.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });

        // Dispatch must be rejected because package 2 is not Loaded
        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            service.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
            {
                ShipmentId = shp.ShipmentId,
                DriverName = "John Doe",
                DriverSignature = "J.Doe",
                AuthorizedUserId = fix.UserId
            }));

        Assert.Contains("All packages must be confirmed Loaded onto trailer before custody transfer", ex.Message);
        Assert.Contains(pkg2.PackageNumber, ex.Message);
    }

    [Fact]
    public async Task ConfirmDispatch_RejectsUnauthorizedUser()
    {
        var fix = await SeedTestEnvironmentAsync();

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
            {
                ShipmentId = Guid.NewGuid(),
                DriverName = "John Doe",
                DriverSignature = "J.Doe",
                AuthorizedUserId = Guid.Empty // Invalid authorized user
            }));

        Assert.Contains("authorized user ID must be provided", ex.Message);
    }

    [Fact]
    public async Task ConfirmDispatch_AuthorizesDriverHandover_AndRecordsCustodyTransfer()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg1, _) = await CreateTestPackageAsync(fix, "CUST-01", "SIM-FEDEX");
        var (pkg2, _) = await CreateTestPackageAsync(fix, "CUST-02", "SIM-FEDEX");

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        // 1. Setup shipment and load both packages
        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg1.Id, UserId = fix.UserId });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg2.Id, UserId = fix.UserId });

        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkg1.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });
        await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkg1.Id, UserId = fix.UserId });

        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkg2.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });
        await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkg2.Id, UserId = fix.UserId });

        // Generate BOL
        var bolResult = await service.GenerateBillOfLadingAsync(new GenerateBolRequest
        {
            ShipmentId = shp.ShipmentId,
            TrailerNumber = "TR-FDX-777",
            SealNumber = "SEAL-889900",
            UserId = fix.UserId
        });

        // 2. Authorize Driver Custody Transfer & Dispatch
        var dispatchResult = await service.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
        {
            ShipmentId = shp.ShipmentId,
            DriverName = "Michael Vance",
            DriverLicenseNumber = "CDL-TX-784920",
            DriverSignature = "M. Vance",
            TrailerNumber = "TR-FDX-777",
            SealNumber = "SEAL-889900",
            AuthorizedUserId = fix.UserId,
            Notes = "Driver verified trailer seal intact and signed BOL"
        });

        Assert.True(dispatchResult.IsSuccess);
        Assert.Equal(ShipmentStatus.Dispatched, dispatchResult.Status);
        Assert.Equal(2, dispatchResult.TotalPackagesDispatched);
        Assert.Equal(10.0m, dispatchResult.TotalWeightKg);
        Assert.Equal("Michael Vance", dispatchResult.DriverName);
        Assert.Equal(bolResult.BolNumber, dispatchResult.BillOfLadingNumber);

        // 3. Verify Custody Transfer record in DB
        var custodyInDb = await context.ShipmentCustodyTransfers
            .FirstOrDefaultAsync(c => c.ShipmentId == shp.ShipmentId);
        Assert.NotNull(custodyInDb);
        Assert.Equal("Michael Vance", custodyInDb.DriverName);
        Assert.Equal("CDL-TX-784920", custodyInDb.DriverLicenseNumber);
        Assert.Equal("M. Vance", custodyInDb.DriverSignatureAcknowledgment);
        Assert.Equal("TR-FDX-777", custodyInDb.TrailerNumber);
        Assert.Equal("SEAL-889900", custodyInDb.SealNumber);
        Assert.Equal(fix.UserId, custodyInDb.AuthorizedByUserId);

        // 4. Verify Shipment State
        var shipmentInDb = await context.Shipments.FindAsync(shp.ShipmentId);
        Assert.Equal(ShipmentStatus.Dispatched, shipmentInDb!.Status);
        Assert.NotNull(shipmentInDb.DispatchedAtUtc);
    }

    [Fact]
    public async Task ZeroStockBalanceMutation_DuringShippingAndDispatch()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg1, _) = await CreateTestPackageAsync(fix, "ZERO-MUT", "SIM-FEDEX");

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        // Capture initial stock balance before shipping operations
        var initialStock = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        decimal initialTotal = initialStock.TotalQuantity;
        decimal initialAllocated = initialStock.QtyAllocated;
        decimal initialAvailable = initialStock.QtyAvailable;
        int initialTxCount = await context.InventoryTransactions.CountAsync();

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        // 1. Create shipment
        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });

        // 2. Consolidate package
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg1.Id, UserId = fix.UserId });

        // 3. Stage package
        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkg1.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });

        // 4. Load package
        await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkg1.Id, UserId = fix.UserId });

        // 5. Generate BOL
        await service.GenerateBillOfLadingAsync(new GenerateBolRequest { ShipmentId = shp.ShipmentId, UserId = fix.UserId });

        // 6. Confirm Dispatch
        await service.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
        {
            ShipmentId = shp.ShipmentId,
            DriverName = "Arturo Reed",
            DriverSignature = "A.Reed",
            AuthorizedUserId = fix.UserId
        });

        // Verify that StockOnHand was NEVER mutated (Zero mutation invariant: ΔQ = 0)
        var finalStock = await context.StockOnHand.FirstAsync(s => s.ItemId == fix.ItemId);
        Assert.Equal(initialTotal, finalStock.TotalQuantity);
        Assert.Equal(initialAllocated, finalStock.QtyAllocated);
        Assert.Equal(initialAvailable, finalStock.QtyAvailable);

        // Verify zero additional ledger transactions created during dispatch
        int finalTxCount = await context.InventoryTransactions.CountAsync();
        Assert.Equal(initialTxCount, finalTxCount);
    }

    [Fact]
    public async Task PartialOrderShipment_OrderRemainsInProgress_UntilAllPackagesDispatched()
    {
        var fix = await SeedTestEnvironmentAsync();
        var sharedOrderId = Guid.NewGuid();

        // Create 2 packages for the same outbound order
        var (pkgA, order) = await CreateTestPackageAsync(fix, "PART-A", "SIM-FEDEX", orderIdOverride: sharedOrderId);
        var (pkgB, _) = await CreateTestPackageAsync(fix, "PART-B", "SIM-FEDEX", orderIdOverride: sharedOrderId);

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        // Shipment 1: Only Package A
        var shp1 = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp1.ShipmentId, PackageId = pkgA.Id, UserId = fix.UserId });
        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkgA.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });
        await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkgA.Id, UserId = fix.UserId });
        await service.GenerateBillOfLadingAsync(new GenerateBolRequest { ShipmentId = shp1.ShipmentId, UserId = fix.UserId });

        // Shipment 2: Only Package B
        var shp2 = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp2.ShipmentId, PackageId = pkgB.Id, UserId = fix.UserId });
        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkgB.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });
        await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkgB.Id, UserId = fix.UserId });
        await service.GenerateBillOfLadingAsync(new GenerateBolRequest { ShipmentId = shp2.ShipmentId, UserId = fix.UserId });

        // Dispatch Shipment 1 (Partial fulfillment)
        var disp1 = await service.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
        {
            ShipmentId = shp1.ShipmentId,
            DriverName = "Driver 1",
            DriverSignature = "D1",
            AuthorizedUserId = fix.UserId
        });

        // Outbound order must STILL be InProgress!
        var orderAfterDisp1 = await context.OutboundOrders.FindAsync(sharedOrderId);
        Assert.Equal(OutboundOrderStatus.InProgress, orderAfterDisp1!.Status);
        Assert.DoesNotContain(sharedOrderId, disp1.CompletedOrderIds);

        // Dispatch Shipment 2 (Final package dispatched)
        var disp2 = await service.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
        {
            ShipmentId = shp2.ShipmentId,
            DriverName = "Driver 2",
            DriverSignature = "D2",
            AuthorizedUserId = fix.UserId
        });

        // Now Outbound order MUST be Completed!
        var orderAfterDisp2 = await context.OutboundOrders.FindAsync(sharedOrderId);
        Assert.Equal(OutboundOrderStatus.Completed, orderAfterDisp2!.Status);
        Assert.Contains(sharedOrderId, disp2.CompletedOrderIds);
    }

    [Fact]
    public async Task ConfirmDispatch_IdempotentRetry_ReturnsExistingState()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (pkg, _) = await CreateTestPackageAsync(fix, "IDEMP-DISP", "SIM-FEDEX");

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var service = new ShipmentConsolidationService(context, NullLogger<ShipmentConsolidationService>.Instance);

        var shp = await service.CreateShipmentAsync(new CreateShipmentRequest
        {
            ClientId = fix.ClientId,
            FacilityId = fix.FacilityId,
            CarrierCode = "SIM-FEDEX",
            UserId = fix.UserId
        });
        await service.AddPackageToShipmentAsync(new AddPackageToShipmentRequest { ShipmentId = shp.ShipmentId, PackageId = pkg.Id, UserId = fix.UserId });
        await service.StagePackageAsync(new StagePackageRequest { PackageId = pkg.Id, StagingLocationId = fix.StagingLaneId, UserId = fix.UserId });
        await service.LoadPackageAsync(new LoadPackageRequest { PackageId = pkg.Id, UserId = fix.UserId });
        await service.GenerateBillOfLadingAsync(new GenerateBolRequest { ShipmentId = shp.ShipmentId, UserId = fix.UserId });

        // First dispatch
        var res1 = await service.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
        {
            ShipmentId = shp.ShipmentId,
            DriverName = "Marcus Brody",
            DriverSignature = "M.Brody",
            IdempotencyKey = "disp-unit-idemp-1",
            AuthorizedUserId = fix.UserId
        });
        Assert.True(res1.IsSuccess);
        Assert.Equal(ShipmentStatus.Dispatched, res1.Status);

        // Second dispatch (identical idempotent retry)
        var res2 = await service.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
        {
            ShipmentId = shp.ShipmentId,
            DriverName = "Marcus Brody",
            DriverSignature = "M.Brody",
            IdempotencyKey = "disp-unit-idemp-1",
            AuthorizedUserId = fix.UserId
        });
        Assert.True(res2.IsSuccess);
        Assert.Equal(ShipmentStatus.Dispatched, res2.Status);
        Assert.Contains("Idempotent", res2.Message);

        // Conflicting driver retry must throw ConcurrencyException (409 Conflict)
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            service.ConfirmDispatchAsync(new ConfirmDispatchCustodyRequest
            {
                ShipmentId = shp.ShipmentId,
                DriverName = "Impostor Driver",
                DriverSignature = "I.Driver",
                IdempotencyKey = "disp-unit-idemp-1",
                AuthorizedUserId = fix.UserId
            }));
    }

    #endregion
}
