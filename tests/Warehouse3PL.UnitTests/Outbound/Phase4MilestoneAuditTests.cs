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

/// <summary>
/// Phase 4 Comprehensive Milestone Audit & End-to-End Integration Verification Suite.
/// Proves the complete closed-loop lifecycle from outbound order fulfillment through delivery exception,
/// Return-to-Sender (RTS) reverse carrier transit, dock receiving, quality grading, and inventory ledger reconciliation.
/// Verifies that total inventory is strictly conserved and that outbound fulfillment status invariance holds.
/// </summary>
public class Phase4MilestoneAuditTests
{
    private class AuditFixture
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public Guid ClientId { get; set; } = Guid.NewGuid();
        public Guid ForeignClientId { get; set; } = Guid.NewGuid();
        public Guid OriginFacilityId { get; set; } = Guid.NewGuid();
        public Guid DestinationFacilityId { get; set; } = Guid.NewGuid();
        public Guid StaffUserId { get; set; } = Guid.NewGuid();
        public Guid ItemId { get; set; } = Guid.NewGuid();
        public Guid OriginLocationId { get; set; } = Guid.NewGuid();
        public Guid DestinationLocationId { get; set; } = Guid.NewGuid();
    }

    private static async Task<AuditFixture> SeedAuditEnvironmentAsync()
    {
        var fix = new AuditFixture();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName);

        var client = new Client
        {
            Id = fix.ClientId,
            CompanyName = "OmniTech Dynamics 3PL",
            AccountCode = "OMNI",
            Status = ClientStatus.Active
        };

        var foreignClient = new Client
        {
            Id = fix.ForeignClientId,
            CompanyName = "Rival Corp",
            AccountCode = "RIVL",
            Status = ClientStatus.Active
        };

        var originFacility = new Facility
        {
            Id = fix.OriginFacilityId,
            Name = "Chicago Outbound Center",
            FacilityCode = "ORD-01",
            AddressLine1 = "100 Express Blvd",
            City = "Chicago",
            StateOrProvince = "IL",
            PostalCode = "60601",
            CountryCode = "US"
        };

        var destFacility = new Facility
        {
            Id = fix.DestinationFacilityId,
            Name = "Indianapolis Returns Depot",
            FacilityCode = "IND-02",
            AddressLine1 = "500 Reverse Logistics Dr",
            City = "Indianapolis",
            StateOrProvince = "IN",
            PostalCode = "46201",
            CountryCode = "US"
        };

        var staff = new User
        {
            Id = fix.StaffUserId,
            Email = "auditor@warehouse3pl.test",
            PasswordHash = "hashed",
            FirstName = "Alex",
            LastName = "Chen",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        };

        var item = new Item
        {
            Id = fix.ItemId,
            ClientId = fix.ClientId,
            Sku = "SKU-CIRCUIT-99",
            Description = "Optoelectronic Microcontroller",
            WeightKg = 0.5m,
            LengthCm = 15m,
            WidthCm = 10m,
            HeightCm = 5m
        };

        var originLoc = new Location
        {
            Id = fix.OriginLocationId,
            FacilityId = fix.OriginFacilityId,
            LocationCode = "BIN-OUT-01",
            Barcode = "LOC-BIN-OUT-01",
            LocationType = LocationType.Bin,
            IsPickable = true,
            IsActive = true
        };

        var destLoc = new Location
        {
            Id = fix.DestinationLocationId,
            FacilityId = fix.DestinationFacilityId,
            LocationCode = "DOCK-RCV-01",
            Barcode = "LOC-DOCK-RCV-01",
            LocationType = LocationType.Bin,
            IsReceiving = true,
            IsActive = true
        };

        context.Clients.AddRange(client, foreignClient);
        context.Facilities.AddRange(originFacility, destFacility);
        context.Users.Add(staff);
        context.Items.Add(item);
        context.Locations.AddRange(originLoc, destLoc);

        await context.SaveChangesAsync();
        return fix;
    }

    [Fact]
    public async Task EndToEndReverseLogisticsAudit_FromExceptionToRestockedInventory_ReconcilesWithoutDiscrepancy()
    {
        // ---------------------------------------------------------------------------------------------
        // 1. SETUP BASELINE ENVIRONMENT & DISPATCHED OUTBOUND ORDER
        // ---------------------------------------------------------------------------------------------
        var fix = await SeedAuditEnvironmentAsync();
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();
        const decimal orderQuantity = 20m;

        using (var setupContext = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName))
        {
            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = fix.ClientId,
                FacilityId = fix.OriginFacilityId,
                OrderNumber = "ORD-AUDIT-2026-001",
                CustomerName = "Apex Robotics",
                Status = OutboundOrderStatus.Completed,
                DeliveryStatus = OrderDeliveryStatus.InTransit
            };

            var orderLine = new OutboundOrderLine
            {
                Id = orderLineId,
                OutboundOrderId = orderId,
                ItemId = fix.ItemId,
                LineNumber = 1,
                QuantityOrdered = orderQuantity,
                QuantityAllocated = orderQuantity,
                QuantityShipped = orderQuantity,
                Status = OutboundOrderLineStatus.Allocated
            };

            var shipment = new Shipment
            {
                Id = shipmentId,
                ClientId = fix.ClientId,
                FacilityId = fix.OriginFacilityId,
                ShipmentNumber = "SHP-AUDIT-001",
                CarrierCode = "SIM-FEDEX",
                CarrierServiceLevel = CarrierServiceLevel.StandardGround,
                Status = ShipmentStatus.Dispatched,
                DeliveryStatus = TrackingMilestoneStatus.InTransit,
                DispatchedAtUtc = DateTime.UtcNow.AddDays(-2)
            };

            var package = new Package
            {
                Id = packageId,
                ClientId = fix.ClientId,
                FacilityId = fix.OriginFacilityId,
                OutboundOrderId = orderId,
                ShipmentId = shipmentId,
                PackageNumber = "PKG-AUDIT-001",
                TrackingNumber = "TRK-AUDIT-OUT-001",
                CarrierCode = "SIM-FEDEX",
                Status = PackageStatus.Loaded,
                CurrentTrackingStatus = TrackingMilestoneStatus.InTransit
            };

            var pkgLine = new PackageLine
            {
                Id = Guid.NewGuid(),
                PackageId = packageId,
                OutboundOrderLineId = orderLineId,
                ItemId = fix.ItemId,
                Quantity = orderQuantity,
                InventoryAllocationId = Guid.NewGuid()
            };

            setupContext.OutboundOrders.Add(order);
            setupContext.OutboundOrderLines.Add(orderLine);
            setupContext.Shipments.Add(shipment);
            setupContext.Packages.Add(package);
            setupContext.PackageLines.Add(pkgLine);
            await setupContext.SaveChangesAsync();
        }

        // ---------------------------------------------------------------------------------------------
        // 2. DELIVERY EXCEPTION INGESTION (Customer Refused Delivery on Site)
        // ---------------------------------------------------------------------------------------------
        var exceptionId = Guid.NewGuid();
        using (var excContext = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName))
        {
            var exception = new ShipmentDeliveryException
            {
                Id = exceptionId,
                ClientId = fix.ClientId,
                FacilityId = fix.OriginFacilityId,
                PackageId = packageId,
                ShipmentId = shipmentId,
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = "TRK-AUDIT-OUT-001",
                CarrierExceptionCode = "CUSTOMER_REFUSED",
                ExceptionType = DeliveryExceptionType.CustomerRefused,
                Severity = DeliveryExceptionSeverity.High,
                CarrierDescription = "Consignee refused to accept parcel at delivery address.",
                OccurredAtUtc = DateTime.UtcNow.AddHours(-24),
                Status = DeliveryExceptionStatus.PendingReturnAuthorization
            };
            excContext.ShipmentDeliveryExceptions.Add(exception);
            await excContext.SaveChangesAsync();
        }

        // ---------------------------------------------------------------------------------------------
        // 3. RETURN AUTHORIZATION CQRS (Step 3B.4.1 & 3B.4.2)
        // ---------------------------------------------------------------------------------------------
        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = false };
        var userContext = new TestCurrentUserContext { UserId = fix.StaffUserId, UserType = UserType.Staff, FacilityId = fix.OriginFacilityId };
        var carrierService = new SimulatedCarrierService(NullLogger<SimulatedCarrierService>.Instance);

        var authService = new ReturnAuthorizationService(
            TestDbContextFactory.Create(tenantProvider, userContext, fix.DbName),
            tenantProvider,
            userContext,
            carrierService,
            NullLogger<ReturnAuthorizationService>.Instance);

        var authResult = await authService.AuthorizeReturnToSenderAsync(new AuthorizeReturnToSenderRequest
        {
            DeliveryExceptionId = exceptionId,
            DestinationFacilityId = fix.DestinationFacilityId,
            Reason = ReturnReason.CustomerRefused,
            AuthorizationNotes = "Return-to-sender authorized for damaged/refused delivery.",
            AuthorizedByUserId = fix.StaffUserId,
            RmaNumber = "RMA-AUDIT-2026",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(14)
        });

        Assert.True(authResult.IsSuccess);
        Assert.Equal(ReturnAuthorizationStatus.Authorized, authResult.Status);

        // ---------------------------------------------------------------------------------------------
        // 4. RETURN SHIPMENT CREATION & CARRIER BOOKING (Step 3B.4.3)
        // ---------------------------------------------------------------------------------------------
        var shipmentService = new ReturnShipmentService(
            TestDbContextFactory.Create(tenantProvider, userContext, fix.DbName),
            tenantProvider,
            userContext,
            carrierService,
            NullLogger<ReturnShipmentService>.Instance);

        var returnShipmentResult = await shipmentService.CreateReturnShipmentAsync(new CreateReturnShipmentRequest
        {
            AuthorizationId = authResult.AuthorizationId,
            CarrierCode = "SIM-FEDEX",
            ServiceLevel = CarrierServiceLevel.StandardGround,
            UserId = fix.StaffUserId
        });

        Assert.True(returnShipmentResult.IsSuccess);
        Assert.Equal(ReturnShipmentStatus.Authorized, returnShipmentResult.Status);
        Assert.StartsWith("SIM-FEDEX-", returnShipmentResult.TrackingNumber);

        // ---------------------------------------------------------------------------------------------
        // 5. REVERSE CARRIER TRACKING MILESTONES & DOCK HANDOFF CREATION (Step 3B.4.4)
        // ---------------------------------------------------------------------------------------------
        var trackingService = new ReturnTrackingService(
            TestDbContextFactory.Create(tenantProvider, userContext, fix.DbName),
            tenantProvider,
            userContext,
            NullLogger<ReturnTrackingService>.Instance);

        // Milestone 5a: Reverse Carrier Handover
        var handoverResult = await trackingService.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = returnShipmentResult.TrackingNumber,
            CarrierEventCode = "PICKUP",
            CarrierDescription = "Package picked up by reverse carrier from consignee.",
            OccurredAtUtc = DateTime.UtcNow.AddHours(-18)
        });
        Assert.Equal(ReturnShipmentStatus.InTransitToWarehouse, handoverResult.ShipmentStatus);

        // Milestone 5b: Delivered at Receiving Facility Dock (Triggers ReturnInboundHandoff)
        var deliveryResult = await trackingService.IngestReturnTrackingEventAsync(new IngestReturnTrackingRequest
        {
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = returnShipmentResult.TrackingNumber,
            CarrierEventCode = "DELIVERED",
            CarrierDescription = "Delivered to warehouse returns dock door 02.",
            OccurredAtUtc = DateTime.UtcNow.AddHours(-2),
            DestinationFacilityCode = "IND-02",
            DockDoorLocation = "DOOR-02"
        });

        Assert.Equal(ReturnShipmentStatus.DeliveredAtWarehouse, deliveryResult.ShipmentStatus);
        Assert.NotNull(deliveryResult.InboundHandoffId);
        Assert.NotNull(deliveryResult.InboundHandoffReference);

        // ---------------------------------------------------------------------------------------------
        // 6. RETURN FACILITY RECEIVING & INSPECTION INTEGRATION (Step 3B.4.5)
        // ---------------------------------------------------------------------------------------------
        var receivingUserContext = new TestCurrentUserContext
        {
            UserId = fix.StaffUserId,
            UserType = UserType.Staff,
            FacilityId = fix.DestinationFacilityId
        };

        var receivingService = new ReturnReceivingService(
            TestDbContextFactory.Create(tenantProvider, receivingUserContext, fix.DbName),
            tenantProvider,
            receivingUserContext,
            NullLogger<ReturnReceivingService>.Instance);

        // Step 6a: Initiate Receiving Session
        var session = await receivingService.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = deliveryResult.InboundHandoffId!.Value,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId,
            DockLocation = "DOOR-02",
            Notes = "Initiating returns shift verification."
        });

        Assert.Equal(ReturnReceivingSessionStatus.Started, session.Status);
        Assert.Single(session.Lines);
        Assert.Equal(orderQuantity, session.Lines[0].ExpectedQuantity);

        // Step 6b: Physical Arrival Confirmation (Scan Barcode & Inspect Outer Carton)
        var arrivalConfirmed = await receivingService.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = returnShipmentResult.TrackingNumber,
            PackageCondition = ReturnPackageCondition.DamagedBox,
            ConditionNotes = "Outer box crushed at lower left corner."
        });
        Assert.Equal(ReturnReceivingSessionStatus.PhysicalReceiptConfirmed, arrivalConfirmed.Status);

        // Step 6c: Quantity Verification (All 20 units accounted for)
        var verified = await receivingService.VerifyReceivedQuantitiesAsync(new VerifyReceivedQuantitiesRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Lines = [new VerifyReceivedLineItem { LineId = session.Lines[0].Id, ReceivedQuantity = 20m }]
        });
        Assert.Equal(20m, verified.Lines[0].ReceivedQuantity);
        Assert.Empty(verified.Discrepancies); // Perfect quantity match, 0 shortages or excess

        // Step 6d: Quality Grading & Disposition Assignment (15 Accepted, 3 Damaged, 2 Quarantined)
        var inspected = await receivingService.InspectAndAssignDispositionAsync(new InspectAndAssignDispositionRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Dispositions = [new InspectLineDispositionItem
            {
                LineId = session.Lines[0].Id,
                AcceptedQuantity = 15m,
                DamagedQuantity = 3m,
                QuarantinedQuantity = 2m,
                Disposition = ReturnDisposition.RestockToAvailable,
                TargetLocationId = fix.DestinationLocationId,
                InspectionNotes = "15 pristine units passed QA; 3 units cracked casing; 2 units sent to QA testing."
            }]
        });
        Assert.Equal(ReturnReceivingSessionStatus.Inspected, inspected.Status);

        // Step 6e: Post Inventory to StockOnHand and Append-Only InventoryTransaction Ledger
        var postResult = await receivingService.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            DefaultLocationId = fix.DestinationLocationId
        });
        Assert.True(postResult.IsSuccess);
        Assert.Equal(15m, postResult.TotalAcceptedQuantity);
        Assert.Equal(3m, postResult.TotalDamagedQuantity);
        Assert.Equal(2m, postResult.TotalQuarantinedQuantity);

        // Step 6f: Complete Return Receiving Session & Finalize Reverse Logistics Aggregates
        var compResult = await receivingService.CompleteReturnReceivingAsync(new CompleteReturnReceivingRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Notes = "Audit workflow closed successfully."
        });
        Assert.True(compResult.IsSuccess);
        Assert.Equal(ReturnReceivingSessionStatus.Completed, compResult.SessionStatus);
        Assert.Equal(ReturnInboundHandoffStatus.Received, compResult.HandoffStatus);
        Assert.Equal(ReturnShipmentStatus.ReceivedAndInspected, compResult.ReturnShipmentStatus);
        Assert.Equal(ReturnAuthorizationStatus.Completed, compResult.ReturnAuthorizationStatus);
        Assert.Equal(OutboundOrderStatus.Completed, compResult.OutboundOrderStatus);

        // ---------------------------------------------------------------------------------------------
        // 7. COMPREHENSIVE END-TO-END INVENTORY & AGGREGATE RECONCILIATION PROOF
        // ---------------------------------------------------------------------------------------------
        using (var verifyContext = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext(),
            fix.DbName))
        {
            // Verify StockOnHand state partitioning at destination facility
            var stock = await verifyContext.StockOnHand
                .FirstOrDefaultAsync(s => s.ClientId == fix.ClientId &&
                                          s.FacilityId == fix.DestinationFacilityId &&
                                          s.ItemId == fix.ItemId);

            Assert.NotNull(stock);
            // CRITICAL INVARIANT 1: Only Accepted stock increases QtyAvailable
            Assert.Equal(15m, stock.QtyAvailable);
            Assert.Equal(3m, stock.QtyDamaged);
            Assert.Equal(2m, stock.QtyQuarantined);
            // Total inventory strictly conserved: 15 + 3 + 2 = 20
            Assert.Equal(20m, stock.TotalQuantity);

            // CRITICAL INVARIANT 2: Immutable InventoryTransaction ledger audit trail
            var txs = await verifyContext.InventoryTransactions
                .Where(t => t.ReferenceId == session.Id)
                .ToListAsync();

            Assert.Single(txs);
            Assert.Equal(InventoryTxType.Receive, txs[0].TxType);
            Assert.Equal(20m, txs[0].Quantity);
            Assert.Equal(fix.DestinationLocationId, txs[0].ToLocationId);
            Assert.Equal(fix.StaffUserId, txs[0].UserId);

            // CRITICAL INVARIANT 3: Outbound fulfillment remains Completed (zero regression)
            var originalOrder = await verifyContext.OutboundOrders.FindAsync(orderId);
            Assert.NotNull(originalOrder);
            Assert.Equal(OutboundOrderStatus.Completed, originalOrder.Status);

            // CRITICAL INVARIANT 4: Aggregate status synchronization
            var finalHandoff = await verifyContext.ReturnInboundHandoffs.FindAsync(deliveryResult.InboundHandoffId!.Value);
            Assert.NotNull(finalHandoff);
            Assert.Equal(ReturnInboundHandoffStatus.Received, finalHandoff.Status);

            var finalShipment = await verifyContext.ReturnShipments.FindAsync(returnShipmentResult.ReturnShipmentId);
            Assert.NotNull(finalShipment);
            Assert.Equal(ReturnShipmentStatus.ReceivedAndInspected, finalShipment.Status);

            var finalAuth = await verifyContext.ReturnAuthorizations.FindAsync(authResult.AuthorizationId);
            Assert.NotNull(finalAuth);
            Assert.Equal(ReturnAuthorizationStatus.Completed, finalAuth.Status);
        }
    }

    [Fact]
    public async Task EndToEndReverseLogisticsAudit_PartialReceiptWithShortage_IsolatesDiscrepancyAndConservesStock()
    {
        var fix = await SeedAuditEnvironmentAsync();
        var orderId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();
        const decimal expectedQuantity = 10m;
        const decimal physicalReceivedQuantity = 6m; // 4 units short

        using (var setup = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            fix.DbName))
        {
            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = fix.ClientId,
                FacilityId = fix.OriginFacilityId,
                OrderNumber = "ORD-PARTIAL-001",
                CustomerName = "Pacific Precision",
                Status = OutboundOrderStatus.Completed,
                DeliveryStatus = OrderDeliveryStatus.DeliveryException
            };

            var orderLine = new OutboundOrderLine
            {
                Id = orderLineId,
                OutboundOrderId = orderId,
                ItemId = fix.ItemId,
                LineNumber = 1,
                QuantityOrdered = expectedQuantity,
                QuantityAllocated = expectedQuantity,
                QuantityShipped = expectedQuantity,
                Status = OutboundOrderLineStatus.Allocated
            };

            var package = new Package
            {
                Id = packageId,
                ClientId = fix.ClientId,
                FacilityId = fix.OriginFacilityId,
                OutboundOrderId = orderId,
                PackageNumber = "PKG-PARTIAL-001",
                TrackingNumber = "TRK-PARTIAL-001",
                Status = PackageStatus.Loaded
            };

            var pkgLine = new PackageLine
            {
                Id = Guid.NewGuid(),
                PackageId = packageId,
                OutboundOrderLineId = orderLineId,
                ItemId = fix.ItemId,
                Quantity = expectedQuantity,
                InventoryAllocationId = Guid.NewGuid()
            };

            var auth = ReturnAuthorization.Create(
                fix.ClientId,
                fix.OriginFacilityId,
                fix.DestinationFacilityId,
                orderId,
                null,
                packageId,
                null,
                "RA-PARTIAL-001",
                ReturnReason.DamagedInTransit,
                "Authorized partial return transit.",
                fix.StaffUserId,
                "RMA-PARTIAL-001",
                DateTime.UtcNow.AddDays(7));

            var shipment = new ReturnShipment
            {
                Id = Guid.NewGuid(),
                ClientId = fix.ClientId,
                ReturnAuthorizationId = auth.Id,
                OrderId = orderId,
                PackageId = packageId,
                OriginFacilityId = fix.OriginFacilityId,
                DestinationFacilityId = fix.DestinationFacilityId,
                CarrierCode = "FEDEX",
                TrackingNumber = package.TrackingNumber,
                AuthorizedByUserId = fix.StaffUserId,
                Status = ReturnShipmentStatus.DeliveredAtWarehouse,
                DeliveredAtWarehouseUtc = DateTime.UtcNow.AddHours(-1)
            };

            auth.LinkReturnShipment(shipment.Id);
            auth.RecordDeliveredAtFacility(DateTime.UtcNow.AddHours(-1));

            var handoff = ReturnInboundHandoff.Create(
                fix.ClientId,
                fix.DestinationFacilityId,
                shipment.Id,
                auth.Id,
                packageId,
                orderId,
                "HND-PARTIAL-001",
                auth.RmaNumber!,
                shipment.TrackingNumber,
                shipment.CarrierCode,
                DateTime.UtcNow.AddHours(-1),
                "DOCK-01",
                "Delivered at dock.");

            shipment.InboundHandoffId = handoff.Id;

            setup.OutboundOrders.Add(order);
            setup.OutboundOrderLines.Add(orderLine);
            setup.Packages.Add(package);
            setup.PackageLines.Add(pkgLine);
            setup.ReturnAuthorizations.Add(auth);
            setup.ReturnShipments.Add(shipment);
            setup.ReturnInboundHandoffs.Add(handoff);
            await setup.SaveChangesAsync();
        }

        var tenantProvider = new TestTenantProvider { TenantId = fix.ClientId, BypassTenantFilter = false };
        var receivingUserContext = new TestCurrentUserContext
        {
            UserId = fix.StaffUserId,
            UserType = UserType.Staff,
            FacilityId = fix.DestinationFacilityId
        };

        var service = new ReturnReceivingService(
            TestDbContextFactory.Create(tenantProvider, receivingUserContext, fix.DbName),
            tenantProvider,
            receivingUserContext,
            NullLogger<ReturnReceivingService>.Instance);

        // Fetch handoff
        using var queryContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext(), fix.DbName);
        var handoffInDb = await queryContext.ReturnInboundHandoffs.FirstAsync();

        // 1. Initiate Session
        var session = await service.InitiateReturnReceivingAsync(new InitiateReturnReceivingRequest
        {
            ReturnInboundHandoffId = handoffInDb.Id,
            FacilityId = fix.DestinationFacilityId,
            OperatorUserId = fix.StaffUserId
        });

        // 2. Physical Arrival Scan
        await service.ConfirmPhysicalArrivalAsync(new ConfirmPhysicalArrivalRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            ScannedBarcode = handoffInDb.TrackingNumber
        });

        // 3. Partial Quantity Verification (6 received vs 10 expected)
        var verified = await service.VerifyReceivedQuantitiesAsync(new VerifyReceivedQuantitiesRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Lines = [new VerifyReceivedLineItem { LineId = session.Lines[0].Id, ReceivedQuantity = physicalReceivedQuantity }]
        });

        Assert.Single(verified.Discrepancies);
        Assert.Equal(ReturnDiscrepancyType.Shortage, verified.Discrepancies[0].DiscrepancyType);
        Assert.Equal(4m, verified.Discrepancies[0].DiscrepancyQuantity);

        // 4. Inspection (4 accepted, 2 quarantined for QA = 6 total)
        await service.InspectAndAssignDispositionAsync(new InspectAndAssignDispositionRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Dispositions = [new InspectLineDispositionItem
            {
                LineId = session.Lines[0].Id,
                AcceptedQuantity = 4m,
                DamagedQuantity = 0m,
                QuarantinedQuantity = 2m,
                Disposition = ReturnDisposition.RestockToAvailable,
                TargetLocationId = fix.DestinationLocationId
            }]
        });

        // 5. Post Inventory
        var postResult = await service.PostReturnInventoryAsync(new PostReturnInventoryRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            DefaultLocationId = fix.DestinationLocationId
        });

        Assert.True(postResult.IsSuccess);
        Assert.Equal(4m, postResult.TotalAcceptedQuantity);
        Assert.Equal(2m, postResult.TotalQuarantinedQuantity);

        // 6. Complete Session
        var compResult = await service.CompleteReturnReceivingAsync(new CompleteReturnReceivingRequest
        {
            SessionId = session.Id,
            OperatorUserId = fix.StaffUserId,
            Notes = "Partial receipt accepted with shortage discrepancy."
        });

        Assert.True(compResult.IsSuccess);

        // Invariant check: StockOnHand available must be exactly 4, never 6 or 10!
        using var finalContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext(), fix.DbName);
        var stock = await finalContext.StockOnHand.FirstOrDefaultAsync(s => s.ClientId == fix.ClientId && s.FacilityId == fix.DestinationFacilityId && s.ItemId == fix.ItemId);
        Assert.NotNull(stock);
        Assert.Equal(4m, stock.QtyAvailable);
        Assert.Equal(2m, stock.QtyQuarantined);
        Assert.Equal(0m, stock.QtyDamaged);
        Assert.Equal(6m, stock.TotalQuantity);
    }
}
