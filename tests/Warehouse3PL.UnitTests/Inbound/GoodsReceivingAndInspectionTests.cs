using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Inbound.Inspections.Commands.RecordInspectionDecision;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.CompleteReceivingSession;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.RecordReceivingDiscrepancy;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.ScanReceiptLine;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.StartReceivingSession;
using Warehouse3PL.Application.Features.Inbound.Receiving.Queries.GetReceivingSessionById;
using Warehouse3PL.Application.Features.Inbound.Receiving.Queries.GetReceivingSessions;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Inbound;

public class GoodsReceivingAndInspectionTests
{
    [Fact]
    public async Task StartReceivingSession_WhenTrailerAtDock_CreatesStartedSession()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-SOUND-01", Description = "Test Goods" });
            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-ORD-01",
                Status = AsnStatus.Arrived,
                Lines = [new AsnLineItem { ItemId = itemId, ExpectedQty = 100 }]
            });
            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                ClientId = clientId,
                AsnId = asnId,
                VisitNumber = "YV-ATDOCK-01",
                TrailerNumber = "TR-DOCK-101",
                Status = YardVisitStatus.AtDock // Confirmed AtDock!
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new StartReceivingSessionCommandHandler(context, currentUser);

        var result = await handler.Handle(new StartReceivingSessionCommand
        {
            FacilityId = facilityId,
            DockDoorId = doorId,
            YardVisitId = visitId,
            AsnId = asnId,
            SealNumber = "SEAL-9988",
            SealIntact = true,
            Notes = "Standard intake session"
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("RCV-FAC-ORD1-", result.SessionNumber);
        Assert.Equal(ReceivingStatus.Started, result.Status);
        Assert.Equal("DOOR-01", result.DoorNumber);
        Assert.Equal("TR-DOCK-101", result.TrailerNumber);
        Assert.False(result.IsBlindReceiving);
    }

    [Fact]
    public async Task StartReceivingSession_WhenTrailerNotAtDock_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });
            seed.YardVisits.Add(new YardVisit
            {
                Id = visitId,
                FacilityId = facilityId,
                VisitNumber = "YV-WAIT-01",
                TrailerNumber = "TR-WAITING",
                Status = YardVisitStatus.WaitingInYard // Not yet AtDock!
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new StartReceivingSessionCommandHandler(context, currentUser);

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task StartReceivingSession_ConcurrentSessionOnSameDoor_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visit1Id = Guid.NewGuid();
        var visit2Id = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-01", Status = DockDoorStatus.Active });

            seed.YardVisits.Add(new YardVisit { Id = visit1Id, FacilityId = facilityId, VisitNumber = "YV-1", TrailerNumber = "TR-1", Status = YardVisitStatus.AtDock });
            seed.YardVisits.Add(new YardVisit { Id = visit2Id, FacilityId = facilityId, VisitNumber = "YV-2", TrailerNumber = "TR-2", Status = YardVisitStatus.AtDock });

            seed.ReceivingSessions.Add(new ReceivingSession
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visit1Id,
                SessionNumber = "RCV-ACTIVE-01",
                Status = ReceivingStatus.InProgress,
                ReceivedByUserId = Guid.NewGuid()
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new StartReceivingSessionCommandHandler(context, currentUser);

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visit2Id
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task StartReceivingSession_ColdChainRefrigeratedDoor_MandatoryProbeTempCheck()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var coldItemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-COLD", Status = DockDoorStatus.Active, SupportsRefrigerated = true });

            var item = new Item { Id = coldItemId, ClientId = clientId, Sku = "SKU-COLD-VACCINE", Description = "Cold Chain", StorageTempClass = StorageTempClass.Refrigerated };
            seed.Items.Add(item);

            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-COLD-01",
                Status = AsnStatus.Arrived,
                Lines = [new AsnLineItem { ItemId = coldItemId, ExpectedQty = 100 }]
            });

            seed.YardVisits.Add(new YardVisit { Id = visitId, FacilityId = facilityId, ClientId = clientId, AsnId = asnId, VisitNumber = "YV-COLD", TrailerNumber = "TR-REEFER", Status = YardVisitStatus.AtDock });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new StartReceivingSessionCommandHandler(context, currentUser);

        // Missing InitialProbeTemperature throws ValidationException
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new StartReceivingSessionCommand
            {
                FacilityId = facilityId,
                DockDoorId = doorId,
                YardVisitId = visitId,
                AsnId = asnId,
                InitialProbeTemperature = null // Missing!
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task StartReceivingSession_ColdChainTempExcursion_FlagsSessionAndCreatesDiscrepancy()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var doorId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var coldItemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "DOOR-COLD", Status = DockDoorStatus.Active, SupportsRefrigerated = true });

            var item = new Item { Id = coldItemId, ClientId = clientId, Sku = "SKU-COLD-VACCINE", Description = "Cold Chain", StorageTempClass = StorageTempClass.Refrigerated };
            seed.Items.Add(item);

            seed.AdvanceShippingNotices.Add(new AdvanceShippingNotice
            {
                Id = asnId,
                ClientId = clientId,
                FacilityId = facilityId,
                AsnNumber = "ASN-COLD-EXC",
                Status = AsnStatus.Arrived,
                Lines = [new AsnLineItem { ItemId = coldItemId, ExpectedQty = 50 }]
            });

            seed.YardVisits.Add(new YardVisit { Id = visitId, FacilityId = facilityId, ClientId = clientId, AsnId = asnId, VisitNumber = "YV-COLD-EXC", TrailerNumber = "TR-REEFER-02", Status = YardVisitStatus.AtDock });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new StartReceivingSessionCommandHandler(context, currentUser);

        // 14.5°C exceeds safe 8°C upper threshold for refrigerated cargo!
        var result = await handler.Handle(new StartReceivingSessionCommand
        {
            FacilityId = facilityId,
            DockDoorId = doorId,
            YardVisitId = visitId,
            AsnId = asnId,
            InitialProbeTemperature = 14.5m
        }, CancellationToken.None);

        Assert.Equal(ReceivingStatus.Flagged, result.Status);
        Assert.True(result.RequiresSupervisorApproval);
        Assert.Single(result.Discrepancies);
        Assert.Equal(DiscrepancyType.TempExcursion, result.Discrepancies[0].Type);
        Assert.Equal(DiscrepancyResolutionStatus.PendingReview, result.Discrepancies[0].Status);
    }

    [Fact]
    public async Task ScanReceiptLine_ValidPoBackedLine_SucceedsAndGeneratesLpn()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var poLineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-PO-01", Description = "Olive Oil Bottles" });

            var po = new PurchaseOrder
            {
                Id = poId,
                ClientId = clientId,
                FacilityId = facilityId,
                PoNumber = "PO-2026-001",
                Status = PurchaseOrderStatus.Submitted
            };
            po.Lines.Add(new PurchaseOrderLine
            {
                Id = poLineId,
                PurchaseOrderId = poId,
                LineNumber = 1,
                ItemId = itemId,
                ExpectedQty = 200m
            });
            seed.PurchaseOrders.Add(po);

            seed.ReceivingSessions.Add(new ReceivingSession
            {
                Id = sessionId,
                FacilityId = facilityId,
                ClientId = clientId,
                PurchaseOrderId = poId,
                SessionNumber = "RCV-TEST-01",
                Status = ReceivingStatus.Started,
                ReceivedByUserId = Guid.NewGuid()
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName);
        var handler = new ScanReceiptLineCommandHandler(context);

        var result = await handler.Handle(new ScanReceiptLineCommand
        {
            ReceivingSessionId = sessionId,
            ItemId = itemId,
            PurchaseOrderLineId = poLineId,
            QuantityReceived = 50m,
            LotNumber = "LOT-OIL-2026-01",
            ExpirationDate = DateTime.UtcNow.AddYears(1),
            Condition = ReceiptConditionStatus.Sound
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("LPN-", result.Lpn);
        Assert.Equal(50m, result.QuantityReceived);
        Assert.Equal(200m, result.QuantityExpected);
        Assert.Equal("SKU-PO-01", result.Sku);
        Assert.Equal("LOT-OIL-2026-01", result.LotNumber);
        Assert.Equal(ReceiptConditionStatus.Sound, result.Condition);

        var session = await context.ReceivingSessions.FindAsync(sessionId);
        Assert.Equal(ReceivingStatus.InProgress, session!.Status);
    }

    [Fact]
    public async Task ScanReceiptLine_SkuMismatchWithPoLine_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var poLineId = Guid.NewGuid();
        var itemAId = Guid.NewGuid();
        var itemBId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Items.Add(new Item { Id = itemAId, ClientId = clientId, Sku = "SKU-A", Description = "Item A" });
            seed.Items.Add(new Item { Id = itemBId, ClientId = clientId, Sku = "SKU-B", Description = "Item B" });

            var po = new PurchaseOrder { Id = poId, ClientId = clientId, FacilityId = facilityId, PoNumber = "PO-MISMATCH" };
            po.Lines.Add(new PurchaseOrderLine { Id = poLineId, PurchaseOrderId = poId, LineNumber = 1, ItemId = itemAId, ExpectedQty = 100 });
            seed.PurchaseOrders.Add(po);

            seed.ReceivingSessions.Add(new ReceivingSession { Id = sessionId, FacilityId = facilityId, ClientId = clientId, PurchaseOrderId = poId, SessionNumber = "RCV-MISMATCH", Status = ReceivingStatus.Started, ReceivedByUserId = Guid.NewGuid() });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName);
        var handler = new ScanReceiptLineCommandHandler(context);

        // Attempting to scan Item B against PO line for Item A
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemBId, // Mismatch
                PurchaseOrderLineId = poLineId,
                QuantityReceived = 10m
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task ScanReceiptLine_OverReceivingBeyondAllowableThreshold_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var poLineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-CAP", Description = "Capped Item" });

            var po = new PurchaseOrder { Id = poId, ClientId = clientId, FacilityId = facilityId, PoNumber = "PO-OVER" };
            po.Lines.Add(new PurchaseOrderLine { Id = poLineId, PurchaseOrderId = poId, LineNumber = 1, ItemId = itemId, ExpectedQty = 100m }); // Max 110 allowable
            seed.PurchaseOrders.Add(po);

            seed.ReceivingSessions.Add(new ReceivingSession { Id = sessionId, FacilityId = facilityId, ClientId = clientId, PurchaseOrderId = poId, SessionNumber = "RCV-OVER", Status = ReceivingStatus.Started, ReceivedByUserId = Guid.NewGuid() });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName);
        var handler = new ScanReceiptLineCommandHandler(context);

        // Attempting to receive 130 on a 100 expected line (exceeds 10% over-receiving limit)
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new ScanReceiptLineCommand
            {
                ReceivingSessionId = sessionId,
                ItemId = itemId,
                PurchaseOrderLineId = poLineId,
                QuantityReceived = 130m
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task ScanReceiptLine_OverReceivingWithinOverageWindow_LogsQuantityOverageDiscrepancy()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var poLineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-WINDOW", Description = "Window Item" });

            var po = new PurchaseOrder { Id = poId, ClientId = clientId, FacilityId = facilityId, PoNumber = "PO-WINDOW" };
            po.Lines.Add(new PurchaseOrderLine { Id = poLineId, PurchaseOrderId = poId, LineNumber = 1, ItemId = itemId, ExpectedQty = 100m });
            seed.PurchaseOrders.Add(po);

            seed.ReceivingSessions.Add(new ReceivingSession { Id = sessionId, FacilityId = facilityId, ClientId = clientId, PurchaseOrderId = poId, SessionNumber = "RCV-WINDOW", Status = ReceivingStatus.Started, ReceivedByUserId = Guid.NewGuid() });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName);
        var handler = new ScanReceiptLineCommandHandler(context);

        // 105 is within the 110 threshold, but 5 over expected -> logs overage discrepancy
        var result = await handler.Handle(new ScanReceiptLineCommand
        {
            ReceivingSessionId = sessionId,
            ItemId = itemId,
            PurchaseOrderLineId = poLineId,
            QuantityReceived = 105m
        }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(105m, result.QuantityReceived);

        var discrepancies = await context.ReceivingDiscrepancies.Where(d => d.ReceivingSessionId == sessionId).ToListAsync();
        Assert.Single(discrepancies);
        Assert.Equal(DiscrepancyType.QuantityOverage, discrepancies[0].Type);
        Assert.Equal(5m, discrepancies[0].DiscrepantQuantity);
        Assert.Equal(DiscrepancyResolutionStatus.PendingReview, discrepancies[0].Status);
    }

    [Fact]
    public async Task ScanReceiptLine_DamagedGoodsCondition_AutoCreatesQuarantinedDiscrepancy()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var poLineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-DAMAGE", Description = "Fragile Glassware" });

            var po = new PurchaseOrder { Id = poId, ClientId = clientId, FacilityId = facilityId, PoNumber = "PO-DMG" };
            po.Lines.Add(new PurchaseOrderLine { Id = poLineId, PurchaseOrderId = poId, LineNumber = 1, ItemId = itemId, ExpectedQty = 50m });
            seed.PurchaseOrders.Add(po);

            seed.ReceivingSessions.Add(new ReceivingSession
            {
                Id = sessionId,
                FacilityId = facilityId,
                ClientId = clientId,
                PurchaseOrderId = poId,
                SessionNumber = "RCV-DMG-01",
                Status = ReceivingStatus.Started,
                ReceivedByUserId = Guid.NewGuid()
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName);
        var handler = new ScanReceiptLineCommandHandler(context);

        var result = await handler.Handle(new ScanReceiptLineCommand
        {
            ReceivingSessionId = sessionId,
            ItemId = itemId,
            PurchaseOrderLineId = poLineId,
            QuantityReceived = 15m,
            Condition = ReceiptConditionStatus.Damaged,
            Notes = "Carton crushed during pallet transit"
        }, CancellationToken.None);

        Assert.Equal(ReceiptConditionStatus.Damaged, result.Condition);

        var discrepancies = await context.ReceivingDiscrepancies.Where(d => d.ReceivingSessionId == sessionId).ToListAsync();
        Assert.Single(discrepancies);
        Assert.Equal(DiscrepancyType.DamagedGoods, discrepancies[0].Type);
        Assert.Equal(15m, discrepancies[0].DiscrepantQuantity);
        Assert.Equal(DiscrepancyResolutionStatus.Quarantined, discrepancies[0].Status);
    }

    [Fact]
    public async Task ScanReceiptLine_BlindReceiving_UnexpectedSku_LogsUnexpectedSkuDiscrepancyAndFlagsSession()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-UNANNOUNCED", Description = "Unannounced Return" });
            seed.ReceivingSessions.Add(new ReceivingSession
            {
                Id = sessionId,
                FacilityId = facilityId,
                ClientId = clientId,
                SessionNumber = "RCV-BLIND-01",
                Status = ReceivingStatus.Started,
                IsBlindReceiving = true,
                RequiresSupervisorApproval = true,
                ReceivedByUserId = Guid.NewGuid()
            });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName);
        var handler = new ScanReceiptLineCommandHandler(context);

        var result = await handler.Handle(new ScanReceiptLineCommand
        {
            ReceivingSessionId = sessionId,
            ItemId = itemId,
            QuantityReceived = 40m,
            Notes = "Blind intake without pre-linked ASN"
        }, CancellationToken.None);

        Assert.NotNull(result);

        var session = await context.ReceivingSessions.Include(s => s.Discrepancies).FirstOrDefaultAsync(s => s.Id == sessionId);
        Assert.Equal(ReceivingStatus.Flagged, session!.Status);
        Assert.True(session.RequiresSupervisorApproval);

        var unexpectedDiscrepancy = session.Discrepancies.FirstOrDefault(d => d.Type == DiscrepancyType.UnexpectedSKU);
        Assert.NotNull(unexpectedDiscrepancy);
        Assert.Equal(40m, unexpectedDiscrepancy.DiscrepantQuantity);
    }

    [Fact]
    public async Task CompleteReceivingSession_RequiresSupervisorApproval_WhenUnapproved_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ReceivingSessions.Add(new ReceivingSession
            {
                Id = sessionId,
                FacilityId = facilityId,
                ClientId = clientId,
                SessionNumber = "RCV-UNAPPROVED",
                Status = ReceivingStatus.InProgress,
                RequiresSupervisorApproval = true,
                IsSupervisorApproved = false, // Not approved!
                ReceivedByUserId = Guid.NewGuid()
            });
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new CompleteReceivingSessionCommandHandler(context, currentUser);

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task CompleteReceivingSession_WithPendingDiscrepancies_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            var session = new ReceivingSession
            {
                Id = sessionId,
                FacilityId = facilityId,
                ClientId = clientId,
                SessionNumber = "RCV-PENDING-DISC",
                Status = ReceivingStatus.InProgress,
                RequiresSupervisorApproval = false,
                ReceivedByUserId = Guid.NewGuid()
            };
            session.Discrepancies.Add(new ReceivingDiscrepancy
            {
                ReceivingSessionId = sessionId,
                Type = DiscrepancyType.QuantityShortage,
                DiscrepantQuantity = 10m,
                Notes = "Carton missing from pallet",
                Status = DiscrepancyResolutionStatus.PendingReview // Unresolved!
            });
            seed.ReceivingSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new CompleteReceivingSessionCommandHandler(context, currentUser);

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task CompleteReceivingSession_ReconcilesPoAndAsnStatuses()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var poLineId = Guid.NewGuid();
        var asnId = Guid.NewGuid();
        var asnLineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-RECON-01", Description = "Reconciliation SKU" });

            var po = new PurchaseOrder { Id = poId, ClientId = clientId, FacilityId = facilityId, PoNumber = "PO-RECON", Status = PurchaseOrderStatus.Submitted };
            po.Lines.Add(new PurchaseOrderLine { Id = poLineId, PurchaseOrderId = poId, LineNumber = 1, ItemId = itemId, ExpectedQty = 100m });
            seed.PurchaseOrders.Add(po);

            var asn = new AdvanceShippingNotice { Id = asnId, ClientId = clientId, FacilityId = facilityId, PurchaseOrderId = poId, AsnNumber = "ASN-RECON", Status = AsnStatus.Receiving };
            asn.Lines.Add(new AsnLineItem { Id = asnLineId, AsnId = asnId, PurchaseOrderLineId = poLineId, ItemId = itemId, ExpectedQty = 100m });
            seed.AdvanceShippingNotices.Add(asn);

            var session = new ReceivingSession
            {
                Id = sessionId,
                FacilityId = facilityId,
                ClientId = clientId,
                PurchaseOrderId = poId,
                AsnId = asnId,
                SessionNumber = "RCV-RECON-01",
                Status = ReceivingStatus.InProgress,
                ReceivedByUserId = Guid.NewGuid()
            };
            session.Lines.Add(new ReceiptLine
            {
                ReceivingSessionId = sessionId,
                ItemId = itemId,
                PurchaseOrderLineId = poLineId,
                AsnLineItemId = asnLineId,
                Lpn = "LPN-RECON-01",
                QuantityReceived = 100m,
                QuantityExpected = 100m,
                Condition = ReceiptConditionStatus.Sound
            });
            seed.ReceivingSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new CompleteReceivingSessionCommandHandler(context, currentUser);

        var result = await handler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);

        Assert.Equal(ReceivingStatus.Completed, result.Status);
        Assert.NotNull(result.CompletedAtUtc);

        var updatedPo = await context.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == poId);
        Assert.Equal(PurchaseOrderStatus.Received, updatedPo!.Status);
        Assert.Equal(100m, updatedPo.Lines.First().ReceivedQty);
        Assert.Equal(PurchaseOrderLineStatus.Completed, updatedPo.Lines.First().Status);

        var updatedAsn = await context.AdvanceShippingNotices.Include(a => a.Lines).FirstOrDefaultAsync(a => a.Id == asnId);
        Assert.Equal(AsnStatus.Received, updatedAsn!.Status);
        Assert.Equal(100m, updatedAsn.Lines.First().ReceivedQty);
    }

    [Fact]
    public async Task CompleteReceivingSession_LedgerIntegration_IngestsStockAndTransactions()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var soundItemId = Guid.NewGuid();
        var damagedItemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-INBOUND", LocationType = LocationType.StagingLane, IsActive = true, IsReceiving = true });

            seed.Items.Add(new Item { Id = soundItemId, ClientId = clientId, Sku = "SKU-LEDGER-SOUND", Description = "Good Stock" });
            seed.Items.Add(new Item { Id = damagedItemId, ClientId = clientId, Sku = "SKU-LEDGER-DMG", Description = "Damaged Stock" });

            var session = new ReceivingSession
            {
                Id = sessionId,
                FacilityId = facilityId,
                ClientId = clientId,
                SessionNumber = "RCV-LEDGER-01",
                Status = ReceivingStatus.InProgress,
                ReceivedByUserId = Guid.NewGuid()
            };

            // 1. Sound line with lot number
            session.Lines.Add(new ReceiptLine
            {
                ReceivingSessionId = sessionId,
                ItemId = soundItemId,
                Lpn = "LPN-SOUND-01",
                QuantityReceived = 80m,
                QuantityExpected = 80m,
                LotNumber = "LOT-2026-SOUND",
                ExpirationDate = DateTime.UtcNow.AddMonths(12),
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });

            // 2. Damaged line
            session.Lines.Add(new ReceiptLine
            {
                ReceivingSessionId = sessionId,
                ItemId = damagedItemId,
                Lpn = "LPN-DMG-01",
                QuantityReceived = 20m,
                QuantityExpected = 20m,
                Condition = ReceiptConditionStatus.Damaged,
                StagingLocationId = stagingLocId
            });

            seed.ReceivingSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName);
        var handler = new CompleteReceivingSessionCommandHandler(context, currentUser);

        var result = await handler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);

        Assert.Equal(ReceivingStatus.Completed, result.Status);

        // Check StockOnHand for sound item: QtyAvailable = 80
        var soundStock = await context.StockOnHand.FirstOrDefaultAsync(s => s.ItemId == soundItemId);
        Assert.NotNull(soundStock);
        Assert.Equal(80m, soundStock.QtyAvailable);
        Assert.Equal(0m, soundStock.QtyDamaged);

        // Check StockOnHand for damaged item: QtyDamaged = 20
        var damagedStock = await context.StockOnHand.FirstOrDefaultAsync(s => s.ItemId == damagedItemId);
        Assert.NotNull(damagedStock);
        Assert.Equal(0m, damagedStock.QtyAvailable);
        Assert.Equal(20m, damagedStock.QtyDamaged);

        // Check InventoryLots created
        var lot = await context.InventoryLots.FirstOrDefaultAsync(l => l.LotNumber == "LOT-2026-SOUND");
        Assert.NotNull(lot);
        Assert.Equal(LotQaStatus.Released, lot.QaStatus);

        // Check Immutable Inventory Transactions written
        var transactions = await context.InventoryTransactions.Where(t => t.ReferenceId == sessionId).ToListAsync();
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, t => Assert.Equal(InventoryTxType.Receive, t.TxType));
    }

    [Fact]
    public async Task RecordInspectionDecision_ApproveQuarantinedDiscrepancy_ReleasesStockToAvailable()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var discrepancyId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", LocationType = LocationType.StagingLane, IsActive = true, IsReceiving = true });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-QC-RELEASE", Description = "Quarantined Goods" });

            var session = new ReceivingSession
            {
                Id = sessionId,
                FacilityId = facilityId,
                ClientId = clientId,
                SessionNumber = "RCV-QC-01",
                Status = ReceivingStatus.Flagged,
                RequiresSupervisorApproval = true,
                ReceivedByUserId = Guid.NewGuid()
            };

            var line = new ReceiptLine
            {
                Id = lineId,
                ReceivingSessionId = sessionId,
                ItemId = itemId,
                Lpn = "LPN-QUARANTINE-01",
                QuantityReceived = 30m,
                QuantityExpected = 30m,
                LotNumber = "LOT-QC-01",
                Condition = ReceiptConditionStatus.Defective,
                StagingLocationId = stagingLocId
            };
            session.Lines.Add(line);

            var discrepancy = new ReceivingDiscrepancy
            {
                Id = discrepancyId,
                ReceivingSessionId = sessionId,
                ReceiptLineId = lineId,
                Type = DiscrepancyType.DamagedGoods,
                DiscrepantQuantity = 30m,
                Notes = "Packaging torn, pending QC sample testing",
                Status = DiscrepancyResolutionStatus.Quarantined
            };
            session.Discrepancies.Add(discrepancy);

            // Existing StockOnHand in Quarantined status
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = stagingLocId,
                QtyAvailable = 0m,
                QtyQuarantined = 30m
            });

            seed.InventoryLots.Add(new InventoryLot
            {
                ClientId = clientId,
                ItemId = itemId,
                LotNumber = "LOT-QC-01",
                QaStatus = LotQaStatus.Quarantine
            });

            seed.ReceivingSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        var supervisorUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };
        using var context = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, supervisorUser, dbName);
        var handler = new RecordInspectionDecisionCommandHandler(context, supervisorUser);

        // Supervisor approves discrepancy after QC inspection
        var result = await handler.Handle(new RecordInspectionDecisionCommand
        {
            DiscrepancyId = discrepancyId,
            Decision = DiscrepancyResolutionStatus.Approved,
            ResolutionNotes = "Lab sample tests passed. Outer box replaced, goods verified sound for release."
        }, CancellationToken.None);

        Assert.Equal(DiscrepancyResolutionStatus.Approved, result.Status);
        Assert.Equal(30m, result.ReleasedQuantity);

        // Verify StockOnHand released: QtyQuarantined = 0, QtyAvailable = 30
        var updatedStock = await context.StockOnHand.FirstOrDefaultAsync(s => s.ItemId == itemId);
        Assert.Equal(0m, updatedStock!.QtyQuarantined);
        Assert.Equal(30m, updatedStock.QtyAvailable);

        // Verify Lot status updated to Released
        var updatedLot = await context.InventoryLots.FirstOrDefaultAsync(l => l.LotNumber == "LOT-QC-01");
        Assert.Equal(LotQaStatus.Released, updatedLot!.QaStatus);

        // Verify status change transaction recorded in immutable ledger
        var tx = await context.InventoryTransactions.FirstOrDefaultAsync(t => t.ReferenceId == discrepancyId);
        Assert.NotNull(tx);
        Assert.Equal(InventoryTxType.StatusChange, tx.TxType);
        Assert.Equal(30m, tx.Quantity);

        // Verify session supervisor approval granted
        var updatedSession = await context.ReceivingSessions.FindAsync(sessionId);
        Assert.True(updatedSession!.IsSupervisorApproved);
    }

    [Fact]
    public async Task PartialReceiving_MultipleSessions_MaintainsPoLineBalances()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var poLineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Retail" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", LocationType = LocationType.StagingLane, IsActive = true, IsReceiving = true });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-PARTIAL", Description = "Split Shipment Goods" });

            var po = new PurchaseOrder { Id = poId, ClientId = clientId, FacilityId = facilityId, PoNumber = "PO-SPLIT-01", Status = PurchaseOrderStatus.Submitted };
            po.Lines.Add(new PurchaseOrderLine { Id = poLineId, PurchaseOrderId = poId, LineNumber = 1, ItemId = itemId, ExpectedQty = 100m, ReceivedQty = 0m });
            seed.PurchaseOrders.Add(po);
            await seed.SaveChangesAsync();
        }

        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff };

        // Session 1: Partial delivery of 40 units
        var session1Id = Guid.NewGuid();
        using (var context1 = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName))
        {
            var session1 = new ReceivingSession
            {
                Id = session1Id,
                FacilityId = facilityId,
                ClientId = clientId,
                PurchaseOrderId = poId,
                SessionNumber = "RCV-PARTIAL-01",
                Status = ReceivingStatus.InProgress,
                ReceivedByUserId = currentUser.UserId!.Value
            };
            session1.Lines.Add(new ReceiptLine
            {
                ReceivingSessionId = session1Id,
                ItemId = itemId,
                PurchaseOrderLineId = poLineId,
                Lpn = "LPN-PART-1",
                QuantityReceived = 40m,
                QuantityExpected = 100m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            context1.ReceivingSessions.Add(session1);
            await context1.SaveChangesAsync();

            var completeHandler = new CompleteReceivingSessionCommandHandler(context1, currentUser);
            await completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = session1Id }, CancellationToken.None);

            var poAfterPart1 = await context1.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == poId);
            Assert.Equal(PurchaseOrderStatus.PartiallyReceived, poAfterPart1!.Status);
            Assert.Equal(40m, poAfterPart1.Lines.First().ReceivedQty);
            Assert.Equal(PurchaseOrderLineStatus.PartiallyReceived, poAfterPart1.Lines.First().Status);
        }

        // Session 2: Second delivery of remaining 60 units
        var session2Id = Guid.NewGuid();
        using (var context2 = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, currentUser, dbName))
        {
            var session2 = new ReceivingSession
            {
                Id = session2Id,
                FacilityId = facilityId,
                ClientId = clientId,
                PurchaseOrderId = poId,
                SessionNumber = "RCV-PARTIAL-02",
                Status = ReceivingStatus.InProgress,
                ReceivedByUserId = currentUser.UserId!.Value
            };
            session2.Lines.Add(new ReceiptLine
            {
                ReceivingSessionId = session2Id,
                ItemId = itemId,
                PurchaseOrderLineId = poLineId,
                Lpn = "LPN-PART-2",
                QuantityReceived = 60m,
                QuantityExpected = 60m,
                Condition = ReceiptConditionStatus.Sound,
                StagingLocationId = stagingLocId
            });
            context2.ReceivingSessions.Add(session2);
            await context2.SaveChangesAsync();

            var completeHandler = new CompleteReceivingSessionCommandHandler(context2, currentUser);
            await completeHandler.Handle(new CompleteReceivingSessionCommand { ReceivingSessionId = session2Id }, CancellationToken.None);

            var poAfterPart2 = await context2.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == poId);
            Assert.Equal(PurchaseOrderStatus.Received, poAfterPart2!.Status);
            Assert.Equal(100m, poAfterPart2.Lines.First().ReceivedQty);
            Assert.Equal(PurchaseOrderLineStatus.Completed, poAfterPart2.Lines.First().Status);

            var totalStock = await context2.StockOnHand.FirstOrDefaultAsync(s => s.ItemId == itemId);
            Assert.Equal(100m, totalStock!.QtyAvailable);
        }
    }
}
