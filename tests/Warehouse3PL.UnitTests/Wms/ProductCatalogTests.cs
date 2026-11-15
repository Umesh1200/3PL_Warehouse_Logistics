using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Features.Items.Commands.CreateItem;
using Warehouse3PL.Application.Features.Items.Queries.GetItems;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;
using Xunit;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

namespace Warehouse3PL.UnitTests.Wms;

public class ProductCatalogTests
{
    [Fact]
    public async Task CreateItem_SameSkuAcrossDifferentTenants_ShouldSucceed()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seedContext.Clients.AddRange(
                new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CL-A" },
                new Client { Id = clientB, CompanyName = "Client B", AccountCode = "CL-B" }
            );
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreateItemCommandHandler(context, tenantProvider, currentUser);

        // Act - Both tenants create the exact same SKU "SKU-CROSS-01"
        var itemA = await handler.Handle(new CreateItemCommand
        {
            ClientId = clientA,
            Sku = "SKU-CROSS-01",
            Description = "Client A Item",
            WeightKg = 2.5m,
            LengthCm = 20,
            WidthCm = 15,
            HeightCm = 10,
            PrimaryBarcode = "123456789012"
        }, CancellationToken.None);

        var itemB = await handler.Handle(new CreateItemCommand
        {
            ClientId = clientB,
            Sku = "SKU-CROSS-01",
            Description = "Client B Item",
            WeightKg = 1.0m,
            LengthCm = 10,
            WidthCm = 10,
            HeightCm = 10,
            PrimaryBarcode = "987654321098"
        }, CancellationToken.None);

        // Assert
        Assert.NotNull(itemA);
        Assert.NotNull(itemB);
        Assert.Equal(clientA, itemA.ClientId);
        Assert.Equal(clientB, itemB.ClientId);
        Assert.Equal("SKU-CROSS-01", itemA.Sku);
        Assert.Equal("SKU-CROSS-01", itemB.Sku);
    }

    [Fact]
    public async Task CreateItem_DuplicateSkuWithinSameTenant_ThrowsValidationException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientA = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seedContext.Clients.Add(new Client { Id = clientA, CompanyName = "Client A", AccountCode = "CL-A" });
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreateItemCommandHandler(context, tenantProvider, currentUser);

        await handler.Handle(new CreateItemCommand
        {
            ClientId = clientA,
            Sku = "UNIQUE-SKU",
            Description = "First creation",
            WeightKg = 1m,
            LengthCm = 10,
            WidthCm = 10,
            HeightCm = 10
        }, CancellationToken.None);

        // Act & Assert - Duplicate within same tenant fails
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await handler.Handle(new CreateItemCommand
            {
                ClientId = clientA,
                Sku = "UNIQUE-SKU",
                Description = "Duplicate SKU attempt",
                WeightKg = 1m,
                LengthCm = 10,
                WidthCm = 10,
                HeightCm = 10
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task CreateItem_AutomaticallyCreatesEachPackagingUnitAndBarcode()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var currentUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName))
        {
            seedContext.Clients.Add(new Client { Id = clientId, CompanyName = "Client A", AccountCode = "CL-A" });
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var handler = new CreateItemCommandHandler(context, tenantProvider, currentUser);

        // Act
        var item = await handler.Handle(new CreateItemCommand
        {
            ClientId = clientId,
            Sku = "COFFEE-BEANS-1KG",
            Description = "Arabica Coffee 1kg bag",
            WeightKg = 1.05m,
            LengthCm = 25,
            WidthCm = 15,
            HeightCm = 8,
            PrimaryBarcode = "7612345678901",
            PrimaryBarcodeSymbology = BarcodeSymbology.EAN_13
        }, CancellationToken.None);

        // Assert
        Assert.Single(item.Barcodes);
        Assert.Equal("7612345678901", item.Barcodes[0]);
        Assert.Single(item.PackagingUnits);
        Assert.Equal(PackagingUnitLevel.Each, item.PackagingUnits[0].UnitLevel);
        Assert.Equal(1m, item.PackagingUnits[0].ConversionQty);
        Assert.Equal(3000m, item.VolumeCm3); // 25 * 15 * 8
    }
}

