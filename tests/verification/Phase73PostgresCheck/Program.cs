using Microsoft.EntityFrameworkCore;
using Npgsql;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Inventory.Commands.TransferInventory;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;

var baseConnection = Environment.GetEnvironmentVariable("PHASE73_POSTGRES_CONNECTION")
    ?? throw new InvalidOperationException("Set PHASE73_POSTGRES_CONNECTION to a disposable PostgreSQL server.");
var builder = new NpgsqlConnectionStringBuilder(baseConnection) { Database = "postgres" };
var databaseName = $"phase73_{Guid.NewGuid():N}";

await using var admin = new NpgsqlConnection(builder.ConnectionString);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin).ExecuteNonQueryAsync();

builder.Database = databaseName;
var connectionString = builder.ConnectionString;
try
{
    var clientId = Guid.NewGuid();
    var facilityId = Guid.NewGuid();
    var itemId = Guid.NewGuid();
    var sourceId = Guid.NewGuid();
    var destinationId = Guid.NewGuid();
    var user = new VerificationIdentity(facilityId);

    await using (var seed = CreateDb(connectionString, user))
    {
        await seed.Database.MigrateAsync();
        seed.Clients.Add(new Client { Id = clientId, AccountCode = "P73", CompanyName = "Phase 7.3 Verification" });
        seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "P73", Name = "Phase 7.3 Facility" });
        seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "P73-SKU", Description = "PostgreSQL verification stock" });
        seed.Locations.AddRange(
            new Location { Id = sourceId, FacilityId = facilityId, LocationCode = "P73-SRC", Barcode = "P73-SRC", LocationType = LocationType.Bin, IsActive = true },
            new Location { Id = destinationId, FacilityId = facilityId, LocationCode = "P73-DST", Barcode = "P73-DST", LocationType = LocationType.Bin, IsActive = true });
        seed.StockOnHand.Add(new StockOnHand
        {
            ClientId = clientId, FacilityId = facilityId, ItemId = itemId, LocationId = sourceId,
            QtyAvailable = 10m, Version = 1
        });
        await seed.SaveChangesAsync();
    }

    // Force failure after SQL has been issued inside the transaction.
    await using (var failing = CreateDb(connectionString, user))
    {
        try
        {
            await failing.ExecuteInTransactionAsync<bool>(async () =>
            {
                var stock = await failing.StockOnHand.SingleAsync(s => s.LocationId == sourceId);
                stock.QtyAvailable -= 5m;
                failing.InventoryTransactions.Add(new InventoryTransaction
                {
                    ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
                    FromLocationId = sourceId, ToLocationId = destinationId,
                    TxType = InventoryTxType.Move, Quantity = 5m, ReferenceType = "PHASE73_FORCED_FAILURE",
                    UserId = user.UserId, TxTimestamp = DateTime.UtcNow
                });
                await failing.SaveChangesAsync();
                throw new ForcedRollbackException();
            });
        }
        catch (ForcedRollbackException)
        {
            // Expected: the transaction must be rolled back.
        }
    }

    await using (var verifyRollback = CreateDb(connectionString, user))
    {
        var source = await verifyRollback.StockOnHand.AsNoTracking().SingleAsync(s => s.LocationId == sourceId);
        if (source.QtyAvailable != 10m || await verifyRollback.InventoryTransactions.AnyAsync())
            throw new InvalidOperationException("PostgreSQL rollback did not restore stock and ledger state.");
        Console.WriteLine("PASS PostgreSQL rollback: source=10, destination absent, ledger=0");
    }

    // Retry the operation through the production handler after the failed attempt.
    await using (var retry = CreateDb(connectionString, user))
    {
        var authorization = new WarehouseOperationalAuthorization(retry, user);
        var handler = new TransferInventoryCommandHandler(retry, user, authorization);
        await handler.Handle(new TransferInventoryCommand
        {
            ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
            FromLocationId = sourceId, ToLocationId = destinationId, Quantity = 5m,
            Reason = "Phase 7.3 rollback/retry verification"
        }, CancellationToken.None);
    }

    await using (var verifyRetry = CreateDb(connectionString, user))
    {
        var balances = await verifyRetry.StockOnHand.AsNoTracking().OrderBy(s => s.LocationId).ToListAsync();
        var source = balances.Single(s => s.LocationId == sourceId);
        var destination = balances.Single(s => s.LocationId == destinationId);
        var ledgerCount = await verifyRetry.InventoryTransactions.CountAsync();
        if (source.QtyAvailable != 5m || destination.QtyAvailable != 5m ||
            balances.Sum(s => s.TotalQuantity) != 10m || ledgerCount != 1)
            throw new InvalidOperationException("PostgreSQL retry violated balance conservation or ledger exactly-once behavior.");
        Console.WriteLine("PASS PostgreSQL retry: source=5, destination=5, conserved total=10, ledger=1");
    }
}
finally
{
    NpgsqlConnection.ClearAllPools();
    builder.Database = "postgres";
    await using var cleanup = new NpgsqlConnection(builder.ConnectionString);
    await cleanup.OpenAsync();
    await new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", cleanup).ExecuteNonQueryAsync();
}

static ApplicationDbContext CreateDb(string connectionString, VerificationIdentity user)
{
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention()
        .Options;
    return new ApplicationDbContext(options, new VerificationTenant(), user,
        new AuditableEntityInterceptor(user, new VerificationClock()));
}

sealed class ForcedRollbackException : Exception;
sealed class VerificationTenant : ITenantProvider
{
    public Guid? TenantId => null;
    public bool HasTenant => false;
    public bool BypassTenantFilter { get; set; } = true;
    public void SetTenantId(Guid tenantId) { }
}
sealed class VerificationClock : IDateTimeProvider { public DateTime UtcNow => DateTime.UtcNow; }
sealed class VerificationIdentity(Guid facilityId) : ICurrentUserContext
{
    public Guid? UserId { get; } = Guid.NewGuid();
    public string? Email => "phase73-verification@local";
    public Guid? TenantId => null;
    public Guid? FacilityId => facilityId;
    public Guid? RequestedFacilityId => null;
    public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
    public IReadOnlyList<string> Roles => ["SuperAdmin"];
    public IReadOnlyList<string> Permissions => [];
    public bool IsAuthenticated => true;
    public bool IsStaff => true;
    public bool IsSuperAdmin => true;
    public bool HasPermission(string permission) => true;
    public bool IsInRole(string role) => true;
}
