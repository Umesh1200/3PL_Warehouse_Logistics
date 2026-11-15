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
using Warehouse3PL.Infrastructure.Services.Picking;
using Warehouse3PL.UnitTests.Common;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Outbound;

public class PickWaveAndRouteOptimizationTests
{
    #region Route Optimizer Tests

    [Fact]
    public void SerpentineRouteOptimizer_SequencesOddAislesAscending_AndEvenAislesDescending()
    {
        var optimizer = new SerpentineRouteOptimizer(NullLogger<SerpentineRouteOptimizer>.Instance);

        var locMap = new Dictionary<Guid, Location>();

        // Aisle 1 (Odd): Bay 1, Bay 3, Bay 5
        var locA1B1 = new Location { Id = Guid.NewGuid(), LocationCode = "AISLE-01-BAY-01-SHELF-1", AisleCode = "01", RackCode = "01", ShelfLevel = 1 };
        var locA1B3 = new Location { Id = Guid.NewGuid(), LocationCode = "AISLE-01-BAY-03-SHELF-1", AisleCode = "01", RackCode = "03", ShelfLevel = 1 };
        var locA1B5 = new Location { Id = Guid.NewGuid(), LocationCode = "AISLE-01-BAY-05-SHELF-1", AisleCode = "01", RackCode = "05", ShelfLevel = 1 };

        // Aisle 2 (Even): Bay 1, Bay 3, Bay 5
        var locA2B1 = new Location { Id = Guid.NewGuid(), LocationCode = "AISLE-02-BAY-01-SHELF-1", AisleCode = "02", RackCode = "01", ShelfLevel = 1 };
        var locA2B3 = new Location { Id = Guid.NewGuid(), LocationCode = "AISLE-02-BAY-03-SHELF-1", AisleCode = "02", RackCode = "03", ShelfLevel = 1 };
        var locA2B5 = new Location { Id = Guid.NewGuid(), LocationCode = "AISLE-02-BAY-05-SHELF-1", AisleCode = "02", RackCode = "05", ShelfLevel = 1 };

        locMap[locA1B1.Id] = locA1B1;
        locMap[locA1B3.Id] = locA1B3;
        locMap[locA1B5.Id] = locA1B5;
        locMap[locA2B1.Id] = locA2B1;
        locMap[locA2B3.Id] = locA2B3;
        locMap[locA2B5.Id] = locA2B5;

        // Provide tasks in deliberately unordered arrangement
        var tasks = new List<PickTask>
        {
            new() { Id = Guid.NewGuid(), SourceLocationId = locA2B1.Id, QuantityToPick = 2m },
            new() { Id = Guid.NewGuid(), SourceLocationId = locA1B5.Id, QuantityToPick = 5m },
            new() { Id = Guid.NewGuid(), SourceLocationId = locA2B5.Id, QuantityToPick = 1m },
            new() { Id = Guid.NewGuid(), SourceLocationId = locA1B1.Id, QuantityToPick = 4m },
            new() { Id = Guid.NewGuid(), SourceLocationId = locA2B3.Id, QuantityToPick = 3m },
            new() { Id = Guid.NewGuid(), SourceLocationId = locA1B3.Id, QuantityToPick = 6m }
        };

        var result = optimizer.OptimizeRoute(tasks, locMap);

        Assert.Equal(6, result.TotalStops);
        Assert.Equal(6, result.SequencedTasks.Count);

        // Verify Serpentine Sequence:
        // Aisle 1 (Odd) ASC: Bay 1 -> Bay 3 -> Bay 5
        Assert.Equal(locA1B1.Id, result.SequencedTasks[0].SourceLocationId);
        Assert.Equal(1, result.SequencedTasks[0].PickSequence);

        Assert.Equal(locA1B3.Id, result.SequencedTasks[1].SourceLocationId);
        Assert.Equal(2, result.SequencedTasks[1].PickSequence);

        Assert.Equal(locA1B5.Id, result.SequencedTasks[2].SourceLocationId);
        Assert.Equal(3, result.SequencedTasks[2].PickSequence);

        // Aisle 2 (Even) DESC: Bay 5 -> Bay 3 -> Bay 1
        Assert.Equal(locA2B5.Id, result.SequencedTasks[3].SourceLocationId);
        Assert.Equal(4, result.SequencedTasks[3].PickSequence);

        Assert.Equal(locA2B3.Id, result.SequencedTasks[4].SourceLocationId);
        Assert.Equal(5, result.SequencedTasks[4].PickSequence);

        Assert.Equal(locA2B1.Id, result.SequencedTasks[5].SourceLocationId);
        Assert.Equal(6, result.SequencedTasks[5].PickSequence);

        Assert.True(result.EstimatedTravelMeters > 0);
        Assert.Empty(result.InaccessibleLocationWarnings);
    }

    [Fact]
    public void SerpentineRouteOptimizer_IsDeterministic()
    {
        var optimizer = new SerpentineRouteOptimizer(NullLogger<SerpentineRouteOptimizer>.Instance);

        var locMap = new Dictionary<Guid, Location>();
        var tasks = new List<PickTask>();

        for (int i = 1; i <= 10; i++)
        {
            var loc = new Location
            {
                Id = Guid.NewGuid(),
                LocationCode = $"AISLE-{(i % 3) + 1:D2}-BAY-{i:D2}",
                AisleCode = $"{(i % 3) + 1:D2}",
                RackCode = $"{i:D2}"
            };
            locMap[loc.Id] = loc;
            tasks.Add(new PickTask { Id = Guid.NewGuid(), SourceLocationId = loc.Id, QuantityToPick = 1m });
        }

        // Run baseline
        var baseline = optimizer.OptimizeRoute(tasks, locMap);
        var baselineOrder = baseline.SequencedTasks.Select(t => t.Id).ToList();

        // Run 5 iterations with shuffled inputs
        var rng = new Random(42);
        for (int run = 0; run < 5; run++)
        {
            var shuffledTasks = tasks.OrderBy(_ => rng.Next()).ToList();
            var runResult = optimizer.OptimizeRoute(shuffledTasks, locMap);
            var runOrder = runResult.SequencedTasks.Select(t => t.Id).ToList();

            Assert.Equal(baselineOrder, runOrder);
        }
    }

    [Fact]
    public void SerpentineRouteOptimizer_FlagsInaccessibleLocations()
    {
        var optimizer = new SerpentineRouteOptimizer(NullLogger<SerpentineRouteOptimizer>.Instance);

        var lockedLoc = new Location
        {
            Id = Guid.NewGuid(),
            LocationCode = "LOC-LOCKED-01",
            AisleCode = "01",
            IsLocked = true
        };

        var locMap = new Dictionary<Guid, Location> { [lockedLoc.Id] = lockedLoc };
        var tasks = new List<PickTask>
        {
            new() { Id = Guid.NewGuid(), SourceLocationId = lockedLoc.Id, QuantityToPick = 5m }
        };

        var result = optimizer.OptimizeRoute(tasks, locMap);

        Assert.Single(result.InaccessibleLocationWarnings);
        Assert.Contains("restricted/inaccessible", result.InaccessibleLocationWarnings[0]);
    }

    #endregion

    #region Task Generator Tests

    [Fact]
    public async Task PickTaskGenerator_GeneratesTasksFromAllocations_PreservingFullTraceability()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var waveId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var allocation = new InventoryAllocation
            {
                Id = allocId,
                ClientId = clientA,
                FacilityId = facilityId,
                OutboundOrderId = orderId,
                OutboundOrderLineId = lineId,
                StockOnHandId = stockId,
                ItemId = itemId,
                LocationId = binId,
                LpnCode = "PALLET-100",
                QuantityRequested = 25m,
                QuantityAllocated = 25m,
                QuantityReleased = 0m,
                Status = AllocationStatus.Allocated
            };
            seed.InventoryAllocations.Add(allocation);
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var generator = new PickTaskGenerator(context, NullLogger<PickTaskGenerator>.Instance);
            var allocation = await context.InventoryAllocations.FirstAsync(a => a.Id == allocId);

            var result = await generator.GenerateTasksForAllocationsAsync(waveId, new[] { allocation });

            Assert.Single(result.GeneratedTasks);
            Assert.Empty(result.ExistingTasks);
            Assert.Equal(25m, result.TotalQuantityToPick);

            var task = result.GeneratedTasks[0];
            Assert.Equal(waveId, task.PickWaveId);
            Assert.Equal(orderId, task.OutboundOrderId);
            Assert.Equal(lineId, task.OutboundOrderLineId);
            Assert.Equal(allocId, task.InventoryAllocationId);
            Assert.Equal(stockId, task.StockOnHandId);
            Assert.Equal(itemId, task.ItemId);
            Assert.Equal(binId, task.SourceLocationId);
            Assert.Equal("PALLET-100", task.ExpectedLpnCode);
            Assert.Equal(25m, task.QuantityToPick);
            Assert.Equal(0m, task.QuantityPicked);
            Assert.Equal(PickTaskStatus.Pending, task.Status);
        }
    }

    [Fact]
    public async Task PickTaskGenerator_DuplicatePrevention_DoesNotDuplicateTasksWhenAlreadyTasked()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var waveId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var allocation = new InventoryAllocation
            {
                Id = allocId,
                ClientId = clientA,
                FacilityId = facilityId,
                QuantityRequested = 10m,
                QuantityAllocated = 10m,
                QuantityReleased = 0m,
                Status = AllocationStatus.Allocated
            };
            seed.InventoryAllocations.Add(allocation);

            // Existing task already covering the full 10 units
            var existingTask = new PickTask
            {
                Id = Guid.NewGuid(),
                ClientId = clientA,
                FacilityId = facilityId,
                PickWaveId = waveId,
                InventoryAllocationId = allocId,
                QuantityToPick = 10m,
                Status = PickTaskStatus.Pending
            };
            seed.PickTasks.Add(existingTask);
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var generator = new PickTaskGenerator(context, NullLogger<PickTaskGenerator>.Instance);
            var allocation = await context.InventoryAllocations.FirstAsync(a => a.Id == allocId);

            var result = await generator.GenerateTasksForAllocationsAsync(waveId, new[] { allocation });

            // Zero new tasks generated because allocation is already fully tasked
            Assert.Empty(result.GeneratedTasks);
            Assert.Single(result.ExistingTasks);
            Assert.Equal(10m, result.TotalQuantityToPick);
        }
    }

    [Fact]
    public async Task PickTaskGenerator_QuantityIntegrity_SupportsPartialRemainingAndProtectsAgainstExceeding()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var waveId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var allocation = new InventoryAllocation
            {
                Id = allocId,
                ClientId = clientA,
                FacilityId = facilityId,
                QuantityRequested = 30m,
                QuantityAllocated = 30m,
                QuantityReleased = 0m,
                Status = AllocationStatus.Allocated
            };
            seed.InventoryAllocations.Add(allocation);

            // Existing task covering 18 units
            var existingTask = new PickTask
            {
                Id = Guid.NewGuid(),
                ClientId = clientA,
                FacilityId = facilityId,
                PickWaveId = waveId,
                InventoryAllocationId = allocId,
                QuantityToPick = 18m,
                Status = PickTaskStatus.Assigned
            };
            seed.PickTasks.Add(existingTask);
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var generator = new PickTaskGenerator(context, NullLogger<PickTaskGenerator>.Instance);
            var allocation = await context.InventoryAllocations.FirstAsync(a => a.Id == allocId);

            var result = await generator.GenerateTasksForAllocationsAsync(waveId, new[] { allocation });

            // Exactly 30 - 18 = 12 units generated
            Assert.Single(result.GeneratedTasks);
            Assert.Equal(12m, result.GeneratedTasks[0].QuantityToPick);
            Assert.Single(result.ExistingTasks);
            Assert.Equal(30m, result.TotalQuantityToPick);
        }
    }

    #endregion

    #region Wave Generator Tests

    [Fact]
    public async Task PickWaveGenerator_SelectsEligibleOrders_AndEnforcesPriority()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Priority Client", AccountCode = "PRI-01" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Priority Facility", FacilityCode = "PF-01" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientA, Sku = "SKU-PRI", Description = "Pri SKU" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "A-01", AisleCode = "01", RackCode = "01" });

            // Create 3 orders with different priorities: 1 (Normal), 3 (Urgent), 2 (High)
            var orderNormal = CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-NORM", priority: 1, 10m);
            var orderUrgent = CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-URGENT", priority: 3, 10m);
            var orderHigh = CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-HIGH", priority: 2, 10m);

            seed.OutboundOrders.AddRange(orderNormal, orderUrgent, orderHigh);
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var taskGenerator = new PickTaskGenerator(context, NullLogger<PickTaskGenerator>.Instance);
            var routeOptimizer = new SerpentineRouteOptimizer(NullLogger<SerpentineRouteOptimizer>.Instance);
            var waveGenerator = new PickWaveGenerator(context, taskGenerator, routeOptimizer, NullLogger<PickWaveGenerator>.Instance);

            var result = await waveGenerator.GenerateWaveAsync(new PickWaveGenerationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                WaveType = PickWaveType.BatchPick,
                MaxOrders = 5
            });

            Assert.NotNull(result.Wave);
            Assert.Equal(3, result.OrdersCount);

            // Sequence should respect Priority DESC (Urgent -> High -> Normal)
            var waveOrders = result.Wave.WaveOrders.OrderBy(wo => wo.SequenceNumber).ToList();
            Assert.Equal("ORD-URGENT", waveOrders[0].OutboundOrder.OrderNumber);
            Assert.Equal("ORD-HIGH", waveOrders[1].OutboundOrder.OrderNumber);
            Assert.Equal("ORD-NORM", waveOrders[2].OutboundOrder.OrderNumber);
        }
    }

    [Fact]
    public async Task PickWaveGenerator_ExcludesOnHold_Cancelled_AndCompletedOrders()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Status Client", AccountCode = "STA-01" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Status Facility", FacilityCode = "SF-01" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientA, Sku = "SKU-STA", Description = "Sta SKU" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "A-01", AisleCode = "01", RackCode = "01" });

            var orderHeld = CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-HELD", 1, 10m, OutboundOrderStatus.OnHold);
            var orderCancelled = CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-CANCELLED", 1, 10m, OutboundOrderStatus.Cancelled);
            var orderCompleted = CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-COMPLETED", 1, 10m, OutboundOrderStatus.Completed);
            var orderActive = CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-ACTIVE", 1, 10m, OutboundOrderStatus.Allocated);

            seed.OutboundOrders.AddRange(orderHeld, orderCancelled, orderCompleted, orderActive);
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var taskGenerator = new PickTaskGenerator(context, NullLogger<PickTaskGenerator>.Instance);
            var routeOptimizer = new SerpentineRouteOptimizer(NullLogger<SerpentineRouteOptimizer>.Instance);
            var waveGenerator = new PickWaveGenerator(context, taskGenerator, routeOptimizer, NullLogger<PickWaveGenerator>.Instance);

            var result = await waveGenerator.GenerateWaveAsync(new PickWaveGenerationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                WaveType = PickWaveType.BatchPick
            });

            // Only ORD-ACTIVE must be included
            Assert.Single(result.Wave.WaveOrders);
            Assert.Equal("ORD-ACTIVE", result.Wave.WaveOrders.First().OutboundOrder.OrderNumber);
        }
    }

    [Fact]
    public async Task PickWaveGenerator_PreventsConflictingActiveWaves_ForSameOrder()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Conflict Client", AccountCode = "CC-01" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Conflict Facility", FacilityCode = "CF-01" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientA, Sku = "SKU-CC", Description = "CC SKU" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "A-01", AisleCode = "01", RackCode = "01" });

            var order = CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-WAVED", 1, 10m, OutboundOrderStatus.Allocated, orderId);
            seed.OutboundOrders.Add(order);

            // Place order into active planning wave
            var activeWave = new PickWave
            {
                ClientId = clientA,
                FacilityId = facilityId,
                WaveNumber = "WAVE-ACTIVE-01",
                Status = PickWaveStatus.Planning
            };
            activeWave.WaveOrders.Add(new PickWaveOrder
            {
                PickWaveId = activeWave.Id,
                OutboundOrderId = orderId,
                SequenceNumber = 1
            });
            seed.PickWaves.Add(activeWave);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var taskGenerator = new PickTaskGenerator(context, NullLogger<PickTaskGenerator>.Instance);
            var routeOptimizer = new SerpentineRouteOptimizer(NullLogger<SerpentineRouteOptimizer>.Instance);
            var waveGenerator = new PickWaveGenerator(context, taskGenerator, routeOptimizer, NullLogger<PickWaveGenerator>.Instance);

            // Attempting to generate wave specifically for this already-waved order must fail
            await Assert.ThrowsAsync<InvalidDomainStateException>(() => waveGenerator.GenerateWaveAsync(new PickWaveGenerationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                OrderIds = new[] { orderId }
            }));
        }
    }

    [Fact]
    public async Task PickWaveGenerator_EnforcesThermalSegregation()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var binId = Guid.NewGuid();

        var frozenItemId = Guid.NewGuid();
        var ambientItemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Temp Client", AccountCode = "TC-01" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Temp Facility", FacilityCode = "TF-01" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "A-01", AisleCode = "01", RackCode = "01" });

            seed.Items.Add(new Item { Id = frozenItemId, ClientId = clientA, Sku = "SKU-ICE", Description = "Frozen Ice Cream", StorageTempClass = StorageTempClass.Frozen });
            seed.Items.Add(new Item { Id = ambientItemId, ClientId = clientA, Sku = "SKU-CHIPS", Description = "Ambient Chips", StorageTempClass = StorageTempClass.Ambient });

            var frozenOrder = CreateOrderWithAllocation(clientA, facilityId, frozenItemId, binId, "ORD-FROZEN", 1, 10m);
            var ambientOrder = CreateOrderWithAllocation(clientA, facilityId, ambientItemId, binId, "ORD-AMBIENT", 1, 10m);

            seed.OutboundOrders.AddRange(frozenOrder, ambientOrder);
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var taskGenerator = new PickTaskGenerator(context, NullLogger<PickTaskGenerator>.Instance);
            var routeOptimizer = new SerpentineRouteOptimizer(NullLogger<SerpentineRouteOptimizer>.Instance);
            var waveGenerator = new PickWaveGenerator(context, taskGenerator, routeOptimizer, NullLogger<PickWaveGenerator>.Instance);

            // Request Frozen wave
            var result = await waveGenerator.GenerateWaveAsync(new PickWaveGenerationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                TempClass = StorageTempClass.Frozen
            });

            Assert.Single(result.Wave.WaveOrders);
            Assert.Equal("ORD-FROZEN", result.Wave.WaveOrders.First().OutboundOrder.OrderNumber);
            Assert.Equal(StorageTempClass.Frozen, result.Wave.TempClass);
        }
    }

    [Fact]
    public async Task PickWaveGenerator_RejectsCrossTenantAndCrossFacilityOrders()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityA = Guid.NewGuid();
        var facilityB = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var orderBId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CLA" });
            seed.Clients.Add(new Client { Id = clientB, CompanyName = "Client B", AccountCode = "CLB" });
            seed.Facilities.Add(new Facility { Id = facilityA, Name = "Facility A", FacilityCode = "FA" });
            seed.Facilities.Add(new Facility { Id = facilityB, Name = "Facility B", FacilityCode = "FB" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityA, LocationCode = "A-01", AisleCode = "01", RackCode = "01" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientB, Sku = "SKU-B", Description = "Item B" });

            var orderClientB = CreateOrderWithAllocation(clientB, facilityA, itemId, binId, "ORD-CLB", 1, 10m, OutboundOrderStatus.Allocated, orderBId);
            seed.OutboundOrders.Add(orderClientB);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var taskGenerator = new PickTaskGenerator(context, NullLogger<PickTaskGenerator>.Instance);
            var routeOptimizer = new SerpentineRouteOptimizer(NullLogger<SerpentineRouteOptimizer>.Instance);
            var waveGenerator = new PickWaveGenerator(context, taskGenerator, routeOptimizer, NullLogger<PickWaveGenerator>.Instance);

            // Attempting to wave Client B order under Client A request must be rejected
            await Assert.ThrowsAsync<DomainException>(() => waveGenerator.GenerateWaveAsync(new PickWaveGenerationRequest
            {
                ClientId = clientA,
                FacilityId = facilityA,
                OrderIds = new[] { orderBId }
            }));
        }
    }

    [Fact]
    public async Task PickWaveGenerator_WaveTypes_DiscreteOrderAndZonePick_EnforceRules()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CLA" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Facility A", FacilityCode = "FA" });
            seed.WarehouseZones.Add(new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZONE-A", Name = "Pick Zone A" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "A-01", AisleCode = "01", RackCode = "01" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientA, Sku = "SKU-A", Description = "Item A" });

            seed.OutboundOrders.Add(CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-1", 1, 10m));
            seed.OutboundOrders.Add(CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-2", 1, 10m));

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var taskGenerator = new PickTaskGenerator(context, NullLogger<PickTaskGenerator>.Instance);
            var routeOptimizer = new SerpentineRouteOptimizer(NullLogger<SerpentineRouteOptimizer>.Instance);
            var waveGenerator = new PickWaveGenerator(context, taskGenerator, routeOptimizer, NullLogger<PickWaveGenerator>.Instance);

            // 1. DiscreteOrder cannot accept multiple requested orders
            await Assert.ThrowsAsync<ValidationException>(() => waveGenerator.GenerateWaveAsync(new PickWaveGenerationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                WaveType = PickWaveType.DiscreteOrder,
                OrderIds = new[] { Guid.NewGuid(), Guid.NewGuid() }
            }));

            // 2. ZonePick requires ZoneId
            await Assert.ThrowsAsync<ValidationException>(() => waveGenerator.GenerateWaveAsync(new PickWaveGenerationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                WaveType = PickWaveType.ZonePick,
                ZoneId = null
            }));

            // 3. ZonePick with ZoneId succeeds
            var zoneResult = await waveGenerator.GenerateWaveAsync(new PickWaveGenerationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                WaveType = PickWaveType.ZonePick,
                ZoneId = zoneId
            });

            Assert.Equal(PickWaveType.ZonePick, zoneResult.Wave.WaveType);
            Assert.Equal(zoneId, zoneResult.Wave.ZoneId);
        }
    }

    [Fact]
    public async Task PickWaveExecution_ZeroStockMutation_AndZeroLedgerEntries_Invariant()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CLA" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Facility A", FacilityCode = "FA" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "A-01", AisleCode = "01", RackCode = "01" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientA, Sku = "SKU-SAFE", Description = "Safe Item" });

            var stock = new StockOnHand
            {
                Id = stockId,
                ClientId = clientA,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = binId,
                QtyAvailable = 100m,
                QtyAllocated = 20m,
                Version = 5
            };
            seed.StockOnHand.Add(stock);

            var order = CreateOrderWithAllocation(clientA, facilityId, itemId, binId, "ORD-SAFE", 1, 20m, OutboundOrderStatus.Allocated, orderId, stockId);
            seed.OutboundOrders.Add(order);

            // Record initial inventory transaction ledger count
            seed.InventoryTransactions.Add(new InventoryTransaction
            {
                ClientId = clientA,
                FacilityId = facilityId,
                ItemId = itemId,
                FromLocationId = binId,
                Quantity = 120m,
                TxType = InventoryTxType.Receive
            });

            await seed.SaveChangesAsync();
        }

        // Execute wave and task generation
        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var taskGenerator = new PickTaskGenerator(context, NullLogger<PickTaskGenerator>.Instance);
            var routeOptimizer = new SerpentineRouteOptimizer(NullLogger<SerpentineRouteOptimizer>.Instance);
            var waveGenerator = new PickWaveGenerator(context, taskGenerator, routeOptimizer, NullLogger<PickWaveGenerator>.Instance);

            var initialLedgerCount = await context.InventoryTransactions.CountAsync();
            var initialStock = await context.StockOnHand.AsNoTracking().FirstAsync(s => s.Id == stockId);

            var result = await waveGenerator.GenerateWaveAsync(new PickWaveGenerationRequest
            {
                ClientId = clientA,
                FacilityId = facilityId,
                WaveType = PickWaveType.BatchPick,
                OrderIds = new[] { orderId }
            });

            // Verify wave and tasks were generated
            Assert.NotNull(result.Wave);
            Assert.Single(result.Tasks);
            Assert.Equal(20m, result.Tasks[0].QuantityToPick);

            // Invariant Verification: Zero mutation to StockOnHand
            var afterStock = await context.StockOnHand.AsNoTracking().FirstAsync(s => s.Id == stockId);
            Assert.Equal(initialStock.QtyAvailable, afterStock.QtyAvailable);
            Assert.Equal(initialStock.QtyAllocated, afterStock.QtyAllocated);
            Assert.Equal(initialStock.Version, afterStock.Version);

            // Invariant Verification: Zero new inventory transaction ledger rows
            var afterLedgerCount = await context.InventoryTransactions.CountAsync();
            Assert.Equal(initialLedgerCount, afterLedgerCount);
        }
    }

    #endregion

    #region Helper Methods

    private static OutboundOrder CreateOrderWithAllocation(
        Guid clientId,
        Guid facilityId,
        Guid itemId,
        Guid locationId,
        string orderNumber,
        int priority,
        decimal quantity,
        OutboundOrderStatus status = OutboundOrderStatus.Allocated,
        Guid? orderId = null,
        Guid? stockId = null)
    {
        var realOrderId = orderId ?? Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var realStockId = stockId ?? Guid.NewGuid();

        var order = new OutboundOrder
        {
            Id = realOrderId,
            ClientId = clientId,
            FacilityId = facilityId,
            OrderNumber = orderNumber,
            CustomerName = $"Customer {orderNumber}",
            Priority = priority,
            Status = status
        };

        var line = new OutboundOrderLine
        {
            Id = lineId,
            OutboundOrderId = realOrderId,
            ItemId = itemId,
            LineNumber = 1,
            QuantityOrdered = quantity,
            QuantityAllocated = status == OutboundOrderStatus.Allocated ? quantity : 0m,
            Status = status == OutboundOrderStatus.Allocated ? OutboundOrderLineStatus.Allocated : OutboundOrderLineStatus.Pending
        };
        order.Lines.Add(line);

        if (status != OutboundOrderStatus.Cancelled && status != OutboundOrderStatus.Draft)
        {
            var allocation = new InventoryAllocation
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                FacilityId = facilityId,
                OutboundOrderId = realOrderId,
                OutboundOrderLineId = lineId,
                StockOnHandId = realStockId,
                ItemId = itemId,
                LocationId = locationId,
                QuantityRequested = quantity,
                QuantityAllocated = quantity,
                QuantityReleased = 0m,
                Status = AllocationStatus.Allocated
            };
            order.Allocations.Add(allocation);
        }

        return order;
    }

    #endregion
}

