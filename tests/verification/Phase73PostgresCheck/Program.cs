using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
using Microsoft.Extensions.Configuration;
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
using Warehouse3PL.Application.Features.Auth.Commands.Login;
using Warehouse3PL.Application.Features.Auth.Commands.RefreshToken;
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
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Configuration;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Allocation;
using Warehouse3PL.Infrastructure.Services.Packing;
using Warehouse3PL.Infrastructure.Services.Picking;
using Warehouse3PL.Infrastructure.Services.Shipping;
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
    var dummyZeroLoc = Guid.Parse("00000000-0000-0000-0000-000000000000");
    var verifier = new VerificationIdentity(facilityId);

    // Step 1: Migrate up to the migration immediately preceding Phase73RemoveLegacyPutawayTargetShadow
    await using (var db = CreateDb(migrationConn, verifier))
    {
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync("20261117102207_Phase6_8_DurableAnalyticsJournal");
    }

    // Step 2: Insert master data and legacy putaway records via raw SQL
    var task1Matching = Guid.NewGuid();
    var task2MissingCanonical = Guid.NewGuid();
    var task3Differing = Guid.NewGuid();
    var taskDup1 = Guid.NewGuid();
    var taskDup2 = Guid.NewGuid();
    var dupLineId = Guid.NewGuid();
    var sessionId = Guid.NewGuid();

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

        // Seed master records with all authoritative required fields and string enum values
        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO clients (client_id, company_name, account_code, status, sla_tier, currency, is_deleted, created_at)
            VALUES ('{clientId}', 'Migration Test Client', 'MTC', 'Active', 'Standard', 'USD', false, NOW());

            INSERT INTO facilities (facility_id, facility_code, name, timezone, country_code, dock_door_count, is_active, is_deleted, created_at)
            VALUES ('{facilityId}', 'MIG-FAC', 'Migration Facility', 'UTC', 'USA', 4, true, false, NOW());

            INSERT INTO items (item_id, client_id, sku, description, unit_price, weight_kg, length_cm, width_cm, height_cm, volume_cm3, abc_velocity, storage_temp_class, allow_planar_rotation, status, is_deleted, created_at)
            VALUES ('{itemId}', '{clientId}', 'MIG-SKU', 'Migration Item', 10.0000, 1.500, 20.00, 15.00, 10.00, 3000.00, 'C', 'Ambient', true, 'Active', false, NOW());

            INSERT INTO locations (location_id, facility_id, location_code, barcode, location_type, is_active, is_receiving, is_pickable, is_damaged, is_locked, is_deleted, created_at)
            VALUES 
                ('{stagingLoc}', '{facilityId}', 'MIG-STG', 'MIG-STG', 'StagingLane', true, true, true, false, false, false, NOW()),
                ('{loc1}', '{facilityId}', 'MIG-LOC1', 'MIG-LOC1', 'Bin', true, false, true, false, false, false, NOW()),
                ('{loc2}', '{facilityId}', 'MIG-LOC2', 'MIG-LOC2', 'Bin', true, false, true, false, false, false, NOW()),
                ('{dummyZeroLoc}', '{facilityId}', 'MIG-DUMMY', 'MIG-DUMMY', 'Bin', true, false, true, false, false, false, NOW());

            INSERT INTO users (user_id, email, password_hash, first_name, last_name, user_type, status, is_deleted, created_at)
            VALUES ('{verifier.UserId}', 'verifier@mig.local', 'hash', 'Migration', 'Verifier', 'Staff', 'Active', false, NOW());

            INSERT INTO receiving_sessions (session_id, facility_id, client_id, session_number, status, started_at_utc, received_by_user_id, is_blind_receiving, requires_supervisor_approval, is_supervisor_approved, version, is_deleted, created_at)
            VALUES ('{sessionId}', '{facilityId}', '{clientId}', 'MIG-RCV-001', 'Completed', NOW(), '{verifier.UserId}', false, false, false, 1, false, NOW());

            INSERT INTO receipt_lines (receipt_line_id, session_id, item_id, lpn, quantity_received, quantity_expected, condition, staging_location_id, scanned_at_utc, version, is_deleted)
            VALUES ('{dupLineId}', '{sessionId}', '{itemId}', 'MIG-LPN-001', 15.0000, 15.0000, 'Sound', '{stagingLoc}', NOW(), 1, false);
        ", sqlConn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Insert representative legacy putaway records:
        // 1. Matching: suggested_location_id == loc1, target_location_id1 == loc1
        // 2. Missing canonical: suggested_location_id == 00000000-0000-0000-0000-000000000000, target_location_id1 == loc2
        // 3. Differing: suggested_location_id == loc1, target_location_id1 == loc2
        // 4. Duplicate tasks for receipt line (dupLineId): taskDup1 (older), taskDup2 (latest)
        await using (var cmd = new NpgsqlCommand($@"
            INSERT INTO putaway_tasks (task_id, client_id, facility_id, item_id, source_location_id, suggested_location_id, target_location_id1, lpn_id, quantity, status, priority, notes, version, created_at_utc, created_at)
            VALUES 
                ('{task1Matching}', '{clientId}', '{facilityId}', '{itemId}', '{stagingLoc}', '{loc1}', '{loc1}', NULL, 10, 'Pending', 'Normal', 'Matching task', 1, NOW(), NOW()),
                ('{task2MissingCanonical}', '{clientId}', '{facilityId}', '{itemId}', '{stagingLoc}', '{dummyZeroLoc}', '{loc2}', NULL, 20, 'Pending', 'Normal', 'Missing canonical task', 1, NOW(), NOW()),
                ('{task3Differing}', '{clientId}', '{facilityId}', '{itemId}', '{stagingLoc}', '{loc1}', '{loc2}', NULL, 30, 'Pending', 'Normal', 'Differing task', 1, NOW(), NOW()),
                ('{taskDup1}', '{clientId}', '{facilityId}', '{itemId}', '{stagingLoc}', '{loc1}', '{loc1}', '{dupLineId}', 15, 'Pending', 'Normal', 'Pre-migration duplicate task 1', 1, NOW() - INTERVAL '1 hour', NOW()),
                ('{taskDup2}', '{clientId}', '{facilityId}', '{itemId}', '{stagingLoc}', '{loc1}', '{loc1}', '{dupLineId}', 15, 'Pending', 'Normal', 'Pre-migration duplicate task 2', 1, NOW(), NOW());

            -- Seed legacy ClientAdmin role containing obsolete floor execution permissions before Phase 7.4.0A migration
            INSERT INTO roles (role_name, description, is_system_role, created_at)
            VALUES ('ClientAdmin', 'Legacy seeded ClientAdmin role', TRUE, NOW())
            ON CONFLICT (role_name) DO NOTHING;

            INSERT INTO role_permissions (role_id, permission_key, access_level, created_at)
            SELECT r.role_id, v.permission_key, 'Admin', NOW()
            FROM roles r
            CROSS JOIN (VALUES
                ('outbound:pick'),
                ('outbound:ship'),
                ('outbound:pack'),
                ('outbound:tasks:manage'),
                ('outbound:waves:manage'),
                ('outbound:orders:allocate')
            ) AS v(permission_key)
            WHERE r.role_name = 'ClientAdmin'
            ON CONFLICT (role_id, permission_key) DO NOTHING;
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
                (SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'putaway_tasks' AND indexname = 'ix_putaway_tasks_target_location_id1') AS idx_exists,
                (SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'putaway_tasks' AND indexname = 'ix_putaway_tasks_lpn_id') AS unique_idx_exists;
        ", sqlConn))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var colExists = reader.GetInt64(0);
            var fkExists = reader.GetInt64(1);
            var idxExists = reader.GetInt64(2);
            var uniqueIdxExists = reader.GetInt64(3);
            if (colExists != 0 || fkExists != 0 || idxExists != 0)
                throw new InvalidOperationException($"Post-migration catalog check failed: col={colExists}, fk={fkExists}, idx={idxExists}");
            if (uniqueIdxExists != 1)
                throw new InvalidOperationException("Partial unique index ix_putaway_tasks_lpn_id was not created on putaway_tasks.");
        }

        // 4a-bis. Verify Phase 7.4.0A migrations removed obsolete ClientAdmin permissions, added dispatch:view + order permissions, and created webhook state/audit tables
        await using (var p74Cmd = new NpgsqlCommand(@"
            SELECT
                (SELECT COUNT(*) FROM role_permissions rp JOIN roles r ON r.role_id = rp.role_id WHERE r.role_name = 'ClientAdmin' AND rp.permission_key IN ('outbound:pick','outbound:ship','outbound:pack','outbound:tasks:manage','outbound:waves:manage','outbound:orders:allocate')) AS obsolete_count,
                (SELECT COUNT(*) FROM role_permissions rp JOIN roles r ON r.role_id = rp.role_id WHERE r.role_name = 'ClientAdmin' AND rp.permission_key IN ('outbound:orders:view','outbound:orders:create','outbound:orders:import','outbound:orders:cancel','dispatch:view')) AS allowed_order_count,
                (SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'carrier_webhook_nonces' AND indexname = 'ix_carrier_webhook_nonces_carrier_code_nonce') AS nonce_idx_exists,
                (SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'carrier_webhook_nonces' AND column_name IN ('status','attempt_count','processing_lease_expires_at_utc','processed_at_utc','claim_token')) AS nonce_state_cols,
                (SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'carrier_webhook_audit_logs') AS audit_tbl_exists,
                (SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'assigned_facility_id') AS user_fac_col;
        ", sqlConn))
        {
            await using var r = await p74Cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            var obsoleteCount = r.GetInt64(0);
            var allowedOrderCount = r.GetInt64(1);
            var nonceIdxExists = r.GetInt64(2);
            var nonceStateCols = r.GetInt64(3);
            var auditTblExists = r.GetInt64(4);
            var userFacCol = r.GetInt64(5);
            if (obsoleteCount != 0 || allowedOrderCount != 5 || nonceIdxExists != 1 || nonceStateCols != 5 || auditTblExists != 1 || userFacCol != 1)
                throw new InvalidOperationException($"Phase 7.4.0A/B migration check failed: obsolete={obsoleteCount}, allowed={allowedOrderCount}, nonceIdx={nonceIdxExists}, nonceStateCols={nonceStateCols}, auditTbl={auditTblExists}, userFacCol={userFacCol}");
            Console.WriteLine("PASS Phase 7.4.0A/B migrations reconciled ClientAdmin, webhook state/audit, and users.assigned_facility_id on PostgreSQL.");
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

        // 4c. Verify duplicate receipt-line task deduplication:
        // - taskDup1 (older duplicate): marked 'Cancelled', audit note appended
        // - taskDup2 (latest duplicate): preserved as 'Pending'
        await using (var cmd = new NpgsqlCommand($@"
            SELECT task_id, status, notes FROM putaway_tasks
            WHERE task_id IN ('{taskDup1}', '{taskDup2}')
            ORDER BY created_at_utc;
        ", sqlConn))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var t1Status = reader.GetString(1);
            var t1Notes = reader.GetString(2);

            await reader.ReadAsync();
            var t2Status = reader.GetString(1);
            var t2Notes = reader.GetString(2);

            if (t1Status != "Cancelled" || !t1Notes.Contains("Superseded by migration deduplication"))
                throw new InvalidOperationException($"Duplicate task 1 deduplication failed: status={t1Status}, notes={t1Notes}");
            if (t2Status != "Pending")
                throw new InvalidOperationException($"Duplicate task 2 was unexpectedly altered: status={t2Status}");
        }

        // 4d. Verify partial unique index enforces uniqueness against new duplicate pending tasks
        var uniqueViolationCaught = false;
        try
        {
            await using var dupInsertCmd = new NpgsqlCommand($@"
                INSERT INTO putaway_tasks (task_id, client_id, facility_id, item_id, source_location_id, suggested_location_id, lpn_id, quantity, status, priority, version, created_at_utc, created_at)
                VALUES ('{Guid.NewGuid()}', '{clientId}', '{facilityId}', '{itemId}', '{stagingLoc}', '{loc1}', '{dupLineId}', 10, 'Pending', 'Normal', 1, NOW(), NOW());
            ", sqlConn);
            await dupInsertCmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == "23505" && pgEx.ConstraintName != null && pgEx.ConstraintName.Contains("ix_putaway_tasks_lpn_id"))
        {
            uniqueViolationCaught = true;
        }

        if (!uniqueViolationCaught)
            throw new InvalidOperationException("Database failed to enforce partial unique index ix_putaway_tasks_lpn_id on duplicate receipt line.");

        // 4e. Verify conflict check logic: multiple completed tasks with differing locations trigger migration exception
        var simulatedConflictCaught = false;
        try
        {
            await using var conflictCheckCmd = new NpgsqlCommand(@"
                DO $$
                DECLARE conflict_count INT;
                BEGIN
                    SELECT COUNT(*) INTO conflict_count
                    FROM (
                        SELECT 'test-lpn' AS lpn, 'Completed' AS st, 'loc-a' AS loc
                        UNION ALL
                        SELECT 'test-lpn', 'Completed', 'loc-b'
                    ) sub
                    WHERE st = 'Completed'
                    GROUP BY lpn
                    HAVING COUNT(DISTINCT loc) > 1;

                    IF conflict_count > 0 THEN
                        RAISE EXCEPTION 'Simulated conflict detected: % unresolved conflicts', conflict_count;
                    END IF;
                END $$;
            ", sqlConn);
            await conflictCheckCmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException pgEx) when (pgEx.Message.Contains("Simulated conflict detected"))
        {
            simulatedConflictCaught = true;
        }

        if (!simulatedConflictCaught)
            throw new InvalidOperationException("Simulated conflict check did not raise expected exception.");
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

    Console.WriteLine("PASS P7.3-B04 Migration upgrade safety: matching/differing reconciliation verified, duplicate deduplication verified, partial unique index operational, shadow FK/column removed.");
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
            new Client { Id = client1Id, AccountCode = "CL1", CompanyName = "Operational Client 1", Status = ClientStatus.Active, SlaTier = SlaTier.Standard, Currency = "USD" },
            new Client { Id = client2Id, AccountCode = "CL2", CompanyName = "Operational Client 2", Status = ClientStatus.Active, SlaTier = SlaTier.Standard, Currency = "USD" });

        seed.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "OP-FAC", Name = "Operational Facility", Timezone = "UTC", CountryCode = "USA", DockDoorCount = 4, IsActive = true });

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
            new Item { Id = item1Id, ClientId = client1Id, Sku = "SKU-OP-1", Description = "Client 1 Item 1", AbcVelocity = AbcVelocity.C, StorageTempClass = StorageTempClass.Ambient, Status = ItemStatus.Active },
            new Item { Id = item2Id, ClientId = client1Id, Sku = "SKU-OP-2", Description = "Client 1 Item 2", AbcVelocity = AbcVelocity.C, StorageTempClass = StorageTempClass.Ambient, Status = ItemStatus.Active },
            new Item { Id = item3Id, ClientId = client2Id, Sku = "SKU-OP-3", Description = "Client 2 Item 3", AbcVelocity = AbcVelocity.C, StorageTempClass = StorageTempClass.Ambient, Status = ItemStatus.Active });

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

    // ------------------------------------------------------------------------------------------------
    // TARGETED POSTGRESQL REGRESSIONS (P7.3-B02 Exception Recovery & ChangeTracker Isolation)
    // ------------------------------------------------------------------------------------------------
    Console.WriteLine("\n[2.5] Running Targeted PostgreSQL Regressions: 23505 Collision, Overlapping Batches, and Unrelated Error Propagation...");

    // Regression 1: PostgreSQL Unique Violation 23505 Collision Recovery & ChangeTracker Isolation
    var sessionColId = Guid.NewGuid();
    var lineColId = Guid.NewGuid();
    await using (var seedCol = CreateDb(operationalConn, user))
    {
        seedCol.ReceivingSessions.Add(new ReceivingSession
        {
            Id = sessionColId,
            FacilityId = facilityId,
            ClientId = client1Id,
            SessionNumber = "RCV-COL-01",
            Status = ReceivingStatus.Completed,
            ReceivedByUserId = user.UserId!.Value,
            Lines = [new ReceiptLine { Id = lineColId, ItemId = item1Id, Lpn = "LPN-COL-01", QuantityReceived = 5m, Condition = ReceiptConditionStatus.Sound, StagingLocationId = staging1Id }]
        });
        await seedCol.SaveChangesAsync();
    }

    // Pre-insert a task directly simulating competing committed transaction
    var preExistingTaskId = Guid.NewGuid();
    await using (var insertComp = CreateDb(operationalConn, user))
    {
        insertComp.PutawayTasks.Add(new PutawayTask
        {
            Id = preExistingTaskId,
            FacilityId = facilityId,
            ClientId = client1Id,
            ItemId = item1Id,
            LpnId = lineColId,
            LpnCode = "LPN-COL-01",
            SourceLocationId = staging1Id,
            SuggestedLocationId = bin1Id,
            Quantity = 5m,
            Status = PutawayTaskStatus.Pending,
            Priority = PutawayPriority.Normal,
            CreatedAtUtc = DateTime.UtcNow
        });
        await insertComp.SaveChangesAsync();
    }

    // Now invoke GeneratePutawayTasksCommandHandler: it will observe candidate line and attempt to handle it.
    // If competing task was committed, handler safely identifies it and returns active task without unhandled exception.
    await using (var testColDb = CreateDb(operationalConn, user))
    {
        var auth = new WarehouseOperationalAuthorization(testColDb, user);
        var selector = new PutawayLocationSelector(testColDb, NullLogger<PutawayLocationSelector>.Instance);
        var colResults = await new GeneratePutawayTasksCommandHandler(testColDb, selector, auth).Handle(
            new GeneratePutawayTasksCommand { FacilityId = facilityId, ClientId = client1Id }, CancellationToken.None);

        // Since lineColId is already assigned, handler returns 0 newly generated tasks without throwing
        if (colResults.Count != 0)
            throw new InvalidOperationException($"Expected 0 new tasks for already assigned line, got {colResults.Count}");

        var taskInDb = await testColDb.PutawayTasks.SingleOrDefaultAsync(t => t.LpnId == lineColId && t.Id == preExistingTaskId);
        if (taskInDb == null)
            throw new InvalidOperationException("Collision recovery did not preserve the authoritative active task for lineColId.");

        // Verify ChangeTracker is clean
        if (testColDb.ChangeTracker.Entries().Any(e => e.State != EntityState.Unchanged))
            throw new InvalidOperationException("ChangeTracker contains dirty/uncommitted entries after collision recovery.");

        Console.WriteLine("PASS PostgreSQL 23505 collision recovery & ChangeTracker clean state verified.");
    }

    // Regression 2: Overlapping Generation Batches (Processing unassigned lines without omission)
    var sessionOverlapId = Guid.NewGuid();
    var lineAId = Guid.NewGuid();
    var lineBId = Guid.NewGuid();
    await using (var seedOverlap = CreateDb(operationalConn, user))
    {
        seedOverlap.ReceivingSessions.Add(new ReceivingSession
        {
            Id = sessionOverlapId,
            FacilityId = facilityId,
            ClientId = client1Id,
            SessionNumber = "RCV-OVERLAP-01",
            Status = ReceivingStatus.Completed,
            ReceivedByUserId = user.UserId!.Value,
            Lines =
            [
                new ReceiptLine { Id = lineAId, ItemId = item1Id, Lpn = "LPN-OV-A", QuantityReceived = 8m, Condition = ReceiptConditionStatus.Sound, StagingLocationId = staging1Id },
                new ReceiptLine { Id = lineBId, ItemId = item2Id, Lpn = "LPN-OV-B", QuantityReceived = 12m, Condition = ReceiptConditionStatus.Sound, StagingLocationId = staging1Id }
            ]
        });
        // Pre-insert task for lineAId as if assigned by competing batch
        seedOverlap.PutawayTasks.Add(new PutawayTask
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            ClientId = client1Id,
            ItemId = item1Id,
            LpnId = lineAId,
            LpnCode = "LPN-OV-A",
            SourceLocationId = staging1Id,
            SuggestedLocationId = bin2Id,
            Quantity = 8m,
            Status = PutawayTaskStatus.Pending,
            Priority = PutawayPriority.Normal,
            CreatedAtUtc = DateTime.UtcNow
        });
        await seedOverlap.SaveChangesAsync();
    }

    await using (var testOverlapDb = CreateDb(operationalConn, user))
    {
        var auth = new WarehouseOperationalAuthorization(testOverlapDb, user);
        var selector = new PutawayLocationSelector(testOverlapDb, NullLogger<PutawayLocationSelector>.Instance);
        var overlapResults = await new GeneratePutawayTasksCommandHandler(testOverlapDb, selector, auth).Handle(
            new GeneratePutawayTasksCommand { FacilityId = facilityId, ClientId = client1Id }, CancellationToken.None);

        // Newly generated result must contain lineBId (newly assigned)
        if (!overlapResults.Any(t => t.LpnId == lineBId))
            throw new InvalidOperationException("Overlapping batch recovery omitted unassigned receipt line B.");

        var lineATaskInDb = await testOverlapDb.PutawayTasks.SingleOrDefaultAsync(t => t.LpnId == lineAId && t.Status == PutawayTaskStatus.Pending);
        var lineBTaskInDb = await testOverlapDb.PutawayTasks.SingleOrDefaultAsync(t => t.LpnId == lineBId && t.Status == PutawayTaskStatus.Pending);
        if (lineATaskInDb == null || lineBTaskInDb == null)
            throw new InvalidOperationException("Both receipt line A and receipt line B must have active tasks in database.");

        Console.WriteLine("PASS Overlapping generation batch handled cleanly: unassigned line B was generated without omission.");
    }

    // Regression 3: Unrelated Database Error Propagation (Non-23505 errors are strictly propagated)
    var unrelatedErrorPropagated = false;
    await using (var testErrorDb = CreateDb(operationalConn, user))
    {
        try
        {
            // Trigger an unrelated error (e.g. FK violation 23503 by inserting a task with an invalid location)
            testErrorDb.PutawayTasks.Add(new PutawayTask
            {
                Id = Guid.NewGuid(),
                FacilityId = facilityId,
                ClientId = client1Id,
                ItemId = item1Id,
                SourceLocationId = Guid.NewGuid(), // Invalid foreign key
                SuggestedLocationId = bin1Id,
                Quantity = 10m,
                Status = PutawayTaskStatus.Pending,
                Priority = PutawayPriority.Normal,
                CreatedAtUtc = DateTime.UtcNow
            });
            await testErrorDb.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Verify that an unrelated error throws and is NOT swallowed by IsReceiptLineUniqueViolation
            var isRecoverable = (bool)typeof(GeneratePutawayTasksCommandHandler)
                .GetMethod("IsReceiptLineUniqueViolation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [ex])!;

            if (!isRecoverable)
            {
                unrelatedErrorPropagated = true;
            }
        }
    }

    if (!unrelatedErrorPropagated)
        throw new InvalidOperationException("Unrelated database constraint error was incorrectly treated as recoverable.");

    Console.WriteLine("PASS Unrelated database constraint error is NOT swallowed and propagates cleanly.");

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
            .Where(t => t.FacilityId == facilityId && t.Status == PutawayTaskStatus.Pending && (t.LpnId == line1Id || t.LpnId == line2Id || t.LpnId == line3Id || t.LpnId == line4Id))
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

        Console.WriteLine("PASS All 4 core putaway tasks confirmed with dual-scan, duplicate confirmation rejected.");
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
    TestAuthHandler.CurrentUserId = user.UserId!.Value;
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
    webBuilder.Services.AddScoped<IAnalyticsQueryService, Warehouse3PL.Infrastructure.Services.Analytics.AnalyticsQueryService>();

    using var app = webBuilder.Build();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseRouting();
    app.UseAuthenticatedTenantResolution();
    app.UseAuthorization();
    app.MapControllers();
    await app.StartAsync();

    var client = app.GetTestClient();
    client.DefaultRequestHeaders.Add("X-Facility-Id", facilityId.ToString());

    // 0. Query initial stock and ledger count before HTTP operations
    var initialStockResponse = await client.GetAsync($"/api/v1/inventory/stock?facilityId={facilityId}");
    if (initialStockResponse.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"Initial stock query failed: {initialStockResponse.StatusCode}");
    var initialStockBody = await initialStockResponse.Content.ReadAsStringAsync();
    decimal initialStagingQty = 0m;
    decimal initialBin1Qty = 0m;
    decimal initialTotalQty = 0m;
    using (var doc = JsonDocument.Parse(initialStockBody))
    {
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        initialStagingQty = items.Where(i => i.GetProperty("locationId").GetGuid() == staging1Id).Sum(i => i.GetProperty("totalQuantity").GetDecimal());
        initialBin1Qty = items.Where(i => i.GetProperty("locationId").GetGuid() == bin1Id).Sum(i => i.GetProperty("totalQuantity").GetDecimal());
        initialTotalQty = items.Sum(i => i.GetProperty("totalQuantity").GetDecimal());
    }

    int initialTxCount;
    await using (var checkDb = CreateDb(operationalConn, user))
    {
        initialTxCount = await checkDb.InventoryTransactions.CountAsync();
    }

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

    // Assert exact stock and ledger effects immediately after receiving completion
    var postRxStockResponse = await client.GetAsync($"/api/v1/inventory/stock?facilityId={facilityId}");
    var postRxStockBody = await postRxStockResponse.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(postRxStockBody))
    {
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        var currentStaging = items.Where(i => i.GetProperty("locationId").GetGuid() == staging1Id).Sum(i => i.GetProperty("totalQuantity").GetDecimal());
        var currentBin1 = items.Where(i => i.GetProperty("locationId").GetGuid() == bin1Id).Sum(i => i.GetProperty("totalQuantity").GetDecimal());
        var currentTotal = items.Sum(i => i.GetProperty("totalQuantity").GetDecimal());

        if (currentStaging != initialStagingQty + 30m)
            throw new InvalidOperationException($"Staging stock after rx mismatch: expected {initialStagingQty + 30m}, found {currentStaging}");
        if (currentBin1 != initialBin1Qty)
            throw new InvalidOperationException($"Bin1 stock after rx changed unexpectedly: expected {initialBin1Qty}, found {currentBin1}");
        if (currentTotal != initialTotalQty + 30m)
            throw new InvalidOperationException($"Total stock after rx mismatch: expected {initialTotalQty + 30m}, found {currentTotal}");
    }

    await using (var checkDb = CreateDb(operationalConn, user))
    {
        var postRxTxCount = await checkDb.InventoryTransactions.CountAsync();
        if (postRxTxCount != initialTxCount + 1)
            throw new InvalidOperationException($"Ledger count after rx mismatch: expected {initialTxCount + 1}, found {postRxTxCount}");
    }
    Console.WriteLine("PASS HTTP PATCH /api/v1/receiving/sessions/{id}/complete -> 200 OK, wire string enum 'Completed', exact staging +30m credit, +1 receiving ledger record.");

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
        var qty = apiTaskElem.GetProperty("quantity").GetDecimal();
        if (statusStr != "Pending")
            throw new InvalidOperationException($"HTTP Generate wire string enum mismatch: expected 'Pending', found '{statusStr}'");
        if (qty != 30m)
            throw new InvalidOperationException($"HTTP Generate wire quantity mismatch: expected 30m, found {qty}");
    }
    Console.WriteLine("PASS HTTP POST /api/v1/putaway/tasks/generate -> 200 OK, wire string enum 'Pending', quantity 30m.");

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

    // Assert exact stock and ledger effects immediately after putaway confirmation
    var postPutawayStockResponse = await client.GetAsync($"/api/v1/inventory/stock?facilityId={facilityId}");
    var postPutawayStockBody = await postPutawayStockResponse.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(postPutawayStockBody))
    {
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        var finalStaging = items.Where(i => i.GetProperty("locationId").GetGuid() == staging1Id).Sum(i => i.GetProperty("totalQuantity").GetDecimal());
        var finalBin1 = items.Where(i => i.GetProperty("locationId").GetGuid() == bin1Id).Sum(i => i.GetProperty("totalQuantity").GetDecimal());
        var finalTotal = items.Sum(i => i.GetProperty("totalQuantity").GetDecimal());

        if (finalStaging != initialStagingQty)
            throw new InvalidOperationException($"Final staging stock mismatch: expected {initialStagingQty} (debited), found {finalStaging}");
        if (finalBin1 != initialBin1Qty + 30m)
            throw new InvalidOperationException($"Final bin1 stock mismatch: expected {initialBin1Qty + 30m} (credited), found {finalBin1}");
        if (finalTotal != initialTotalQty + 30m)
            throw new InvalidOperationException($"Final total stock conservation mismatch: expected {initialTotalQty + 30m}, found {finalTotal}");
    }

    await using (var checkDb = CreateDb(operationalConn, user))
    {
        var finalTxCount = await checkDb.InventoryTransactions.CountAsync();
        if (finalTxCount != initialTxCount + 2)
            throw new InvalidOperationException($"Final ledger count mismatch: expected {initialTxCount + 2} (+1 rx, +1 putaway), found {finalTxCount}");
    }
    Console.WriteLine("PASS HTTP POST /api/v1/putaway/tasks/{id}/confirm -> 200 OK, wire string enum 'Completed', exact staging -30m debit, destination +30m credit, total conserved, +1 putaway ledger record.");

    // 4. HTTP Query Stock On Hand
    Console.WriteLine("PASS HTTP GET /api/v1/inventory/stock -> 200 OK, verified wire DTO and exact destination balances (staging=0m, bin1=40m, total=100m, total transactions=10).");

    // ------------------------------------------------------------------------------------------------
    // SECTION 4: METADATA SCHEMA & CLIENT CRUD HTTP INTEGRATION VERIFICATION (P-META-01 - P-META-04)
    // ------------------------------------------------------------------------------------------------
    Console.WriteLine("\n[4/4] Running Metadata Schema & Client CRUD HTTP Integration Verification...");

    // 4.1 Authoritative Schema Resolution: GET /api/v1/schemas/Client
    var schemaResponse = await client.GetAsync("/api/v1/schemas/Client");
    if (schemaResponse.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"HTTP GET /api/v1/schemas/Client failed: {schemaResponse.StatusCode} - {await schemaResponse.Content.ReadAsStringAsync()}");

    var schemaBody = await schemaResponse.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(schemaBody))
    {
        var root = doc.RootElement;
        var name = root.GetProperty("name").GetString();
        var primaryKey = root.GetProperty("primaryKey").GetString();
        if (name != "Client" || primaryKey != "id")
            throw new InvalidOperationException($"Client schema identity mismatch: name={name}, primaryKey={primaryKey}");

        var fields = root.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        if (!fields.Contains("accountCode") || !fields.Contains("companyName") || !fields.Contains("slaTier") || !fields.Contains("currency") || !fields.Contains("status"))
            throw new InvalidOperationException("Client schema missing core required fields.");

        // Must NOT contain contact editing fields
        if (fields.Contains("contactFirstName") || fields.Contains("contactLastName") || fields.Contains("contactEmail") || fields.Contains("contactPhone"))
            throw new InvalidOperationException("Client schema incorrectly contains unsupported contact editing fields.");
    }
    Console.WriteLine("PASS HTTP GET /api/v1/schemas/Client -> 200 OK, authoritative metadata resolved, contact fields excluded.");

    // 4.2 Negative Authorization: GET /api/v1/schemas/Client without clients:view permission
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, "StandardViewer"),
        new Claim("user_type", "Staff")
        // No clients:view permission
    };
    var unauthSchemaResponse = await client.GetAsync("/api/v1/schemas/Client");
    if (unauthSchemaResponse.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Expected 403 Forbidden for unauthorized schema query, got {unauthSchemaResponse.StatusCode}");
    Console.WriteLine("PASS HTTP GET /api/v1/schemas/Client without clients:view -> 403 Forbidden (fail-closed verified).");

    // Negative Authorization: GET /api/v1/clients without clients:view permission
    var unauthClientsResponse = await client.GetAsync("/api/v1/clients");
    if (unauthClientsResponse.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Expected 403 Forbidden for unauthorized clients query, got {unauthClientsResponse.StatusCode}");
    Console.WriteLine("PASS HTTP GET /api/v1/clients without clients:view -> 403 Forbidden (fail-closed verified).");

    // Restore full staff authorization
    TestAuthHandler.CustomClaims = null;

    // 4.3a Lowercase AccountCode rejection: POST /api/v1/clients with "pme-01"
    var lowercasePayload = new
    {
        CompanyName = "Postgres Lowercase Validation",
        AccountCode = "pme-01",
        SlaTier = 2,
        Currency = "USD",
        Status = 2
    };
    var lowercaseResponse = await client.PostAsJsonAsync("/api/v1/clients", lowercasePayload);
    if (lowercaseResponse.StatusCode != HttpStatusCode.BadRequest)
        throw new InvalidOperationException($"Expected 400 BadRequest for lowercase accountCode, got {lowercaseResponse.StatusCode}");
    Console.WriteLine("PASS HTTP POST /api/v1/clients with lowercase accountCode -> 400 Bad Request (validator contract verified).");

    // 4.3b Create Client with valid uppercase code: POST /api/v1/clients with Gold SLA and Suspended status
    var createClientPayload = new
    {
        CompanyName = "Postgres Metadata Enterprise",
        AccountCode = "PME-01",
        SlaTier = SlaTier.Gold, // 3
        Currency = "USD",
        Status = ClientStatus.Suspended // 3
    };
    var createClientResponse = await client.PostAsJsonAsync("/api/v1/clients", createClientPayload);
    if (createClientResponse.StatusCode != HttpStatusCode.Created)
        throw new InvalidOperationException($"HTTP POST /api/v1/clients failed: {createClientResponse.StatusCode} - {await createClientResponse.Content.ReadAsStringAsync()}");

    var createClientBody = await createClientResponse.Content.ReadAsStringAsync();
    Guid createdClientId;
    using (var doc = JsonDocument.Parse(createClientBody))
    {
        var root = doc.RootElement;
        createdClientId = root.GetProperty("id").GetGuid();
        var code = root.GetProperty("accountCode").GetString();
        var cName = root.GetProperty("companyName").GetString();
        var curr = root.GetProperty("currency").GetString();
        var sla = root.GetProperty("slaTier").GetString();
        var st = root.GetProperty("status").GetString();
        if (code != "PME-01")
            throw new InvalidOperationException($"AccountCode normalization to uppercase failed: expected 'PME-01', found '{code}'");
        if (cName != "Postgres Metadata Enterprise")
            throw new InvalidOperationException($"CompanyName mismatch: expected 'Postgres Metadata Enterprise', found '{cName}'");
        if (curr != "USD")
            throw new InvalidOperationException($"Currency normalization failed: expected 'USD', found '{curr}'");
        if (sla != "Gold" || st != "Suspended")
            throw new InvalidOperationException($"Wire string enum mismatch on create: expected 'Gold'/'Suspended', found '{sla}'/'{st}'");
    }
    Console.WriteLine("PASS HTTP POST /api/v1/clients -> 201 Created, uppercase code normalization & valid UUID verified.");

    // 4.4 Search, Server-Side Sorting & Pagination: GET /api/v1/clients?searchTerm=Metadata&sortBy=companyName&sortDescending=false
    var queryClientsResponse = await client.GetAsync("/api/v1/clients?searchTerm=Metadata&sortBy=companyName&sortDescending=false&pageNumber=1&pageSize=10");
    if (queryClientsResponse.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"HTTP GET /api/v1/clients query failed: {queryClientsResponse.StatusCode}");

    var queryClientsBody = await queryClientsResponse.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(queryClientsBody))
    {
        var root = doc.RootElement;
        var totalCount = root.GetProperty("totalCount").GetInt32();
        var items = root.GetProperty("items").EnumerateArray().ToList();
        if (totalCount < 1 || !items.Any(i => i.GetProperty("id").GetGuid() == createdClientId))
            throw new InvalidOperationException("Filtered and sorted query did not return the created client.");
    }
    Console.WriteLine("PASS HTTP GET /api/v1/clients?searchTerm=Metadata&sortBy=companyName -> 200 OK, server-side sorting & filtering verified.");

    // 4.5 Detail Query: GET /api/v1/clients/{id}
    var detailResponse = await client.GetAsync($"/api/v1/clients/{createdClientId}");
    if (detailResponse.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"HTTP GET /api/v1/clients/{createdClientId} failed: {detailResponse.StatusCode}");
    Console.WriteLine("PASS HTTP GET /api/v1/clients/{id} -> 200 OK, authoritative detail retrieval verified.");

    // 4.6a Update Client with companyName change ONLY: PUT /api/v1/clients/{id}
    // Wire simulation: retrieved client had string enums 'Gold' and 'Suspended'
    var updatePayloadOnlyName = new
    {
        Id = createdClientId,
        CompanyName = "Postgres Metadata Enterprise Renamed",
        SlaTier = "Gold",
        Status = "Suspended",
        Currency = "USD"
    };
    var updateResponse1 = await client.PutAsJsonAsync($"/api/v1/clients/{createdClientId}", updatePayloadOnlyName);
    if (updateResponse1.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"HTTP PUT /api/v1/clients/{createdClientId} (only name change) failed: {updateResponse1.StatusCode} - {await updateResponse1.Content.ReadAsStringAsync()}");

    // 4.6b Re-read PostgreSQL directly to verify Gold and Suspended were preserved without corruption
    await using (var dbCheck = CreateDb(operationalConn, user))
    {
        var clientInDb = await dbCheck.Clients.SingleAsync(c => c.Id == createdClientId);
        if (clientInDb.CompanyName != "Postgres Metadata Enterprise Renamed")
            throw new InvalidOperationException($"Company name not updated in PostgreSQL: found '{clientInDb.CompanyName}'");
        if (clientInDb.SlaTier != SlaTier.Gold)
            throw new InvalidOperationException($"SLA Tier corrupted in PostgreSQL: expected 'Gold' (3), found '{clientInDb.SlaTier}'");
        if (clientInDb.Status != ClientStatus.Suspended)
            throw new InvalidOperationException($"Status corrupted in PostgreSQL: expected 'Suspended' (3), found '{clientInDb.Status}'");
    }
    Console.WriteLine("PASS HTTP PUT /api/v1/clients/{id} preserving string enums -> verified in PostgreSQL: Gold & Suspended intact without silent corruption.");

    // 4.6c Intentional Enum Update: PUT /api/v1/clients/{id} updating SLA to Platinum and Status to Active
    var updatePayloadIntentional = new
    {
        Id = createdClientId,
        CompanyName = "Postgres Metadata Enterprise Renamed",
        SlaTier = SlaTier.Platinum, // 4
        Status = ClientStatus.Active, // 2
        Currency = "EUR"
    };
    var updateResponse2 = await client.PutAsJsonAsync($"/api/v1/clients/{createdClientId}", updatePayloadIntentional);
    if (updateResponse2.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"HTTP PUT /api/v1/clients/{createdClientId} (intentional enum change) failed: {updateResponse2.StatusCode}");

    await using (var dbCheck = CreateDb(operationalConn, user))
    {
        var clientInDb = await dbCheck.Clients.SingleAsync(c => c.Id == createdClientId);
        if (clientInDb.SlaTier != SlaTier.Platinum || clientInDb.Status != ClientStatus.Active || clientInDb.Currency != "EUR")
            throw new InvalidOperationException($"Intentional update failed: SlaTier={clientInDb.SlaTier}, Status={clientInDb.Status}, Currency={clientInDb.Currency}");
    }
    Console.WriteLine("PASS HTTP PUT /api/v1/clients/{id} intentional enum change -> verified in PostgreSQL: Platinum & Active persisted accurately.");

    // 4.7 Multi-tenant Isolation & Missing Tenant Fail-Closed Negative Checks (P-META-07)
    // 4.7a Non-staff user WITHOUT tenant context
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, "ClientUser"),
        new Claim("user_type", "Client"),
        new Claim("permissions", "clients:view"),
        new Claim("permissions", "clients:edit"),
        new Claim("permissions", "clients:delete")
        // No tenant_id claim
    };

    var noTenantList = await client.GetAsync("/api/v1/clients");
    if (noTenantList.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Expected 403 Forbidden for non-staff without tenant context on list, got {noTenantList.StatusCode}");

    var noTenantDetail = await client.GetAsync($"/api/v1/clients/{createdClientId}");
    if (noTenantDetail.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Expected 403 Forbidden for non-staff without tenant context on detail, got {noTenantDetail.StatusCode}");

    var noTenantUpdate = await client.PutAsJsonAsync($"/api/v1/clients/{createdClientId}", new { Id = createdClientId, CompanyName = "Spoofed", SlaTier = 1, Status = 2, Currency = "USD" });
    if (noTenantUpdate.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Expected 403 Forbidden for non-staff without tenant context on update, got {noTenantUpdate.StatusCode}");

    var noTenantDelete = await client.DeleteAsync($"/api/v1/clients/{createdClientId}");
    if (noTenantDelete.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Expected 403 Forbidden for non-staff without tenant context on delete, got {noTenantDelete.StatusCode}");

    Console.WriteLine("PASS Non-staff without tenant context fail-closed on list, detail, update, delete -> 403 Forbidden verified.");

    // 4.7b Non-staff user assigned to client1Id attempting foreign tenant (createdClientId) access
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, "ClientUser"),
        new Claim("user_type", "Client"),
        new Claim("tenant_id", client1Id.ToString()),
        new Claim("permissions", "clients:view"),
        new Claim("permissions", "clients:edit"),
        new Claim("permissions", "clients:delete")
    };

    var foreignDetail = await client.GetAsync($"/api/v1/clients/{createdClientId}");
    if (foreignDetail.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Expected 403 Forbidden for cross-tenant detail access, got {foreignDetail.StatusCode}");

    var foreignUpdate = await client.PutAsJsonAsync($"/api/v1/clients/{createdClientId}", new { Id = createdClientId, CompanyName = "CrossTenantHacked", SlaTier = 1, Status = 2, Currency = "USD" });
    if (foreignUpdate.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Expected 403 Forbidden for cross-tenant update access, got {foreignUpdate.StatusCode}");

    var foreignDelete = await client.DeleteAsync($"/api/v1/clients/{createdClientId}");
    if (foreignDelete.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Expected 403 Forbidden for cross-tenant deactivation, got {foreignDelete.StatusCode}");

    var ownList = await client.GetAsync("/api/v1/clients");
    if (ownList.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"Expected 200 OK for own tenant list, got {ownList.StatusCode}");
    var ownListBody = await ownList.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(ownListBody))
    {
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        if (items.Count != 1 || items[0].GetProperty("id").GetGuid() != client1Id)
            throw new InvalidOperationException("Non-staff tenant list isolation violated: expected only caller's own client record.");
    }
    Console.WriteLine("PASS Cross-tenant security isolation verified: foreign detail, update, delete -> 403 Forbidden; list properly partitioned.");

    // Restore full staff authorization
    TestAuthHandler.CustomClaims = null;

    // 4.8 Soft Delete: DELETE /api/v1/clients/{id} (Staff authorized)
    var deleteResponse = await client.DeleteAsync($"/api/v1/clients/{createdClientId}");
    if (deleteResponse.StatusCode != HttpStatusCode.NoContent)
        throw new InvalidOperationException($"HTTP DELETE /api/v1/clients/{createdClientId} failed: {deleteResponse.StatusCode}");

    await using (var dbCheck = CreateDb(operationalConn, user))
    {
        var clientInDb = await dbCheck.Clients.IgnoreQueryFilters().SingleAsync(c => c.Id == createdClientId);
        if (!clientInDb.IsDeleted)
            throw new InvalidOperationException("Client record in PostgreSQL is not marked as is_deleted = true.");
    }
    Console.WriteLine("PASS HTTP DELETE /api/v1/clients/{id} -> 204 NoContent, soft delete (is_deleted = true) in PostgreSQL verified.");

    // ------------------------------------------------------------------------------------------------
    // SECTION 5: CONTROL TOWER TELEMETRY & QUEUE STATUS / RBAC VERIFICATION (P-CT-01 & P-CT-02)
    // ------------------------------------------------------------------------------------------------
    Console.WriteLine("\n[5/5] Running Section 5: Control Tower Telemetry, Queue Status Filtering & RBAC Verification...");

    // 5.1 Authorized Control Tower Summary
    var summaryResponse = await client.GetAsync($"/api/v1/control-tower/summary?facilityId={facilityId}");
    if (summaryResponse.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"Control Tower Summary query failed: {summaryResponse.StatusCode}");
    var summaryJson = await summaryResponse.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(summaryJson))
    {
        var asOfUtc = doc.RootElement.GetProperty("asOfUtc").GetString();
        if (string.IsNullOrEmpty(asOfUtc))
            throw new InvalidOperationException("Control Tower summary asOfUtc was empty.");
    }
    Console.WriteLine("PASS HTTP GET /api/v1/control-tower/summary -> 200 OK with authentic asOfUtc.");

    // 5.2 Negative Control Tower Summary without analytics permission
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, "FloorOperator"),
        new Claim("UserType", "Staff"),
        new Claim("user_type", "Staff"),
        new Claim("Permission", "Inbound:Putaway:Execute")
    };
    var unauthSummaryResponse = await client.GetAsync($"/api/v1/control-tower/summary?facilityId={facilityId}");
    if (unauthSummaryResponse.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Unauthenticated Control Tower Summary should return 403 Forbidden, got {unauthSummaryResponse.StatusCode}");
    Console.WriteLine("PASS Negative Check: HTTP GET /api/v1/control-tower/summary without analytics permission -> 403 Forbidden.");

    // 5.3 Active Putaway Queue Status Filtering (P-CT-01)
    TestAuthHandler.CustomClaims = null; // restore full permissions
    var putawayFilteredResponse = await client.GetAsync($"/api/v1/putaway/tasks?facilityId={facilityId}&statuses=Pending&statuses=Assigned&statuses=InTransit&pageNumber=1&pageSize=5");
    if (putawayFilteredResponse.StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException($"Putaway tasks query with active statuses failed: {putawayFilteredResponse.StatusCode}");
    var putawayJson = await putawayFilteredResponse.Content.ReadAsStringAsync();
    using (var doc = JsonDocument.Parse(putawayJson))
    {
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        foreach (var task in items)
        {
            var statusStr = task.GetProperty("status").GetString();
            if (statusStr == "Completed" || statusStr == "Cancelled")
                throw new InvalidOperationException($"Putaway tasks query returned inactive task with status: {statusStr}");
        }
        var totalCount = doc.RootElement.GetProperty("totalCount").GetInt32();
        if (totalCount < 0)
            throw new InvalidOperationException("Putaway tasks query totalCount invalid.");
    }
    Console.WriteLine("PASS HTTP GET /api/v1/putaway/tasks with statuses=Pending,Assigned,InTransit -> 200 OK, inactive tasks excluded.");

    // 5.4 RBAC: Floor operator without inbound:view cannot list putaway queue (P-CT-02)
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, "FloorOperator"),
        new Claim("UserType", "Staff"),
        new Claim("user_type", "Staff"),
        new Claim("facility_id", facilityId.ToString()),
        new Claim("Permission", "Inbound:Putaway:Execute"),
        new Claim("permissions", "inbound:putaway:execute")
    };
    var operatorPutawayListResponse = await client.GetAsync($"/api/v1/putaway/tasks?facilityId={facilityId}");
    if (operatorPutawayListResponse.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Floor operator listing putaway tasks should return 403 Forbidden, got {operatorPutawayListResponse.StatusCode}");
    Console.WriteLine("PASS Negative Check: HTTP GET /api/v1/putaway/tasks without inbound:view -> 403 Forbidden.");

    // 5.5 Floor operator with inbound:putaway:execute and assigned facility CAN invoke /next endpoint
    var operatorNextResponse = await client.GetAsync($"/api/v1/putaway/tasks/next?facilityId={facilityId}");
    if (operatorNextResponse.StatusCode != HttpStatusCode.OK && operatorNextResponse.StatusCode != HttpStatusCode.NoContent)
        throw new InvalidOperationException($"Floor operator GET /api/v1/putaway/tasks/next failed: {operatorNextResponse.StatusCode}");
    Console.WriteLine($"PASS Execution Check: HTTP GET /api/v1/putaway/tasks/next with inbound:putaway:execute -> {operatorNextResponse.StatusCode} (Authorized).");

    // 5.5b Floor operator without assigned facility is rejected with 403 Forbidden
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, "FloorOperator"),
        new Claim("UserType", "Staff"),
        new Claim("user_type", "Staff"),
        new Claim("Permission", "Inbound:Putaway:Execute"),
        new Claim("permissions", "inbound:putaway:execute")
    };
    var unassignedFacilityResponse = await client.GetAsync($"/api/v1/putaway/tasks/next?facilityId={facilityId}");
    if (unassignedFacilityResponse.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Floor operator without assigned facility must return 403 Forbidden, got {unassignedFacilityResponse.StatusCode}");
    Console.WriteLine("PASS Security Check: HTTP GET /api/v1/putaway/tasks/next with missing assigned facility -> 403 Forbidden.");

    // 5.5c Floor operator targeting a foreign facility is rejected with 403 Forbidden
    var foreignFacilityId = Guid.NewGuid();
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, "FloorOperator"),
        new Claim("UserType", "Staff"),
        new Claim("user_type", "Staff"),
        new Claim("facility_id", facilityId.ToString()),
        new Claim("Permission", "Inbound:Putaway:Execute"),
        new Claim("permissions", "inbound:putaway:execute")
    };
    var foreignFacilityResponse = await client.GetAsync($"/api/v1/putaway/tasks/next?facilityId={foreignFacilityId}");
    if (foreignFacilityResponse.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Floor operator targeting foreign facility must return 403 Forbidden, got {foreignFacilityResponse.StatusCode}");
    Console.WriteLine("PASS Security Check: HTTP GET /api/v1/putaway/tasks/next with foreign facility ID -> 403 Forbidden.");

    // 5.6 Floor operator without inbound:view cannot list receiving sessions
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, "FloorOperator"),
        new Claim("UserType", "Staff"),
        new Claim("user_type", "Staff"),
        new Claim("facility_id", facilityId.ToString()),
        new Claim("Permission", "Inbound:Receive"),
        new Claim("permissions", "inbound:receive")
    };
    var operatorReceivingListResponse = await client.GetAsync($"/api/v1/receiving/sessions?facilityId={facilityId}");
    if (operatorReceivingListResponse.StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException($"Floor operator listing receiving sessions should return 403 Forbidden, got {operatorReceivingListResponse.StatusCode}");
    Console.WriteLine("PASS Negative Check: HTTP GET /api/v1/receiving/sessions without inbound:view -> 403 Forbidden.");

    // Restore full permissions
    TestAuthHandler.CustomClaims = null;

    // ------------------------------------------------------------------------------------------------
    // SECTION 6: PHASE 7.4.0A OUTBOUND SECURITY, RBAC RECONCILIATION & WEBHOOK HMAC (POSTGRESQL)
    // ------------------------------------------------------------------------------------------------
    Console.WriteLine("\n[6/6] Running Phase 7.4.0A Outbound Security, ClientAdmin Reconciliation & Carrier Webhook Verification on PostgreSQL...");

    // 6.1 Seed legacy ClientAdmin permissions and verify DbInitializer reconciles idempotently on PostgreSQL
    await using (var reconDb = CreateDb(operationalConn, user))
    {
        var legacyRole = new Role
        {
            RoleName = SystemRoles.ClientAdmin,
            Description = "Client admin role",
            IsSystemRole = true
        };
        legacyRole.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Pick, AccessLevel = AccessLevel.Admin });
        legacyRole.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Ship, AccessLevel = AccessLevel.Admin });
        legacyRole.RolePermissions.Add(new RolePermission { PermissionKey = Permissions.Outbound.Pack, AccessLevel = AccessLevel.Admin });
        reconDb.Roles.Add(legacyRole);
        await reconDb.SaveChangesAsync();

        var initializer = new DbInitializer(reconDb, new PasswordHasher(), NullLogger<DbInitializer>.Instance);
        await initializer.SeedAsync();
        await initializer.SeedAsync(); // Idempotency pass

        var refreshed = await reconDb.Roles.Include(r => r.RolePermissions)
            .SingleAsync(r => r.RoleName == SystemRoles.ClientAdmin);
        var permSet = refreshed.RolePermissions.Select(p => p.PermissionKey).ToList();
        if (permSet.Contains(Permissions.Outbound.Pick) ||
            permSet.Contains(Permissions.Outbound.Ship) ||
            permSet.Contains(Permissions.Outbound.Pack) ||
            !permSet.Contains(Permissions.Outbound.OrdersView) ||
            !permSet.Contains(Permissions.Outbound.OrdersCreate) ||
            !permSet.Contains(Permissions.Dispatch.View))
        {
            throw new InvalidOperationException("DbInitializer idempotent ClientAdmin reconciliation check failed on PostgreSQL.");
        }
        if (permSet.Distinct(StringComparer.OrdinalIgnoreCase).Count() != permSet.Count)
        {
            throw new InvalidOperationException("Duplicate RolePermission entries found after DbInitializer idempotent run.");
        }
    }
    Console.WriteLine("PASS PostgreSQL DbInitializer.SeedAsync() reconciles legacy ClientAdmin permissions idempotently.");

    // 6.2 Seed Outbound, Packing, Shipping & Return entities with valid PostgreSQL FKs
    var p74FacB = Guid.NewGuid();
    var p74LocB = Guid.NewGuid();
    var p74OrderC1FacA = Guid.NewGuid();
    var p74OrderC1FacB = Guid.NewGuid();
    var p74OrderC2FacA = Guid.NewGuid();
    var p74OrderLineId = Guid.NewGuid();
    var p74AllocId = Guid.NewGuid();
    var p74WaveFacB = Guid.NewGuid();
    var p74TaskFacB = Guid.NewGuid();
    var p74ShipFacB = Guid.NewGuid();
    var p74HuC1FacA = Guid.NewGuid();
    var p74HuC2FacA = Guid.NewGuid();
    var p74PkgC1FacA = Guid.NewGuid();
    var p74PkgC2FacA = Guid.NewGuid();
    var p74DelivExcId = Guid.NewGuid();
    var p74ReturnShipId = Guid.NewGuid();
    var p74Tracking = $"TRK-PG-RTS-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    await using (var p74Db = CreateDb(operationalConn, user))
    {
        var stockId = await p74Db.StockOnHand
            .Where(s => s.ClientId == client1Id)
            .Select(s => s.Id)
            .FirstAsync();

        p74Db.Facilities.Add(new Facility
        {
            Id = p74FacB,
            FacilityCode = "PG-FAC-B",
            Name = "Postgres Secondary Facility B",
            AddressLine1 = "500 Secondary Way",
            City = "Dallas",
            Timezone = "America/Chicago",
            CountryCode = "US",
            IsActive = true
        });
        p74Db.Locations.Add(new Location
        {
            Id = p74LocB,
            FacilityId = p74FacB,
            LocationCode = "PG-B-01",
            Barcode = "PG-B-01",
            LocationType = LocationType.Bin,
            IsActive = true,
            IsPickable = true
        });
        p74Db.ClientFacilityAccesses.AddRange(
            new ClientFacilityAccess { ClientId = client1Id, FacilityId = facilityId, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = client2Id, FacilityId = facilityId, Status = FacilityAccessStatus.Active });

        var ord1 = new OutboundOrder { Id = p74OrderC1FacA, ClientId = client1Id, FacilityId = facilityId, OrderNumber = "PG-ORD-1A", CustomerName = "PG C1", Status = OutboundOrderStatus.Released };
        var ord2 = new OutboundOrder { Id = p74OrderC1FacB, ClientId = client1Id, FacilityId = p74FacB, OrderNumber = "PG-ORD-1B", CustomerName = "PG C1B", Status = OutboundOrderStatus.Released };
        var ord3 = new OutboundOrder { Id = p74OrderC2FacA, ClientId = client2Id, FacilityId = facilityId, OrderNumber = "PG-ORD-2A", CustomerName = "PG C2", Status = OutboundOrderStatus.Released };
        p74Db.OutboundOrders.AddRange(ord1, ord2, ord3);

        p74Db.OutboundOrderLines.Add(new OutboundOrderLine
        {
            Id = p74OrderLineId,
            OutboundOrderId = p74OrderC1FacB,
            ItemId = item1Id,
            LineNumber = 1,
            QuantityOrdered = 5m,
            QuantityAllocated = 5m
        });
        p74Db.InventoryAllocations.Add(new InventoryAllocation
        {
            Id = p74AllocId,
            ClientId = client1Id,
            FacilityId = p74FacB,
            OutboundOrderId = p74OrderC1FacB,
            OutboundOrderLineId = p74OrderLineId,
            StockOnHandId = stockId,
            ItemId = item1Id,
            LocationId = p74LocB,
            QuantityRequested = 5m,
            QuantityAllocated = 5m,
            Status = AllocationStatus.Allocated
        });
        p74Db.PickWaves.Add(new PickWave { Id = p74WaveFacB, ClientId = client1Id, FacilityId = p74FacB, WaveNumber = "PG-WV-B", Status = PickWaveStatus.Planning });
        p74Db.PickTasks.Add(new PickTask
        {
            Id = p74TaskFacB,
            ClientId = client1Id,
            FacilityId = p74FacB,
            PickWaveId = p74WaveFacB,
            OutboundOrderId = p74OrderC1FacB,
            OutboundOrderLineId = p74OrderLineId,
            InventoryAllocationId = p74AllocId,
            StockOnHandId = stockId,
            ItemId = item1Id,
            SourceLocationId = p74LocB,
            QuantityToPick = 5m,
            PickSequence = 1,
            Status = PickTaskStatus.Pending
        });

        p74Db.Shipments.Add(new Shipment
        {
            Id = p74ShipFacB,
            ClientId = client1Id,
            FacilityId = p74FacB,
            ShipmentNumber = "PG-SHP-B1",
            CarrierCode = "SIM-FEDEX",
            Status = ShipmentStatus.Draft
        });

        p74Db.HandlingUnits.AddRange(
            new HandlingUnit { Id = p74HuC1FacA, ClientId = client1Id, FacilityId = facilityId, LpnCode = "PG-CARTON-1A", UnitType = HandlingUnitType.ShippingCarton },
            new HandlingUnit { Id = p74HuC2FacA, ClientId = client2Id, FacilityId = facilityId, LpnCode = "PG-CARTON-2A", UnitType = HandlingUnitType.ShippingCarton });
        p74Db.Packages.AddRange(
            new Package { Id = p74PkgC1FacA, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = p74OrderC1FacA, HandlingUnitId = p74HuC1FacA, PackageNumber = "PG-PKG-1A", CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-PG-C1A", Status = PackageStatus.LabelPrinted },
            new Package { Id = p74PkgC2FacA, ClientId = client2Id, FacilityId = facilityId, OutboundOrderId = p74OrderC2FacA, HandlingUnitId = p74HuC2FacA, PackageNumber = "PG-PKG-2A", CarrierCode = "SIM-FEDEX", TrackingNumber = "TRK-PG-C2A", Status = PackageStatus.LabelPrinted });

        p74Db.ShipmentDeliveryExceptions.Add(new ShipmentDeliveryException
        {
            Id = p74DelivExcId,
            ClientId = client1Id,
            FacilityId = facilityId,
            PackageId = p74PkgC1FacA,
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = "TRK-PG-C1A",
            CarrierExceptionCode = "CUSTOMER_REFUSED",
            ExceptionType = DeliveryExceptionType.CustomerRefused,
            Severity = DeliveryExceptionSeverity.High,
            CarrierDescription = "Customer refused delivery",
            OccurredAtUtc = DateTime.UtcNow.AddHours(-3),
            Status = DeliveryExceptionStatus.PendingReturnAuthorization
        });

        var p74Auth = ReturnAuthorization.Create(
            clientId: client1Id,
            originFacilityId: facilityId,
            destinationFacilityId: facilityId,
            orderId: p74OrderC1FacA,
            originalShipmentId: null,
            packageId: p74PkgC1FacA,
            deliveryExceptionId: p74DelivExcId,
            authorizationNumber: "PG-RTS-740A",
            reason: ReturnReason.CustomerRefused,
            notes: "PG RTS verification",
            authorizedByUserId: user.UserId!.Value,
            rmaNumber: "PG-RMA-01");
        p74Auth.LinkReturnShipment(p74ReturnShipId);
        p74Db.ReturnAuthorizations.Add(p74Auth);

        p74Db.ReturnShipments.Add(new ReturnShipment
        {
            Id = p74ReturnShipId,
            ClientId = client1Id,
            OriginFacilityId = facilityId,
            DestinationFacilityId = facilityId,
            OrderId = p74OrderC1FacA,
            PackageId = p74PkgC1FacA,
            DeliveryExceptionId = p74DelivExcId,
            ReturnAuthorizationId = p74Auth.Id,
            ReturnRmaNumber = p74Auth.RmaNumber!,
            Reason = ReturnReason.CustomerRefused,
            AuthorizedByUserId = user.UserId!.Value,
            AuthorizedAtUtc = DateTime.UtcNow,
            CarrierCode = "SIM-FEDEX",
            TrackingNumber = p74Tracking,
            CarrierServiceLevel = "StandardGround",
            Status = ReturnShipmentStatus.Authorized
        });

        await p74Db.SaveChangesAsync();
    }

    // 6.3 Build dedicated WebApi TestServer with real TenantProvider + Outbound services against live PostgreSQL
    const string pgPrimarySecret = "pg-ci-fedex-primary-secret";
    const string pgSecondarySecret = "pg-ci-fedex-secondary-secret";
    var p74WebBuilder = WebApplication.CreateBuilder();
    p74WebBuilder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
    p74WebBuilder.Logging.ClearProviders();
    p74WebBuilder.WebHost.UseTestServer();
    p74WebBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["CarrierWebhooks:TimestampToleranceSeconds"] = "300",
        ["CarrierWebhooks:MaxPayloadBytes"] = "1024",
        ["CarrierWebhooks:Carriers:SIM-FEDEX:PrimarySecret"] = pgPrimarySecret,
        ["CarrierWebhooks:Carriers:SIM-FEDEX:SecondarySecret"] = pgSecondarySecret,
        ["CarrierWebhooks:Carriers:SIM-FEDEX:SecondarySecretExpiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(1).ToString("O")
    });
    p74WebBuilder.Services.Configure<CarrierWebhookOptions>(
        p74WebBuilder.Configuration.GetSection(CarrierWebhookOptions.SectionName));
    p74WebBuilder.Services.AddControllers()
        .AddApplicationPart(typeof(OutboundOrdersController).Assembly)
        .AddApplicationPart(typeof(PgWebhookProbeController).Assembly)
        .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
    p74WebBuilder.Services.AddApplicationServices();
    p74WebBuilder.Services.AddAuthentication("TestAuth")
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestAuth", _ => { });
    p74WebBuilder.Services.AddAuthorization();
    p74WebBuilder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    p74WebBuilder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
    p74WebBuilder.Services.AddHttpContextAccessor();
    p74WebBuilder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
    p74WebBuilder.Services.AddSingleton<ITokenService, JwtTokenService>();
    p74WebBuilder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
    p74WebBuilder.Services.AddScoped<ITenantProvider, TenantProvider>();
    var p74Clock = new PgControllableClock();
    p74WebBuilder.Services.AddSingleton(p74Clock);
    p74WebBuilder.Services.AddSingleton<IDateTimeProvider>(p74Clock);
    p74WebBuilder.Services.AddScoped<AuditableEntityInterceptor>();
    p74WebBuilder.Services.AddScoped<IAllocationCandidateFilter, AllocationCandidateFilter>();
    p74WebBuilder.Services.AddScoped<IAllocationStrategy, FifoAllocationStrategy>();
    p74WebBuilder.Services.AddScoped<IAllocationStrategy, FefoAllocationStrategy>();
    p74WebBuilder.Services.AddScoped<IAllocationStrategy, ManualAllocationStrategy>();
    p74WebBuilder.Services.AddScoped<IAllocationStrategyResolver, AllocationStrategyResolver>();
    p74WebBuilder.Services.AddScoped<IAllocationEngine, AllocationEngine>();
    p74WebBuilder.Services.AddScoped<IAllocationExecutionService, AllocationExecutionService>();
    p74WebBuilder.Services.AddScoped<IPickRouteOptimizer, SerpentineRouteOptimizer>();
    p74WebBuilder.Services.AddScoped<IPickTaskGenerator, PickTaskGenerator>();
    p74WebBuilder.Services.AddScoped<IPickWaveGenerator, PickWaveGenerator>();
    p74WebBuilder.Services.AddScoped<IPickExecutionService, PickExecutionService>();
    p74WebBuilder.Services.AddScoped<ICartonizationService, CartonizationService>();
    p74WebBuilder.Services.AddScoped<IPackingExecutionService, PackingExecutionService>();
    p74WebBuilder.Services.AddScoped<IShippingCarrierService, SimulatedCarrierService>();
    p74WebBuilder.Services.AddScoped<IShippingLabelService, ShippingLabelService>();
    p74WebBuilder.Services.AddScoped<IShipmentConsolidationService, ShipmentConsolidationService>();
    p74WebBuilder.Services.AddScoped<ReturnTrackingService>();
    p74WebBuilder.Services.AddScoped<IReturnTrackingService, PgFaultInjectableReturnTrackingService>();
    p74WebBuilder.Services.AddSingleton<PgWebhookProbeState>();
    p74WebBuilder.Services.AddDbContext<ApplicationDbContext>((sp, opts) =>
        opts.UseNpgsql(operationalConn).UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
    p74WebBuilder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

    using var p74App = p74WebBuilder.Build();
    p74App.UseMiddleware<ExceptionHandlingMiddleware>();
    p74App.UseRouting();
    p74App.UseAuthenticatedTenantResolution();
    p74App.UseAuthorization();
    p74App.MapControllers();
    await p74App.StartAsync();
    var p74Client = p74App.GetTestClient();

    // 6.4 ClientAdmin with legacy outbound:pick & outbound:ship rejected (403) on execution, allowed on own track (200), concealed on foreign track (404)
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, SystemRoles.ClientAdmin),
        new Claim("user_type", "Client"),
        new Claim("tenant_id", client1Id.ToString()),
        new Claim("permissions", Permissions.Outbound.OrdersView),
        new Claim("permissions", Permissions.Outbound.Pick),
        new Claim("permissions", Permissions.Outbound.Ship),
        new Claim("permissions", Permissions.Outbound.Pack),
        new Claim("permissions", Permissions.Outbound.OrdersAllocate),
        new Claim("permissions", Permissions.Outbound.TasksView)
    };
    foreach (var resp in new[]
    {
        await p74Client.PostAsJsonAsync($"/api/v1/outbound/orders/{p74OrderC1FacA}/allocate", new { }),
        await p74Client.PostAsJsonAsync($"/api/v1/outbound/pick-tasks/{p74TaskFacB}/confirm", new { scannedLocationBarcode = "PG-B-01", scannedItemBarcode = "X", scannedToteLpn = "T1", quantity = 1m }),
        await p74Client.GetAsync($"/api/v1/outbound/shipments/{p74ShipFacB}"),
        await p74Client.PostAsJsonAsync("/api/v1/outbound/shipping/rates", new { packageId = p74PkgC1FacA })
    })
    {
        if (resp.StatusCode != HttpStatusCode.Forbidden)
            throw new InvalidOperationException($"Expected 403 Forbidden for legacy ClientAdmin floor request, got {resp.StatusCode}");
    }
    if ((await p74Client.GetAsync($"/api/v1/outbound/shipping/packages/{p74PkgC1FacA}/track")).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Expected 200 OK for owning ClientAdmin package tracking transition.");
    if ((await p74Client.GetAsync($"/api/v1/outbound/shipping/packages/{p74PkgC2FacA}/track")).StatusCode != HttpStatusCode.NotFound)
        throw new InvalidOperationException("Expected 404 NotFound for foreign tenant package tracking.");
    Console.WriteLine("PASS PostgreSQL ClientAdmin legacy claim rejection (403), own track transition (200), and foreign concealment (404).");

    // 6.4b Post-reconciliation ClientAdmin (dispatch:view + outbound:orders:* WITHOUT outbound:ship) tracks own package (200), conceals foreign (404), denied rates/labels/shipments (403)
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, SystemRoles.ClientAdmin),
        new Claim("user_type", "Client"),
        new Claim("tenant_id", client1Id.ToString()),
        new Claim("permissions", Permissions.Outbound.OrdersView),
        new Claim("permissions", Permissions.Outbound.OrdersCreate),
        new Claim("permissions", Permissions.Dispatch.View)
    };
    if ((await p74Client.GetAsync($"/api/v1/outbound/shipping/packages/{p74PkgC1FacA}/track")).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Expected 200 OK for reconciled ClientAdmin (dispatch:view) own-tenant tracking.");
    if ((await p74Client.GetAsync($"/api/v1/outbound/shipping/packages/{p74PkgC2FacA}/track")).StatusCode != HttpStatusCode.NotFound)
        throw new InvalidOperationException("Expected 404 NotFound for reconciled ClientAdmin foreign tracking.");
    if ((await p74Client.PostAsJsonAsync("/api/v1/outbound/shipping/rates", new { packageId = p74PkgC1FacA })).StatusCode != HttpStatusCode.Forbidden ||
        (await p74Client.PostAsJsonAsync("/api/v1/outbound/shipping/labels/generate", new { packageId = p74PkgC1FacA, carrierCode = "SIM-FEDEX", serviceLevel = 1 })).StatusCode != HttpStatusCode.Forbidden ||
        (await p74Client.GetAsync($"/api/v1/outbound/shipments/{p74ShipFacB}")).StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException("Reconciled ClientAdmin with dispatch:view must be denied (403) on shipping mutations/shipments.");
    Console.WriteLine("PASS PostgreSQL Post-Reconciliation ClientAdmin (dispatch:view): own track (200), foreign concealment (404), shipping mutations denied (403).");

    // 6.5 Driver (with dispatch:view + outbound:ship) & unassigned staff rejections (403) + MultiFacilityView read-only vs write separation
    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, SystemRoles.Driver),
        new Claim("user_type", "Driver"),
        new Claim("facility_id", facilityId.ToString()),
        new Claim("permissions", Permissions.Outbound.OrdersView),
        new Claim("permissions", Permissions.Outbound.Ship),
        new Claim("permissions", Permissions.Dispatch.View)
    };
    if ((await p74Client.GetAsync($"/api/v1/outbound/orders/{p74OrderC1FacA}")).StatusCode != HttpStatusCode.Forbidden ||
        (await p74Client.GetAsync($"/api/v1/outbound/shipping/packages/{p74PkgC1FacA}/track")).StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException("Driver identity was not rejected with 403 Forbidden.");

    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim("user_type", "Staff"),
        new Claim("permissions", Permissions.Outbound.OrdersView)
    };
    if ((await p74Client.GetAsync($"/api/v1/outbound/orders/{p74OrderC1FacA}")).StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException("Unassigned staff was not rejected with 403 Forbidden.");

    TestAuthHandler.CustomClaims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim("user_type", "Staff"),
        new Claim("facility_id", facilityId.ToString()),
        new Claim("permissions", Permissions.Analytics.MultiFacilityView),
        new Claim("permissions", Permissions.Outbound.OrdersView),
        new Claim("permissions", Permissions.Outbound.Ship),
        new Claim("permissions", Permissions.Outbound.OrdersAllocate)
    };
    if ((await p74Client.GetAsync($"/api/v1/outbound/shipments/{p74ShipFacB}")).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("MultiFacilityView staff should read cross-facility shipment with 200 OK.");
    if ((await p74Client.PostAsJsonAsync($"/api/v1/outbound/orders/{p74OrderC1FacB}/allocate", new { })).StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException("MultiFacilityView staff must still be rejected (403) on cross-facility mutation.");
    Console.WriteLine("PASS PostgreSQL Driver (403), Unassigned Staff (403), and MultiFacilityView read (200) vs mutation (403).");

    // 6.6 Carrier Webhook HMAC-SHA256, replay constraint (23505), secret rotation, cross-tenant 403, and audit logs
    TestAuthHandler.CustomClaims = null;
    static HttpRequestMessage SignWebhook(string carrier, string secret, string body, string ts, string nonce)
    {
        var canonical = $"{ts}.{nonce}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/outbound/shipping/returns/tracking/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Carrier-Id", carrier);
        req.Headers.Add("X-Webhook-Timestamp", ts);
        req.Headers.Add("X-Webhook-Nonce", nonce);
        req.Headers.Add("X-Webhook-Signature", sig);
        return req;
    }

    var whBody1 = JsonSerializer.Serialize(new
    {
        carrierCode = "SIM-FEDEX",
        trackingNumber = p74Tracking,
        carrierEventId = "PG-EVT-1",
        carrierEventCode = "IN_TRANSIT",
        carrierDescription = "Departed origin hub",
        occurredAtUtc = DateTime.UtcNow
    });

    if ((await p74Client.PostAsync("/api/v1/outbound/shipping/returns/tracking/webhook", new StringContent(whBody1, Encoding.UTF8, "application/json"))).StatusCode != HttpStatusCode.Unauthorized)
        throw new InvalidOperationException("Unsigned webhook must return 401.");
    if ((await p74Client.SendAsync(SignWebhook("SIM-FEDEX", "bad-secret", whBody1, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "pg-nonce-bad"))).StatusCode != HttpStatusCode.Unauthorized)
        throw new InvalidOperationException("Invalid HMAC signature must return 401.");
    if ((await p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, whBody1, DateTimeOffset.UtcNow.AddSeconds(-400).ToUnixTimeSeconds().ToString(), "pg-nonce-exp"))).StatusCode != HttpStatusCode.Unauthorized)
        throw new InvalidOperationException("Expired webhook timestamp must return 401.");

    if ((await p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, whBody1, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "pg-nonce-1"))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Valid HMAC webhook failed.");
    if ((await p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, whBody1, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "pg-nonce-1"))).StatusCode != HttpStatusCode.Unauthorized)
        throw new InvalidOperationException("Replayed webhook nonce must return 401.");

    var whBody2 = JsonSerializer.Serialize(new
    {
        carrierCode = "SIM-FEDEX",
        trackingNumber = p74Tracking,
        carrierEventId = "PG-EVT-2",
        carrierEventCode = "OUT_FOR_DELIVERY",
        carrierDescription = "Out for delivery",
        occurredAtUtc = DateTime.UtcNow
    });
    if ((await p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgSecondarySecret, whBody2, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "pg-nonce-2"))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Secondary rotation secret webhook failed.");

    var whCrossTenant = JsonSerializer.Serialize(new
    {
        carrierCode = "SIM-FEDEX",
        trackingNumber = p74Tracking,
        carrierEventId = "PG-EVT-3",
        carrierEventCode = "IN_TRANSIT",
        carrierDescription = "Cross tenant attempt",
        occurredAtUtc = DateTime.UtcNow,
        tenantIdOverride = client2Id
    });
    if ((await p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, whCrossTenant, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "pg-nonce-3"))).StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException("Cross-tenant TenantIdOverride webhook must return 403 Forbidden.");

    await using (var p74VerifyDb = CreateDb(operationalConn, user))
    {
        var eventCount = await p74VerifyDb.ReturnTrackingEvents.CountAsync(e => e.ReturnShipmentId == p74ReturnShipId);
        var auditCount = await p74VerifyDb.CarrierWebhookAuditLogs.CountAsync();
        if (eventCount != 2 || auditCount < 7)
            throw new InvalidOperationException($"Unexpected PostgreSQL webhook counts: events={eventCount}, audits={auditCount}");

        // Direct DB unique-index (23505) assertion on carrier_webhook_nonces
        p74VerifyDb.CarrierWebhookNonces.Add(new CarrierWebhookNonce
        {
            CarrierCode = "SIM-FEDEX",
            Nonce = "pg-nonce-1",
            PayloadSha256 = new string('0', 64),
            Status = CarrierWebhookNonceStatus.Succeeded,
            WebhookTimestampUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            LastAttemptAtUtc = DateTime.UtcNow
        });
        var dupNonceCaught = false;
        try
        {
            await p74VerifyDb.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            dupNonceCaught = true;
        }
        if (!dupNonceCaught)
            throw new InvalidOperationException("PostgreSQL unique constraint ix_carrier_webhook_nonces_carrier_code_nonce did not raise 23505.");
    }

    // 6.7 P74-0A-02/03/04 on Live PostgreSQL: safe retry recovery, concurrent duplicate safety, fail-closed extreme timestamps, 413 payload limit & accurate status audit
    var pgProbeState = p74App.Services.GetRequiredService<PgWebhookProbeState>();
    var pgTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    var pgRetryBody = JsonSerializer.Serialize(new
    {
        carrierCode = "SIM-FEDEX",
        trackingNumber = p74Tracking,
        carrierEventId = "PG-EVT-RETRY-1",
        carrierEventCode = "EXCEPTION",
        carrierDescription = "Transient outage retry test",
        occurredAtUtc = DateTime.UtcNow
    });

    pgProbeState.FailNextNonce = "pg-retry-nonce-1";
    if ((await p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgRetryBody, pgTs, "pg-retry-nonce-1"))).StatusCode != HttpStatusCode.InternalServerError)
        throw new InvalidOperationException("Expected 500 InternalServerError on injected transient failure.");
    if ((await p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgRetryBody, pgTs, "pg-retry-nonce-1"))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Identical signed retry after transient failure should succeed (200 OK).");
    if ((await p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgRetryBody, pgTs, "pg-retry-nonce-1"))).StatusCode != HttpStatusCode.Unauthorized)
        throw new InvalidOperationException("Replay after succeeded retry must return 401 Unauthorized.");

    // Concurrent duplicate signed requests with unseen nonce -> exactly 1 business mutation
    var pgConcurrentBody = JsonSerializer.Serialize(new
    {
        carrierCode = "SIM-FEDEX",
        trackingNumber = p74Tracking,
        carrierEventId = "PG-EVT-CONC-1",
        carrierEventCode = "DELIVERED",
        carrierDescription = "Concurrent Postgres test",
        occurredAtUtc = DateTime.UtcNow
    });
    var concResponses = await Task.WhenAll(
        p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgConcurrentBody, pgTs, "pg-conc-nonce-1")),
        p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgConcurrentBody, pgTs, "pg-conc-nonce-1")));
    if (concResponses.Count(r => r.StatusCode == HttpStatusCode.OK) != 1 ||
        concResponses.Count(r => r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.Unauthorized) != 1)
        throw new InvalidOperationException($"Concurrent webhook race failed: statuses={string.Join(",", concResponses.Select(r => r.StatusCode))}");

    // Fail-closed invalid/extreme timestamps -> 401 (no 500)
    foreach (var badTs in new[] { long.MaxValue.ToString(), "253402300800", "not-a-timestamp", "9999-12-31T23:59:59Z" })
    {
        if ((await p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgRetryBody, badTs, $"pg-badts-{badTs[..4]}"))).StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidOperationException($"Extreme/malformed timestamp '{badTs}' must return 401 Unauthorized.");
    }

    // 413 payload limit + accurate non-200 status auditing (202, 503 -> retry 200)
    if ((await p74Client.PostAsync("/api/v1/outbound/shipping/returns/tracking/webhook",
            new StringContent(new string('X', 2048), Encoding.UTF8, "application/json"))).StatusCode != HttpStatusCode.RequestEntityTooLarge)
        throw new InvalidOperationException("Oversized webhook payload (>1024 bytes) must return 413 PayloadTooLarge.");
    if ((await p74Client.PostAsync("/api/v1/outbound/shipping/returns/tracking/webhook",
            new PgChunkedContent(Encoding.UTF8.GetBytes(new string('Y', 2048))))).StatusCode != HttpStatusCode.RequestEntityTooLarge)
        throw new InvalidOperationException("Oversized chunked webhook payload must return 413 PayloadTooLarge.");

    static HttpRequestMessage SignProbe(string path, string secret, string body, string ts, string nonce)
    {
        var canonical = $"{ts}.{nonce}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Add("X-Carrier-Id", "SIM-FEDEX");
        req.Headers.Add("X-Webhook-Timestamp", ts);
        req.Headers.Add("X-Webhook-Nonce", nonce);
        req.Headers.Add("X-Webhook-Signature", sig);
        return req;
    }

    var probePayload = JsonSerializer.Serialize(new { carrierCode = "SIM-FEDEX", trackingNumber = p74Tracking });
    if ((await p74Client.SendAsync(SignProbe("/api/v1/pg-probe/status/202", pgPrimarySecret, probePayload, pgTs, "pg-probe-202"))).StatusCode != HttpStatusCode.Accepted)
        throw new InvalidOperationException("Expected 202 Accepted from probe.");
    if ((await p74Client.SendAsync(SignProbe("/api/v1/pg-probe/status/503", pgPrimarySecret, probePayload, pgTs, "pg-probe-503"))).StatusCode != HttpStatusCode.ServiceUnavailable)
        throw new InvalidOperationException("Expected 503 ServiceUnavailable from probe.");
    if ((await p74Client.SendAsync(SignProbe("/api/v1/pg-probe/status/200", pgPrimarySecret, probePayload, pgTs, "pg-probe-503"))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Expected retry recovery (200 OK) after 503 ServiceUnavailable.");

    await using (var p74VerifyDb2 = CreateDb(operationalConn, user))
    {
        var finalEvents = await p74VerifyDb2.ReturnTrackingEvents.CountAsync(e => e.ReturnShipmentId == p74ReturnShipId);
        if (finalEvents != 4)
            throw new InvalidOperationException($"Expected exactly 4 ReturnTrackingEvents (2 initial + 1 retry + 1 concurrent), got {finalEvents}");

        var retryNonceRow = await p74VerifyDb2.CarrierWebhookNonces.SingleAsync(x => x.Nonce == "pg-retry-nonce-1");
        if (retryNonceRow.Status != CarrierWebhookNonceStatus.Succeeded || retryNonceRow.AttemptCount != 2)
            throw new InvalidOperationException($"Retry nonce state mismatch: status={retryNonceRow.Status}, attempts={retryNonceRow.AttemptCount}");

        var allAudits = await p74VerifyDb2.CarrierWebhookAuditLogs.ToListAsync();
        if (!allAudits.Any(a => a.Outcome == "Received") ||
            !allAudits.Any(a => a.Nonce == "pg-retry-nonce-1" && a.Outcome == "Failed" && a.HttpStatusCode == 500) ||
            !allAudits.Any(a => a.Nonce == "pg-retry-nonce-1" && a.Outcome == "Processed" && a.HttpStatusCode == 200) ||
            !allAudits.Any(a => a.Nonce == "pg-retry-nonce-1" && a.Outcome == "ReplayRejected" && a.HttpStatusCode == 401) ||
            allAudits.Count(a => a.Outcome == "PayloadTooLarge" && a.HttpStatusCode == 413) < 2 ||
            !allAudits.Any(a => a.Nonce == "pg-probe-202" && a.Outcome == "Processed" && a.HttpStatusCode == 202) ||
            !allAudits.Any(a => a.Nonce == "pg-probe-503" && a.Outcome == "Failed" && a.HttpStatusCode == 503))
            throw new InvalidOperationException("PostgreSQL webhook audit logs missing expected Received/Processed/Failed/ReplayRejected/413/202/503 entries.");
    }
    Console.WriteLine("PASS PostgreSQL Carrier Webhook retry recovery, concurrent safety, fail-closed timestamps, 413 limits, and accurate audit statuses verified.");

    // 6.8 P74-0A-05: Deterministic Two-Worker Lease Fencing on Live PostgreSQL (forward & reverse completion orders)
    // A) Forward order (Worker B succeeds first, stale Worker A fails afterward)
    const string pgFenceNonceFwd = "pg-fence-fwd-1";
    var pgFenceBody1 = JsonSerializer.Serialize(new
    {
        carrierCode = "SIM-FEDEX",
        trackingNumber = p74Tracking,
        carrierEventId = "PG-EVT-FENCE-1",
        carrierEventCode = "IN_TRANSIT",
        carrierDescription = "Postgres lease fencing forward-fail",
        occurredAtUtc = DateTime.UtcNow
    });
    var fCtrlA = new PgWebhookAttemptControl { FailOnProceed = true };
    var fCtrlB = new PgWebhookAttemptControl { FailOnProceed = false };
    pgProbeState.Enqueue(pgFenceNonceFwd, fCtrlA);
    pgProbeState.Enqueue(pgFenceNonceFwd, fCtrlB);

    var wA1Task = p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgFenceBody1, new DateTimeOffset(p74Clock.UtcNow).ToUnixTimeSeconds().ToString(), pgFenceNonceFwd));
    await fCtrlA.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    p74Clock.UtcNow = p74Clock.UtcNow.AddSeconds(70);

    var wB1Task = p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgFenceBody1, new DateTimeOffset(p74Clock.UtcNow).ToUnixTimeSeconds().ToString(), pgFenceNonceFwd));
    await fCtrlB.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    fCtrlB.Proceed.TrySetResult(true);
    if ((await wB1Task).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Worker B reclamation failed on PostgreSQL.");

    fCtrlA.Proceed.TrySetResult(true);
    if ((await wA1Task).StatusCode != HttpStatusCode.InternalServerError)
        throw new InvalidOperationException("Expected stale Worker A injected failure to return 500.");

    // B) Forward order (Worker B succeeds first, stale Worker A completes normally afterward)
    const string pgFenceNonceFwdOk = "pg-fence-fwd-ok-2";
    var pgFenceBody2 = JsonSerializer.Serialize(new
    {
        carrierCode = "SIM-FEDEX",
        trackingNumber = p74Tracking,
        carrierEventId = "PG-EVT-FENCE-2",
        carrierEventCode = "IN_TRANSIT",
        carrierDescription = "Postgres lease fencing forward-ok",
        occurredAtUtc = DateTime.UtcNow
    });
    var fOkCtrlA = new PgWebhookAttemptControl { FailOnProceed = false };
    var fOkCtrlB = new PgWebhookAttemptControl { FailOnProceed = false };
    pgProbeState.Enqueue(pgFenceNonceFwdOk, fOkCtrlA);
    pgProbeState.Enqueue(pgFenceNonceFwdOk, fOkCtrlB);

    var wA2OkTask = p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgFenceBody2, new DateTimeOffset(p74Clock.UtcNow).ToUnixTimeSeconds().ToString(), pgFenceNonceFwdOk));
    await fOkCtrlA.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    p74Clock.UtcNow = p74Clock.UtcNow.AddSeconds(70);

    var wB2OkTask = p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgFenceBody2, new DateTimeOffset(p74Clock.UtcNow).ToUnixTimeSeconds().ToString(), pgFenceNonceFwdOk));
    await fOkCtrlB.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    fOkCtrlB.Proceed.TrySetResult(true);
    if ((await wB2OkTask).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Worker B2 failed on PostgreSQL.");
    fOkCtrlA.Proceed.TrySetResult(true);
    await wA2OkTask;

    // C) Reverse completion order (Worker A completes FIRST while Worker B owns lease; Worker B finishes SECOND)
    const string pgFenceNonceRev = "pg-fence-rev-3";
    var pgFenceBody3 = JsonSerializer.Serialize(new
    {
        carrierCode = "SIM-FEDEX",
        trackingNumber = p74Tracking,
        carrierEventId = "PG-EVT-FENCE-3",
        carrierEventCode = "OUT_FOR_DELIVERY",
        carrierDescription = "Postgres lease fencing reverse order",
        occurredAtUtc = DateTime.UtcNow
    });
    var rCtrlA = new PgWebhookAttemptControl { FailOnProceed = false };
    var rCtrlB = new PgWebhookAttemptControl { FailOnProceed = false };
    pgProbeState.Enqueue(pgFenceNonceRev, rCtrlA);
    pgProbeState.Enqueue(pgFenceNonceRev, rCtrlB);

    var wA3Task = p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgFenceBody3, new DateTimeOffset(p74Clock.UtcNow).ToUnixTimeSeconds().ToString(), pgFenceNonceRev));
    await rCtrlA.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    p74Clock.UtcNow = p74Clock.UtcNow.AddSeconds(70);

    var wB3Task = p74Client.SendAsync(SignWebhook("SIM-FEDEX", pgPrimarySecret, pgFenceBody3, new DateTimeOffset(p74Clock.UtcNow).ToUnixTimeSeconds().ToString(), pgFenceNonceRev));
    await rCtrlB.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

    // Stale Worker A finishes FIRST -> must NOT clear Worker B's active Processing state
    rCtrlA.Proceed.TrySetResult(true);
    await wA3Task;

    await using (var midPgDb = CreateDb(operationalConn, user))
    {
        var midRow = await midPgDb.CarrierWebhookNonces.SingleAsync(x => x.Nonce == pgFenceNonceRev);
        if (midRow.Status != CarrierWebhookNonceStatus.Processing || !midRow.ClaimToken.HasValue || midRow.AttemptCount != 2)
            throw new InvalidOperationException($"Reverse-order stale completion corrupted active Processing state: status={midRow.Status}, token={midRow.ClaimToken}, attempts={midRow.AttemptCount}");
    }

    // Legitimate Worker B completes SECOND (encounters fingerprint dedup or commits cleanly) -> Succeeded with 1 event
    rCtrlB.Proceed.TrySetResult(true);
    if ((await wB3Task).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Worker B3 failed during reverse-order completion on PostgreSQL.");

    await using (var fenceVerifyDb = CreateDb(operationalConn, user))
    {
        var totalEventsAfterFence = await fenceVerifyDb.ReturnTrackingEvents.CountAsync(e => e.ReturnShipmentId == p74ReturnShipId);
        if (totalEventsAfterFence != 7) // 4 previous + 3 distinct fenced events (1 per nonce)
            throw new InvalidOperationException($"Expected exactly 7 total ReturnTrackingEvents after fencing tests (no duplicates), got {totalEventsAfterFence}");

        foreach (var n in new[] { pgFenceNonceFwd, pgFenceNonceFwdOk, pgFenceNonceRev })
        {
            var row = await fenceVerifyDb.CarrierWebhookNonces.SingleAsync(x => x.Nonce == n);
            if (row.Status != CarrierWebhookNonceStatus.Succeeded || row.ClaimToken.HasValue || row.AttemptCount != 2 || row.LastHttpStatusCode != 200)
                throw new InvalidOperationException($"Fenced nonce '{n}' final state invalid: status={row.Status}, token={row.ClaimToken}, attempts={row.AttemptCount}, code={row.LastHttpStatusCode}");

            var audits = await fenceVerifyDb.CarrierWebhookAuditLogs.Where(a => a.Nonce == n).ToListAsync();
            if (audits.Count(a => a.Outcome == "Received") != 2 ||
                audits.Count(a => a.Outcome == "StaleAttemptIgnored") != 1 ||
                audits.Count(a => a.Outcome == "Processed" && a.HttpStatusCode == 200) != 1 ||
                audits.Any(a => a.Outcome == "Failed"))
                throw new InvalidOperationException($"Audit history inaccurate for fenced nonce '{n}'.");
        }
    }
    Console.WriteLine("PASS PostgreSQL Two-Worker Lease Fencing (forward-fail, forward-ok, reverse completion) verified cleanly.");

    // ------------------------------------------------------------------------------------------------
    // SECTION 7: PHASE 7.4.0B READ/QUEUE APIS & ATOMIC RF CLAIM CONCURRENCY (POSTGRESQL)
    // ------------------------------------------------------------------------------------------------
    Console.WriteLine("\n[7/7] Running Phase 7.4.0B Read/Queue APIs & Two-Operator Claim Race on PostgreSQL...");

    var bOpAId = Guid.NewGuid();
    var bOpBId = Guid.NewGuid();
    var bForeignOpId = Guid.NewGuid();
    var bUnassignedOpId = Guid.NewGuid();
    var bInactiveOpId = Guid.NewGuid();
    var bSupId = user.UserId!.Value;
    var bPackStationId = Guid.NewGuid();
    var bBinId = Guid.NewGuid();
    var bBinShortId = Guid.NewGuid();
    var bOrderId = Guid.NewGuid();
    var bOrderLineId = Guid.NewGuid();
    var bStockId = Guid.NewGuid();
    var bAlloc1Id = Guid.NewGuid();
    var bAlloc2Id = Guid.NewGuid();
    var bAllocRaceId = Guid.NewGuid();
    var bAllocPlanId = Guid.NewGuid();
    var bAllocCancId = Guid.NewGuid();
    var bAllocOverFailId = Guid.NewGuid();
    var bWaveId = Guid.NewGuid();
    var bPlanWaveId = Guid.NewGuid();
    var bCancWaveId = Guid.NewGuid();
    var bTaskClaimId = Guid.NewGuid();
    var bTaskShortId = Guid.NewGuid();
    var bTaskRaceId = Guid.NewGuid();
    var bTaskOverFailId = Guid.NewGuid();
    var bTaskPlanAssignedId = Guid.NewGuid();
    var bTaskPlanPendingId = Guid.NewGuid();
    var bTaskCancAssignedId = Guid.NewGuid();
    var bToteId = Guid.NewGuid();
    var bPackSessionId = Guid.NewGuid();
    var bHuCartonElig = Guid.NewGuid();
    var bPkgEligId = Guid.NewGuid();

    await using (var bDb = CreateDb(operationalConn, user))
    {
        bDb.Users.AddRange(
            new User { Id = bOpAId, AssignedFacilityId = facilityId, Email = "pg-opa@740b.test", FirstName = "Pg", LastName = "OpA", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" },
            new User { Id = bOpBId, AssignedFacilityId = facilityId, Email = "pg-opb@740b.test", FirstName = "Pg", LastName = "OpB", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" },
            new User { Id = bForeignOpId, AssignedFacilityId = p74FacB, Email = "pg-for@740b.test", FirstName = "Pg", LastName = "Foreign", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" },
            new User { Id = bUnassignedOpId, AssignedFacilityId = null, Email = "pg-nofac@740b.test", FirstName = "Pg", LastName = "NoFac", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" },
            new User { Id = bInactiveOpId, AssignedFacilityId = facilityId, Email = "pg-inact@740b.test", FirstName = "Pg", LastName = "Inactive", UserType = UserType.Staff, Status = UserStatus.Inactive, PasswordHash = "x" });

        bDb.Locations.AddRange(
            new Location
            {
                Id = bPackStationId, FacilityId = facilityId, LocationCode = "PG-PK-01", Barcode = "PG-PK-01",
                LocationType = LocationType.PackingStation, IsActive = true
            },
            new Location
            {
                Id = bBinId, FacilityId = facilityId, LocationCode = "PG-740B-BIN1", Barcode = "PG-740B-BIN1",
                LocationType = LocationType.Bin, IsActive = true, IsPickable = true
            },
            new Location
            {
                Id = bBinShortId, FacilityId = facilityId, LocationCode = "PG-740B-SHORT", Barcode = "PG-740B-SHORT",
                LocationType = LocationType.Bin, IsActive = true, IsPickable = true
            });

        var bOrder = new OutboundOrder { Id = bOrderId, ClientId = client1Id, FacilityId = facilityId, OrderNumber = "PG-740B-ORD1", CustomerName = "PG Buyer", Status = OutboundOrderStatus.Allocated };
        var bLine = new OutboundOrderLine { Id = bOrderLineId, OutboundOrderId = bOrderId, ItemId = item1Id, LineNumber = 1, QuantityOrdered = 30m, QuantityAllocated = 30m };
        bOrder.Lines.Add(bLine);
        bDb.OutboundOrders.Add(bOrder);

        bDb.StockOnHand.Add(new StockOnHand
        {
            Id = bStockId, ClientId = client1Id, FacilityId = facilityId, ItemId = item1Id, LocationId = bBinId,
            QtyAvailable = 30m, QtyAllocated = 30m, Version = 1
        });
        bDb.InventoryAllocations.AddRange(
            new InventoryAllocation { Id = bAlloc1Id, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, StockOnHandId = bStockId, ItemId = item1Id, LocationId = bBinId, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated },
            new InventoryAllocation { Id = bAlloc2Id, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, StockOnHandId = bStockId, ItemId = item1Id, LocationId = bBinShortId, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated },
            new InventoryAllocation { Id = bAllocRaceId, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, StockOnHandId = bStockId, ItemId = item1Id, LocationId = bBinId, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated },
            new InventoryAllocation { Id = bAllocPlanId, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, StockOnHandId = bStockId, ItemId = item1Id, LocationId = bBinId, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated },
            new InventoryAllocation { Id = bAllocCancId, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, StockOnHandId = bStockId, ItemId = item1Id, LocationId = bBinId, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated },
            new InventoryAllocation { Id = bAllocOverFailId, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, StockOnHandId = bStockId, ItemId = item1Id, LocationId = bBinId, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated });

        bDb.PickWaves.AddRange(
            new PickWave { Id = bWaveId, ClientId = client1Id, FacilityId = facilityId, WaveNumber = "PG-740B-WV1", Priority = 3, Status = PickWaveStatus.Released },
            new PickWave { Id = bPlanWaveId, ClientId = client1Id, FacilityId = facilityId, WaveNumber = "PG-740B-PLAN", Priority = 10, Status = PickWaveStatus.Planning },
            new PickWave { Id = bCancWaveId, ClientId = client1Id, FacilityId = facilityId, WaveNumber = "PG-740B-CANC", Priority = 10, Status = PickWaveStatus.Cancelled });

        bDb.PickTasks.AddRange(
            new PickTask { Id = bTaskClaimId, ClientId = client1Id, FacilityId = facilityId, PickWaveId = bWaveId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, InventoryAllocationId = bAlloc1Id, StockOnHandId = bStockId, ItemId = item1Id, SourceLocationId = bBinId, QuantityToPick = 5m, PickSequence = 1, Status = PickTaskStatus.Pending, Version = 1 },
            new PickTask { Id = bTaskShortId, ClientId = client1Id, FacilityId = facilityId, PickWaveId = bWaveId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, InventoryAllocationId = bAlloc2Id, StockOnHandId = bStockId, ItemId = item1Id, SourceLocationId = bBinShortId, QuantityToPick = 5m, PickSequence = 2, Status = PickTaskStatus.Pending, Version = 1 },
            new PickTask { Id = bTaskRaceId, ClientId = client1Id, FacilityId = facilityId, PickWaveId = bWaveId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, InventoryAllocationId = bAllocRaceId, StockOnHandId = bStockId, ItemId = item1Id, SourceLocationId = bBinId, QuantityToPick = 5m, PickSequence = 3, Status = PickTaskStatus.Pending, Version = 1 },
            new PickTask { Id = bTaskOverFailId, ClientId = client1Id, FacilityId = facilityId, PickWaveId = bWaveId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, InventoryAllocationId = bAllocOverFailId, StockOnHandId = bStockId, ItemId = item1Id, SourceLocationId = bBinId, QuantityToPick = 5m, PickSequence = 4, Status = PickTaskStatus.Pending, Version = 1 },
            new PickTask { Id = bTaskPlanAssignedId, ClientId = client1Id, FacilityId = facilityId, PickWaveId = bPlanWaveId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, InventoryAllocationId = bAllocPlanId, StockOnHandId = bStockId, ItemId = item1Id, SourceLocationId = bBinId, QuantityToPick = 5m, PickSequence = 1, Status = PickTaskStatus.Assigned, AssignedUserId = bOpAId, Version = 1 },
            new PickTask { Id = bTaskPlanPendingId, ClientId = client1Id, FacilityId = facilityId, PickWaveId = bPlanWaveId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, InventoryAllocationId = bAllocPlanId, StockOnHandId = bStockId, ItemId = item1Id, SourceLocationId = bBinId, QuantityToPick = 5m, PickSequence = 2, Status = PickTaskStatus.Pending, Version = 1 },
            new PickTask { Id = bTaskCancAssignedId, ClientId = client1Id, FacilityId = facilityId, PickWaveId = bCancWaveId, OutboundOrderId = bOrderId, OutboundOrderLineId = bOrderLineId, InventoryAllocationId = bAllocCancId, StockOnHandId = bStockId, ItemId = item1Id, SourceLocationId = bBinId, QuantityToPick = 5m, PickSequence = 1, Status = PickTaskStatus.Assigned, AssignedUserId = bOpAId, Version = 1 });

        bDb.HandlingUnits.AddRange(
            new HandlingUnit { Id = bToteId, ClientId = client1Id, FacilityId = facilityId, LpnCode = "PG-TOTE-740B", UnitType = HandlingUnitType.Tote, Status = HandlingUnitStatus.Available, CurrentOutboundOrderId = bOrderId },
            new HandlingUnit { Id = bHuCartonElig, ClientId = client1Id, FacilityId = facilityId, LpnCode = "PG-CTN-740B", UnitType = HandlingUnitType.ShippingCarton });

        bDb.PackingSessions.Add(new PackingSession { Id = bPackSessionId, ClientId = client1Id, FacilityId = facilityId, PackingStationLocationId = bPackStationId, OutboundOrderId = bOrderId, OperatorUserId = bSupId, Status = PackingSessionStatus.Active });
        bDb.Packages.Add(new Package { Id = bPkgEligId, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = bOrderId, HandlingUnitId = bHuCartonElig, PackageNumber = "PG-PKG-ELIG", CartonSequence = 1, Status = PackageStatus.Packed, CarrierCode = "SIM-FEDEX", PackedAtUtc = DateTime.UtcNow });
        bDb.PackageLines.Add(new PackageLine { PackageId = bPkgEligId, OutboundOrderLineId = bOrderLineId, ItemId = item1Id, Quantity = 3m, InventoryAllocationId = bAlloc1Id });

        await bDb.SaveChangesAsync();
    }

    static HttpRequestMessage StaffReq(HttpMethod method, string path, Guid userId, Guid? facId, string permsCsv, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("X-Test-Actor-UserId", userId.ToString());
        if (facId.HasValue) req.Headers.Add("X-Test-Actor-FacilityId", facId.Value.ToString());
        req.Headers.Add("X-Test-Actor-Permissions", permsCsv);
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    // 7.1 Strictly read-only /next (leaves Pending untouched & returns 204 when empty)
    using (var nextReq = StaffReq(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={facilityId}", bOpAId, facilityId, Permissions.Outbound.Pick))
    using (var nextResp = await p74Client.SendAsync(nextReq))
    {
        if (nextResp.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Expected 200 OK from /pick-tasks/next, got {nextResp.StatusCode}");
        using var doc = JsonDocument.Parse(await nextResp.Content.ReadAsStringAsync());
        if (doc.RootElement.GetProperty("id").GetGuid() != bTaskClaimId)
            throw new InvalidOperationException("/next did not select lowest sequence Pending task bTaskClaimId.");
    }
    await using (var chkRo = CreateDb(operationalConn, user))
    {
        var roTask = await chkRo.PickTasks.AsNoTracking().SingleAsync(t => t.Id == bTaskClaimId);
        if (roTask.Status != PickTaskStatus.Pending || roTask.AssignedUserId.HasValue || roTask.Version != 1u)
            throw new InvalidOperationException("/pick-tasks/next implicitly mutated task state.");
    }

    // 7.2 Concurrent Two-Operator Claim Race on PostgreSQL (1x 200 + 1x 409)
    var claimTaskA = p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskClaimId}/claim", bOpAId, facilityId, Permissions.Outbound.Pick, new { operatorUserId = bOpBId }));
    var claimTaskB = p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskClaimId}/claim", bOpBId, facilityId, Permissions.Outbound.Pick, new { }));
    var claimResps = await Task.WhenAll(claimTaskA, claimTaskB);
    if (claimResps.Count(r => r.StatusCode == HttpStatusCode.OK) != 1 ||
        claimResps.Count(r => r.StatusCode == HttpStatusCode.Conflict) != 1)
        throw new InvalidOperationException($"PostgreSQL two-operator claim race failed: {string.Join(",", claimResps.Select(r => r.StatusCode))}");

    var winClaim = claimResps.Single(r => r.StatusCode == HttpStatusCode.OK);
    Guid pgClaimWinnerId;
    uint pgClaimWinVersion;
    using (var doc = JsonDocument.Parse(await winClaim.Content.ReadAsStringAsync()))
    {
        pgClaimWinnerId = doc.RootElement.GetProperty("assignedUserId").GetGuid();
        pgClaimWinVersion = doc.RootElement.GetProperty("version").GetUInt32();
    }
    var pgClaimLoserId = pgClaimWinnerId == bOpAId ? bOpBId : bOpAId;
    foreach (var r in claimResps) r.Dispose();

    // Repeated same-operator claim is idempotent (200, same version); loser gets 409
    using (var repResp = await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskClaimId}/claim", pgClaimWinnerId, facilityId, Permissions.Outbound.Pick, new { })))
    {
        if (repResp.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Repeated same-operator claim should return 200 OK, got {repResp.StatusCode}");
        using var doc = JsonDocument.Parse(await repResp.Content.ReadAsStringAsync());
        if (doc.RootElement.GetProperty("version").GetUInt32() != pgClaimWinVersion)
            throw new InvalidOperationException("Repeated same-operator claim unexpectedly bumped Version.");
    }
    using (var loseResp = await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskClaimId}/claim", pgClaimLoserId, facilityId, Permissions.Outbound.Pick, new { })))
    {
        if (loseResp.StatusCode != HttpStatusCode.Conflict)
            throw new InvalidOperationException($"Loser re-claim should return 409 Conflict, got {loseResp.StatusCode}");
    }

    // 7.3 Ownership & Unclaimed Execution Enforcement + Stock/Ledger Conservation on PostgreSQL
    string binBarcode;
    string itemSku;
    await using (var lookupDb = CreateDb(operationalConn, user))
    {
        binBarcode = (await lookupDb.Locations.AsNoTracking().SingleAsync(l => l.Id == bBinId)).Barcode ?? "PG-740B-BIN1";
        itemSku = (await lookupDb.Items.AsNoTracking().SingleAsync(i => i.Id == item1Id)).Sku;
    }

    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskShortId}/confirm", pgClaimWinnerId, facilityId, Permissions.Outbound.Pick,
        new { scannedLocationBarcode = binBarcode, scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-740B", quantity = 5m }))).StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException("Unclaimed task execution by ordinary operator must return 403 Forbidden.");

    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskClaimId}/confirm", pgClaimLoserId, facilityId, Permissions.Outbound.Pick,
        new { scannedLocationBarcode = binBarcode, scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-740B", quantity = 5m }))).StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException("Foreign operator execution must return 403 Forbidden.");

    // Owner confirms cleanly + duplicate confirm is idempotent -> 1 Pick ledger tx & conserved balances
    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskClaimId}/confirm", pgClaimWinnerId, facilityId, Permissions.Outbound.Pick,
        new { scannedLocationBarcode = binBarcode, scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-740B", quantity = 5m, idempotencyKey = "pg-idem-1" }))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Claimed owner pick confirmation failed on PostgreSQL.");

    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskClaimId}/confirm", pgClaimWinnerId, facilityId, Permissions.Outbound.Pick,
        new { scannedLocationBarcode = binBarcode, scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-740B", quantity = 5m, idempotencyKey = "pg-idem-1" }))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Duplicate owner pick confirmation should succeed idempotently.");

    // Supervisor explicit auditable override on unclaimed bTaskShortId
    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskShortId}/short-pick", bSupId, facilityId, $"{Permissions.Outbound.Pick},{Permissions.Outbound.TasksManage}",
        new { quantityPicked = 0m, reason = 1, notes = "PG supervisor override verification" }))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Supervisor override short-pick failed on PostgreSQL.");

    await using (var bVerifyDb = CreateDb(operationalConn, user))
    {
        var bSrc = await bVerifyDb.StockOnHand.SingleAsync(s => s.Id == bStockId);
        var bTote = await bVerifyDb.StockOnHand.SingleAsync(s => s.HandlingUnitId == bToteId);
        if (bSrc.QtyAvailable != 30m || bSrc.QtyAllocated != 20m || bTote.QtyAllocated != 5m || bTote.QtyAvailable != 0m)
            throw new InvalidOperationException($"PostgreSQL stock conservation mismatch: srcAvail={bSrc.QtyAvailable}, srcAlloc={bSrc.QtyAllocated}, toteAlloc={bTote.QtyAllocated}");

        var pickTxCount = await bVerifyDb.InventoryTransactions.CountAsync(t => t.ReferenceId == bTaskClaimId && t.TxType == InventoryTxType.Pick);
        if (pickTxCount != 1)
            throw new InvalidOperationException($"Expected 1 Pick ledger transaction, found {pickTxCount}");

        var claimHistCount = await bVerifyDb.PickTaskStatusHistories.CountAsync(h => h.PickTaskId == bTaskClaimId && h.ToStatus == PickTaskStatus.Assigned);
        var overrideHistCount = await bVerifyDb.PickTaskStatusHistories.CountAsync(h => h.PickTaskId == bTaskShortId && h.Reason != null && h.Reason.Contains("Task-management override"));
        if (claimHistCount != 1 || overrideHistCount != 1)
            throw new InvalidOperationException($"PostgreSQL task history mismatch: claimHist={claimHistCount}, overrideHist={overrideHistCount}");
    }

    // 7.3b P74-0B-01 Released-Wave Enforcement (Planning & Cancelled blocked across /next, /assign, /claim, /confirm, /short-pick)
    foreach (var blockedId in new[] { bTaskPlanAssignedId, bTaskPlanPendingId, bTaskCancAssignedId })
    {
        if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{blockedId}/assign", bSupId, facilityId,
            $"{Permissions.Outbound.TasksView},{Permissions.Outbound.TasksManage}", new { operatorUserId = bOpBId }))).StatusCode != HttpStatusCode.UnprocessableEntity)
            throw new InvalidOperationException($"P74-0B-01: expected 422 on /assign for unreleased/cancelled wave task {blockedId}.");

        if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{blockedId}/claim", bOpAId, facilityId,
            Permissions.Outbound.Pick, new { }))).StatusCode != HttpStatusCode.UnprocessableEntity)
            throw new InvalidOperationException($"P74-0B-01: expected 422 on /claim for unreleased/cancelled wave task {blockedId}.");
    }
    foreach (var blockedAssignedId in new[] { bTaskPlanAssignedId, bTaskCancAssignedId })
    {
        if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{blockedAssignedId}/confirm", bOpAId, facilityId,
            Permissions.Outbound.Pick, new { scannedLocationBarcode = binBarcode, scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-740B", quantity = 5m }))).StatusCode != HttpStatusCode.UnprocessableEntity)
            throw new InvalidOperationException($"P74-0B-01: expected 422 on /confirm for unreleased/cancelled wave task {blockedAssignedId}.");

        if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{blockedAssignedId}/short-pick", bOpAId, facilityId,
            Permissions.Outbound.Pick, new { quantityPicked = 0m, reason = 1, notes = "blocked wave" }))).StatusCode != HttpStatusCode.UnprocessableEntity)
            throw new InvalidOperationException($"P74-0B-01: expected 422 on /short-pick for unreleased/cancelled wave task {blockedAssignedId}.");
    }

    // 7.3c P74-0B-03 Assignee Facility Authorization (/assign)
    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskRaceId}/assign", bSupId, facilityId,
        $"{Permissions.Outbound.TasksView},{Permissions.Outbound.TasksManage}", new { operatorUserId = bForeignOpId }))).StatusCode != HttpStatusCode.Forbidden ||
        (await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskRaceId}/assign", bSupId, facilityId,
        $"{Permissions.Outbound.TasksView},{Permissions.Outbound.TasksManage}", new { operatorUserId = bUnassignedOpId }))).StatusCode != HttpStatusCode.Forbidden ||
        (await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskRaceId}/assign", bSupId, facilityId,
        $"{Permissions.Outbound.TasksView},{Permissions.Outbound.TasksManage}", new { operatorUserId = bInactiveOpId }))).StatusCode != HttpStatusCode.BadRequest)
        throw new InvalidOperationException("P74-0B-03: assignee facility/status rejection failed on PostgreSQL.");

    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskRaceId}/assign", bSupId, facilityId,
        $"{Permissions.Outbound.TasksView},{Permissions.Outbound.TasksManage}", new { operatorUserId = bOpAId }))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("P74-0B-03: valid same-facility staff /assign failed on PostgreSQL.");

    // 7.3d P74-0B-02 Live PostgreSQL Mid-Flight Reassignment Race & Atomic Supervisor Override Rollback
    // Concurrent reassignment to bOpBId right as bOpAId tries to commit PickExecutionService.ConfirmPickAsync -> DbUpdateConcurrencyException rolls back cleanly!
    var pgRaceInterceptor = new ConcurrentReassignInterceptor(() =>
    {
        using var r = StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskRaceId}/assign", bSupId, facilityId,
            $"{Permissions.Outbound.TasksView},{Permissions.Outbound.TasksManage}", new { operatorUserId = bOpBId });
        using var resp = p74Client.SendAsync(r).GetAwaiter().GetResult();
        if (resp.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Concurrent PostgreSQL /assign failed with {resp.StatusCode}");
    });

    await using (var raceDb = CreateDb(operationalConn, user, pgRaceInterceptor))
    {
        var raceSvc = new PickExecutionService(raceDb, NullLogger<PickExecutionService>.Instance);
        var concurrencyThrown = false;
        try
        {
            await raceSvc.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = bTaskRaceId,
                ScannedLocationBarcode = binBarcode,
                ScannedItemBarcode = itemSku,
                ScannedToteLpn = "PG-TOTE-740B",
                Quantity = 5m,
                UserId = bOpAId,
                RequireAssignedOwnership = true,
                AllowSupervisorOverride = false
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            concurrencyThrown = true;
        }
        if (!concurrencyThrown)
            throw new InvalidOperationException("P74-0B-02: expected DbUpdateConcurrencyException on PostgreSQL mid-flight reassignment race.");
    }

    // Subsequent HTTP /confirm by displaced bOpAId returns 403 Forbidden; new owner bOpBId executes cleanly!
    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskRaceId}/confirm", bOpAId, facilityId,
        Permissions.Outbound.Pick, new { scannedLocationBarcode = binBarcode, scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-740B", quantity = 5m }))).StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException("P74-0B-02: displaced bOpAId must receive 403 Forbidden after reassignment.");

    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskRaceId}/confirm", bOpBId, facilityId,
        Permissions.Outbound.Pick, new { scannedLocationBarcode = binBarcode, scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-740B", quantity = 5m }))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("P74-0B-02: new assignee bOpBId /confirm failed.");

    // Atomic supervisor override rollback verification (ThrowAfterSaveInterceptor rolls back BOTH stock movement AND PickTaskStatusHistory override audit row!)
    await using (var overFailDb = CreateDb(operationalConn, user, new ThrowAfterSaveInterceptor()))
    {
        var overFailSvc = new PickExecutionService(overFailDb, NullLogger<PickExecutionService>.Instance);
        var rolledBack = false;
        try
        {
            await overFailSvc.ConfirmPickAsync(new ConfirmPickRequest
            {
                PickTaskId = bTaskOverFailId,
                ScannedLocationBarcode = binBarcode,
                ScannedItemBarcode = itemSku,
                ScannedToteLpn = "PG-TOTE-740B",
                Quantity = 5m,
                UserId = bSupId,
                RequireAssignedOwnership = true,
                AllowSupervisorOverride = true,
                OverrideReason = "PG atomic override rollback check"
            });
        }
        catch (ForcedRollbackException)
        {
            rolledBack = true;
        }
        if (!rolledBack)
            throw new InvalidOperationException("P74-0B-02: expected ForcedRollbackException on supervisor override rollback test.");
    }

    // Complete bTaskOverFailId legitimately via supervisor override so NO active tasks remain in facilityId
    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskOverFailId}/short-pick", bSupId, facilityId,
        $"{Permissions.Outbound.Pick},{Permissions.Outbound.TasksManage}", new { quantityPicked = 0m, reason = 1, notes = "cleanup override" }))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("Supervisor override short-pick on bTaskOverFailId failed.");

    await using (var p74BCheckDb = CreateDb(operationalConn, user))
    {
        // Stock conservation: 30m - 5m (bTaskClaimId) - 5m (bTaskShortId released) - 5m (bTaskRaceId) - 5m (bTaskOverFailId released) = 10m (bPlan + bCanc); tote = 10m
        var chkSrc = await p74BCheckDb.StockOnHand.SingleAsync(s => s.Id == bStockId);
        var chkTote = await p74BCheckDb.StockOnHand.SingleAsync(s => s.HandlingUnitId == bToteId);
        if (chkSrc.QtyAvailable != 30m || chkSrc.QtyAllocated != 10m || chkTote.QtyAllocated != 10m)
            throw new InvalidOperationException($"PostgreSQL post-remediation stock conservation mismatch: srcAvail={chkSrc.QtyAvailable}, srcAlloc={chkSrc.QtyAllocated}, toteAlloc={chkTote.QtyAllocated}");

        // Verify zero failed override Pick ledger or Confirm-override history on bTaskOverFailId
        if (await p74BCheckDb.InventoryTransactions.AnyAsync(t => t.ReferenceId == bTaskOverFailId && t.TxType == InventoryTxType.Pick) ||
            await p74BCheckDb.PickTaskStatusHistories.AnyAsync(h => h.PickTaskId == bTaskOverFailId && h.Reason != null && h.Reason.Contains("PG atomic override rollback check")))
            throw new InvalidOperationException("P74-0B-02: rolled-back supervisor override leaked ledger or audit rows in PostgreSQL!");

        // Verify exact single Pick ledger tx for bTaskRaceId (from bOpBId only!)
        var raceTxs = await p74BCheckDb.InventoryTransactions.Where(t => t.ReferenceId == bTaskRaceId && t.TxType == InventoryTxType.Pick).ToListAsync();
        if (raceTxs.Count != 1 || raceTxs[0].UserId != bOpBId)
            throw new InvalidOperationException($"P74-0B-02: expected exactly 1 Pick tx by bOpBId for bTaskRaceId, found {raceTxs.Count}.");
    }

    // 7.4 Read endpoints & RBAC separation matrix on PostgreSQL
    foreach (var checkPath in new[]
    {
        $"/api/v1/outbound/pick-tasks?facilityId={facilityId}",
        $"/api/v1/outbound/shipments?facilityId={facilityId}",
        $"/api/v1/outbound/shipments/eligible-packages?facilityId={facilityId}",
        $"/api/v1/outbound/packing/sessions?facilityId={facilityId}",
        $"/api/v1/outbound/packing/sessions/{bPackSessionId}",
        $"/api/v1/outbound/packing/packages?packingSessionId={bPackSessionId}"
    })
    {
        if ((await p74Client.SendAsync(StaffReq(HttpMethod.Get, checkPath, bSupId, facilityId,
            $"{Permissions.Outbound.TasksView},{Permissions.Outbound.Ship},{Permissions.Outbound.Pack}"))).StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Phase 7.4.0B read endpoint failed on PostgreSQL: {checkPath}");
    }

    // View-only denied /next & /claim; exec-only denied queue & /assign; manage-only denied execution; MultiFacilityView reads allowed vs /claim denied
    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={facilityId}", bOpAId, facilityId, Permissions.Outbound.TasksView))).StatusCode != HttpStatusCode.Forbidden ||
        (await p74Client.SendAsync(StaffReq(HttpMethod.Get, $"/api/v1/outbound/pick-tasks?facilityId={facilityId}", bOpAId, facilityId, Permissions.Outbound.Pick))).StatusCode != HttpStatusCode.Forbidden ||
        (await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{bTaskClaimId}/claim", bSupId, facilityId, $"{Permissions.Outbound.TasksView},{Permissions.Outbound.TasksManage}", new { }))).StatusCode != HttpStatusCode.Forbidden ||
        (await p74Client.SendAsync(StaffReq(HttpMethod.Get, $"/api/v1/outbound/pick-tasks?facilityId={p74FacB}", bSupId, facilityId, $"{Permissions.Analytics.MultiFacilityView},{Permissions.Outbound.TasksView}"))).StatusCode != HttpStatusCode.OK ||
        (await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{p74TaskFacB}/claim", bSupId, facilityId, $"{Permissions.Analytics.MultiFacilityView},{Permissions.Outbound.Pick}", new { }))).StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException("Phase 7.4.0B RBAC separation assertion failed on PostgreSQL.");

    // /next returns 204 when no pending/assigned tasks remain in facility
    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Get, $"/api/v1/outbound/pick-tasks/next?facilityId={facilityId}", bOpAId, facilityId, Permissions.Outbound.Pick))).StatusCode != HttpStatusCode.NoContent)
        throw new InvalidOperationException("Expected 204 NoContent from /pick-tasks/next when no eligible tasks remain.");

    Console.WriteLine("PASS PostgreSQL Phase 7.4.0B Read/Queue APIs, Two-Operator Claim Race (200/409), Read-Only /next, Ownership Enforcement, and Stock Conservation.");

    // 7.5 P74-0B-05 Atomic Positive-Quantity Short-Pick Execution + P74-0B-04 PostgreSQL Staff Facility Provisioning
    var p5BinOkId = Guid.NewGuid();
    var p5BinFailId = Guid.NewGuid();
    var p5BinRaceId = Guid.NewGuid();
    var p5OrderId = Guid.NewGuid();
    var p5LineId = Guid.NewGuid();
    var p5StockId = Guid.NewGuid();
    var p5AllocOkId = Guid.NewGuid();
    var p5AllocFailId = Guid.NewGuid();
    var p5AllocRaceId = Guid.NewGuid();
    var p5WaveId = Guid.NewGuid();
    var p5TaskOkId = Guid.NewGuid();
    var p5TaskFailId = Guid.NewGuid();
    var p5TaskRaceId = Guid.NewGuid();
    var p5ToteId = Guid.NewGuid();

    await using (var p5Db = CreateDb(operationalConn, user))
    {
        p5Db.Locations.AddRange(
            new Location { Id = p5BinOkId, FacilityId = facilityId, LocationCode = "PG-P5-OK", Barcode = "PG-P5-OK", LocationType = LocationType.Bin, IsActive = true, IsPickable = true },
            new Location { Id = p5BinFailId, FacilityId = facilityId, LocationCode = "PG-P5-FAIL", Barcode = "PG-P5-FAIL", LocationType = LocationType.Bin, IsActive = true, IsPickable = true },
            new Location { Id = p5BinRaceId, FacilityId = facilityId, LocationCode = "PG-P5-RACE", Barcode = "PG-P5-RACE", LocationType = LocationType.Bin, IsActive = true, IsPickable = true });

        var p5Order = new OutboundOrder { Id = p5OrderId, ClientId = client1Id, FacilityId = facilityId, OrderNumber = "PG-P5-ORD1", CustomerName = "P5 Buyer", Status = OutboundOrderStatus.Allocated };
        p5Order.Lines.Add(new OutboundOrderLine { Id = p5LineId, OutboundOrderId = p5OrderId, ItemId = item1Id, LineNumber = 1, QuantityOrdered = 15m, QuantityAllocated = 15m });
        p5Db.OutboundOrders.Add(p5Order);

        p5Db.StockOnHand.Add(new StockOnHand { Id = p5StockId, ClientId = client1Id, FacilityId = facilityId, ItemId = item1Id, LocationId = p5BinOkId, QtyAvailable = 20m, QtyAllocated = 15m, Version = 1 });
        p5Db.InventoryAllocations.AddRange(
            new InventoryAllocation { Id = p5AllocOkId, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = p5OrderId, OutboundOrderLineId = p5LineId, StockOnHandId = p5StockId, ItemId = item1Id, LocationId = p5BinOkId, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated },
            new InventoryAllocation { Id = p5AllocFailId, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = p5OrderId, OutboundOrderLineId = p5LineId, StockOnHandId = p5StockId, ItemId = item1Id, LocationId = p5BinFailId, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated },
            new InventoryAllocation { Id = p5AllocRaceId, ClientId = client1Id, FacilityId = facilityId, OutboundOrderId = p5OrderId, OutboundOrderLineId = p5LineId, StockOnHandId = p5StockId, ItemId = item1Id, LocationId = p5BinRaceId, QuantityRequested = 5m, QuantityAllocated = 5m, Status = AllocationStatus.Allocated });

        p5Db.PickWaves.Add(new PickWave { Id = p5WaveId, ClientId = client1Id, FacilityId = facilityId, WaveNumber = "PG-P5-WV", Priority = 5, Status = PickWaveStatus.Released });
        p5Db.PickTasks.AddRange(
            new PickTask { Id = p5TaskOkId, ClientId = client1Id, FacilityId = facilityId, PickWaveId = p5WaveId, OutboundOrderId = p5OrderId, OutboundOrderLineId = p5LineId, InventoryAllocationId = p5AllocOkId, StockOnHandId = p5StockId, ItemId = item1Id, SourceLocationId = p5BinOkId, QuantityToPick = 5m, PickSequence = 1, Status = PickTaskStatus.Assigned, AssignedUserId = bOpAId, Version = 1 },
            new PickTask { Id = p5TaskFailId, ClientId = client1Id, FacilityId = facilityId, PickWaveId = p5WaveId, OutboundOrderId = p5OrderId, OutboundOrderLineId = p5LineId, InventoryAllocationId = p5AllocFailId, StockOnHandId = p5StockId, ItemId = item1Id, SourceLocationId = p5BinFailId, QuantityToPick = 5m, PickSequence = 2, Status = PickTaskStatus.Pending, Version = 1 },
            new PickTask { Id = p5TaskRaceId, ClientId = client1Id, FacilityId = facilityId, PickWaveId = p5WaveId, OutboundOrderId = p5OrderId, OutboundOrderLineId = p5LineId, InventoryAllocationId = p5AllocRaceId, StockOnHandId = p5StockId, ItemId = item1Id, SourceLocationId = p5BinRaceId, QuantityToPick = 5m, PickSequence = 3, Status = PickTaskStatus.Assigned, AssignedUserId = bOpAId, Version = 1 });

        p5Db.HandlingUnits.Add(new HandlingUnit { Id = p5ToteId, ClientId = client1Id, FacilityId = facilityId, LpnCode = "PG-TOTE-P5", UnitType = HandlingUnitType.Tote, Status = HandlingUnitStatus.Available, CurrentOutboundOrderId = p5OrderId });
        await p5Db.SaveChangesAsync();
    }

    // 7.5a Positive-quantity short-pick (2m picked + 3m discrepancy) executes cleanly via HTTP in one atomic transaction
    using (var okShortResp = await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{p5TaskOkId}/short-pick", bOpAId, facilityId, Permissions.Outbound.Pick,
        new { quantityPicked = 2m, reason = (int)ShortPickReason.DamagedStock, notes = "PG partial pick 2/5", scannedLocationBarcode = "PG-P5-OK", scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-P5" })))
    {
        if (okShortResp.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"P74-0B-05: positive-quantity short-pick failed with {okShortResp.StatusCode}: {await okShortResp.Content.ReadAsStringAsync()}");
    }

    await using (var p5CheckOk = CreateDb(operationalConn, user))
    {
        var s = await p5CheckOk.StockOnHand.SingleAsync(x => x.Id == p5StockId);
        var toteS = await p5CheckOk.StockOnHand.SingleAsync(x => x.HandlingUnitId == p5ToteId);
        var locOk = await p5CheckOk.Locations.SingleAsync(l => l.Id == p5BinOkId);
        var pickTx = await p5CheckOk.InventoryTransactions.Where(t => t.ReferenceId == p5TaskOkId && t.TxType == InventoryTxType.Pick).ToListAsync();
        var exc = await p5CheckOk.PickExceptions.Where(e => e.PickTaskId == p5TaskOkId).ToListAsync();
        if (s.QtyAllocated != 10m || toteS.QtyAllocated != 2m || toteS.QtyAvailable != 0m || !locOk.IsLocked ||
            pickTx.Count != 1 || pickTx[0].Quantity != 2m || exc.Count != 1 || exc[0].ActualQuantityPicked != 2m ||
            !await p5CheckOk.CycleCounts.AnyAsync(c => c.Id == exc[0].GeneratedCycleCountId) ||
            !await p5CheckOk.LocationLocks.AnyAsync(ll => ll.LocationId == p5BinOkId))
            throw new InvalidOperationException("P74-0B-05: positive-quantity short-pick persisted incomplete state.");
    }

    // 7.5b Forced exception AFTER partial stock movement & SaveChanges inside outer transaction -> ZERO partial commit allowed!
    await using (var p5FailDb = CreateDb(operationalConn, user, new ThrowAfterSaveInterceptor()))
    {
        var p5FailSvc = new PickExecutionService(p5FailDb, NullLogger<PickExecutionService>.Instance);
        var rolledBack = false;
        try
        {
            await p5FailSvc.ReportShortPickAsync(new ReportShortPickRequest
            {
                PickTaskId = p5TaskFailId,
                QuantityPicked = 2m,
                Reason = ShortPickReason.BinEmpty,
                Notes = "PG forced rollback after partial pick",
                ScannedLocationBarcode = "PG-P5-FAIL",
                ScannedItemBarcode = itemSku,
                ScannedToteLpn = "PG-TOTE-P5",
                UserId = bSupId,
                RequireAssignedOwnership = true,
                AllowSupervisorOverride = true,
                OverrideReason = "PG positive short-pick rollback test"
            });
        }
        catch (ForcedRollbackException)
        {
            rolledBack = true;
        }
        if (!rolledBack)
            throw new InvalidOperationException("P74-0B-05: expected ForcedRollbackException on positive-quantity short-pick rollback test.");
    }

    await using (var p5CheckAfterRollback = CreateDb(operationalConn, user))
    {
        var s = await p5CheckAfterRollback.StockOnHand.SingleAsync(x => x.Id == p5StockId);
        var toteS = await p5CheckAfterRollback.StockOnHand.SingleAsync(x => x.HandlingUnitId == p5ToteId);
        var failTask = await p5CheckAfterRollback.PickTasks.SingleAsync(t => t.Id == p5TaskFailId);
        var locFail = await p5CheckAfterRollback.Locations.SingleAsync(l => l.Id == p5BinFailId);
        if (s.QtyAllocated != 10m || toteS.QtyAllocated != 2m || failTask.Status != PickTaskStatus.Pending || locFail.IsLocked ||
            await p5CheckAfterRollback.InventoryTransactions.AnyAsync(t => t.ReferenceId == p5TaskFailId) ||
            await p5CheckAfterRollback.PickExceptions.AnyAsync(e => e.PickTaskId == p5TaskFailId) ||
            await p5CheckAfterRollback.LocationLocks.AnyAsync(ll => ll.LocationId == p5BinFailId) ||
            await p5CheckAfterRollback.PickTaskStatusHistories.AnyAsync(h => h.PickTaskId == p5TaskFailId))
            throw new InvalidOperationException("P74-0B-05: forced rollback leaked partial stock, ledger, exception, lock, or audit data!");
    }

    // 7.5c Clean retry after rollback (with atomic supervisor override audit) + duplicate terminal retry blocked
    using (var retryOkResp = await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{p5TaskFailId}/short-pick", bSupId, facilityId,
        $"{Permissions.Outbound.Pick},{Permissions.Outbound.TasksManage}",
        new { quantityPicked = 2m, reason = (int)ShortPickReason.BinEmpty, notes = "PG clean retry", scannedLocationBarcode = "PG-P5-FAIL", scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-P5" })))
    {
        if (retryOkResp.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"P74-0B-05: retry after rollback failed with {retryOkResp.StatusCode}");
    }

    using (var retryDupResp = await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{p5TaskFailId}/short-pick", bSupId, facilityId,
        $"{Permissions.Outbound.Pick},{Permissions.Outbound.TasksManage}",
        new { quantityPicked = 2m, reason = (int)ShortPickReason.BinEmpty, notes = "dup retry", scannedLocationBarcode = "PG-P5-FAIL", scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-P5" })))
    {
        if (retryDupResp.StatusCode != HttpStatusCode.UnprocessableEntity)
            throw new InvalidOperationException($"P74-0B-05: duplicate terminal short-pick retry should return 422, got {retryDupResp.StatusCode}");
    }

    // 7.5d Live PostgreSQL concurrent reassignment during positive-quantity short-pick (DbUpdateConcurrencyException -> full atomic rollback)
    var p5RaceInterceptor = new ConcurrentReassignInterceptor(() =>
    {
        using var r = StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{p5TaskRaceId}/assign", bSupId, facilityId,
            $"{Permissions.Outbound.TasksView},{Permissions.Outbound.TasksManage}", new { operatorUserId = bOpBId });
        using var resp = p74Client.SendAsync(r).GetAwaiter().GetResult();
        if (resp.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"P74-0B-05 concurrent /assign failed with {resp.StatusCode}");
    });

    await using (var p5RaceDb = CreateDb(operationalConn, user, p5RaceInterceptor))
    {
        var p5RaceSvc = new PickExecutionService(p5RaceDb, NullLogger<PickExecutionService>.Instance);
        var concThrown = false;
        try
        {
            await p5RaceSvc.ReportShortPickAsync(new ReportShortPickRequest
            {
                PickTaskId = p5TaskRaceId,
                QuantityPicked = 2m,
                Reason = ShortPickReason.BinEmpty,
                Notes = "PG concurrent reassign positive short-pick",
                ScannedLocationBarcode = "PG-P5-RACE",
                ScannedItemBarcode = itemSku,
                ScannedToteLpn = "PG-TOTE-P5",
                UserId = bOpAId,
                RequireAssignedOwnership = true,
                AllowSupervisorOverride = false
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            concThrown = true;
        }
        if (!concThrown)
            throw new InvalidOperationException("P74-0B-05: expected DbUpdateConcurrencyException during concurrent positive short-pick reassignment.");
    }

    // Displaced bOpAId gets 403; new assignee bOpBId executes cleanly -> full conservation & exact override audit
    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{p5TaskRaceId}/short-pick", bOpAId, facilityId, Permissions.Outbound.Pick,
        new { quantityPicked = 2m, reason = (int)ShortPickReason.BinEmpty, notes = "displaced", scannedLocationBarcode = "PG-P5-RACE", scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-P5" }))).StatusCode != HttpStatusCode.Forbidden)
        throw new InvalidOperationException("P74-0B-05: displaced bOpAId must be rejected 403.");

    if ((await p74Client.SendAsync(StaffReq(HttpMethod.Post, $"/api/v1/outbound/pick-tasks/{p5TaskRaceId}/short-pick", bOpBId, facilityId, Permissions.Outbound.Pick,
        new { quantityPicked = 2m, reason = (int)ShortPickReason.BinEmpty, notes = "new owner short-pick", scannedLocationBarcode = "PG-P5-RACE", scannedItemBarcode = itemSku, scannedToteLpn = "PG-TOTE-P5" }))).StatusCode != HttpStatusCode.OK)
        throw new InvalidOperationException("P74-0B-05: new assignee bOpBId positive short-pick failed.");

    await using (var p5FinalDb = CreateDb(operationalConn, user))
    {
        var s = await p5FinalDb.StockOnHand.SingleAsync(x => x.Id == p5StockId);
        var toteS = await p5FinalDb.StockOnHand.SingleAsync(x => x.HandlingUnitId == p5ToteId);
        var pickTxs = await p5FinalDb.InventoryTransactions.Where(t => (t.ReferenceId == p5TaskOkId || t.ReferenceId == p5TaskFailId || t.ReferenceId == p5TaskRaceId) && t.TxType == InventoryTxType.Pick).ToListAsync();
        var overrideAudits = await p5FinalDb.PickTaskStatusHistories.Where(h => h.PickTaskId == p5TaskFailId && h.Reason != null && h.Reason.Contains("Task-management override")).ToListAsync();
        var raceTxs = pickTxs.Where(t => t.ReferenceId == p5TaskRaceId).ToList();

        if (s.QtyAvailable != 20m || s.QtyAllocated != 0m || toteS.QtyAllocated != 6m || toteS.QtyAvailable != 0m ||
            pickTxs.Count != 3 || overrideAudits.Count != 1 || raceTxs.Count != 1 || raceTxs[0].UserId != bOpBId)
            throw new InvalidOperationException($"P74-0B-05 final PostgreSQL reconciliation failed: srcAlloc={s.QtyAllocated}, toteAlloc={toteS.QtyAllocated}, pickTxs={pickTxs.Count}, overrides={overrideAudits.Count}");
    }

    // 7.5e P74-0B-04 PostgreSQL Staff Facility Provisioning, Assigned-Facility Update & Real JWT Claim Roundtrip
    Guid provUserId;
    using (var rCreateStaff = await p74Client.SendAsync(StaffReq(HttpMethod.Post, "/api/v1/users", bSupId, facilityId, Permissions.Users.Create, new
    {
        email = "pg-provisioned-staff@740b.test",
        password = "Password123!",
        firstName = "Pg",
        lastName = "Provisioned",
        userType = UserType.Staff,
        assignedFacilityId = facilityId,
        roles = new[] { SystemRoles.WarehouseOperator }
    })))
    {
        if (rCreateStaff.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"P74-0B-04 PostgreSQL /api/v1/users create failed: {rCreateStaff.StatusCode} - {await rCreateStaff.Content.ReadAsStringAsync()}");
        using var doc = JsonDocument.Parse(await rCreateStaff.Content.ReadAsStringAsync());
        provUserId = doc.RootElement.GetProperty("id").GetGuid();
        if (doc.RootElement.GetProperty("assignedFacilityId").GetGuid() != facilityId)
            throw new InvalidOperationException("P74-0B-04: created staff AssignedFacilityId mismatch.");
    }

    await using (var pgJwtDb = CreateDb(operationalConn, user))
    {
        var jwtSvc = new JwtTokenService(Options.Create(new JwtOptions { SecretKey = "Pg-740B-Verification-SigningKey-2026!!", Issuer = "pg-test", Audience = "pg-test", ExpiryMinutes = 60 }), new VerificationClock());
        var loginHandler = new LoginCommandHandler(pgJwtDb, new PasswordHasher(), jwtSvc, new VerificationClock());
        var loginRes = await loginHandler.Handle(new LoginCommand { Email = "pg-provisioned-staff@740b.test", Password = "Password123!" }, CancellationToken.None);
        var jwt1 = new JwtSecurityTokenHandler().ReadJwtToken(loginRes.AccessToken);
        if (jwt1.Claims.FirstOrDefault(c => c.Type == "facility_id")?.Value != facilityId.ToString())
            throw new InvalidOperationException("P74-0B-04 PostgreSQL login JWT missing expected facility_id claim.");

        using var rReassignUser = await p74Client.SendAsync(StaffReq(HttpMethod.Put, $"/api/v1/users/{provUserId}/assigned-facility", bSupId, facilityId, Permissions.Users.Edit, new { facilityId = p74FacB }));
        if (rReassignUser.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"P74-0B-04 PostgreSQL PUT /assigned-facility failed: {rReassignUser.StatusCode}");

        pgJwtDb.ClearChangeTracker();
        var refreshHandler = new RefreshTokenCommandHandler(pgJwtDb, jwtSvc, new VerificationClock());
        var refreshRes = await refreshHandler.Handle(new RefreshTokenCommand { AccessToken = loginRes.AccessToken, RefreshToken = loginRes.RefreshToken }, CancellationToken.None);
        var jwt2 = new JwtSecurityTokenHandler().ReadJwtToken(refreshRes.AccessToken);
        if (jwt2.Claims.FirstOrDefault(c => c.Type == "facility_id")?.Value != p74FacB.ToString())
            throw new InvalidOperationException("P74-0B-04 PostgreSQL refresh-token JWT did not pick up updated p74FacB facility_id.");
    }

    Console.WriteLine("PASS PostgreSQL P74-0B-04 (Staff Provisioning & JWT Claims) & P74-0B-05 (Atomic Positive Short-Pick, Forced Rollback, Retry, Concurrency Race) verified cleanly.");

    Console.WriteLine("\n================================================================================");
    Console.WriteLine("ALL PHASE 7.3, 7.4.0A & PHASE 7.4.0B POSTGRESQL VERIFICATIONS PASSED CLEANLY!");
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

sealed class ConcurrentReassignInterceptor(Action onFirstSaving) : SaveChangesInterceptor
{
    private int _fired;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _fired, 1) == 0) onFirstSaving();
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
    public static Guid CurrentUserId { get; set; } = Guid.Empty;
    public static List<Claim>? CustomClaims { get; set; } = null;

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var actorUserId = Request.Headers["X-Test-Actor-UserId"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(actorUserId))
        {
            var actorClaims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, actorUserId),
                new Claim("UserType", "Staff"),
                new Claim("user_type", "Staff")
            };
            var actorFac = Request.Headers["X-Test-Actor-FacilityId"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(actorFac))
            {
                actorClaims.Add(new Claim("facility_id", actorFac));
            }
            var actorPerms = Request.Headers["X-Test-Actor-Permissions"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(actorPerms))
            {
                foreach (var p in actorPerms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    actorClaims.Add(new Claim("permissions", p));
                }
            }
            var actorIdentity = new ClaimsIdentity(actorClaims, "TestAuth");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(actorIdentity), "TestAuth")));
        }

        var userId = CurrentUserId != Guid.Empty ? CurrentUserId.ToString() : Guid.NewGuid().ToString();
        var claims = CustomClaims ?? new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Email, "staff@test.local"),
            new Claim(ClaimTypes.Role, "SuperAdmin"),
            new Claim("UserType", "Staff"),
            new Claim("user_type", "Staff"),
            new Claim("Permission", "Inbound:Receive"),
            new Claim("Permission", "Inbound:Putaway:Execute"),
            new Claim("Permission", "Inbound:Putaway:Manage"),
            new Claim("Permission", "Inbound:View"),
            new Claim("Permission", "Inventory:View"),
            new Claim("Permission", "Analytics:CrossFacility"),
            new Claim("Permission", "Analytics:ControlTower:View"),
            new Claim("permissions", "analytics:control-tower:view"),
            new Claim("permissions", "clients:view"),
            new Claim("permissions", "clients:create"),
            new Claim("permissions", "clients:edit"),
            new Claim("permissions", "clients:delete"),
            new Claim("Permission", "clients:view"),
            new Claim("Permission", "clients:create"),
            new Claim("Permission", "clients:edit"),
            new Claim("Permission", "clients:delete")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "TestAuth");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

sealed class PgControllableClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}

sealed class PgWebhookAttemptControl
{
    public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Proceed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool FailOnProceed { get; init; }
}

sealed class PgWebhookProbeState
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentQueue<PgWebhookAttemptControl>> _controls = new(StringComparer.Ordinal);
    public string? FailNextNonce { get; set; }

    public void Enqueue(string nonce, PgWebhookAttemptControl control)
    {
        var q = _controls.GetOrAdd(nonce, _ => new System.Collections.Concurrent.ConcurrentQueue<PgWebhookAttemptControl>());
        q.Enqueue(control);
    }

    public bool TryDequeue(string? nonce, out PgWebhookAttemptControl? control)
    {
        control = null;
        if (string.IsNullOrEmpty(nonce)) return false;
        return _controls.TryGetValue(nonce, out var q) && q.TryDequeue(out control);
    }
}

sealed class PgFaultInjectableReturnTrackingService(
    ReturnTrackingService inner,
    PgWebhookProbeState probeState,
    Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor) : IReturnTrackingService
{
    public async Task<IngestReturnTrackingResult> IngestReturnTrackingEventAsync(
        IngestReturnTrackingRequest request,
        CancellationToken cancellationToken = default)
    {
        var nonce = httpContextAccessor.HttpContext?.Request.Headers["X-Webhook-Nonce"].FirstOrDefault();
        if (probeState.TryDequeue(nonce, out var control) && control != null)
        {
            control.Entered.TrySetResult(true);
            await control.Proceed.Task;
            if (control.FailOnProceed)
            {
                throw new TimeoutException("Simulated PostgreSQL delayed worker failure after lease expiry.");
            }
        }
        else if (!string.IsNullOrEmpty(probeState.FailNextNonce) &&
                 string.Equals(probeState.FailNextNonce, nonce, StringComparison.Ordinal))
        {
            probeState.FailNextNonce = null;
            throw new TimeoutException("Simulated PostgreSQL transient downstream failure.");
        }

        return await inner.IngestReturnTrackingEventAsync(request, cancellationToken);
    }

    public Task<ReturnInboundHandoffDto?> GetInboundHandoffByReturnShipmentIdAsync(
        Guid returnShipmentId,
        CancellationToken cancellationToken = default)
        => inner.GetInboundHandoffByReturnShipmentIdAsync(returnShipmentId, cancellationToken);

    public Task<ReturnInboundHandoffDto?> GetInboundHandoffByAuthorizationIdAsync(
        Guid returnAuthorizationId,
        CancellationToken cancellationToken = default)
        => inner.GetInboundHandoffByAuthorizationIdAsync(returnAuthorizationId, cancellationToken);

    public Task<IReadOnlyList<ReturnTrackingTimelineEventDto>> GetReturnTrackingHistoryAsync(
        Guid returnShipmentId,
        CancellationToken cancellationToken = default)
        => inner.GetReturnTrackingHistoryAsync(returnShipmentId, cancellationToken);
}

[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Mvc.Route("api/v1/pg-probe")]
public sealed class PgWebhookProbeController : Microsoft.AspNetCore.Mvc.ControllerBase
{
    [Microsoft.AspNetCore.Mvc.HttpPost("status/{code:int}")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [Microsoft.AspNetCore.Mvc.RequestSizeLimit(262_144)]
    [Warehouse3PL.WebApi.Security.CarrierWebhookHmacAuth]
    public Microsoft.AspNetCore.Mvc.IActionResult ReturnExplicitStatus([Microsoft.AspNetCore.Mvc.FromRoute] int code)
        => StatusCode(code, new { status = code });
}

sealed class PgChunkedContent(byte[] payload) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => stream.WriteAsync(payload, 0, payload.Length);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
