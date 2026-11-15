using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Allocation;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public class AllocationEngineAndStrategiesTests
{
    private static (AllocationEngine engine, FifoAllocationStrategy fifo, FefoAllocationStrategy fefo, ManualAllocationStrategy manual, AllocationCandidateFilter filter)
        CreateTestEngine(IApplicationDbContext context)
    {
        var filter = new AllocationCandidateFilter();
        var fifo = new FifoAllocationStrategy(context, filter);
        var fefo = new FefoAllocationStrategy(context, filter);
        var manual = new ManualAllocationStrategy(context, filter);
        var resolver = new AllocationStrategyResolver(new IAllocationStrategy[] { fifo, fefo, manual });
        var engine = new AllocationEngine(context, resolver, filter);
        return (engine, fifo, fefo, manual, filter);
    }

    [Fact]
    public async Task FifoStrategy_OrdersStrictlyByReceivedAtUtc_AndTieBreakers()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var loc1 = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "A-01", LocationType = LocationType.Bin, PickSequence = 10 };
            var loc2 = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "A-02", LocationType = LocationType.Bin, PickSequence = 20 };
            var loc3 = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "A-03", LocationType = LocationType.Bin, PickSequence = 5 };

            seed.Locations.AddRange(loc1, loc2, loc3);
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-FIFO", StorageTempClass = StorageTempClass.Ambient });

            // Stock 1: Received 3 days ago (middle)
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = loc1.Id, QtyAvailable = 50m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-3)
            });

            // Stock 2: Received 10 days ago (OLDEST - should be 1st)
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = loc2.Id, QtyAvailable = 50m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-10)
            });

            // Stock 3: Received today (NEWEST - should be 3rd)
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = loc3.Id, QtyAvailable = 50m, ReceivedAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var (_, fifo, _, _, _) = CreateTestEngine(context);
            var request = new AllocationRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                QuantityRequested = 20m
            };

            var candidates = await fifo.SelectCandidatesAsync(request);

            Assert.Equal(3, candidates.Count);
            Assert.Equal("A-02", candidates[0].Location.LocationCode); // 10 days ago
            Assert.Equal("A-01", candidates[1].Location.LocationCode); // 3 days ago
            Assert.Equal("A-03", candidates[2].Location.LocationCode); // Today
        }
    }

    [Fact]
    public async Task FefoStrategy_OrdersStrictlyByExpirationDate_AndTieBreakers()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var loc1 = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "B-01", LocationType = LocationType.Bin, PickSequence = 10 };
            var loc2 = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "B-02", LocationType = LocationType.Bin, PickSequence = 20 };
            var loc3 = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "B-03", LocationType = LocationType.Bin, PickSequence = 5 };

            seed.Locations.AddRange(loc1, loc2, loc3);
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-FEFO", StorageTempClass = StorageTempClass.Ambient });

            var lotExpiringIn30Days = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-30D", ExpirationDate = DateTime.UtcNow.AddDays(30), QaStatus = LotQaStatus.Released };
            var lotExpiringIn10Days = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-10D", ExpirationDate = DateTime.UtcNow.AddDays(10), QaStatus = LotQaStatus.Released };
            var lotExpiringIn90Days = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-90D", ExpirationDate = DateTime.UtcNow.AddDays(90), QaStatus = LotQaStatus.Released };

            seed.InventoryLots.AddRange(lotExpiringIn30Days, lotExpiringIn10Days, lotExpiringIn90Days);

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = loc1.Id, LotId = lotExpiringIn30Days.Id, QtyAvailable = 30m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-20)
            });

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = loc2.Id, LotId = lotExpiringIn10Days.Id, QtyAvailable = 30m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-1)
            });

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = loc3.Id, LotId = lotExpiringIn90Days.Id, QtyAvailable = 30m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-30)
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var (_, _, fefo, _, _) = CreateTestEngine(context);
            var request = new AllocationRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                QuantityRequested = 25m
            };

            var candidates = await fefo.SelectCandidatesAsync(request);

            Assert.Equal(3, candidates.Count);
            // FEFO orders by earliest expiration date first, regardless of received time:
            Assert.Equal("LOT-10D", candidates[0].Lot?.LotNumber); // Expires in 10 days
            Assert.Equal("LOT-30D", candidates[1].Lot?.LotNumber); // Expires in 30 days
            Assert.Equal("LOT-90D", candidates[2].Lot?.LotNumber); // Expires in 90 days
        }
    }

    [Fact]
    public async Task CandidateFilter_ExcludesQuarantinedAndExpiredStock()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-01", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-EXCLUDE", StorageTempClass = StorageTempClass.Ambient });

            var expiredLot = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-EXPIRED", ExpirationDate = DateTime.UtcNow.AddDays(-1), QaStatus = LotQaStatus.Released };
            var quarantinedLot = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-QUARANTINE", ExpirationDate = DateTime.UtcNow.AddDays(60), QaStatus = LotQaStatus.Quarantine };
            var holdLot = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-HOLD", ExpirationDate = DateTime.UtcNow.AddDays(60), QaStatus = LotQaStatus.Hold };
            var soundLot = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-SOUND", ExpirationDate = DateTime.UtcNow.AddDays(60), QaStatus = LotQaStatus.Released };

            seed.InventoryLots.AddRange(expiredLot, quarantinedLot, holdLot, soundLot);

            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = binId, LotId = expiredLot.Id, QtyAvailable = 20m });
            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = binId, LotId = quarantinedLot.Id, QtyAvailable = 20m });
            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = binId, LotId = holdLot.Id, QtyAvailable = 20m });
            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = binId, LotId = soundLot.Id, QtyAvailable = 20m });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var (_, fifo, _, _, _) = CreateTestEngine(context);
            var request = new AllocationRequest { ClientId = clientId, FacilityId = facilityId, ItemId = itemId, QuantityRequested = 10m };

            var candidates = await fifo.SelectCandidatesAsync(request);

            // Exactly 1 candidate eligible (LOT-SOUND); expired, quarantine, and hold are excluded!
            Assert.Single(candidates);
            Assert.Equal("LOT-SOUND", candidates[0].Lot?.LotNumber);
        }
    }

    [Fact]
    public async Task CandidateFilter_ExcludesLockedLocationsAndStagingLanes()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var stagingLoc = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "STAGING-01", LocationType = LocationType.StagingLane };
            var dockLoc = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "DOCK-01", LocationType = LocationType.DockDoor };
            var lockedLoc = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "LOCKED-BIN", LocationType = LocationType.Bin, IsLocked = true };

            var activeLockLoc = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "CYCLE-LOCK-BIN", LocationType = LocationType.Bin };
            activeLockLoc.Locks.Add(new LocationLock { LocationId = activeLockLoc.Id, LockType = LocationLockType.CycleCount, ReasonCode = "Audit" });

            var regularBin = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "OPEN-BIN", LocationType = LocationType.Bin };

            seed.Locations.AddRange(stagingLoc, dockLoc, lockedLoc, activeLockLoc, regularBin);
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-LOC-TEST", StorageTempClass = StorageTempClass.Ambient });

            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = stagingLoc.Id, QtyAvailable = 50m });
            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = dockLoc.Id, QtyAvailable = 50m });
            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = lockedLoc.Id, QtyAvailable = 50m });
            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = activeLockLoc.Id, QtyAvailable = 50m });
            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = regularBin.Id, QtyAvailable = 50m });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var (_, fifo, _, _, _) = CreateTestEngine(context);
            var request = new AllocationRequest { ClientId = clientId, FacilityId = facilityId, ItemId = itemId, QuantityRequested = 10m };

            var candidates = await fifo.SelectCandidatesAsync(request);

            // Staging lanes, dock doors, locked bins, and cycle-locked locations excluded; only OPEN-BIN remains!
            Assert.Single(candidates);
            Assert.Equal("OPEN-BIN", candidates[0].Location.LocationCode);
        }
    }

    [Fact]
    public async Task CandidateFilter_EnforcesTemperatureClassAndShelfLifePolicy()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var frozenItemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var ambientZone = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facilityId, ZoneCode = "AMB", ZoneType = WarehouseZoneType.BulkStorage };
            var freezerZone = new WarehouseZone { Id = Guid.NewGuid(), FacilityId = facilityId, ZoneCode = "FRZ", ZoneType = WarehouseZoneType.ColdStorage, TempMaxC = -20m };

            seed.WarehouseZones.AddRange(ambientZone, freezerZone);

            var ambientBin = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, ZoneId = ambientZone.Id, LocationCode = "BIN-AMB", LocationType = LocationType.Bin };
            var freezerBin = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, ZoneId = freezerZone.Id, LocationCode = "BIN-FRZ", LocationType = LocationType.Bin };

            seed.Locations.AddRange(ambientBin, freezerBin);

            seed.Items.Add(new Item { Id = frozenItemId, ClientId = clientId, Sku = "SKU-ICE-CREAM", StorageTempClass = StorageTempClass.Frozen });

            // Stock in ambient bin (temperature violation)
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = frozenItemId,
                LocationId = ambientBin.Id, QtyAvailable = 40m
            });

            // Stock in freezer bin (temperature compatible)
            var lot = new InventoryLot
            {
                Id = Guid.NewGuid(), ClientId = clientId, ItemId = frozenItemId, LotNumber = "LOT-FRZ",
                ExpirationDate = DateTime.UtcNow.AddDays(20), QaStatus = LotQaStatus.Released
            };
            seed.InventoryLots.Add(lot);

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = frozenItemId,
                LocationId = freezerBin.Id, LotId = lot.Id, QtyAvailable = 40m
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var (_, fifo, _, _, _) = CreateTestEngine(context);

            // 1. With standard policy (0 days threshold): Freezer bin is selected, ambient bin excluded
            var request = new AllocationRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = frozenItemId,
                QuantityRequested = 10m,
                Policy = new AllocationPolicy { MinDaysBeforeExpiry = 0 }
            };

            var candidates = await fifo.SelectCandidatesAsync(request);
            Assert.Single(candidates);
            Assert.Equal("BIN-FRZ", candidates[0].Location.LocationCode);

            // 2. With shelf-life policy requiring at least 30 days remaining (lot expires in 20 days):
            // Should be excluded due to shelf-life cutoff!
            request.Policy = new AllocationPolicy { MinDaysBeforeExpiry = 30 };
            var candidatesWithStrictPolicy = await fifo.SelectCandidatesAsync(request);
            Assert.Empty(candidatesWithStrictPolicy);
        }
    }

    [Fact]
    public async Task CandidateFilter_EnforcesSpecificLotRestriction()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-01", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-LOTS", StorageTempClass = StorageTempClass.Ambient });

            var lotA = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-ALPHA", ExpirationDate = DateTime.UtcNow.AddDays(100), QaStatus = LotQaStatus.Released };
            var lotB = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-BRAVO", ExpirationDate = DateTime.UtcNow.AddDays(100), QaStatus = LotQaStatus.Released };

            seed.InventoryLots.AddRange(lotA, lotB);
            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = binId, LotId = lotA.Id, QtyAvailable = 50m });
            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = binId, LotId = lotB.Id, QtyAvailable = 50m });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var (_, fifo, _, _, _) = CreateTestEngine(context);
            var request = new AllocationRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                QuantityRequested = 20m,
                RequiredLotNumber = "LOT-BRAVO" // Order specifically requests LOT-BRAVO
            };

            var candidates = await fifo.SelectCandidatesAsync(request);

            Assert.Single(candidates);
            Assert.Equal("LOT-BRAVO", candidates[0].Lot?.LotNumber);
        }
    }

    [Fact]
    public async Task ManualStrategy_PrioritizesDesignatedLocationAndLot()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        var bin1 = Guid.NewGuid();
        var bin2 = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.Add(new Location { Id = bin1, FacilityId = facilityId, LocationCode = "BIN-MANUAL-1", LocationType = LocationType.Bin });
            seed.Locations.Add(new Location { Id = bin2, FacilityId = facilityId, LocationCode = "BIN-MANUAL-2", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-MANUAL", StorageTempClass = StorageTempClass.Ambient });

            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = bin1, QtyAvailable = 25m });
            seed.StockOnHand.Add(new StockOnHand { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = bin2, QtyAvailable = 25m });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var (_, _, _, manual, _) = CreateTestEngine(context);
            var request = new AllocationRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                QuantityRequested = 15m,
                PreferredLocationId = bin2 // Operator designate bin 2
            };

            var candidates = await manual.SelectCandidatesAsync(request);

            Assert.Single(candidates);
            Assert.Equal("BIN-MANUAL-2", candidates[0].Location.LocationCode);
        }
    }

    [Fact]
    public async Task AllocationEngine_PlansMultiBinAllocation_AcrossCandidates()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var bin1 = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "A-BIN-1", LocationType = LocationType.Bin, PickSequence = 10 };
            var bin2 = new Location { Id = Guid.NewGuid(), FacilityId = facilityId, LocationCode = "A-BIN-2", LocationType = LocationType.Bin, PickSequence = 20 };

            seed.Locations.AddRange(bin1, bin2);
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-MULTI-BIN", StorageTempClass = StorageTempClass.Ambient });

            // Bin 1 has 60 units (Received 5 days ago)
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = bin1.Id, QtyAvailable = 60m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-5)
            });

            // Bin 2 has 40 units (Received 3 days ago)
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = bin2.Id, QtyAvailable = 40m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-3)
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var (engine, _, _, _, _) = CreateTestEngine(context);
            var request = new AllocationRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                OutboundOrderLineId = Guid.NewGuid(),
                QuantityRequested = 90m // Demand of 90 requires 60 from bin 1 and 30 from bin 2
            };

            var plan = await engine.PlanLineAllocationAsync(request);

            Assert.Equal(90m, plan.QuantityPlanned);
            Assert.Equal(0m, plan.ShortageQuantity);
            Assert.True(plan.IsFullyPlanned);
            Assert.Equal(2, plan.CandidateAllocations.Count);

            // Candidate 1 (Bin 1 - older stock): 60 allocated
            Assert.Equal("A-BIN-1", plan.CandidateAllocations[0].LocationCode);
            Assert.Equal(60m, plan.CandidateAllocations[0].QuantityAllocated);
            Assert.Equal(0m, plan.CandidateAllocations[0].AvailableInBinRemaining);

            // Candidate 2 (Bin 2 - newer stock): 30 allocated
            Assert.Equal("A-BIN-2", plan.CandidateAllocations[1].LocationCode);
            Assert.Equal(30m, plan.CandidateAllocations[1].QuantityAllocated);
            Assert.Equal(10m, plan.CandidateAllocations[1].AvailableInBinRemaining);
        }
    }

    [Fact]
    public async Task AllocationEngine_DetectsShortage_AndPolicyAllowPartialAllocationRules()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-SHORT", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-SHORTAGE", StorageTempClass = StorageTempClass.Ambient });

            // Only 30 units available in stock
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = binId, QtyAvailable = 30m
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var (engine, _, _, _, _) = CreateTestEngine(context);

            // Case A: AllowPartialAllocation = true (Default)
            var requestA = new AllocationRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                QuantityRequested = 50m,
                Policy = new AllocationPolicy { AllowPartialAllocation = true }
            };

            var planA = await engine.PlanLineAllocationAsync(requestA);
            Assert.Equal(30m, planA.QuantityPlanned);
            Assert.Equal(20m, planA.ShortageQuantity);
            Assert.False(planA.IsFullyPlanned);
            Assert.Single(planA.CandidateAllocations);
            Assert.Single(planA.Shortages);
            Assert.Equal(20m, planA.Shortages[0].ShortageQuantity);

            // Case B: AllowPartialAllocation = false (Must be all or nothing)
            var requestB = new AllocationRequest
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                QuantityRequested = 50m,
                Policy = new AllocationPolicy { AllowPartialAllocation = false }
            };

            var planB = await engine.PlanLineAllocationAsync(requestB);
            Assert.Equal(0m, planB.QuantityPlanned);
            Assert.Equal(50m, planB.ShortageQuantity);
            Assert.Empty(planB.CandidateAllocations); // Aborted completely
            Assert.Single(planB.Shortages);
            Assert.Equal(50m, planB.Shortages[0].ShortageQuantity);
        }
    }

    [Fact]
    public async Task ReadOnlyInvariant_EnsuresZeroInventoryOrLedgerMutationInStep2()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Acme", AccountCode = "ACM" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Main", FacilityCode = "MN-01" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-RO", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-READONLY", StorageTempClass = StorageTempClass.Ambient });

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stockId,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = binId,
                QtyAvailable = 100m,
                QtyAllocated = 0m
            });

            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = clientId,
                FacilityId = facilityId,
                OrderNumber = "ORD-READONLY-01",
                CustomerName = "Client 1",
                Status = OutboundOrderStatus.Released
            };
            order.Lines.Add(new OutboundOrderLine
            {
                ItemId = itemId,
                LineNumber = 1,
                QuantityOrdered = 40m,
                QuantityAllocated = 0m,
                Status = OutboundOrderLineStatus.Pending
            });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var (engine, _, _, _, _) = CreateTestEngine(context);

            // Execute order planning
            var orderPlan = await engine.PlanOrderAllocationAsync(orderId);

            Assert.Equal(40m, orderPlan.TotalQuantityPlanned);
            Assert.True(orderPlan.IsFullyPlanned);

            // STRICT READ-ONLY INVARIANTS:
            // 1. StockOnHand must NOT have changed QtyAvailable or QtyAllocated!
            var stock = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(100m, stock.QtyAvailable);
            Assert.Equal(0m, stock.QtyAllocated);

            // 2. InventoryAllocations table must be completely empty!
            var allocationsCount = await context.InventoryAllocations.CountAsync();
            Assert.Equal(0, allocationsCount);

            // 3. InventoryTransactions ledger table must be completely empty!
            var ledgerCount = await context.InventoryTransactions.CountAsync();
            Assert.Equal(0, ledgerCount);

            // 4. OutboundOrder status must NOT have changed!
            var order = await context.OutboundOrders.FirstAsync(o => o.Id == orderId);
            Assert.Equal(OutboundOrderStatus.Released, order.Status);
        }
    }
}

