using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.AssignPutawayTask;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ConfirmPutawayTask;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.GeneratePutawayTasks;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ReportPutawayException;
using Warehouse3PL.Application.Features.Putaway.Tasks.Queries.GetNextPutawayTask;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.UnitTests.Common;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Inbound;

public class PutawayStrategyAndExecutionTests
{
    [Fact]
    public async Task PutawaySlotting_RefrigeratedItem_DirectsExclusivelyToChilledZone()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var ambientZoneId = Guid.NewGuid();
        var chilledZoneId = Guid.NewGuid();
        var ambientBinId = Guid.NewGuid();
        var chilledBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-CHI", Name = "Chicago Cold Hub" });
            seed.WarehouseZones.AddRange(
                new WarehouseZone { Id = ambientZoneId, FacilityId = facilityId, ZoneCode = "ZN-AMB", ZoneType = WarehouseZoneType.BulkStorage, TempMinC = 15m, TempMaxC = 25m },
                new WarehouseZone { Id = chilledZoneId, FacilityId = facilityId, ZoneCode = "ZN-CHILL", ZoneType = WarehouseZoneType.ColdStorage, TempMinC = 2m, TempMaxC = 6m }
            );

            seed.Locations.AddRange(
                new Location { Id = ambientBinId, FacilityId = facilityId, ZoneId = ambientZoneId, LocationCode = "BIN-AMB-01", Barcode = "LOC-AMB-01", LocationType = LocationType.Bin, MaxWeightKg = 2000, MaxVolumeCm3 = 5000000 },
                new Location { Id = chilledBinId, FacilityId = facilityId, ZoneId = chilledZoneId, LocationCode = "BIN-CHL-01", Barcode = "LOC-CHL-01", LocationType = LocationType.Bin, MaxWeightKg = 2000, MaxVolumeCm3 = 5000000 }
            );

            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "VACCINE-CHILLED",
                Description = "Refrigerated Pharmaceuticals",
                StorageTempClass = StorageTempClass.Refrigerated,
                WeightKg = 0.5m,
                VolumeCm3 = 1000
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);

        var location = await selector.SelectOptimalLocationAsync(clientId, facilityId, itemId, quantity: 10m);

        Assert.NotNull(location);
        Assert.Equal(chilledBinId, location.Id);
        Assert.Equal("BIN-CHL-01", location.LocationCode);
    }

    [Fact]
    public async Task PutawaySlotting_FrozenItem_DirectsExclusivelyToFreezerZone()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var ambientZoneId = Guid.NewGuid();
        var freezerZoneId = Guid.NewGuid();
        var ambientBinId = Guid.NewGuid();
        var freezerBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-CHI", Name = "Chicago Hub" });
            seed.WarehouseZones.AddRange(
                new WarehouseZone { Id = ambientZoneId, FacilityId = facilityId, ZoneCode = "ZN-AMB", ZoneType = WarehouseZoneType.BulkStorage },
                new WarehouseZone { Id = freezerZoneId, FacilityId = facilityId, ZoneCode = "ZN-FRZ", ZoneType = WarehouseZoneType.ColdStorage, TempMaxC = -20m }
            );

            seed.Locations.AddRange(
                new Location { Id = ambientBinId, FacilityId = facilityId, ZoneId = ambientZoneId, LocationCode = "BIN-AMB-01", Barcode = "LOC-AMB-01", LocationType = LocationType.Bin },
                new Location { Id = freezerBinId, FacilityId = facilityId, ZoneId = freezerZoneId, LocationCode = "BIN-FRZ-01", Barcode = "LOC-FRZ-01", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "ICE-CREAM-PINTS",
                Description = "Frozen Gelato",
                StorageTempClass = StorageTempClass.Frozen,
                WeightKg = 0.8m,
                VolumeCm3 = 1200
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);

        var location = await selector.SelectOptimalLocationAsync(clientId, facilityId, itemId, quantity: 20m);

        Assert.NotNull(location);
        Assert.Equal(freezerBinId, location.Id);
    }

    [Fact]
    public async Task PutawaySlotting_AmbientItem_RejectsColdRoomUnlessOverflowAllowed()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var ambientZoneId = Guid.NewGuid();
        var coldZoneId = Guid.NewGuid();
        var ambientBinId = Guid.NewGuid();
        var coldBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-1", Name = "Standard Hub" });
            seed.WarehouseZones.AddRange(
                new WarehouseZone { Id = ambientZoneId, FacilityId = facilityId, ZoneCode = "ZN-AMB", ZoneType = WarehouseZoneType.BulkStorage },
                new WarehouseZone { Id = coldZoneId, FacilityId = facilityId, ZoneCode = "ZN-COLD", ZoneType = WarehouseZoneType.ColdStorage, TempMaxC = 4m }
            );

            seed.Locations.AddRange(
                new Location { Id = ambientBinId, FacilityId = facilityId, ZoneId = ambientZoneId, LocationCode = "BIN-DRY-01", Barcode = "LOC-DRY-01", LocationType = LocationType.Bin },
                new Location { Id = coldBinId, FacilityId = facilityId, ZoneId = coldZoneId, LocationCode = "BIN-COLD-01", Barcode = "LOC-COLD-01", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "DRY-CEREAL-BOX",
                Description = "Breakfast Cereal",
                StorageTempClass = StorageTempClass.Ambient,
                WeightKg = 0.4m,
                VolumeCm3 = 2000
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);

        var recs = await selector.EvaluateCandidateLocationsAsync(clientId, facilityId, itemId, quantity: 10m);

        Assert.Single(recs);
        Assert.Equal(ambientBinId, recs[0].LocationId);
    }

    [Fact]
    public async Task PutawaySlotting_VelocityA_DirectsToLowShelfLevelAndFrontAisle()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var frontLowBinId = Guid.NewGuid();
        var backHighBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.WarehouseZones.Add(new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZN-PICK", ZoneType = WarehouseZoneType.BulkStorage });

            seed.Locations.AddRange(
                new Location
                {
                    Id = frontLowBinId,
                    FacilityId = facilityId,
                    ZoneId = zoneId,
                    LocationCode = "BIN-FRONT-L1",
                    Barcode = "LOC-F1",
                    LocationType = LocationType.Bin,
                    ShelfLevel = 1,
                    PickSequence = 10
                },
                new Location
                {
                    Id = backHighBinId,
                    FacilityId = facilityId,
                    ZoneId = zoneId,
                    LocationCode = "BIN-BACK-L4",
                    Barcode = "LOC-B4",
                    LocationType = LocationType.Bin,
                    ShelfLevel = 4,
                    PickSequence = 250
                }
            );

            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "FAST-MOVER-PHONE",
                Description = "Smartphone X",
                StorageTempClass = StorageTempClass.Ambient,
                AbcVelocity = AbcVelocity.A,
                WeightKg = 0.3m,
                VolumeCm3 = 500
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);

        var recs = await selector.EvaluateCandidateLocationsAsync(clientId, facilityId, itemId, quantity: 10m);

        Assert.Equal(2, recs.Count);
        Assert.Equal(frontLowBinId, recs[0].LocationId);
        Assert.True(recs[0].FitScore > recs[1].FitScore);
        Assert.Contains("fast-moving A-velocity", recs[0].Reason);
    }

    [Fact]
    public async Task PutawaySlotting_VelocityC_DirectsToHighBayReserveLevel()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var lowBinId = Guid.NewGuid();
        var highBayBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.WarehouseZones.Add(new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZN-MAIN", ZoneType = WarehouseZoneType.BulkStorage });

            seed.Locations.AddRange(
                new Location { Id = lowBinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-L1", Barcode = "BAR-L1", LocationType = LocationType.Bin, ShelfLevel = 1 },
                new Location { Id = highBayBinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-L4", Barcode = "BAR-L4", LocationType = LocationType.Bin, ShelfLevel = 4 }
            );

            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "SLOW-MOVER-SPARE-PART",
                Description = "Machine Bolt 200mm",
                StorageTempClass = StorageTempClass.Ambient,
                AbcVelocity = AbcVelocity.C,
                WeightKg = 1.0m,
                VolumeCm3 = 1000
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);

        var recs = await selector.EvaluateCandidateLocationsAsync(clientId, facilityId, itemId, quantity: 10m);

        Assert.Equal(2, recs.Count);
        Assert.Equal(highBayBinId, recs[0].LocationId);
        Assert.True(recs[0].FitScore > recs[1].FitScore);
        Assert.Contains("C-velocity", recs[0].Reason);
    }

    [Fact]
    public async Task PutawaySlotting_ItemLocationAffinity_ConsolidatesWithExistingSameSku()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var existingSkuBinId = Guid.NewGuid();
        var emptyBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.WarehouseZones.Add(new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZN-A", ZoneType = WarehouseZoneType.BulkStorage });

            seed.Locations.AddRange(
                new Location { Id = existingSkuBinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-EXISTING", Barcode = "BAR-EX", LocationType = LocationType.Bin },
                new Location { Id = emptyBinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-EMPTY", Barcode = "BAR-EM", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-SOAP", Description = "Hand Soap", WeightKg = 0.5m });

            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = existingSkuBinId,
                QtyAvailable = 15m
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);

        var recs = await selector.EvaluateCandidateLocationsAsync(clientId, facilityId, itemId, quantity: 10m);

        Assert.Equal(existingSkuBinId, recs[0].LocationId);
        Assert.True(recs[0].FitScore > recs[1].FitScore);
        Assert.Contains("Consolidation with existing SKU", recs[0].Reason);
    }

    [Fact]
    public async Task PutawaySlotting_ItemLotAffinity_PrioritizesIdenticalLotLocation()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var lot1BinId = Guid.NewGuid();
        var lot2BinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var lot1Id = Guid.NewGuid();
        var lot2Id = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.WarehouseZones.Add(new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZN-A", ZoneType = WarehouseZoneType.BulkStorage });

            seed.Locations.AddRange(
                new Location { Id = lot1BinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-LOT-1", Barcode = "BAR-L1", LocationType = LocationType.Bin },
                new Location { Id = lot2BinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-LOT-2", Barcode = "BAR-L2", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-LOT-TRACKED", Description = "Pharma Batch Item", WeightKg = 0.2m });

            seed.StockOnHand.AddRange(
                new StockOnHand { ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LotId = lot1Id, LocationId = lot1BinId, QtyAvailable = 20m },
                new StockOnHand { ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LotId = lot2Id, LocationId = lot2BinId, QtyAvailable = 20m }
            );

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);

        var recs = await selector.EvaluateCandidateLocationsAsync(clientId, facilityId, itemId, quantity: 5m, lotId: lot1Id);

        Assert.Equal(lot1BinId, recs[0].LocationId);
        Assert.True(recs[0].FitScore > recs[1].FitScore);
        Assert.Contains("identical SKU and Lot", recs[0].Reason);
    }

    [Fact]
    public async Task PutawaySlotting_ExceedsMaxWeight_RejectsLocation()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var heavyBinId = Guid.NewGuid();
        var lightBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.WarehouseZones.Add(new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZN-A", ZoneType = WarehouseZoneType.BulkStorage });

            // Light bin capacity is 50kg, Heavy bin capacity is 1000kg
            seed.Locations.AddRange(
                new Location { Id = lightBinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-LIGHT", Barcode = "BAR-LT", LocationType = LocationType.Bin, MaxWeightKg = 50m },
                new Location { Id = heavyBinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-HEAVY", Barcode = "BAR-HV", LocationType = LocationType.Bin, MaxWeightKg = 1000m }
            );

            // Item weighs 20kg each. 5 units = 100kg -> exceeds 50kg bin!
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "ANVIL-HEAVY", Description = "Steel Anvil", WeightKg = 20m });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);

        var recs = await selector.EvaluateCandidateLocationsAsync(clientId, facilityId, itemId, quantity: 5m);

        Assert.Single(recs);
        Assert.Equal(heavyBinId, recs[0].LocationId);
    }

    [Fact]
    public async Task PutawaySlotting_ExceedsMaxVolume_RejectsLocation()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var smallBinId = Guid.NewGuid();
        var largeBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.WarehouseZones.Add(new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZN-A", ZoneType = WarehouseZoneType.BulkStorage });

            seed.Locations.AddRange(
                new Location { Id = smallBinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-SM", Barcode = "BAR-SM", LocationType = LocationType.Bin, MaxVolumeCm3 = 2000m },
                new Location { Id = largeBinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-LG", Barcode = "BAR-LG", LocationType = LocationType.Bin, MaxVolumeCm3 = 50000m }
            );

            // Item volume is 1000cm3 each. 5 units = 5000cm3 -> exceeds small bin (2000cm3)
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "BULKY-PILLOW", Description = "Pillow", WeightKg = 0.5m, VolumeCm3 = 1000m });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);

        var recs = await selector.EvaluateCandidateLocationsAsync(clientId, facilityId, itemId, quantity: 5m);

        Assert.Single(recs);
        Assert.Equal(largeBinId, recs[0].LocationId);
    }

    [Fact]
    public async Task GenerateTasks_UnassignedStagingLpn_CreatesPendingPutawayTask()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var targetBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var receiptLineId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Main Hub" });
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-LANE-01", Barcode = "LOC-STAGE", LocationType = LocationType.StagingLane },
                new Location { Id = targetBinId, FacilityId = facilityId, LocationCode = "BIN-STORAGE-01", Barcode = "LOC-BIN-01", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-AUTO-GEN", Description = "Test Goods", StorageTempClass = StorageTempClass.Ambient, WeightKg = 1m });

            var session = new ReceivingSession
            {
                Id = sessionId,
                ClientId = clientId,
                FacilityId = facilityId,
                SessionNumber = "RCV-SESS-001",
                Status = ReceivingStatus.Completed
            };

            var line = new ReceiptLine
            {
                Id = receiptLineId,
                ReceivingSessionId = sessionId,
                ItemId = itemId,
                Lpn = "LPN-20261102-GEN01",
                QuantityReceived = 40m,
                QuantityExpected = 40m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            };
            session.Lines.Add(line);
            seed.ReceivingSessions.Add(session);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
        var handler = new GeneratePutawayTasksCommandHandler(context, selector);

        var tasks = await handler.Handle(new GeneratePutawayTasksCommand
        {
            FacilityId = facilityId,
            ClientId = clientId
        }, CancellationToken.None);

        Assert.Single(tasks);
        Assert.Equal("LPN-20261102-GEN01", tasks[0].LpnCode);
        Assert.Equal(PutawayTaskStatus.Pending, tasks[0].Status);
        Assert.Equal(40m, tasks[0].Quantity);
        Assert.Equal(targetBinId, tasks[0].SuggestedLocationId);
    }

    [Fact]
    public async Task GenerateTasks_ColdChainItem_SetsExpeditePriority()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var coldBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.WarehouseZones.Add(new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZN-COLD", ZoneType = WarehouseZoneType.ColdStorage, TempMaxC = 4m });
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", Barcode = "STAGE-01", LocationType = LocationType.StagingLane },
                new Location { Id = coldBinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-COLD-01", Barcode = "LOC-COLD", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-CHILLED-INSULIN", Description = "Insulin", StorageTempClass = StorageTempClass.Refrigerated, WeightKg = 0.2m });

            var session = new ReceivingSession { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, SessionNumber = "RCV-COLD-01", Status = ReceivingStatus.Completed };
            session.Lines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ItemId = itemId,
                Lpn = "LPN-COLD-RUSH-01",
                QuantityReceived = 10m,
                QuantityExpected = 10m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            seed.ReceivingSessions.Add(session);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
        var handler = new GeneratePutawayTasksCommandHandler(context, selector);

        var tasks = await handler.Handle(new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);

        Assert.Single(tasks);
        Assert.Equal(PutawayPriority.Expedite, tasks[0].Priority);
    }

    [Fact]
    public async Task GenerateTasks_VelocityAItem_SetsHighPriority()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", Barcode = "STAGE-01", LocationType = LocationType.StagingLane },
                new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-01", Barcode = "LOC-01", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-VEL-A", Description = "Trending Item", StorageTempClass = StorageTempClass.Ambient, AbcVelocity = AbcVelocity.A, WeightKg = 1m });

            var session = new ReceivingSession { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, SessionNumber = "RCV-VELA-01", Status = ReceivingStatus.Completed };
            session.Lines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ItemId = itemId,
                Lpn = "LPN-VELA-001",
                QuantityReceived = 25m,
                QuantityExpected = 25m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            seed.ReceivingSessions.Add(session);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
        var handler = new GeneratePutawayTasksCommandHandler(context, selector);

        var tasks = await handler.Handle(new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);

        Assert.Single(tasks);
        Assert.Equal(PutawayPriority.High, tasks[0].Priority);
    }

    [Fact]
    public async Task GetNextPutawayTask_ReturnsExpeditePriorityBeforeNormal_FIFO()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var loc1 = Guid.NewGuid();
        var loc2 = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        var task1Id = Guid.NewGuid();
        var task2Id = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = loc1, FacilityId = facilityId, LocationCode = "LOC-1", Barcode = "B-1", LocationType = LocationType.Bin },
                new Location { Id = loc2, FacilityId = facilityId, LocationCode = "LOC-2", Barcode = "B-2", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-TEST", Description = "Test Item" });

            // Task 1: Normal priority created 1 hour ago
            seed.PutawayTasks.Add(new PutawayTask
            {
                Id = task1Id,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                SourceLocationId = loc1,
                SuggestedLocationId = loc2,
                Quantity = 10m,
                Status = PutawayTaskStatus.Pending,
                Priority = PutawayPriority.Normal,
                CreatedAtUtc = DateTime.UtcNow.AddHours(-1)
            });

            // Task 2: Expedite priority created 10 minutes ago
            seed.PutawayTasks.Add(new PutawayTask
            {
                Id = task2Id,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                SourceLocationId = loc1,
                SuggestedLocationId = loc2,
                Quantity = 5m,
                Status = PutawayTaskStatus.Pending,
                Priority = PutawayPriority.Expedite,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new GetNextPutawayTaskQueryHandler(context);

        var nextTask = await handler.Handle(new GetNextPutawayTaskQuery(facilityId), CancellationToken.None);

        Assert.NotNull(nextTask);
        Assert.Equal(task2Id, nextTask.Id); // Expedite task selected first
        Assert.Equal(PutawayPriority.Expedite, nextTask.Priority);
    }

    [Fact]
    public async Task AssignPutawayTask_WorkerAssignment_TransitionsToAssigned()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var loc1 = Guid.NewGuid();
        var loc2 = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var workerUserId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = workerUserId, UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = loc1, FacilityId = facilityId, LocationCode = "LOC-1", Barcode = "B-1", LocationType = LocationType.Bin },
                new Location { Id = loc2, FacilityId = facilityId, LocationCode = "LOC-2", Barcode = "B-2", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-ASSIGN", Description = "Assign Test" });

            seed.PutawayTasks.Add(new PutawayTask
            {
                Id = taskId,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                SourceLocationId = loc1,
                SuggestedLocationId = loc2,
                Quantity = 20m,
                Status = PutawayTaskStatus.Pending
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new AssignPutawayTaskCommandHandler(context, currentUser);

        var result = await handler.Handle(new AssignPutawayTaskCommand { TaskId = taskId, UserId = workerUserId }, CancellationToken.None);

        Assert.Equal(PutawayTaskStatus.Assigned, result.Status);
        Assert.Equal(workerUserId, result.AssignedUserId);
    }

    [Fact]
    public async Task AssignPutawayTask_InvalidState_ThrowsInvalidDomainStateException()
    {
        var task = new PutawayTask();
        task.Complete(Guid.NewGuid(), Guid.NewGuid()); // Transition to Completed

        Assert.Throws<InvalidDomainStateException>(() => task.AssignToWorker(Guid.NewGuid()));
    }

    [Fact]
    public async Task ConfirmPutawayTask_LpnMismatch_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var loc1 = Guid.NewGuid();
        var loc2 = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = loc1, FacilityId = facilityId, LocationCode = "STAGE-1", Barcode = "ST-1", LocationType = LocationType.StagingLane },
                new Location { Id = loc2, FacilityId = facilityId, LocationCode = "BIN-1", Barcode = "LOC-BIN-01", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-MISMATCH", Description = "Test Item" });

            seed.PutawayTasks.Add(new PutawayTask
            {
                Id = taskId,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LpnCode = "LPN-REAL-12345",
                SourceLocationId = loc1,
                SuggestedLocationId = loc2,
                Quantity = 10m,
                Status = PutawayTaskStatus.Pending
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new ConfirmPutawayTaskCommandHandler(context, currentUser);

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmPutawayTaskCommand
        {
            TaskId = taskId,
            ScannedLpn = "LPN-WRONG-99999",
            ScannedLocationBarcode = "LOC-BIN-01"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmPutawayTask_DestinationBarcodeMismatch_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var loc1 = Guid.NewGuid();
        var targetBinId = Guid.NewGuid();
        var otherBinId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = loc1, FacilityId = facilityId, LocationCode = "STAGE-1", Barcode = "ST-1", LocationType = LocationType.StagingLane },
                new Location { Id = targetBinId, FacilityId = facilityId, LocationCode = "BIN-SUGGESTED", Barcode = "BAR-SUGGESTED", LocationType = LocationType.Bin },
                new Location { Id = otherBinId, FacilityId = facilityId, LocationCode = "BIN-OTHER", Barcode = "BAR-OTHER", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-LOC-FAIL", Description = "Test Item" });

            seed.PutawayTasks.Add(new PutawayTask
            {
                Id = taskId,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LpnCode = "LPN-PALLET-01",
                SourceLocationId = loc1,
                SuggestedLocationId = targetBinId,
                Quantity = 15m,
                Status = PutawayTaskStatus.Pending
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new ConfirmPutawayTaskCommandHandler(context, currentUser);

        // Operator scanned BAR-OTHER without override permission
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmPutawayTaskCommand
        {
            TaskId = taskId,
            ScannedLpn = "LPN-PALLET-01",
            ScannedLocationBarcode = "BAR-OTHER",
            AllowOverride = false
        }, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmPutawayTask_DualScanSuccess_AtomicallyMovesStockAndLogsTransferTransaction()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var targetBinId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var workerUserId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = workerUserId, UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING_LANE_01", Barcode = "LOC-STAGE-01", LocationType = LocationType.StagingLane },
                new Location { Id = targetBinId, FacilityId = facilityId, LocationCode = "BIN-A-01-01", Barcode = "LOC-BIN-A01", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-DUAL-SCAN", Description = "Dual Scan Goods" });

            // Initial staging inventory
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = stagingLocId,
                QtyAvailable = 50m
            });

            seed.PutawayTasks.Add(new PutawayTask
            {
                Id = taskId,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LpnCode = "LPN-DUAL-001",
                SourceLocationId = stagingLocId,
                SuggestedLocationId = targetBinId,
                Quantity = 50m,
                Status = PutawayTaskStatus.Pending
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new ConfirmPutawayTaskCommandHandler(context, currentUser);

        var result = await handler.Handle(new ConfirmPutawayTaskCommand
        {
            TaskId = taskId,
            ScannedLpn = "LPN-DUAL-001",
            ScannedLocationBarcode = "LOC-BIN-A01"
        }, CancellationToken.None);

        Assert.Equal(PutawayTaskStatus.Completed, result.Status);
        Assert.NotNull(result.CompletedAtUtc);
        Assert.Equal(targetBinId, result.ActualLocationId);

        // Verify StockOnHand Relocation
        var stagingStock = await context.StockOnHand.FirstAsync(s => s.LocationId == stagingLocId && s.ItemId == itemId);
        var binStock = await context.StockOnHand.FirstAsync(s => s.LocationId == targetBinId && s.ItemId == itemId);

        Assert.Equal(0m, stagingStock.QtyAvailable);
        Assert.Equal(50m, binStock.QtyAvailable);

        // Verify Append-only Ledger Transaction
        var transferTx = await context.InventoryTransactions
            .FirstOrDefaultAsync(t => t.ReferenceId == taskId && t.TxType == InventoryTxType.Transfer);

        Assert.NotNull(transferTx);
        Assert.Equal(50m, transferTx.Quantity);
        Assert.Equal(stagingLocId, transferTx.FromLocationId);
        Assert.Equal(targetBinId, transferTx.ToLocationId);
        Assert.Equal("PUTAWAY_TASK", transferTx.ReferenceType);
    }

    [Fact]
    public async Task ReportException_ObstructedBin_SetsExceptionHoldAndDynamicReRoutes()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var blockedBinId = Guid.NewGuid();
        var alternativeBinId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", Barcode = "BAR-ST", LocationType = LocationType.StagingLane },
                new Location { Id = blockedBinId, FacilityId = facilityId, LocationCode = "BIN-BLOCKED-01", Barcode = "BAR-BLOCKED", LocationType = LocationType.Bin },
                new Location { Id = alternativeBinId, FacilityId = facilityId, LocationCode = "BIN-ALTERNATE-02", Barcode = "BAR-ALT", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-EXCEPTION-REROUTE", Description = "Reroute Test Item", StorageTempClass = StorageTempClass.Ambient, WeightKg = 1m });

            seed.PutawayTasks.Add(new PutawayTask
            {
                Id = taskId,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                SourceLocationId = stagingLocId,
                SuggestedLocationId = blockedBinId,
                Quantity = 10m,
                Status = PutawayTaskStatus.Pending
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
        var handler = new ReportPutawayExceptionCommandHandler(context, selector);

        var result = await handler.Handle(new ReportPutawayExceptionCommand
        {
            TaskId = taskId,
            ExceptionReason = "Aisle blocked by maintenance equipment",
            AutoReRoute = true
        }, CancellationToken.None);

        Assert.Equal(PutawayTaskStatus.ReRouted, result.Status);
        Assert.Equal(alternativeBinId, result.SuggestedLocationId);
        Assert.Equal("Aisle blocked by maintenance equipment", result.ExceptionReason);
        Assert.Contains("Dynamic reroute", result.Notes);
    }
}

