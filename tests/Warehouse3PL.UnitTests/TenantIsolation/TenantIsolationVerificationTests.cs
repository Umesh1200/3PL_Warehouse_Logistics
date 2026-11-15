using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Features.Clients.Queries.GetClientById;
using Warehouse3PL.Application.Features.Clients.Queries.GetClients;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.TenantIsolation;

public class TenantIsolationVerificationTests
{
    [Fact]
    public async Task GlobalQueryFilter_ShouldOnlyReturnContactsForCurrentTenant()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantProvider = new TestTenantProvider { TenantId = tenantA };
        var currentUser = new TestCurrentUserContext { TenantId = tenantA, UserType = UserType.Client };
        var dbName = Guid.NewGuid().ToString();

        // Seed data using admin context
        var adminTenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        using (var seedContext = TestDbContextFactory.Create(adminTenantProvider, adminUser, dbName))
        {
            seedContext.ClientContacts.AddRange(
                new ClientContact { ClientId = tenantA, FirstName = "Alice", LastName = "TenantA", Email = "alice@a.com" },
                new ClientContact { ClientId = tenantB, FirstName = "Bob", LastName = "TenantB", Email = "bob@b.com" }
            );
            await seedContext.SaveChangesAsync();
        }

        // Act - Query as Tenant A
        using var clientAContext = TestDbContextFactory.Create(tenantProvider, currentUser, dbName);
        var visibleContacts = await clientAContext.ClientContacts.ToListAsync();

        // Assert
        Assert.Single(visibleContacts);
        Assert.Equal(tenantA, visibleContacts[0].ClientId);
        Assert.Equal("Alice", visibleContacts[0].FirstName);
    }

    [Fact]
    public async Task StaffUser_ShouldSeeAllTenantRecords_RegardlessOfTenantId()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var adminTenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seedContext = TestDbContextFactory.Create(adminTenantProvider, adminUser, dbName))
        {
            seedContext.ClientContacts.AddRange(
                new ClientContact { ClientId = tenantA, FirstName = "Alice", LastName = "TenantA", Email = "alice@a.com" },
                new ClientContact { ClientId = tenantB, FirstName = "Bob", LastName = "TenantB", Email = "bob@b.com" }
            );
            await seedContext.SaveChangesAsync();
        }

        // Act - Query as Staff
        using var staffContext = TestDbContextFactory.Create(adminTenantProvider, adminUser, dbName);
        var allContacts = await staffContext.ClientContacts.ToListAsync();

        // Assert
        Assert.Equal(2, allContacts.Count);
    }

    [Fact]
    public async Task GetClientsQuery_AsClientUser_ShouldOnlyReturnOwnClientTenant()
    {
        // Arrange
        var clientAId = Guid.NewGuid();
        var clientBId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var adminTenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seedContext = TestDbContextFactory.Create(adminTenantProvider, adminUser, dbName))
        {
            seedContext.Clients.AddRange(
                new Client { Id = clientAId, CompanyName = "Alpha 3PL Client", AccountCode = "ALPHA-01" },
                new Client { Id = clientBId, CompanyName = "Beta 3PL Client", AccountCode = "BETA-01" }
            );
            await seedContext.SaveChangesAsync();
        }

        // Query as Client A user
        var clientUser = new TestCurrentUserContext { TenantId = clientAId, UserType = UserType.Client };
        var tenantProvider = new TestTenantProvider { TenantId = clientAId };

        using var queryContext = TestDbContextFactory.Create(tenantProvider, clientUser, dbName);
        var handler = new GetClientsQueryHandler(queryContext, clientUser);

        // Act
        var result = await handler.Handle(new GetClientsQuery(), CancellationToken.None);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal(clientAId, result.Items[0].Id);
        Assert.Equal("Alpha 3PL Client", result.Items[0].CompanyName);
    }

    [Fact]
    public async Task GetClientByIdQuery_CrossTenantAccess_ShouldThrowForbiddenException()
    {
        // Arrange
        var clientAId = Guid.NewGuid();
        var clientBId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var adminTenantProvider = new TestTenantProvider { BypassTenantFilter = true };
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };

        using (var seedContext = TestDbContextFactory.Create(adminTenantProvider, adminUser, dbName))
        {
            seedContext.Clients.AddRange(
                new Client { Id = clientAId, CompanyName = "Alpha Client", AccountCode = "ALPHA" },
                new Client { Id = clientBId, CompanyName = "Beta Client", AccountCode = "BETA" }
            );
            await seedContext.SaveChangesAsync();
        }

        // Client A user tries to fetch Client B details
        var clientAUser = new TestCurrentUserContext { TenantId = clientAId, UserType = UserType.Client };
        var clientATenantProvider = new TestTenantProvider { TenantId = clientAId };

        using var queryContext = TestDbContextFactory.Create(clientATenantProvider, clientAUser, dbName);
        var handler = new GetClientByIdQueryHandler(queryContext, clientAUser);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(async () =>
        {
            await handler.Handle(new GetClientByIdQuery(clientBId), CancellationToken.None);
        });
    }
}

