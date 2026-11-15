using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Commands.CancelPurchaseOrder;
using Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Commands.CreatePurchaseOrder;
using Warehouse3PL.Application.Features.Inbound.PurchaseOrders.Commands.SubmitPurchaseOrder;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Inbound;

public class PurchaseOrderManagementTests
{
    [Fact]
    public async Task CreatePurchaseOrder_WithValidLines_CalculatesCostAndPersistsDraft()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId1 = Guid.NewGuid();
        var itemId2 = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess
            {
                ClientId = clientId,
                FacilityId = facilityId,
                Status = FacilityAccessStatus.Active
            });
            seed.Items.Add(new Item { Id = itemId1, ClientId = clientId, Sku = "SKU-01", Description = "Widget A", UnitPrice = 10m });
            seed.Items.Add(new Item { Id = itemId2, ClientId = clientId, Sku = "SKU-02", Description = "Widget B", UnitPrice = 25m });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreatePurchaseOrderCommandHandler(context, currentUser);

        // Act
        var result = await handler.Handle(new CreatePurchaseOrderCommand
        {
            FacilityId = facilityId,
            PoNumber = "PO-2026-0001",
            Lines =
            [
                new CreatePoLineItemDto { LineNumber = 1, ItemId = itemId1, ExpectedQty = 50m, UnitPrice = 10m },
                new CreatePoLineItemDto { LineNumber = 2, ItemId = itemId2, ExpectedQty = 20m, UnitPrice = 25m }
            ]
        }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("PO-2026-0001", result.PoNumber);
        Assert.Equal(PurchaseOrderStatus.Draft, result.Status);
        Assert.Equal(1000m, result.TotalEstimatedCost); // (50 * 10) + (20 * 25) = 500 + 500 = 1000
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(1u, result.Version);
    }

    [Fact]
    public async Task CreatePurchaseOrder_CrossTenantItemInjection_ThrowsForbiddenException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemOfClientB = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientA };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientA, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientA, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Clients.Add(new Client { Id = clientB, CompanyName = "Tenant B", AccountCode = "CL-B" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientA, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            // Item strictly belongs to Client B
            seed.Items.Add(new Item { Id = itemOfClientB, ClientId = clientB, Sku = "SKU-CLIENT-B", Description = "Competitor Item" });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreatePurchaseOrderCommandHandler(context, currentUser);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new CreatePurchaseOrderCommand
        {
            FacilityId = facilityId,
            PoNumber = "PO-ATTACK-01",
            Lines = [new CreatePoLineItemDto { LineNumber = 1, ItemId = itemOfClientB, ExpectedQty = 10m, UnitPrice = 5m }]
        }, CancellationToken.None));

        Assert.Contains("Cross-tenant item injection is strictly prohibited", ex.Message);
    }

    [Fact]
    public async Task CreatePurchaseOrder_WithoutFacilityAccess_ThrowsForbiddenException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var unauthorizedFacilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = unauthorizedFacilityId, FacilityCode = "FAC-LAX1", Name = "Los Angeles Hub" });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-01", Description = "Widget" });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreatePurchaseOrderCommandHandler(context, currentUser);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new CreatePurchaseOrderCommand
        {
            FacilityId = unauthorizedFacilityId,
            PoNumber = "PO-NO-ACCESS",
            Lines = [new CreatePoLineItemDto { LineNumber = 1, ItemId = itemId, ExpectedQty = 10m, UnitPrice = 5m }]
        }, CancellationToken.None));

        Assert.Contains("does not have active authorized access", ex.Message);
    }

    [Fact]
    public async Task CreatePurchaseOrder_DuplicateLineNumbers_ThrowsValidationException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-01", Description = "Widget" });
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreatePurchaseOrderCommandHandler(context, currentUser);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new CreatePurchaseOrderCommand
        {
            FacilityId = facilityId,
            PoNumber = "PO-DUP-LINES",
            Lines =
            [
                new CreatePoLineItemDto { LineNumber = 1, ItemId = itemId, ExpectedQty = 10m, UnitPrice = 5m },
                new CreatePoLineItemDto { LineNumber = 1, ItemId = itemId, ExpectedQty = 20m, UnitPrice = 5m }
            ]
        }, CancellationToken.None));

        Assert.Contains("Duplicate line numbers detected", ex.Errors["Lines"].First());
    }

    [Fact]
    public async Task SubmitPurchaseOrder_TransitionsStatusAndBumpsVersion()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            var item = new Item { Id = itemId, ClientId = clientId, Sku = "SKU-01", Description = "Widget" };
            seed.Items.Add(item);

            var po = new PurchaseOrder
            {
                Id = poId,
                ClientId = clientId,
                FacilityId = facilityId,
                PoNumber = "PO-SUBMIT-TEST",
                Status = PurchaseOrderStatus.Draft,
                Version = 1
            };
            po.Lines.Add(new PurchaseOrderLine { LineNumber = 1, ItemId = itemId, ExpectedQty = 100m, UnitPrice = 12m });
            seed.PurchaseOrders.Add(po);
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new SubmitPurchaseOrderCommandHandler(context, currentUser);

        // Act
        var result = await handler.Handle(new SubmitPurchaseOrderCommand(poId), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PurchaseOrderStatus.Submitted, result.Status);
        Assert.Equal(2u, result.Version); // Version bumped from 1 to 2
    }

    [Fact]
    public async Task CancelPurchaseOrder_FromDraftOrSubmitted_CancelsAndRecordsReason()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = clientId };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), TenantId = clientId, UserType = UserType.Client };

        using (var seed = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, new TestCurrentUserContext { UserType = UserType.Staff }, dbName))
        {
            seed.Clients.Add(new Client { Id = clientId, CompanyName = "Tenant A", AccountCode = "CL-A" });
            seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-ORD1", Name = "Chicago Hub" });
            seed.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facilityId, Status = FacilityAccessStatus.Active });
            var item = new Item { Id = itemId, ClientId = clientId, Sku = "SKU-01", Description = "Widget" };
            seed.Items.Add(item);

            var po = new PurchaseOrder
            {
                Id = poId,
                ClientId = clientId,
                FacilityId = facilityId,
                PoNumber = "PO-CANCEL-TEST",
                Status = PurchaseOrderStatus.Submitted,
                Version = 1
            };
            po.Lines.Add(new PurchaseOrderLine { LineNumber = 1, ItemId = itemId, ExpectedQty = 50m, UnitPrice = 10m, Status = PurchaseOrderLineStatus.Open });
            seed.PurchaseOrders.Add(po);
            await seed.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CancelPurchaseOrderCommandHandler(context, currentUser);

        // Act
        var success = await handler.Handle(new CancelPurchaseOrderCommand(poId, "Supplier out of stock"), CancellationToken.None);

        // Assert
        Assert.True(success);
        var cancelledPo = await context.PurchaseOrders.Include(p => p.Lines).FirstAsync(p => p.Id == poId);
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelledPo.Status);
        Assert.Equal("Supplier out of stock", cancelledPo.CancellationReason);
        Assert.Equal(PurchaseOrderLineStatus.Cancelled, cancelledPo.Lines.First().Status);
        Assert.Equal(2u, cancelledPo.Version);
    }
}

