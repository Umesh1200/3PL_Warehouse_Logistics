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

public class PickDomainAndPersistenceTests
{
    #region HandlingUnit Domain Tests

    [Fact]
    public void HandlingUnit_Initializes_WithDefaults()
    {
        var unit = new HandlingUnit
        {
            LpnCode = "TOTE-001"
        };

        Assert.NotEqual(Guid.Empty, unit.Id);
        Assert.Equal("TOTE-001", unit.LpnCode);
        Assert.Equal(HandlingUnitType.Tote, unit.UnitType);
        Assert.Equal(HandlingUnitStatus.Available, unit.Status);
        Assert.Null(unit.CurrentLocationId);
        Assert.Null(unit.CurrentPickWaveId);
        Assert.Null(unit.CurrentOutboundOrderId);
        Assert.Equal(1u, unit.Version);
        Assert.False(unit.IsDeleted);
    }

    [Fact]
    public void HandlingUnit_AssignToWave_TransitionsStatusToInUse()
    {
        var unit = new HandlingUnit
        {
            LpnCode = "TOTE-101",
            Status = HandlingUnitStatus.Available
        };

        var waveId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        unit.AssignToWave(waveId, orderId);

        Assert.Equal(HandlingUnitStatus.InUse, unit.Status);
        Assert.Equal(waveId, unit.CurrentPickWaveId);
        Assert.Equal(orderId, unit.CurrentOutboundOrderId);
    }

    [Fact]
    public void HandlingUnit_AssignToWave_ThrowsWhenStatusNotAvailableOrInUse()
    {
        var unit = new HandlingUnit
        {
            LpnCode = "TOTE-DAMAGED",
            Status = HandlingUnitStatus.Damaged
        };

        var waveId = Guid.NewGuid();

        Assert.Throws<InvalidDomainStateException>(() => unit.AssignToWave(waveId));
    }

    [Fact]
    public void HandlingUnit_MoveToLocation_UpdatesLocationAndStatus()
    {
        var unit = new HandlingUnit
        {
            LpnCode = "TOTE-102",
            Status = HandlingUnitStatus.InUse
        };

        var packingStationId = Guid.NewGuid();

        unit.MoveToLocation(packingStationId, HandlingUnitStatus.AtPackingStation);

        Assert.Equal(packingStationId, unit.CurrentLocationId);
        Assert.Equal(HandlingUnitStatus.AtPackingStation, unit.Status);
    }

    [Fact]
    public void HandlingUnit_Release_ClearsWaveAndOrderAndResetsToAvailable()
    {
        var unit = new HandlingUnit
        {
            LpnCode = "TOTE-103",
            Status = HandlingUnitStatus.AtPackingStation,
            CurrentPickWaveId = Guid.NewGuid(),
            CurrentOutboundOrderId = Guid.NewGuid()
        };

        unit.Release();

        Assert.Null(unit.CurrentPickWaveId);
        Assert.Null(unit.CurrentOutboundOrderId);
        Assert.Equal(HandlingUnitStatus.Available, unit.Status);
    }

    #endregion

    #region PickWave Domain Tests

    [Fact]
    public void PickWave_Initializes_WithDefaults()
    {
        var wave = new PickWave
        {
            WaveNumber = "WAVE-2026-001"
        };

        Assert.NotEqual(Guid.Empty, wave.Id);
        Assert.Equal("WAVE-2026-001", wave.WaveNumber);
        Assert.Equal(PickWaveType.BatchPick, wave.WaveType);
        Assert.Equal(PickWaveStatus.Planning, wave.Status);
        Assert.Equal(1, wave.Priority);
        Assert.Null(wave.ReleasedAtUtc);
        Assert.Null(wave.StartedAtUtc);
        Assert.Null(wave.CompletedAtUtc);
        Assert.Equal(1u, wave.Version);
    }

    [Fact]
    public void PickWave_Release_ValidatesRequirementsAndTransitionsStatus()
    {
        var userId = Guid.NewGuid();
        var wave = new PickWave
        {
            WaveNumber = "WAVE-001",
            Status = PickWaveStatus.Planning
        };

        // 1. Throws if no orders
        Assert.Throws<InvalidDomainStateException>(() => wave.Release(userId));

        wave.WaveOrders.Add(new PickWaveOrder { PickWaveId = wave.Id, OutboundOrderId = Guid.NewGuid(), SequenceNumber = 1 });

        // 2. Throws if no tasks
        Assert.Throws<InvalidDomainStateException>(() => wave.Release(userId));

        wave.Tasks.Add(new PickTask
        {
            PickWaveId = wave.Id,
            QuantityToPick = 10m,
            Status = PickTaskStatus.Pending
        });

        // 3. Successfully releases
        wave.Release(userId);

        Assert.Equal(PickWaveStatus.Released, wave.Status);
        Assert.NotNull(wave.ReleasedAtUtc);

        // 4. Cannot release again once released
        Assert.Throws<InvalidDomainStateException>(() => wave.Release(userId));
    }

    [Fact]
    public void PickWave_Start_TransitionsFromReleasedToInProgress()
    {
        var userId = Guid.NewGuid();
        var wave = new PickWave
        {
            WaveNumber = "WAVE-START-01",
            Status = PickWaveStatus.Released
        };

        wave.Start(userId);

        Assert.Equal(PickWaveStatus.InProgress, wave.Status);
        Assert.NotNull(wave.StartedAtUtc);

        // Calling Start again while InProgress is idempotent and does not overwrite StartedAtUtc
        var originalStartTime = wave.StartedAtUtc;
        wave.Start(userId);
        Assert.Equal(originalStartTime, wave.StartedAtUtc);

        // Cannot start a Planning wave directly
        var planningWave = new PickWave { WaveNumber = "WAVE-PLAN", Status = PickWaveStatus.Planning };
        Assert.Throws<InvalidDomainStateException>(() => planningWave.Start(userId));
    }

    [Fact]
    public void PickWave_Complete_ValidatesAllTasksTerminal()
    {
        var userId = Guid.NewGuid();
        var wave = new PickWave
        {
            WaveNumber = "WAVE-COMP-01",
            Status = PickWaveStatus.InProgress
        };

        var task1 = new PickTask { PickWaveId = wave.Id, Status = PickTaskStatus.Completed };
        var task2 = new PickTask { PickWaveId = wave.Id, Status = PickTaskStatus.Pending };
        wave.Tasks.Add(task1);
        wave.Tasks.Add(task2);

        // 1. Cannot complete while open tasks exist
        Assert.Throws<InvalidDomainStateException>(() => wave.Complete(userId));

        // 2. Make task2 ShortPicked (terminal)
        task2.Status = PickTaskStatus.ShortPicked;
        wave.Complete(userId);

        Assert.Equal(PickWaveStatus.Completed, wave.Status);
        Assert.NotNull(wave.CompletedAtUtc);

        // 3. Cannot complete already completed wave
        Assert.Throws<InvalidDomainStateException>(() => wave.Complete(userId));
    }

    [Fact]
    public void PickWave_Cancel_CancelsWaveAndNonTerminalTasks()
    {
        var userId = Guid.NewGuid();
        var wave = new PickWave
        {
            WaveNumber = "WAVE-CNCL-01",
            Status = PickWaveStatus.InProgress
        };

        var completedTask = new PickTask { PickWaveId = wave.Id, Status = PickTaskStatus.Completed, QuantityToPick = 5m, QuantityPicked = 5m };
        var pendingTask = new PickTask { PickWaveId = wave.Id, Status = PickTaskStatus.Pending, QuantityToPick = 10m };
        var assignedTask = new PickTask { PickWaveId = wave.Id, Status = PickTaskStatus.Assigned, QuantityToPick = 8m };

        wave.Tasks.Add(completedTask);
        wave.Tasks.Add(pendingTask);
        wave.Tasks.Add(assignedTask);

        // 1. Throws if reason is empty
        Assert.Throws<ValidationException>(() => wave.Cancel("", userId));

        // 2. Cancels wave
        wave.Cancel("Order cancelled by customer", userId);

        Assert.Equal(PickWaveStatus.Cancelled, wave.Status);
        Assert.Equal("Order cancelled by customer", wave.CancellationReason);
        Assert.NotNull(wave.CancelledAtUtc);

        // Completed task remains completed; open tasks cancelled
        Assert.Equal(PickTaskStatus.Completed, completedTask.Status);
        Assert.Equal(PickTaskStatus.Cancelled, pendingTask.Status);
        Assert.Equal(PickTaskStatus.Cancelled, assignedTask.Status);

        // 3. Cannot cancel completed wave
        var compWave = new PickWave { WaveNumber = "WAVE-DONE", Status = PickWaveStatus.Completed };
        Assert.Throws<InvalidDomainStateException>(() => compWave.Cancel("Late cancel", userId));
    }

    #endregion

    #region PickTask Domain Tests

    [Fact]
    public void PickTask_Initializes_WithDefaults()
    {
        var task = new PickTask
        {
            QuantityToPick = 20m,
            PickSequence = 10
        };

        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal(20m, task.QuantityToPick);
        Assert.Equal(0m, task.QuantityPicked);
        Assert.Equal(20m, task.ShortPickQuantity);
        Assert.Equal(PickTaskStatus.Pending, task.Status);
        Assert.Equal(10, task.PickSequence);
        Assert.Null(task.AssignedUserId);
        Assert.Null(task.AssignedAtUtc);
        Assert.Null(task.StartedAtUtc);
        Assert.Null(task.CompletedAtUtc);
        Assert.Empty(task.StatusHistories);
        Assert.Empty(task.Exceptions);
    }

    [Fact]
    public void PickTask_Lifecycle_AssignAndStart()
    {
        var userId = Guid.NewGuid();
        var task = new PickTask
        {
            QuantityToPick = 15m,
            Status = PickTaskStatus.Pending
        };

        // 1. Assign to operator
        task.Assign(userId);
        Assert.Equal(PickTaskStatus.Assigned, task.Status);
        Assert.Equal(userId, task.AssignedUserId);
        Assert.NotNull(task.AssignedAtUtc);
        Assert.Single(task.StatusHistories);
        Assert.Equal(PickTaskStatus.Pending, task.StatusHistories.First().FromStatus);
        Assert.Equal(PickTaskStatus.Assigned, task.StatusHistories.First().ToStatus);

        // 2. Start RF picking
        task.Start(userId);
        Assert.Equal(PickTaskStatus.InProgress, task.Status);
        Assert.NotNull(task.StartedAtUtc);
        Assert.Equal(2, task.StatusHistories.Count);
    }

    [Fact]
    public void PickTask_ConfirmPick_FullPick_TransitionsToCompleted()
    {
        var userId = Guid.NewGuid();
        var toteId = Guid.NewGuid();
        var task = new PickTask
        {
            QuantityToPick = 10m,
            Status = PickTaskStatus.InProgress
        };

        // Validation: cannot pick zero or negative
        Assert.Throws<ValidationException>(() => task.ConfirmPick(0m, "TOTE-01", toteId, userId));
        Assert.Throws<ValidationException>(() => task.ConfirmPick(-2m, "TOTE-01", toteId, userId));

        // Validation: cannot pick more than requested
        Assert.Throws<ValidationException>(() => task.ConfirmPick(15m, "TOTE-01", toteId, userId));

        // Validation: cannot confirm without tote barcode
        Assert.Throws<ValidationException>(() => task.ConfirmPick(10m, "", toteId, userId));

        // Full pick
        task.ConfirmPick(10m, "TOTE-01", toteId, userId);

        Assert.Equal(PickTaskStatus.Completed, task.Status);
        Assert.Equal(10m, task.QuantityPicked);
        Assert.Equal(0m, task.ShortPickQuantity);
        Assert.Equal("TOTE-01", task.TargetToteBarcode);
        Assert.Equal(toteId, task.TargetHandlingUnitId);
        Assert.NotNull(task.CompletedAtUtc);
        Assert.Single(task.StatusHistories);
    }

    [Fact]
    public void PickTask_ConfirmPick_PartialPick_TransitionsToShortPicked()
    {
        var userId = Guid.NewGuid();
        var toteId = Guid.NewGuid();
        var task = new PickTask
        {
            QuantityToPick = 10m,
            Status = PickTaskStatus.InProgress
        };

        task.ConfirmPick(6m, "TOTE-02", toteId, userId);

        Assert.Equal(PickTaskStatus.ShortPicked, task.Status);
        Assert.Equal(6m, task.QuantityPicked);
        Assert.Equal(4m, task.ShortPickQuantity);
        Assert.Equal("TOTE-02", task.TargetToteBarcode);
        Assert.NotNull(task.CompletedAtUtc);
    }

    [Fact]
    public void PickTask_ReportShortPick_TransitionsToShortPickedWithAudit()
    {
        var userId = Guid.NewGuid();
        var task = new PickTask
        {
            QuantityToPick = 25m,
            Status = PickTaskStatus.InProgress
        };

        // Validation: cannot report negative picked
        Assert.Throws<ValidationException>(() => task.ReportShortPick(-1m, ShortPickReason.BinEmpty, "Empty", userId));

        // Validation: cannot report short pick if picked >= quantityToPick
        Assert.Throws<ValidationException>(() => task.ReportShortPick(25m, ShortPickReason.BinEmpty, "All found", userId));

        task.ReportShortPick(15m, ShortPickReason.BinEmpty, "Only 15 available in bin", userId);

        Assert.Equal(PickTaskStatus.ShortPicked, task.Status);
        Assert.Equal(15m, task.QuantityPicked);
        Assert.Equal(10m, task.ShortPickQuantity);
        Assert.Equal(ShortPickReason.BinEmpty, task.ShortPickReason);
        Assert.Equal("Only 15 available in bin", task.ShortPickNotes);
        Assert.NotNull(task.CompletedAtUtc);
        Assert.Single(task.StatusHistories);
    }

    [Fact]
    public void PickTask_Cancel_CancelsTaskAndPreventsDoubleCompletion()
    {
        var task = new PickTask
        {
            QuantityToPick = 10m,
            Status = PickTaskStatus.Pending
        };

        task.Cancel("Wave deselect");
        Assert.Equal(PickTaskStatus.Cancelled, task.Status);

        // Cannot cancel already completed task
        var completedTask = new PickTask
        {
            QuantityToPick = 10m,
            QuantityPicked = 10m,
            Status = PickTaskStatus.Completed
        };
        Assert.Throws<InvalidDomainStateException>(() => completedTask.Cancel("Late cancel"));
    }

    #endregion

    #region PickException Domain Tests

    [Fact]
    public void PickException_CalculatesDiscrepancyAndLinksCycleCount()
    {
        var cycleCountId = Guid.NewGuid();
        var exception = new PickException
        {
            ExpectedQuantity = 20m,
            ActualQuantityPicked = 12m,
            Reason = ShortPickReason.DamagedStock,
            Notes = "8 units crushed in carton",
            ReportedByUserId = Guid.NewGuid(),
            GeneratedCycleCountId = cycleCountId,
            LocationLockApplied = true
        };

        Assert.NotEqual(Guid.Empty, exception.Id);
        Assert.Equal(8m, exception.DiscrepancyQuantity);
        Assert.Equal(ShortPickReason.DamagedStock, exception.Reason);
        Assert.Equal(cycleCountId, exception.GeneratedCycleCountId);
        Assert.True(exception.LocationLockApplied);
    }

    #endregion

    #region EF Core Persistence, Query Filters & Cascade Tests

    [Fact]
    public async Task Pick_EFCorePersistence_CascadeDeleteAndTenantIsolation_WorkCorrectly()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityId = Guid.NewGuid();

        var binId = Guid.NewGuid();
        var packingStationId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var allocationId = Guid.NewGuid();
        var waveId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var toteId = Guid.NewGuid();
        var exceptionId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientA, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientA, UserType = UserType.Client };

        // 1. Seed data with Staff / Bypass filter
        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Client A Log", AccountCode = "CLA-LOG" });
            seed.Clients.Add(new Client { Id = clientB, CompanyName = "Client B Log", AccountCode = "CLB-LOG" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Dallas Central", FacilityCode = "DFW-01" });

            seed.Locations.Add(new Location
            {
                Id = binId,
                FacilityId = facilityId,
                LocationCode = "AISLE-01-BAY-02-SHELF-3",
                Barcode = "LOC-01-02-03",
                LocationType = LocationType.Bin
            });

            seed.Locations.Add(new Location
            {
                Id = packingStationId,
                FacilityId = facilityId,
                LocationCode = "PACK-STATION-01",
                Barcode = "LOC-PK-01",
                LocationType = LocationType.PackingStation
            });

            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientA,
                Sku = "SKU-PICK-TEST-01",
                Description = "High Velocity Widget"
            });

            // StockOnHand with movable tote tracking
            seed.StockOnHand.Add(new StockOnHand
            {
                Id = stockId,
                ClientId = clientA,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = binId,
                QtyAvailable = 50m,
                QtyAllocated = 20m,
                HandlingUnitId = toteId,
                LpnCode = "TOTE-900"
            });

            // Outbound Order & Line
            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = clientA,
                FacilityId = facilityId,
                OrderNumber = "ORD-WAVE-001",
                CustomerName = "Retail Customer",
                Status = OutboundOrderStatus.Allocated
            };
            var line = new OutboundOrderLine
            {
                Id = lineId,
                OutboundOrderId = orderId,
                ItemId = itemId,
                LineNumber = 1,
                QuantityOrdered = 20m,
                QuantityAllocated = 20m,
                Status = OutboundOrderLineStatus.Allocated
            };
            order.Lines.Add(line);
            seed.OutboundOrders.Add(order);

            // Inventory Allocation
            var allocation = new InventoryAllocation
            {
                Id = allocationId,
                ClientId = clientA,
                FacilityId = facilityId,
                OutboundOrderId = orderId,
                OutboundOrderLineId = lineId,
                StockOnHandId = stockId,
                ItemId = itemId,
                LocationId = binId,
                QuantityRequested = 20m,
                QuantityAllocated = 20m,
                Status = AllocationStatus.Allocated
            };
            seed.InventoryAllocations.Add(allocation);

            // Handling Unit (Tote)
            var tote = new HandlingUnit
            {
                Id = toteId,
                ClientId = clientA,
                FacilityId = facilityId,
                LpnCode = "TOTE-900",
                UnitType = HandlingUnitType.Tote,
                Status = HandlingUnitStatus.InUse,
                CurrentLocationId = binId,
                CurrentPickWaveId = waveId,
                CurrentOutboundOrderId = orderId
            };
            seed.HandlingUnits.Add(tote);

            // Pick Wave
            var wave = new PickWave
            {
                Id = waveId,
                ClientId = clientA,
                FacilityId = facilityId,
                WaveNumber = "WAVE-DFW-001",
                WaveType = PickWaveType.BatchPick,
                Status = PickWaveStatus.Released,
                Priority = 2,
                ReleasedAtUtc = DateTime.UtcNow
            };

            var waveOrder = new PickWaveOrder
            {
                PickWaveId = waveId,
                OutboundOrderId = orderId,
                SequenceNumber = 1
            };
            wave.WaveOrders.Add(waveOrder);

            // Pick Task
            var task = new PickTask
            {
                Id = taskId,
                ClientId = clientA,
                FacilityId = facilityId,
                PickWaveId = waveId,
                OutboundOrderId = orderId,
                OutboundOrderLineId = lineId,
                InventoryAllocationId = allocationId,
                StockOnHandId = stockId,
                ItemId = itemId,
                SourceLocationId = binId,
                DestinationLocationId = packingStationId,
                TargetHandlingUnitId = toteId,
                TargetToteBarcode = "TOTE-900",
                QuantityToPick = 20m,
                QuantityPicked = 18m,
                PickSequence = 1,
                Status = PickTaskStatus.ShortPicked,
                ShortPickReason = ShortPickReason.BinEmpty,
                ShortPickNotes = "2 missing units"
            };

            task.StatusHistories.Add(new PickTaskStatusHistory
            {
                PickTaskId = taskId,
                FromStatus = PickTaskStatus.InProgress,
                ToStatus = PickTaskStatus.ShortPicked,
                Reason = "Short pick recorded",
                ChangedByUserId = Guid.NewGuid()
            });

            wave.Tasks.Add(task);
            seed.PickWaves.Add(wave);

            // Pick Exception
            var exception = new PickException
            {
                Id = exceptionId,
                ClientId = clientA,
                FacilityId = facilityId,
                PickTaskId = taskId,
                OutboundOrderId = orderId,
                OutboundOrderLineId = lineId,
                LocationId = binId,
                ItemId = itemId,
                ExpectedQuantity = 20m,
                ActualQuantityPicked = 18m,
                Reason = ShortPickReason.BinEmpty,
                Notes = "Discrepancy at bin",
                ReportedByUserId = Guid.NewGuid(),
                LocationLockApplied = true
            };
            seed.PickExceptions.Add(exception);

            // Client B Data (to verify tenant isolation)
            var waveB = new PickWave
            {
                ClientId = clientB,
                FacilityId = facilityId,
                WaveNumber = "WAVE-CLB-999",
                WaveType = PickWaveType.DiscreteOrder,
                Status = PickWaveStatus.Planning
            };
            seed.PickWaves.Add(waveB);

            var toteB = new HandlingUnit
            {
                ClientId = clientB,
                FacilityId = facilityId,
                LpnCode = "TOTE-CLB-01",
                UnitType = HandlingUnitType.Tote,
                Status = HandlingUnitStatus.Available
            };
            seed.HandlingUnits.Add(toteB);

            await seed.SaveChangesAsync();
        }

        // 2. Query under Client A context (Tenant Filter Active)
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            // Verify HandlingUnit retrieval and tenant isolation
            var totes = await context.HandlingUnits.ToListAsync();
            Assert.Single(totes);
            Assert.Equal("TOTE-900", totes[0].LpnCode);
            Assert.Equal(HandlingUnitStatus.InUse, totes[0].Status);

            // Verify StockOnHand LpnCode and HandlingUnitId
            var stock = await context.StockOnHand.FirstAsync(s => s.Id == stockId);
            Assert.Equal("TOTE-900", stock.LpnCode);
            Assert.Equal(toteId, stock.HandlingUnitId);

            // Verify PickWave retrieval and tenant isolation
            var waves = await context.PickWaves
                .Include(w => w.WaveOrders)
                .Include(w => w.Tasks)
                    .ThenInclude(t => t.StatusHistories)
                .Include(w => w.Tasks)
                    .ThenInclude(t => t.Exceptions)
                .ToListAsync();

            Assert.Single(waves);
            Assert.Equal("WAVE-DFW-001", waves[0].WaveNumber);
            Assert.Single(waves[0].WaveOrders);
            Assert.Single(waves[0].Tasks);

            var task = waves[0].Tasks.First();
            Assert.Equal(20m, task.QuantityToPick);
            Assert.Equal(18m, task.QuantityPicked);
            Assert.Equal(2m, task.ShortPickQuantity);
            Assert.Equal(PickTaskStatus.ShortPicked, task.Status);
            Assert.Single(task.StatusHistories);

            // Verify PickException retrieval
            var exceptions = await context.PickExceptions.ToListAsync();
            Assert.Single(exceptions);
            Assert.Equal(2m, exceptions[0].DiscrepancyQuantity);
            Assert.True(exceptions[0].LocationLockApplied);
            Assert.Equal(ShortPickReason.BinEmpty, exceptions[0].Reason);
        }

        // 3. Verify Soft Deletion and Global Query Filters on PickWave and HandlingUnit
        using (var deleteContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            var waveToDelete = await deleteContext.PickWaves.FirstAsync(w => w.Id == waveId);
            var toteToDelete = await deleteContext.HandlingUnits.FirstAsync(h => h.Id == toteId);

            deleteContext.PickWaves.Remove(waveToDelete);
            deleteContext.HandlingUnits.Remove(toteToDelete);
            await deleteContext.SaveChangesAsync();
        }

        // Verify with fresh query context
        using (var verifyContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            // Standard query filter hides soft-deleted entities
            var deletedWave = await verifyContext.PickWaves.FirstOrDefaultAsync(w => w.Id == waveId);
            Assert.Null(deletedWave);

            var deletedTote = await verifyContext.HandlingUnits.FirstOrDefaultAsync(h => h.Id == toteId);
            Assert.Null(deletedTote);

            // Raw query ignoring filters confirms soft deletion audit fields
            var rawWave = await verifyContext.PickWaves.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.Id == waveId);
            Assert.NotNull(rawWave);
            Assert.True(rawWave.IsDeleted);
            Assert.NotNull(rawWave.DeletedAt);

            var rawTote = await verifyContext.HandlingUnits.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == toteId);
            Assert.NotNull(rawTote);
            Assert.True(rawTote.IsDeleted);
            Assert.NotNull(rawTote.DeletedAt);

            // OutboundOrder and InventoryAllocation remain untouched (DeleteBehavior.Restrict)
            Assert.NotNull(await verifyContext.OutboundOrders.FindAsync(orderId));
            Assert.NotNull(await verifyContext.InventoryAllocations.FindAsync(allocationId));
        }
    }

    #endregion
}

