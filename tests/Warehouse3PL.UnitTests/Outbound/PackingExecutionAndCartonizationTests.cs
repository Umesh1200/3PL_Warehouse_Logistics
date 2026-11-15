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
using Warehouse3PL.Infrastructure.Services.Packing;
using Warehouse3PL.UnitTests.Common;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Outbound;

public class PackingExecutionAndCartonizationTests
{
    #region Cartonization Tests

    [Fact]
    public async Task Cartonization_RecommendsSmallestCompatibleCarton()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CA" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Main Facility", FacilityCode = "FAC-01" });

            // Small box: 20x20x10 = 4,000 cm3, max 10kg
            seed.PackagingMaterials.Add(new PackagingMaterial
            {
                FacilityId = facilityId,
                Code = "BOX-SM",
                Name = "Small Box",
                LengthCm = 20m,
                WidthCm = 20m,
                HeightCm = 10m,
                MaxWeightKg = 10m,
                TareWeightKg = 0.2m,
                IsActive = true
            });

            // Medium box: 30x30x20 = 18,000 cm3, max 20kg
            seed.PackagingMaterials.Add(new PackagingMaterial
            {
                FacilityId = facilityId,
                Code = "BOX-MD",
                Name = "Medium Box",
                LengthCm = 30m,
                WidthCm = 30m,
                HeightCm = 20m,
                MaxWeightKg = 20m,
                TareWeightKg = 0.4m,
                IsActive = true
            });

            // Large box: 60x40x40 = 96,000 cm3, max 40kg
            seed.PackagingMaterials.Add(new PackagingMaterial
            {
                FacilityId = facilityId,
                Code = "BOX-LG",
                Name = "Large Box",
                LengthCm = 60m,
                WidthCm = 40m,
                HeightCm = 40m,
                MaxWeightKg = 40m,
                TareWeightKg = 0.8m,
                IsActive = true
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new CartonizationService(context, NullLogger<CartonizationService>.Instance);

            // Demand: 2 units of small item (10x10x5 cm, 1kg each = 2kg, 1,000 cm3 total)
            var request = new CartonizationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                VolumeUtilizationFactor = 0.85m,
                Items =
                [
                    new CartonizationItemRequest
                    {
                        ItemId = Guid.NewGuid(),
                        Sku = "ITEM-SM",
                        Quantity = 2m,
                        LengthCm = 10m,
                        WidthCm = 10m,
                        HeightCm = 5m,
                        WeightKg = 1m
                    }
                ]
            };

            var recommendation = await service.RecommendCartonizationAsync(request);

            Assert.True(recommendation.IsSuccess);
            Assert.Equal(1, recommendation.RecommendedPackageCount);
            var pkg = recommendation.RecommendedPackages.Single();
            Assert.Equal("BOX-SM", pkg.PackagingMaterialCode);
            Assert.Equal(2m, pkg.EstimatedNetWeightKg);
            Assert.Equal(2.2m, pkg.EstimatedGrossWeightKg);
        }
    }

    [Fact]
    public async Task Cartonization_RejectsBox_WhenItemDimensionExceedsLimitsEvenWithRotation()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Main Facility", FacilityCode = "FAC-01" });

            // Box is 30x30x20 cm
            seed.PackagingMaterials.Add(new PackagingMaterial
            {
                FacilityId = facilityId,
                Code = "BOX-FIXED",
                Name = "Fixed Box",
                LengthCm = 30m,
                WidthCm = 30m,
                HeightCm = 20m,
                MaxWeightKg = 30m,
                TareWeightKg = 0.5m,
                IsActive = true
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new CartonizationService(context, NullLogger<CartonizationService>.Instance);

            // Item is 45cm long (cannot fit in 30x30x20 in any 3D rotation)
            var request = new CartonizationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                Items =
                [
                    new CartonizationItemRequest
                    {
                        ItemId = Guid.NewGuid(),
                        Sku = "LONG-PIPE",
                        Quantity = 1m,
                        LengthCm = 45m,
                        WidthCm = 10m,
                        HeightCm = 10m,
                        WeightKg = 2m
                    }
                ]
            };

            var recommendation = await service.RecommendCartonizationAsync(request);

            Assert.False(recommendation.IsSuccess);
            Assert.Contains("physically exceed all available packaging materials", recommendation.Message);
        }
    }

    [Fact]
    public async Task Cartonization_RotatesItem_ToFitCompatibleCarton()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Main Facility", FacilityCode = "FAC-01" });

            // Box is 40 x 20 x 15 cm
            seed.PackagingMaterials.Add(new PackagingMaterial
            {
                FacilityId = facilityId,
                Code = "BOX-RECT",
                Name = "Rectangular Box",
                LengthCm = 40m,
                WidthCm = 20m,
                HeightCm = 15m,
                MaxWeightKg = 20m,
                TareWeightKg = 0.3m,
                IsActive = true
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new CartonizationService(context, NullLogger<CartonizationService>.Instance);

            // Item default orientation: L=15, W=38, H=10.
            // Rotated: 38 fits in 40, 15 fits in 20, 10 fits in 15!
            var request = new CartonizationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                Items =
                [
                    new CartonizationItemRequest
                    {
                        ItemId = Guid.NewGuid(),
                        Sku = "ROTATABLE-OBJ",
                        Quantity = 1m,
                        LengthCm = 15m,
                        WidthCm = 38m,
                        HeightCm = 10m,
                        WeightKg = 3m
                    }
                ]
            };

            var recommendation = await service.RecommendCartonizationAsync(request);

            Assert.True(recommendation.IsSuccess);
            Assert.Equal(1, recommendation.RecommendedPackageCount);
            Assert.Equal("BOX-RECT", recommendation.RecommendedPackages.First().PackagingMaterialCode);
        }
    }

    [Fact]
    public async Task Cartonization_RejectsBox_WhenMaxWeightExceeded()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Main Facility", FacilityCode = "FAC-01" });

            // Light box: large volume (50x50x50 = 125,000 cm3) but max weight only 5kg
            seed.PackagingMaterials.Add(new PackagingMaterial
            {
                FacilityId = facilityId,
                Code = "BOX-LIGHT",
                Name = "Lightweight Box",
                LengthCm = 50m,
                WidthCm = 50m,
                HeightCm = 50m,
                MaxWeightKg = 5m,
                TareWeightKg = 0.3m,
                IsActive = true
            });

            // Heavy box: smaller volume (30x30x30 = 27,000 cm3) but max weight 30kg
            seed.PackagingMaterials.Add(new PackagingMaterial
            {
                FacilityId = facilityId,
                Code = "BOX-HEAVY",
                Name = "Heavy Duty Box",
                LengthCm = 30m,
                WidthCm = 30m,
                HeightCm = 30m,
                MaxWeightKg = 30m,
                TareWeightKg = 0.8m,
                IsActive = true
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new CartonizationService(context, NullLogger<CartonizationService>.Instance);

            // Demand: Dense metal item weighing 12kg (exceeds BOX-LIGHT 5kg max)
            var request = new CartonizationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                Items =
                [
                    new CartonizationItemRequest
                    {
                        ItemId = Guid.NewGuid(),
                        Sku = "LEAD-WEIGHT",
                        Quantity = 1m,
                        LengthCm = 15m,
                        WidthCm = 15m,
                        HeightCm = 15m,
                        WeightKg = 12m
                    }
                ]
            };

            var recommendation = await service.RecommendCartonizationAsync(request);

            Assert.True(recommendation.IsSuccess);
            // Must choose BOX-HEAVY because BOX-LIGHT exceeds max weight
            Assert.Equal("BOX-HEAVY", recommendation.RecommendedPackages.First().PackagingMaterialCode);
        }
    }

    [Fact]
    public async Task Cartonization_PartitionsItemsIntoMultipleCartons_WhenExceedingSingleBoxCapacity()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Main Facility", FacilityCode = "FAC-01" });

            // Standard box: 30x30x20 = 18,000 cm3, max 10kg
            seed.PackagingMaterials.Add(new PackagingMaterial
            {
                FacilityId = facilityId,
                Code = "BOX-STD",
                Name = "Standard Box",
                LengthCm = 30m,
                WidthCm = 30m,
                HeightCm = 20m,
                MaxWeightKg = 10m,
                TareWeightKg = 0.4m,
                IsActive = true
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new CartonizationService(context, NullLogger<CartonizationService>.Instance);

            // Demand: 3 units weighing 6kg each (Total: 18kg > 10kg single box limit)
            var request = new CartonizationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                Items =
                [
                    new CartonizationItemRequest
                    {
                        ItemId = Guid.NewGuid(),
                        Sku = "HEAVY-UNIT",
                        Quantity = 3m,
                        LengthCm = 15m,
                        WidthCm = 15m,
                        HeightCm = 10m,
                        WeightKg = 6m
                    }
                ]
            };

            var recommendation = await service.RecommendCartonizationAsync(request);

            Assert.True(recommendation.IsSuccess);
            Assert.True(recommendation.RecommendedPackageCount >= 2,
                $"Expected at least 2 packages due to 18kg demand vs 10kg box limit, got {recommendation.RecommendedPackageCount}");
        }
    }

    #endregion

    #region Packing Execution Tests

    [Fact]
    public async Task StartPackingSession_PreventsConcurrentSessionsForDifferentOperators()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();
        var operatorB = Guid.NewGuid();

        var (orderId, stationId, _, _, _) = await SeedPackingEnvironmentAsync(dbName, clientA, facilityId);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);

            // 1. Operator A starts session
            var resA = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });
            Assert.True(resA.IsSuccess);

            // 2. Operator B attempts to start session for the same order -> Rejected
            var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
                service.StartOrResumeSessionAsync(new StartPackingSessionRequest
                {
                    FacilityId = facilityId,
                    PackingStationLocationId = stationId,
                    OutboundOrderId = orderId,
                    OperatorUserId = operatorB
                }));

            Assert.Contains("already being packed in active session", ex.Message);
        }
    }

    [Fact]
    public async Task StartPackingSession_AllowsResumeForSameOperator()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, _, _, _) = await SeedPackingEnvironmentAsync(dbName, clientA, facilityId);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);

            // Start session
            var session1 = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            // Suspend session
            var entity = await context.PackingSessions.FindAsync(session1.SessionId);
            entity!.Suspend("Break", operatorA);
            await context.SaveChangesAsync();

            // Resume session
            var session2 = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            Assert.Equal(session1.SessionId, session2.SessionId);
            Assert.Equal(PackingSessionStatus.Active, session2.Status);
        }
    }

    [Fact]
    public async Task PackItem_ValidToteAndItemScan_TransfersStockAtomically_AndPreservesConservation()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, toteId, boxMaterialId, itemId) = await SeedPackingEnvironmentAsync(
            dbName, clientA, facilityId, initialPickedQty: 10m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);

            // 1. Start Session
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            // 2. Create Carton Package
            var pkgResult = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });
            Assert.True(pkgResult.IsSuccess);

            // 3. Pack 6 units from Tote into Package
            var packResult = await service.PackItemAsync(new PackItemRequest
            {
                PackingSessionId = session.SessionId,
                PackageId = pkgResult.PackageId,
                ScannedToteLpn = "TOTE-PK-01",
                ScannedItemBarcode = "SKU-PACK-01",
                Quantity = 6m,
                UserId = operatorA
            });

            Assert.True(packResult.IsSuccess);
            Assert.Equal(6m, packResult.QuantityPacked);
            Assert.Equal(4m, packResult.RemainingToteQuantity);
            Assert.False(packResult.IsOrderFullyPacked);

            // 4. Invariant Verification: Physical Stock Conservation
            var toteStock = await context.StockOnHand.FirstAsync(s => s.HandlingUnitId == toteId);
            var cartonStock = await context.StockOnHand.FirstAsync(s => s.HandlingUnitId == pkgResult.HandlingUnitId);

            // Delta Q_tote = -6 (10 -> 4)
            Assert.Equal(4m, toteStock.QtyAllocated);
            // Delta Q_carton = +6 (0 -> 6)
            Assert.Equal(6m, cartonStock.QtyAllocated);
            // QtyAvailable in carton must remain strictly 0
            Assert.Equal(0m, cartonStock.QtyAvailable);
            Assert.Equal(0m, toteStock.QtyAvailable);

            // Total Allocated across tote and carton = 10m (Zero loss, zero secondary reservation)
            Assert.Equal(10m, toteStock.QtyAllocated + cartonStock.QtyAllocated);

            // 5. Immutable Ledger Verification
            var tx = await context.InventoryTransactions.FirstAsync(t => t.Id == packResult.InventoryTransactionId);
            Assert.Equal(InventoryTxType.Pack, tx.TxType);
            Assert.Equal(6m, tx.Quantity);
            Assert.Equal("Package", tx.ReferenceType);
            Assert.Equal(pkgResult.PackageId, tx.ReferenceId);
        }
    }

    [Fact]
    public async Task PackItem_RejectsInvalidToteLpn()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, _, boxMaterialId, _) = await SeedPackingEnvironmentAsync(dbName, clientA, facilityId);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            var pkg = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.PackItemAsync(new PackItemRequest
                {
                    PackingSessionId = session.SessionId,
                    PackageId = pkg.PackageId,
                    ScannedToteLpn = "NON-EXISTENT-TOTE",
                    ScannedItemBarcode = "SKU-PACK-01",
                    Quantity = 1m,
                    UserId = operatorA
                }));

            Assert.Contains("was not found in facility", ex.Message);
        }
    }

    [Fact]
    public async Task PackItem_RejectsCrossTenantTote()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, _, boxMaterialId, _) = await SeedPackingEnvironmentAsync(dbName, clientA, facilityId);

        // Add a tote belonging to Client B
        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientB, CompanyName = "Client B", AccountCode = "CB" });
            seed.HandlingUnits.Add(new HandlingUnit
            {
                ClientId = clientB,
                FacilityId = facilityId,
                LpnCode = "TOTE-CLIENT-B",
                UnitType = HandlingUnitType.PickTote,
                Status = HandlingUnitStatus.InUse
            });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            var pkg = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.PackItemAsync(new PackItemRequest
                {
                    PackingSessionId = session.SessionId,
                    PackageId = pkg.PackageId,
                    ScannedToteLpn = "TOTE-CLIENT-B",
                    ScannedItemBarcode = "SKU-PACK-01",
                    Quantity = 1m,
                    UserId = operatorA
                }));

            Assert.Contains("Cross-tenant packing rejected", ex.Message);
        }
    }

    [Fact]
    public async Task PackItem_RejectsIncorrectSkuBarcode()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, _, boxMaterialId, _) = await SeedPackingEnvironmentAsync(dbName, clientA, facilityId);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            var pkg = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.PackItemAsync(new PackItemRequest
                {
                    PackingSessionId = session.SessionId,
                    PackageId = pkg.PackageId,
                    ScannedToteLpn = "TOTE-PK-01",
                    ScannedItemBarcode = "WRONG-BARCODE-999",
                    Quantity = 1m,
                    UserId = operatorA
                }));

            Assert.Contains("does not match any item on Order", ex.Message);
        }
    }

    [Fact]
    public async Task PackItem_RejectsWrongLot_WhenLotTracked()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, _, boxMaterialId, _) = await SeedPackingEnvironmentAsync(
            dbName, clientA, facilityId, isLotTracked: true, lotNumber: "LOT-VAL-100");

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            var pkg = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.PackItemAsync(new PackItemRequest
                {
                    PackingSessionId = session.SessionId,
                    PackageId = pkg.PackageId,
                    ScannedToteLpn = "TOTE-PK-01",
                    ScannedItemBarcode = "SKU-PACK-01",
                    ScannedLotNumber = "WRONG-LOT-999",
                    Quantity = 1m,
                    UserId = operatorA
                }));

            Assert.Contains("does not match order requirement", ex.Message);
        }
    }

    [Fact]
    public async Task PackItem_RejectsQuantityExceedingPickedQuantity()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, _, boxMaterialId, _) = await SeedPackingEnvironmentAsync(
            dbName, clientA, facilityId, initialPickedQty: 5m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            var pkg = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });

            // Attempt to pack 10 units when only 5 exist in tote
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.PackItemAsync(new PackItemRequest
                {
                    PackingSessionId = session.SessionId,
                    PackageId = pkg.PackageId,
                    ScannedToteLpn = "TOTE-PK-01",
                    ScannedItemBarcode = "SKU-PACK-01",
                    Quantity = 10m,
                    UserId = operatorA
                }));

            Assert.Contains("exceeds available picked quantity 5 in tote", ex.Message);
        }
    }

    [Fact]
    public async Task PackItem_ValidPartialPacking_ThenSecondPack_TransfersBalanceCorrectly()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, toteId, boxMaterialId, _) = await SeedPackingEnvironmentAsync(
            dbName, clientA, facilityId, initialPickedQty: 10m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            var pkg = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });

            // 1. Pack 4 units
            var step1 = await service.PackItemAsync(new PackItemRequest
            {
                PackingSessionId = session.SessionId,
                PackageId = pkg.PackageId,
                ScannedToteLpn = "TOTE-PK-01",
                ScannedItemBarcode = "SKU-PACK-01",
                Quantity = 4m,
                UserId = operatorA
            });
            Assert.Equal(6m, step1.RemainingToteQuantity);
            Assert.False(step1.IsOrderFullyPacked);

            // 2. Pack remaining 6 units
            var step2 = await service.PackItemAsync(new PackItemRequest
            {
                PackingSessionId = session.SessionId,
                PackageId = pkg.PackageId,
                ScannedToteLpn = "TOTE-PK-01",
                ScannedItemBarcode = "SKU-PACK-01",
                Quantity = 6m,
                UserId = operatorA
            });
            Assert.Equal(0m, step2.RemainingToteQuantity);
            Assert.True(step2.IsOrderFullyPacked);

            // Final state: tote has 0 allocated, carton has 10 allocated
            var toteStock = await context.StockOnHand.FirstAsync(s => s.HandlingUnitId == toteId);
            var cartonStock = await context.StockOnHand.FirstAsync(s => s.HandlingUnitId == pkg.HandlingUnitId);
            Assert.Equal(0m, toteStock.QtyAllocated);
            Assert.Equal(10m, cartonStock.QtyAllocated);
        }
    }

    [Fact]
    public async Task PackItem_RejectsPackingWhenCartonMaxWeightExceeded()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        // Packaging material with low max weight (3kg)
        var (orderId, stationId, _, boxMaterialId, itemId) = await SeedPackingEnvironmentAsync(
            dbName, clientA, facilityId, initialPickedQty: 10m, itemWeightKg: 1m, boxMaxWeightKg: 3m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            var pkg = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });

            // Packing 5 units = 5kg > 3kg max
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.PackItemAsync(new PackItemRequest
                {
                    PackingSessionId = session.SessionId,
                    PackageId = pkg.PackageId,
                    ScannedToteLpn = "TOTE-PK-01",
                    ScannedItemBarcode = "SKU-PACK-01",
                    Quantity = 5m,
                    UserId = operatorA
                }));

            Assert.Contains("exceed carton limit (3.00kg)", ex.Message);
        }
    }

    [Fact]
    public async Task ClosePackage_ValidatesScaleGrossAndTareWeight()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, _, boxMaterialId, _) = await SeedPackingEnvironmentAsync(
            dbName, clientA, facilityId, initialPickedQty: 10m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            var pkg = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });

            // 1. Cannot close empty package
            var emptyEx = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
                service.ClosePackageAsync(new ClosePackageRequest
                {
                    PackageId = pkg.PackageId,
                    ScaleGrossWeightKg = 5m,
                    UserId = operatorA
                }));
            Assert.Contains("Cannot close empty package", emptyEx.Message);

            // Pack 5 units
            await service.PackItemAsync(new PackItemRequest
            {
                PackingSessionId = session.SessionId,
                PackageId = pkg.PackageId,
                ScannedToteLpn = "TOTE-PK-01",
                ScannedItemBarcode = "SKU-PACK-01",
                Quantity = 5m,
                UserId = operatorA
            });

            // 2. Cannot close with scale weight less than tare weight (0.5kg)
            var tareEx = await Assert.ThrowsAsync<ValidationException>(() =>
                service.ClosePackageAsync(new ClosePackageRequest
                {
                    PackageId = pkg.PackageId,
                    ScaleGrossWeightKg = 0.2m,
                    UserId = operatorA
                }));
            Assert.Contains("cannot be less than packaging tare weight", tareEx.Message);

            // 3. Valid scale close: Gross = 3.5kg, Tare = 0.5kg -> Net = 3.0kg
            var closeResult = await service.ClosePackageAsync(new ClosePackageRequest
            {
                PackageId = pkg.PackageId,
                ScaleGrossWeightKg = 3.5m,
                UserId = operatorA
            });

            Assert.True(closeResult.IsSuccess);
            Assert.Equal(PackageStatus.Packed, closeResult.Status);
            Assert.Equal(3.5m, closeResult.GrossWeightKg);
            Assert.Equal(0.5m, closeResult.TareWeightKg);
            Assert.Equal(3.0m, closeResult.NetWeightKg);
        }
    }

    [Fact]
    public async Task ReleaseTote_RejectsRelease_WhenToteStillHasInventory()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();

        await SeedPackingEnvironmentAsync(dbName, clientA, facilityId, initialPickedQty: 10m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);

            // Tote still has 10 units of inventory
            var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
                service.ReleaseToteAsync(new ReleaseToteRequest
                {
                    ToteLpn = "TOTE-PK-01",
                    FacilityId = facilityId,
                    UserId = Guid.NewGuid()
                }));

            Assert.Contains("It still holds", ex.Message);
            Assert.Contains("active inventory", ex.Message);
        }
    }

    [Fact]
    public async Task ReleaseTote_Succeeds_WhenToteIsCompletelyEmpty()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, toteId, boxMaterialId, _) = await SeedPackingEnvironmentAsync(
            dbName, clientA, facilityId, initialPickedQty: 10m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            var pkg = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });

            // Pack ALL 10 units so tote balance becomes 0
            await service.PackItemAsync(new PackItemRequest
            {
                PackingSessionId = session.SessionId,
                PackageId = pkg.PackageId,
                ScannedToteLpn = "TOTE-PK-01",
                ScannedItemBarcode = "SKU-PACK-01",
                Quantity = 10m,
                UserId = operatorA
            });

            // Release empty tote
            var releaseResult = await service.ReleaseToteAsync(new ReleaseToteRequest
            {
                ToteLpn = "TOTE-PK-01",
                FacilityId = facilityId,
                UserId = operatorA
            });

            Assert.True(releaseResult.IsSuccess);
            Assert.Equal(HandlingUnitStatus.Available, releaseResult.Status);

            var tote = await context.HandlingUnits.FindAsync(toteId);
            Assert.Equal(HandlingUnitStatus.Available, tote!.Status);
            Assert.Null(tote.CurrentOutboundOrderId);
            Assert.Null(tote.CurrentPickWaveId);
        }
    }

    [Fact]
    public async Task CompletePackingSession_ValidatesAllPackagesSealedAndLinesPacked()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var operatorA = Guid.NewGuid();

        var (orderId, stationId, _, boxMaterialId, _) = await SeedPackingEnvironmentAsync(
            dbName, clientA, facilityId, initialPickedQty: 10m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PackingExecutionService(context, NullLogger<PackingExecutionService>.Instance);
            var session = await service.StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facilityId,
                PackingStationLocationId = stationId,
                OutboundOrderId = orderId,
                OperatorUserId = operatorA
            });

            var pkg = await service.CreatePackageAsync(new CreatePackageRequest
            {
                PackingSessionId = session.SessionId,
                PackagingMaterialId = boxMaterialId,
                UserId = operatorA
            });

            // Pack 10 units
            await service.PackItemAsync(new PackItemRequest
            {
                PackingSessionId = session.SessionId,
                PackageId = pkg.PackageId,
                ScannedToteLpn = "TOTE-PK-01",
                ScannedItemBarcode = "SKU-PACK-01",
                Quantity = 10m,
                UserId = operatorA
            });

            // 1. Cannot complete if package is still Open
            var unsealedEx = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
                service.CompletePackingSessionAsync(new CompletePackingSessionRequest
                {
                    PackingSessionId = session.SessionId,
                    UserId = operatorA
                }));
            Assert.Contains("open packages awaiting scale weight", unsealedEx.Message);

            // Close package
            await service.ClosePackageAsync(new ClosePackageRequest
            {
                PackageId = pkg.PackageId,
                ScaleGrossWeightKg = 6m,
                UserId = operatorA
            });

            // 2. Now completes successfully
            var completeResult = await service.CompletePackingSessionAsync(new CompletePackingSessionRequest
            {
                PackingSessionId = session.SessionId,
                UserId = operatorA
            });

            Assert.True(completeResult.IsSuccess);
            Assert.Equal(PackingSessionStatus.Completed, completeResult.Status);
            Assert.NotNull(completeResult.CompletedAtUtc);
            Assert.Equal(1, completeResult.TotalPackagesCount);
        }
    }

    #endregion

    #region Helper Seed Methods

    private static async Task<(Guid orderId, Guid stationId, Guid toteId, Guid boxMaterialId, Guid itemId)> SeedPackingEnvironmentAsync(
        string dbName,
        Guid clientId,
        Guid facilityId,
        decimal initialPickedQty = 10m,
        bool isLotTracked = false,
        string? lotNumber = null,
        decimal itemWeightKg = 0.5m,
        decimal boxMaxWeightKg = 25m)
    {
        var orderId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var toteId = Guid.NewGuid();
        var boxMaterialId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        Guid? lotId = isLotTracked ? Guid.NewGuid() : null;

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Packing Client", AccountCode = "PC" });
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
                Sku = "SKU-PACK-01",
                Description = "Item to Pack",
                WeightKg = itemWeightKg,
                LengthCm = 15m,
                WidthCm = 10m,
                HeightCm = 5m,
                IsLotTracked = isLotTracked
            };
            item.Barcodes.Add(new ItemBarcode { ItemId = itemId, BarcodeValue = "SKU-PACK-01" });
            seed.Items.Add(item);

            if (isLotTracked && lotId.HasValue)
            {
                seed.InventoryLots.Add(new InventoryLot
                {
                    Id = lotId.Value,
                    ClientId = clientId,
                    ItemId = itemId,
                    LotNumber = lotNumber ?? "LOT-001",
                    QaStatus = LotQaStatus.Released
                });
            }

            var tote = new HandlingUnit
            {
                Id = toteId,
                ClientId = clientId,
                FacilityId = facilityId,
                LpnCode = "TOTE-PK-01",
                UnitType = HandlingUnitType.PickTote,
                Status = HandlingUnitStatus.InUse,
                CurrentLocationId = stationId,
                CurrentOutboundOrderId = orderId
            };
            seed.HandlingUnits.Add(tote);

            var boxMaterial = new PackagingMaterial
            {
                Id = boxMaterialId,
                FacilityId = facilityId,
                Code = "BOX-MED",
                Name = "Medium Box",
                LengthCm = 35m,
                WidthCm = 25m,
                HeightCm = 20m,
                MaxWeightKg = boxMaxWeightKg,
                TareWeightKg = 0.5m,
                IsActive = true
            };
            seed.PackagingMaterials.Add(boxMaterial);

            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = clientId,
                FacilityId = facilityId,
                OrderNumber = "ORD-PK-001",
                OrderType = OutboundOrderType.EcommerceFulfillment,
                Status = OutboundOrderStatus.Allocated
            };

            var orderLine = new OutboundOrderLine
            {
                Id = lineId,
                OutboundOrderId = orderId,
                ItemId = itemId,
                LineNumber = 1,
                QuantityOrdered = initialPickedQty,
                QuantityAllocated = initialPickedQty,
                RequiredLotNumber = lotNumber,
                Status = OutboundOrderLineStatus.Allocated
            };
            order.Lines.Add(orderLine);
            seed.OutboundOrders.Add(order);

            // Stock in Tote
            var toteStock = new StockOnHand
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LotId = lotId,
                LocationId = stationId,
                HandlingUnitId = toteId,
                LpnCode = tote.LpnCode,
                QtyAvailable = 0m,
                QtyAllocated = initialPickedQty,
                ReceivedAtUtc = DateTime.UtcNow
            };
            seed.StockOnHand.Add(toteStock);

            // Allocation linking line to tote stock
            var alloc = new InventoryAllocation
            {
                Id = allocId,
                ClientId = clientId,
                FacilityId = facilityId,
                OutboundOrderId = orderId,
                OutboundOrderLineId = lineId,
                StockOnHandId = toteStock.Id,
                ItemId = itemId,
                LocationId = stationId,
                LotId = lotId,
                QuantityRequested = initialPickedQty,
                QuantityAllocated = initialPickedQty,
                QuantityPicked = initialPickedQty,
                Status = AllocationStatus.Picked,
                TargetHandlingUnitId = toteId,
                TargetLpnCode = tote.LpnCode,
                AllocatedAtUtc = DateTime.UtcNow
            };
            seed.InventoryAllocations.Add(alloc);

            await seed.SaveChangesAsync();
        }

        return (orderId, stationId, toteId, boxMaterialId, itemId);
    }

    #endregion
}
