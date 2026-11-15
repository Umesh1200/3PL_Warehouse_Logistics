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

public class PickExecutionAndLedgerTests
{
    #region Scan Validation Tests

    [Fact]
    public async Task ConfirmPick_RejectsLocationMismatch_WithoutModifyingInventory()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, _, _, _, _) = await SeedPickEnvironmentAsync(dbName, clientA, facilityId);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);

            var ex = await Assert.ThrowsAsync<ValidationException>(() => service.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = taskId,
                ScannedLocationBarcode = "WRONG-BIN-BARCODE",
                ScannedItemBarcode = "SKU-PICK-01",
                ScannedToteLpn = "TOTE-01",
                Quantity = 10m,
                UserId = Guid.NewGuid()
            }));

            Assert.Contains("does not match expected location", ex.Message);

            // Verify zero transactions and unmutated task
            Assert.Empty(await context.InventoryTransactions.Where(t => t.TxType == InventoryTxType.Pick).ToListAsync());
            var task = await context.PickTasks.FirstAsync(t => t.Id == taskId);
            Assert.Equal(PickTaskStatus.Pending, task.Status);
            Assert.Equal(0m, task.QuantityPicked);
        }
    }

    [Fact]
    public async Task ConfirmPick_RejectsItemSkuMismatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, _, _, _, _) = await SeedPickEnvironmentAsync(dbName, clientA, facilityId);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);

            var ex = await Assert.ThrowsAsync<ValidationException>(() => service.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = taskId,
                ScannedLocationBarcode = "LOC-A-01",
                ScannedItemBarcode = "WRONG-SKU-XYZ",
                ScannedToteLpn = "TOTE-01",
                Quantity = 10m,
                UserId = Guid.NewGuid()
            }));

            Assert.Contains("does not match expected SKU", ex.Message);
        }
    }

    [Fact]
    public async Task ConfirmPick_RejectsLotMismatch_WhenLotIsTracked()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, _, _, _, _) = await SeedPickEnvironmentAsync(dbName, clientA, facilityId, lotTracked: true, lotNumber: "LOT-EXPECTED-99");

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);

            var ex = await Assert.ThrowsAsync<ValidationException>(() => service.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = taskId,
                ScannedLocationBarcode = "LOC-A-01",
                ScannedItemBarcode = "SKU-PICK-01",
                ScannedLotNumber = "LOT-WRONG-11",
                ScannedToteLpn = "TOTE-01",
                Quantity = 10m,
                UserId = Guid.NewGuid()
            }));

            Assert.Contains("does not match expected lot", ex.Message);
        }
    }

    [Fact]
    public async Task ConfirmPick_RejectsDamagedTote_OrCrossWaveAssignedTote()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, _, _, _, _) = await SeedPickEnvironmentAsync(dbName, clientA, facilityId);

        // Seed damaged tote and other wave tote
        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.HandlingUnits.Add(new HandlingUnit
            {
                ClientId = clientA,
                FacilityId = facilityId,
                LpnCode = "TOTE-DAMAGED",
                Status = HandlingUnitStatus.Damaged
            });

            seed.HandlingUnits.Add(new HandlingUnit
            {
                ClientId = clientA,
                FacilityId = facilityId,
                LpnCode = "TOTE-OTHER-WAVE",
                Status = HandlingUnitStatus.InUse,
                CurrentPickWaveId = Guid.NewGuid() // Different wave
            });

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);

            // Damaged tote rejected
            await Assert.ThrowsAsync<ValidationException>(() => service.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = taskId,
                ScannedLocationBarcode = "LOC-A-01",
                ScannedItemBarcode = "SKU-PICK-01",
                ScannedToteLpn = "TOTE-DAMAGED",
                Quantity = 10m,
                UserId = Guid.NewGuid()
            }));

            // Cross-wave tote rejected
            await Assert.ThrowsAsync<ValidationException>(() => service.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = taskId,
                ScannedLocationBarcode = "LOC-A-01",
                ScannedItemBarcode = "SKU-PICK-01",
                ScannedToteLpn = "TOTE-OTHER-WAVE",
                Quantity = 10m,
                UserId = Guid.NewGuid()
            }));
        }
    }

    [Fact]
    public async Task ConfirmPick_RejectsPicksFromLockedOrDamagedLocation()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, locId, _, _, _) = await SeedPickEnvironmentAsync(dbName, clientA, facilityId);

        using (var lockContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var loc = await lockContext.Locations.FirstAsync(l => l.Id == locId);
            loc.IsLocked = true;
            await lockContext.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);

            var ex = await Assert.ThrowsAsync<ValidationException>(() => service.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = taskId,
                ScannedLocationBarcode = "LOC-A-01",
                ScannedItemBarcode = "SKU-PICK-01",
                ScannedToteLpn = "TOTE-01",
                Quantity = 10m,
                UserId = Guid.NewGuid()
            }));

            Assert.Contains("locked, damaged, or inactive", ex.Message);
        }
    }

    [Fact]
    public async Task ConfirmPick_RejectsExcessQuantity()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, _, _, _, _) = await SeedPickEnvironmentAsync(dbName, clientA, facilityId, taskQty: 10m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);

            // Attempting to pick 15 when task requested 10
            var ex = await Assert.ThrowsAsync<ValidationException>(() => service.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = taskId,
                ScannedLocationBarcode = "LOC-A-01",
                ScannedItemBarcode = "SKU-PICK-01",
                ScannedToteLpn = "TOTE-01",
                Quantity = 15m,
                UserId = Guid.NewGuid()
            }));

            Assert.Contains("exceeds remaining task demand", ex.Message);
        }
    }

    #endregion

    #region Physical Inventory Movement & Ledger Tests

    [Fact]
    public async Task ConfirmPick_ExecutesAtomicInventoryMovement_AndPreservesConservation()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, _, stockId, allocId, toteId) = await SeedPickEnvironmentAsync(
            dbName, clientA, facilityId, sourceQtyAllocated: 20m, sourceQtyAvailable: 80m, taskQty: 20m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);
            var userId = Guid.NewGuid();

            var result = await service.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = taskId,
                ScannedLocationBarcode = "LOC-A-01",
                ScannedItemBarcode = "SKU-PICK-01",
                ScannedToteLpn = "TOTE-01",
                Quantity = 20m,
                UserId = userId
            });

            Assert.True(result.IsSuccess);
            Assert.Equal(PickTaskStatus.Completed, result.TaskStatus);
            Assert.Equal(20m, result.QuantityPicked);
            Assert.Equal(0m, result.ShortPickQuantity);
            Assert.True(result.WaveCompleted);

            // 1. Source Storage Bin Verification
            var sourceStock = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(80m, sourceStock.QtyAvailable); // QtyAvailable unmutated
            Assert.Equal(0m, sourceStock.QtyAllocated);   // QtyAllocated reduced by 20m

            // 2. Destination Tote Stock Verification
            var destStock = await context.StockOnHand.FirstAsync(s => s.Id == result.DestinationStockId);
            Assert.Equal(toteId, destStock.HandlingUnitId);
            Assert.Equal("TOTE-01", destStock.LpnCode);
            Assert.Equal(0m, destStock.QtyAvailable);     // Crucial: not freely available stock
            Assert.Equal(20m, destStock.QtyAllocated);   // Held in allocated state for packing
            Assert.Equal(sourceStock.ItemId, destStock.ItemId);

            // 3. Physical Inventory Conservation Law: Delta Source + Delta Destination == 0
            var deltaSource = sourceStock.QtyAllocated - 20m; // was 20, now 0 -> -20
            var deltaDest = destStock.QtyAllocated - 0m;      // was 0, now 20 -> +20
            Assert.Equal(0m, deltaSource + deltaDest);

            // 4. Traceability on InventoryAllocation
            var allocation = await context.InventoryAllocations.FirstAsync(a => a.Id == allocId);
            Assert.Equal(20m, allocation.QuantityPicked);
            Assert.Equal(0m, allocation.RemainingToPickQuantity);
            Assert.Equal(AllocationStatus.Picked, allocation.Status);
            Assert.Equal(destStock.Id, allocation.PickedStockOnHandId);
            Assert.Equal(toteId, allocation.TargetHandlingUnitId);
            Assert.Equal("TOTE-01", allocation.TargetLpnCode);

            // 5. Immutable Inventory Transaction Ledger
            var tx = await context.InventoryTransactions.FirstAsync(t => t.Id == result.InventoryTransactionId);
            Assert.Equal(InventoryTxType.Pick, tx.TxType);
            Assert.Equal(20m, tx.Quantity);
            Assert.Equal(sourceStock.LocationId, tx.FromLocationId);
            Assert.Equal(destStock.LocationId, tx.ToLocationId);
            Assert.Equal("PickTask", tx.ReferenceType);
            Assert.Equal(taskId, tx.ReferenceId);
            Assert.Equal(userId, tx.UserId);
        }
    }

    [Fact]
    public async Task ConfirmPick_ExistingToteStock_IncrementsBalanceWithoutCreatingDuplicates()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, _, stockId, allocId, toteId) = await SeedPickEnvironmentAsync(
            dbName, clientA, facilityId, sourceQtyAllocated: 50m, sourceQtyAvailable: 50m, taskQty: 20m);

        // Pre-seed an existing StockOnHand record inside the destination tote with 10 units already picked
        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var task = await seed.PickTasks.FirstAsync(t => t.Id == taskId);
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientA,
                FacilityId = facilityId,
                ItemId = task.ItemId,
                LocationId = task.SourceLocationId,
                HandlingUnitId = toteId,
                LpnCode = "TOTE-01",
                QtyAvailable = 0m,
                QtyAllocated = 10m,
                Version = 1
            });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);

            var result = await service.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = taskId,
                ScannedLocationBarcode = "LOC-A-01",
                ScannedItemBarcode = "SKU-PICK-01",
                ScannedToteLpn = "TOTE-01",
                Quantity = 20m,
                UserId = Guid.NewGuid()
            });

            Assert.True(result.IsSuccess);

            // Verify only ONE StockOnHand record exists in the destination tote, incremented from 10 to 30
            var toteStockList = await context.StockOnHand.Where(s => s.HandlingUnitId == toteId).ToListAsync();
            Assert.Single(toteStockList);
            Assert.Equal(30m, toteStockList[0].QtyAllocated);
            Assert.Equal(0m, toteStockList[0].QtyAvailable);
        }
    }

    [Fact]
    public async Task ConfirmPick_IdempotentRetry_DoesNotDuplicateStockMovement()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, _, stockId, _, _) = await SeedPickEnvironmentAsync(
            dbName, clientA, facilityId, sourceQtyAllocated: 20m, sourceQtyAvailable: 80m, taskQty: 20m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);
            var userId = Guid.NewGuid();

            var request = new ConfirmPickRequest
            {
                PickTaskId = taskId,
                ScannedLocationBarcode = "LOC-A-01",
                ScannedItemBarcode = "SKU-PICK-01",
                ScannedToteLpn = "TOTE-01",
                Quantity = 20m,
                UserId = userId,
                IdempotencyKey = "IDEMP-PICK-001"
            };

            // First confirmation
            var result1 = await service.ConfirmPickAsync(request);
            Assert.True(result1.IsSuccess);

            var ledgerCountAfterFirst = await context.InventoryTransactions.CountAsync(t => t.TxType == InventoryTxType.Pick);
            var sourceStockAfterFirst = await context.StockOnHand.AsNoTracking().FirstAsync(s => s.Id == stockId);

            // Second identical confirmation (retried network request)
            var result2 = await service.ConfirmPickAsync(request);
            Assert.True(result2.IsSuccess);
            Assert.Contains("idempotent", result2.Message);

            // Invariant: Zero duplicate ledger rows, zero additional stock deduction
            var ledgerCountAfterSecond = await context.InventoryTransactions.CountAsync(t => t.TxType == InventoryTxType.Pick);
            var sourceStockAfterSecond = await context.StockOnHand.AsNoTracking().FirstAsync(s => s.Id == stockId);

            Assert.Equal(ledgerCountAfterFirst, ledgerCountAfterSecond);
            Assert.Equal(sourceStockAfterFirst.QtyAllocated, sourceStockAfterSecond.QtyAllocated);
        }
    }

    #endregion

    #region Short-Pick & Cycle Count Integration Tests

    [Fact]
    public async Task ReportShortPick_BinEmpty_ReleasesAllocation_TriggersCycleCount_AndLocksLocation()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, locId, stockId, allocId, _) = await SeedPickEnvironmentAsync(
            dbName, clientA, facilityId, sourceQtyAllocated: 15m, sourceQtyAvailable: 0m, taskQty: 15m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);
            var userId = Guid.NewGuid();

            var result = await service.ReportShortPickAsync(new ReportShortPickRequest
            {
                PickTaskId = taskId,
                QuantityPicked = 0m, // Entire bin empty
                Reason = ShortPickReason.BinEmpty,
                Notes = "Physical bin completely empty",
                UserId = userId
            });

            Assert.True(result.IsSuccess);
            Assert.Equal(PickTaskStatus.ShortPicked, result.TaskStatus);
            Assert.Equal(0m, result.QuantityPicked);
            Assert.Equal(15m, result.DiscrepancyQuantity);
            Assert.True(result.LocationLockApplied); // High severity BinEmpty locks location
            Assert.NotNull(result.GeneratedCycleCountId);

            // 1. Verify Allocation Released for shortfall
            var allocation = await context.InventoryAllocations.FirstAsync(a => a.Id == allocId);
            Assert.Equal(15m, allocation.QuantityReleased);
            Assert.Equal(0m, allocation.RemainingToPickQuantity);
            Assert.Equal(AllocationStatus.Released, allocation.Status);

            // 2. Verify Source Stock Allocated Released
            var sourceStock = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(0m, sourceStock.QtyAllocated);

            // 3. Verify Cycle Count Scheduled
            var cycleCount = await context.CycleCounts
                .Include(c => c.Items)
                .FirstAsync(c => c.Id == result.GeneratedCycleCountId);

            Assert.Equal(CycleCountType.AdHoc, cycleCount.CountType);
            Assert.Equal(CycleCountStatus.Scheduled, cycleCount.Status);
            Assert.Single(cycleCount.Items);
            Assert.Equal(locId, cycleCount.Items.First().LocationId);

            // 4. Verify Location Lock Applied
            var location = await context.Locations.FirstAsync(l => l.Id == locId);
            Assert.True(location.IsLocked);

            var locLock = await context.LocationLocks.FirstAsync(l => l.LocationId == locId);
            Assert.True(locLock.IsActive);
            Assert.Equal(LocationLockType.CycleCount, locLock.LockType);

            // 5. Verify PickException Recorded
            var exception = await context.PickExceptions.FirstAsync(e => e.Id == result.PickExceptionId);
            Assert.Equal(15m, exception.DiscrepancyQuantity);
            Assert.Equal(ShortPickReason.BinEmpty, exception.Reason);
            Assert.True(exception.LocationLockApplied);
            Assert.Equal(cycleCount.Id, exception.GeneratedCycleCountId);
        }
    }

    [Fact]
    public async Task ReportShortPick_PartialPick_MovesFoundQuantity_AndReleasesShortfall()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var (taskId, locId, stockId, allocId, toteId) = await SeedPickEnvironmentAsync(
            dbName, clientA, facilityId, sourceQtyAllocated: 20m, sourceQtyAvailable: 80m, taskQty: 20m);

        using (var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var service = new PickExecutionService(context, NullLogger<PickExecutionService>.Instance);
            var userId = Guid.NewGuid();

            // Found 16 units, 4 units damaged/unusable in carton
            var result = await service.ReportShortPickAsync(new ReportShortPickRequest
            {
                PickTaskId = taskId,
                QuantityPicked = 16m,
                Reason = ShortPickReason.DamagedStock,
                Notes = "4 units crushed; 16 units intact",
                ScannedLocationBarcode = "LOC-A-01",
                ScannedItemBarcode = "SKU-PICK-01",
                ScannedToteLpn = "TOTE-01",
                UserId = userId
            });

            Assert.True(result.IsSuccess);
            Assert.Equal(16m, result.QuantityPicked);
            Assert.Equal(4m, result.DiscrepancyQuantity);
            Assert.False(result.LocationLockApplied); // Minor 4 units damage does not lock entire location

            // 1. Destination Tote received exactly the 16 confirmed units
            var destStock = await context.StockOnHand.FirstAsync(s => s.HandlingUnitId == toteId);
            Assert.Equal(16m, destStock.QtyAllocated);

            // 2. Source Bin deducted 16 picked units and released 4 shortfall units (total reduction 20m)
            var sourceStock = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal(0m, sourceStock.QtyAllocated);

            // 3. Allocation tracks 16 picked, 4 released
            var allocation = await context.InventoryAllocations.FirstAsync(a => a.Id == allocId);
            Assert.Equal(16m, allocation.QuantityPicked);
            Assert.Equal(4m, allocation.QuantityReleased);
            Assert.Equal(0m, allocation.RemainingToPickQuantity);
            Assert.Equal(AllocationStatus.Picked, allocation.Status);

            // 4. Ledger records Pick for 16 units
            var tx = await context.InventoryTransactions.FirstAsync(t => t.TxType == InventoryTxType.Pick);
            Assert.Equal(16m, tx.Quantity);
        }
    }

    #endregion

    #region Helper Setup Methods

    private static async Task<(Guid taskId, Guid locId, Guid stockId, Guid allocId, Guid toteId)> SeedPickEnvironmentAsync(
        string dbName,
        Guid clientId,
        Guid facilityId,
        decimal sourceQtyAllocated = 20m,
        decimal sourceQtyAvailable = 80m,
        decimal taskQty = 20m,
        bool lotTracked = false,
        string lotNumber = "LOT-01")
    {
        var binId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var lotId = lotTracked ? Guid.NewGuid() : (Guid?)null;
        var stockId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var allocId = Guid.NewGuid();
        var waveId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var toteId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Pick Client", AccountCode = "PC-01" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Pick Facility", FacilityCode = "PF-01" });

            seed.Locations.Add(new Location
            {
                Id = binId,
                FacilityId = facilityId,
                LocationCode = "LOC-A-01",
                Barcode = "BAR-LOC-A-01",
                AisleCode = "01",
                RackCode = "01",
                ShelfLevel = 1,
                IsActive = true,
                IsPickable = true
            });

            var item = new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "SKU-PICK-01",
                Description = "Fast Mover Widget",
                IsLotTracked = lotTracked
            };
            item.Barcodes.Add(new ItemBarcode
            {
                ItemId = itemId,
                BarcodeValue = "UPC-123456789012",
                IsPrimary = true
            });
            seed.Items.Add(item);

            if (lotTracked && lotId.HasValue)
            {
                seed.InventoryLots.Add(new InventoryLot
                {
                    Id = lotId.Value,
                    ClientId = clientId,
                    ItemId = itemId,
                    LotNumber = lotNumber,
                    ExpirationDate = DateTime.UtcNow.AddYears(1)
                });
            }

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stockId,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = binId,
                LotId = lotId,
                QtyAvailable = sourceQtyAvailable,
                QtyAllocated = sourceQtyAllocated,
                ReceivedAtUtc = DateTime.UtcNow.AddDays(-2),
                Version = 1
            });

            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = clientId,
                FacilityId = facilityId,
                OrderNumber = "ORD-PICK-01",
                CustomerName = "Target Store",
                Status = OutboundOrderStatus.Allocated
            };
            var line = new OutboundOrderLine
            {
                Id = lineId,
                OutboundOrderId = orderId,
                ItemId = itemId,
                LineNumber = 1,
                QuantityOrdered = taskQty,
                QuantityAllocated = taskQty,
                Status = OutboundOrderLineStatus.Allocated
            };
            order.Lines.Add(line);
            seed.OutboundOrders.Add(order);

            var allocation = new InventoryAllocation
            {
                Id = allocId,
                ClientId = clientId,
                FacilityId = facilityId,
                OutboundOrderId = orderId,
                OutboundOrderLineId = lineId,
                StockOnHandId = stockId,
                ItemId = itemId,
                LocationId = binId,
                LotId = lotId,
                QuantityRequested = taskQty,
                QuantityAllocated = taskQty,
                QuantityReleased = 0m,
                QuantityPicked = 0m,
                Status = AllocationStatus.Allocated
            };
            seed.InventoryAllocations.Add(allocation);

            seed.HandlingUnits.Add(new HandlingUnit
            {
                Id = toteId,
                ClientId = clientId,
                FacilityId = facilityId,
                LpnCode = "TOTE-01",
                UnitType = HandlingUnitType.Tote,
                Status = HandlingUnitStatus.Available
            });

            var wave = new PickWave
            {
                Id = waveId,
                ClientId = clientId,
                FacilityId = facilityId,
                WaveNumber = "WAVE-PICK-01",
                Status = PickWaveStatus.Released
            };
            wave.WaveOrders.Add(new PickWaveOrder { PickWaveId = waveId, OutboundOrderId = orderId, SequenceNumber = 1 });

            var task = new PickTask
            {
                Id = taskId,
                ClientId = clientId,
                FacilityId = facilityId,
                PickWaveId = waveId,
                OutboundOrderId = orderId,
                OutboundOrderLineId = lineId,
                InventoryAllocationId = allocId,
                StockOnHandId = stockId,
                ItemId = itemId,
                SourceLocationId = binId,
                LotId = lotId,
                QuantityToPick = taskQty,
                QuantityPicked = 0m,
                PickSequence = 1,
                Status = PickTaskStatus.Pending
            };
            wave.Tasks.Add(task);
            seed.PickWaves.Add(wave);

            await seed.SaveChangesAsync();
        }

        return (taskId, binId, stockId, allocId, toteId);
    }

    #endregion
}

