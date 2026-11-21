using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Features.Inventory.Commands.ReserveInventory;
using Warehouse3PL.Application.Features.Inventory.Commands.TransferInventory;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Wms;

public class InventoryLedgerAndBalanceTests
{
    [Fact]
    public async Task TransferInventory_UpdatesBalancesAndCreatesImmutableLedgerEntry()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var fromLocId = Guid.NewGuid();
        var toLocId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserId = Guid.NewGuid(), UserType = UserType.Staff, FacilityId = facilityId };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seedContext.Clients.Add(new Client { Id = clientId, CompanyName = "Apex Logistics", AccountCode = "APX" });
            seedContext.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "FAC-1", Name = "Central" });
            seedContext.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "SKU-TRANSFER", Description = "Test" });
            seedContext.Locations.AddRange(
                new Location { Id = fromLocId, FacilityId = facilityId, LocationCode = "LOC-A", Barcode = "BAR-A" },
                new Location { Id = toLocId, FacilityId = facilityId, LocationCode = "LOC-B", Barcode = "BAR-B" }
            );

            seedContext.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = fromLocId,
                QtyAvailable = 50m,
                Version = 1
            });

            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new TransferInventoryCommandHandler(context, currentUser,
            new Warehouse3PL.Application.Common.Security.WarehouseOperationalAuthorization(context, currentUser));

        // Act - Transfer 20 units from LOC-A to LOC-B
        var txId = await handler.Handle(new TransferInventoryCommand
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ItemId = itemId,
            FromLocationId = fromLocId,
            ToLocationId = toLocId,
            Quantity = 20m,
            Reason = "Replenishment move"
        }, CancellationToken.None);

        // Assert - Balances
        var fromStock = await context.StockOnHand.FirstAsync(s => s.LocationId == fromLocId && s.ItemId == itemId);
        var toStock = await context.StockOnHand.FirstAsync(s => s.LocationId == toLocId && s.ItemId == itemId);

        Assert.Equal(30m, fromStock.QtyAvailable);
        Assert.Equal(20m, toStock.QtyAvailable);
        Assert.True(fromStock.Version > 1);

        // Assert - Immutable Ledger Entry
        var ledgerEntry = await context.InventoryTransactions.FirstOrDefaultAsync(t => t.Id == txId);
        Assert.NotNull(ledgerEntry);
        Assert.Equal(InventoryTxType.Move, ledgerEntry.TxType);
        Assert.Equal(20m, ledgerEntry.Quantity);
        Assert.Equal(fromLocId, ledgerEntry.FromLocationId);
        Assert.Equal(toLocId, ledgerEntry.ToLocationId);
        Assert.Equal(currentUser.UserId, ledgerEntry.UserId);
    }

    [Fact]
    public async Task TransferInventory_WhenInsufficientStock_ThrowsValidationException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var fromLocId = Guid.NewGuid();
        var toLocId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, FacilityId = facilityId };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seedContext.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = fromLocId,
                QtyAvailable = 5m // Only 5 available
            });
            seedContext.Locations.AddRange(
                new Location { Id = fromLocId, FacilityId = facilityId, LocationCode = "LOC-A", Barcode = "BAR-A" },
                new Location { Id = toLocId, FacilityId = facilityId, LocationCode = "LOC-B", Barcode = "BAR-B" }
            );
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new TransferInventoryCommandHandler(context, currentUser,
            new Warehouse3PL.Application.Common.Security.WarehouseOperationalAuthorization(context, currentUser));

        // Act & Assert - Attempt to move 10 units
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new TransferInventoryCommand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                FromLocationId = fromLocId,
                ToLocationId = toLocId,
                Quantity = 10m
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task ReserveInventory_TransitionsAvailableToReserved_WithConcurrencyTokenIncrement()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var locId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff, FacilityId = facilityId };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seedContext.StockOnHand.Add(new StockOnHand
            {
                ClientId = clientId,
                FacilityId = facilityId,
                ItemId = itemId,
                LocationId = locId,
                QtyAvailable = 100m,
                QtyReserved = 0m,
                Version = 1
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new ReserveInventoryCommandHandler(context, currentUser,
            new Warehouse3PL.Application.Common.Security.WarehouseOperationalAuthorization(context, currentUser));

        // Act - Reserve 35 units for an order
        var success = await handler.Handle(new ReserveInventoryCommand
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ItemId = itemId,
            LocationId = locId,
            Quantity = 35m,
            ReferenceType = "ORDER",
            ReferenceId = Guid.NewGuid()
        }, CancellationToken.None);

        // Assert
        Assert.True(success);
        var stock = await context.StockOnHand.FirstAsync(s => s.LocationId == locId && s.ItemId == itemId);
        Assert.Equal(65m, stock.QtyAvailable);
        Assert.Equal(35m, stock.QtyReserved);
        Assert.Equal(100m, stock.TotalQuantity); // Total remains 100
        Assert.Equal(2u, stock.Version); // Concurrency token bumped
    }
}

