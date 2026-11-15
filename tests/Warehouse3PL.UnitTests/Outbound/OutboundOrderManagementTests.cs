using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.CrossDock.Plans.Commands.GenerateCrossDockPlan;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.CancelOutboundOrder;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.CreateOutboundOrder;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.HoldOutboundOrder;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.ImportOutboundOrdersBulk;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Commands.ReleaseOutboundOrder;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.CheckOrderStockAvailability;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.GetOutboundOrderById;
using Warehouse3PL.Application.Features.Outbound.OrderManagement.Queries.GetOutboundOrders;
using Warehouse3PL.Domain.Entities.Catalog;
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

namespace Warehouse3PL.UnitTests.Outbound;

public class OutboundOrderManagementTests
{
    [Fact]
    public async Task CreateOrder_ValidB2BOrder_SucceedsWithSnapshotAndHistory()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Supply", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Chicago Hub", FacilityCode = "ORD-01" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId });
            seed.Customers.Add(new Customer { Id = customerId, ClientId = clientId, CustomerCode = "CUST-01", CustomerName = "Midwest Retailers" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-B2B-01", Description = "Premium Electronics" });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new CreateOutboundOrderCommandHandler(context, tenantProvider, currentUser);
            var result = await handler.Handle(new CreateOutboundOrderCommand
            {
                FacilityId = facilityId,
                CustomerId = customerId,
                OrderNumber = "SO-2026-B2B-100",
                ExternalOrderReference = "PO-CLIENT-999",
                OrderType = OutboundOrderType.B2BSalesOrder,
                Priority = 2,
                ServiceLevel = CarrierServiceLevel.StandardGround,
                ShippingAddress = new CreateShippingAddressDto
                {
                    RecipientName = "Receiving Mgr",
                    CompanyName = "Midwest Retailers Dock",
                    AddressLine1 = "100 Michigan Ave",
                    City = "Chicago",
                    StateOrProvince = "IL",
                    PostalCode = "60601",
                    CountryCode = "USA"
                },
                Lines =
                [
                    new CreateOutboundOrderLineDto { ItemId = itemId, QuantityOrdered = 50m }
                ]
            }, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("SO-2026-B2B-100", result.OrderNumber);
            Assert.Equal(OutboundOrderStatus.Released, result.Status);
            Assert.Equal(OutboundOrderType.B2BSalesOrder, result.OrderType);
            Assert.Single(result.Lines);
            Assert.Equal(50m, result.Lines[0].QuantityOrdered);
            Assert.NotNull(result.ShippingAddress);
            Assert.Equal("100 Michigan Ave", result.ShippingAddress.AddressLine1);
            Assert.Single(result.StatusHistories);
            Assert.Equal(OutboundOrderStatus.Released, result.StatusHistories[0].ToStatus);
        }
    }

    [Fact]
    public async Task CreateOrder_CrossTenantCustomerAndSkuInjection_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientAId = Guid.NewGuid();
        var clientBId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();
        var itemBId = Guid.NewGuid();
        var itemAId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientAId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientAId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientAId, CompanyName = "Client A", AccountCode = "CLA" });
            seed.Clients.Add(new Client { Id = clientBId, CompanyName = "Client B", AccountCode = "CLB" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Shared Hub", FacilityCode = "HUB-01" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientAId, FacilityId = facilityId });

            // Customer and Item belonging to Client B
            seed.Customers.Add(new Customer { Id = customerBId, ClientId = clientBId, CustomerCode = "CUST-B", CustomerName = "Client B Store" });
            seed.Items.Add(new Item { Id = itemBId, ClientId = clientBId, Sku = "SKU-B", Description = "B Item" });
            seed.Items.Add(new Item { Id = itemAId, ClientId = clientAId, Sku = "SKU-A", Description = "A Item" });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new CreateOutboundOrderCommandHandler(context, tenantProvider, currentUser);

            // 1. Client A targeting Client B's customer
            var customerInjectionCmd = new CreateOutboundOrderCommand
            {
                FacilityId = facilityId,
                CustomerId = customerBId,
                OrderType = OutboundOrderType.B2BSalesOrder,
                ShippingAddress = new CreateShippingAddressDto
                {
                    RecipientName = "R", AddressLine1 = "A1", City = "C", StateOrProvince = "S", PostalCode = "0", CountryCode = "USA"
                },
                Lines = [new CreateOutboundOrderLineDto { ItemId = itemAId, QuantityOrdered = 10m }]
            };
            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(customerInjectionCmd, CancellationToken.None));

            // 2. Client A targeting Client B's Item SKU
            var skuInjectionCmd = new CreateOutboundOrderCommand
            {
                FacilityId = facilityId,
                OrderType = OutboundOrderType.B2BSalesOrder,
                ShippingAddress = new CreateShippingAddressDto
                {
                    RecipientName = "R", AddressLine1 = "A1", City = "C", StateOrProvince = "S", PostalCode = "0", CountryCode = "USA"
                },
                Lines = [new CreateOutboundOrderLineDto { ItemId = itemBId, QuantityOrdered = 10m }]
            };
            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(skuInjectionCmd, CancellationToken.None));
        }
    }

    [Fact]
    public async Task CreateOrder_UnauthorizedFacilityAccess_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var unauthorizedFacilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Client A", AccountCode = "CLA" });
            seed.Facilities.Add(new Facility { Id = unauthorizedFacilityId, Name = "Restricted Hub", FacilityCode = "REST-01" });
            // NOTE: ClientFacilityAccess is intentionally NOT granted!
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-A", Description = "A Item" });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new CreateOutboundOrderCommandHandler(context, tenantProvider, currentUser);
            var cmd = new CreateOutboundOrderCommand
            {
                FacilityId = unauthorizedFacilityId,
                OrderType = OutboundOrderType.B2BSalesOrder,
                ShippingAddress = new CreateShippingAddressDto
                {
                    RecipientName = "R", AddressLine1 = "A1", City = "C", StateOrProvince = "S", PostalCode = "0", CountryCode = "USA"
                },
                Lines = [new CreateOutboundOrderLineDto { ItemId = itemId, QuantityOrdered = 10m }]
            };

            await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(cmd, CancellationToken.None));
        }
    }

    [Fact]
    public async Task CreateOrder_DuplicateExternalOrderReference_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Dallas Hub", FacilityCode = "DFW-01" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-A", Description = "A Item" });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new CreateOutboundOrderCommandHandler(context, tenantProvider, currentUser);
            var cmd1 = new CreateOutboundOrderCommand
            {
                FacilityId = facilityId,
                ExternalOrderReference = "SHOPIFY-REF-1001",
                OrderType = OutboundOrderType.EcommerceFulfillment,
                ShippingAddress = new CreateShippingAddressDto
                {
                    RecipientName = "Jane Doe", AddressLine1 = "123 Main St", City = "Dallas", StateOrProvince = "TX", PostalCode = "75001", CountryCode = "USA"
                },
                Lines = [new CreateOutboundOrderLineDto { ItemId = itemId, QuantityOrdered = 2m }]
            };

            await handler.Handle(cmd1, CancellationToken.None);

            // Second order with exact same external reference must throw ValidationException
            var cmd2 = new CreateOutboundOrderCommand
            {
                FacilityId = facilityId,
                ExternalOrderReference = "SHOPIFY-REF-1001",
                OrderType = OutboundOrderType.EcommerceFulfillment,
                ShippingAddress = new CreateShippingAddressDto
                {
                    RecipientName = "John Doe", AddressLine1 = "456 Elm St", City = "Dallas", StateOrProvince = "TX", PostalCode = "75001", CountryCode = "USA"
                },
                Lines = [new CreateOutboundOrderLineDto { ItemId = itemId, QuantityOrdered = 1m }]
            };

            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(cmd2, CancellationToken.None));
        }
    }

    [Fact]
    public async Task StockTransferOrder_SameFacilityOrInvalidDestination_ThrowsValidationException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityAId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityAId, Name = "Dallas Hub", FacilityCode = "DFW-01" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityAId });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-A", Description = "A Item" });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new CreateOutboundOrderCommandHandler(context, tenantProvider, currentUser);

            // Stock transfer targeting same facility
            var transferSame = new CreateOutboundOrderCommand
            {
                FacilityId = facilityAId,
                DestinationFacilityId = facilityAId,
                OrderType = OutboundOrderType.StockTransferOrder,
                ShippingAddress = new CreateShippingAddressDto
                {
                    RecipientName = "Hub Recv", AddressLine1 = "Dock A", City = "Dallas", StateOrProvince = "TX", PostalCode = "75001", CountryCode = "USA"
                },
                Lines = [new CreateOutboundOrderLineDto { ItemId = itemId, QuantityOrdered = 20m }]
            };

            await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(transferSame, CancellationToken.None));
        }
    }

    [Fact]
    public async Task BulkImport_PartialFailuresAndDuplicates_IsolatesValidRows()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemAId = Guid.NewGuid();
        var unknownItemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Dallas Hub", FacilityCode = "DFW-01" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId });
            seed.Items.Add(new Item { Id = itemAId, ClientId = clientId, Sku = "SKU-VALID", Description = "Valid Item" });

            // Pre-existing order with external reference
            seed.OutboundOrders.Add(new OutboundOrder
            {
                ClientId = clientId,
                FacilityId = facilityId,
                OrderNumber = "SO-EXISTING-01",
                ExternalOrderReference = "PO-DUPLICATE-99",
                CustomerName = "Pre-existing Customer",
                Status = OutboundOrderStatus.Released
            });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new ImportOutboundOrdersBulkCommandHandler(context, tenantProvider, currentUser);
            var result = await handler.Handle(new ImportOutboundOrdersBulkCommand
            {
                FacilityId = facilityId,
                SourceFileName = "daily_orders_batch.json",
                Orders =
                [
                    // Row 1: Valid
                    new CreateOutboundOrderCommand
                    {
                        FacilityId = facilityId,
                        ExternalOrderReference = "PO-NEW-101",
                        Lines = [new CreateOutboundOrderLineDto { ItemId = itemAId, QuantityOrdered = 5m }]
                    },
                    // Row 2: Unknown SKU
                    new CreateOutboundOrderCommand
                    {
                        FacilityId = facilityId,
                        ExternalOrderReference = "PO-NEW-102",
                        Lines = [new CreateOutboundOrderLineDto { ItemId = unknownItemId, QuantityOrdered = 5m }]
                    },
                    // Row 3: Duplicate External Reference
                    new CreateOutboundOrderCommand
                    {
                        FacilityId = facilityId,
                        ExternalOrderReference = "PO-DUPLICATE-99",
                        Lines = [new CreateOutboundOrderLineDto { ItemId = itemAId, QuantityOrdered = 5m }]
                    }
                ]
            }, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(3, result.TotalRows);
            Assert.Equal(1, result.SuccessfulRows);
            Assert.Equal(1, result.FailedRows);
            Assert.Equal(1, result.DuplicateRows);
            Assert.Equal(ImportBatchStatus.CompletedWithErrors, result.Status);
            Assert.Equal(3, result.Rows.Count);

            Assert.True(result.Rows[0].IsSuccess);
            Assert.False(result.Rows[1].IsSuccess);
            Assert.Contains("does not exist", result.Rows[1].ErrorMessage);
            Assert.False(result.Rows[2].IsSuccess);
            Assert.True(result.Rows[2].IsDuplicate);
        }
    }

    [Fact]
    public async Task HoldAndRelease_PreservesPreHoldStatusAndRecordsAuditHistory()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Dallas Hub", FacilityCode = "DFW-01" });
            seed.OutboundOrders.Add(new OutboundOrder
            {
                Id = orderId,
                ClientId = clientId,
                FacilityId = facilityId,
                OrderNumber = "SO-HOLD-TEST-01",
                CustomerName = "Hold Customer",
                Status = OutboundOrderStatus.PartiallyAllocated
            });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var holdHandler = new HoldOutboundOrderCommandHandler(context, tenantProvider, currentUser);
            var heldOrder = await holdHandler.Handle(new HoldOutboundOrderCommand
            {
                OrderId = orderId,
                Reason = "Address verification pending"
            }, CancellationToken.None);

            Assert.Equal(OutboundOrderStatus.OnHold, heldOrder.Status);
            Assert.Equal(OutboundOrderStatus.PartiallyAllocated, heldOrder.StatusBeforeHold);
            Assert.Equal("Address verification pending", heldOrder.HoldReason);
            Assert.Single(heldOrder.StatusHistories);
            Assert.Equal(OutboundOrderStatus.OnHold, heldOrder.StatusHistories[0].ToStatus);

            // Release order
            var releaseHandler = new ReleaseOutboundOrderCommandHandler(context, tenantProvider, currentUser);
            var releasedOrder = await releaseHandler.Handle(new ReleaseOutboundOrderCommand
            {
                OrderId = orderId,
                Notes = "Address verified by customer support"
            }, CancellationToken.None);

            // Must accurately restore pre-hold status (PartiallyAllocated)
            Assert.Equal(OutboundOrderStatus.PartiallyAllocated, releasedOrder.Status);
            Assert.Null(releasedOrder.StatusBeforeHold);
            Assert.Null(releasedOrder.HoldReason);
            Assert.Equal(2, releasedOrder.StatusHistories.Count);
        }
    }

    [Fact]
    public async Task CancelOrder_InProgressOrCompleted_ThrowsInvalidDomainStateException()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var inProgressOrderId = Guid.NewGuid();
        var completedOrderId = Guid.NewGuid();
        var releasedOrderId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Dallas Hub", FacilityCode = "DFW-01" });
            seed.OutboundOrders.Add(new OutboundOrder
            {
                Id = inProgressOrderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "SO-INPROGRESS", CustomerName = "C1", Status = OutboundOrderStatus.InProgress
            });
            seed.OutboundOrders.Add(new OutboundOrder
            {
                Id = completedOrderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "SO-COMPLETED", CustomerName = "C2", Status = OutboundOrderStatus.Completed
            });
            seed.OutboundOrders.Add(new OutboundOrder
            {
                Id = releasedOrderId, ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "SO-RELEASED", CustomerName = "C3", Status = OutboundOrderStatus.Released
            });
            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new CancelOutboundOrderCommandHandler(context, tenantProvider, currentUser);

            // Cannot cancel InProgress
            await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
                handler.Handle(new CancelOutboundOrderCommand { OrderId = inProgressOrderId, Reason = "Abort" }, CancellationToken.None));

            // Cannot cancel Completed
            await Assert.ThrowsAsync<InvalidDomainStateException>(() =>
                handler.Handle(new CancelOutboundOrderCommand { OrderId = completedOrderId, Reason = "Abort" }, CancellationToken.None));

            // Cancelling Released succeeds
            var cancelled = await handler.Handle(new CancelOutboundOrderCommand { OrderId = releasedOrderId, Reason = "Customer changed mind" }, CancellationToken.None);
            Assert.Equal(OutboundOrderStatus.Cancelled, cancelled.Status);
            Assert.Equal("Customer changed mind", cancelled.CancellationReason);
        }
    }

    [Fact]
    public async Task CheckStockAvailability_IsReadOnlyAndDoesNotMutateStockOrLedger()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var itemIdA = Guid.NewGuid();
        var itemIdB = Guid.NewGuid();
        var locPickableId = Guid.NewGuid();
        var locDamagedId = Guid.NewGuid();
        var expiredLotId = Guid.NewGuid();
        var soundLotId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Dallas Hub", FacilityCode = "DFW-01" });

            seed.Locations.Add(new Location { Id = locPickableId, FacilityId = facilityId, LocationCode = "BIN-SOUND-01", IsActive = true, IsPickable = true, IsDamaged = false });
            seed.Locations.Add(new Location { Id = locDamagedId, FacilityId = facilityId, LocationCode = "BIN-DMG-01", IsActive = true, IsPickable = true, IsDamaged = true });

            seed.Items.Add(new Item { Id = itemIdA, ClientId = clientId, Sku = "SKU-ITEM-A", Description = "Item A" });
            seed.Items.Add(new Item { Id = itemIdB, ClientId = clientId, Sku = "SKU-ITEM-B", Description = "Item B" });

            seed.InventoryLots.Add(new InventoryLot
            {
                Id = soundLotId, ClientId = clientId, ItemId = itemIdB, LotNumber = "LOT-SOUND-2026",
                ExpirationDate = DateTime.UtcNow.AddMonths(6), QaStatus = LotQaStatus.Released
            });
            seed.InventoryLots.Add(new InventoryLot
            {
                Id = expiredLotId, ClientId = clientId, ItemId = itemIdB, LotNumber = "LOT-EXPIRED-2025",
                ExpirationDate = DateTime.UtcNow.AddDays(-10), QaStatus = LotQaStatus.Released
            });

            // Item A: 50 units in sound bin
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId, FacilityId = facilityId, ItemId = itemIdA,
                LocationId = locPickableId, QtyAvailable = 50m
            });

            // Item B: 10 units in sound lot, 30 units in expired lot, 15 units in damaged location
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId, FacilityId = facilityId, ItemId = itemIdB,
                LocationId = locPickableId, LotId = soundLotId, QtyAvailable = 10m
            });
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId, FacilityId = facilityId, ItemId = itemIdB,
                LocationId = locPickableId, LotId = expiredLotId, QtyAvailable = 30m
            });
            seed.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId, FacilityId = facilityId, ItemId = itemIdB,
                LocationId = locDamagedId, QtyAvailable = 15m
            });

            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = clientId,
                FacilityId = facilityId,
                OrderNumber = "SO-AVAIL-TEST",
                CustomerName = "Target Store",
                Status = OutboundOrderStatus.Released
            };
            order.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = orderId, ItemId = itemIdA, LineNumber = 1, QuantityOrdered = 20m });
            order.Lines.Add(new OutboundOrderLine { Id = Guid.NewGuid(), OutboundOrderId = orderId, ItemId = itemIdB, LineNumber = 2, QuantityOrdered = 25m });
            seed.OutboundOrders.Add(order);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var handler = new CheckOrderStockAvailabilityQueryHandler(context, tenantProvider, currentUser);
            var assessment = await handler.Handle(new CheckOrderStockAvailabilityQuery(orderId), CancellationToken.None);

            Assert.NotNull(assessment);
            Assert.False(assessment.IsFullyAvailable); // Line B has shortage

            // Line 1 (Item A): Requested 20, Available 50 -> Shortage 0, FullyAvailable = true
            var lineA = assessment.Lines.First(l => l.ItemId == itemIdA);
            Assert.Equal(20m, lineA.QuantityRequested);
            Assert.Equal(50m, lineA.QuantityAvailable);
            Assert.Equal(0m, lineA.ShortageQuantity);
            Assert.True(lineA.IsFullyAvailable);

            // Line 2 (Item B): Requested 25, Available 10 (expired 30 and damaged 15 excluded!) -> Shortage 15, FullyAvailable = false
            var lineB = assessment.Lines.First(l => l.ItemId == itemIdB);
            Assert.Equal(25m, lineB.QuantityRequested);
            Assert.Equal(10m, lineB.QuantityAvailable);
            Assert.Equal(15m, lineB.ShortageQuantity);
            Assert.False(lineB.IsFullyAvailable);

            // Overall fulfillment percentage: (20 + 10) / (20 + 25) = 30 / 45 = 66.67%
            Assert.Equal(66.67m, assessment.OverallFulfillmentPercent);

            // READ-ONLY INVARIANT: Verify NO StockOnHand rows were mutated, and NO ledger records created
            var stockItemA = await context.StockOnHand.FirstAsync(s => s.ItemId == itemIdA);
            Assert.Equal(50m, stockItemA.QtyAvailable);
            Assert.Equal(0m, stockItemA.QtyAllocated);

            var ledgerCount = await context.InventoryTransactions.CountAsync();
            Assert.Equal(0, ledgerCount); // Strictly 0 transactions created!
        }
    }

    [Fact]
    public async Task CrossDockingCompatibility_ExcludesOnHoldCancelledCompletedOrders()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var stagingLocId = Guid.NewGuid();
        var recSessionId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId, BypassTenantFilter = false };
        var currentUser = new TestCurrentUserContext { TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex", AccountCode = "APX" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Dallas Hub", FacilityCode = "DFW-01" });
            seed.Locations.Add(new Location { Id = stagingLocId, FacilityId = facilityId, LocationCode = "STAGING-01", Barcode = "BAR-ST-01", LocationType = LocationType.StagingLane });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-CROSSDOCK-COMPAT", Description = "Test SKU" });

            seed.ReceivingSessions.Add(new ReceivingSession
            {
                Id = recSessionId,
                ClientId = clientId,
                FacilityId = facilityId,
                SessionNumber = "REC-CD-001",
                Status = ReceivingStatus.Completed
            });

            seed.ReceiptLines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(),
                ReceivingSessionId = recSessionId,
                ItemId = itemId,
                StagingLocationId = stagingLocId,
                Condition = ReceiptConditionStatus.Sound,
                QuantityReceived = 10m,
                Lpn = "LPN-CD-01"
            });

            seed.StockOnHand.Add(new StockOnHand
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = stagingLocId,
                QtyAvailable = 10m
            });

            // 1. Released Order (ELIGIBLE)
            var orderReleased = new OutboundOrder
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-ELIGIBLE", CustomerName = "C1", Status = OutboundOrderStatus.Released
            };
            orderReleased.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 10m });
            seed.OutboundOrders.Add(orderReleased);

            // 2. OnHold Order (INELIGIBLE)
            var orderHold = new OutboundOrder
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-ONHOLD", CustomerName = "C2", Status = OutboundOrderStatus.OnHold
            };
            orderHold.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 10m });
            seed.OutboundOrders.Add(orderHold);

            // 3. Cancelled Order (INELIGIBLE)
            var orderCancelled = new OutboundOrder
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-CANCELLED", CustomerName = "C3", Status = OutboundOrderStatus.Cancelled
            };
            orderCancelled.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 10m });
            seed.OutboundOrders.Add(orderCancelled);

            // 4. Completed Order (INELIGIBLE)
            var orderCompleted = new OutboundOrder
            {
                Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facilityId,
                OrderNumber = "ORD-COMPLETED", CustomerName = "C4", Status = OutboundOrderStatus.Completed
            };
            orderCompleted.Lines.Add(new OutboundOrderLine { ItemId = itemId, LineNumber = 1, QuantityOrdered = 10m });
            seed.OutboundOrders.Add(orderCompleted);

            await seed.SaveChangesAsync();
        }

        using (var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var crossDockEngine = new CrossDockEngine(context, NullLogger<CrossDockEngine>.Instance);
            var opportunities = await crossDockEngine.DetectOpportunitiesAsync(
                facilityId,
                clientId,
                cancellationToken: CancellationToken.None);

            // Exactly 1 opportunity must be returned (for ORD-ELIGIBLE only)
            Assert.Single(opportunities);
            Assert.Equal("ORD-ELIGIBLE", opportunities[0].MatchedOutboundOrderNumber);
        }
    }
}
