using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.CompleteReceivingSession;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ConfirmPutawayTask;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
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
    var stagingId = Guid.NewGuid();
    var destinationId = Guid.NewGuid();
    var doorId = Guid.NewGuid();
    var visitId = Guid.NewGuid();
    var sessionId = Guid.NewGuid();
    var taskId = Guid.NewGuid();
    var user = new VerificationIdentity(facilityId);

    await using (var seed = CreateDb(connectionString, user))
    {
        await seed.Database.MigrateAsync();
        seed.Clients.Add(new Client { Id = clientId, AccountCode = "P73", CompanyName = "Phase 7.3 Verification" });
        seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "P73", Name = "Phase 7.3 Facility" });
        seed.Users.Add(new User
        {
            Id = user.UserId!.Value,
            Email = "phase73-verification@local",
            PasswordHash = "verification-only",
            FirstName = "Phase73",
            LastName = "Verifier",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        });
        seed.Items.Add(new Item { Id = itemId, ClientId = clientId, Sku = "P73-SKU", Description = "PostgreSQL verification stock" });
        seed.Locations.AddRange(
            new Location { Id = stagingId, FacilityId = facilityId, LocationCode = "P73-STG", Barcode = "P73-STG", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true },
            new Location { Id = destinationId, FacilityId = facilityId, LocationCode = "P73-DST", Barcode = "P73-DST", LocationType = LocationType.Bin, IsActive = true });
        seed.DockDoors.Add(new DockDoor { Id = doorId, FacilityId = facilityId, DoorNumber = "P73-D1", Status = DockDoorStatus.Active });
        seed.YardVisits.Add(new YardVisit
        {
            Id = visitId, FacilityId = facilityId, ClientId = clientId, VisitNumber = "P73-V1",
            TrailerNumber = "P73-TR1", Status = YardVisitStatus.AtDock
        });
        seed.ReceivingSessions.Add(new ReceivingSession
        {
            Id = sessionId, FacilityId = facilityId, DockDoorId = doorId, YardVisitId = visitId,
            ClientId = clientId, SessionNumber = "P73-RCV1", Status = ReceivingStatus.InProgress,
            ReceivedByUserId = user.UserId!.Value,
            Lines =
            [
                new ReceiptLine
                {
                    ItemId = itemId, Lpn = "P73-LPN1", QuantityReceived = 10m,
                    Condition = ReceiptConditionStatus.Sound, StagingLocationId = stagingId
                }
            ]
        });
        seed.PutawayTasks.Add(new PutawayTask
        {
            Id = taskId, ClientId = clientId, FacilityId = facilityId, ItemId = itemId,
            SourceLocationId = stagingId, SuggestedLocationId = destinationId, TargetLocationId = destinationId,
            LpnCode = "P73-LPN1", Quantity = 10m, Status = PutawayTaskStatus.Pending
        });
        await seed.SaveChangesAsync();
    }

    // Receiving completion: throw after SaveChanges has issued SQL, then verify the outer
    // production transaction rolls lifecycle, stock and ledger back together.
    var receivingFaultExercised = false;
    await using (var failing = CreateDb(connectionString, user, new ThrowAfterSaveInterceptor()))
    {
        try
        {
            var auth = new WarehouseOperationalAuthorization(failing, user);
            await new CompleteReceivingSessionCommandHandler(failing, user, auth).Handle(
                new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
        }
        catch (ForcedRollbackException)
        {
            receivingFaultExercised = true;
        }
    }
    if (!receivingFaultExercised)
        throw new InvalidOperationException("Receiving completion did not trigger the expected ForcedRollbackException fault.");

    await using (var verifyRollback = CreateDb(connectionString, user))
    {
        var session = await verifyRollback.ReceivingSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        if (session.Status != ReceivingStatus.InProgress ||
            await verifyRollback.StockOnHand.AnyAsync() ||
            await verifyRollback.InventoryTransactions.AnyAsync())
            throw new InvalidOperationException("Receiving rollback did not restore lifecycle, stock and ledger state.");
        Console.WriteLine("PASS PostgreSQL receiving rollback: status=InProgress, stock=0, ledger=0");
    }

    // Retry receiving completion through the production handler.
    await using (var retry = CreateDb(connectionString, user))
    {
        var auth = new WarehouseOperationalAuthorization(retry, user);
        await new CompleteReceivingSessionCommandHandler(retry, user, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
    }

    await using (var verifyRetry = CreateDb(connectionString, user))
    {
        var session = await verifyRetry.ReceivingSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        var source = await verifyRetry.StockOnHand.AsNoTracking().SingleAsync(s => s.LocationId == stagingId);
        if (session.Status != ReceivingStatus.Completed || source.QtyAvailable != 10m ||
            await verifyRetry.InventoryTransactions.CountAsync(t => t.ReferenceId == sessionId) != 1)
            throw new InvalidOperationException("Receiving retry did not complete exactly once.");
        Console.WriteLine("PASS PostgreSQL receiving retry: status=Completed, staging=10, receive ledger=1");
    }

    // Duplicate receiving completion: verify repeated submissions cannot produce duplicate inventory or ledger effects.
    await using (var duplicate = CreateDb(connectionString, user))
    {
        var auth = new WarehouseOperationalAuthorization(duplicate, user);
        var duplicateResult = await new CompleteReceivingSessionCommandHandler(duplicate, user, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = sessionId }, CancellationToken.None);
        if (duplicateResult.Status != ReceivingStatus.Completed)
            throw new InvalidOperationException("Duplicate receiving submission returned unexpected status.");
    }

    await using (var verifyDuplicate = CreateDb(connectionString, user))
    {
        var session = await verifyDuplicate.ReceivingSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        var source = await verifyDuplicate.StockOnHand.AsNoTracking().SingleAsync(s => s.LocationId == stagingId);
        if (session.Status != ReceivingStatus.Completed || source.QtyAvailable != 10m ||
            await verifyDuplicate.InventoryTransactions.CountAsync(t => t.ReferenceId == sessionId) != 1)
            throw new InvalidOperationException("Duplicate receiving submission produced unexpected inventory or ledger effects.");
        Console.WriteLine("PASS PostgreSQL duplicate receiving idempotency: status=Completed, staging=10, receive ledger=1");
    }

    // Putaway confirmation: force the same post-save failure and verify rollback.
    var putawayFaultExercised = false;
    await using (var failing = CreateDb(connectionString, user, new ThrowAfterSaveInterceptor()))
    {
        try
        {
            var auth = new WarehouseOperationalAuthorization(failing, user);
            await new ConfirmPutawayTaskCommandHandler(failing, user, auth).Handle(new ConfirmPutawayTaskCommand
            {
                TaskId = taskId, ScannedLpn = "P73-LPN1", ScannedLocationBarcode = "P73-DST"
            }, CancellationToken.None);
        }
        catch (ForcedRollbackException)
        {
            putawayFaultExercised = true;
        }
    }
    if (!putawayFaultExercised)
        throw new InvalidOperationException("Putaway confirmation did not trigger the expected ForcedRollbackException fault.");

    await using (var verifyRollback = CreateDb(connectionString, user))
    {
        var task = await verifyRollback.PutawayTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var source = await verifyRollback.StockOnHand.AsNoTracking().SingleAsync(s => s.LocationId == stagingId);
        if (task.Status != PutawayTaskStatus.Pending || task.ActualLocationId != null ||
            source.QtyAvailable != 10m || await verifyRollback.StockOnHand.AnyAsync(s => s.LocationId == destinationId) ||
            await verifyRollback.InventoryTransactions.CountAsync() != 1)
            throw new InvalidOperationException("Putaway rollback did not restore task, balances and ledger state.");
        Console.WriteLine("PASS PostgreSQL putaway rollback: status=Pending, staging=10, destination absent, ledger=1");
    }

    await using (var retry = CreateDb(connectionString, user))
    {
        var auth = new WarehouseOperationalAuthorization(retry, user);
        await new ConfirmPutawayTaskCommandHandler(retry, user, auth).Handle(new ConfirmPutawayTaskCommand
        {
            TaskId = taskId, ScannedLpn = "P73-LPN1", ScannedLocationBarcode = "P73-DST"
        }, CancellationToken.None);
    }

    await using (var verifyRetry = CreateDb(connectionString, user))
    {
        var task = await verifyRetry.PutawayTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var balances = await verifyRetry.StockOnHand.AsNoTracking().ToListAsync();
        var source = balances.Single(s => s.LocationId == stagingId);
        var destination = balances.Single(s => s.LocationId == destinationId);
        if (task.Status != PutawayTaskStatus.Completed || task.ActualLocationId != destinationId ||
            source.QtyAvailable != 0m || destination.QtyAvailable != 10m ||
            balances.Sum(s => s.TotalQuantity) != 10m ||
            await verifyRetry.InventoryTransactions.CountAsync() != 2 ||
            await verifyRetry.InventoryTransactions.CountAsync(t => t.ReferenceId == taskId) != 1)
            throw new InvalidOperationException("Putaway retry violated lifecycle, conservation or ledger exactly-once behavior.");
        Console.WriteLine("PASS PostgreSQL putaway retry: status=Completed, staging=0, destination=10, conserved total=10, ledgers=2");
    }

    // Duplicate putaway confirmation: verify repeated submissions cannot produce duplicate inventory or ledger effects.
    var duplicatePutawayThrew = false;
    await using (var duplicate = CreateDb(connectionString, user))
    {
        var auth = new WarehouseOperationalAuthorization(duplicate, user);
        try
        {
            await new ConfirmPutawayTaskCommandHandler(duplicate, user, auth).Handle(new ConfirmPutawayTaskCommand
            {
                TaskId = taskId, ScannedLpn = "P73-LPN1", ScannedLocationBarcode = "P73-DST"
            }, CancellationToken.None);
        }
        catch (Warehouse3PL.Domain.Exceptions.ValidationException)
        {
            duplicatePutawayThrew = true;
        }
    }
    if (!duplicatePutawayThrew)
        throw new InvalidOperationException("Duplicate putaway confirmation did not reject already-completed task with ValidationException.");

    await using (var verifyDuplicatePutaway = CreateDb(connectionString, user))
    {
        var task = await verifyDuplicatePutaway.PutawayTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        var balances = await verifyDuplicatePutaway.StockOnHand.AsNoTracking().ToListAsync();
        var source = balances.Single(s => s.LocationId == stagingId);
        var destination = balances.Single(s => s.LocationId == destinationId);
        if (task.Status != PutawayTaskStatus.Completed || task.ActualLocationId != destinationId ||
            source.QtyAvailable != 0m || destination.QtyAvailable != 10m ||
            balances.Sum(s => s.TotalQuantity) != 10m ||
            await verifyDuplicatePutaway.InventoryTransactions.CountAsync() != 2 ||
            await verifyDuplicatePutaway.InventoryTransactions.CountAsync(t => t.ReferenceId == taskId) != 1)
            throw new InvalidOperationException("Duplicate putaway submission corrupted balances or ledger transactions.");
        Console.WriteLine("PASS PostgreSQL duplicate putaway prevention: status=Completed, staging=0, destination=10, conserved total=10, ledgers=2");
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

static ApplicationDbContext CreateDb(string connectionString, VerificationIdentity user, IInterceptor? interceptor = null)
{
    var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention();
    if (interceptor != null) optionsBuilder.AddInterceptors(interceptor);
    return new ApplicationDbContext(optionsBuilder.Options, new VerificationTenant(), user,
        new AuditableEntityInterceptor(user, new VerificationClock()));
}

sealed class ForcedRollbackException : Exception;
sealed class ThrowAfterSaveInterceptor : SaveChangesInterceptor
{
    private int _thrown;
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _thrown, 1) == 0) throw new ForcedRollbackException();
        return ValueTask.FromResult(result);
    }
}
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
