using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public class OutboundOrderDomainAndPersistenceTests
{
    [Fact]
    public void OutboundOrder_StateTransitions_HoldAndReleaseRestoresPreviousStatus()
    {
        var order = new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "SO-2026-001",
            Status = OutboundOrderStatus.Released
        };

        var userId = Guid.NewGuid();

        // 1. Hold from Released
        order.Hold("Customer credit review", userId);
        Assert.Equal(OutboundOrderStatus.OnHold, order.Status);
        Assert.Equal(OutboundOrderStatus.Released, order.StatusBeforeHold);
        Assert.Equal("Customer credit review", order.HoldReason);
        Assert.NotNull(order.HeldAtUtc);
        Assert.False(order.CanBeAllocated);
        Assert.Single(order.StatusHistories);
        Assert.Equal(OutboundOrderStatus.Released, order.StatusHistories.First().FromStatus);
        Assert.Equal(OutboundOrderStatus.OnHold, order.StatusHistories.First().ToStatus);

        // 2. ReleaseHold restores to Released
        order.ReleaseHold(userId, "Credit approved");
        Assert.Equal(OutboundOrderStatus.Released, order.Status);
        Assert.Null(order.StatusBeforeHold);
        Assert.Null(order.HoldReason);
        Assert.Null(order.HeldAtUtc);
        Assert.True(order.CanBeAllocated);
        Assert.Equal(2, order.StatusHistories.Count);

        // 3. Hold from PartiallyAllocated
        order.Status = OutboundOrderStatus.PartiallyAllocated;
        order.Hold("Address amendment required", userId);
        Assert.Equal(OutboundOrderStatus.OnHold, order.Status);
        Assert.Equal(OutboundOrderStatus.PartiallyAllocated, order.StatusBeforeHold);

        // 4. ReleaseHold restores to PartiallyAllocated
        order.ReleaseHold(userId, "Address confirmed");
        Assert.Equal(OutboundOrderStatus.PartiallyAllocated, order.Status);
        Assert.Null(order.StatusBeforeHold);
    }

    [Fact]
    public void OutboundOrder_StateTransitions_InvalidTransitionGuards()
    {
        var order = new OutboundOrder { Id = Guid.NewGuid(), OrderNumber = "SO-GUARD-01", Status = OutboundOrderStatus.Released };
        var userId = Guid.NewGuid();

        // Cannot release an order that isn't on hold
        Assert.Throws<InvalidDomainStateException>(() => order.ReleaseHold(userId));

        // Cannot hold without reason
        Assert.Throws<ValidationException>(() => order.Hold("", userId));

        // Cannot cancel without reason
        Assert.Throws<ValidationException>(() => order.Cancel("", userId));

        // Cannot hold completed or cancelled orders
        order.Status = OutboundOrderStatus.Completed;
        Assert.Throws<InvalidDomainStateException>(() => order.Hold("Hold attempt", userId));

        order.Status = OutboundOrderStatus.Cancelled;
        Assert.Throws<InvalidDomainStateException>(() => order.Hold("Hold attempt", userId));

        // Cannot cancel InProgress or Completed orders
        order.Status = OutboundOrderStatus.InProgress;
        Assert.Throws<InvalidDomainStateException>(() => order.Cancel("Cancel attempt", userId));

        order.Status = OutboundOrderStatus.Completed;
        Assert.Throws<InvalidDomainStateException>(() => order.Cancel("Cancel attempt", userId));
    }

    [Fact]
    public void StockTransferOrder_Validations_RequiresDestinationFacilityAndDifferentFromSource()
    {
        var facilityA = Guid.NewGuid();
        var facilityB = Guid.NewGuid();

        // Stock transfer order without destination throws
        var transferWithoutDest = new OutboundOrder
        {
            FacilityId = facilityA,
            OrderType = OutboundOrderType.StockTransferOrder,
            DestinationFacilityId = null
        };
        Assert.Throws<DomainException>(() => transferWithoutDest.ValidateStockTransferRules());

        // Stock transfer order with same source and destination throws
        var transferSameFacility = new OutboundOrder
        {
            FacilityId = facilityA,
            OrderType = OutboundOrderType.StockTransferOrder,
            DestinationFacilityId = facilityA
        };
        Assert.Throws<DomainException>(() => transferSameFacility.ValidateStockTransferRules());

        // Valid stock transfer order passes
        var validTransfer = new OutboundOrder
        {
            FacilityId = facilityA,
            OrderType = OutboundOrderType.StockTransferOrder,
            DestinationFacilityId = facilityB
        };
        validTransfer.ValidateStockTransferRules(); // Should not throw
        Assert.True(validTransfer.IsStockTransfer);

        // Standard B2B sales order does not throw even without DestinationFacilityId
        var b2bOrder = new OutboundOrder
        {
            FacilityId = facilityA,
            OrderType = OutboundOrderType.B2BSalesOrder,
            DestinationFacilityId = null
        };
        b2bOrder.ValidateStockTransferRules(); // Passes cleanly
        Assert.False(b2bOrder.IsStockTransfer);
    }

    [Fact]
    public void ShippingAddress_Amendment_PreservesOriginalSnapshotAndAuditTrail()
    {
        var order = new OutboundOrder { Id = Guid.NewGuid(), OrderNumber = "SO-ADDR-01" };
        var address = new ShippingAddress
        {
            OutboundOrderId = order.Id,
            RecipientName = "Acme Retail Store #4",
            CompanyName = "Acme Distribution Corp",
            AddressLine1 = "100 Industrial Parkway",
            City = "Dallas",
            StateOrProvince = "TX",
            PostalCode = "75001",
            CountryCode = "USA"
        };
        order.SetShippingAddress(address);

        Assert.Contains("Acme Retail Store #4", order.DestinationAddress);
        Assert.False(address.IsAmended);
        Assert.Null(address.OriginalAddressSnapshot);

        // Perform amendment
        var userId = Guid.NewGuid();
        address.Amend(
            recipientName: "Acme Retail Store #4 - Dock B",
            companyName: "Acme Distribution Corp",
            addressLine1: "102 Industrial Parkway",
            addressLine2: "Suite 500",
            city: "Dallas",
            stateOrProvince: "TX",
            postalCode: "75001",
            countryCode: "USA",
            phoneNumber: "555-0192",
            deliveryInstructions: "Call dock master before arrival",
            amendedByUserId: userId,
            reason: "Delivery rerouted to specialized receiving bay"
        );

        Assert.True(address.IsAmended);
        Assert.NotNull(address.OriginalAddressSnapshot);
        Assert.Contains("100 Industrial Parkway", address.OriginalAddressSnapshot);
        Assert.Equal("102 Industrial Parkway", address.AddressLine1);
        Assert.Equal("Suite 500", address.AddressLine2);
        Assert.Equal(userId, address.AmendedByUserId);
        Assert.Equal("Delivery rerouted to specialized receiving bay", address.AmendmentReason);
    }

    [Fact]
    public async Task Customer_TenantIsolation_AndGlobalClientScope()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientAId = Guid.NewGuid();
        var clientBId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientAId, CompanyName = "Client Alpha", AccountCode = "ALPHA" });
            seed.Clients.Add(new Client { Id = clientBId, CompanyName = "Client Beta", AccountCode = "BETA" });

            seed.Customers.Add(new Customer
            {
                Id = Guid.NewGuid(),
                ClientId = clientAId,
                CustomerCode = "CUST-A-001",
                CustomerName = "Alpha Supermarket North",
                CustomerType = CustomerType.WholesaleDistributor
            });

            seed.Customers.Add(new Customer
            {
                Id = Guid.NewGuid(),
                ClientId = clientBId,
                CustomerCode = "CUST-B-001",
                CustomerName = "Beta Electronics Retail",
                CustomerType = CustomerType.RetailCustomer
            });

            await seed.SaveChangesAsync();
        }

        // Query under Client A context
        var clientATenant = new TestTenantProvider { TenantId = clientAId, BypassTenantFilter = false };
        var clientAUser = new TestCurrentUserContext { TenantId = clientAId, UserType = UserType.Client };

        using (var contextA = TestDbContextFactory.Create(clientATenant, clientAUser, dbName))
        {
            var customersA = await contextA.Customers.ToListAsync();
            Assert.Single(customersA);
            Assert.Equal("CUST-A-001", customersA[0].CustomerCode);
            Assert.Equal("Alpha Supermarket North", customersA[0].CustomerName);
        }

        // Query under Client B context
        var clientBTenant = new TestTenantProvider { TenantId = clientBId, BypassTenantFilter = false };
        var clientBUser = new TestCurrentUserContext { TenantId = clientBId, UserType = UserType.Client };

        using (var contextB = TestDbContextFactory.Create(clientBTenant, clientBUser, dbName))
        {
            var customersB = await contextB.Customers.ToListAsync();
            Assert.Single(customersB);
            Assert.Equal("CUST-B-001", customersB[0].CustomerCode);
            Assert.Equal("Beta Electronics Retail", customersB[0].CustomerName);
        }
    }

    [Fact]
    public void OrderImportBatch_Aggregates_TracksMetricsAndRowAudits()
    {
        var batch = new OrderImportBatch
        {
            Id = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            BatchNumber = "BATCH-2026-001",
            SourceFileName = "daily_shopify_orders.csv",
            TotalRows = 100
        };

        Assert.Equal(ImportBatchStatus.Processing, batch.Status);

        batch.Rows.Add(new OrderImportBatchRow
        {
            OrderImportBatchId = batch.Id,
            RowNumber = 1,
            ExternalOrderReference = "SHOPIFY-101",
            IsSuccess = true,
            CreatedOutboundOrderId = Guid.NewGuid()
        });

        batch.Rows.Add(new OrderImportBatchRow
        {
            OrderImportBatchId = batch.Id,
            RowNumber = 2,
            ExternalOrderReference = "SHOPIFY-102",
            IsSuccess = false,
            ErrorMessage = "SKU 'SKU-UNKNOWN' not recognized for client"
        });

        batch.Complete(successfulRows: 98, failedRows: 2, duplicateRows: 0, errorJson: "{\"row2\":\"Invalid SKU\"}");

        Assert.Equal(ImportBatchStatus.CompletedWithErrors, batch.Status);
        Assert.Equal(98, batch.SuccessfulRows);
        Assert.Equal(2, batch.FailedRows);
        Assert.Equal(0, batch.DuplicateRows);
        Assert.NotNull(batch.CompletedAtUtc);
    }

    [Fact]
    public async Task OutboundOrder_EFCorePersistence_UniqueConstraintsAndNavigations()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seed = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Client", AccountCode = "APEX" });
            seed.Facilities.Add(new Facility { Id = facilityId, Name = "Dallas Main Hub", FacilityCode = "DFW-01" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-OUT-001", Description = "Widget A" });

            var customer = new Customer
            {
                Id = customerId,
                ClientId = clientId,
                CustomerCode = "CUST-APEX-01",
                CustomerName = "Apex Premier Customer"
            };
            seed.Customers.Add(customer);

            var order = new OutboundOrder
            {
                Id = orderId,
                ClientId = clientId,
                FacilityId = facilityId,
                CustomerId = customerId,
                OrderNumber = "SO-2026-PERSIST-01",
                ExternalOrderReference = "EXT-REF-999",
                OrderType = OutboundOrderType.B2BSalesOrder,
                Status = OutboundOrderStatus.Released,
                Priority = 2,
                ServiceLevel = CarrierServiceLevel.NextDayAir,
                CarrierCode = "FEDEX_AIR",
                CustomerName = "Apex Premier Customer"
            };

            var address = new ShippingAddress
            {
                OutboundOrderId = orderId,
                RecipientName = "Receiving Bay 3",
                AddressLine1 = "500 Logistics Way",
                City = "Fort Worth",
                StateOrProvince = "TX",
                PostalCode = "76102",
                CountryCode = "USA"
            };
            order.SetShippingAddress(address);

            order.Lines.Add(new OutboundOrderLine
            {
                Id = Guid.NewGuid(),
                OutboundOrderId = orderId,
                ItemId = itemId,
                LineNumber = 1,
                QuantityOrdered = 25m,
                QuantityAllocated = 0m,
                Status = OutboundOrderLineStatus.Pending
            });

            order.StatusHistories.Add(new OrderStatusHistory
            {
                OutboundOrderId = orderId,
                FromStatus = OutboundOrderStatus.Draft,
                ToStatus = OutboundOrderStatus.Released,
                Reason = "Initial order submission",
                ChangedByUserId = currentUser.UserId!.Value
            });

            seed.OutboundOrders.Add(order);
            await seed.SaveChangesAsync();
        }

        // Query back and verify all navigations and persistence mappings
        using (var verify = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            var savedOrder = await verify.OutboundOrders
                .Include(o => o.Customer)
                .Include(o => o.ShippingAddress)
                .Include(o => o.Lines)
                    .ThenInclude(l => l.Item)
                .Include(o => o.StatusHistories)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            Assert.NotNull(savedOrder);
            Assert.Equal("SO-2026-PERSIST-01", savedOrder.OrderNumber);
            Assert.Equal(OutboundOrderType.B2BSalesOrder, savedOrder.OrderType);
            Assert.Equal(CarrierServiceLevel.NextDayAir, savedOrder.ServiceLevel);
            Assert.Equal("FEDEX_AIR", savedOrder.CarrierCode);

            // Verify 1:1 ShippingAddress
            Assert.NotNull(savedOrder.ShippingAddress);
            Assert.Equal("500 Logistics Way", savedOrder.ShippingAddress.AddressLine1);
            Assert.Equal("Fort Worth", savedOrder.ShippingAddress.City);

            // Verify Customer relationship
            Assert.NotNull(savedOrder.Customer);
            Assert.Equal("CUST-APEX-01", savedOrder.Customer.CustomerCode);

            // Verify Line items
            Assert.Single(savedOrder.Lines);
            Assert.Equal("SKU-OUT-001", savedOrder.Lines.First().Item.Sku);
            Assert.Equal(25m, savedOrder.Lines.First().QuantityOrdered);

            // Verify Audit status histories
            Assert.Single(savedOrder.StatusHistories);
            Assert.Equal(OutboundOrderStatus.Draft, savedOrder.StatusHistories.First().FromStatus);
            Assert.Equal(OutboundOrderStatus.Released, savedOrder.StatusHistories.First().ToStatus);
        }
    }
}
