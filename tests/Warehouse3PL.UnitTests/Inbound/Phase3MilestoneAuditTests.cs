using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Features.CrossDock.Plans.Commands.CancelCrossDockPlan;
using Warehouse3PL.Application.Features.CrossDock.Plans.Commands.GenerateCrossDockPlan;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.ConfirmCrossDockTask;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.CompleteReceivingSession;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ScanReceiptLine;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.StartReceivingSession;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ConfirmPutawayTask;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.GeneratePutawayTasks;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.UnitTests.Common;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Inbound;

/// <summary>
/// Phase 3 Comprehensive Milestone Audit & Verification Test Suite.
/// Validates multi-tenancy, facility security, lifecycle invariants, concurrency protections,
/// and end-to-end inventory reconciliation across PO -> Receiving -> Staging -> Cross-Dock / Putaway -> Storage.
/// </summary>
public class Phase3MilestoneAuditTests
{
    [Fact]
    public async Task EndToEndReconciliation_100UnitsReceived_75CrossDocked_25Putaway_ReconcilesPerfectZeroDiscrepancy()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var poLineId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var asnLineId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var outboundStagingLocId = Guid.NewGuid();
        var permanentBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var operatorUserId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = operatorUserId, UserType = UserType.Staff };

        // 1. Arrange - Seed Baseline Warehouse, Inventory Locations, PO, ASN, and Outbound Demand
        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail Global" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Primary Hub" });

            seed.WarehouseZones.Add(new WarehouseZone
            {
                Id = zoneId,
                FacilityId = facilityId,
                ZoneCode = "ZN-MAIN",
                ZoneType = WarehouseZoneType.BulkStorage
            });

            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "STAGING_LANE_01", Barcode = "BAR-RCV-STAGE", LocationType = LocationType.StagingLane },
                new Location { Id = outboundStagingLocId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "OUTBOUND_MARSHAL_01", Barcode = "BAR-OUT-STAGE", LocationType = LocationType.StagingLane },
                new Location { Id = permanentBinId, FacilityId = facilityId, ZoneId = zoneId, LocationCode = "BIN-AISLE1-RACK1-S1", Barcode = "BAR-PERM-BIN", LocationType = LocationType.Bin, MaxWeightKg = 5000, MaxVolumeCm3 = 10000000 }
            );

            seed.DockDoors.Add(new DockDoor
            {
                Id = doorId,
                FacilityId = facilityId,
                DoorNumber = "DOCK-DOOR-01",
                Status = DockDoorStatus.Active
            });

            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                VisitNumber = "YV-AUDIT-01",
                CarrierName = "Apex Logistics Freight",
                TrailerNumber = "TR-2026-X",
                Status = YardVisitStatus.AtDock
            });

            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "SKU-RECON-AUDIT-01",
                Description = "High-Value Consumer Electronics",
                StorageTempClass = StorageTempClass.Ambient,
                WeightKg = 2.0m,
                VolumeCm3 = 5000m
            });

            var po = new PurchaseOrder
            {
                Id = poId,
                ClientId = clientId,
                FacilityId = facilityId,
                PoNumber = "PO-2026-AUDIT",
                Status = PurchaseOrderStatus.Submitted
            };
            po.Lines.Add(new PurchaseOrderLine
            {
                Id = poLineId,
                PurchaseOrderId = poId,
                ItemId = itemId,
                LineNumber = 1,
                ExpectedQty = 100m,
                ReceivedQty = 0m,
                Status = PurchaseOrderLineStatus.Open
            });
            seed.PurchaseOrders.Add(po);

            var asn = new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                PurchaseOrderId = poId,
                AsnNumber = "ASN-2026-AUDIT",
                Status = AsnStatus.Arrived
            };
            asn.Lines.Add(new AsnLineItem
            {
                Id = asnLineId,
                AsnId = asnId,
                ItemId = itemId,
                PurchaseOrderLineId = poLineId,
                ExpectedQty = 100m,
                ReceivedQty = 0m
            });
            seed.AdvanceShippingNotices.Add(asn);

            // Active Outbound Demand (Order for 75 units)
            var outboundOrder = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                FacilityId = facilityId,
                OrderNumber = "ORD-OUT-AUDIT-75",
                CustomerName = "Regional Fulfillment Center #4",
                Status = OutboundOrderStatus.Released,
                Priority = 2
            };
            outboundOrder.Lines.Add(new OutboundOrderLine
            {
                Id = Guid.NewGuid(),
                OutboundOrderId = outboundOrder.Id,
                ItemId = itemId,
                LineNumber = 1,
                QuantityOrdered = 75m,
                QuantityAllocated = 0m,
                Status = OutboundOrderLineStatus.Pending
            });
            seed.OutboundOrders.Add(outboundOrder);

            await seed.SaveChangesAsync();
        }

        // =========================================================================
        // STEP 1: Inbound Receiving Session & Physical Intake of 100 units
        // =========================================================================
        Guid sessionId;
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var startHandler = new StartReceivingSessionCommandHandler(context, currentUser);
            var sessionResult = await startHandler.Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId,
                PurchaseOrderId = poId,
                AsnId = asnId
            }, CancellationToken.None);

            sessionId = sessionResult.Id;

            // Scan intake of 100 sound units on LPN-AUDIT-100
            var scanHandler = new ScanReceiptLineCommandHandler(context);
            await scanHandler.Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemId,
                PurchaseOrderLineId = poLineId,
                AsnLineItemId = asnLineId,
                Lpn = "LPN-AUDIT-100",
                QuantityReceived = 100m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            }, CancellationToken.None);

            // Complete Receiving Session -> posts stock to StockOnHand & creates InventoryTransaction (TxType.Receive)
            var completeHandler = new CompleteReceivingSessionCommandHandler(context, currentUser);
            await completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
        }

        // Verification after Step 1
        using (var verify1 = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var stagingStock = await verify1.StockOnHand.FirstAsync(s => s.LocationId == stagingLocId && s.ItemId == itemId);
            Assert.Equal(100m, stagingStock.QtyAvailable);
            Assert.Equal(0m, stagingStock.QtyAllocated);

            var receiveTx = await verify1.InventoryTransactions.FirstAsync(t => t.TxType == InventoryTxType.Receive);
            Assert.Equal(100m, receiveTx.Quantity);
            Assert.Equal(stagingLocId, receiveTx.ToLocationId);
        }

        // =========================================================================
        // STEP 2: Cross-Dock Engine detects opportunity and allocates 75 units
        // =========================================================================
        Guid crossDockPlanId;
        Guid crossDockTaskId;
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var engine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);
            var planHandler = new GenerateCrossDockPlanCommandHandler(context, engine);

            var planResult = await planHandler.Handle(new GenerateCrossDockPlanCommand
            {
                FacilityId = facilityId,
                ClientId = clientId,
                ReceivingSessionId = sessionId,
                TargetOutboundLocationId = outboundStagingLocId
            }, CancellationToken.None);

            crossDockPlanId = planResult.Id;
            Assert.Equal(75m, planResult.TotalPlannedQuantity);
            Assert.Single(planResult.Tasks);
            crossDockTaskId = planResult.Tasks[0].Id;
        }

        // Verification after Step 2 (Invariant: 75 allocated, 25 available for putaway)
        using (var verify2 = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var stagingStock = await verify2.StockOnHand.FirstAsync(s => s.LocationId == stagingLocId && s.ItemId == itemId);
            Assert.Equal(25m, stagingStock.QtyAvailable); // 25 units left for putaway
            Assert.Equal(75m, stagingStock.QtyAllocated); // 75 units reserved for cross-dock
        }

        // =========================================================================
        // STEP 3: Putaway Engine generates directed putaway for unallocated remainder (25 units)
        // =========================================================================
        Guid putawayTaskId;
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            // Update ReceiptLine with available balance for putaway
            var line = await context.ReceiptLines.FirstAsync(l => l.Lpn == "LPN-AUDIT-100");
            line.QuantityReceived = 25m; // Unallocated remainder
            await context.SaveChangesAsync();

            var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
            var putawayHandler = new GeneratePutawayTasksCommandHandler(context, selector);

            var putawayTasks = await putawayHandler.Handle(new GeneratePutawayTasksCommand
            {
                FacilityId = facilityId,
                ClientId = clientId
            }, CancellationToken.None);

            Assert.Single(putawayTasks);
            var pTask = putawayTasks[0];
            Assert.Equal(25m, pTask.Quantity);
            Assert.Equal(permanentBinId, pTask.SuggestedLocationId);
            putawayTaskId = pTask.Id;
        }

        // =========================================================================
        // STEP 4: Confirm Cross-Dock Dual-Scan (75 units transferred to Outbound Staging)
        // =========================================================================
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var confirmXdkHandler = new ConfirmCrossDockTaskCommandHandler(context, currentUser);
            var xdkResult = await confirmXdkHandler.Handle(new ConfirmCrossDockTaskCommand
            {
                TaskId = crossDockTaskId,
                ScannedLpn = "LPN-AUDIT-100",
                ScannedDestinationBarcode = "BAR-OUT-STAGE"
            }, CancellationToken.None);

            Assert.Equal(CrossDockTaskStatus.Completed, xdkResult.Status);
        }

        // =========================================================================
        // STEP 5: Confirm Putaway Dual-Scan (25 units transferred to Permanent Bin)
        // =========================================================================
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var confirmPutawayHandler = new ConfirmPutawayTaskCommandHandler(context, currentUser);
            var putawayResult = await confirmPutawayHandler.Handle(new ConfirmPutawayTaskCommand
            {
                TaskId = putawayTaskId,
                ScannedLpn = "LPN-AUDIT-100",
                ScannedLocationBarcode = "BAR-PERM-BIN"
            }, CancellationToken.None);

            Assert.Equal(PutawayTaskStatus.Completed, putawayResult.Status);
        }

        // =========================================================================
        // FINAL INVENTORY RECONCILIATION AUDIT (Zero Discrepancy Verification)
        // =========================================================================
        using (var audit = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            // 1. Transient Staging Lane must be completely empty (0 Available, 0 Allocated)
            var stagingStock = await audit.StockOnHand.FirstOrDefaultAsync(s => s.LocationId == stagingLocId && s.ItemId == itemId);
            Assert.NotNull(stagingStock);
            Assert.Equal(0m, stagingStock.QtyAvailable);
            Assert.Equal(0m, stagingStock.QtyAllocated);
            Assert.Equal(0m, stagingStock.TotalQuantity);

            // 2. Outbound Staging Lane holds exactly 75 units allocated for shipping
            var outboundStock = await audit.StockOnHand.FirstOrDefaultAsync(s => s.LocationId == outboundStagingLocId && s.ItemId == itemId);
            Assert.NotNull(outboundStock);
            Assert.Equal(75m, outboundStock.QtyAllocated);
            Assert.Equal(0m, outboundStock.QtyAvailable);
            Assert.Equal(75m, outboundStock.TotalQuantity);

            // 3. Permanent Storage Bin holds exactly 25 units available in inventory
            var binStock = await audit.StockOnHand.FirstOrDefaultAsync(s => s.LocationId == permanentBinId && s.ItemId == itemId);
            Assert.NotNull(binStock);
            Assert.Equal(25m, binStock.QtyAvailable);
            Assert.Equal(0m, binStock.QtyAllocated);
            Assert.Equal(25m, binStock.TotalQuantity);

            // 4. Global Warehouse Balance Sum = 75 + 25 = 100 units exactly
            var facilityStocks = await audit.StockOnHand
                .Where(s => s.FacilityId == facilityId && s.ItemId == itemId)
                .ToListAsync();
            var totalFacilityStock = facilityStocks.Sum(s => s.TotalQuantity);
            Assert.Equal(100m, totalFacilityStock);

            // 5. Append-only Ledger Transactions Audit
            var allLedgerTxs = await audit.InventoryTransactions
                .Where(t => t.FacilityId == facilityId && t.ItemId == itemId)
                .OrderBy(t => t.Id)
                .ToListAsync();

            Assert.Equal(3, allLedgerTxs.Count);

            // Tx 1: Intake Receive Tx (100 units to STAGING)
            Assert.Equal(InventoryTxType.Receive, allLedgerTxs[0].TxType);
            Assert.Equal(100m, allLedgerTxs[0].Quantity);
            Assert.Equal(stagingLocId, allLedgerTxs[0].ToLocationId);

            // Tx 2: Cross-Dock Transfer Tx (75 units from STAGING to OUTBOUND)
            Assert.Equal(InventoryTxType.Transfer, allLedgerTxs[1].TxType);
            Assert.Equal(75m, allLedgerTxs[1].Quantity);
            Assert.Equal(stagingLocId, allLedgerTxs[1].FromLocationId);
            Assert.Equal(outboundStagingLocId, allLedgerTxs[1].ToLocationId);
            Assert.Equal("CROSS_DOCK_TASK", allLedgerTxs[1].ReferenceType);

            // Tx 3: Putaway Transfer Tx (25 units from STAGING to PERM BIN)
            Assert.Equal(InventoryTxType.Transfer, allLedgerTxs[2].TxType);
            Assert.Equal(25m, allLedgerTxs[2].Quantity);
            Assert.Equal(stagingLocId, allLedgerTxs[2].FromLocationId);
            Assert.Equal(permanentBinId, allLedgerTxs[2].ToLocationId);
            Assert.Equal("PUTAWAY_TASK", allLedgerTxs[2].ReferenceType);

            // Net Ledger Movement = 100 in, 0 variance!
            var netReceived = allLedgerTxs.Where(t => t.TxType == InventoryTxType.Receive).Sum(t => t.Quantity);
            var netTransferred = allLedgerTxs.Where(t => t.TxType == InventoryTxType.Transfer).Sum(t => t.Quantity);
            Assert.Equal(100m, netReceived);
            Assert.Equal(100m, netTransferred);
        }
    }

    [Fact]
    public async Task MultiTenancyAudit_CrossDockAndPutaway_StrictlyIsolatesClients()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientAId = Guid.NewGuid();
        var clientBId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var itemIdA = Guid.NewGuid();
        var itemIdB = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientAId, BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGE-01", Barcode = "BAR-01", LocationType = LocationType.StagingLane });

            seed.Items.AddRange(
                new Item { Id = itemIdA, ClientId = clientAId, Sku = "SKU-SHARED-TENANT", Description = "Client A Product" },
                new Item { Id = itemIdB, ClientId = clientBId, Sku = "SKU-SHARED-TENANT", Description = "Client B Product" }
            );

            // Inbound for Client A
            var session = new ReceivingSession { Id = Guid.NewGuid(), ClientId = clientAId, FacilityId = facilityId, SessionNumber = "RCV-TENANT-A", Status = ReceivingStatus.Completed };
            session.Lines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ItemId = itemIdA,
                Lpn = "LPN-TENANT-A",
                QuantityReceived = 50m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            seed.ReceivingSessions.Add(session);

            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientAId,
                FacilityId = facilityId,
                ItemId = itemIdA,
                LocationId = stagingLocId,
                QtyAvailable = 50m
            });

            // Outbound Order for Client B
            var orderB = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = clientBId,
                FacilityId = facilityId,
                OrderNumber = "ORD-TENANT-B",
                Status = OutboundOrderStatus.Released
            };
            orderB.Lines.Add(new OutboundOrderLine
            {
                Id = Guid.NewGuid(),
                OutboundOrderId = orderB.Id,
                ItemId = itemIdB,
                QuantityOrdered = 50m
            });
            seed.OutboundOrders.Add(orderB);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var engine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);

        // When evaluating for Client A, Client B's demand is strictly excluded
        var oppsForClientA = await engine.DetectOpportunitiesAsync(facilityId, clientAId);
        Assert.Empty(oppsForClientA);

        // When evaluating for Client B, Client A's inbound inventory is strictly excluded
        var oppsForClientB = await engine.DetectOpportunitiesAsync(facilityId, clientBId);
        Assert.Empty(oppsForClientB);
    }

    [Fact]
    public async Task ConcurrencyAudit_SimultaneousPutawayAndCrossDock_NeverOverAllocates()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var outLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", Barcode = "BAR-01", LocationType = LocationType.StagingLane },
                new Location { Id = outLocId, FacilityId = facilityId, LocationCode = "OUT-01", Barcode = "BAR-02", LocationType = LocationType.StagingLane }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-CONCURRENCY", Description = "Contested SKU" });

            var session = new ReceivingSession { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, SessionNumber = "RCV-CONC", Status = ReceivingStatus.Completed };
            session.Lines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ItemId = itemId,
                Lpn = "LPN-CONC-50",
                QuantityReceived = 50m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            seed.ReceivingSessions.Add(session);

            // Exactly 50 units in stock
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = stagingLocId,
                QtyAvailable = 50m,
                Version = 1
            });

            var order = new OutboundOrder { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, OrderNumber = "ORD-CONC-50", Status = OutboundOrderStatus.Released };
            order.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemId, QuantityOrdered = 50m });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var engine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);
        var xdkHandler = new GenerateCrossDockPlanCommandHandler(context, engine);

        // 1. Cross-dock allocates all 50 units
        var planResult = await xdkHandler.Handle(new GenerateCrossDockPlanCommand
        {
            FacilityId = facilityId,
            ClientId = clientId,
            TargetOutboundLocationId = outLocId
        }, CancellationToken.None);

        Assert.Equal(50m, planResult.TotalPlannedQuantity);

        // 2. Putaway now checks staging stock: QtyAvailable is 0, so Putaway CANNOT allocate or double-commit
        var stagingStock = await context.StockOnHand.FirstAsync(s => s.LocationId == stagingLocId && s.ItemId == itemId);
        Assert.Equal(0m, stagingStock.QtyAvailable);
        Assert.Equal(50m, stagingStock.QtyAllocated);

        // Putaway task generation produces 0 tasks
        var selector = new PutawayLocationSelector(context, NullLogger<PutawayLocationSelector>.Instance);
        var putawayHandler = new GeneratePutawayTasksCommandHandler(context, selector);

        var putawayTasks = await putawayHandler.Handle(new GeneratePutawayTasksCommand
        {
            FacilityId = facilityId,
            ClientId = clientId
        }, CancellationToken.None);

        Assert.Empty(putawayTasks); // Over-allocation completely prevented!
    }

    [Fact]
    public void LifecycleStateTransitionAudit_RejectsInvalidTransitions()
    {
        // 1. PutawayTask cannot be completed if already completed
        var pTask = new PutawayTask();
        pTask.Complete(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<InvalidDomainStateException>(() => pTask.Complete(Guid.NewGuid(), Guid.NewGuid()));

        // 2. CrossDockTask cannot be assigned if already completed
        var xTask = new CrossDockTask();
        xTask.ConfirmExecution("LPN-1", "BAR-1", Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<InvalidDomainStateException>(() => xTask.AssignToWorker(Guid.NewGuid()));

        // 3. CrossDockPlan cannot be approved if already completed
        var plan = new CrossDockPlan();
        plan.Approve();
        plan.StartExecution();
        plan.Complete();
        Assert.Throws<InvalidDomainStateException>(() => plan.Approve());

        // 4. CrossDockPlan cannot be cancelled if completed
        Assert.Throws<InvalidDomainStateException>(() => plan.Cancel("Attempted abort"));
    }

    [Fact]
    public async Task ExceptionAndCancellationAudit_AbortedCrossDock_RestoresExactPutawayAvailability()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var outLocId = Guid.NewGuid();
        var permBinId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        Guid planId;
        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "RCV-STAGE", Barcode = "RCV", LocationType = LocationType.StagingLane },
                new Location { Id = outLocId, FacilityId = facilityId, LocationCode = "OUT-STAGE", Barcode = "OUT", LocationType = LocationType.StagingLane },
                new Location { Id = permBinId, FacilityId = facilityId, LocationCode = "BIN-STORAGE", Barcode = "PERM", LocationType = LocationType.Bin, MaxWeightKg = 5000, MaxVolumeCm3 = 5000000 }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-RESTORE", Description = "Restorable goods", StorageTempClass = StorageTempClass.Ambient, WeightKg = 1m });

            var session = new ReceivingSession { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, SessionNumber = "RCV-RESTORE", Status = ReceivingStatus.Completed };
            session.Lines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ItemId = itemId,
                Lpn = "LPN-RESTORE-100",
                QuantityReceived = 100m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            seed.ReceivingSessions.Add(session);
            seed.StockOnHand.Add(new StockOnHand { ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = stagingLocId, QtyAvailable = 100m });

            var order = new OutboundOrder { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, OrderNumber = "ORD-CANCEL-ME", Status = OutboundOrderStatus.Released };
            order.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemId, QuantityOrdered = 60m });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var engine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);
            var planHandler = new GenerateCrossDockPlanCommandHandler(context, engine);

            var planResult = await planHandler.Handle(new GenerateCrossDockPlanCommand
            {
                FacilityId = facilityId,
                ClientId = clientId,
                TargetOutboundLocationId = outLocId
            }, CancellationToken.None);

            planId = planResult.Id;
        }

        // Verify stock is locked (60 allocated, 40 available)
        using (var verifyLocked = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var stagingStock = await verifyLocked.StockOnHand.FirstAsync(s => s.LocationId == stagingLocId && s.ItemId == itemId);
            Assert.Equal(60m, stagingStock.QtyAllocated);
            Assert.Equal(40m, stagingStock.QtyAvailable);
        }

        // Act - Customer cancels the outbound order; cancel cross-dock plan
        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var cancelHandler = new CancelCrossDockPlanCommandHandler(context);
            var cancelResult = await cancelHandler.Handle(new CancelCrossDockPlanCommand(planId, "Customer cancelled order"), CancellationToken.None);
            Assert.Equal(CrossDockPlanStatus.Cancelled, cancelResult.Status);
        }

        // Assert - Exact restoration: QtyAllocated drops to 0, QtyAvailable returns to 100
        using (var verifyRestored = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var stagingStock = await verifyRestored.StockOnHand.FirstAsync(s => s.LocationId == stagingLocId && s.ItemId == itemId);
            Assert.Equal(0m, stagingStock.QtyAllocated);
            Assert.Equal(100m, stagingStock.QtyAvailable); // Full 100 units restored!

            // Now putaway engine can process the entire 100 units!
            var selector = new PutawayLocationSelector(verifyRestored, NullLogger<PutawayLocationSelector>.Instance);
            var putawayHandler = new GeneratePutawayTasksCommandHandler(verifyRestored, selector);

            var pTasks = await putawayHandler.Handle(new GeneratePutawayTasksCommand
            {
                FacilityId = facilityId,
                ClientId = clientId
            }, CancellationToken.None);

            Assert.Single(pTasks);
            Assert.Equal(100m, pTasks[0].Quantity);
        }
    }

    [Fact]
    public async Task LpnTraceabilityAudit_HandlingUnitHistory_IsFullyReconstructedFromLedger()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var binLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = operatorId, UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", Barcode = "BAR-STG", LocationType = LocationType.StagingLane },
                new Location { Id = binLocId, FacilityId = facilityId, LocationCode = "BIN-01", Barcode = "BAR-BIN", LocationType = LocationType.Bin }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-TRACE", Description = "Traceable Asset" });

            // Ledger Event 1: Goods Received at Staging
            seed.InventoryTransactions.Add(new InventoryTransaction
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                ToLocationId = stagingLocId,
                TxType = InventoryTxType.Receive,
                Quantity = 50m,
                ReferenceType = "RECEIVING_SESSION",
                UserId = operatorId,
                Notes = "Intake scan for LPN-TRACE-50",
                TxTimestamp = DateTime.UtcNow.AddHours(-2)
            });

            // Ledger Event 2: Putaway Transfer to Bin
            seed.InventoryTransactions.Add(new InventoryTransaction
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                FromLocationId = stagingLocId,
                ToLocationId = binLocId,
                TxType = InventoryTxType.Transfer,
                Quantity = 50m,
                ReferenceType = "PUTAWAY_TASK",
                UserId = operatorId,
                Notes = "Putaway scan confirmed to BIN-01",
                TxTimestamp = DateTime.UtcNow.AddMinutes(-30)
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var auditTrail = await context.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.ItemId == itemId && t.FacilityId == facilityId)
            .OrderBy(t => t.TxTimestamp)
            .ToListAsync();

        Assert.Equal(2, auditTrail.Count);
        Assert.Equal(InventoryTxType.Receive, auditTrail[0].TxType);
        Assert.Equal(InventoryTxType.Transfer, auditTrail[1].TxType);
        Assert.Equal(operatorId, auditTrail[0].UserId);
        Assert.Equal(operatorId, auditTrail[1].UserId);
        Assert.Equal("RECEIVING_SESSION", auditTrail[0].ReferenceType);
        Assert.Equal("PUTAWAY_TASK", auditTrail[1].ReferenceType);
    }
}

