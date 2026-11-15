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
using Warehouse3PL.Infrastructure.Services.Shipping;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Outbound;

public class ReturnReceivingAndInspectionTests
{
    private class TestEnvFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid ForeignClientId { get; set; } = Guid.NewGuid();
        public Guid OriginFacilityId { get; set; } = Guid.NewGuid();
        public Guid DestinationFacilityId { get; set; } = Guid.NewGuid();
        public Guid ForeignFacilityId { get; set; } = Guid.NewGuid();
        public Guid StaffUserId { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; } = Guid.NewGuid();
        public Guid SecondItemId { get; set; } = Guid.NewGuid();
        public Guid LocationId { get; set; } = Guid.NewGuid();
    }

    private static async Task<TestEnvFixture> SeedTestEnvironmentAsync()
    {
        var fix = new TestEnvFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var client = new Client
        {
            Id = fix.ClientId,
            CompanyName = "Apex Precision Components",
            AccountCode = $"APC{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Rival Distribution",
            AccountCode = $"RVD{Guid.NewGuid().ToString()[..4].ToUpperInvariant()}",
            Status = ClientStatus.Active
        };

        var originFacility = new Facility
        {
            Id = fix.OriginFacilityId,
            Name = "Louisville Central Hub",
            FacilityCode = "SDF1",
            AddressLine1 = "100 Logistics Blvd",
            City = "Louisville",
            StateOrProvince = "KY",
            PostalCode = "40202",
            CountryCode = "US"
        };

        var destinationFacility = new Facility
        {
            Id = fix.DestinationFacilityId,
            Name = "Lexington Returns Center",
            FacilityCode = "LEX1",
            AddressLine1 = "500 Reverse Logistics Pkwy",
            City = "Lexington",
            StateOrProvince = "KY",
            PostalCode = "40509",
            CountryCode = "US"
        };

        var foreignFacility = new Facility
        {
            Id = fix.ForeignFacilityId,
            Name = "Cincinnati Hub",
            FacilityCode = "CVG1",
            AddressLine1 = "200 Ohio River Rd",
            City = "Cincinnati",
            StateOrProvince = "OH",
            PostalCode = "45202",
            CountryCode = "US"
        };

        var staffUser = new User
        {
            Id = fix.StaffUserId,
            Email = "dock.operator@warehouse3pl.test",
            PasswordHash = "hash",
            FirstName = "Tyler",
            LastName = "Morgan",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-VALVE-01",
            Description = "Precision Control Valve"
        };

        var secondItem = new Item
        {
            Id = fix.SecondItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-SEAL-02",
            Description = "Polymer Flange Seal"
        };

        var location = new Location
        {
            Id = fix.LocationId,
            FacilityId = fix.DestinationFacilityId,
            LocationCode = "DOCK-RCV-01",
            Barcode = "LOC-DOCK-RCV-01",
            LocationType = LocationType.Bin,
            IsReceiving = true,
            IsActive = true
        };

        context.Clients.AddRange(client, foreignClient);
        context.Facilities.AddRange(originFacility, destinationFacility, foreignFacility);
        context.Users.Add(staffUser);
        context.Items.AddRange(item, secondItem);
        context.Locations.Add(location);

        await context.SaveChangesAsync();
        return fix;
    }

    private static async Task<(ReturnInboundHandoff Handoff, ReturnShipment Shipment, ReturnAuthorization Auth, OutboundOrder Order, Package Pkg)>
        SeedPendingHandoffGraphAsync(TestEnvFixture fix, decimal itemQuantity = 10m)
    {
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var order = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            OrderNumber = $"ORD-RET-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
            CustomerName = "National Heavy Industries",
            Status = OutboundOrderStatus.Completed,
            DeliveryStatus = OrderDeliveryStatus.DeliveryException
        };

        var orderLine = new OutboundOrderLine
        {
            Id = Guid.NewGuid(),
            OutboundOrderId = order.Id,
            ItemId = fix.ItemId,
            LineNumber = 1,
            QuantityOrdered = itemQuantity,
            QuantityAllocated = itemQuantity,
            QuantityShipped = itemQuantity,
            Status = OutboundOrderLineStatus.Allocated
        };

        var package = new Package
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            FacilityId = fix.OriginFacilityId,
            OutboundOrderId = order.Id,
            PackageNumber = $"PKG-RET-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
            TrackingNumber = $"TRK-RTS-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            Status = PackageStatus.Loaded
        };

        var pkgLine = new PackageLine
        {
            Id = Guid.NewGuid(),
            PackageId = package.Id,
            OutboundOrderLineId = orderLine.Id,
            ItemId = fix.ItemId,
            Quantity = itemQuantity,
            InventoryAllocationId = Guid.NewGuid()
        };

        var auth = ReturnAuthorization.Create(
            fix.ClientId,
            fix.OriginFacilityId,
            fix.DestinationFacilityId,
            order.Id,
            null,
            package.Id,
            null,
            $"RA-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
            ReturnReason.DamagedInTransit,
            "Return authorized due to transit damage.",
            fix.StaffUserId,
            $"RMA-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
            DateTime.UtcNow.AddDays(7));

        var shipment = new ReturnShipment
        {
            Id = Guid.NewGuid(),
            ClientId = fix.ClientId,
            ReturnAuthorizationId = auth.Id,
            OrderId = order.Id,
            PackageId = package.Id,
            OriginFacilityId = fix.OriginFacilityId,
            DestinationFacilityId = fix.DestinationFacilityId,
            CarrierCode = "FEDEX",
            TrackingNumber = package.TrackingNumber,
            AuthorizedByUserId = fix.StaffUserId,
            Status = ReturnShipmentStatus.DeliveredAtWarehouse,
            DeliveredAtWarehouseUtc = DateTime.UtcNow.AddHours(-2)
        };

        auth.LinkReturnShipment(shipment.Id);
        auth.RecordDeliveredAtFacility(DateTime.UtcNow.AddHours(-2));

        var handoff = ReturnInboundHandoff.Create(
            fix.ClientId,
            fix.DestinationFacilityId,
            shipment.Id,
            auth.Id,
            package.Id,
            order.Id,
            $"HND-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            auth.RmaNumber!,
            shipment.TrackingNumber,
            shipment.CarrierCode,
            DateTime.UtcNow.AddHours(-2),
            "DOCK-RCV-01",
            "Reverse carrier delivery at dock door.");

        shipment.InboundHandoffId = handoff.Id;

        context.OutboundOrders.Add(order);
        context.OutboundOrderLines.Add(orderLine);
        context.Packages.Add(package);
        context.PackageLines.Add(pkgLine);
        context.ReturnAuthorizations.Add(auth);
        context.ReturnShipments.Add(shipment);
        context.ReturnInboundHandoffs.Add(handoff);

        await context.SaveChangesAsync();
        return (handoff, shipment, auth, order, package);
    }

    private static ReturnReceivingService CreateService(
        TestEnvFixture fix,
        Guid? tenantOverride = null,
        bool bypassTenant = false,
        Guid? userFacilityOverride = null)
    {
        var tenantProvider = new TestTenantProvider
        {
            TenantId = tenantOverride ?? fix.ClientId,
            BypassTenantFilter = bypassTenant
        };

        var userContext = new TestCurrentUserContext
        {
            UserId = fix.StaffUserId,
            UserType = UserType.Staff,
            FacilityId = userFacilityOverride ?? fix.DestinationFacilityId
        };

        var context = TestDbContextFactory.Create(tenantProvider, userContext, fix.DbName);
        return new ReturnReceivingService(
            context,
            tenantProvider,
            userContext,
            NullLogger<ReturnReceivingService>.Instance);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 1: Valid pending handoff creates a receiving session
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test1_ValidPendingHandoff_CreatesReceivingSession_PopulatesExpectedLines_UpdatesHandoff()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, _, _) = await SeedPendingHandoffGraphAsync(fix, 10m);
        var service = CreateService(fix);

        var result = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId,
            DockLocation = "DOOR-04",
            Notes = "Initiating morning return shift."
        });

        Assert.NotNull(result);
        Assert.Equal(ReturnReceivingSessionStatus.Started, result.Status);
        Assert.Equal(fix.DestinationFacilityId, result.FacilityId);
        Assert.Equal(fix.ClientId, result.ClientId);
        Assert.StartsWith("RRCV-", result.SessionNumber);
        Assert.False(result.IsInventoryPosted);
        Assert.Single(result.Lines);
        Assert.Equal("SKU-VALVE-01", result.Lines[0].Sku);
        Assert.Equal(10m, result.Lines[0].ExpectedQuantity);
        Assert.Equal(0m, result.Lines[0].ReceivedQuantity);

        // Verify handoff updated to Processing
        using var verifyContext = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext(),
            fix.DbName);

        var handoffInDb = await verifyContext.ReturnInboundHandoffs.FindAsync(handoff.Id);
        Assert.NotNull(handoffInDb);
        Assert.Equal(ReturnInboundHandoffStatus.Processing, handoffInDb.Status);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 2: Unauthorized facility cannot receive a return
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test2_UnauthorizedFacility_CannotReceiveReturn_ThrowsUnauthorizedException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, _, _) = await SeedPendingHandoffGraphAsync(fix, 10m);
        var service = CreateService(fix, userFacilityOverride: fix.ForeignFacilityId);

        // Operator passes foreign facility ID mismatched with handoff.DestinationFacilityId
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
            {
                ReturnInboundHandoffId = handoff.Id,
                FacilityId = fix.ForeignFacilityId,
                OperatorUserId = fix.StaffUserId
            }));

        Assert.Contains("not authorized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 3: Partial physical receipt is supported
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test3_PartialPhysicalReceipt_IsSupported_LogsShortageDiscrepancy()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, _, pkg) = await SeedPendingHandoffGraphAsync(fix, 10m);
        var service = CreateService(fix);

        var session = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        // Confirm arrival
        await service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = handoff.TrackingNumber,
            PackageCondition = ReturnPackageCondition.DamagedBox
        });

        // Partial receipt: only 6 of 10 units received
        var verifyResult = await service.VerifyReceivedQuantitiesAsync(new VerifyReceivedQuantitiesRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Lines = [new VerifyReceivedLineItem { LineId = session.Lines[0].Id, ReceivedQuantity = 6m }]
        });

        Assert.Single(verifyResult.Lines);
        Assert.Equal(6m, verifyResult.Lines[0].ReceivedQuantity);
        Assert.Single(verifyResult.Discrepancies);
        Assert.Equal(ReturnDiscrepancyType.Shortage, verifyResult.Discrepancies[0].DiscrepancyType);
        Assert.Equal(4m, verifyResult.Discrepancies[0].DiscrepancyQuantity);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 4: Duplicate package scan does not duplicate receipt
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test4_DuplicatePackageScan_ThrowsInvalidDomainStateException_PreventsDuplicateReceipt()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, _, _) = await SeedPendingHandoffGraphAsync(fix, 10m);
        var service = CreateService(fix);

        var session = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        // First scan succeeds
        await service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = handoff.TrackingNumber,
            PackageCondition = ReturnPackageCondition.Intact
        });

        // Duplicate scan on same session throws
        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
            {
                SessionId = session.Id,
                OperatorUserId = fix.StaffUserId,
                ScannedBarcode = handoff.TrackingNumber,
                PackageCondition = ReturnPackageCondition.Intact
            }));

        Assert.Contains("Duplicate scan prevented", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Invalid barcode check throws ValidationException
        using var newSessionContext = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext(),
            fix.DbName);

        var (handoff2, _, _, _, _) = await SeedPendingHandoffGraphAsync(fix, 5m);
        var session2 = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff2.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        var barcodeEx = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
            {
                SessionId = session2.Id,
                OperatorUserId = fix.StaffUserId,
                ScannedBarcode = "WRONG-RANDOM-BARCODE-9999",
                PackageCondition = ReturnPackageCondition.Intact
            }));

        Assert.Contains("does not match", barcodeEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 5: Received quantity exceeding expected quantity routes through discrepancy handling
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test5_ReceivedQuantityExceedingExpected_RoutesThroughExcessDiscrepancyHandling()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, _, _) = await SeedPendingHandoffGraphAsync(fix, 5m);
        var service = CreateService(fix);

        var session = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        await service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = handoff.TrackingNumber
        });

        // Expected 5, received 8 + 2 unannounced seals
        var result = await service.VerifyReceivedQuantitiesAsync(new VerifyReceivedQuantitiesRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Lines = [new VerifyReceivedLineItem { LineId = session.Lines[0].Id, ReceivedQuantity = 8m }],
            UnannouncedItems = [new UnannouncedReturnItem { Sku = "SKU-SEAL-02", ReceivedQuantity = 2m, Notes = "Extra seal in box" }]
        });

        Assert.Equal(2, result.Discrepancies.Count);
        var excessDisc = result.Discrepancies.FirstOrDefault(d => d.DiscrepancyType == ReturnDiscrepancyType.Excess);
        Assert.NotNull(excessDisc);
        Assert.Equal(3m, excessDisc.DiscrepancyQuantity);

        var unannouncedDisc = result.Discrepancies.FirstOrDefault(d => d.DiscrepancyType == ReturnDiscrepancyType.UnannouncedReturn);
        Assert.NotNull(unannouncedDisc);
        Assert.Equal(2m, unannouncedDisc.ReceivedQuantity);

        // Lines collection should now contain both the original line and unannounced item line
        Assert.Equal(2, result.Lines.Count);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 6: Damaged stock is not added to available inventory
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test6_DamagedStock_NotAddedToAvailableInventory_GoesToDamagedStock()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, _, _) = await SeedPendingHandoffGraphAsync(fix, 10m);
        var service = CreateService(fix);

        var session = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        await service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = handoff.TrackingNumber,
            PackageCondition = ReturnPackageCondition.Crushed
        });

        await service.VerifyReceivedQuantitiesAsync(new VerifyReceivedQuantitiesRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Lines = [new VerifyReceivedLineItem { LineId = session.Lines[0].Id, ReceivedQuantity = 10m }]
        });

        // 10 units received: 3 accepted (RestockToAvailable), 7 damaged (QuarantineDamaged)
        await service.InspectAndAssignDispositionAsync(new InspectAndAssignDispositionRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Dispositions = [new InspectLineDispositionItem
            {
                LineId = session.Lines[0].Id,
                AcceptedQuantity = 3m,
                DamagedQuantity = 7m,
                QuarantinedQuantity = 0m,
                Disposition = ReturnDisposition.QuarantineDamaged,
                TargetLocationId = fix.LocationId,
                InspectionNotes = "7 units crushed packaging; 3 units intact valves."
            }]
        });

        var postResult = await service.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            DefaultLocationId = fix.LocationId
        });

        Assert.True(postResult.IsSuccess);
        Assert.Equal(3m, postResult.TotalAcceptedQuantity);
        Assert.Equal(7m, postResult.TotalDamagedQuantity);

        // Verify warehouse stock ledger balances
        using var verifyContext = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext(),
            fix.DbName);

        var stock = await verifyContext.StockOnHand
            .FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.FacilityId == fix.DestinationFacilityId && s.ItemId == fix.ItemId);

        Assert.NotNull(stock);
        // CRITICAL INVARIANT: QtyAvailable must be exactly 3, NOT 10!
        Assert.Equal(3m, stock.QtyAvailable);
        Assert.Equal(7m, stock.QtyDamaged);
        Assert.Equal(10m, stock.TotalQuantity);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 7: Accepted stock is posted through existing InventoryTransaction and StockOnHand
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test7_AcceptedStock_PostedThroughInventoryTransactionAndStockOnHand()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, _, _) = await SeedPendingHandoffGraphAsync(fix, 15m);
        var service = CreateService(fix);

        var session = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        await service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = handoff.TrackingNumber
        });

        await service.VerifyReceivedQuantitiesAsync(new VerifyReceivedQuantitiesRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Lines = [new VerifyReceivedLineItem { LineId = session.Lines[0].Id, ReceivedQuantity = 15m }]
        });

        await service.InspectAndAssignDispositionAsync(new InspectAndAssignDispositionRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Dispositions = [new InspectLineDispositionItem
            {
                LineId = session.Lines[0].Id,
                AcceptedQuantity = 15m,
                DamagedQuantity = 0m,
                QuarantinedQuantity = 0m,
                Disposition = ReturnDisposition.RestockToAvailable,
                TargetLocationId = fix.LocationId
            }]
        });

        var postResult = await service.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId
        });

        Assert.True(postResult.IsSuccess);
        Assert.Single(postResult.InventoryTransactionIds);

        // Verify ledger transactions
        using var verifyContext = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext(),
            fix.DbName);

        var tx = await verifyContext.InventoryTransactions.FindAsync(postResult.InventoryTransactionIds[0]);
        Assert.NotNull(tx);
        Assert.Equal(InventoryTxType.Receive, tx.TxType);
        Assert.Equal(15m, tx.Quantity);
        Assert.Equal("ReturnReceivingSession", tx.ReferenceType);
        Assert.Equal(session.Id, tx.ReferenceId);
        Assert.Equal(fix.DestinationFacilityId, tx.FacilityId);
        Assert.Equal(fix.ClientId, tx.ClientId);

        var stock = await verifyContext.StockOnHand
            .FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.FacilityId == fix.DestinationFacilityId && s.ItemId == fix.ItemId);

        Assert.NotNull(stock);
        Assert.Equal(15m, stock.QtyAvailable);
        Assert.Equal(0m, stock.QtyDamaged);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 8: Concurrent receiving confirmations cannot double-post quantities
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test8_ConcurrentInventoryPosting_CannotDoublePostQuantities_ThrowsInvalidDomainStateException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, _, _) = await SeedPendingHandoffGraphAsync(fix, 8m);
        var service = CreateService(fix);

        var session = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        await service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = handoff.TrackingNumber
        });

        await service.VerifyReceivedQuantitiesAsync(new VerifyReceivedQuantitiesRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Lines = [new VerifyReceivedLineItem { LineId = session.Lines[0].Id, ReceivedQuantity = 8m }]
        });

        await service.InspectAndAssignDispositionAsync(new InspectAndAssignDispositionRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Dispositions = [new InspectLineDispositionItem
            {
                LineId = session.Lines[0].Id,
                AcceptedQuantity = 8m,
                DamagedQuantity = 0m,
                QuarantinedQuantity = 0m,
                Disposition = ReturnDisposition.RestockToAvailable
            }]
        });

        // First post succeeds
        var firstPost = await service.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId
        });
        Assert.True(firstPost.IsSuccess);

        // Second post attempt throws and prevents double posting
        var ex = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            service.PostReturnInventoryAsync(new PostReturnInventoryRequest
            {
                SessionId = session.Id,
                OperatorUserId = fix.StaffUserId
            }));

        Assert.Contains("Double posting prevented", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify stock total did NOT double
        using var verifyContext = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext(),
            fix.DbName);

        var stock = await verifyContext.StockOnHand
            .FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.FacilityId == fix.DestinationFacilityId && s.ItemId == fix.ItemId);

        Assert.NotNull(stock);
        Assert.Equal(8m, stock.QtyAvailable);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 9: A return handoff cannot be completed twice / duplicate sessions prevented
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test9_ReturnHandoffCannotBeCompletedTwice_DuplicateSessionsPrevented()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, shipment, auth, order, _) = await SeedPendingHandoffGraphAsync(fix, 10m);
        var service = CreateService(fix);

        // Attempting to initiate two concurrent sessions for the same handoff is blocked
        var session1 = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        var dupEx = await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
            service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
            {
                ReturnInboundHandoffId = handoff.Id,
                FacilityId = fix.DestinationFacilityId,
                OperatorUserId = fix.StaffUserId
            }));

        Assert.Contains("already active for this inbound handoff", dupEx.Message, StringComparison.OrdinalIgnoreCase);

        // Complete the session
        await service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session1.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = handoff.TrackingNumber
        });

        await service.VerifyReceivedQuantitiesAsync(new VerifyReceivedQuantitiesRequest
        {
            SessionId = session1.Id,
            OperatorUserId = fix.StaffUserId,
            Lines = [new VerifyReceivedLineItem { LineId = session1.Lines[0].Id, ReceivedQuantity = 10m }]
        });

        await service.InspectAndAssignDispositionAsync(new InspectAndAssignDispositionRequest
        {
            SessionId = session1.Id,
            OperatorUserId = fix.StaffUserId,
            Dispositions = [new InspectLineDispositionItem
            {
                LineId = session1.Lines[0].Id,
                AcceptedQuantity = 10m,
                DamagedQuantity = 0m,
                QuarantinedQuantity = 0m,
                Disposition = ReturnDisposition.RestockToAvailable
            }]
        });

        await service.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session1.Id,
            OperatorUserId = fix.StaffUserId
        });

        var compResult = await service.CompleteReturnReceivingAsync(new CompleteReturnReceivingRequest
        {
            SessionId = session1.Id,
            OperatorUserId = fix.StaffUserId,
            Notes = "Dock receiving finalized."
        });

        Assert.True(compResult.IsSuccess);
        Assert.Equal(ReturnReceivingSessionStatus.Completed, compResult.SessionStatus);
        Assert.Equal(ReturnInboundHandoffStatus.Received, compResult.HandoffStatus);
        Assert.Equal(ReturnShipmentStatus.ReceivedAndInspected, compResult.ReturnShipmentStatus);
        Assert.Equal(ReturnAuthorizationStatus.Completed, compResult.ReturnAuthorizationStatus);

        // Subsequent completion call is idempotent
        var compRepeat = await service.CompleteReturnReceivingAsync(new CompleteReturnReceivingRequest
        {
            SessionId = session1.Id,
            OperatorUserId = fix.StaffUserId
        });

        Assert.True(compRepeat.IsSuccess);
        Assert.Contains("already been completed", compRepeat.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 10: Original outbound fulfillment remains Completed (Delta FulfillmentStatus = 0)
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test10_OriginalOutboundFulfillment_RemainsCompleted_InvariantPreserved()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, order, _) = await SeedPendingHandoffGraphAsync(fix, 12m);
        var service = CreateService(fix);

        var session = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        await service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = handoff.TrackingNumber
        });

        await service.VerifyReceivedQuantitiesAsync(new VerifyReceivedQuantitiesRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Lines = [new VerifyReceivedLineItem { LineId = session.Lines[0].Id, ReceivedQuantity = 12m }]
        });

        await service.InspectAndAssignDispositionAsync(new InspectAndAssignDispositionRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Dispositions = [new InspectLineDispositionItem
            {
                LineId = session.Lines[0].Id,
                AcceptedQuantity = 12m,
                DamagedQuantity = 0m,
                QuarantinedQuantity = 0m,
                Disposition = ReturnDisposition.RestockToAvailable
            }]
        });

        await service.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId
        });

        var compResult = await service.CompleteReturnReceivingAsync(new CompleteReturnReceivingRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId
        });

        Assert.Equal(OutboundOrderStatus.Completed, compResult.OutboundOrderStatus);

        // Invariant check on database entity
        using var verifyContext = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext(),
            fix.DbName);

        var orderInDb = await verifyContext.OutboundOrders.FindAsync(order.Id);
        Assert.NotNull(orderInDb);
        Assert.Equal(OutboundOrderStatus.Completed, orderInDb.Status);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 11: Cross-tenant receiving is rejected with UnauthorizedException
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test11_CrossTenantReceiving_IsRejectedWithUnauthorizedException()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, _, _) = await SeedPendingHandoffGraphAsync(fix, 10m);

        // Context running under ForeignClientId
        var crossTenantService = CreateService(fix, tenantOverride: fix.ForeignClientId, bypassTenant: false);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            crossTenantService.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
            {
                ReturnInboundHandoffId = handoff.Id,
                FacilityId = fix.DestinationFacilityId,
                OperatorUserId = fix.StaffUserId
            }));

        Assert.Contains("cross-tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------------------
    // TEST 12: Inventory conservation holds throughout receiving transaction
    // ---------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Test12_InventoryConservationHolds_AcceptedPlusDamagedPlusQuarantined_MustEqualReceived()
    {
        var fix = await SeedTestEnvironmentAsync();
        var (handoff, _, _, _, _) = await SeedPendingHandoffGraphAsync(fix, 20m);
        var service = CreateService(fix);

        var session = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoff.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        await service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = handoff.TrackingNumber
        });

        await service.VerifyReceivedQuantitiesAsync(new VerifyReceivedQuantitiesRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Lines = [new VerifyReceivedLineItem { LineId = session.Lines[0].Id, ReceivedQuantity = 20m }]
        });

        // Violation: sum = 12 + 5 + 1 = 18 != 20
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.InspectAndAssignDispositionAsync(new InspectAndAssignDispositionRequest
            {
                SessionId = session.Id,
                OperatorUserId = fix.StaffUserId,
                Dispositions = [new InspectLineDispositionItem
                {
                    LineId = session.Lines[0].Id,
                    AcceptedQuantity = 12m,
                    DamagedQuantity = 5m,
                    QuarantinedQuantity = 1m, // Missing 2 units!
                    Disposition = ReturnDisposition.RestockToAvailable
                }]
            }));

        Assert.Contains("must strictly equal received quantity", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Correct conservation: 12 + 5 + 3 = 20
        var inspectedSession = await service.InspectAndAssignDispositionAsync(new InspectAndAssignDispositionRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Dispositions = [new InspectLineDispositionItem
            {
                LineId = session.Lines[0].Id,
                AcceptedQuantity = 12m,
                DamagedQuantity = 5m,
                QuarantinedQuantity = 3m,
                Disposition = ReturnDisposition.RestockToAvailable
            }]
        });

        Assert.Equal(ReturnReceivingSessionStatus.Inspected, inspectedSession.Status);

        var postResult = await service.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId
        });

        Assert.Equal(12m, postResult.TotalAcceptedQuantity);
        Assert.Equal(5m, postResult.TotalDamagedQuantity);
        Assert.Equal(3m, postResult.TotalQuarantinedQuantity);
        Assert.Equal(20m, postResult.TotalAcceptedQuantity + postResult.TotalDamagedQuantity + postResult.TotalQuarantinedQuantity);

        // Invariant check on StockOnHand total
        using var verifyContext = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext(),
            fix.DbName);

        var stock = await verifyContext.StockOnHand
            .FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.FacilityId == fix.DestinationFacilityId && s.ItemId == fix.ItemId);

        Assert.NotNull(stock);
        Assert.Equal(12m, stock.QtyAvailable);
        Assert.Equal(5m, stock.QtyDamaged);
        Assert.Equal(3m, stock.QtyQuarantined);
        Assert.Equal(20m, stock.TotalQuantity);
    }
}
