using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ApproveReceivingSession;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.CompleteReceivingSession;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.RecordReceivingDiscrepancy;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ResolveReceivingDiscrepancy;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ScanReceiptLine;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.StartReceivingSession;
using Warehouse3PL.Application.Features.Inbound.Receiving.Queries.GetReceivingSessionById;
using Warehouse3PL.Application.Features.Inbound.Receiving.Queries.GetReceivingSessions;
using Warehouse3PL.Application.Features.Inventory.Queries.GetStockOnHand;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ConfirmPutawayTask;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.GeneratePutawayTasks;
using Warehouse3PL.Application.Features.Putaway.Tasks.Queries.GetPutawayTasks;
using Warehouse3PL.Domain.Constants;
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
    public async Task RealOperationalJourney_ReceiptPosting_TaskGeneration_StockConservingPutaway_LedgerAudited()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var storageBinId = Guid.NewGuid();
        var itemSoundId = Guid.NewGuid();
        var itemDamagedId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.Receive, Permissions.Inbound.DiscrepancyResolve, Permissions.Inbound.PutawayExecute, Permissions.Inbound.PutawayManage]
        };

        using (var seed = TestDbContextFactory.Create(tenantProvider, staffUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = clientId,
                FacilityId = facilityId,
                Status = FacilityAccessStatus.Active
            });
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
                AsnNumber = "ASN-JOURNEY-01",
                Status = AsnStatus.Arrived,
                Lines = [new AsnLineItem { ItemId = itemSoundId, ExpectedQty = 100 }]
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
                    LocationCode = "BIN-A-01",
                    Barcode = "LOC-BIN-A01",
                    LocationType = LocationType.Bin,
                    IsReceiving = false,
                    IsActive = true
                }
            );
            await seed.SaveChangesAsync();
        }

        Guid sessionId;
        using (var context = TestDbContextFactory.Create(tenantProvider, staffUser, dbName))
        {
            var operationalAuth = new WarehouseOperationalAuthorization(context, staffUser);

            // 1. Start Session
            var startHandler = new StartReceivingSessionCommandHandler(context, staffUser, operationalAuth);
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

            // 2. Scan Lines: 100 Sound, 10 Damaged
            var scanHandler = new ScanReceiptLineCommandHandler(context, operationalAuth);
            await scanHandler.Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemSoundId,
                QuantityReceived = 100m,
                Lpn = "LPN-SOUND-01",
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            }, CancellationToken.None);

            await scanHandler.Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemDamagedId,
                QuantityReceived = 10m,
                Lpn = "LPN-DMG-02",
                Condition = ReceiptConditionStatus.Damaged,
                StagingLocationId = stagingLocId
            }, CancellationToken.None);

            // 3. Record Discrepancy -> triggers supervisor gate
            var discHandler = new RecordReceivingDiscrepancyCommandHandler(context, operationalAuth);
            var disc = await discHandler.Handle(new RecordReceivingDiscrepancyCommand
            {
                ReceivingSessionId = sessionId,
                Type = DiscrepancyType.QuantityOverage,
                DiscrepantQuantity = 10m,
                Notes = "10 units damaged overage"
            }, CancellationToken.None);

            // 4. Verify completion is BLOCKED while discrepancy is PendingReview
            var completeHandler = new CompleteReceivingSessionCommandHandler(context, staffUser, operationalAuth);
            await Assert.ThrowsAsync<ValidationException>(() =>
                completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None));

            // 5. Supervisor resolves discrepancy
            var resolveHandler = new ResolveReceivingDiscrepancyCommandHandler(context, staffUser, operationalAuth);
            await resolveHandler.Handle(new ResolveReceivingDiscrepancyCommand
            {
                ReceivingSessionId = sessionId,
                DiscrepancyId = disc.Id,
                Resolution = DiscrepancyResolutionStatus.Approved,
                Notes = "Approved by warehouse supervisor"
            }, CancellationToken.None);

            // 6. Complete Receiving Session -> Atomically ingests into staging ledger
            var completedSession = await completeHandler.Handle(
                new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
            Assert.Equal(ReceivingStatus.Completed, completedSession.Status);

            // Assert staging stock updated
            var soundStagingStock = await context.StockOnHand.FirstOrDefaultAsync(s => s.ItemId == itemSoundId && s.LocationId == stagingLocId);
            var dmgStagingStock = await context.StockOnHand.FirstOrDefaultAsync(s => s.ItemId == itemDamagedId && s.LocationId == stagingLocId);
            Assert.NotNull(soundStagingStock);
            Assert.Equal(100m, soundStagingStock.QtyAvailable);
            Assert.NotNull(dmgStagingStock);
            Assert.Equal(10m, dmgStagingStock.QtyDamaged);

            // Assert transactions recorded
            var receiveTxs = await context.InventoryTransactions
                .Where(t => t.ReferenceId == sessionId && t.TxType == InventoryTxType.Receive)
                .ToListAsync();
            Assert.Equal(2, receiveTxs.Count);

            // 7. Generate Directed Putaway Tasks -> Only completed sound lines are eligible
            var locationSelector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
            var genHandler = new GeneratePutawayTasksCommandHandler(context, locationSelector, operationalAuth);
            var tasks = await genHandler.Handle(new GeneratePutawayTasksCommand
            {
                FacilityId = facilityId,
                ClientId = clientId
            }, CancellationToken.None);

            Assert.Single(tasks);
            var soundTask = tasks[0];
            Assert.Equal("LPN-SOUND-01", soundTask.LpnCode);
            Assert.Equal(100m, soundTask.Quantity);
            Assert.Equal(PutawayTaskStatus.Pending, soundTask.Status);

            // 8. Confirm Dual-Scan: Valid LPN + Target Bin relocates stock
            var confirmHandler = new ConfirmPutawayTaskCommandHandler(context, staffUser, operationalAuth);
            var confirmedTask = await confirmHandler.Handle(new ConfirmPutawayTaskCommand
            {
                TaskId = soundTask.Id,
                ScannedLpn = "LPN-SOUND-01",
                ScannedLocationBarcode = "LOC-BIN-A01",
                AllowOverride = true
            }, CancellationToken.None);

            Assert.Equal(PutawayTaskStatus.Completed, confirmedTask.Status);

            // Staging decremented to 0
            soundStagingStock = await context.StockOnHand.FirstOrDefaultAsync(s => s.ItemId == itemSoundId && s.LocationId == stagingLocId);
            Assert.Equal(0m, soundStagingStock!.QtyAvailable);

            // Destination bin credited with 100
            var binStock = await context.StockOnHand.FirstOrDefaultAsync(s => s.ItemId == itemSoundId && s.LocationId == storageBinId);
            Assert.NotNull(binStock);
            Assert.Equal(100m, binStock.QtyAvailable);

            // Transfer ledger transaction recorded
            var transferTx = await context.InventoryTransactions
                .FirstOrDefaultAsync(t => t.ReferenceId == soundTask.Id && t.TxType == InventoryTxType.Transfer);
            Assert.NotNull(transferTx);
            Assert.Equal(100m, transferTx.Quantity);
            Assert.Equal(stagingLocId, transferTx.FromLocationId);
            Assert.Equal(storageBinId, transferTx.ToLocationId);
        }
    }

    [Fact]
    public async Task Putaway_WhenSourceStockMissingOrInsufficient_ThrowsValidationException_WithoutCreatingInventory()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var storageBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.PutawayExecute, Permissions.Inbound.PutawayManage]
        };

        using (var seed = TestDbContextFactory.Create(tenantProvider, staffUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-DEF-100", Description = "Test Item" });
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STG-01", Barcode = "BAR-STG-01", LocationType = LocationType.StagingLane },
                new Location { Id = storageBinId, FacilityId = facilityId, LocationCode = "BIN-01", Barcode = "BAR-BIN-01", LocationType = LocationType.Bin, IsActive = true }
            );
            // Stock in staging has ONLY 5 units, but task requires 20 units
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = stagingLocId,
                QtyAvailable = 5m
            });
            seed.PutawayTasks.Add(new PutawayTask
            {
                Id = taskId,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LpnCode = "LPN-SHORT-01",
                Quantity = 20m, // Requires 20
                SourceLocationId = stagingLocId,
                SuggestedLocationId = storageBinId,
                Status = PutawayTaskStatus.Pending
            });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, staffUser, dbName))
        {
            var operationalAuth = new WarehouseOperationalAuthorization(context, staffUser);
            var confirmHandler = new ConfirmPutawayTaskCommandHandler(context, staffUser, operationalAuth);

            // Act & Assert: Must throw ValidationException due to insufficient source stock
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                confirmHandler.Handle(new ConfirmPutawayTaskCommand
                {
                    TaskId = taskId,
                    ScannedLpn = "LPN-SHORT-01",
                    ScannedLocationBarcode = "BAR-BIN-01"
                }, CancellationToken.None));

            Assert.Contains("Insufficient stock", ex.Message);

            // Assert destination inventory was NOT created (Stock conservation)
            var destStock = await context.StockOnHand.FirstOrDefaultAsync(s => s.LocationId == storageBinId);
            Assert.Null(destStock);

            // Source stock unchanged at 5
            var sourceStock = await context.StockOnHand.FirstOrDefaultAsync(s => s.LocationId == stagingLocId);
            Assert.Equal(5m, sourceStock!.QtyAvailable);

            // Task remains pending
            var task = await context.PutawayTasks.FindAsync(taskId);
            Assert.Equal(PutawayTaskStatus.Pending, task!.Status);
        }
    }

    [Fact]
    public async Task Putaway_ServerSideOverridePermission_AndDestinationSuitability_Enforced()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var suggestedBinId = Guid.NewGuid();
        var lockedBinId = Guid.NewGuid();
        var validAltBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        // User without override permission
        var operatorUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.PutawayExecute] // Only execute, no manage/override
        };

        // Supervisor with override permission
        var supervisorUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.PutawayExecute, Permissions.Inbound.PutawayManage]
        };

        using (var seed = TestDbContextFactory.Create(tenantProvider, operatorUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-OVERRIDE", Description = "Test Item" });
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STG-01", Barcode = "BAR-STG", LocationType = LocationType.StagingLane },
                new Location { Id = suggestedBinId, FacilityId = facilityId, LocationCode = "BIN-SUGG", Barcode = "BAR-SUGG", LocationType = LocationType.Bin, IsActive = true },
                new Location { Id = lockedBinId, FacilityId = facilityId, LocationCode = "BIN-LOCKED", Barcode = "BAR-LOCKED", LocationType = LocationType.Bin, IsActive = true, IsLocked = true },
                new Location { Id = validAltBinId, FacilityId = facilityId, LocationCode = "BIN-ALT", Barcode = "BAR-ALT", LocationType = LocationType.Bin, IsActive = true }
            );
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
                LpnCode = "LPN-OVR-01",
                Quantity = 50m,
                SourceLocationId = stagingLocId,
                SuggestedLocationId = suggestedBinId,
                Status = PutawayTaskStatus.Pending
            });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, operatorUser, dbName))
        {
            var opAuth = new WarehouseOperationalAuthorization(context, operatorUser);
            var confirmHandler = new ConfirmPutawayTaskCommandHandler(context, operatorUser, opAuth);

            // 1. Caller without override permission attempts override -> ForbiddenException
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                confirmHandler.Handle(new ConfirmPutawayTaskCommand
                {
                    TaskId = taskId,
                    ScannedLpn = "LPN-OVR-01",
                    ScannedLocationBarcode = "BAR-ALT",
                    AllowOverride = true
                }, CancellationToken.None));
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, supervisorUser, dbName))
        {
            var supAuth = new WarehouseOperationalAuthorization(context, supervisorUser);
            var confirmHandler = new ConfirmPutawayTaskCommandHandler(context, supervisorUser, supAuth);

            // 2. Caller with override attempts to store in locked bin -> ValidationException
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                confirmHandler.Handle(new ConfirmPutawayTaskCommand
                {
                    TaskId = taskId,
                    ScannedLpn = "LPN-OVR-01",
                    ScannedLocationBarcode = "BAR-LOCKED",
                    AllowOverride = true
                }, CancellationToken.None));
            Assert.Contains("locked", ex.Message);

            // 3. Caller with override confirms valid alternative bin -> Succeeds
            var res = await confirmHandler.Handle(new ConfirmPutawayTaskCommand
            {
                TaskId = taskId,
                ScannedLpn = "LPN-OVR-01",
                ScannedLocationBarcode = "BAR-ALT",
                AllowOverride = true
            }, CancellationToken.None);

            Assert.Equal(PutawayTaskStatus.Completed, res.Status);
            Assert.Equal(validAltBinId, res.ActualLocationId);
        }
    }

    [Fact]
    public async Task ReceivingCompletion_FallbackStaging_PersistsOnReceiptLine_AndEnablesPutawayDiscovery()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.Receive, Permissions.Inbound.PutawayManage]
        };

        var poId = Guid.NewGuid();

        // Facility with NO pre-existing staging location
        using (var seed = TestDbContextFactory.Create(tenantProvider, staffUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-CHI2", Name = "Chicago West" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-W1", Status = DockDoorStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-AUTO-STG", Description = "Auto Staging SKU" });
            seed.Locations.Add(new Location
            {
                Id = Guid.NewGuid(),
                FacilityId = facilityId,
                LocationCode = "BIN-DEST-01",
                Barcode = "LOC-BIN-D1",
                LocationType = LocationType.Bin,
                MaxWeightKg = 5000,
                MaxVolumeCm3 = 10000000,
                IsActive = true
            });

            var po = new PurchaseOrder { Id = poId, ClientId = clientId, FacilityId = facilityId, PoNumber = "PO-STG-01", Status = PurchaseOrderStatus.Submitted };
            po.Lines.Add(new PurchaseOrderLine { Id = Guid.NewGuid(), PurchaseOrderId = poId, LineNumber = 1, ItemId = itemId, ExpectedQty = 40m });
            seed.PurchaseOrders.Add(po);

            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                ClientId = clientId,
                VisitNumber = "YV-W1",
                TrailerNumber = "TR-W1",
                Status = YardVisitStatus.AtDock
            });
            await seed.SaveChangesAsync();
        }

        Guid sessionId;
        using (var context = TestDbContextFactory.Create(tenantProvider, staffUser, dbName))
        {
            var opAuth = new WarehouseOperationalAuthorization(context, staffUser);

            var startHandler = new StartReceivingSessionCommandHandler(context, staffUser, opAuth);
            var session = await startHandler.Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId,
                PurchaseOrderId = poId,
                IsBlindReceiving = false
            }, CancellationToken.None);
            sessionId = session.Id;

            var scanHandler = new ScanReceiptLineCommandHandler(context, opAuth);
            var line = await scanHandler.Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemId,
                QuantityReceived = 40m,
                Lpn = "LPN-AUTO-01",
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = null // NO staging location specified
            }, CancellationToken.None);

            // Complete session -> creates fallback staging and persists onto ReceiptLine
            var completeHandler = new CompleteReceivingSessionCommandHandler(context, staffUser, opAuth);
            var completed = await completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);

            Assert.Equal(ReceivingStatus.Completed, completed.Status);
            var completedLine = completed.Lines.First();
            Assert.NotNull(completedLine.StagingLocationId);

            // Verify persisted in database
            var dbLine = await context.ReceiptLines.FindAsync(line.Id);
            Assert.NotNull(dbLine!.StagingLocationId);
            var fallback = await context.Locations.FindAsync(dbLine.StagingLocationId.Value);
            Assert.NotNull(fallback);
            Assert.True(fallback!.IsReceiving);
            Assert.True(fallback.IsActive);
            Assert.False(fallback.IsDeleted);
            Assert.Equal(LocationType.StagingLane, fallback.LocationType);

            // Generate Putaway tasks -> discovers the line and generates task!
            var locationSelector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
            var genHandler = new GeneratePutawayTasksCommandHandler(context, locationSelector, opAuth);
            var tasks = await genHandler.Handle(new GeneratePutawayTasksCommand
            {
                FacilityId = facilityId,
                ClientId = clientId
            }, CancellationToken.None);

            Assert.Single(tasks);
            Assert.Equal(dbLine.StagingLocationId, tasks[0].SourceLocationId);
        }
    }

    [Fact]
    public async Task OperationalAuthorization_TenantOwnership_AndFacilityConfinement_SecuritySuite()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityA = Guid.NewGuid();
        var facilityB = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientA };

        // Staff assigned to Facility A (without cross-facility grant)
        var staffFacilityA = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientA,
            FacilityId = facilityA,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inventory.View, Permissions.Inbound.View]
        };

        // Client A with access ONLY to Facility A
        var clientUserA = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientA,
            FacilityId = facilityA,
            UserType = UserType.Client,
            PermissionsList = [Permissions.Inventory.View, Permissions.Inbound.View]
        };

        using (var seed = TestDbContextFactory.Create(tenantProvider, staffFacilityA, dbName))
        {
            seed.Clients.AddRange(
                new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CLA" },
                new Client { Id = clientB, CompanyName = "Client B", AccountCode = "CLB" }
            );
            seed.Facilities.AddRange(
                new Facility { Id = facilityA, FacilityCode = "FAC-A", Name = "Facility A", IsActive = true },
                new Facility { Id = facilityB, FacilityCode = "FAC-B", Name = "Facility B", IsActive = true }
            );
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = clientA,
                FacilityId = facilityA,
                Status = FacilityAccessStatus.Active
            });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, staffFacilityA, dbName))
        {
            var opAuth = new WarehouseOperationalAuthorization(context, staffFacilityA);

            // 1. Staff spoofing X-Facility-Id to Facility B -> ForbiddenException
            staffFacilityA.RequestedFacilityId = facilityB;
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                opAuth.ResolveQueryScopeAsync(null, facilityB, CancellationToken.None));

            // Reset spoofed header
            staffFacilityA.RequestedFacilityId = null;

            // 2. Staff omitting facility parameter -> automatically resolves to assigned Facility A
            var staffScope = await opAuth.ResolveQueryScopeAsync(null, null, CancellationToken.None);
            Assert.Equal(facilityA, staffScope.FacilityId);
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, clientUserA, dbName))
        {
            var opAuth = new WarehouseOperationalAuthorization(context, clientUserA);

            // 3. Client A requesting foreign Facility B -> ForbiddenException
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                opAuth.ResolveQueryScopeAsync(null, facilityB, CancellationToken.None));

            // 4. Client A requesting cross-client data for Client B -> ForbiddenException
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                opAuth.ResolveQueryScopeAsync(clientB, facilityA, CancellationToken.None));

            // 5. Client A requesting authorized Facility A -> succeeds
            var clientScope = await opAuth.ResolveQueryScopeAsync(clientA, facilityA, CancellationToken.None);
            Assert.Equal(clientA, clientScope.ClientId);
            Assert.Equal(facilityA, clientScope.FacilityId);
        }

        // 6. Driver or unsupported role -> ForbiddenException
        var driverUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            UserType = UserType.Driver,
            PermissionsList = [Permissions.Inventory.View]
        };

        using (var context = TestDbContextFactory.Create(tenantProvider, driverUser, dbName))
        {
            var opAuth = new WarehouseOperationalAuthorization(context, driverUser);
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                opAuth.ResolveQueryScopeAsync(null, facilityA, CancellationToken.None));
        }
    }

    [Fact]
    public async Task BlindReceiving_SupervisorApprovalAndDiscrepancyResolution_ThroughOperationalAPI()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.Receive, Permissions.Inbound.View, Permissions.Inbound.Approve, Permissions.Inbound.DiscrepancyResolve]
        };

        using (var seed = TestDbContextFactory.Create(tenantProvider, staffUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-BLIND", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-B1", Status = DockDoorStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-BLIND-01", Description = "Blind Freight" });
            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                ClientId = clientId,
                VisitNumber = "YV-B1",
                TrailerNumber = "TR-B1",
                Status = YardVisitStatus.AtDock
            });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, staffUser, dbName))
        {
            var opAuth = new WarehouseOperationalAuthorization(context, staffUser);

            // 1. Start Blind Receiving Session (RequiresSupervisorApproval = true)
            var startHandler = new StartReceivingSessionCommandHandler(context, staffUser, opAuth);
            var session = await startHandler.Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId,
                IsBlindReceiving = true
            }, CancellationToken.None);

            Assert.True(session.RequiresSupervisorApproval);
            Assert.False(session.IsSupervisorApproved);

            // 2. Scan line -> logs UnexpectedSKU discrepancy and flags session
            var scanHandler = new ScanReceiptLineCommandHandler(context, opAuth);
            await scanHandler.Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = session.Id,
                ItemId = itemId,
                QuantityReceived = 15m,
                Lpn = "LPN-BLIND-01",
                Condition = ReceiptConditionStatus.Sound
            }, CancellationToken.None);

            var dbDiscrepancies = await context.ReceivingDiscrepancies
                .Where(d => d.ReceivingSessionId == session.Id)
                .ToListAsync();
            Assert.Single(dbDiscrepancies);
            Assert.Equal(DiscrepancyType.UnexpectedSKU, dbDiscrepancies[0].Type);
            Assert.Equal(DiscrepancyResolutionStatus.PendingReview, dbDiscrepancies[0].Status);

            // 3. Attempting completion without supervisor approval and discrepancy resolution fails
            var completeHandler = new CompleteReceivingSessionCommandHandler(context, staffUser, opAuth);
            await Assert.ThrowsAsync<ValidationException>(() =>
                completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = session.Id }, CancellationToken.None));

            // 4. Resolve discrepancy via API command
            var resolveHandler = new ResolveReceivingDiscrepancyCommandHandler(context, staffUser, opAuth);
            staffUser.PermissionsList.Remove(Permissions.Inbound.DiscrepancyResolve);
            await Assert.ThrowsAsync<ForbiddenException>(() => resolveHandler.Handle(new ResolveReceivingDiscrepancyCommand
            {
                ReceivingSessionId = session.Id,
                DiscrepancyId = dbDiscrepancies[0].Id,
                Resolution = DiscrepancyResolutionStatus.Approved
            }, CancellationToken.None));
            staffUser.PermissionsList.Add(Permissions.Inbound.DiscrepancyResolve);
            await resolveHandler.Handle(new ResolveReceivingDiscrepancyCommand
            {
                ReceivingSessionId = session.Id,
                DiscrepancyId = dbDiscrepancies[0].Id,
                Resolution = DiscrepancyResolutionStatus.Approved,
                Notes = "Discrepancy reviewed and approved by lead supervisor."
            }, CancellationToken.None);

            // 5. Approve session via API command
            var approveHandler = new ApproveReceivingSessionCommandHandler(context, staffUser, opAuth);
            staffUser.PermissionsList.Remove(Permissions.Inbound.Approve);
            await Assert.ThrowsAsync<ForbiddenException>(() => approveHandler.Handle(
                new ApproveReceivingSessionCommand(session.Id), CancellationToken.None));
            staffUser.PermissionsList.Add(Permissions.Inbound.Approve);
            var approved = await approveHandler.Handle(new ApproveReceivingSessionCommand(session.Id), CancellationToken.None);
            Assert.True(approved.IsSupervisorApproved);

            // 6. Complete session now succeeds
            var completed = await completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = session.Id }, CancellationToken.None);
            Assert.Equal(ReceivingStatus.Completed, completed.Status);
        }
    }

    [Fact]
    public async Task CompleteReceivingSession_DuplicateSubmissions_AreIdempotent()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();

        var poId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var staffUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            TenantId = clientId,
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.Receive]
        };

        using (var seed = TestDbContextFactory.Create(tenantProvider, staffUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-IDEMP", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-I1", Status = DockDoorStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-IDEMP", Description = "Idempotency Test Item" });

            var po = new PurchaseOrder { Id = poId, ClientId = clientId, FacilityId = facilityId, PoNumber = "PO-IDEMP", Status = PurchaseOrderStatus.Submitted };
            po.Lines.Add(new PurchaseOrderLine { Id = Guid.NewGuid(), PurchaseOrderId = poId, LineNumber = 1, ItemId = itemId, ExpectedQty = 50m });
            seed.PurchaseOrders.Add(po);

            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STG-I1", Barcode = "BAR-I1", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true });
            seed.YardVisits.Add(new YardVisit { Id = visitId, FacilityId = facilityId, ClientId = clientId, VisitNumber = "YV-I1", TrailerNumber = "TR-I1", Status = YardVisitStatus.AtDock });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, staffUser, dbName))
        {
            var opAuth = new WarehouseOperationalAuthorization(context, staffUser);
            var startHandler = new StartReceivingSessionCommandHandler(context, staffUser, opAuth);
            var session = await startHandler.Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId,
                PurchaseOrderId = poId,
                IsBlindReceiving = false
            }, CancellationToken.None);

            var scanHandler = new ScanReceiptLineCommandHandler(context, opAuth);
            await scanHandler.Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = session.Id,
                ItemId = itemId,
                QuantityReceived = 50m,
                Lpn = "LPN-IDEMP-01",
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            }, CancellationToken.None);

            var completeHandler = new CompleteReceivingSessionCommandHandler(context, staffUser, opAuth);

            // First completion
            var firstRun = await completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = session.Id }, CancellationToken.None);
            Assert.Equal(ReceivingStatus.Completed, firstRun.Status);

            // Second completion (simulated duplicate click/network replay)
            var secondRun = await completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = session.Id }, CancellationToken.None);
            Assert.Equal(ReceivingStatus.Completed, secondRun.Status);

            // Verify stock is exactly 50 (NOT 100)
            var stock = await context.StockOnHand.FirstOrDefaultAsync(s => s.ItemId == itemId && s.LocationId == stagingLocId);
            Assert.Equal(50m, stock!.QtyAvailable);

            // Verify exactly 1 transaction was written (NOT duplicated)
            var txCount = await context.InventoryTransactions.CountAsync(t => t.ReferenceId == session.Id);
            Assert.Equal(1, txCount);
        }
    }

    [Fact]
    public async Task CompleteReceivingSession_SameLotNumberAcrossItems_CreatesDistinctLotsAndStockKeys()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var stagingId = Guid.NewGuid();
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var tenant = new TestTenantProvider { TenantId = clientId };
        var user = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(), FacilityId = facilityId, TenantId = clientId, UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.Receive]
        };

        using (var seed = TestDbContextFactory.Create(tenant, user, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Lot Client", AccountCode = "LOT" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "LOT-FAC", Name = "Lot Facility" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "LOT-D1", Status = DockDoorStatus.Active });
            seed.YardVisits.Add(new YardVisit { Id = visitId, FacilityId = facilityId, ClientId = clientId, VisitNumber = "LOT-YV", TrailerNumber = "LOT-TR", Status = YardVisitStatus.AtDock });
            seed.Locations.Add(new Location { Id = stagingId, FacilityId = facilityId, LocationCode = "LOT-STG", Barcode = "LOT-STG", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true });
            seed.Items.AddRange(
                new Item { Id = firstItemId, ClientId = clientId, Sku = "LOT-A", Description = "First" },
                new Item { Id = secondItemId, ClientId = clientId, Sku = "LOT-B", Description = "Second" });
            seed.ReceivingSessions.Add(new ReceivingSession
            {
                Id = sessionId, FacilityId = facilityId, DockDoorId = doorId, YardVisitId = visitId,
                ClientId = clientId, SessionNumber = "RCV-LOT-1", Status = ReceivingStatus.InProgress,
                ReceivedByUserId = user.UserId!.Value,
                Lines =
                [
                    new ReceiptLine { ItemId = firstItemId, Lpn = "LOT-LPN-A", QuantityReceived = 3, LotNumber = "SHARED", StagingLocationId = stagingId },
                    new ReceiptLine { ItemId = secondItemId, Lpn = "LOT-LPN-B", QuantityReceived = 4, LotNumber = "SHARED", StagingLocationId = stagingId }
                ]
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenant, user, dbName);
        var auth = new WarehouseOperationalAuthorization(context, user);
        await new CompleteReceivingSessionCommandHandler(context, user, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);

        var lots = await context.InventoryLots.Where(l => l.LotNumber == "SHARED").OrderBy(l => l.ItemId).ToListAsync();
        Assert.Equal(2, lots.Count);
        Assert.NotEqual(lots[0].ItemId, lots[1].ItemId);
        var stocks = await context.StockOnHand.Where(s => s.LocationId == stagingId).ToListAsync();
        Assert.Equal(2, stocks.Count);
        Assert.All(stocks, stock => Assert.NotNull(stock.LotId));
        Assert.Equal(7m, stocks.Sum(s => s.TotalQuantity));
    }

    [Fact]
    public async Task StockProjection_RestoresOnHoldTotalAndLastCountedAt()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var countedAt = DateTime.UtcNow.AddHours(-2);
        var tenant = new TestTenantProvider { TenantId = clientId };
        var user = new TestCurrentUserContext { UserId = Guid.NewGuid(), FacilityId = facilityId, UserType = UserType.Staff };
        using (var seed = TestDbContextFactory.Create(tenant, user, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Stock Client", AccountCode = "STK" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "STK-F", Name = "Stock Facility" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "STK-1", Description = "Stock" });
            seed.Locations.Add(new Location { Id = locationId, FacilityId = facilityId, LocationCode = "STK-BIN", Barcode = "STK-BIN" });
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = locationId,
                QtyAvailable = 1, QtyAllocated = 2, QtyReserved = 3, QtyQuarantined = 4, QtyDamaged = 5,
                QtyOnHold = 6, LastCountedAt = countedAt
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenant, user, dbName);
        var result = await new GetStockOnHandQueryHandler(context, new WarehouseOperationalAuthorization(context, user))
            .Handle(new GetStockOnHandQuery { FacilityId = facilityId }, CancellationToken.None);
        var stock = Assert.Single(result.Items);
        Assert.Equal(6m, stock.QtyOnHold);
        Assert.Equal(21m, stock.TotalQuantity);
        Assert.Equal(countedAt, stock.LastCountedAt);
    }

    [Fact]
    public async Task PutawayGeneration_MultiItemPallet_GeneratesSeparateTasksForDifferentReceiptLinesSharingLpn()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingId = Guid.NewGuid();
        var bin1Id = Guid.NewGuid();
        var bin2Id = Guid.NewGuid();
        var itemAId = Guid.NewGuid();
        var itemBId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var lineAId = Guid.NewGuid();
        var lineBId = Guid.NewGuid();

        var tenant = new TestTenantProvider { TenantId = clientId };
        var user = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.Receive, Permissions.Inbound.PutawayExecute, Permissions.Inbound.PutawayManage]
        };

        using (var seed = TestDbContextFactory.Create(tenant, user, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Multi Pallet Client", AccountCode = "MPC" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "MPC-F", Name = "MPC Facility" });
            seed.Items.AddRange(
                new Item { Id = itemAId, ClientId = clientId, Sku = "SKU-MPC-A", Description = "Item A" },
                new Item { Id = itemBId, ClientId = clientId, Sku = "SKU-MPC-B", Description = "Item B" });
            seed.Locations.AddRange(
                new Location { Id = stagingId, FacilityId = facilityId, LocationCode = "MPC-STG", Barcode = "MPC-STG", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true },
                new Location { Id = bin1Id, FacilityId = facilityId, LocationCode = "MPC-BIN1", Barcode = "MPC-BIN1", LocationType = LocationType.Bin, IsActive = true },
                new Location { Id = bin2Id, FacilityId = facilityId, LocationCode = "MPC-BIN2", Barcode = "MPC-BIN2", LocationType = LocationType.Bin, IsActive = true });
            seed.ReceivingSessions.Add(new ReceivingSession
            {
                Id = sessionId,
                FacilityId = facilityId,
                ClientId = clientId,
                SessionNumber = "RCV-MPC-1",
                Status = ReceivingStatus.InProgress,
                ReceivedByUserId = user.UserId!.Value,
                Lines =
                [
                    new ReceiptLine { Id = lineAId, ItemId = itemAId, Lpn = "PALLET-MULTI-1", QuantityReceived = 10, Condition = ReceiptConditionStatus.Sound, StagingLocationId = stagingId },
                    new ReceiptLine { Id = lineBId, ItemId = itemBId, Lpn = "PALLET-MULTI-1", QuantityReceived = 20, Condition = ReceiptConditionStatus.Sound, StagingLocationId = stagingId }
                ]
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenant, user, dbName);
        var auth = new WarehouseOperationalAuthorization(context, user);
        await new CompleteReceivingSessionCommandHandler(context, user, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);

        var locationSelector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
        var genHandler = new GeneratePutawayTasksCommandHandler(context, locationSelector, auth);
        var tasks = await genHandler.Handle(new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);

        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.Equal("PALLET-MULTI-1", t.LpnCode));
        Assert.Contains(tasks, t => t.LpnId == lineAId && t.ItemId == itemAId && t.Quantity == 10);
        Assert.Contains(tasks, t => t.LpnId == lineBId && t.ItemId == itemBId && t.Quantity == 20);
    }

    [Fact]
    public async Task PutawayGeneration_CrossClientSharedLpn_GeneratesDistinctTasksForBothClients()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingId = Guid.NewGuid();
        var bin1Id = Guid.NewGuid();
        var bin2Id = Guid.NewGuid();
        var itemA = Guid.NewGuid();
        var itemB = Guid.NewGuid();
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var lineA = Guid.NewGuid();
        var lineB = Guid.NewGuid();

        var tenant = new TestTenantProvider { BypassTenantFilter = true };
        var superUser = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            FacilityId = facilityId,
            UserType = UserType.Staff,
            RolesList = ["SuperAdmin"],
            PermissionsList = [Permissions.Inbound.Receive, Permissions.Inbound.PutawayExecute, Permissions.Inbound.PutawayManage]
        };

        using (var seed = TestDbContextFactory.Create(tenant, superUser, dbName))
        {
            seed.Clients.AddRange(
                new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CLA" },
                new Client { Id = clientB, CompanyName = "Client B", AccountCode = "CLB" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "CL-F", Name = "Client Facility" });
            seed.Items.AddRange(
                new Item { Id = itemA, ClientId = clientA, Sku = "SKU-CLA", Description = "Client A Item" },
                new Item { Id = itemB, ClientId = clientB, Sku = "SKU-CLB", Description = "Client B Item" });
            seed.Locations.AddRange(
                new Location { Id = stagingId, FacilityId = facilityId, LocationCode = "CL-STG", Barcode = "CL-STG", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true },
                new Location { Id = bin1Id, FacilityId = facilityId, LocationCode = "CL-BIN1", Barcode = "CL-BIN1", LocationType = LocationType.Bin, IsActive = true },
                new Location { Id = bin2Id, FacilityId = facilityId, LocationCode = "CL-BIN2", Barcode = "CL-BIN2", LocationType = LocationType.Bin, IsActive = true });
            seed.ReceivingSessions.AddRange(
                new ReceivingSession
                {
                    Id = sessionA, FacilityId = facilityId, ClientId = clientA, SessionNumber = "RCV-CLA",
                    Status = ReceivingStatus.InProgress, ReceivedByUserId = superUser.UserId!.Value,
                    Lines = [new ReceiptLine { Id = lineA, ItemId = itemA, Lpn = "SHARED-PALLET-01", QuantityReceived = 5, Condition = ReceiptConditionStatus.Sound, StagingLocationId = stagingId }]
                },
                new ReceivingSession
                {
                    Id = sessionB, FacilityId = facilityId, ClientId = clientB, SessionNumber = "RCV-CLB",
                    Status = ReceivingStatus.InProgress, ReceivedByUserId = superUser.UserId!.Value,
                    Lines = [new ReceiptLine { Id = lineB, ItemId = itemB, Lpn = "SHARED-PALLET-01", QuantityReceived = 8, Condition = ReceiptConditionStatus.Sound, StagingLocationId = stagingId }]
                });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenant, superUser, dbName);
        var auth = new WarehouseOperationalAuthorization(context, superUser);
        await new CompleteReceivingSessionCommandHandler(context, superUser, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = sessionA }, CancellationToken.None);
        await new CompleteReceivingSessionCommandHandler(context, superUser, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = sessionB }, CancellationToken.None);

        var locationSelector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
        var genHandler = new GeneratePutawayTasksCommandHandler(context, locationSelector, auth);
        var tasks = await genHandler.Handle(new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);

        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.Equal("SHARED-PALLET-01", t.LpnCode));
        Assert.Contains(tasks, t => t.ClientId == clientA && t.ItemId == itemA && t.Quantity == 5);
        Assert.Contains(tasks, t => t.ClientId == clientB && t.ItemId == itemB && t.Quantity == 8);
    }

    [Fact]
    public async Task PutawayGeneration_RepeatedSubmission_IsIdempotentAndDoesNotDuplicateTasks()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var tenant = new TestTenantProvider { TenantId = clientId };
        var user = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.Receive, Permissions.Inbound.PutawayExecute, Permissions.Inbound.PutawayManage]
        };

        using (var seed = TestDbContextFactory.Create(tenant, user, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Repeat Client", AccountCode = "RPC" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "RPC-F", Name = "Repeat Facility" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-RPC", Description = "Repeat Item" });
            seed.Locations.AddRange(
                new Location { Id = stagingId, FacilityId = facilityId, LocationCode = "RPC-STG", Barcode = "RPC-STG", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true },
                new Location { Id = binId, FacilityId = facilityId, LocationCode = "RPC-BIN", Barcode = "RPC-BIN", LocationType = LocationType.Bin, IsActive = true });
            seed.ReceivingSessions.Add(new ReceivingSession
            {
                Id = sessionId, FacilityId = facilityId, ClientId = clientId, SessionNumber = "RCV-RPC",
                Status = ReceivingStatus.InProgress, ReceivedByUserId = user.UserId!.Value,
                Lines = [new ReceiptLine { ItemId = itemId, Lpn = "RPC-LPN", QuantityReceived = 12, Condition = ReceiptConditionStatus.Sound, StagingLocationId = stagingId }]
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenant, user, dbName);
        var auth = new WarehouseOperationalAuthorization(context, user);
        await new CompleteReceivingSessionCommandHandler(context, user, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);

        var locationSelector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
        var genHandler = new GeneratePutawayTasksCommandHandler(context, locationSelector, auth);

        var firstTasks = await genHandler.Handle(new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);
        Assert.Single(firstTasks);

        var secondTasks = await genHandler.Handle(new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);
        Assert.Empty(secondTasks);

        var allTasks = await context.PutawayTasks.Where(t => t.FacilityId == facilityId).ToListAsync();
        Assert.Single(allTasks);
    }

    [Fact]
    public async Task PutawayGeneration_CancelledTask_AllowsRegenerationForReceiptLine()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        var tenant = new TestTenantProvider { TenantId = clientId };
        var user = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.Receive, Permissions.Inbound.PutawayExecute, Permissions.Inbound.PutawayManage]
        };

        using (var seed = TestDbContextFactory.Create(tenant, user, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Regen Client", AccountCode = "RGC" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "RGC-F", Name = "Regen Facility" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-RGC", Description = "Regen Item" });
            seed.Locations.AddRange(
                new Location { Id = stagingId, FacilityId = facilityId, LocationCode = "RGC-STG", Barcode = "RGC-STG", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true },
                new Location { Id = binId, FacilityId = facilityId, LocationCode = "RGC-BIN", Barcode = "RGC-BIN", LocationType = LocationType.Bin, IsActive = true });
            seed.ReceivingSessions.Add(new ReceivingSession
            {
                Id = sessionId, FacilityId = facilityId, ClientId = clientId, SessionNumber = "RCV-RGC",
                Status = ReceivingStatus.InProgress, ReceivedByUserId = user.UserId!.Value,
                Lines = [new ReceiptLine { Id = lineId, ItemId = itemId, Lpn = "RGC-LPN", QuantityReceived = 15, Condition = ReceiptConditionStatus.Sound, StagingLocationId = stagingId }]
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenant, user, dbName);
        var auth = new WarehouseOperationalAuthorization(context, user);
        await new CompleteReceivingSessionCommandHandler(context, user, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);

        var locationSelector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
        var genHandler = new GeneratePutawayTasksCommandHandler(context, locationSelector, auth);

        var initialTasks = await genHandler.Handle(new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);
        var initialTask = Assert.Single(initialTasks);
        Assert.Equal(PutawayTaskStatus.Pending, initialTask.Status);

        // Cancel the task
        var entity = await context.PutawayTasks.SingleAsync(t => t.Id == initialTask.Id);
        entity.Status = PutawayTaskStatus.Cancelled;
        await context.SaveChangesAsync();

        // Regenerate -> should create a new active task for the line
        var regeneratedTasks = await genHandler.Handle(new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);
        var newTask = Assert.Single(regeneratedTasks);
        Assert.NotEqual(initialTask.Id, newTask.Id);
        Assert.Equal(lineId, newTask.LpnId);
        Assert.Equal(PutawayTaskStatus.Pending, newTask.Status);

        // Verify total tasks in DB: 1 cancelled, 1 active
        var dbTasks = await context.PutawayTasks.Where(t => t.FacilityId == facilityId).ToListAsync();
        Assert.Equal(2, dbTasks.Count);
        Assert.Single(dbTasks, t => t.Status == PutawayTaskStatus.Cancelled);
        Assert.Single(dbTasks, t => t.Status == PutawayTaskStatus.Pending);
    }

    [Fact]
    public async Task PutawayGeneration_PreservesNormalizedLotIdentity()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var tenant = new TestTenantProvider { TenantId = clientId };
        var user = new TestCurrentUserContext
        {
            UserId = Guid.NewGuid(),
            FacilityId = facilityId,
            UserType = UserType.Staff,
            PermissionsList = [Permissions.Inbound.Receive, Permissions.Inbound.PutawayExecute, Permissions.Inbound.PutawayManage]
        };

        using (var seed = TestDbContextFactory.Create(tenant, user, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Norm Lot Client", AccountCode = "NLC" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "NLC-F", Name = "Norm Facility" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-NLC", Description = "Norm Item" });
            seed.Locations.AddRange(
                new Location { Id = stagingId, FacilityId = facilityId, LocationCode = "NLC-STG", Barcode = "NLC-STG", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true },
                new Location { Id = binId, FacilityId = facilityId, LocationCode = "NLC-BIN", Barcode = "NLC-BIN", LocationType = LocationType.Bin, IsActive = true });
            seed.ReceivingSessions.Add(new ReceivingSession
            {
                Id = sessionId, FacilityId = facilityId, ClientId = clientId, SessionNumber = "RCV-NLC",
                Status = ReceivingStatus.InProgress, ReceivedByUserId = user.UserId!.Value,
                Lines = [new ReceiptLine { ItemId = itemId, Lpn = "NLC-LPN", QuantityReceived = 7, LotNumber = "lot-exp-01  ", Condition = ReceiptConditionStatus.Sound, StagingLocationId = stagingId }]
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenant, user, dbName);
        var auth = new WarehouseOperationalAuthorization(context, user);
        await new CompleteReceivingSessionCommandHandler(context, user, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);

        var lot = await context.InventoryLots.SingleAsync(l => l.ClientId == clientId && l.ItemId == itemId);
        Assert.Equal("LOT-EXP-01", lot.LotNumber);

        var locationSelector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
        var genHandler = new GeneratePutawayTasksCommandHandler(context, locationSelector, auth);
        var tasks = await genHandler.Handle(new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);

        var task = Assert.Single(tasks);
        Assert.Equal(lot.Id, task.LotId);

        // Confirm putaway with dual-scan and verify lot identity moves to destination
        var confirmHandler = new ConfirmPutawayTaskCommandHandler(context, user, auth);
        await confirmHandler.Handle(new ConfirmPutawayTaskCommand
        {
            TaskId = task.Id,
            ScannedLpn = "NLC-LPN",
            ScannedLocationBarcode = "NLC-BIN"
        }, CancellationToken.None);

        var destStock = await context.StockOnHand.SingleAsync(s => s.LocationId == binId);
        Assert.Equal(lot.Id, destStock.LotId);
        Assert.Equal(7m, destStock.QtyAvailable);
    }
}
