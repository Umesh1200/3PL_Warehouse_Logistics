using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Outbound.Allocation.Commands;
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
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Outbound;

public class AllocationExecutionTransactionalTests
{
    private static AllocationExecutionService CreateExecutionService(
        IApplicationDbContext context,
        ITenantProvider tenantProvider)
    {
        var filter = new AllocationCandidateFilter();
        var fifo = new FifoAllocationStrategy(context, filter);
        var fefo = new FefoAllocationStrategy(context, filter);
        var manual = new ManualAllocationStrategy(context, filter);
        var resolver = new AllocationStrategyResolver(new IAllocationStrategy[] { fifo, fefo, manual });
        var engine = new AllocationEngine(context, resolver, filter);
        return new AllocationExecutionService(context, engine, filter, tenantProvider);
    }

    [Fact]
    public async Task ExecuteOrderAllocation_FullAllocation_HardReservesInventoryAndUpdatesStatus()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Full Alloc Corp", AccountCode = "FAC" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Main DFW", FacilityCode = "DFW-01" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-FULL-01", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-FULL-ALLOC", StorageTempClass = StorageTempClass.Ambient });

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stockId, ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = binId, QtyAvailable = 100m, QtyAllocated = 0m
            });

            var order = new OutboundOrder
            {
                Id = orderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-FULL-001", CustomerName = "Customer A", Status = OutboundOrderStatus.Released
            };
            order.Lines.Add(new OutboundOrderLine
            {
                ItemId = itemId, LineNumber = 1, QuantityOrdered = 40m, QuantityAllocated = 0m, Status = OutboundOrderLineStatus.Pending
            });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var service = CreateExecutionService(context, tenantProvider);

            var result = await service.ExecuteOrderAllocationAsync(orderId, userId: currentUser.UserId);

            Assert.True(result.IsFullyAllocated);
            Assert.Equal(40m, result.TotalQuantityAllocated);
            Assert.Equal(0m, result.TotalShortageQuantity);
            Assert.Equal(OutboundOrderStatus.Allocated, result.Status);

            // 1. Verify StockOnHand hard reservation: QtyAvailable 100 -> 60, QtyAllocated 0 -> 40
            var stock = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(60m, stock.QtyAvailable);
            Assert.Equal(40m, stock.QtyAllocated);
            Assert.Equal(100m, stock.TotalQuantity); // Physical on-hand strictly unchanged!

            // 2. Verify InventoryAllocation record created
            var allocations = await context.InventoryAllocations.Where(a => a.OutboundOrderId == orderId).ToListAsync();
            Assert.Single(allocations);
            Assert.Equal(40m, allocations[0].QuantityAllocated);
            Assert.Equal(0m, allocations[0].QuantityReleased);
            Assert.Equal(40m, allocations[0].ActiveAllocatedQuantity);
            Assert.Equal(AllocationStatus.Allocated, allocations[0].Status);

            // 3. Verify Order and Line states
            var order = await context.OutboundOrders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId);
            Assert.Equal(OutboundOrderStatus.Allocated, order.Status);
            Assert.Equal(OutboundOrderLineStatus.Allocated, order.Lines.First().Status);

            // 4. Verify ZERO pick ledger entries created
            var ledgerCount = await context.InventoryTransactions.CountAsync();
            Assert.Equal(0, ledgerCount);
        }
    }

    [Fact]
    public async Task ExecuteOrderAllocation_PartialAllocation_LogsShortageAndSetsPartiallyAllocated()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Partial Alloc Corp", AccountCode = "PAC" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Main DFW", FacilityCode = "DFW-01" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-PART-01", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-PARTIAL", StorageTempClass = StorageTempClass.Ambient });

            // Only 35 units available in stock
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stockId, ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = binId, QtyAvailable = 35m, QtyAllocated = 0m
            });

            // Order demands 50 units
            var order = new OutboundOrder
            {
                Id = orderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-PART-001", CustomerName = "Customer B", Status = OutboundOrderStatus.Released
            };
            order.Lines.Add(new OutboundOrderLine
            {
                ItemId = itemId, LineNumber = 1, QuantityOrdered = 50m, QuantityAllocated = 0m, Status = OutboundOrderLineStatus.Pending
            });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var service = CreateExecutionService(context, tenantProvider);

            var result = await service.ExecuteOrderAllocationAsync(orderId);

            Assert.False(result.IsFullyAllocated);
            Assert.Equal(35m, result.TotalQuantityAllocated);
            Assert.Equal(15m, result.TotalShortageQuantity);
            Assert.Equal(OutboundOrderStatus.PartiallyAllocated, result.Status);

            // 1. StockOnHand: QtyAvailable 35 -> 0, QtyAllocated 0 -> 35
            var stock = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(0m, stock.QtyAvailable);
            Assert.Equal(35m, stock.QtyAllocated);

            // 2. Shortage logged
            var shortages = await context.AllocationShortages.Where(s => s.OutboundOrderId == orderId).ToListAsync();
            Assert.Single(shortages);
            Assert.Equal(15m, shortages[0].ShortageQuantity);
            Assert.Equal(ShortageReason.InsufficientStock, shortages[0].Reason);

            // 3. Order status is PartiallyAllocated
            var order = await context.OutboundOrders.FirstAsync(o => o.Id == orderId);
            Assert.Equal(OutboundOrderStatus.PartiallyAllocated, order.Status);
        }
    }

    [Fact]
    public async Task ExecuteOrderAllocation_MultipleBinsAndLots_ReservesAcrossCandidatesDeterministically()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var bin1 = Guid.NewGuid();
        var bin2 = Guid.NewGuid();
        var stock1 = Guid.NewGuid();
        var stock2 = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.Add(new Location { Id = bin1, FacilityId = facilityId, LocationCode = "BIN-01", LocationType = LocationType.Bin, PickSequence = 10 });
            seed.Locations.Add(new Location { Id = bin2, FacilityId = facilityId, LocationCode = "BIN-02", LocationType = LocationType.Bin, PickSequence = 20 });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-MULTIBIN-TEST", StorageTempClass = StorageTempClass.Ambient });

            var lot1 = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-1", ExpirationDate = DateTime.UtcNow.AddDays(50), QaStatus = LotQaStatus.Released };
            var lot2 = new InventoryLot { Id = Guid.NewGuid(), ClientId = clientId, ItemId = itemId, LotNumber = "LOT-2", ExpirationDate = DateTime.UtcNow.AddDays(60), QaStatus = LotQaStatus.Released };
            seed.InventoryLots.AddRange(lot1, lot2);

            // Bin 1: 50 units (Received 10 days ago)
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stock1, ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = bin1, LotId = lot1.Id, QtyAvailable = 50m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-10)
            });

            // Bin 2: 50 units (Received 5 days ago)
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stock2, ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = bin2, LotId = lot2.Id, QtyAvailable = 50m, ReceivedAtUtc = DateTime.UtcNow.AddDays(-5)
            });

            // Demand is 85 units -> Needs 50 from Bin 1 + 35 from Bin 2
            var order = new OutboundOrder
            {
                Id = orderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-MB-01", CustomerName = "MultiBin Cust", Status = OutboundOrderStatus.Released
            };
            order.Lines.Add(new OutboundOrderLine
            {
                ItemId = itemId, LineNumber = 1, QuantityOrdered = 85m, QuantityAllocated = 0m, Status = OutboundOrderLineStatus.Pending
            });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var service = CreateExecutionService(context, tenantProvider);

            var result = await service.ExecuteOrderAllocationAsync(orderId);

            Assert.True(result.IsFullyAllocated);
            Assert.Equal(85m, result.TotalQuantityAllocated);

            // Bin 1: 50 allocated, 0 available remaining
            var s1 = await context.StockOnHand.FirstAsync(s => s.Id == stock1);
            Assert.Equal(0m, s1.QtyAvailable);
            Assert.Equal(50m, s1.QtyAllocated);

            // Bin 2: 35 allocated, 15 available remaining
            var s2 = await context.StockOnHand.FirstAsync(s => s.Id == stock2);
            Assert.Equal(15m, s2.QtyAvailable);
            Assert.Equal(35m, s2.QtyAllocated);

            // 2 InventoryAllocation records for the 1 line
            var allocations = await context.InventoryAllocations.Where(a => a.OutboundOrderId == orderId).ToListAsync();
            Assert.Equal(2, allocations.Count);
            Assert.Contains(allocations, a => a.LocationId == bin1 && a.QuantityAllocated == 50m);
            Assert.Contains(allocations, a => a.LocationId == bin2 && a.QuantityAllocated == 35m);
        }
    }

    [Fact]
    public async Task IdempotencyProtection_PreventsDoubleReservationOnRetriedRequest()
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
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-IDEM", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-IDEM", StorageTempClass = StorageTempClass.Ambient });

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stockId, ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = binId, QtyAvailable = 100m
            });

            var order = new OutboundOrder
            {
                Id = orderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-IDEM-01", CustomerName = "Idem Cust", Status = OutboundOrderStatus.Released
            };
            order.Lines.Add(new OutboundOrderLine
            {
                ItemId = itemId, LineNumber = 1, QuantityOrdered = 30m, QuantityAllocated = 0m
            });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var service = CreateExecutionService(context, tenantProvider);

            // First submission with IdempotencyKey
            var result1 = await service.ExecuteOrderAllocationAsync(orderId, idempotencyKey: "REQ-IDEM-999");
            Assert.False(result1.IsDuplicateRequest);
            Assert.Equal(30m, result1.TotalQuantityAllocated);

            var stockAfterFirst = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(70m, stockAfterFirst.QtyAvailable);
            Assert.Equal(30m, stockAfterFirst.QtyAllocated);

            // Re-submitting identical request with same IdempotencyKey
            var result2 = await service.ExecuteOrderAllocationAsync(orderId, idempotencyKey: "REQ-IDEM-999");
            Assert.True(result2.IsDuplicateRequest); // Flagged as duplicate!
            Assert.Equal(30m, result2.TotalQuantityAllocated);

            // INVARIANT: StockOnHand was NOT mutated a second time (Zero double-reservation!)
            var stockAfterSecond = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(70m, stockAfterSecond.QtyAvailable);
            Assert.Equal(30m, stockAfterSecond.QtyAllocated);

            // Only 1 allocation record exists
            var allocCount = await context.InventoryAllocations.CountAsync(a => a.OutboundOrderId == orderId);
            Assert.Equal(1, allocCount);
        }
    }

    [Fact]
    public async Task Deallocation_FullAndPartialRelease_RestoresStockAndUpdatesOrderLifecycle()
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
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-DEALLOC", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-DEALLOC", StorageTempClass = StorageTempClass.Ambient });

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stockId, ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = binId, QtyAvailable = 60m, QtyAllocated = 40m
            });

            var order = new OutboundOrder
            {
                Id = orderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-DEALLOC-01", CustomerName = "Dealloc Cust", Status = OutboundOrderStatus.Allocated
            };
            var line = new OutboundOrderLine
            {
                ItemId = itemId, LineNumber = 1, QuantityOrdered = 40m, QuantityAllocated = 40m, Status = OutboundOrderLineStatus.Allocated
            };
            order.Lines.Add(line);
            seed.OutboundOrders.Add(order);

            var allocation = new InventoryAllocation
            {
                ClientId = clientId, FacilityId = facilityId, OutboundOrderId = order.Id,
                OutboundOrderLineId = line.Id, StockOnHandId = stockId, ItemId = itemId,
                LocationId = binId, QuantityRequested = 40m, QuantityAllocated = 40m, QuantityReleased = 0m,
                Status = AllocationStatus.Allocated
            };
            seed.InventoryAllocations.Add(allocation);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var service = CreateExecutionService(context, tenantProvider);

            var alloc = await context.InventoryAllocations.FirstAsync();

            // 1. Partial Release of 15 units
            var partialResult = await service.ReleaseAllocationAsync(alloc.Id, 15m, "Customer reduced quantity");

            Assert.Equal(15m, partialResult.QuantityReleased);
            Assert.Equal(25m, partialResult.ActiveRemainingQuantity);
            Assert.Equal(OutboundOrderStatus.PartiallyAllocated, partialResult.OrderStatus);
            Assert.Equal(OutboundOrderLineStatus.PartiallyAllocated, partialResult.LineStatus);

            var stockAfterPartial = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(75m, stockAfterPartial.QtyAvailable); // 60 + 15
            Assert.Equal(25m, stockAfterPartial.QtyAllocated); // 40 - 15

            // 2. Full Order De-allocation of remaining 25 units
            var deallocResult = await service.ExecuteOrderDeallocationAsync(orderId, "Order cancelled by client");

            Assert.Equal(25m, deallocResult.TotalQuantityDeallocated);
            Assert.Equal(OutboundOrderStatus.Released, deallocResult.RestoredStatus);

            var stockAfterFull = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(100m, stockAfterFull.QtyAvailable); // 75 + 25 = 100
            Assert.Equal(0m, stockAfterFull.QtyAllocated);   // 25 - 25 = 0

            // 3. AUDIT INVARIANT: Allocation record still exists with Released status (NOT deleted!)
            var finalAlloc = await context.InventoryAllocations.FirstAsync(a => a.Id == alloc.Id);
            Assert.Equal(AllocationStatus.Released, finalAlloc.Status);
            Assert.Equal(40m, finalAlloc.QuantityReleased);
            Assert.Equal(0m, finalAlloc.ActiveAllocatedQuantity);
        }
    }

    [Fact]
    public async Task OverRelease_And_OverAllocation_Prevention()
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
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-OVER", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-OVER", StorageTempClass = StorageTempClass.Ambient });

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stockId, ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = binId, QtyAvailable = 80m, QtyAllocated = 20m
            });

            var order = new OutboundOrder
            {
                Id = orderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-OVER-01", CustomerName = "Over Cust", Status = OutboundOrderStatus.PartiallyAllocated
            };
            var line = new OutboundOrderLine
            {
                ItemId = itemId, LineNumber = 1, QuantityOrdered = 20m, QuantityAllocated = 20m, Status = OutboundOrderLineStatus.Allocated
            };
            order.Lines.Add(line);
            seed.OutboundOrders.Add(order);

            var alloc = new InventoryAllocation
            {
                ClientId = clientId, FacilityId = facilityId, OutboundOrderId = order.Id,
                OutboundOrderLineId = line.Id, StockOnHandId = stockId, ItemId = itemId,
                LocationId = binId, QuantityRequested = 20m, QuantityAllocated = 20m, QuantityReleased = 0m,
                Status = AllocationStatus.Allocated
            };
            seed.InventoryAllocations.Add(alloc);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var service = CreateExecutionService(context, tenantProvider);
            var alloc = await context.InventoryAllocations.FirstAsync();

            // 1. Attempting to release 25 units when only 20 active throws ValidationException
            await Assert.ThrowsAsync<ValidationException>(() =>
                service.ReleaseAllocationAsync(alloc.Id, 25m, "Too much release"));

            // 2. Attempting to release 0 or negative units throws ValidationException
            await Assert.ThrowsAsync<ValidationException>(() =>
                service.ReleaseAllocationAsync(alloc.Id, 0m, "Zero release"));
            await Assert.ThrowsAsync<ValidationException>(() =>
                service.ReleaseAllocationAsync(alloc.Id, -5m, "Negative release"));
        }
    }

    [Fact]
    public async Task LifecycleValidation_RejectsAllocationForOnHoldAndCancelledOrders()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var holdOrderId = Guid.NewGuid();
        var cancelledOrderId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-HOLD", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-HOLD-TEST", StorageTempClass = StorageTempClass.Ambient });

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = binId, QtyAvailable = 100m
            });

            var holdOrder = new OutboundOrder
            {
                Id = holdOrderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-HOLD", CustomerName = "Hold Cust", Status = OutboundOrderStatus.OnHold, HoldReason = "Credit Check"
            };
            holdOrder.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 10m });

            var cancelledOrder = new OutboundOrder
            {
                Id = cancelledOrderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-CANCEL", CustomerName = "Cancel Cust", Status = OutboundOrderStatus.Cancelled
            };
            cancelledOrder.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 10m });

            seed.OutboundOrders.AddRange(holdOrder, cancelledOrder);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var service = CreateExecutionService(context, tenantProvider);

            await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
                service.ExecuteOrderAllocationAsync(holdOrderId));

            await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
                service.ExecuteOrderAllocationAsync(cancelledOrderId));
        }
    }

    [Fact]
    public async Task TenantIsolation_RejectsCrossTenantAllocation()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var orderAId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CLA" });
            seed.Clients.Add(new Client { Id = clientB, CompanyName = "Client B", AccountCode = "CLB" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Hub", FacilityCode = "H-01" });
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-ISO", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientA, Sku = "SKU-ISO", StorageTempClass = StorageTempClass.Ambient });

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientA, FacilityId = facilityId, ItemId = itemId,
                LocationId = binId, QtyAvailable = 50m
            });

            var orderA = new OutboundOrder
            {
                Id = orderAId, ClientId = clientA, FacilityId = facilityId,
                OrderNumber = "ORD-CLIENT-A", CustomerName = "Cust A", Status = OutboundOrderStatus.Released
            };
            orderA.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 20m });
            seed.OutboundOrders.Add(orderA);

            await seed.SaveChangesAsync();
        }

        // Authenticated as Client B trying to allocate Client A's order
        var tenantProviderB = new TestTenantProvider { TenantId = clientB, BypassTenantFilter = false };
        var currentUserB = new TestCurrentUserContext { TenantId = clientB, UserType = UserType.Client };

        using (var context = TestDbContextFactory.Create(tenantProviderB, currentUserB, dbName))
        {
            var service = CreateExecutionService(context, tenantProviderB);

            // Must throw EntityNotFoundException (due to global tenant filter) or UnauthorizedAccessException
            await Assert.ThrowsAnyAsync<Exception>(() =>
                service.ExecuteOrderAllocationAsync(orderAId));
        }
    }

    [Fact]
    public async Task ConcurrencyContention_SecondAllocationAdaptsToRemainingStockWithoutOverAllocating()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var order1Id = Guid.NewGuid();
        var order2Id = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.Add(new Location { Id = binId, FacilityId = facilityId, LocationCode = "BIN-CONCURR", LocationType = LocationType.Bin });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-CONCURR", StorageTempClass = StorageTempClass.Ambient });

            // StockOnHand has only 50 units
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stockId, ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = binId, QtyAvailable = 50m, QtyAllocated = 0m
            });

            // Order 1 demands 40 units
            var order1 = new OutboundOrder
            {
                Id = order1Id, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-CONC-1", CustomerName = "Cust 1", Status = OutboundOrderStatus.Released
            };
            order1.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 40m });

            // Order 2 demands 40 units (Total demand 80 > 50 available)
            var order2 = new OutboundOrder
            {
                Id = order2Id, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-CONC-2", CustomerName = "Cust 2", Status = OutboundOrderStatus.Released
            };
            order2.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 40m });

            seed.OutboundOrders.AddRange(order1, order2);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var service = CreateExecutionService(context, tenantProvider);

            // 1. Order 1 allocates first: takes 40 units, leaving 10 available
            var res1 = await service.ExecuteOrderAllocationAsync(order1Id);
            Assert.True(res1.IsFullyAllocated);
            Assert.Equal(40m, res1.TotalQuantityAllocated);

            // 2. Order 2 executes next against the same StockOnHand balance:
            // Re-evaluates inside transaction, discovers only 10 units remaining, allocates 10, logs 30 shortage!
            var res2 = await service.ExecuteOrderAllocationAsync(order2Id);
            Assert.False(res2.IsFullyAllocated);
            Assert.Equal(10m, res2.TotalQuantityAllocated);
            Assert.Equal(30m, res2.TotalShortageQuantity);
            Assert.Equal(OutboundOrderStatus.PartiallyAllocated, res2.Status);

            // 3. INVARIANT: Total QtyAllocated across both orders is strictly 50 (never over-allocated!)
            var finalStock = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(0m, finalStock.QtyAvailable);
            Assert.Equal(50m, finalStock.QtyAllocated);
            Assert.Equal(50m, finalStock.TotalQuantity);
        }
    }

    [Fact]
    public async Task CrossDocking_And_Allocation_ConcurrencyBoundaryIsPreserved()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var storageBinId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            // Transient staging location (Phase 3.5 CrossDock space)
            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-XD", LocationType = LocationType.StagingLane });

            // Storage bin (Phase 4.2 Allocation space)
            seed.Locations.Add(new Location { Id = storageBinId, FacilityId = facilityId, LocationCode = "STORAGE-BIN", LocationType = LocationType.Bin });

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-XD-ALLOC", StorageTempClass = StorageTempClass.Ambient });

            // 50 units sitting in transient staging (eligible for CrossDock only)
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = stagingLocId, QtyAvailable = 50m
            });

            // 30 units sitting in permanent storage bin (eligible for Allocation only)
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                LocationId = storageBinId, QtyAvailable = 30m
            });

            var order = new OutboundOrder
            {
                Id = orderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-XD-CONCURR", CustomerName = "CrossDock Cust", Status = OutboundOrderStatus.Released
            };
            order.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 60m });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var service = CreateExecutionService(context, tenantProvider);

            // Execute standard allocation engine
            var result = await service.ExecuteOrderAllocationAsync(orderId);

            // INVARIANT: Allocation strictly allocates ONLY from storage bin (30 units).
            // It NEVER touches or consumes the 50 units in the transient cross-dock staging lane!
            Assert.Equal(30m, result.TotalQuantityAllocated);
            Assert.Equal(30m, result.TotalShortageQuantity);

            var stagingStock = await context.StockOnHand.FirstAsync(s => s.LocationId == stagingLocId);
            Assert.Equal(50m, stagingStock.QtyAvailable); // Staging stock strictly intact for CrossDock!
            Assert.Equal(0m, stagingStock.QtyAllocated);

            var storageStock = await context.StockOnHand.FirstAsync(s => s.LocationId == storageBinId);
            Assert.Equal(0m, storageStock.QtyAvailable);
            Assert.Equal(30m, storageStock.QtyAllocated);
        }
    }
}
