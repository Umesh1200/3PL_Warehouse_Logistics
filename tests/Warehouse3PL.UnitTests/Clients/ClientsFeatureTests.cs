using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Features.Clients.Commands.CreateClient;
using Warehouse3PL.Application.Features.Clients.Commands.DeactivateClient;
using Warehouse3PL.Application.Features.Clients.Commands.UpdateClient;
using Warehouse3PL.Application.Features.Clients.Queries.GetClientById;
using Warehouse3PL.Application.Features.Clients.Queries.GetClients;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.UnitTests.Common;
using Xunit;

namespace Warehouse3PL.UnitTests.Clients;

public class ClientsFeatureTests
{
    [Fact]
    public async Task GetClients_WithPagination_ReturnsRequestedPageAndAccurateTotals()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName))
        {
            for (int i = 1; i <= 5; i++)
            {
                seedContext.Clients.Add(new Client
                {
                    Id = Guid.NewGuid(),
                    CompanyName = $"Client Organization {i:D2}",
                    AccountCode = $"CLNT_{i:D2}",
                    Status = ClientStatus.Active,
                    SlaTier = SlaTier.Standard,
                    Currency = "USD",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-i)
                });
            }
            await seedContext.SaveChangesAsync();
        }

        using var queryContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName);
        var handler = new GetClientsQueryHandler(queryContext, adminUser);

        // Act - Request Page 2, PageSize 2
        var query = new GetClientsQuery
        {
            PageNumber = 2,
            PageSize = 2
        };
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetClients_WithSearchTerm_FiltersCorrectlyAndMaintainsAccurateTotalCount()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName))
        {
            seedContext.Clients.AddRange(
                new Client { Id = Guid.NewGuid(), CompanyName = "Apex Freight International", AccountCode = "APEX_01", Status = ClientStatus.Active, SlaTier = SlaTier.Gold, Currency = "USD" },
                new Client { Id = Guid.NewGuid(), CompanyName = "Beacon Medical Solutions", AccountCode = "BEACON_01", Status = ClientStatus.Active, SlaTier = SlaTier.Platinum, Currency = "USD" },
                new Client { Id = Guid.NewGuid(), CompanyName = "Apex Warehousing Corp", AccountCode = "APEX_02", Status = ClientStatus.Onboarding, SlaTier = SlaTier.Silver, Currency = "CAD" }
            );
            await seedContext.SaveChangesAsync();
        }

        using var queryContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName);
        var handler = new GetClientsQueryHandler(queryContext, adminUser);

        // Act
        var result = await handler.Handle(new GetClientsQuery { SearchTerm = "Apex" }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, c => Assert.Contains("Apex", c.CompanyName));
    }

    [Fact]
    public async Task GetClients_WithServerSideSorting_OrdersByRequestedField()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName))
        {
            seedContext.Clients.AddRange(
                new Client { Id = Guid.NewGuid(), CompanyName = "Zeta Logistics", AccountCode = "ZETA_CORP", Status = ClientStatus.Active, SlaTier = SlaTier.Standard, Currency = "USD" },
                new Client { Id = Guid.NewGuid(), CompanyName = "Alpha Freight", AccountCode = "ALPHA_CORP", Status = ClientStatus.Active, SlaTier = SlaTier.Platinum, Currency = "EUR" },
                new Client { Id = Guid.NewGuid(), CompanyName = "Beta Global", AccountCode = "BETA_CORP", Status = ClientStatus.Active, SlaTier = SlaTier.Gold, Currency = "GBP" }
            );
            await seedContext.SaveChangesAsync();
        }

        using var queryContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName);
        var handler = new GetClientsQueryHandler(queryContext, adminUser);

        // Act 1: Sort by companyName ascending
        var ascResult = await handler.Handle(new GetClientsQuery { SortBy = "companyName", SortDescending = false }, CancellationToken.None);
        Assert.Equal("Alpha Freight", ascResult.Items[0].CompanyName);
        Assert.Equal("Beta Global", ascResult.Items[1].CompanyName);
        Assert.Equal("Zeta Logistics", ascResult.Items[2].CompanyName);

        // Act 2: Sort by companyName descending
        var descResult = await handler.Handle(new GetClientsQuery { SortBy = "companyName", SortDescending = true }, CancellationToken.None);
        Assert.Equal("Zeta Logistics", descResult.Items[0].CompanyName);
        Assert.Equal("Beta Global", descResult.Items[1].CompanyName);
        Assert.Equal("Alpha Freight", descResult.Items[2].CompanyName);

        // Act 3: Sort by accountCode ascending
        var codeResult = await handler.Handle(new GetClientsQuery { SortBy = "accountCode", SortDescending = false }, CancellationToken.None);
        Assert.Equal("ALPHA_CORP", codeResult.Items[0].AccountCode);
        Assert.Equal("BETA_CORP", codeResult.Items[1].AccountCode);
        Assert.Equal("ZETA_CORP", codeResult.Items[2].AccountCode);

        // Act 4: Non-allowlisted sort field safely falls back to default without error
        var fallbackResult = await handler.Handle(new GetClientsQuery { SortBy = "invalid_column_xyz", SortDescending = false }, CancellationToken.None);
        Assert.NotNull(fallbackResult);
        Assert.Equal(3, fallbackResult.Items.Count);
    }

    [Fact]
    public async Task GetClients_NonStaffTenantIsolation_OnlyReturnsOwnTenant()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var clientUser = new TestCurrentUserContext { TenantId = tenantA, UserType = UserType.Client };
        var tenantProvider = new TestTenantProvider { TenantId = tenantA };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, adminUser, dbName))
        {
            seedContext.Clients.AddRange(
                new Client { Id = tenantA, CompanyName = "Tenant A Corp", AccountCode = "TENANT_A", Status = ClientStatus.Active, SlaTier = SlaTier.Standard, Currency = "USD" },
                new Client { Id = tenantB, CompanyName = "Tenant B Corp", AccountCode = "TENANT_B", Status = ClientStatus.Active, SlaTier = SlaTier.Standard, Currency = "USD" }
            );
            await seedContext.SaveChangesAsync();
        }

        using var queryContext = TestDbContextFactory.Create(tenantProvider, clientUser, dbName);
        var handler = new GetClientsQueryHandler(queryContext, clientUser);

        // Act
        var result = await handler.Handle(new GetClientsQuery(), CancellationToken.None);

        // Assert - Client user must only see Tenant A
        Assert.Single(result.Items);
        Assert.Equal(tenantA, result.Items[0].Id);
        Assert.Equal("Tenant A Corp", result.Items[0].CompanyName);
    }

    [Fact]
    public async Task GetClientById_NonStaffForeignTenant_ThrowsForbiddenException()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var clientUser = new TestCurrentUserContext { TenantId = tenantA, UserType = UserType.Client };

        using (var seedContext = TestDbContextFactory.Create(new TestTenantProvider { BypassTenantFilter = true }, adminUser, dbName))
        {
            seedContext.Clients.Add(new Client
            {
                Id = tenantB,
                CompanyName = "Foreign Tenant B",
                AccountCode = "TENANT_B",
                Status = ClientStatus.Active,
                SlaTier = SlaTier.Standard,
                Currency = "USD"
            });
            await seedContext.SaveChangesAsync();
        }

        using var queryContext = TestDbContextFactory.Create(new TestTenantProvider { TenantId = tenantA }, clientUser, dbName);
        var handler = new GetClientByIdQueryHandler(queryContext, clientUser);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetClientByIdQuery(tenantB), CancellationToken.None));
    }

    [Fact]
    public async Task CreateClient_ValidCommand_CreatesClientWithNormalizedCodes()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };

        using var context = TestDbContextFactory.Create(tenantProvider, adminUser, dbName);
        var handler = new CreateClientCommandHandler(context);

        var command = new CreateClientCommand
        {
            CompanyName = "  Quantum Logistics LLC  ",
            AccountCode = "  ql_us_01  ",
            Currency = "  usd  ",
            SlaTier = SlaTier.Gold,
            Status = ClientStatus.Active
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Quantum Logistics LLC", result.CompanyName);
        Assert.Equal("QL_US_01", result.AccountCode);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(SlaTier.Gold, result.SlaTier);
        Assert.Equal(ClientStatus.Active, result.Status);

        // Verify in database
        var inDb = await context.Clients.FindAsync(result.Id);
        Assert.NotNull(inDb);
        Assert.Equal("QL_US_01", inDb.AccountCode);
    }

    [Fact]
    public async Task CreateClient_DuplicateAccountCode_ThrowsValidationException()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName))
        {
            seedContext.Clients.Add(new Client
            {
                Id = Guid.NewGuid(),
                CompanyName = "Existing Client",
                AccountCode = "DUP_CODE_01",
                Status = ClientStatus.Active,
                SlaTier = SlaTier.Standard,
                Currency = "USD"
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, adminUser, dbName);
        var handler = new CreateClientCommandHandler(context);

        var command = new CreateClientCommand
        {
            CompanyName = "New Client With Same Code",
            AccountCode = "dup_code_01", // Should normalize to DUP_CODE_01
            Currency = "USD"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateClient_UpdatesSupportedFields_PreservesImmutableAccountCode()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName))
        {
            seedContext.Clients.Add(new Client
            {
                Id = clientId,
                CompanyName = "Original Company Name",
                AccountCode = "IMMUTABLE_PARTITION_CODE",
                Status = ClientStatus.Onboarding,
                SlaTier = SlaTier.Standard,
                Currency = "USD"
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, adminUser, dbName);
        var handler = new UpdateClientCommandHandler(context, adminUser);

        var command = new UpdateClientCommand
        {
            Id = clientId,
            CompanyName = "Updated Company Name Ltd",
            Status = ClientStatus.Active,
            SlaTier = SlaTier.Platinum,
            Currency = "eur"
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Updated Company Name Ltd", result.CompanyName);
        Assert.Equal("IMMUTABLE_PARTITION_CODE", result.AccountCode);
        Assert.Equal(ClientStatus.Active, result.Status);
        Assert.Equal(SlaTier.Platinum, result.SlaTier);
        Assert.Equal("EUR", result.Currency);

        // Verify in database
        var inDb = await context.Clients.FindAsync(clientId);
        Assert.NotNull(inDb);
        Assert.Equal("Updated Company Name Ltd", inDb.CompanyName);
        Assert.Equal("IMMUTABLE_PARTITION_CODE", inDb.AccountCode);
    }

    [Fact]
    public async Task DeactivateClient_SoftDeletesAndSuspendsClient()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName))
        {
            seedContext.Clients.Add(new Client
            {
                Id = clientId,
                CompanyName = "Client To Deactivate",
                AccountCode = "DEACT_01",
                Status = ClientStatus.Active,
                SlaTier = SlaTier.Standard,
                Currency = "USD",
                IsDeleted = false
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, adminUser, dbName);
        var handler = new DeactivateClientCommandHandler(context, adminUser);

        // Act
        var success = await handler.Handle(new DeactivateClientCommand(clientId), CancellationToken.None);

        // Assert
        Assert.True(success);

        var inDb = await context.Clients.FindAsync(clientId);
        Assert.NotNull(inDb);
        Assert.True(inDb.IsDeleted);
        Assert.Equal(ClientStatus.Suspended, inDb.Status);

        // Soft-deleted client should now be excluded from GetClients query
        var queryHandler = new GetClientsQueryHandler(context, adminUser);
        var queryResult = await queryHandler.Handle(new GetClientsQuery { SearchTerm = "DEACT_01" }, CancellationToken.None);
        Assert.Empty(queryResult.Items);
    }

    [Fact]
    public async Task NonStaff_WithoutTenantContext_FailsClosedOnAllOperations()
    {
        // Arrange
        var clientId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var nonStaffNoTenant = new TestCurrentUserContext
        {
            UserType = UserType.Client,
            TenantId = null // Missing tenant context
        };
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName))
        {
            seedContext.Clients.Add(new Client
            {
                Id = clientId,
                CompanyName = "Isolated Client",
                AccountCode = "ISO_01",
                Status = ClientStatus.Active,
                SlaTier = SlaTier.Standard,
                Currency = "USD"
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, nonStaffNoTenant, dbName);

        // 1. GetClientsQuery must throw ForbiddenException
        var listHandler = new GetClientsQueryHandler(context, nonStaffNoTenant);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            listHandler.Handle(new GetClientsQuery(), CancellationToken.None));

        // 2. GetClientByIdQuery must throw ForbiddenException
        var detailHandler = new GetClientByIdQueryHandler(context, nonStaffNoTenant);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            detailHandler.Handle(new GetClientByIdQuery(clientId), CancellationToken.None));

        // 3. UpdateClientCommand must throw ForbiddenException
        var updateHandler = new UpdateClientCommandHandler(context, nonStaffNoTenant);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            updateHandler.Handle(new UpdateClientCommand { Id = clientId, CompanyName = "Hacked" }, CancellationToken.None));

        // 4. DeactivateClientCommand must throw ForbiddenException (staff only)
        var deactivateHandler = new DeactivateClientCommandHandler(context, nonStaffNoTenant);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            deactivateHandler.Handle(new DeactivateClientCommand(clientId), CancellationToken.None));
    }

    [Fact]
    public async Task NonStaff_CrossTenantMutation_ThrowsForbiddenException()
    {
        // Arrange
        var ownTenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var adminUser = new TestCurrentUserContext { UserType = UserType.Staff };
        var clientUser = new TestCurrentUserContext
        {
            UserType = UserType.Client,
            TenantId = ownTenantId
        };
        var tenantProvider = new TestTenantProvider { BypassTenantFilter = true };

        using (var seedContext = TestDbContextFactory.Create(tenantProvider, adminUser, dbName))
        {
            seedContext.Clients.AddRange(
                new Client { Id = ownTenantId, CompanyName = "Own Tenant", AccountCode = "OWN_01", Status = ClientStatus.Active, SlaTier = SlaTier.Standard, Currency = "USD" },
                new Client { Id = foreignTenantId, CompanyName = "Foreign Tenant", AccountCode = "FOR_01", Status = ClientStatus.Active, SlaTier = SlaTier.Standard, Currency = "USD" }
            );
            await seedContext.SaveChangesAsync();
        }

        using var context = TestDbContextFactory.Create(tenantProvider, clientUser, dbName);

        // Update own tenant should succeed
        var updateHandler = new UpdateClientCommandHandler(context, clientUser);
        var ownUpdateResult = await updateHandler.Handle(new UpdateClientCommand
        {
            Id = ownTenantId,
            CompanyName = "Own Tenant Renovated",
            Currency = "USD",
            SlaTier = SlaTier.Gold,
            Status = ClientStatus.Active
        }, CancellationToken.None);
        Assert.Equal("Own Tenant Renovated", ownUpdateResult.CompanyName);

        // Update foreign tenant must throw ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            updateHandler.Handle(new UpdateClientCommand
            {
                Id = foreignTenantId,
                CompanyName = "Cross Tenant Tampering",
                Currency = "USD"
            }, CancellationToken.None));

        // Deactivate own or foreign tenant must throw ForbiddenException (staff only)
        var deactivateHandler = new DeactivateClientCommandHandler(context, clientUser);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            deactivateHandler.Handle(new DeactivateClientCommand(ownTenantId), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            deactivateHandler.Handle(new DeactivateClientCommand(foreignTenantId), CancellationToken.None));
    }
}
