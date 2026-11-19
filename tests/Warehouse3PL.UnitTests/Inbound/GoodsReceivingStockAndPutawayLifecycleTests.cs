using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.CompleteReceivingSession;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ScanReceiptLine;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.StartReceivingSession;
using Warehouse3PL.Application.Features.Inventory.Queries.GetStockOnHand;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ConfirmPutawayTask;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.GeneratePutawayTasks;
using Warehouse3PL.Application.Features.Putaway.Tasks.Queries.GetPutawayTasks;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Dock;
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

public class GoodsReceivingStockAndPutawayLifecycleTests
{
    [Fact]
    public async Task CompleteReceivingSession_AtomicallyIncrementsStockOnHandInStaging_AndCreatesLedgerTransaction()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var itemSoundId = Guid.NewGuid();
        var itemDamagedId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });
            seed.Items.AddRange(
                new Item { Id = itemSoundId, ClientId = clientId, Sku = "SKU-SOUND-100", Description = "Sound Goods" },
                new Item { Id = itemDamagedId, ClientId = clientId, Sku = "SKU-DMG-200", Description = "Damaged Goods" }
            );
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-TEST-01",
                Status = AsnStatus.Arrived,
                Lines = [new AsnLineItem { ItemId = itemSoundId, ExpectedQty = 50 }]
            });
            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                ClientId = clientId,
                AsnId = asnId,
                VisitNumber = "YV-01",
                TrailerNumber = "TR-101",
                Status = YardVisitStatus.AtDock
            });
            seed.Locations.Add(new Location
            {
                Id = stagingLocId,
                FacilityId = facilityId,
                LocationCode = "STAGING-01",
                Barcode = "LOC-STG-01",
                LocationType = LocationType.StagingLane,
                IsReceiving = true,
                IsActive = true
            });
            await seed.SaveChangesAsync();
        }

        Guid sessionId;
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var startHandler = new StartReceivingSessionCommandHandler(context, currentUser);
            var session = await startHandler.Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId,
                AsnId = asnId,
                IsBlindReceiving = false,
                SealNumber = "SEAL-9988",
                SealIntact = true
            }, CancellationToken.None);
            sessionId = session.Id;

            var scanHandler = new ScanReceiptLineCommandHandler(context);
            // Scan 10 sound units
            await scanHandler.Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemSoundId,
                QuantityReceived = 10m,
                Lpn = "LPN-SOUND-01",
                LotNumber = "LOT-2026-A",
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            }, CancellationToken.None);

            // Scan 3 damaged units
            await scanHandler.Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemDamagedId,
                QuantityReceived = 3m,
                Lpn = "LPN-DMG-01",
                Condition = ReceiptConditionStatus.Damaged,
                StagingLocationId = stagingLocId
            }, CancellationToken.None);
        }

        // Complete Receiving Session
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var completeHandler = new CompleteReceivingSessionCommandHandler(context, currentUser);
            var completedSession = await completeHandler.Handle(new CompleteReceivingSessionCommand
            {
                ReceivingSessionId = sessionId
            }, CancellationToken.None);

            Assert.Equal(ReceivingStatus.Completed, completedSession.Status);
            Assert.NotNull(completedSession.CompletedAtUtc);
        }

        // Verify StockOnHand and Ledger Transactions
        using (var verify = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var soundStock = await verify.StockOnHand
                .FirstOrDefaultAsync(s => s.ClientId == clientId && s.ItemId == itemSoundId && s.LocationId == stagingLocId);
            Assert.NotNull(soundStock);
            Assert.Equal(10m, soundStock.QtyAvailable);
            Assert.Equal(0m, soundStock.QtyDamaged);

            var dmgStock = await verify.StockOnHand
                .FirstOrDefaultAsync(s => s.ClientId == clientId && s.ItemId == itemDamagedId && s.LocationId == stagingLocId);
            Assert.NotNull(dmgStock);
            Assert.Equal(0m, dmgStock.QtyAvailable);
            Assert.Equal(3m, dmgStock.QtyDamaged);

            var ledgerTxs = await verify.InventoryTransactions
                .Where(t => t.ClientId == clientId && t.FacilityId == facilityId)
                .ToListAsync();
            Assert.Equal(2, ledgerTxs.Count);
            Assert.All(ledgerTxs, tx => Assert.Equal(InventoryTxType.Receive, tx.TxType));
        }
    }

    [Fact]
    public async Task CompleteReceivingSession_IsIdempotent_DoesNotDuplicateStockOnHandOrTransactions()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-IDEM-1", Description = "Idempotent Item" });
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-IDEM-01",
                Status = AsnStatus.Arrived,
                Lines = [new AsnLineItem { ItemId = itemId, ExpectedQty = 50 }]
            });
            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                ClientId = clientId,
                AsnId = asnId,
                VisitNumber = "YV-01",
                TrailerNumber = "TR-101",
                Status = YardVisitStatus.AtDock
            });
            seed.Locations.Add(new Location
            {
                Id = stagingLocId,
                FacilityId = facilityId,
                LocationCode = "STAGING-01",
                Barcode = "LOC-STG-01",
                LocationType = LocationType.StagingLane,
                IsReceiving = true,
                IsActive = true
            });
            await seed.SaveChangesAsync();
        }

        Guid sessionId;
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var session = await new StartReceivingSessionCommandHandler(context, currentUser).Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId,
                AsnId = asnId
            }, CancellationToken.None);
            sessionId = session.Id;

            await new ScanReceiptLineCommandHandler(context).Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemId,
                QuantityReceived = 25m,
                Lpn = "LPN-IDEM-01",
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            }, CancellationToken.None);
        }

        // Call Complete Session first time
        using (var context1 = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var res1 = await new CompleteReceivingSessionCommandHandler(context1, currentUser).Handle(
                new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
            Assert.Equal(ReceivingStatus.Completed, res1.Status);
        }

        // Call Complete Session second time (Idempotent replay)
        using (var context2 = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var res2 = await new CompleteReceivingSessionCommandHandler(context2, currentUser).Handle(
                new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
            Assert.Equal(ReceivingStatus.Completed, res2.Status);
        }

        // Verify balances were not doubled
        using (var verify = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var stock = await verify.StockOnHand
                .FirstOrDefaultAsync(s => s.ClientId == clientId && s.ItemId == itemId && s.LocationId == stagingLocId);
            Assert.NotNull(stock);
            Assert.Equal(25m, stock.QtyAvailable);

            var txs = await verify.InventoryTransactions
                .Where(t => t.ClientId == clientId && t.ItemId == itemId)
                .ToListAsync();
            Assert.Single(txs);
        }
    }

    [Fact]
    public async Task CompleteReceivingSession_BlindReceivingRequiresSupervisorApproval_AndCompletesAfterApproval()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var supervisorId = Guid.NewGuid();
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });
            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                ClientId = clientId,
                VisitNumber = "YV-01",
                TrailerNumber = "TR-101",
                Status = YardVisitStatus.AtDock
            });
            seed.Locations.Add(new Location
            {
                Id = stagingLocId,
                FacilityId = facilityId,
                LocationCode = "STAGING-01",
                Barcode = "LOC-STG-01",
                LocationType = LocationType.StagingLane,
                IsReceiving = true,
                IsActive = true
            });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-BLIND-1", Description = "Blind Item" });
            await seed.SaveChangesAsync();
        }

        Guid sessionId;
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            // Start Blind Receiving Session (no ASN, no PO)
            var session = await new StartReceivingSessionCommandHandler(context, currentUser).Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId,
                IsBlindReceiving = true
            }, CancellationToken.None);
            sessionId = session.Id;
            Assert.True(session.RequiresSupervisorApproval);
            Assert.False(session.IsSupervisorApproved);

            await new ScanReceiptLineCommandHandler(context).Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemId,
                QuantityReceived = 5m,
                Lpn = "LPN-BLIND-01",
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            }, CancellationToken.None);
        }

        // Attempting to complete without approval should throw ValidationException
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var completeHandler = new CompleteReceivingSessionCommandHandler(context, currentUser);
            await Assert.ThrowsAsync<ValidationException>(() => completeHandler.Handle(
                new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None));
        }

        // Supervisor approves session and resolves pending blind discrepancies
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var session = await context.ReceivingSessions
                .Include(s => s.Discrepancies)
                .FirstOrDefaultAsync(s => s.Id == sessionId);
            Assert.NotNull(session);
            session.ApproveSupervisor(supervisorId);
            foreach (var d in session.Discrepancies)
            {
                d.Resolve(DiscrepancyResolutionStatus.Approved, supervisorId, "Blind receipt approved");
            }
            await context.SaveChangesAsync();
        }

        // Complete session now succeeds
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var completeHandler = new CompleteReceivingSessionCommandHandler(context, currentUser);
            var completed = await completeHandler.Handle(
                new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
            Assert.Equal(ReceivingStatus.Completed, completed.Status);
        }
    }

    [Fact]
    public async Task GenerateAndConfirmPutawayTask_RelocatesStockFromStagingToStorageBin()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var storageBinId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });
            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "SKU-PUTAWAY-10",
                Description = "Putaway Item",
                AbcVelocity = AbcVelocity.A,
                StorageTempClass = StorageTempClass.Ambient
            });
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-PUTAWAY-01",
                Status = AsnStatus.Arrived,
                Lines = [new AsnLineItem { ItemId = itemId, ExpectedQty = 50 }]
            });
            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                ClientId = clientId,
                AsnId = asnId,
                VisitNumber = "YV-01",
                TrailerNumber = "TR-101",
                Status = YardVisitStatus.AtDock
            });
            seed.WarehouseZones.Add(new WarehouseZone { Id = zoneId, FacilityId = facilityId, ZoneCode = "ZONE-AMB", Name = "Ambient Zone" });
            seed.Locations.AddRange(
                new Location
                {
                    Id = stagingLocId,
                    FacilityId = facilityId,
                    LocationCode = "STAGING-01",
                    Barcode = "LOC-STG-01",
                    LocationType = LocationType.StagingLane,
                    IsReceiving = true,
                    IsActive = true
                },
                new Location
                {
                    Id = storageBinId,
                    FacilityId = facilityId,
                    ZoneId = zoneId,
                    LocationCode = "BIN-A-01",
                    Barcode = "LOC-BIN-A-01",
                    LocationType = LocationType.Bin,
                    IsActive = true
                }
            );
            await seed.SaveChangesAsync();
        }

        // 1. Receive item into staging
        Guid sessionId;
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var session = await new StartReceivingSessionCommandHandler(context, currentUser).Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId,
                AsnId = asnId
            }, CancellationToken.None);
            sessionId = session.Id;

            await new ScanReceiptLineCommandHandler(context).Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemId,
                QuantityReceived = 15m,
                Lpn = "LPN-PUTAWAY-15",
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            }, CancellationToken.None);

            await new CompleteReceivingSessionCommandHandler(context, currentUser).Handle(
                new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
        }

        // 2. Generate Putaway Tasks
        List<Application.Common.Models.PutawayTaskDto> tasks;
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var locationSelector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
            var generateHandler = new GeneratePutawayTasksCommandHandler(context, locationSelector);
            tasks = await generateHandler.Handle(new GeneratePutawayTasksCommand
            {
                FacilityId = facilityId,
                ClientId = clientId
            }, CancellationToken.None);

            Assert.Single(tasks);
            Assert.Equal("LPN-PUTAWAY-15", tasks[0].LpnCode);
            Assert.Equal(stagingLocId, tasks[0].SourceLocationId);
            Assert.Equal(storageBinId, tasks[0].SuggestedLocationId);
            Assert.Equal(PutawayTaskStatus.Pending, tasks[0].Status);
            Assert.Equal(15m, tasks[0].Quantity);
        }

        var taskId = tasks[0].Id;

        // 3. Confirm Putaway Task via Dual-Scan
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var confirmHandler = new ConfirmPutawayTaskCommandHandler(context, currentUser);
            var confirmed = await confirmHandler.Handle(new ConfirmPutawayTaskCommand
            {
                TaskId = taskId,
                ScannedLpn = "LPN-PUTAWAY-15",
                ScannedLocationBarcode = "LOC-BIN-A-01",
                AllowOverride = false
            }, CancellationToken.None);

            Assert.Equal(PutawayTaskStatus.Completed, confirmed.Status);
            Assert.Equal(storageBinId, confirmed.ActualLocationId);
        }

        // 4. Verify StockOnHand Relocation from Staging to Storage Bin
        using (var verify = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var stagingStock = await verify.StockOnHand
                .FirstOrDefaultAsync(s => s.ClientId == clientId && s.ItemId == itemId && s.LocationId == stagingLocId);
            Assert.NotNull(stagingStock);
            Assert.Equal(0m, stagingStock.QtyAvailable); // Decremented from 15 to 0

            var storageStock = await verify.StockOnHand
                .FirstOrDefaultAsync(s => s.ClientId == clientId && s.ItemId == itemId && s.LocationId == storageBinId);
            Assert.NotNull(storageStock);
            Assert.Equal(15m, storageStock.QtyAvailable); // Incremented to 15

            var transferTx = await verify.InventoryTransactions
                .FirstOrDefaultAsync(t => t.ClientId == clientId && t.ReferenceId == taskId);
            Assert.NotNull(transferTx);
            Assert.Equal(InventoryTxType.Transfer, transferTx.TxType);
            Assert.Equal(stagingLocId, transferTx.FromLocationId);
            Assert.Equal(storageBinId, transferTx.ToLocationId);
            Assert.Equal(15m, transferTx.Quantity);
        }
    }

    [Fact]
    public async Task ConfirmPutawayTask_MismatchedLpnOrUnassignedBinWithoutOverride_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var suggestedBinId = Guid.NewGuid();
        var otherBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        Guid taskId = Guid.NewGuid();
        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago" });
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STG-1", Barcode = "LOC-STG-1", LocationType = LocationType.StagingLane, IsActive = true },
                new Location { Id = suggestedBinId, FacilityId = facilityId, LocationCode = "BIN-1", Barcode = "LOC-BIN-1", LocationType = LocationType.Bin, IsActive = true },
                new Location { Id = otherBinId, FacilityId = facilityId, LocationCode = "BIN-2", Barcode = "LOC-BIN-2", LocationType = LocationType.Bin, IsActive = true }
            );
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-VAL", Description = "Validation SKU" });
            seed.PutawayTasks.Add(new PutawayTask
            {
                Id = taskId,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LpnCode = "LPN-REAL-123",
                SourceLocationId = stagingLocId,
                SuggestedLocationId = suggestedBinId,
                Quantity = 10m,
                Status = PutawayTaskStatus.Pending
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new ConfirmPutawayTaskCommandHandler(context, currentUser);

        // Mismatched LPN barcode should throw
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmPutawayTaskCommand
        {
            TaskId = taskId,
            ScannedLpn = "LPN-WRONG-999",
            ScannedLocationBarcode = "LOC-BIN-1",
            AllowOverride = false
        }, CancellationToken.None));

        // Mismatched Bin barcode without override should throw
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmPutawayTaskCommand
        {
            TaskId = taskId,
            ScannedLpn = "LPN-REAL-123",
            ScannedLocationBarcode = "LOC-BIN-2",
            AllowOverride = false
        }, CancellationToken.None));
    }

    [Fact]
    public async Task GetStockOnHandQuery_NonStaffTenantConfinement_EnforcesTenantAndRejectsOverride()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var itemA = Guid.NewGuid();
        var itemB = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, adminUser, dbName))
        {
            seed.Clients.AddRange(
                new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CL-A" },
                new Client { Id = clientB, CompanyName = "Client B", AccountCode = "CL-B" }
            );
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago" });
            seed.Locations.Add(new Location { Id = locationId, FacilityId = facilityId, LocationCode = "BIN-100", Barcode = "BAR-100", IsActive = true });
            seed.Items.AddRange(
                new Item { Id = itemA, ClientId = clientA, Sku = "SKU-A", Description = "Item A" },
                new Item { Id = itemB, ClientId = clientB, Sku = "SKU-B", Description = "Item B" }
            );
            seed.StockOnHand.AddRange(
                new StockOnHand { ClientId = clientA, FacilityId = facilityId, ItemId = itemA, LocationId = locationId, QtyAvailable = 100m, Version = 1 },
                new StockOnHand { ClientId = clientB, FacilityId = facilityId, ItemId = itemB, LocationId = locationId, QtyAvailable = 200m, Version = 1 }
            );
            await seed.SaveChangesAsync();
        }

        // Test 1: Client A caller attempts to supply Client B's ID in query
        var clientAUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Client,
            TenantId = clientA
        };
        var clientATenantProvider = new TestTenantProvider { TenantId = clientA };

        using (var contextA = TestDbContextFactory.Create(clientATenantProvider, clientAUser, dbName))
        {
            var handler = new GetStockOnHandQueryHandler(contextA, clientATenantProvider, clientAUser);

            // Even when asking for Client B's ID, the handler must confine to Client A
            var result = await handler.Handle(new GetStockOnHandQuery
            {
                FacilityId = facilityId,
                ClientId = clientB // Attempted bypass
            }, CancellationToken.None);

            Assert.Single(result.Items);
            Assert.Equal("SKU-A", result.Items[0].Sku);
            Assert.Equal(clientA, result.Items[0].ClientId);
            Assert.Equal(100m, result.Items[0].QtyAvailable);
        }

        // Test 2: Non-staff with null tenant must fail closed
        var missingTenantUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Client,
            TenantId = null
        };
        var missingTenantProvider = new TestTenantProvider { TenantId = null };

        using (var contextNull = TestDbContextFactory.Create(missingTenantProvider, missingTenantUser, dbName))
        {
            var handler = new GetStockOnHandQueryHandler(contextNull, missingTenantProvider, missingTenantUser);
            var result = await handler.Handle(new GetStockOnHandQuery
            {
                FacilityId = facilityId
            }, CancellationToken.None);

            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        }
    }

    [Fact]
    public async Task GetPutawayTasksQuery_FiltersByFacilityAndTenantConfinement()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facility1 = Guid.NewGuid();
        var facility2 = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var itemA = Guid.NewGuid();
        var itemB = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, adminUser, dbName))
        {
            seed.Clients.AddRange(
                new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CL-A" },
                new Client { Id = clientB, CompanyName = "Client B", AccountCode = "CL-B" }
            );
            seed.Facilities.AddRange(
                new Facility { Id = facility1, FacilityCode = "FAC-1", Name = "Facility 1" },
                new Facility { Id = facility2, FacilityCode = "FAC-2", Name = "Facility 2" }
            );
            seed.Locations.Add(new Location { Id = locationId, FacilityId = facility1, LocationCode = "LOC-1", Barcode = "BAR-1", IsActive = true });
            seed.Items.AddRange(
                new Item { Id = itemA, ClientId = clientA, Sku = "SKU-A", Description = "Item A" },
                new Item { Id = itemB, ClientId = clientB, Sku = "SKU-B", Description = "Item B" }
            );
            seed.PutawayTasks.AddRange(
                new PutawayTask { Id = Guid.NewGuid(), ClientId = clientA, FacilityId = facility1, ItemId = itemA, SourceLocationId = locationId, SuggestedLocationId = locationId, Quantity = 5m, Status = PutawayTaskStatus.Pending },
                new PutawayTask { Id = Guid.NewGuid(), ClientId = clientB, FacilityId = facility1, ItemId = itemB, SourceLocationId = locationId, SuggestedLocationId = locationId, Quantity = 10m, Status = PutawayTaskStatus.Pending }
            );
            await seed.SaveChangesAsync();
        }

        var clientAUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Client,
            TenantId = clientA
        };
        var clientATenantProvider = new TestTenantProvider { TenantId = clientA };

        using (var context = TestDbContextFactory.Create(clientATenantProvider, clientAUser, dbName))
        {
            var handler = new GetPutawayTasksQueryHandler(context, clientAUser, clientATenantProvider);

            // Client A user only sees Client A's putaway task
            var result = await handler.Handle(new GetPutawayTasksQuery
            {
                FacilityId = facility1,
                Status = PutawayTaskStatus.Pending
            }, CancellationToken.None);

            Assert.Single(result.Items);
            Assert.Equal(clientA, result.Items[0].ClientId);
            Assert.Equal("SKU-A", result.Items[0].Sku);
        }
    }
}
