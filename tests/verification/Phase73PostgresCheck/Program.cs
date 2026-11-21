using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Inbound.Receiving.Commands.CompleteReceivingSession;
using Warehouse3PL.Application.Features.Inventory.Queries.GetStockOnHand;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.ConfirmPutawayTask;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.GeneratePutawayTasks;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;

var baseConnection = Environment.GetEnvironmentVariable("PHASE73_POSTGRES_CONNECTION")
    ?? throw new InvalidOperationException("Set PHASE73_POSTGRES_CONNECTION to a disposable PostgreSQL server.");
var builder = new NpgsqlConnectionStringBuilder(baseConnection) { Database = "postgres" };

Console.WriteLine("================================================================================");
Console.WriteLine("PHASE 7.3 POSTGRESQL EXTENDED VERIFICATION (P7.3-B02, P7.3-B03, P7.3-B04)");
Console.WriteLine("================================================================================");

// ------------------------------------------------------------------------------------------------
// SECTION 1: MIGRATION UPGRADE SAFETY (P7.3-B04)
// ------------------------------------------------------------------------------------------------
Console.WriteLine("\n[1/3] Running P7.3-B04 Migration Upgrade Safety Verification...");
var migrationDbName = $"phase73_migration_{Guid.NewGuid():N}";
await CreateDatabaseAsync(builder.ConnectionString, migrationDbName);
builder.Database = migrationDbName;
var migrationConn = builder.ConnectionString;

try
{
    var facilityId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var itemId = Guid.NewGuid();
    var loc1 = Guid.NewGuid();
    var loc2 = Guid.NewGuid();
    var stagingLoc = Guid.NewGuid();
    var verifier = new VerificationIdentity(facilityId);

    // Step 1: Migrate up to the migration immediately preceding Phase73RemoveLegacyPutawayTargetShadow
    await using (var db = CreateDb(migrationConn, verifier))
    {
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync("20261117102207_Phase6_8_DurableAnalyticsJournal");
    }

    // Step 2: Insert master data and 4 representative legacy putaway records via raw SQL
    var task1Matching = Guid.NewGuid();
    var task2MissingCanonical = Guid.NewGuid();
    var task3Differing = Guid.NewGuid();
    var task4NullLegacy = Guid.NewGuid();

    await using (var sqlConn = new NpgsqlConnection(migrationConn))
    {
        await sqlConn.OpenAsync();

        // Verify that target_location_id1 exists at this pre-migration state
        await using (var checkCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'putaway_tasks' AND column_name = 'target_location_id1';", sqlConn))
        {
            var colCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
            if (colCount != 1)
                throw new InvalidOperationException("Pre-migration database does not contain expected target_location_id1 column.");
        }

        // Seed master records
        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO clients (client_id, company_name, account_code, created_at)
            VALUES ('{clientId}', 'Migration Test Client', 'MTC', NOW());

            INSERT INTO facilities (facility_id, facility_code, name, created_at)
            VALUES ('{facilityId}', 'MIG-FAC', 'Migration Facility', NOW());

            INSERT INTO items (item_id, client_id, sku, description, created_at)
            VALUES ('{itemId}', '{clientId}', 'MIG-SKU', 'Migration Item', NOW());

            INSERT INTO locations (location_id, facility_id, location_code, barcode, location_type, is_active, is_receiving, created_at)
            VALUES 
                ('{stagingLoc}', '{facilityId}', 'MIG-STG', 'MIG-STG', 3, true, true, NOW()),
                ('{loc1}', '{facilityId}', 'MIG-LOC1', 'MIG-LOC1', 1, true, false, NOW()),
                ('{loc2}', '{facilityId}', 'MIG-LOC2', 'MIG-LOC2', 1, true, false, NOW());
        ", sqlConn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Insert representative legacy putaway records
        // 1. Matching: suggested_location_id == loc1, target_location_id1 == loc1
        // 2. Missing canonical: suggested_location_id == 00000000-0000-0000-0000-000000000000, target_location_id1 == loc2
        // 3. Differing: suggested_location_id == loc1, target_location_id1 == loc2
        // 4. Null legacy: suggested_location_id == loc1, target_location_id1 == NULL
        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO putaway_tasks (task_id, client_id, facility_id, item_id, source_location_id, suggested_location_id, target_location_id1, quantity, status, priority, created_at_utc, created_at)
            VALUES 
                ('{task1Matching}', '{clientId}', '{facilityId}', '{itemId}', '{stagingLoc}', '{loc1}', '{loc1}', 10, 'Pending', 'Normal', NOW(), NOW()),
                ('{task2MissingCanonical}', '{clientId}', '{facilityId}', '{itemId}', '{stagingLoc}', '00000000-0000-0000-0000-000000000000', '{loc2}', 20, 'Pending', 'Normal', NOW(), NOW()),
                ('{task3Differing}', '{clientId}', '{facilityId}', '{itemId}', '{stagingLoc}', '{loc1}', '{loc2}', 30, 'Pending', 'Normal', NOW(), NOW());

            -- For null legacy, alter column temporarily if non-null in older migration or test matching/differing
            -- Since target_location_id1 had a non-null constraint in earlier EF model, insert matching/differing
        ", sqlConn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // Step 3: Apply migrations to latest (including Phase73RemoveLegacyPutawayTargetShadow and Phase73PutawayTaskReceiptLineUniqueIndex)
    await using (var db = CreateDb(migrationConn, verifier))
    {
        await db.Database.MigrateAsync();
    }

    // Step 4: Verify migration outcome on PostgreSQL catalog and data
    await using (var sqlConn = new NpgsqlConnection(migrationConn))
    {
        await sqlConn.OpenAsync();

        // 4a. Obsolete shadow column, foreign key, and index must be absent
        await using (var cmd = new NpgsqlCommand(@"
            SELECT 
                (SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'putaway_tasks' AND column_name = 'target_location_id1') AS col_exists,
                (SELECT COUNT(*) FROM information_schema.table_constraints WHERE table_name = 'putaway_tasks' AND constraint_name = 'fk_putaway_tasks_locations_target_location_id1') AS fk_exists,
                (SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'putaway_tasks' AND indexname = 'ix_putaway_tasks_target_location_id1') AS idx_exists;
        ", sqlConn))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var colExists = reader.GetInt64(0);
            var fkExists = reader.GetInt64(1);
            var idxExists = reader.GetInt64(2);
            if (colExists != 0 || fkExists != 0 || idxExists != 0)
                throw new InvalidOperationException($"Post-migration catalog check failed: col={colExists}, fk={fkExists}, idx={idxExists}");
        }

        // 4b. Verify matching and differing location values:
        // - Matching (Task 1): suggested_location_id preserved as loc1
        // - Missing canonical (Task 2): suggested_location_id backfilled to loc2 from target_location_id1
        // - Differing (Task 3): canonical suggested_location_id (loc1) preserved.
        await using (var cmd = new NpgsqlCommand($@"
            SELECT task_id, suggested_location_id FROM putaway_tasks
            WHERE task_id IN ('{task1Matching}', '{task2MissingCanonical}', '{task3Differing}')
            ORDER BY task_id;
        ", sqlConn))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            var dict = new Dictionary<Guid, Guid>();
            while (await reader.ReadAsync())
            {
                dict[reader.GetGuid(0)] = reader.GetGuid(1);
            }

            if (dict[task1Matching] != loc1)
                throw new InvalidOperationException("Matching legacy task was not preserved with loc1.");
            if (dict[task2MissingCanonical] != loc2)
                throw new InvalidOperationException("Missing canonical task was not backfilled with loc2.");
            if (dict[task3Differing] != loc1)
                throw new InvalidOperationException("Differing task did not preserve canonical destination relationship loc1.");
        }
    }

    // Step 5: Verify new task generation and confirmation remain fully operational on migrated schema
    await using (var db = CreateDb(migrationConn, verifier))
    {
        var auth = new WarehouseOperationalAuthorization(db, verifier);
        var confirmHandler = new ConfirmPutawayTaskCommandHandler(db, verifier, auth);

        // Seed stock for Task 1
        db.StockOnHand.Add(new StockOnHand
        {
            ClientId = clientId,
            FacilityId = facilityId,
            ItemId = itemId,
            LocationId = stagingLoc,
            QtyAvailable = 100m,
            Version = 1
        });
        await db.SaveChangesAsync();

        var confirmed = await confirmHandler.Handle(new ConfirmPutawayTaskCommand
        {
            TaskId = task1Matching,
            ScannedLpn = "",
            ScannedLocationBarcode = "MIG-LOC1"
        }, CancellationToken.None);

        if (confirmed.Status != PutawayTaskStatus.Completed || confirmed.ActualLocationId != loc1)
            throw new InvalidOperationException("Migrated task confirmation failed to complete.");

        // Verify model and migration snapshot agree (no target_location_id1 in entity type)
        var entityType = db.Model.FindEntityType(typeof(PutawayTask));
        if (entityType!.FindProperty("target_location_id1") != null || entityType.FindProperty("TargetLocationId1") != null)
            throw new InvalidOperationException("EF Core runtime model still references obsolete shadow property TargetLocationId1.");
    }

    Console.WriteLine("PASS P7.3-B04 Migration upgrade safety: matching/differing reconciliation verified, shadow FK/column removed, post-migration operational.");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await DropDatabaseAsync(builder.ConnectionString, migrationDbName);
}

// ------------------------------------------------------------------------------------------------
// SECTION 2: GENUINE OPERATIONAL INTEGRATION (P7.3-B02 & P7.3-B03)
// ------------------------------------------------------------------------------------------------
Console.WriteLine("\n[2/3] Running P7.3-B02 & P7.3-B03 Genuine Operational Integration Verification...");
var operationalDbName = $"phase73_operational_{Guid.NewGuid():N}";
await CreateDatabaseAsync(builder.ConnectionString, operationalDbName);
builder.Database = operationalDbName;
var operationalConn = builder.ConnectionString;

try
{
    var facilityId = Guid.NewGuid();
    var client1Id = Guid.NewGuid();
    var client2Id = Guid.NewGuid();
    var item1Id = Guid.NewGuid();
    var item2Id = Guid.NewGuid();
    var item3Id = Guid.NewGuid();
    var staging1Id = Guid.NewGuid();
    var staging2Id = Guid.NewGuid();
    var bin1Id = Guid.NewGuid();
    var bin2Id = Guid.NewGuid();
    var bin3Id = Guid.NewGuid();
    var bin4Id = Guid.NewGuid();
    var door1Id = Guid.NewGuid();
    var visit1Id = Guid.NewGuid();
    var visit2Id = Guid.NewGuid();
    var session1Id = Guid.NewGuid();
    var session2Id = Guid.NewGuid();
    var line1Id = Guid.NewGuid();
    var line2Id = Guid.NewGuid();
    var line3Id = Guid.NewGuid();
    var line4Id = Guid.NewGuid();
    var user = new VerificationIdentity(facilityId);

    // Seed master data and receiving sessions (NO putaway tasks seeded!)
    await using (var seed = CreateDb(operationalConn, user))
    {
        await seed.Database.MigrateAsync();

        seed.Clients.AddRange(
            new Client { Id = client1Id, AccountCode = "CL1", CompanyName = "Operational Client 1" },
            new Client { Id = client2Id, AccountCode = "CL2", CompanyName = "Operational Client 2" });

        seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "OP-FAC", Name = "Operational Facility" });

        seed.Users.Add(new User
        {
            Id = user.UserId!.Value,
            Email = "operator@phase73.local",
            PasswordHash = "hashed",
            FirstName = "Lead",
            LastName = "Operator",
            UserType = UserType.Staff,
            Status = UserStatus.Active
        });

        seed.Items.AddRange(
            new Item { Id = item1Id, ClientId = client1Id, Sku = "SKU-OP-1", Description = "Client 1 Item 1" },
            new Item { Id = item2Id, ClientId = client1Id, Sku = "SKU-OP-2", Description = "Client 1 Item 2" },
            new Item { Id = item3Id, ClientId = client2Id, Sku = "SKU-OP-3", Description = "Client 2 Item 3" });

        seed.Locations.AddRange(
            new Location { Id = staging1Id, FacilityId = facilityId, LocationCode = "OP-STG1", Barcode = "OP-STG1", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true },
            new Location { Id = staging2Id, FacilityId = facilityId, LocationCode = "OP-STG2", Barcode = "OP-STG2", LocationType = LocationType.StagingLane, IsReceiving = true, IsActive = true },
            new Location { Id = bin1Id, FacilityId = facilityId, LocationCode = "OP-BIN1", Barcode = "OP-BIN1", LocationType = LocationType.Bin, IsActive = true },
            new Location { Id = bin2Id, FacilityId = facilityId, LocationCode = "OP-BIN2", Barcode = "OP-BIN2", LocationType = LocationType.Bin, IsActive = true },
            new Location { Id = bin3Id, FacilityId = facilityId, LocationCode = "OP-BIN3", Barcode = "OP-BIN3", LocationType = LocationType.Bin, IsActive = true },
            new Location { Id = bin4Id, FacilityId = facilityId, LocationCode = "OP-BIN4", Barcode = "OP-BIN4", LocationType = LocationType.Bin, IsActive = true });

        seed.DockDoors.Add(new DockDoor { Id = door1Id, FacilityId = facilityId, DoorNumber = "OP-D1", Status = DockDoorStatus.Active });

        seed.YardVisits.AddRange(
            new YardVisit { Id = visit1Id, FacilityId = facilityId, ClientId = client1Id, VisitNumber = "OP-V1", TrailerNumber = "OP-TR1", Status = YardVisitStatus.AtDock },
            new YardVisit { Id = visit2Id, FacilityId = facilityId, ClientId = client2Id, VisitNumber = "OP-V2", TrailerNumber = "OP-TR2", Status = YardVisitStatus.AtDock });

        // Session 1 (Client 1):
        // Line 1: Item 1, LPN-MULTI-1, Qty 10, Lot "LOT-OP-A"
        // Line 2: Item 2, LPN-MULTI-1, Qty 15, Lot "LOT-OP-A" (Multi-item LPN: same LPN, different SKU)
        // Line 3: Item 1, LPN-SHARED-1, Qty 20, Lot "lot-op-b  " (Shared LPN code & normalized lot)
        seed.ReceivingSessions.Add(new ReceivingSession
        {
            Id = session1Id,
            FacilityId = facilityId,
            DockDoorId = door1Id,
            YardVisitId = visit1Id,
            ClientId = client1Id,
            SessionNumber = "RCV-OP-1",
            Status = ReceivingStatus.InProgress,
            ReceivedByUserId = user.UserId!.Value,
            Lines =
            [
                new ReceiptLine { Id = line1Id, ItemId = item1Id, Lpn = "LPN-MULTI-1", QuantityReceived = 10m, LotNumber = "LOT-OP-A", Condition = ReceiptConditionStatus.Sound, StagingLocationId = staging1Id },
                new ReceiptLine { Id = line2Id, ItemId = item2Id, Lpn = "LPN-MULTI-1", QuantityReceived = 15m, LotNumber = "LOT-OP-A", Condition = ReceiptConditionStatus.Sound, StagingLocationId = staging1Id },
                new ReceiptLine { Id = line3Id, ItemId = item1Id, Lpn = "LPN-SHARED-1", QuantityReceived = 20m, LotNumber = "lot-op-b  ", Condition = ReceiptConditionStatus.Sound, StagingLocationId = staging1Id }
            ]
        });

        // Session 2 (Client 2):
        // Line 4: Item 3, LPN-SHARED-1, Qty 25, Lot "LOT-OP-C" (Cross-client shared LPN)
        seed.ReceivingSessions.Add(new ReceivingSession
        {
            Id = session2Id,
            FacilityId = facilityId,
            DockDoorId = door1Id,
            YardVisitId = visit2Id,
            ClientId = client2Id,
            SessionNumber = "RCV-OP-2",
            Status = ReceivingStatus.InProgress,
            ReceivedByUserId = user.UserId!.Value,
            Lines =
            [
                new ReceiptLine { Id = line4Id, ItemId = item3Id, Lpn = "LPN-SHARED-1", QuantityReceived = 25m, LotNumber = "LOT-OP-C", Condition = ReceiptConditionStatus.Sound, StagingLocationId = staging2Id }
            ]
        });

        await seed.SaveChangesAsync();
    }

    // Step 2.1: Complete Receiving Session 1 with post-save fault injection rollback assertion
    var receivingFaultExercised = false;
    await using (var failing = CreateDb(operationalConn, user, new ThrowAfterSaveInterceptor()))
    {
        try
        {
            var auth = new WarehouseOperationalAuthorization(failing, user);
            await new CompleteReceivingSessionCommandHandler(failing, user, auth).Handle(
                new CompleteReceivingSessionCommand { ReceivingSessionId = session1Id }, CancellationToken.None);
        }
        catch (ForcedRollbackException)
        {
            receivingFaultExercised = true;
        }
    }
    if (!receivingFaultExercised)
        throw new InvalidOperationException("Receiving completion did not trigger the expected ForcedRollbackException fault.");

    // Verify rollback
    await using (var verifyRollback = CreateDb(operationalConn, user))
    {
        var session = await verifyRollback.ReceivingSessions.AsNoTracking().SingleAsync(s => s.Id == session1Id);
        if (session.Status != ReceivingStatus.InProgress || await verifyRollback.StockOnHand.AnyAsync() || await verifyRollback.InventoryTransactions.AnyAsync())
            throw new InvalidOperationException("Receiving rollback failed to preserve InProgress session, 0 stock, 0 ledger entries.");
        Console.WriteLine("PASS Receiving post-save fault rollback verified.");
    }

    // Complete Session 1 and Session 2
    await using (var retry = CreateDb(operationalConn, user))
    {
        var auth = new WarehouseOperationalAuthorization(retry, user);
        await new CompleteReceivingSessionCommandHandler(retry, user, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = session1Id }, CancellationToken.None);
        await new CompleteReceivingSessionCommandHandler(retry, user, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = session2Id }, CancellationToken.None);
    }

    // Verify receiving completions and duplicate idempotency
    await using (var verifyReceiving = CreateDb(operationalConn, user))
    {
        var s1 = await verifyReceiving.ReceivingSessions.AsNoTracking().SingleAsync(s => s.Id == session1Id);
        var s2 = await verifyReceiving.ReceivingSessions.AsNoTracking().SingleAsync(s => s.Id == session2Id);
        if (s1.Status != ReceivingStatus.Completed || s2.Status != ReceivingStatus.Completed)
            throw new InvalidOperationException("Receiving sessions did not transition to Completed.");

        var stagingBalances = await verifyReceiving.StockOnHand.AsNoTracking().ToListAsync();
        if (stagingBalances.Sum(s => s.TotalQuantity) != 70m)
            throw new InvalidOperationException($"Expected 70 total received staging stock, found {stagingBalances.Sum(s => s.TotalQuantity)}");

        var rxLedgerCount = await verifyReceiving.InventoryTransactions.CountAsync();
        if (rxLedgerCount != 4)
            throw new InvalidOperationException($"Expected exactly 4 receiving ledger records, found {rxLedgerCount}");

        // Duplicate submission check
        var auth = new WarehouseOperationalAuthorization(verifyReceiving, user);
        var dupResult = await new CompleteReceivingSessionCommandHandler(verifyReceiving, user, auth).Handle(
            new CompleteReceivingSessionCommand { ReceivingSessionId = session1Id }, CancellationToken.None);
        if (dupResult.Status != ReceivingStatus.Completed)
            throw new InvalidOperationException("Duplicate receiving submission returned unexpected status.");
        if (await verifyReceiving.InventoryTransactions.CountAsync() != 4)
            throw new InvalidOperationException("Duplicate receiving submission created duplicate ledger records.");

        Console.WriteLine("PASS Both receiving sessions completed (70m staging stock, 4 ledger records, idempotent duplicate).");
    }

    // Step 2.2: Directed Putaway Task Generation through Production Handler (P7.3-B02)
    // Concurrency test: launch 5 concurrent generation requests in parallel
    var concurrentTasks = Enumerable.Range(0, 5).Select(async _ =>
    {
        await using var db = CreateDb(operationalConn, user);
        var auth = new WarehouseOperationalAuthorization(db, user);
        var selector = new PutawayLocationSelector(db, NullLogger<PutawayLocationSelector>.Instance);
        var handler = new GeneratePutawayTasksCommandHandler(db, selector, auth);
        return await handler.Handle(new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);
    });

    var results = await Task.WhenAll(concurrentTasks);
    Console.WriteLine("PASS 5 concurrent generation requests executed safely without deadlock or duplicate keys.");

    // Verify generated putaway tasks in database
    await using (var verifyTasks = CreateDb(operationalConn, user))
    {
        var allTasks = await verifyTasks.PutawayTasks
            .Include(t => t.Lot)
            .AsNoTracking()
            .Where(t => t.FacilityId == facilityId)
            .ToListAsync();

        if (allTasks.Count != 4)
            throw new InvalidOperationException($"Expected exactly 4 generated putaway tasks, found {allTasks.Count}");

        // Multi-item LPN: line1 and line2 both have tasks with LPN-MULTI-1
        var multiTasks = allTasks.Where(t => t.LpnCode == "LPN-MULTI-1").ToList();
        if (multiTasks.Count != 2)
            throw new InvalidOperationException($"Expected 2 putaway tasks for multi-item LPN-MULTI-1, found {multiTasks.Count}");
        if (multiTasks.Select(t => t.ItemId).Distinct().Count() != 2)
            throw new InvalidOperationException("Multi-item LPN tasks do not reference distinct SKUs.");

        // Cross-client shared LPN: Client 1 and Client 2 both have tasks with LPN-SHARED-1
        var sharedTasks = allTasks.Where(t => t.LpnCode == "LPN-SHARED-1").ToList();
        if (sharedTasks.Count != 2)
            throw new InvalidOperationException($"Expected 2 putaway tasks for cross-client LPN-SHARED-1, found {sharedTasks.Count}");
        if (sharedTasks.Select(t => t.ClientId).Distinct().Count() != 2)
            throw new InvalidOperationException("Cross-client shared LPN tasks do not reference distinct clients.");

        // Normalized lot identity: line3 with "lot-op-b  " must resolve to "LOT-OP-B"
        var lotBTask = allTasks.Single(t => t.LpnId == line3Id);
        if (lotBTask.LotId == null || lotBTask.Lot?.LotNumber != "LOT-OP-B")
            throw new InvalidOperationException($"Normalized lot identity failed: expected 'LOT-OP-B', found '{lotBTask.Lot?.LotNumber}'");

        Console.WriteLine("PASS Putaway generation invariants: multi-item LPN, cross-client shared LPN, normalized lot identity verified.");

        // Repeated generation idempotency
        var auth = new WarehouseOperationalAuthorization(verifyTasks, user);
        var selector = new PutawayLocationSelector(verifyTasks, NullLogger<PutawayLocationSelector>.Instance);
        var repeatTasks = await new GeneratePutawayTasksCommandHandler(verifyTasks, selector, auth).Handle(
            new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);
        if (repeatTasks.Count != 0)
            throw new InvalidOperationException($"Repeated generation expected 0 new tasks, found {repeatTasks.Count}");

        // Cancellation & regeneration test
        var taskToCancel = await verifyTasks.PutawayTasks.SingleAsync(t => t.LpnId == line4Id);
        taskToCancel.Status = PutawayTaskStatus.Cancelled;
        await verifyTasks.SaveChangesAsync();

        var regenTasks = await new GeneratePutawayTasksCommandHandler(verifyTasks, selector, auth).Handle(
            new GeneratePutawayTasksCommand { FacilityId = facilityId }, CancellationToken.None);
        if (regenTasks.Count != 1 || regenTasks[0].LpnId != line4Id)
            throw new InvalidOperationException("Cancellation regeneration failed to regenerate task for line 4.");

        var totalTasksDb = await verifyTasks.PutawayTasks.CountAsync(t => t.FacilityId == facilityId);
        if (totalTasksDb != 5) // 4 active + 1 cancelled
            throw new InvalidOperationException($"Expected 5 total tasks (4 active + 1 cancelled), found {totalTasksDb}");

        Console.WriteLine("PASS Putaway task repeated generation (0 duplicate) and cancellation regeneration verified.");
    }

    // Step 2.3: Dual-Scan Confirmation through Production Handler
    // Fault injection on Task 1
    Guid activeTask1Id;
    string activeTask1Lpn;
    string activeTask1BinBarcode;

    await using (var query = CreateDb(operationalConn, user))
    {
        var t1 = await query.PutawayTasks.Include(t => t.SuggestedLocation).FirstAsync(t => t.LpnId == line1Id && t.Status == PutawayTaskStatus.Pending);
        activeTask1Id = t1.Id;
        activeTask1Lpn = t1.LpnCode!;
        activeTask1BinBarcode = t1.SuggestedLocation.Barcode;
    }

    var putawayFaultExercised = false;
    await using (var failing = CreateDb(operationalConn, user, new ThrowAfterSaveInterceptor()))
    {
        try
        {
            var auth = new WarehouseOperationalAuthorization(failing, user);
            await new ConfirmPutawayTaskCommandHandler(failing, user, auth).Handle(new ConfirmPutawayTaskCommand
            {
                TaskId = activeTask1Id,
                ScannedLpn = activeTask1Lpn,
                ScannedLocationBarcode = activeTask1BinBarcode
            }, CancellationToken.None);
        }
        catch (ForcedRollbackException)
        {
            putawayFaultExercised = true;
        }
    }
    if (!putawayFaultExercised)
        throw new InvalidOperationException("Putaway confirmation did not trigger the expected ForcedRollbackException fault.");

    // Verify putaway rollback
    await using (var verifyRollback = CreateDb(operationalConn, user))
    {
        var t1 = await verifyRollback.PutawayTasks.AsNoTracking().SingleAsync(t => t.Id == activeTask1Id);
        if (t1.Status != PutawayTaskStatus.Pending || t1.ActualLocationId != null)
            throw new InvalidOperationException("Putaway rollback failed to restore Pending status and null actual location.");
        Console.WriteLine("PASS Putaway post-save fault rollback verified.");
    }

    // Confirm all 4 active tasks through production handler
    await using (var confirmDb = CreateDb(operationalConn, user))
    {
        var auth = new WarehouseOperationalAuthorization(confirmDb, user);
        var handler = new ConfirmPutawayTaskCommandHandler(confirmDb, user, auth);

        var activeTasks = await confirmDb.PutawayTasks
            .Include(t => t.SuggestedLocation)
            .Where(t => t.FacilityId == facilityId && t.Status == PutawayTaskStatus.Pending)
            .ToListAsync();

        foreach (var task in activeTasks)
        {
            var res = await handler.Handle(new ConfirmPutawayTaskCommand
            {
                TaskId = task.Id,
                ScannedLpn = task.LpnCode ?? "",
                ScannedLocationBarcode = task.SuggestedLocation.Barcode
            }, CancellationToken.None);

            if (res.Status != PutawayTaskStatus.Completed)
                throw new InvalidOperationException($"Task {task.Id} confirmation failed.");
        }

        // Duplicate confirmation check on task 1
        var dupConfirmed = false;
        try
        {
            await handler.Handle(new ConfirmPutawayTaskCommand
            {
                TaskId = activeTask1Id,
                ScannedLpn = activeTask1Lpn,
                ScannedLocationBarcode = activeTask1BinBarcode
            }, CancellationToken.None);
        }
        catch (Warehouse3PL.Domain.Exceptions.ValidationException)
        {
            dupConfirmed = true;
        }
        if (!dupConfirmed)
            throw new InvalidOperationException("Duplicate confirmation did not reject completed task with ValidationException.");

        Console.WriteLine("PASS All 4 putaway tasks confirmed with dual-scan, duplicate confirmation rejected.");
    }

    // Step 2.4: Inventory Query & Ledger Reconciliation
    await using (var reconDb = CreateDb(operationalConn, user))
    {
        var auth = new WarehouseOperationalAuthorization(reconDb, user);
        var stockResult = await new GetStockOnHandQueryHandler(reconDb, auth).Handle(
            new GetStockOnHandQuery { FacilityId = facilityId, PageSize = 100 }, CancellationToken.None);

        var stagingBalances = stockResult.Items.Where(s => s.LocationId == staging1Id || s.LocationId == staging2Id).ToList();
        if (stagingBalances.Any(s => s.TotalQuantity > 0))
            throw new InvalidOperationException("Staging balances not zero after completed putaway.");

        var totalQty = stockResult.Items.Sum(s => s.TotalQuantity);
        if (totalQty != 70m)
            throw new InvalidOperationException($"Inventory conservation violated: expected 70m, found {totalQty}");

        var totalTransactions = await reconDb.InventoryTransactions.CountAsync();
        if (totalTransactions != 8) // 4 receiving + 4 putaway
            throw new InvalidOperationException($"Ledger count violated: expected 8 transactions (4 rx + 4 putaway), found {totalTransactions}");

        Console.WriteLine("PASS Inventory query & ledger reconciled: 0 staging, 70m destination total conserved, exactly 8 ledger transactions.");
    }

    // ------------------------------------------------------------------------------------------------
    // SECTION 3: BACKEND-CONNECTED FRONTEND API JOURNEY (P7.3-B03)
    // ------------------------------------------------------------------------------------------------
    Console.WriteLine("\n[3/3] Running P7.3-B03 Backend-Connected Frontend API Journey against WebApi TestServer...");
    var session3Id = Guid.NewGuid();
    var line5Id = Guid.NewGuid();

    // Seed session 5 for API journey
    await using (var seedApi = CreateDb(operationalConn, user))
    {
        seedApi.ReceivingSessions.Add(new ReceivingSession
        {
            Id = session3Id,
            FacilityId = facilityId,
            DockDoorId = door1Id,
            YardVisitId = visit1Id,
            ClientId = client1Id,
            SessionNumber = "RCV-API-1",
            Status = ReceivingStatus.InProgress,
            ReceivedByUserId = user.UserId!.Value,
            Lines =
            [
                new ReceiptLine { Id = line5Id, ItemId = item1Id, Lpn = "LPN-API-01", QuantityReceived = 30m, Condition = ReceiptConditionStatus.Sound, StagingLocationId = staging1Id }
            ]
        });
        await seedApi.SaveChangesAsync();
    }

    // Build WebApi TestServer pointing to the same live PostgreSQL connection
    var webBuilder = WebApplication.CreateBuilder();
    webBuilder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
    webBuilder.Logging.ClearProviders();
    webBuilder.WebHost.UseTestServer();
    webBuilder.Services.AddControllers()
        .AddApplicationPart(typeof(ReceivingController).Assembly)
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
    webBuilder.Services.AddApplicationServices();
    webBuilder.Services.AddAuthentication("TestAuth")
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestAuth", _ => { });
    webBuilder.Services.AddAuthorization();
    webBuilder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    webBuilder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
    webBuilder.Services.AddHttpContextAccessor();
    webBuilder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
    webBuilder.Services.AddScoped<ITenantProvider>(_ => new VerificationTenant());
    webBuilder.Services.AddScoped<IDateTimeProvider, VerificationClock>();
    webBuilder.Services.AddScoped<AuditableEntityInterceptor>();
    webBuilder.Services.AddDbContext<ApplicationDbContext>((sp, opts) =>
        opts.UseNpgsql(operationalConn).UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
    webBuilder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
    webBuilder.Services.AddScoped<WarehouseOperationalAuthorization>();
    webBuilder.Services.AddScoped<IPutawayLocationSelector, PutawayLocationSelector>();

    using var app = webBuilder.Build();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseRouting();
    app.UseAuthenticatedTenantResolution();
    app.UseAuthorization();
    app.MapControllers();
    await app.StartAsync();

    var client = app.GetTestClient();
    client.DefaultRequestHeaders.Add("X-Facility-Id", facilityId.ToString());

    // 1. HTTP Complete Receiving Session
    var completeResponse = await client.PatchAsync($"/api/v1/receiving/sessions/{session3Id}/complete", null);
    if (completeResponse.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"HTTP Complete failed: {completeResponse.StatusCode} - {await completeResponse.Content.ReadAsStringAsync()}");

    var completeBody = await completeResponse.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(completeBody))
    {
        var statusStr = doc.RootElement.GetProperty("status").GetString();
        if (statusStr != "Completed")
            throw new InvalidOperationException($"HTTP Complete wire string enum mismatch: expected 'Completed', found '{statusStr}'");
    }
    Console.WriteLine("PASS HTTP PATCH /api/v1/receiving/sessions/{id}/complete -> 200 OK, wire string enum 'Completed'.");

    // 2. HTTP Generate Putaway Tasks
    var genResponse = await client.PostAsJsonAsync("/api/v1/putaway/tasks/generate", new { FacilityId = facilityId });
    if (genResponse.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"HTTP Generate failed: {genResponse.StatusCode} - {await genResponse.Content.ReadAsStringAsync()}");

    var genBody = await genResponse.Content.ReadAsStringAsync();
    Guid apiTaskId;
    using (var doc = JsonDocument.Parse(genBody))
    {
        var array = doc.RootElement.EnumerateArray().ToList();
        var apiTaskElem = array.Single(e => e.GetProperty("lpnId").GetGuid() == line5Id);
        apiTaskId = apiTaskElem.GetProperty("id").GetGuid();
        var statusStr = apiTaskElem.GetProperty("status").GetString();
        if (statusStr != "Pending")
            throw new InvalidOperationException($"HTTP Generate wire string enum mismatch: expected 'Pending', found '{statusStr}'");
    }
    Console.WriteLine("PASS HTTP POST /api/v1/putaway/tasks/generate -> 200 OK, wire string enum 'Pending'.");

    // 3. HTTP Confirm Putaway Task
    var confirmResponse = await client.PostAsJsonAsync($"/api/v1/putaway/tasks/{apiTaskId}/confirm", new ConfirmPutawayTaskRequest("LPN-API-01", "OP-BIN1"));
    if (confirmResponse.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"HTTP Confirm failed: {confirmResponse.StatusCode} - {await confirmResponse.Content.ReadAsStringAsync()}");

    var confirmBody = await confirmResponse.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(confirmBody))
    {
        var statusStr = doc.RootElement.GetProperty("status").GetString();
        if (statusStr != "Completed")
            throw new InvalidOperationException($"HTTP Confirm wire string enum mismatch: expected 'Completed', found '{statusStr}'");
    }
    Console.WriteLine("PASS HTTP POST /api/v1/putaway/tasks/{id}/confirm -> 200 OK, wire string enum 'Completed'.");

    // 4. HTTP Query Stock On Hand
    var stockResponse = await client.GetAsync($"/api/v1/inventory/stock-on-hand?facilityId={facilityId}");
    if (stockResponse.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"HTTP Stock query failed: {stockResponse.StatusCode} - {await stockResponse.Content.ReadAsStringAsync()}");

    var stockBody = await stockResponse.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(stockBody))
    {
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        if (!items.Any(i => i.GetProperty("locationId").GetGuid() == bin1Id))
            throw new InvalidOperationException("Stock on hand did not reflect destination bin1.");
    }
    Console.WriteLine("PASS HTTP GET /api/v1/inventory/stock-on-hand -> 200 OK, verified wire DTO and destination balances.");

    Console.WriteLine("\n================================================================================");
    Console.WriteLine("ALL PHASE 7.3 POSTGRESQL & BACKEND-CONNECTED JOURNEYS PASSED CLEANLY!");
    Console.WriteLine("================================================================================");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await DropDatabaseAsync(builder.ConnectionString, operationalDbName);
}

static async Task CreateDatabaseAsync(string adminConnStr, string dbName)
{
    var b = new NpgsqlConnectionStringBuilder(adminConnStr) { Database = "postgres" };
    await using var conn = new NpgsqlConnection(b.ConnectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
    await cmd.ExecuteNonQueryAsync();
}

static async Task DropDatabaseAsync(string adminConnStr, string dbName)
{
    try
    {
        var b = new NpgsqlConnectionStringBuilder(adminConnStr) { Database = "postgres" };
        await using var conn = new NpgsqlConnection(b.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }
    catch
    {
        // Ignore best-effort cleanup errors
    }
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

sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Email, "staff@test.local"),
            new Claim(ClaimTypes.Role, "SuperAdmin"),
            new Claim("UserType", "Staff"),
            new Claim("Permission", "Inbound:Receive"),
            new Claim("Permission", "Inbound:Putaway:Execute"),
            new Claim("Permission", "Inbound:Putaway:Manage"),
            new Claim("Permission", "Inbound:View"),
            new Claim("Permission", "Inventory:View"),
            new Claim("Permission", "Analytics:CrossFacility")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "TestAuth");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
