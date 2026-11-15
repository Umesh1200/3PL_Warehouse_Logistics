using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Features.CrossDock.Opportunities.Queries.DetectCrossDockOpportunities;
using Warehouse3PL.Application.Features.CrossDock.Plans.Commands.CancelCrossDockPlan;
using Warehouse3PL.Application.Features.CrossDock.Plans.Commands.GenerateCrossDockPlan;
using Warehouse3PL.Application.Features.CrossDock.Plans.Queries.GetCrossDockPlanById;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.AssignCrossDockTask;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.ConfirmCrossDockTask;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.ReportCrossDockException;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Queries.GetNextCrossDockTask;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.CrossDock;
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

public class CrossDockingAndFlowThroughTests
{
    [Fact]
    public async Task DetectOpportunities_WhenMatchingOutboundDemandExists_ReturnsOpportunity()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-CHI", Name = "Chicago Hub" });
            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", Barcode = "BAR-ST-01", LocationType = LocationType.StagingLane });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-CROSSDOCK-1", Description = "Fast Moving Item", WeightKg = 1m });

            // Inbound Receipt Line
            var session = new ReceivingSession { Id = sessionId, ClientId = clientId, FacilityId = facilityId, SessionNumber = "RCV-SESS-100", Status = ReceivingStatus.Completed };
            session.Lines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = sessionId,
                ItemId = itemId,
                Lpn = "LPN-IN-001",
                QuantityReceived = 100m,
                QuantityExpected = 100m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            seed.ReceivingSessions.Add(session);

            // Staging Inventory
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = stagingLocId,
                QtyAvailable = 100m
            });

            // Outbound Order Demand
            var order = new OutboundOrder
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                FacilityId = facilityId,
                OrderNumber = "ORD-OUT-101",
                CustomerName = "Target Store #45",
                Status = OutboundOrderStatus.Released,
                Priority = 2
            };
            order.Lines.Add(new OutboundOrderLine
            {
                Id = Guid.NewGuid(),
                OutboundOrderId = order.Id,
                ItemId = itemId,
                QuantityOrdered = 60m,
                QuantityAllocated = 0m,
                Status = OutboundOrderLineStatus.Pending
            });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var engine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);

        var opportunities = await engine.DetectOpportunitiesAsync(facilityId, clientId);

        Assert.Single(opportunities);
        var opp = opportunities[0];
        Assert.Equal("SKU-CROSSDOCK-1", opp.Sku);
        Assert.Equal("LPN-IN-001", opp.InboundLpn);
        Assert.Equal(60m, opp.RecommendedCrossDockQty);
        Assert.Equal(40m, opp.RemainingPutawayQty);
        Assert.Equal("ORD-OUT-101", opp.MatchedOutboundOrderNumber);
    }

    [Fact]
    public async Task DetectOpportunities_TenantAndFacilityIsolation_ExcludesOtherTenantDemand()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientAId = Guid.NewGuid();
        var clientBId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var itemIdA = Guid.NewGuid();
        var itemIdB = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", Barcode = "BAR-ST-01", LocationType = LocationType.StagingLane });
            seed.Items.AddRange(
                new Item { Id = itemIdA, ClientId = clientAId, Sku = "SKU-SHARED-CODE", Description = "Client A Item" },
                new Item { Id = itemIdB, ClientId = clientBId, Sku = "SKU-SHARED-CODE", Description = "Client B Item" }
            );

            // Inbound for Client A
            var session = new ReceivingSession { Id = Guid.NewGuid(), ClientId = clientAId, FacilityId = facilityId, SessionNumber = "RCV-A", Status = ReceivingStatus.Completed };
            session.Lines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ItemId = itemIdA,
                Lpn = "LPN-A",
                QuantityReceived = 50m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            seed.ReceivingSessions.Add(session);
            seed.StockOnHand.Add(new StockOnHand { ClientId = clientAId, FacilityId = facilityId, ItemId = itemIdA, LocationId = stagingLocId, QtyAvailable = 50m });

            // Outbound Order for Client B only!
            var orderB = new OutboundOrder { Id = Guid.NewGuid(), ClientId = clientBId, FacilityId = facilityId, OrderNumber = "ORD-B", CustomerName = "Client B Customer", Status = OutboundOrderStatus.Released };
            orderB.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = orderB.Id, ItemId = itemIdB, QuantityOrdered = 50m });
            seed.OutboundOrders.Add(orderB);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var engine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);

        // Client A opportunities must not match Client B demand
        var opportunities = await engine.DetectOpportunitiesAsync(facilityId, clientAId);

        Assert.Empty(opportunities);
    }

    [Fact]
    public async Task DetectOpportunities_QualityRestrictedInventory_ExcludesQuarantinedOrDamagedStock()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", Barcode = "BAR-ST", LocationType = LocationType.StagingLane });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-DAMAGED", Description = "Fragile Goods" });

            // Session with 1 damaged line and 1 quarantined discrepancy
            var session = new ReceivingSession { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, SessionNumber = "RCV-DISCREPANT", Status = ReceivingStatus.Completed };
            var damagedLine = new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ItemId = itemId,
                Lpn = "LPN-DAMAGED",
                QuantityReceived = 30m,
                Condition = ReceiptConditionStatus.Damaged,
                StagingLocationId = stagingLocId
            };
            damagedLine.Discrepancies.Add(new ReceivingDiscrepancy
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ReceiptLineId = damagedLine.Id,
                Type = DiscrepancyType.DamagedGoods,
                DiscrepantQuantity = 30m,
                Status = DiscrepancyResolutionStatus.Quarantined
            });
            session.Lines.Add(damagedLine);
            seed.ReceivingSessions.Add(session);

            var order = new OutboundOrder { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, OrderNumber = "ORD-DEMAND", Status = OutboundOrderStatus.Released };
            order.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemId, QuantityOrdered = 30m });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var engine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);

        var opportunities = await engine.DetectOpportunitiesAsync(facilityId, clientId);

        Assert.Empty(opportunities);
    }

    [Fact]
    public async Task DetectOpportunities_LotMatchingConstraint_RequiresExactLotMatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", Barcode = "BAR-ST", LocationType = LocationType.StagingLane });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-LOT", Description = "Batch item" });

            // Inbound has LOT-A
            var session = new ReceivingSession { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, SessionNumber = "RCV-LOT", Status = ReceivingStatus.Completed };
            session.Lines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ItemId = itemId,
                Lpn = "LPN-LOT-A",
                LotNumber = "LOT-A",
                QuantityReceived = 50m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            seed.ReceivingSessions.Add(session);
            seed.StockOnHand.Add(new StockOnHand { ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = stagingLocId, QtyAvailable = 50m });

            // Outbound specifically requires LOT-B
            var order = new OutboundOrder { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, OrderNumber = "ORD-REQ-LOT-B", Status = OutboundOrderStatus.Released };
            order.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemId, RequiredLotNumber = "LOT-B", QuantityOrdered = 50m });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var engine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);

        var opportunities = await engine.DetectOpportunitiesAsync(facilityId, clientId);

        Assert.Empty(opportunities); // Cannot match LOT-A with order requiring LOT-B
    }

    [Fact]
    public async Task GeneratePlan_PartialQuantityAllocation_SplitsInboundAcrossMultipleOutboundOrders()
    {
        // 100 Inbound units: Order A demand 40, Order B demand 35. Total cross-dock = 75, Remaining putaway = 25.
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var outboundStagingLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "RCV-STAGE-01", Barcode = "BAR-RCV-01", LocationType = LocationType.StagingLane },
                new Location { Id = outboundStagingLocId, FacilityId = facilityId, LocationCode = "OUT-STAGE-01", Barcode = "BAR-OUT-01", LocationType = LocationType.StagingLane }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-SPLIT", Description = "Split Allocation Item", WeightKg = 1m });

            var session = new ReceivingSession { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, SessionNumber = "RCV-100-UNITS", Status = ReceivingStatus.Completed };
            session.Lines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ItemId = itemId,
                Lpn = "LPN-100-SPLIT",
                QuantityReceived = 100m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            seed.ReceivingSessions.Add(session);

            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = stagingLocId,
                QtyAvailable = 100m
            });

            // Order A (40 units)
            var orderA = new OutboundOrder { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, OrderNumber = "ORD-A", Priority = 2, Status = OutboundOrderStatus.Released };
            orderA.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = orderA.Id, ItemId = itemId, QuantityOrdered = 40m });

            // Order B (35 units)
            var orderB = new OutboundOrder { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, OrderNumber = "ORD-B", Priority = 1, Status = OutboundOrderStatus.Released };
            orderB.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = orderB.Id, ItemId = itemId, QuantityOrdered = 35m });

            seed.OutboundOrders.AddRange(orderA, orderB);
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var engine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);
        var handler = new GenerateCrossDockPlanCommandHandler(context, engine);

        // Act - Generate Cross Dock Plan
        var plan = await handler.Handle(new GenerateCrossDockPlanCommand
        {
            FacilityId = facilityId,
            ClientId = clientId,
            TargetOutboundLocationId = outboundStagingLocId
        }, CancellationToken.None);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(75m, plan.TotalPlannedQuantity); // 40 + 35 = 75
        Assert.Equal(2, plan.Tasks.Count); // One task for Order A, one for Order B

        // Verify StockOnHand state at Receiving Staging
        var stagingStock = await context.StockOnHand.FirstAsync(s => s.LocationId == stagingLocId && s.ItemId == itemId);
        Assert.Equal(75m, stagingStock.QtyAllocated); // 75 units locked for cross-dock
        Assert.Equal(25m, stagingStock.QtyAvailable); // 25 units remain available for putaway!
    }

    [Fact]
    public async Task GeneratePlan_ColdChainItem_AssignsCriticalColdChainPriority()
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
                new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "RCV-STG", Barcode = "RCV", LocationType = LocationType.StagingLane },
                new Location { Id = outLocId, FacilityId = facilityId, LocationCode = "OUT-STG", Barcode = "OUT", LocationType = LocationType.StagingLane }
            );

            seed.Items.Add(new Item
            {
                Id = itemId,
                ClientId = clientId,
                Sku = "FROZEN-SERUM",
                Description = "Cold item",
                StorageTempClass = StorageTempClass.Frozen,
                WeightKg = 0.5m
            });

            var session = new ReceivingSession { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, SessionNumber = "RCV-COLD", Status = ReceivingStatus.Completed };
            session.Lines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = session.Id,
                ItemId = itemId,
                Lpn = "LPN-COLD-XDK",
                QuantityReceived = 20m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            seed.ReceivingSessions.Add(session);
            seed.StockOnHand.Add(new StockOnHand { ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = stagingLocId, QtyAvailable = 20m });

            var order = new OutboundOrder { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, OrderNumber = "ORD-URGENT", Status = OutboundOrderStatus.Released };
            order.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = order.Id, ItemId = itemId, QuantityOrdered = 20m });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var engine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);
        var handler = new GenerateCrossDockPlanCommandHandler(context, engine);

        var plan = await handler.Handle(new GenerateCrossDockPlanCommand
        {
            FacilityId = facilityId,
            ClientId = clientId,
            TargetOutboundLocationId = outLocId
        }, CancellationToken.None);

        Assert.Single(plan.Tasks);
        Assert.Equal(CrossDockPriority.CriticalColdChain, plan.Tasks[0].Priority);
    }

    [Fact]
    public async Task GetNextCrossDockTask_ReturnsColdChainBeforeNormal_FIFO()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var loc1 = Guid.NewGuid();
        var loc2 = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        var task1Id = Guid.NewGuid();
        var task2Id = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = loc1, FacilityId = facilityId, LocationCode = "L-1", Barcode = "B-1", LocationType = LocationType.StagingLane },
                new Location { Id = loc2, FacilityId = facilityId, LocationCode = "L-2", Barcode = "B-2", LocationType = LocationType.StagingLane }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-TEST", Description = "Test" });

            var plan = new CrossDockPlan { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, PlanNumber = "P-1" };
            var line = new CrossDockPlanLine { Id = Guid.NewGuid(), CrossDockPlanId = plan.Id, ItemId = itemId, SourceLocationId = loc1, TargetLocationId = loc2 };
            plan.Lines.Add(line);
            seed.CrossDockPlans.Add(plan);

            // Normal task created 30 mins ago
            seed.CrossDockTasks.Add(new CrossDockTask
            {
                Id = task1Id,
                CrossDockPlanId = plan.Id,
                CrossDockPlanLineId = line.Id,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                Lpn = "LPN-NORMAL",
                SourceLocationId = loc1,
                DestinationLocationId = loc2,
                Quantity = 10m,
                Priority = CrossDockPriority.Normal,
                Status = CrossDockTaskStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30)
            });

            // CriticalColdChain task created 5 mins ago
            seed.CrossDockTasks.Add(new CrossDockTask
            {
                Id = task2Id,
                CrossDockPlanId = plan.Id,
                CrossDockPlanLineId = line.Id,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                Lpn = "LPN-COLD",
                SourceLocationId = loc1,
                DestinationLocationId = loc2,
                Quantity = 5m,
                Priority = CrossDockPriority.CriticalColdChain,
                Status = CrossDockTaskStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new GetNextCrossDockTaskQueryHandler(context);

        var nextTask = await handler.Handle(new GetNextCrossDockTaskQuery(facilityId), CancellationToken.None);

        Assert.NotNull(nextTask);
        Assert.Equal(task2Id, nextTask.Id); // Cold chain expedited task dispatched first!
        Assert.Equal(CrossDockPriority.CriticalColdChain, nextTask.Priority);
    }

    [Fact]
    public async Task AssignCrossDockTask_ClaimsTaskToWorker()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var loc1 = Guid.NewGuid();
        var loc2 = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var workerId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = workerId, UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = loc1, FacilityId = facilityId, LocationCode = "L-1", Barcode = "B-1", LocationType = LocationType.StagingLane },
                new Location { Id = loc2, FacilityId = facilityId, LocationCode = "L-2", Barcode = "B-2", LocationType = LocationType.StagingLane }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-1", Description = "Test" });

            var plan = new CrossDockPlan { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, PlanNumber = "P-1" };
            var line = new CrossDockPlanLine { Id = Guid.NewGuid(), CrossDockPlanId = plan.Id, ItemId = itemId, SourceLocationId = loc1, TargetLocationId = loc2 };
            plan.Lines.Add(line);
            seed.CrossDockPlans.Add(plan);

            seed.CrossDockTasks.Add(new CrossDockTask
            {
                Id = taskId,
                CrossDockPlanId = plan.Id,
                CrossDockPlanLineId = line.Id,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                Lpn = "LPN-CLAIM",
                SourceLocationId = loc1,
                DestinationLocationId = loc2,
                Quantity = 15m,
                Status = CrossDockTaskStatus.Pending
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new AssignCrossDockTaskCommandHandler(context, currentUser);

        var result = await handler.Handle(new AssignCrossDockTaskCommand { TaskId = taskId, UserId = workerId }, CancellationToken.None);

        Assert.Equal(CrossDockTaskStatus.Assigned, result.Status);
        Assert.Equal(workerId, result.AssignedUserId);
    }

    [Fact]
    public async Task ConfirmCrossDockTask_LpnMismatch_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var loc1 = Guid.NewGuid();
        var loc2 = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = loc1, FacilityId = facilityId, LocationCode = "L-1", Barcode = "B-1", LocationType = LocationType.StagingLane },
                new Location { Id = loc2, FacilityId = facilityId, LocationCode = "L-2", Barcode = "B-2", LocationType = LocationType.StagingLane }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-1", Description = "Test" });

            var plan = new CrossDockPlan { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, PlanNumber = "P-1" };
            var line = new CrossDockPlanLine { Id = Guid.NewGuid(), CrossDockPlanId = plan.Id, ItemId = itemId, SourceLocationId = loc1, TargetLocationId = loc2 };
            plan.Lines.Add(line);
            seed.CrossDockPlans.Add(plan);

            seed.CrossDockTasks.Add(new CrossDockTask
            {
                Id = taskId,
                CrossDockPlanId = plan.Id,
                CrossDockPlanLineId = line.Id,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                Lpn = "LPN-EXPECTED",
                SourceLocationId = loc1,
                DestinationLocationId = loc2,
                Quantity = 10m,
                Status = CrossDockTaskStatus.Pending
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new ConfirmCrossDockTaskCommandHandler(context, currentUser);

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmCrossDockTaskCommand
        {
            TaskId = taskId,
            ScannedLpn = "LPN-WRONG",
            ScannedDestinationBarcode = "B-2"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmCrossDockTask_DestinationBarcodeMismatch_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var loc1 = Guid.NewGuid();
        var destLoc = Guid.NewGuid();
        var otherLoc = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = loc1, FacilityId = facilityId, LocationCode = "L-1", Barcode = "B-1", LocationType = LocationType.StagingLane },
                new Location { Id = destLoc, FacilityId = facilityId, LocationCode = "OUT-LANE-1", Barcode = "BAR-OUT-1", LocationType = LocationType.StagingLane },
                new Location { Id = otherLoc, FacilityId = facilityId, LocationCode = "OUT-LANE-2", Barcode = "BAR-OUT-2", LocationType = LocationType.StagingLane }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-1", Description = "Test" });

            var plan = new CrossDockPlan { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, PlanNumber = "P-1" };
            var line = new CrossDockPlanLine { Id = Guid.NewGuid(), CrossDockPlanId = plan.Id, ItemId = itemId, SourceLocationId = loc1, TargetLocationId = destLoc };
            plan.Lines.Add(line);
            seed.CrossDockPlans.Add(plan);

            seed.CrossDockTasks.Add(new CrossDockTask
            {
                Id = taskId,
                CrossDockPlanId = plan.Id,
                CrossDockPlanLineId = line.Id,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                Lpn = "LPN-PALLET-1",
                SourceLocationId = loc1,
                DestinationLocationId = destLoc,
                Quantity = 10m,
                Status = CrossDockTaskStatus.Pending
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new ConfirmCrossDockTaskCommandHandler(context, currentUser);

        // Operator scanned BAR-OUT-2 without override permission
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new ConfirmCrossDockTaskCommand
        {
            TaskId = taskId,
            ScannedLpn = "LPN-PALLET-1",
            ScannedDestinationBarcode = "BAR-OUT-2",
            AllowOverride = false
        }, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmCrossDockTask_DualScanSuccess_AtomicallyMovesStockAndLogsTransferTransaction()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sourceLocId = Guid.NewGuid();
        var destLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var workerUserId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = workerUserId, UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = sourceLocId, FacilityId = facilityId, LocationCode = "RCV-STAGE-01", Barcode = "BAR-RCV-01", LocationType = LocationType.StagingLane },
                new Location { Id = destLocId, FacilityId = facilityId, LocationCode = "OUT-MARSHAL-01", Barcode = "BAR-OUT-01", LocationType = LocationType.StagingLane }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-FLOWTHROUGH", Description = "Crossdock Goods" });

            // Initial staging inventory: 40 units allocated for cross-dock
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = sourceLocId,
                QtyAvailable = 10m,
                QtyAllocated = 40m
            });

            var plan = new CrossDockPlan { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, PlanNumber = "XDK-PLAN-01", TotalPlannedQuantity = 40m, Status = CrossDockPlanStatus.InProgress };
            var line = new CrossDockPlanLine { Id = Guid.NewGuid(), CrossDockPlanId = plan.Id, ItemId = itemId, SourceLocationId = sourceLocId, TargetLocationId = destLocId, PlannedQuantity = 40m };
            var alloc = new CrossDockAllocation { Id = Guid.NewGuid(), CrossDockPlanLineId = line.Id, ClientId = clientId, FacilityId = facilityId, ItemId = itemId, OutboundOrderId = Guid.NewGuid(), OutboundOrderLineId = Guid.NewGuid(), AllocatedQuantity = 40m };
            line.Allocations.Add(alloc);
            plan.Lines.Add(line);
            seed.CrossDockPlans.Add(plan);

            seed.CrossDockTasks.Add(new CrossDockTask
            {
                Id = taskId,
                CrossDockPlanId = plan.Id,
                CrossDockPlanLineId = line.Id,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                Lpn = "LPN-FLOW-001",
                SourceLocationId = sourceLocId,
                DestinationLocationId = destLocId,
                Quantity = 40m,
                Status = CrossDockTaskStatus.Pending
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new ConfirmCrossDockTaskCommandHandler(context, currentUser);

        // Act - Dual scan confirmation
        var result = await handler.Handle(new ConfirmCrossDockTaskCommand
        {
            TaskId = taskId,
            ScannedLpn = "LPN-FLOW-001",
            ScannedDestinationBarcode = "BAR-OUT-01"
        }, CancellationToken.None);

        // Assert Task completion
        Assert.Equal(CrossDockTaskStatus.Completed, result.Status);
        Assert.NotNull(result.CompletedAtUtc);
        Assert.Equal(destLocId, result.ActualDestinationLocationId);

        // Verify StockOnHand Relocation
        var sourceStock = await context.StockOnHand.FirstAsync(s => s.LocationId == sourceLocId && s.ItemId == itemId);
        var destStock = await context.StockOnHand.FirstAsync(s => s.LocationId == destLocId && s.ItemId == itemId);

        Assert.Equal(0m, sourceStock.QtyAllocated); // Cleared from staging
        Assert.Equal(10m, sourceStock.QtyAvailable); // Unallocated remainder untouched
        Assert.Equal(40m, destStock.QtyAllocated); // Staged for outbound dispatch!

        // Verify Ledger Transaction
        var xdkTx = await context.InventoryTransactions
            .FirstOrDefaultAsync(t => t.ReferenceId == taskId && t.TxType == InventoryTxType.Transfer);

        Assert.NotNull(xdkTx);
        Assert.Equal(40m, xdkTx.Quantity);
        Assert.Equal(sourceLocId, xdkTx.FromLocationId);
        Assert.Equal(destLocId, xdkTx.ToLocationId);
        Assert.Equal("CROSS_DOCK_TASK", xdkTx.ReferenceType);

        // Verify Plan and Allocation state
        var updatedAlloc = await context.CrossDockAllocations.FirstAsync();
        Assert.Equal(CrossDockAllocationStatus.Transferred, updatedAlloc.Status);

        var updatedPlan = await context.CrossDockPlans.FirstAsync();
        Assert.Equal(CrossDockPlanStatus.Completed, updatedPlan.Status);
    }

    [Fact]
    public async Task ReportException_DamagedOrShortage_DeallocatesAndReturnsStock()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sourceLocId = Guid.NewGuid();
        var destLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = sourceLocId, FacilityId = facilityId, LocationCode = "RCV-STG", Barcode = "BAR-RCV", LocationType = LocationType.StagingLane },
                new Location { Id = destLocId, FacilityId = facilityId, LocationCode = "OUT-STG", Barcode = "BAR-OUT", LocationType = LocationType.StagingLane }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-EXC", Description = "Exception item" });

            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = sourceLocId,
                QtyAvailable = 0m,
                QtyAllocated = 50m
            });

            var order = new OutboundOrder { Id = orderId, ClientId = clientId, FacilityId = facilityId, OrderNumber = "ORD-EXC" };
            var orderLine = new OutboundOrderLine { Id = orderLineId, OutboundOrderId = orderId, ItemId = itemId, QuantityOrdered = 50m, QuantityAllocated = 50m, Status = OutboundOrderLineStatus.Allocated };
            order.Lines.Add(orderLine);
            seed.OutboundOrders.Add(order);

            var plan = new CrossDockPlan { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId, PlanNumber = "P-EXC", TotalPlannedQuantity = 50m };
            var line = new CrossDockPlanLine { Id = Guid.NewGuid(), CrossDockPlanId = plan.Id, ItemId = itemId, SourceLocationId = sourceLocId, TargetLocationId = destLocId, PlannedQuantity = 50m };
            var alloc = new CrossDockAllocation { Id = Guid.NewGuid(), CrossDockPlanLineId = line.Id, ClientId = clientId, FacilityId = facilityId, ItemId = itemId, OutboundOrderId = orderId, OutboundOrderLineId = orderLineId, AllocatedQuantity = 50m, OutboundOrderLine = orderLine };
            line.Allocations.Add(alloc);
            plan.Lines.Add(line);
            seed.CrossDockPlans.Add(plan);

            seed.CrossDockTasks.Add(new CrossDockTask
            {
                Id = taskId,
                CrossDockPlanId = plan.Id,
                CrossDockPlanLineId = line.Id,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                Lpn = "LPN-EXC",
                SourceLocationId = sourceLocId,
                DestinationLocationId = destLocId,
                Quantity = 50m,
                Status = CrossDockTaskStatus.Pending
            });

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new ReportCrossDockExceptionCommandHandler(context, currentUser);

        // Report 10 units shortage
        var exceptionResult = await handler.Handle(new ReportCrossDockExceptionCommand
        {
            TaskId = taskId,
            Type = CrossDockExceptionType.Shortage,
            DiscrepantQuantity = 10m,
            Reason = "10 units damaged/short on arrival",
            ReleaseRemainingStock = true
        }, CancellationToken.None);

        Assert.Equal(CrossDockExceptionType.Shortage, exceptionResult.Type);
        Assert.Equal(10m, exceptionResult.QuantityDiscrepant);

        // Verify task is in ExceptionHold
        var task = await context.CrossDockTasks.FirstAsync(t => t.Id == taskId);
        Assert.Equal(CrossDockTaskStatus.ExceptionHold, task.Status);

        // Verify 10 units released back to QtyAvailable
        var stagingStock = await context.StockOnHand.FirstAsync(s => s.LocationId == sourceLocId && s.ItemId == itemId);
        Assert.Equal(40m, stagingStock.QtyAllocated);
        Assert.Equal(10m, stagingStock.QtyAvailable);

        // Verify outbound order line deallocated
        var updatedOrderLine = await context.OutboundOrderLines.FirstAsync(l => l.Id == orderLineId);
        Assert.Equal(40m, updatedOrderLine.QuantityAllocated);
        Assert.Equal(OutboundOrderLineStatus.PartiallyAllocated, updatedOrderLine.Status);
    }

    [Fact]
    public async Task CancelPlan_ReleasesAllAllocations_RestoresAvailableStockForPutaway()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sourceLocId = Guid.NewGuid();
        var destLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Locations.AddRange(
                new Location { Id = sourceLocId, FacilityId = facilityId, LocationCode = "RCV-STG", Barcode = "BAR-RCV", LocationType = LocationType.StagingLane },
                new Location { Id = destLocId, FacilityId = facilityId, LocationCode = "OUT-STG", Barcode = "BAR-OUT", LocationType = LocationType.StagingLane }
            );

            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-CANCEL", Description = "Cancelled item" });

            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = sourceLocId,
                QtyAvailable = 0m,
                QtyAllocated = 30m
            });

            var order = new OutboundOrder { Id = orderId, ClientId = clientId, FacilityId = facilityId, OrderNumber = "ORD-CANCEL" };
            var orderLine = new OutboundOrderLine { Id = orderLineId, OutboundOrderId = orderId, ItemId = itemId, QuantityOrdered = 30m, QuantityAllocated = 30m, Status = OutboundOrderLineStatus.Allocated };
            order.Lines.Add(orderLine);
            seed.OutboundOrders.Add(order);

            var plan = new CrossDockPlan { Id = planId, ClientId = clientId, FacilityId = facilityId, PlanNumber = "P-CANCEL", TotalPlannedQuantity = 30m };
            var line = new CrossDockPlanLine { Id = Guid.NewGuid(), CrossDockPlanId = plan.Id, ItemId = itemId, SourceLocationId = sourceLocId, TargetLocationId = destLocId, PlannedQuantity = 30m };
            var alloc = new CrossDockAllocation { Id = Guid.NewGuid(), CrossDockPlanLineId = line.Id, ClientId = clientId, FacilityId = facilityId, ItemId = itemId, OutboundOrderId = orderId, OutboundOrderLineId = orderLineId, AllocatedQuantity = 30m, OutboundOrderLine = orderLine };
            line.Allocations.Add(alloc);
            plan.Lines.Add(line);

            seed.CrossDockTasks.Add(new CrossDockTask
            {
                Id = Guid.NewGuid(),
                CrossDockPlanId = plan.Id,
                CrossDockPlanLineId = line.Id,
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                Lpn = "LPN-CANCEL",
                SourceLocationId = sourceLocId,
                DestinationLocationId = destLocId,
                Quantity = 30m,
                Status = CrossDockTaskStatus.Pending
            });
            seed.CrossDockPlans.Add(plan);

            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CancelCrossDockPlanCommandHandler(context);

        var result = await handler.Handle(new CancelCrossDockPlanCommand(planId, "Customer cancelled outbound shipment"), CancellationToken.None);

        Assert.Equal(CrossDockPlanStatus.Cancelled, result.Status);

        // Verify stock is restored to available for putaway
        var stagingStock = await context.StockOnHand.FirstAsync(s => s.LocationId == sourceLocId && s.ItemId == itemId);
        Assert.Equal(0m, stagingStock.QtyAllocated);
        Assert.Equal(30m, stagingStock.QtyAvailable);

        // Verify allocation released
        var cancelledAlloc = await context.CrossDockAllocations.FirstAsync();
        Assert.Equal(CrossDockAllocationStatus.Released, cancelledAlloc.Status);

        // Verify task cancelled
        var task = await context.CrossDockTasks.FirstAsync();
        Assert.Equal(CrossDockTaskStatus.Cancelled, task.Status);

        // Verify order line returned to Pending
        var cancelledOrderLine = await context.OutboundOrderLines.FirstAsync();
        Assert.Equal(0m, cancelledOrderLine.QuantityAllocated);
        Assert.Equal(OutboundOrderLineStatus.Pending, cancelledOrderLine.Status);
    }
}
