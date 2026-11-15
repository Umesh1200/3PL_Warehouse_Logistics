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

public class AllocationDomainAndPersistenceTests
{
    [Fact]
    public void InventoryAllocation_ActiveAllocatedQuantity_CalculatesAccurately()
    {
        var allocation = new InventoryAllocation
        {
            QuantityRequested = 25m,
            QuantityAllocated = 25m,
            QuantityReleased = 10m
        };

        Assert.Equal(15m, allocation.ActiveAllocatedQuantity);

        // Released exceeds allocated (edge case safeguard)
        allocation.QuantityReleased = 30m;
        Assert.Equal(0m, allocation.ActiveAllocatedQuantity);
    }

    [Fact]
    public void InventoryAllocation_Release_ValidatesQuantityAndUpdatesStatus()
    {
        var userId = Guid.NewGuid();
        var allocation = new InventoryAllocation
        {
            QuantityRequested = 20m,
            QuantityAllocated = 20m,
            QuantityReleased = 0m,
            Status = AllocationStatus.Allocated
        };

        // 1. Cannot release zero or negative
        Assert.Throws<ValidationException>(() => allocation.Release(0m, userId, "Invalid qty"));
        Assert.Throws<ValidationException>(() => allocation.Release(-5m, userId, "Invalid qty"));

        // 2. Cannot release without reason
        Assert.Throws<ValidationException>(() => allocation.Release(5m, userId, "   "));

        // 3. Cannot release more than active quantity
        Assert.Throws<ValidationException>(() => allocation.Release(25m, userId, "Too much"));

        // 4. Partial release keeps status Allocated
        allocation.Release(8m, userId, "Partial order reduction");
        Assert.Equal(8m, allocation.QuantityReleased);
        Assert.Equal(12m, allocation.ActiveAllocatedQuantity);
        Assert.Equal(AllocationStatus.Allocated, allocation.Status);
        Assert.Equal("Partial order reduction", allocation.ReleaseReason);
        Assert.Equal(userId, allocation.ReleasedByUserId);
        Assert.NotNull(allocation.ReleasedAtUtc);

        // 5. Complete release sets status Released
        allocation.Release(12m, userId, "Customer cancelled item");
        Assert.Equal(20m, allocation.QuantityReleased);
        Assert.Equal(0m, allocation.ActiveAllocatedQuantity);
        Assert.Equal(AllocationStatus.Released, allocation.Status);

        // 6. Cannot release again once fully released
        Assert.Throws<InvalidDomainStateException>(() => allocation.Release(1m, userId, "Extra release"));
    }

    [Fact]
    public void InventoryAllocation_Cancel_ReleasesActiveQuantityAndSetsCancelled()
    {
        var userId = Guid.NewGuid();
        var allocation = new InventoryAllocation
        {
            QuantityRequested = 15m,
            QuantityAllocated = 15m,
            QuantityReleased = 5m,
            Status = AllocationStatus.Allocated
        };

        allocation.Cancel(userId, "Order aborted");

        Assert.Equal(15m, allocation.QuantityReleased);
        Assert.Equal(0m, allocation.ActiveAllocatedQuantity);
        Assert.Equal(AllocationStatus.Cancelled, allocation.Status);
        Assert.Equal("Order aborted", allocation.ReleaseReason);
    }

    [Fact]
    public void OutboundOrder_RecalculateAllocationStatus_ReturnsToReleasedWhenAllDeallocated()
    {
        var order = new OutboundOrder
        {
            OrderNumber = "ORD-RECALC-01",
            Status = OutboundOrderStatus.Allocated
        };

        var line1 = new OutboundOrderLine
        {
            LineNumber = 1,
            QuantityOrdered = 20m,
            QuantityAllocated = 20m,
            Status = OutboundOrderLineStatus.Allocated
        };
        var line2 = new OutboundOrderLine
        {
            LineNumber = 2,
            QuantityOrdered = 10m,
            QuantityAllocated = 10m,
            Status = OutboundOrderLineStatus.Allocated
        };
        order.Lines.Add(line1);
        order.Lines.Add(line2);

        // Deallocate all stock from both lines
        line1.Deallocate(20m);
        line2.Deallocate(10m);

        Assert.Equal(0m, line1.QuantityAllocated);
        Assert.Equal(OutboundOrderLineStatus.Pending, line1.Status);
        Assert.Equal(0m, line2.QuantityAllocated);
        Assert.Equal(OutboundOrderLineStatus.Pending, line2.Status);

        order.RecalculateAllocationStatus();

        Assert.Equal(OutboundOrderStatus.Released, order.Status);
        Assert.Contains(order.StatusHistories, h =>
            h.FromStatus == OutboundOrderStatus.Allocated &&
            h.ToStatus == OutboundOrderStatus.Released);
    }

    [Fact]
    public void OutboundOrder_RecalculateAllocationStatus_TransitionsPartiallyAllocatedAndAllocated()
    {
        var order = new OutboundOrder
        {
            OrderNumber = "ORD-RECALC-02",
            Status = OutboundOrderStatus.Released
        };

        var line1 = new OutboundOrderLine { LineNumber = 1, QuantityOrdered = 50m, QuantityAllocated = 0m };
        var line2 = new OutboundOrderLine { LineNumber = 2, QuantityOrdered = 50m, QuantityAllocated = 0m };
        order.Lines.Add(line1);
        order.Lines.Add(line2);

        // 1. Partial allocation of line 1
        line1.Allocate(30m);
        order.RecalculateAllocationStatus();
        Assert.Equal(OutboundOrderStatus.PartiallyAllocated, order.Status);

        // 2. Full allocation of line 1, line 2 remains unallocated
        line1.Allocate(20m);
        order.RecalculateAllocationStatus();
        Assert.Equal(OutboundOrderStatus.PartiallyAllocated, order.Status);

        // 3. Full allocation of line 2 -> Order becomes Allocated
        line2.Allocate(50m);
        order.RecalculateAllocationStatus();
        Assert.Equal(OutboundOrderStatus.Allocated, order.Status);
    }

    [Fact]
    public void OutboundOrder_RecalculateAllocationStatus_PreservesOnHoldStateAndUpdatesStatusBeforeHold()
    {
        var userId = Guid.NewGuid();
        var order = new OutboundOrder
        {
            OrderNumber = "ORD-HOLD-RECALC",
            Status = OutboundOrderStatus.Released
        };

        var line = new OutboundOrderLine { LineNumber = 1, QuantityOrdered = 40m, QuantityAllocated = 40m };
        order.Lines.Add(line);
        order.RecalculateAllocationStatus();
        Assert.Equal(OutboundOrderStatus.Allocated, order.Status);

        // Put order on hold
        order.Hold("Compliance inspection", userId);
        Assert.Equal(OutboundOrderStatus.OnHold, order.Status);
        Assert.Equal(OutboundOrderStatus.Allocated, order.StatusBeforeHold);

        // Deallocate half while on hold
        line.Deallocate(20m);
        order.RecalculateAllocationStatus();

        // INVARIANT: Status remains OnHold, but StatusBeforeHold is updated to PartiallyAllocated!
        Assert.Equal(OutboundOrderStatus.OnHold, order.Status);
        Assert.Equal(OutboundOrderStatus.PartiallyAllocated, order.StatusBeforeHold);

        // Deallocate remaining while on hold
        line.Deallocate(20m);
        order.RecalculateAllocationStatus();

        // INVARIANT: Status remains OnHold, StatusBeforeHold is now Released!
        Assert.Equal(OutboundOrderStatus.OnHold, order.Status);
        Assert.Equal(OutboundOrderStatus.Released, order.StatusBeforeHold);

        // Release hold -> Restores to Released (not stale Allocated)
        order.ReleaseHold(userId);
        Assert.Equal(OutboundOrderStatus.Released, order.Status);
    }

    [Fact]
    public void MultiBinAllocation_SupportsMultipleAllocationsPerOrderLine()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var bin1 = Guid.NewGuid();
        var bin2 = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var line = new OutboundOrderLine
        {
            Id = lineId,
            OutboundOrderId = orderId,
            ItemId = itemId,
            LineNumber = 1,
            QuantityOrdered = 100m
        };

        var alloc1 = new InventoryAllocation
        {
            OutboundOrderId = orderId,
            OutboundOrderLineId = lineId,
            ItemId = itemId,
            LocationId = bin1,
            LpnCode = "LPN-BIN-01",
            QuantityRequested = 60m,
            QuantityAllocated = 60m
        };

        var alloc2 = new InventoryAllocation
        {
            OutboundOrderId = orderId,
            OutboundOrderLineId = lineId,
            ItemId = itemId,
            LocationId = bin2,
            LpnCode = "LPN-BIN-02",
            QuantityRequested = 40m,
            QuantityAllocated = 40m
        };

        line.Allocations.Add(alloc1);
        line.Allocations.Add(alloc2);
        line.Allocate(alloc1.QuantityAllocated + alloc2.QuantityAllocated);

        Assert.Equal(100m, line.QuantityAllocated);
        Assert.Equal(OutboundOrderLineStatus.Allocated, line.Status);
        Assert.Equal(2, line.Allocations.Count);

        // Release alloc1 from bin 1
        alloc1.Release(60m, userId, "Bin 1 inventory consolidation");
        line.Deallocate(60m);

        Assert.Equal(40m, line.QuantityAllocated);
        Assert.Equal(OutboundOrderLineStatus.PartiallyAllocated, line.Status);
        Assert.Equal(0m, alloc1.ActiveAllocatedQuantity);
        Assert.Equal(40m, alloc2.ActiveAllocatedQuantity);
    }

    [Fact]
    public void AllocationShortage_CreationAndResolution_TracksRootCause()
    {
        var shortage = new AllocationShortage
        {
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            OutboundOrderId = Guid.NewGuid(),
            OutboundOrderLineId = Guid.NewGuid(),
            ItemId = Guid.NewGuid(),
            QuantityRequested = 50m,
            QuantityAllocated = 20m,
            ShortageQuantity = 30m,
            Reason = ShortageReason.InsufficientStock,
            Notes = "On-hand stock depleted across pick zones"
        };

        Assert.False(shortage.IsResolved);
        Assert.Null(shortage.ResolvedAtUtc);
        Assert.Equal(30m, shortage.ShortageQuantity);
        Assert.Equal(ShortageReason.InsufficientStock, shortage.Reason);

        var resolutionTime = DateTime.UtcNow;
        shortage.Resolve(resolutionTime);

        Assert.True(shortage.IsResolved);
        Assert.Equal(resolutionTime, shortage.ResolvedAtUtc);
    }

    [Fact]
    public async Task EFCore_Persistence_InventoryAllocation_Shortage_Policy_And_QueryFilters()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientA, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientA, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CLA" });
            seed.Clients.Add(new Client { Id = clientB, CompanyName = "Client B", AccountCode = "CLB" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Chicago Hub", FacilityCode = "ORD-01" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-A-01", Barcode = "BAR-BIN-01", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientA, Sku = "SKU-ALLOC-TEST", Description = "Test Item" });

            var receivedTime = DateTime.UtcNow.AddDays(-5);
            var stock = new StockOnHand
            {
                Id = stockId,
                ClientId = clientA,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = binId,
                QtyAvailable = 100m,
                QtyAllocated = 25m,
                ReceivedAtUtc = receivedTime
            };
            seed.StockOnHand.Add(stock);

            var policy = new AllocationPolicy
            {
                ClientId = clientA,
                FacilityId = facilityId,
                DefaultStrategy = AllocationStrategyType.FIFO,
                AllowPartialAllocation = true,
                AllowMixedLots = false,
                MinDaysBeforeExpiry = 30
            };
            seed.AllocationPolicies.Add(policy);

            var orderA = new OutboundOrder
            {
                ClientId = clientA,
                FacilityId = facilityId,
                OrderNumber = "ORD-PERSIST-A",
                CustomerName = "Cust A",
                Status = OutboundOrderStatus.PartiallyAllocated
            };
            var lineA = new OutboundOrderLine
            {
                ItemId = itemId,
                LineNumber = 1,
                QuantityOrdered = 40m,
                QuantityAllocated = 25m,
                Status = OutboundOrderLineStatus.PartiallyAllocated
            };
            orderA.Lines.Add(lineA);
            seed.OutboundOrders.Add(orderA);

            var allocation = new InventoryAllocation
            {
                ClientId = clientA,
                FacilityId = facilityId,
                OutboundOrderId = orderA.Id,
                OutboundOrderLineId = lineA.Id,
                StockOnHandId = stockId,
                ItemId = itemId,
                LocationId = binId,
                QuantityRequested = 40m,
                QuantityAllocated = 25m,
                QuantityReleased = 0m,
                StrategyUsed = AllocationStrategyType.FIFO,
                Status = AllocationStatus.Allocated
            };
            seed.InventoryAllocations.Add(allocation);

            var shortage = new AllocationShortage
            {
                ClientId = clientA,
                FacilityId = facilityId,
                OutboundOrderId = orderA.Id,
                OutboundOrderLineId = lineA.Id,
                ItemId = itemId,
                QuantityRequested = 40m,
                QuantityAllocated = 25m,
                ShortageQuantity = 15m,
                Reason = ShortageReason.InsufficientStock
            };
            seed.AllocationShortages.Add(shortage);

            // Client B Order (should be filtered out by tenant isolation)
            var orderB = new OutboundOrder
            {
                ClientId = clientB,
                FacilityId = facilityId,
                OrderNumber = "ORD-PERSIST-B",
                CustomerName = "Cust B",
                Status = OutboundOrderStatus.Released
            };
            seed.OutboundOrders.Add(orderB);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            // 1. StockOnHand.ReceivedAtUtc persistence
            var savedStock = await context.StockOnHand.FirstOrDefaultAsync(s => s.Id == stockId);
            Assert.NotNull(savedStock);
            Assert.NotNull(savedStock.ReceivedAtUtc);

            // 2. Allocation Policy retrieval
            var savedPolicy = await context.AllocationPolicies.FirstOrDefaultAsync(p => p.ClientId == clientA);
            Assert.NotNull(savedPolicy);
            Assert.Equal(AllocationStrategyType.FIFO, savedPolicy.DefaultStrategy);
            Assert.False(savedPolicy.AllowMixedLots);
            Assert.Equal(30, savedPolicy.MinDaysBeforeExpiry);

            // 3. Inventory Allocation persistence & tenant filter
            var allocations = await context.InventoryAllocations.ToListAsync();
            Assert.Single(allocations);
            Assert.Equal(25m, allocations[0].QuantityAllocated);
            Assert.Equal(25m, allocations[0].ActiveAllocatedQuantity);
            Assert.Equal(AllocationStatus.Allocated, allocations[0].Status);

            // 4. Allocation Shortage persistence
            var shortages = await context.AllocationShortages.ToListAsync();
            Assert.Single(shortages);
            Assert.Equal(15m, shortages[0].ShortageQuantity);
            Assert.Equal(ShortageReason.InsufficientStock, shortages[0].Reason);

            // 5. Tenant isolation ensures Client B orders and allocations are invisible
            var visibleOrders = await context.OutboundOrders.ToListAsync();
            Assert.Single(visibleOrders);
            Assert.Equal("ORD-PERSIST-A", visibleOrders[0].OrderNumber);
        }
    }
}

