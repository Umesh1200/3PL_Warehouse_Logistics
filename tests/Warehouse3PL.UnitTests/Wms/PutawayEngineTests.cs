using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Features.Putaway.Commands.ConfirmPutaway;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Wms;

public class PutawayEngineTests
{
    [Fact]
    public async Task PutawayRecommendation_TemperatureRequirement_DirectsFrozenItemToColdZone()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var ambientZoneId = Guid.NewGuid();
        var coldZoneId = Guid.NewGuid();

        var ambientBinId = Guid.NewGuid();
        var coldBinId = Guid.NewGuid();

        var frozenItemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seedContext.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-COLD", Name = "Cold Chain Hub" });
            
            var ambientZone = new WarehouseZone
            {
                Id = ambientZoneId,
                FacilityId = facilityId,
                ZoneCode = "ZN-AMBIENT",
                ZoneType = WarehouseZoneType.BulkStorage,
                TempMinC = 15m,
                TempMaxC = 25m
            };

            var coldZone = new WarehouseZone
            {
                Id = coldZoneId,
                FacilityId = facilityId,
                ZoneCode = "ZN-FREEZER",
                ZoneType = WarehouseZoneType.ColdStorage,
                TempMinC = -25m,
                TempMaxC = -18m
            };

            seedContext.WarehouseZones.AddRange(ambientZone, coldZone);

            seedContext.Locations.AddRange(
                new Location
                {
                    Id = ambientBinId,
                    FacilityId = facilityId,
                    ZoneId = ambientZoneId,
                    LocationCode = "BIN-AMB-01",
                    Barcode = "LOC-AMB-01",
                    LocationType = LocationType.Bin,
                    MaxWeightKg = 2000,
                    MaxVolumeCm3 = 5000000
                },
                new Location
                {
                    Id = coldBinId,
                    FacilityId = facilityId,
                    ZoneId = coldZoneId,
                    LocationCode = "BIN-FRZ-01",
                    Barcode = "LOC-FRZ-01",
                    LocationType = LocationType.Bin,
                    MaxWeightKg = 2000,
                    MaxVolumeCm3 = 5000000
                }
            );

            seedContext.Items.Add(new Item
            {
                Id = frozenItemId,
                ClientId = clientId,
                Sku = "ICE-CREAM-VANILLA",
                Description = "Frozen Dairy Item",
                StorageTempClass = StorageTempClass.Frozen,
                WeightKg = 1.0m,
                LengthCm = 20,
                WidthCm = 15,
                HeightCm = 10,
                VolumeCm3 = 3000
            });

            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var putawayService = new PutawayRecommendationService(context, NullLogger<PutawayRecommendationService>.Instance);

        // Act - Request putaway recommendation for 10 units of frozen ice cream
        var recommendations = await putawayService.RecommendLocationsAsync(
            clientId, facilityId, frozenItemId, quantity: 10m);

        // Assert - Only the cold storage freezer bin is recommended
        Assert.Single(recommendations);
        Assert.Equal(coldBinId, recommendations[0].LocationId);
        Assert.Equal("BIN-FRZ-01", recommendations[0].LocationCode);
    }

    [Fact]
    public async Task PutawayRecommendation_ConsolidationBonus_PrioritizesExistingSkuLocation()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var binAId = Guid.NewGuid(); // Has existing stock of this SKU
        var binBId = Guid.NewGuid(); // Empty bin
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seedContext.WarehouseZones.Add(new WarehouseZone
            {
                Id = zoneId,
                FacilityId = facilityId,
                ZoneCode = "ZN-DRY",
                ZoneType = WarehouseZoneType.BulkStorage
            });

            seedContext.Locations.AddRange(
                new Location { Id = binAId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-A", Barcode = "BAR-A", LocationType = LocationType.Bin, MaxWeightKg = 1000, MaxVolumeCm3 = 1000000 },
                new Location { Id = binBId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-B", Barcode = "BAR-B", LocationType = LocationType.Bin, MaxWeightKg = 1000, MaxVolumeCm3 = 1000000 }
            );

            seedContext.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "SHAMPOO-500ML",
                Description = "Hair Care",
                StorageTempClass = StorageTempClass.Ambient,
                WeightKg = 0.6m,
                VolumeCm3 = 800
            });

            // Bin A already has 20 units of this SKU
            seedContext.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = binAId,
                QtyAvailable = 20m
            });

            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var putawayService = new PutawayRecommendationService(context, NullLogger<PutawayRecommendationService>.Instance);

        // Act
        var recommendations = await putawayService.RecommendLocationsAsync(clientId, facilityId, itemId, quantity: 10m);

        // Assert - Both bins fit, but Bin A has higher score due to SKU consolidation
        Assert.Equal(2, recommendations.Count);
        Assert.Equal(binAId, recommendations[0].LocationId);
        Assert.True(recommendations[0].FitScore > recommendations[1].FitScore);
        Assert.Contains("Consolidation", recommendations[0].Reason);
    }

    [Fact]
    public async Task ConfirmPutaway_MovesStockFromDockToBin_AndCompletesTask()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var dockLocId = Guid.NewGuid();
        var targetBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seedContext.Locations.AddRange(
                new Location { Id = dockLocId, FacilityId = facilityId, LocationCode = "RCV-DOCK-01", Barcode = "LOC-DOCK-01", LocationType = LocationType.DockDoor },
                new Location { Id = targetBinId, FacilityId = facilityId, LocationCode = "BIN-STORAGE-01", Barcode = "LOC-BIN-01", LocationType = LocationType.Bin }
            );

            seedContext.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "PUTAWAY-ITEM", Description = "Goods" });

            seedContext.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = dockLocId,
                QtyAvailable = 50m
            });

            seedContext.PutawayTasks.Add(new PutawayTask
            {
                Id = taskId,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                SourceLocationId = dockLocId,
                TargetLocationId = targetBinId,
                Quantity = 50m,
                Status = PutawayTaskStatus.Pending
            });

            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new ConfirmPutawayCommandHandler(context, currentUser);

        // Act - Confirm Putaway Execution
        var result = await handler.Handle(new ConfirmPutawayCommand
        {
            TaskId = taskId
        }, CancellationToken.None);

        // Assert
        Assert.Equal(PutawayTaskStatus.Completed, result.Status);
        Assert.NotNull(result.CompletedAt);

        // Verify Stock Changes
        var dockStock = await context.StockOnHand.FirstAsync(s => s.LocationId == dockLocId && s.ItemId == itemId);
        var targetStock = await context.StockOnHand.FirstAsync(s => s.LocationId == targetBinId && s.ItemId == itemId);

        Assert.Equal(0m, dockStock.QtyAvailable);
        Assert.Equal(50m, targetStock.QtyAvailable);

        // Verify Putaway Ledger Entry
        var putawayTx = await context.InventoryTransactions.FirstOrDefaultAsync(t => t.TxType == InventoryTxType.Putaway);
        Assert.NotNull(putawayTx);
        Assert.Equal(50m, putawayTx.Quantity);
        Assert.Equal(dockLocId, putawayTx.FromLocationId);
        Assert.Equal(targetBinId, putawayTx.ToLocationId);
    }
}

