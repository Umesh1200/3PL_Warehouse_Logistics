using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.CrossDock.Tasks.Commands.ConfirmCrossDockTask;
using Warehouse3PL.Application.Features.Facilities.Commands;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.CreateDockDoor;
using Warehouse3PL.Application.Features.Inbound.DockManagement.DockDoors.Commands.UpdateDockDoorStatus;
using Warehouse3PL.Application.Features.Inventory.Commands.TransferInventory;
using Warehouse3PL.Application.Features.Locations.Commands.CreateLocation;
using Warehouse3PL.Application.Features.Locations.Commands.LockLocation;
using Warehouse3PL.Application.Features.Putaway;
using Warehouse3PL.Application.Features.Putaway.Commands.ConfirmPutaway;
using Warehouse3PL.Application.Features.Putaway.Tasks.Commands.GeneratePutawayTasks;
using Warehouse3PL.Application.Features.WarehouseMaster;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.CrossDock;
using Warehouse3PL.Domain.Entities.Dock;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Packing;
using Warehouse3PL.Infrastructure.Services.Shipping;
using ValidationException = Warehouse3PL.Domain.Exceptions.ValidationException;

internal static class Mdm21PostgresCheck
{
    private const string PreMdm21Migration = "20261127103000_MDM1_6_F03R01_F04R01_SafeUpgradeAndStructuredReasons";
    private const string Mdm21Migration = "20261127125902_MDM2_1_WarehouseMasterAndLocationHierarchy";
    private const string Mdm21CorrectiveMigration = "20261127150334_MDM2_1_R01_R06_ConcurrencyAndLifecycleGuards";

    public static async Task RunAsync(string baseConnectionString)
    {
        Console.WriteLine("\n[MDM-2.1] Running Live PostgreSQL 16 Warehouse Master & Physical Location Hierarchy Verification (A01-A07)...");

        await VerifyMigrationPreflightRollbackAndRetryAsync(baseConnectionString);

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm21_pg_{Guid.NewGuid():N}";
        await CreateDbAsync(adminBuilder.ConnectionString, dbName);
        var targetConn = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = dbName }.ConnectionString;

        try
        {
            await using (var initDb = CreatePgDb(targetConn))
            {
                await initDb.Database.MigrateAsync();
                await ReferenceCatalogSeeder.SeedAsync(initDb);
            }

            // PG-2: Verify FK catalog preservation (all 21 location_id FKs + 3 zone_id FKs + new composite same-facility FKs)
            await VerifyForeignKeyCatalogAsync(targetConn);

            // Seed two facilities (Chicago ORD1 & Kathmandu KTM1), two clients, and DB-verified SuperAdmin
            var facOrd = Guid.NewGuid();
            var facKtm = Guid.NewGuid();
            var clientA = Guid.NewGuid();
            var clientB = Guid.NewGuid();
            var adminId = Guid.NewGuid();

            await using (var seedDb = CreatePgDb(targetConn))
            {
                seedDb.Facilities.AddRange(
                    new Facility { Id = facOrd, FacilityCode = "FAC-ORD1", Name = "Chicago Hub", CountryCode = "US", Timezone = "America/Chicago", DefaultCurrencyCode = "USD", IsActive = true },
                    new Facility { Id = facKtm, FacilityCode = "FAC-KTM1", Name = "Kathmandu Hub", CountryCode = "NP", Timezone = "Asia/Kathmandu", DefaultCurrencyCode = "NPR", IsActive = true });
                seedDb.Clients.AddRange(
                    new Client { Id = clientA, CompanyName = "Alpha 3PL Client", AccountCode = "CL-ALPHA", Status = ClientStatus.Active },
                    new Client { Id = clientB, CompanyName = "Beta 3PL Client", AccountCode = "CL-BETA", Status = ClientStatus.Active });
                seedDb.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientA, FacilityId = facOrd, Status = FacilityAccessStatus.Active });
                var role = new Role { RoleName = SystemRoles.SuperAdmin };
                seedDb.Roles.Add(role);
                await seedDb.SaveChangesAsync();
                seedDb.Users.Add(new User
                {
                    Id = adminId,
                    Email = "mdm21-pg-admin@nexus3pl.test",
                    PasswordHash = "hash",
                    FirstName = "PG",
                    LastName = "Admin",
                    UserType = UserType.Staff,
                    Status = UserStatus.Active,
                    AssignedFacilityId = facOrd
                });
                seedDb.UserRoles.Add(new UserRole { UserId = adminId, RoleId = role.Id });
                await seedDb.SaveChangesAsync();
            }

            var adminCtx = new Mdm21Identity(adminId, facOrd);

            // PG-3 & PG-4: DB-layer composite same-facility FKs, self-cycle CHECK, and multi-hop cycle trigger
            await VerifyDatabaseConstraintsAndCycleTriggerAsync(targetConn, adminCtx, facOrd, facKtm);

            // PG-5 & PG-6: Concurrent opposite reparenting (A->B vs B->A) + atomic subtree recalculation
            await VerifyConcurrentOppositeReparentingAndSubtreeAsync(targetConn, adminCtx, facOrd);

            // PG-7, PG-8, PG-9: Configurable rules (KTM1/ORD1), permanent normalized uniqueness, A01 lock independence
            await VerifyRulesUniquenessAndLockIndependenceAsync(targetConn, adminCtx, facOrd, facKtm);

            // PG-10: Concurrent location deactivation vs putaway stock placement race
            await VerifyConcurrentDeactivationVsPutawayRaceAsync(targetConn, adminCtx, facOrd, clientA);

            // PG-11 & PG-12: Dedicated client, packaging isolation, dock cm/ft + appointment guard, A06 profiles, 2-context MDM-1.5 outbox
            await VerifyProfilesPackagingDockAndEpochOutboxAsync(targetConn, adminCtx, facOrd, clientA, clientB);

            // PG-14 (R01): All 6 StockOnHand buckets (Reserved/Damaged/OnHold) & Damaged HU block deactivation
            await VerifyR01StockBucketsAndDamagedHuGuardsAsync(targetConn, adminCtx, facOrd, clientA);

            // PG-15 (R02): Descendant configurable MaxDepth rejection + rule mutation conflict rejection
            await VerifyR02DescendantDepthAndRuleConflictAsync(targetConn, adminCtx, facOrd);

            // PG-16 (R04): Concurrent Parent deactivation vs Child creation race
            await VerifyR04ConcurrentParentDeactivationVsChildCreateAsync(targetConn, adminCtx, facOrd);

            // PG-17 (R04): Concurrent Location deactivation vs Putaway task generation race
            await VerifyR04ConcurrentDeactivationVsPutawayTaskGenerationAsync(targetConn, adminCtx, facOrd, clientA);

            // PG-18 (R04): Concurrent IsStorable=false vs TransferInventoryCommand race
            await VerifyR04ConcurrentNonStorableVsTransferAsync(targetConn, adminCtx, facOrd, clientA);

            // PG-19 (R04): Concurrent Storage-profile tightening vs TransferInventoryCommand race
            await VerifyR04ConcurrentProfileTighteningVsTransferAsync(targetConn, adminCtx, facOrd, clientA);

            // PG-20 (R04): Concurrent Hierarchy rule mutation vs Reparenting + direct SQL opposite reparenting
            await VerifyR04ConcurrentRuleChangeVsReparentingAndTriggerLockAsync(targetConn, adminCtx, facOrd);

            // PG-21..PG-30 (MDM21-F01..F03): Aggregate guards, return/cross-dock/move eligibility, shared zone/packing/facility concurrency
            await VerifyF01AggregateGuardsAsync(targetConn, adminCtx, clientA);
            await VerifyF02ReturnMoveAndCrossDockEligibilityAsync(targetConn, adminCtx, facOrd, facKtm, clientA, clientB);
            await VerifyF03ConcurrentZonePackingAndFacilityBoundariesAsync(targetConn, adminCtx, facOrd, clientA, clientB);

            // PG-13: Reversible Down() & re-Up():
            // (a) Incremental rollback/re-apply of corrective migration preserving facility rules & populated data
            // (b) Full rollback to PreMdm21Migration, verifying R02 preflight fails closed on custom Aisle->Aisle links without rules, then succeeds cleanly.
            await using (var revDb = CreatePgDb(targetConn))
            {
                var migrator = revDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(Mdm21Migration);
                await migrator.MigrateAsync(Mdm21CorrectiveMigration);

                await migrator.MigrateAsync(PreMdm21Migration);

                bool r02PreflightBlocked = false;
                try
                {
                    await migrator.MigrateAsync(Mdm21CorrectiveMigration);
                }
                catch (Exception ex) when (ex.ToString().Contains("MDM21_R02_PREFLIGHT_FAILED", StringComparison.Ordinal))
                {
                    r02PreflightBlocked = true;
                }
                if (!r02PreflightBlocked)
                    throw new InvalidOperationException("PG-13 violation: Corrective migration did not fail closed with MDM21_R02_PREFLIGHT_FAILED on custom Aisle->Aisle links without rules.");

                await revDb.Database.ExecuteSqlRawAsync(
                    "UPDATE locations SET parent_location_id = NULL WHERE location_code IN ('RACE-A','RACE-B','RR-A','RR-B','SQL-A','SQL-B');");

                await migrator.MigrateAsync(Mdm21CorrectiveMigration);
            }

            Console.WriteLine("  [PASS] PG-13 & All MDM-2.1 (R01-R06 & F01-F03) Live PostgreSQL 16 verification checks passed!");
        }
        finally
        {
            await DropDbAsync(adminBuilder.ConnectionString, dbName);
        }
    }

    private static async Task VerifyMigrationPreflightRollbackAndRetryAsync(string baseConnectionString)
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm21_preflight_{Guid.NewGuid():N}";
        await CreateDbAsync(adminBuilder.ConnectionString, dbName);
        var connStr = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = dbName }.ConnectionString;

        try
        {
            await using (var db = CreatePgDb(connStr))
            {
                var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(PreMdm21Migration);
            }

            var facId = Guid.NewGuid();
            var loc1 = Guid.NewGuid();
            var loc2 = Guid.NewGuid();

            await using (var sqlConn = new NpgsqlConnection(connStr))
            {
                await sqlConn.OpenAsync();
                await using var seedCmd = new NpgsqlCommand($"""
                    INSERT INTO facilities (facility_id, facility_code, name, timezone, country_code, country_normalization_status, dock_door_count, is_active, is_deleted, created_at)
                    VALUES ('{facId}', 'FAC-PRE', 'Preflight Hub', 'America/Chicago', 'US', 'Normalized', 2, true, false, NOW());

                    INSERT INTO locations (location_id, facility_id, location_type, location_code, barcode, is_active, is_pickable, is_receiving, is_damaged, is_locked, is_deleted, created_at)
                    VALUES
                        ('{loc1}', '{facId}', 'Bin', 'BIN-DUP', 'BC-1', true, true, false, false, false, false, NOW()),
                        ('{loc2}', '{facId}', 'Bin', '  bin-dup ', 'BC-2', true, true, false, false, false, false, NOW());
                    """, sqlConn);
                await seedCmd.ExecuteNonQueryAsync();
            }

            // Attempt migration with normalized code collision -> MUST fail closed with MDM21_PREFLIGHT_FAILED
            bool failedAsExpected = false;
            await using (var failDb = CreatePgDb(connStr))
            {
                try
                {
                    await failDb.Database.MigrateAsync();
                }
                catch (Exception ex) when (ex.ToString().Contains("MDM21_PREFLIGHT_FAILED", StringComparison.Ordinal))
                {
                    failedAsExpected = true;
                }
            }
            if (!failedAsExpected)
                throw new InvalidOperationException("MDM-2.1 migration did not fail closed on normalized location_code collision.");

            // Verify migration was rolled back cleanly (not recorded in __EFMigrationsHistory)
            await using (var sqlConn = new NpgsqlConnection(connStr))
            {
                await sqlConn.OpenAsync();
                await using var histCmd = new NpgsqlCommand(
                    $"SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE migration_id = '{Mdm21Migration}'", sqlConn);
                if (Convert.ToInt32(await histCmd.ExecuteScalarAsync()) != 0)
                    throw new InvalidOperationException("Failed MDM-2.1 migration should not be recorded in __EFMigrationsHistory.");

                // Fix the collision
                await using var fixCmd = new NpgsqlCommand(
                    $"UPDATE locations SET location_code = 'BIN-FIXED' WHERE location_id = '{loc2}'", sqlConn);
                await fixCmd.ExecuteNonQueryAsync();
            }

            // Retry migration -> MUST succeed and deterministically backfill hierarchy_level & hierarchy_path
            await using (var retryDb = CreatePgDb(connStr))
            {
                await retryDb.Database.MigrateAsync();
                var backfilled = await retryDb.Locations.AsNoTracking().FirstAsync(l => l.Id == loc1);
                if (backfilled.NormalizedLocationCode != "BIN-DUP" ||
                    backfilled.HierarchyLevel != 1 ||
                    backfilled.HierarchyPath != $"/{loc1:N}/" ||
                    !backfilled.IsStorable)
                {
                    throw new InvalidOperationException($"MDM-2.1 backfill mismatch: code={backfilled.NormalizedLocationCode}, level={backfilled.HierarchyLevel}, path={backfilled.HierarchyPath}");
                }
            }

            Console.WriteLine("  [PASS] PG-1: Migration preflight fail-closed, clean rollback, fix & retry backfill verified.");
        }
        finally
        {
            await DropDbAsync(adminBuilder.ConnectionString, dbName);
        }
    }

    private static async Task VerifyForeignKeyCatalogAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("""
            SELECT COUNT(*) FROM pg_constraint
            WHERE contype = 'f' AND confrelid = 'locations'::regclass;
            """, conn);
        var locFkCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        // Previously 21 FKs referencing locations + 4 new composite FKs (dock_doors, storage_profile, operational_profile x2, equipment_req)
        if (locFkCount < 25)
            throw new InvalidOperationException($"Expected >= 25 FKs referencing locations (preserving all 21 existing + new composite FKs), found {locFkCount}.");

        await using var zCmd = new NpgsqlCommand("""
            SELECT COUNT(*) FROM pg_constraint
            WHERE contype = 'f' AND confrelid = 'warehouse_zones'::regclass;
            """, conn);
        var zoneFkCount = Convert.ToInt32(await zCmd.ExecuteScalarAsync());
        if (zoneFkCount < 3)
            throw new InvalidOperationException($"Expected >= 3 FKs referencing warehouse_zones, found {zoneFkCount}.");

        Console.WriteLine($"  [PASS] PG-2: FK catalog preserved ({locFkCount} location FKs, {zoneFkCount} zone FKs).");
    }

    private static async Task VerifyDatabaseConstraintsAndCycleTriggerAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd, Guid facKtm)
    {
        Guid ordA, ordB, ordC, ktmZone, ktmLoc;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            var auth = new WarehouseMasterAuthorization(db, adminCtx);
            var createZone = new CreateWarehouseZoneCommandHandler(db, auth);
            var zKtm = await createZone.Handle(new CreateWarehouseZoneCommand { FacilityId = facKtm, ZoneCode = "ZN-KTM-1", Name = "KTM Zone" }, CancellationToken.None);
            ktmZone = zKtm.Id;

            var createLoc = new CreateLocationCommandHandler(db, auth);
            var lKtm = await createLoc.Handle(new CreateLocationCommand { FacilityId = facKtm, ZoneId = ktmZone, LocationType = LocationType.Bin, LocationCode = "KTM-BIN-01", Barcode = "BC-KTM-01" }, CancellationToken.None);
            ktmLoc = lKtm.Id;

            var a = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Aisle, LocationCode = "CYC-A", Barcode = "BC-CYC-A" }, CancellationToken.None);
            var b = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ParentLocationId = a.Id, LocationType = LocationType.Rack, LocationCode = "CYC-B", Barcode = "BC-CYC-B" }, CancellationToken.None);
            var c = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ParentLocationId = b.Id, LocationType = LocationType.Bin, LocationCode = "CYC-C", Barcode = "BC-CYC-C" }, CancellationToken.None);
            ordA = a.Id;
            ordB = b.Id;
            ordC = c.Id;
        }

        await using var sqlConn = new NpgsqlConnection(connStr);
        await sqlConn.OpenAsync();

        // Cross-facility parent FK rejection (23503)
        await AssertPgErrorAsync(sqlConn,
            $"UPDATE locations SET parent_location_id = '{ktmLoc}' WHERE location_id = '{ordA}'",
            "23503", "cross-facility parent FK");

        // Cross-facility zone FK rejection (23503)
        await AssertPgErrorAsync(sqlConn,
            $"UPDATE locations SET zone_id = '{ktmZone}' WHERE location_id = '{ordA}'",
            "23503", "cross-facility zone FK");

        // Cross-facility dock_doors.location_id FK rejection (23503)
        await AssertPgErrorAsync(sqlConn,
            $"INSERT INTO dock_doors (dock_door_id, facility_id, location_id, door_number, normalized_door_number, door_type, status, supports_refrigerated, config_revision, is_deleted, created_at) VALUES ('{Guid.NewGuid()}', '{facOrd}', '{ktmLoc}', 'D-BAD', 'D-BAD', 'Inbound', 'Active', false, 1, false, NOW())",
            "23503", "cross-facility dock_doors.location_id FK");

        // Multi-hop cycle trigger rejection (A -> B -> C, attempt A.parent = C)
        await AssertPgMessageAsync(sqlConn,
            $"UPDATE locations SET parent_location_id = '{ordC}' WHERE location_id = '{ordA}'",
            "LOCATION_HIERARCHY_CYCLE", "multi-hop cycle trigger");

        Console.WriteLine("  [PASS] PG-3 & PG-4: Composite same-facility FKs and PostgreSQL multi-hop cycle trigger enforced.");
    }

    private static async Task VerifyConcurrentOppositeReparentingAndSubtreeAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd)
    {
        Guid nodeA, nodeB, childOfA;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            var auth = new WarehouseMasterAuthorization(db, adminCtx);
            // Allow Aisle -> Aisle in facOrd temporarily to test pure opposite reparenting A->B vs B->A
            await new UpsertLocationHierarchyRuleCommandHandler(db, auth).Handle(new UpsertLocationHierarchyRuleCommand
            {
                FacilityId = facOrd,
                ParentLocationType = LocationType.Aisle,
                ChildLocationType = LocationType.Aisle,
                MaxDepth = 6,
                IsStorableLeaf = false,
                IsActive = true
            }, CancellationToken.None);

            var createLoc = new CreateLocationCommandHandler(db, auth);
            var a = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Aisle, LocationCode = "RACE-A", Barcode = "BC-RACE-A" }, CancellationToken.None);
            var b = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Aisle, LocationCode = "RACE-B", Barcode = "BC-RACE-B" }, CancellationToken.None);
            var rackUnderA = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ParentLocationId = a.Id, LocationType = LocationType.Rack, LocationCode = "RACE-A-R1", Barcode = "BC-RACE-A-R1" }, CancellationToken.None);
            nodeA = a.Id;
            nodeB = b.Id;
            childOfA = rackUnderA.Id;
        }

        // Launch opposite moves simultaneously on 2 independent DbContexts
        var t1 = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            var handler = new MoveLocationCommandHandler(db1, new WarehouseMasterAuthorization(db1, adminCtx));
            await handler.Handle(new MoveLocationCommand(nodeA, nodeB), CancellationToken.None);
        });
        var t2 = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            var handler = new MoveLocationCommandHandler(db2, new WarehouseMasterAuthorization(db2, adminCtx));
            await handler.Handle(new MoveLocationCommand(nodeB, nodeA), CancellationToken.None);
        });

        int succeeded = 0, cycleRejected = 0;
        foreach (var t in new[] { t1, t2 })
        {
            try
            {
                await t;
                succeeded++;
            }
            catch (DomainException dex) when (dex.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase))
            {
                cycleRejected++;
            }
        }

        if (succeeded != 1 || cycleRejected != 1)
            throw new InvalidOperationException($"Expected 1 success and 1 cycle rejection in concurrent opposite reparenting, got succeeded={succeeded}, cycleRejected={cycleRejected}.");

        // Verify subtree consistency
        await using (var verifyDb = CreatePgDb(connStr, adminCtx))
        {
            var aRow = await verifyDb.Locations.AsNoTracking().FirstAsync(l => l.Id == nodeA);
            var bRow = await verifyDb.Locations.AsNoTracking().FirstAsync(l => l.Id == nodeB);
            var cRow = await verifyDb.Locations.AsNoTracking().FirstAsync(l => l.Id == childOfA);
            if (aRow.ParentLocationId == nodeB && bRow.ParentLocationId == nodeA)
                throw new InvalidOperationException("Cycle persisted after concurrent opposite reparenting!");
            if (!cRow.HierarchyPath.StartsWith(aRow.HierarchyPath, StringComparison.Ordinal) || cRow.HierarchyLevel != aRow.HierarchyLevel + 1)
                throw new InvalidOperationException($"Subtree path/depth out of sync: parent={aRow.HierarchyPath} (L{aRow.HierarchyLevel}), child={cRow.HierarchyPath} (L{cRow.HierarchyLevel}).");
        }

        Console.WriteLine("  [PASS] PG-5 & PG-6: Concurrent opposite A->B vs B->A reparenting serialized & subtree paths recalculated atomically.");
    }

    private static async Task VerifyRulesUniquenessAndLockIndependenceAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd, Guid facKtm)
    {
        await using var db = CreatePgDb(connStr, adminCtx);
        var auth = new WarehouseMasterAuthorization(db, adminCtx);
        var createLoc = new CreateLocationCommandHandler(db, auth);

        var bin = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facOrd,
            LocationType = LocationType.Bin,
            LocationCode = "PERM-PG-01",
            Barcode = "BC-PERM-PG-01"
        }, CancellationToken.None);

        // Two independent locks + reactivation preserves locks
        var lockHandler = new LockLocationCommandHandler(db, auth);
        var lk1 = await lockHandler.Handle(new LockLocationCommand { LocationId = bin.Id, LockType = LocationLockType.CycleCount, ReasonCode = "CC1" }, CancellationToken.None);
        var lk2 = await lockHandler.Handle(new LockLocationCommand { LocationId = bin.Id, LockType = LocationLockType.Maintenance, ReasonCode = "M1" }, CancellationToken.None);

        var statusHandler = new UpdateLocationStatusCommandHandler(db, auth);
        await statusHandler.Handle(new UpdateLocationStatusCommand(bin.Id, LocationStatus.Inactive), CancellationToken.None);
        var activeAgain = await statusHandler.Handle(new UpdateLocationStatusCommand(bin.Id, LocationStatus.Active), CancellationToken.None);
        if (!activeAgain.IsLocked)
            throw new InvalidOperationException("A01 violation: Reactivating location implicitly cleared IsLocked!");

        var releaseHandler = new ReleaseLocationLockCommandHandler(db, auth);
        await releaseHandler.Handle(new ReleaseLocationLockCommand(bin.Id, lk1), CancellationToken.None);
        if (!(await db.Locations.AsNoTracking().FirstAsync(l => l.Id == bin.Id)).IsLocked)
            throw new InvalidOperationException("Releasing 1 of 2 locks cleared IsLocked prematurely!");

        await releaseHandler.Handle(new ReleaseLocationLockCommand(bin.Id, lk2), CancellationToken.None);
        if ((await db.Locations.AsNoTracking().FirstAsync(l => l.Id == bin.Id)).IsLocked)
            throw new InvalidOperationException("Releasing final lock did not clear IsLocked!");

        // Soft-delete bin, then verify permanent normalized code/barcode uniqueness at both App & PG layers
        var tracked = await db.Locations.FirstAsync(l => l.Id == bin.Id);
        tracked.IsDeleted = true;
        tracked.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        bool dupRejected = false;
        try
        {
            await createLoc.Handle(new CreateLocationCommand
            {
                FacilityId = facOrd,
                LocationType = LocationType.Bin,
                LocationCode = "  perm-pg-01 ",
                Barcode = "BC-OTHER-01"
            }, CancellationToken.None);
        }
        catch (ValidationException)
        {
            dupRejected = true;
        }
        if (!dupRejected)
            throw new InvalidOperationException("A03 violation: soft-deleted normalized code was reused!");

        Console.WriteLine("  [PASS] PG-7, PG-8 & PG-9: Configurable rules, permanent normalized uniqueness, and A01 lock independence verified.");
    }

    private static async Task VerifyConcurrentDeactivationVsPutawayRaceAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd, Guid clientA)
    {
        Guid srcLocId, destBinId, putawayTaskId;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            var auth = new WarehouseMasterAuthorization(db, adminCtx);
            var createLoc = new CreateLocationCommandHandler(db, auth);
            var src = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.StagingLane, LocationCode = "STG-RACE", Barcode = "BC-STG-RACE", IsReceiving = true }, CancellationToken.None);
            var dest = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Bin, LocationCode = "BIN-RACE", Barcode = "BC-BIN-RACE" }, CancellationToken.None);
            srcLocId = src.Id;
            destBinId = dest.Id;

            var item = new Item { ClientId = clientA, Sku = "SKU-RACE", Description = "Race Item", WeightKg = 1m, LengthCm = 10, WidthCm = 10, HeightCm = 10, StorageTempClass = StorageTempClass.Ambient };
            db.Items.Add(item);
            db.StockOnHand.Add(new StockOnHand { ClientId = clientA, FacilityId = facOrd, ItemId = item.Id, LocationId = srcLocId, QtyAvailable = 10m });
            var task = new PutawayTask
            {
                ClientId = clientA,
                FacilityId = facOrd,
                ItemId = item.Id,
                SourceLocationId = srcLocId,
                SuggestedLocationId = destBinId,
                TargetLocationId = destBinId,
                Quantity = 5m,
                Status = PutawayTaskStatus.Pending
            };
            db.PutawayTasks.Add(task);
            putawayTaskId = task.Id;
            await db.SaveChangesAsync();
        }

        // Run UpdateLocationStatusCommand(Inactive) and ConfirmPutawayCommand concurrently against destBinId
        var tDeactivate = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            var handler = new UpdateLocationStatusCommandHandler(db1, new WarehouseMasterAuthorization(db1, adminCtx));
            await handler.Handle(new UpdateLocationStatusCommand(destBinId, LocationStatus.Inactive), CancellationToken.None);
        });
        var tPutaway = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            var opAuth = new WarehouseOperationalAuthorization(db2, adminCtx);
            var handler = new ConfirmPutawayCommandHandler(db2, adminCtx, opAuth);
            await handler.Handle(new ConfirmPutawayCommand { TaskId = putawayTaskId, ConfirmedTargetLocationId = destBinId, ConfirmedQuantity = 5m }, CancellationToken.None);
        });

        bool deactOk = false, putawayOk = false;
        try { await tDeactivate; deactOk = true; } catch (DomainException) { }
        try { await tPutaway; putawayOk = true; } catch (ValidationException) { }

        if (deactOk && putawayOk)
            throw new InvalidOperationException("A07 race violation: Both location deactivation and putaway confirmation succeeded concurrently!");

        // Verify final DB invariant: never Inactive with active stock
        await using (var checkDb = CreatePgDb(connStr, adminCtx))
        {
            var loc = await checkDb.Locations.AsNoTracking().FirstAsync(l => l.Id == destBinId);
            var stockQty = await checkDb.StockOnHand.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.LocationId == destBinId)
                .SumAsync(s => s.QtyAvailable + s.QtyAllocated + s.QtyQuarantined);
            if (loc.Status != LocationStatus.Active && stockQty > 0m)
                throw new InvalidOperationException($"A07 invariant broken: location is {loc.Status} while holding {stockQty} stock!");
        }

        Console.WriteLine("  [PASS] PG-10: Concurrent location deactivation vs putaway race serialized cleanly.");
    }

    private static async Task VerifyProfilesPackagingDockAndEpochOutboxAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd, Guid clientA, Guid clientB)
    {
        long initialFacilityEpoch;
        await using (var readDb = CreatePgDb(connStr, adminCtx))
        {
            initialFacilityEpoch = await readDb.ConfigurationScopeEpochs.AsNoTracking()
                .Where(e => e.ScopeType == ConfigurationScopeType.Facility && e.ScopeKey == facOrd.ToString("D").ToUpperInvariant())
                .Select(e => e.CurrentEpoch)
                .FirstOrDefaultAsync();
        }

        await using (var writeDb = CreatePgDb(connStr, adminCtx))
        {
            var auth = new WarehouseMasterAuthorization(writeDb, adminCtx);
            var dock = await new CreateDockDoorCommandHandler(writeDb, auth).Handle(new CreateDockDoorCommand
            {
                FacilityId = facOrd,
                DoorNumber = "D-PG-53",
                MaxTrailerLengthFeet = 53
            }, CancellationToken.None);
            if (dock.MaxTrailerLengthCm != 1615.44m)
                throw new InvalidOperationException($"Expected 1615.44 cm for 53 ft, got {dock.MaxTrailerLengthCm}.");

            await new CreatePackagingMaterialCommandHandler(writeDb, auth).Handle(new CreatePackagingMaterialCommand
            {
                FacilityId = facOrd,
                ClientId = null,
                Code = "PG-BOX-1",
                Name = "PG Facility Box",
                LengthCm = 30,
                WidthCm = 20,
                HeightCm = 15,
                MaxWeightKg = 20,
                TareWeightKg = 0.4m,
                CurrencyCode = "USD"
            }, CancellationToken.None);

            await new CreatePackagingMaterialCommandHandler(writeDb, auth).Handle(new CreatePackagingMaterialCommand
            {
                FacilityId = facOrd,
                ClientId = clientA,
                Code = "PG-BOX-1",
                Name = "PG Client A Box",
                LengthCm = 30,
                WidthCm = 20,
                HeightCm = 15,
                MaxWeightKg = 20,
                TareWeightKg = 0.4m,
                CurrencyCode = "USD"
            }, CancellationToken.None);

            // Client B without access must be rejected
            bool rejectedB = false;
            try
            {
                await new CreatePackagingMaterialCommandHandler(writeDb, auth).Handle(new CreatePackagingMaterialCommand
                {
                    FacilityId = facOrd,
                    ClientId = clientB,
                    Code = "PG-BOX-B",
                    Name = "Unauthorized Client B Box",
                    LengthCm = 30,
                    WidthCm = 20,
                    HeightCm = 15,
                    MaxWeightKg = 20,
                    TareWeightKg = 0.4m
                }, CancellationToken.None);
            }
            catch (ValidationException)
            {
                rejectedB = true;
            }
            if (!rejectedB)
                throw new InvalidOperationException("Unauthorized Client B packaging material was not rejected!");
        }

        // Second independent DbContext verifies MDM-1.5 epoch increment & outbox events
        await using (var observerDb = CreatePgDb(connStr, adminCtx))
        {
            var newFacilityEpoch = await observerDb.ConfigurationScopeEpochs.AsNoTracking()
                .Where(e => e.ScopeType == ConfigurationScopeType.Facility && e.ScopeKey == facOrd.ToString("D").ToUpperInvariant())
                .Select(e => e.CurrentEpoch)
                .FirstOrDefaultAsync();
            if (newFacilityEpoch <= initialFacilityEpoch)
                throw new InvalidOperationException($"Expected facility configuration epoch to advance from {initialFacilityEpoch}, got {newFacilityEpoch}.");

            var outboxCount = await observerDb.ConfigurationInvalidationEvents.AsNoTracking()
                .CountAsync(e => e.EntityType == nameof(DockDoor) || e.EntityType == nameof(PackagingMaterial));
            if (outboxCount < 2)
                throw new InvalidOperationException($"Expected >= 2 MDM-1.5 outbox events for DockDoor/PackagingMaterial, found {outboxCount}.");
        }

        Console.WriteLine("  [PASS] PG-11 & PG-12: Dock cm/ft conversion, packaging tenant isolation, and 2-context MDM-1.5 epoch/outbox invalidation verified.");
    }

    private static async Task VerifyR01StockBucketsAndDamagedHuGuardsAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd, Guid clientA)
    {
        await using var db = CreatePgDb(connStr, adminCtx);
        var auth = new WarehouseMasterAuthorization(db, adminCtx);
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var statusHandler = new UpdateLocationStatusCommandHandler(db, auth);

        var item = new Item { ClientId = clientA, Sku = "SKU-R01-PG", Description = "R01 PG Item", WeightKg = 1m };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var buckets = new (string Code, Action<StockOnHand> Configure)[]
        {
            ("R01-RES", s => s.QtyReserved = 2m),
            ("R01-DMG", s => s.QtyDamaged = 1m),
            ("R01-HLD", s => s.QtyOnHold = 3m),
        };

        foreach (var (code, configure) in buckets)
        {
            var loc = await createLoc.Handle(new CreateLocationCommand
            {
                FacilityId = facOrd,
                LocationType = LocationType.Bin,
                LocationCode = code,
                Barcode = $"BC-{code}"
            }, CancellationToken.None);

            var stock = new StockOnHand { ClientId = clientA, FacilityId = facOrd, ItemId = item.Id, LocationId = loc.Id };
            configure(stock);
            db.StockOnHand.Add(stock);
            await db.SaveChangesAsync();

            bool blocked = false;
            try
            {
                await statusHandler.Handle(new UpdateLocationStatusCommand(loc.Id, LocationStatus.Inactive), CancellationToken.None);
            }
            catch (DomainException)
            {
                blocked = true;
            }
            if (!blocked)
                throw new InvalidOperationException($"PG-14 R01 violation: Location {code} with non-zero stock bucket was deactivated!");
        }

        // Damaged HandlingUnit physically present in bin must block deactivation
        var huBin = await createLoc.Handle(new CreateLocationCommand
        {
            FacilityId = facOrd,
            LocationType = LocationType.Bin,
            LocationCode = "R01-HU-DMG",
            Barcode = "BC-R01-HU-DMG"
        }, CancellationToken.None);
        db.HandlingUnits.Add(new HandlingUnit
        {
            ClientId = clientA,
            FacilityId = facOrd,
            CurrentLocationId = huBin.Id,
            LpnCode = "LPN-PG-R01-DMG",
            UnitType = HandlingUnitType.Pallet,
            Status = HandlingUnitStatus.Damaged
        });
        await db.SaveChangesAsync();

        bool huBlocked = false;
        try
        {
            await statusHandler.Handle(new UpdateLocationStatusCommand(huBin.Id, LocationStatus.Inactive), CancellationToken.None);
        }
        catch (DomainException)
        {
            huBlocked = true;
        }
        if (!huBlocked)
            throw new InvalidOperationException("PG-14 R01 violation: Location with physically present Damaged HandlingUnit was deactivated!");

        Console.WriteLine("  [PASS] PG-14 (R01): QtyReserved, QtyDamaged, QtyOnHold and Damaged HandlingUnit block deactivation on live PostgreSQL.");
    }

    private static async Task VerifyR02DescendantDepthAndRuleConflictAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd)
    {
        await using var db = CreatePgDb(connStr, adminCtx);
        var auth = new WarehouseMasterAuthorization(db, adminCtx);
        var upsertRule = new UpsertLocationHierarchyRuleCommandHandler(db, auth);
        var createLoc = new CreateLocationCommandHandler(db, auth);
        var moveLoc = new MoveLocationCommandHandler(db, auth);

        // Configure facOrd rule: Level -> Bin with MaxDepth = 3
        await upsertRule.Handle(new UpsertLocationHierarchyRuleCommand
        {
            FacilityId = facOrd,
            ParentLocationType = LocationType.ShelfLevel,
            ChildLocationType = LocationType.Bin,
            MaxDepth = 3,
            IsStorableLeaf = true,
            IsActive = true
        }, CancellationToken.None);

        var aisle = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Aisle, LocationCode = "R02-AISLE", Barcode = "BC-R02-AISLE" }, CancellationToken.None);
        var rack = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ParentLocationId = aisle.Id, LocationType = LocationType.Rack, LocationCode = "R02-RACK", Barcode = "BC-R02-RACK" }, CancellationToken.None);
        var level = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.ShelfLevel, LocationCode = "R02-LEVEL", Barcode = "BC-R02-LEVEL" }, CancellationToken.None);
        var bin = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ParentLocationId = level.Id, LocationType = LocationType.Bin, LocationCode = "R02-BIN", Barcode = "BC-R02-BIN" }, CancellationToken.None);

        // Moving level (L1) + bin (L2) under rack (L2) would push bin to L4 > MaxDepth 3 -> MUST reject!
        bool depthBlocked = false;
        try
        {
            await moveLoc.Handle(new MoveLocationCommand(level.Id, rack.Id), CancellationToken.None);
        }
        catch (DomainException)
        {
            depthBlocked = true;
        }
        if (!depthBlocked)
            throw new InvalidOperationException("PG-15 R02 violation: MoveLocation allowed descendant bin to exceed configurable MaxDepth=3!");

        // Verify subtree remained completely unchanged
        var levelAfter = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == level.Id);
        var binAfter = await db.Locations.AsNoTracking().FirstAsync(l => l.Id == bin.Id);
        if (levelAfter.ParentLocationId != null || levelAfter.HierarchyLevel != 1 || binAfter.HierarchyLevel != 2)
            throw new InvalidOperationException("PG-15 R02 violation: Rejected move mutated subtree state!");

        // Attempting to disable Level -> Bin rule while bin exists under level -> MUST reject!
        bool ruleConflictBlocked = false;
        try
        {
            await upsertRule.Handle(new UpsertLocationHierarchyRuleCommand
            {
                FacilityId = facOrd,
                ParentLocationType = LocationType.ShelfLevel,
                ChildLocationType = LocationType.Bin,
                MaxDepth = 3,
                IsStorableLeaf = true,
                IsActive = false
            }, CancellationToken.None);
        }
        catch (DomainException)
        {
            ruleConflictBlocked = true;
        }
        if (!ruleConflictBlocked)
            throw new InvalidOperationException("PG-15 R02 violation: Rule mutation silently invalidated existing Level->Bin hierarchy!");

        Console.WriteLine("  [PASS] PG-15 (R02): Descendant configurable MaxDepth and rule-mutation conflict guards verified on live PostgreSQL.");
    }

    private static async Task VerifyR04ConcurrentParentDeactivationVsChildCreateAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd)
    {
        Guid parentAisleId;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            var createLoc = new CreateLocationCommandHandler(db, new WarehouseMasterAuthorization(db, adminCtx));
            var parent = await createLoc.Handle(new CreateLocationCommand
            {
                FacilityId = facOrd,
                LocationType = LocationType.Aisle,
                LocationCode = "R04-PAR-RACE",
                Barcode = "BC-R04-PAR-RACE"
            }, CancellationToken.None);
            parentAisleId = parent.Id;
        }

        var tDeactivate = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            await new UpdateLocationStatusCommandHandler(db1, new WarehouseMasterAuthorization(db1, adminCtx))
                .Handle(new UpdateLocationStatusCommand(parentAisleId, LocationStatus.Inactive), CancellationToken.None);
        });
        var tCreateChild = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            await new CreateLocationCommandHandler(db2, new WarehouseMasterAuthorization(db2, adminCtx))
                .Handle(new CreateLocationCommand
                {
                    FacilityId = facOrd,
                    ParentLocationId = parentAisleId,
                    LocationType = LocationType.Rack,
                    LocationCode = "R04-CHD-RACE",
                    Barcode = "BC-R04-CHD-RACE"
                }, CancellationToken.None);
        });

        bool deactOk = false, createOk = false;
        try { await tDeactivate; deactOk = true; } catch (DomainException) { }
        try { await tCreateChild; createOk = true; } catch (DomainException) { }

        if (deactOk && createOk)
            throw new InvalidOperationException("PG-16 R04 violation: Both parent deactivation and child creation succeeded concurrently!");

        await using (var checkDb = CreatePgDb(connStr, adminCtx))
        {
            var parent = await checkDb.Locations.AsNoTracking().FirstAsync(l => l.Id == parentAisleId);
            var activeChildren = await checkDb.Locations.AsNoTracking()
                .CountAsync(l => l.ParentLocationId == parentAisleId && l.Status == LocationStatus.Active && l.IsActive);
            if (parent.Status != LocationStatus.Active && activeChildren > 0)
                throw new InvalidOperationException("PG-16 R04 invariant broken: Active child persisted under inactive parent!");
        }

        Console.WriteLine("  [PASS] PG-16 (R04): Concurrent parent deactivation vs child creation serialized cleanly.");
    }

    private static async Task VerifyR04ConcurrentDeactivationVsPutawayTaskGenerationAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd, Guid clientA)
    {
        Guid facIsoId = Guid.NewGuid();
        Guid destBinId;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            db.Facilities.Add(new Facility
            {
                Id = facIsoId,
                FacilityCode = "FAC-R04-PUT",
                Name = "R04 Putaway Race Hub",
                CountryCode = "US",
                Timezone = "America/Chicago",
                DefaultCurrencyCode = "USD",
                IsActive = true
            });
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientA, FacilityId = facIsoId, Status = FacilityAccessStatus.Active });
            await db.SaveChangesAsync();

            var auth = new WarehouseMasterAuthorization(db, adminCtx);
            var createLoc = new CreateLocationCommandHandler(db, auth);
            var stg = await createLoc.Handle(new CreateLocationCommand { FacilityId = facIsoId, LocationType = LocationType.StagingLane, LocationCode = "STG-R04", Barcode = "BC-STG-R04", IsReceiving = true }, CancellationToken.None);
            var bin = await createLoc.Handle(new CreateLocationCommand { FacilityId = facIsoId, LocationType = LocationType.Bin, LocationCode = "BIN-R04-PUT", Barcode = "BC-BIN-R04-PUT", IsStorable = true }, CancellationToken.None);
            destBinId = bin.Id;

            var item = new Item { ClientId = clientA, Sku = "SKU-R04-PUT", Description = "R04 Putaway Item", WeightKg = 1m, LengthCm = 10, WidthCm = 10, HeightCm = 10, StorageTempClass = StorageTempClass.Ambient };
            db.Items.Add(item);
            var session = new ReceivingSession
            {
                ClientId = clientA,
                FacilityId = facIsoId,
                SessionNumber = "RCV-R04-001",
                Status = ReceivingStatus.Completed,
                ReceivedByUserId = adminCtx.UserId!.Value,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-10),
                CompletedAtUtc = DateTime.UtcNow.AddMinutes(-5)
            };
            db.ReceivingSessions.Add(session);
            db.ReceiptLines.Add(new ReceiptLine
            {
                ReceivingSessionId = session.Id,
                ItemId = item.Id,
                Lpn = "LPN-R04-PUT-001",
                QuantityExpected = 5m,
                QuantityReceived = 5m,
                StagingLocationId = stg.Id,
                Condition = ReceiptConditionStatus.Sound,
                ScannedAtUtc = DateTime.UtcNow.AddMinutes(-6)
            });
            await db.SaveChangesAsync();
        }

        var tDeactivate = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            await new UpdateLocationStatusCommandHandler(db1, new WarehouseMasterAuthorization(db1, adminCtx))
                .Handle(new UpdateLocationStatusCommand(destBinId, LocationStatus.Inactive), CancellationToken.None);
        });
        var tGenerate = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            var selector = new PutawayLocationSelector(db2, NullLogger<PutawayLocationSelector>.Instance);
            var opAuth = new WarehouseOperationalAuthorization(db2, adminCtx);
            await new GeneratePutawayTasksCommandHandler(db2, selector, opAuth)
                .Handle(new GeneratePutawayTasksCommand { FacilityId = facIsoId, ClientId = clientA }, CancellationToken.None);
        });

        try { await tDeactivate; } catch (DomainException) { }
        try { await tGenerate; } catch (Exception) { }

        await using (var checkDb = CreatePgDb(connStr, adminCtx))
        {
            var bin = await checkDb.Locations.AsNoTracking().FirstAsync(l => l.Id == destBinId);
            var openTasks = await checkDb.PutawayTasks.AsNoTracking()
                .CountAsync(t => t.SuggestedLocationId == destBinId &&
                                 (t.Status == PutawayTaskStatus.Pending || t.Status == PutawayTaskStatus.Assigned));
            if (bin.Status != LocationStatus.Active && openTasks > 0)
                throw new InvalidOperationException($"PG-17 R04 invariant broken: {openTasks} open putaway task(s) target inactive bin!");
        }

        Console.WriteLine("  [PASS] PG-17 (R04): Concurrent location deactivation vs putaway task generation serialized cleanly.");
    }

    private static async Task VerifyR04ConcurrentNonStorableVsTransferAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd, Guid clientA)
    {
        Guid srcId, destId, itemId;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            var createLoc = new CreateLocationCommandHandler(db, new WarehouseMasterAuthorization(db, adminCtx));
            var src = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Bin, LocationCode = "R04-TR-SRC", Barcode = "BC-R04-TR-SRC" }, CancellationToken.None);
            var dest = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Bin, LocationCode = "R04-TR-DST", Barcode = "BC-R04-TR-DST" }, CancellationToken.None);
            srcId = src.Id;
            destId = dest.Id;

            var item = new Item { ClientId = clientA, Sku = "SKU-R04-TR", Description = "R04 Transfer Item", WeightKg = 1m };
            db.Items.Add(item);
            itemId = item.Id;
            db.StockOnHand.Add(new StockOnHand { ClientId = clientA, FacilityId = facOrd, ItemId = itemId, LocationId = srcId, QtyAvailable = 10m });
            await db.SaveChangesAsync();
        }

        var tNonStorable = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            await new UpdateLocationCommandHandler(db1, new WarehouseMasterAuthorization(db1, adminCtx))
                .Handle(new UpdateLocationCommand { Id = destId, IsStorable = false }, CancellationToken.None);
        });
        var tTransfer = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            await new TransferInventoryCommandHandler(db2, adminCtx, new WarehouseOperationalAuthorization(db2, adminCtx))
                .Handle(new TransferInventoryCommand
                {
                    ClientId = clientA,
                    FacilityId = facOrd,
                    ItemId = itemId,
                    FromLocationId = srcId,
                    ToLocationId = destId,
                    Quantity = 4m
                }, CancellationToken.None);
        });

        bool nonStorableOk = false, transferOk = false;
        try { await tNonStorable; nonStorableOk = true; } catch (DomainException) { }
        try { await tTransfer; transferOk = true; } catch (ValidationException) { } catch (DomainException) { }

        if (nonStorableOk && transferOk)
            throw new InvalidOperationException("PG-18 R04 violation: Both IsStorable=false and TransferInventoryCommand succeeded concurrently!");

        await using (var checkDb = CreatePgDb(connStr, adminCtx))
        {
            var dest = await checkDb.Locations.AsNoTracking().FirstAsync(l => l.Id == destId);
            var stockQty = await checkDb.StockOnHand.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.LocationId == destId)
                .SumAsync(s => s.QtyAvailable + s.QtyAllocated + s.QtyReserved + s.QtyQuarantined + s.QtyDamaged + s.QtyOnHold);
            if (!dest.IsStorable && stockQty > 0m)
                throw new InvalidOperationException($"PG-18 R04 invariant broken: Non-storable bin holds {stockQty} stock!");
        }

        Console.WriteLine("  [PASS] PG-18 (R04): Concurrent IsStorable=false vs TransferInventoryCommand serialized cleanly.");
    }

    private static async Task VerifyR04ConcurrentProfileTighteningVsTransferAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd, Guid clientA)
    {
        Guid srcId, destId, item2Id;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            var createLoc = new CreateLocationCommandHandler(db, new WarehouseMasterAuthorization(db, adminCtx));
            var src = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Bin, LocationCode = "R04-PR-SRC", Barcode = "BC-R04-PR-SRC" }, CancellationToken.None);
            var dest = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Bin, LocationCode = "R04-PR-DST", Barcode = "BC-R04-PR-DST" }, CancellationToken.None);
            srcId = src.Id;
            destId = dest.Id;

            var item1 = new Item { ClientId = clientA, Sku = "SKU-R04-PR-1", Description = "R04 Profile Item 1", WeightKg = 1m };
            var item2 = new Item { ClientId = clientA, Sku = "SKU-R04-PR-2", Description = "R04 Profile Item 2", WeightKg = 1m };
            db.Items.AddRange(item1, item2);
            item2Id = item2.Id;
            db.StockOnHand.AddRange(
                new StockOnHand { ClientId = clientA, FacilityId = facOrd, ItemId = item1.Id, LocationId = destId, QtyAvailable = 5m },
                new StockOnHand { ClientId = clientA, FacilityId = facOrd, ItemId = item2Id, LocationId = srcId, QtyAvailable = 10m });
            await db.SaveChangesAsync();
        }

        var tTighten = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            await new UpsertLocationStorageProfileCommandHandler(db1, new WarehouseMasterAuthorization(db1, adminCtx))
                .Handle(new UpsertLocationStorageProfileCommand { LocationId = destId, AllowMixedSku = false }, CancellationToken.None);
        });
        var tTransfer = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            await new TransferInventoryCommandHandler(db2, adminCtx, new WarehouseOperationalAuthorization(db2, adminCtx))
                .Handle(new TransferInventoryCommand
                {
                    ClientId = clientA,
                    FacilityId = facOrd,
                    ItemId = item2Id,
                    FromLocationId = srcId,
                    ToLocationId = destId,
                    Quantity = 4m
                }, CancellationToken.None);
        });

        bool tightenOk = false, transferOk = false;
        try { await tTighten; tightenOk = true; } catch (DomainException) { }
        try { await tTransfer; transferOk = true; } catch (DomainException) { }

        if (tightenOk && transferOk)
            throw new InvalidOperationException("PG-19 R04 violation: Both AllowMixedSku=false and transferring a second SKU succeeded concurrently!");

        await using (var checkDb = CreatePgDb(connStr, adminCtx))
        {
            var profile = await checkDb.LocationStorageProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == destId);
            var distinctSkus = await checkDb.StockOnHand.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.LocationId == destId &&
                            (s.QtyAvailable + s.QtyAllocated + s.QtyReserved + s.QtyQuarantined + s.QtyDamaged + s.QtyOnHold) > 0m)
                .Select(s => s.ItemId)
                .Distinct()
                .CountAsync();
            if (profile != null && !profile.AllowMixedSku && distinctSkus > 1)
                throw new InvalidOperationException($"PG-19 R04 invariant broken: Storage profile has AllowMixedSku=false while holding {distinctSkus} distinct SKUs!");
        }

        Console.WriteLine("  [PASS] PG-19 (R04): Concurrent storage-profile tightening (AllowMixedSku=false) vs TransferInventoryCommand serialized cleanly.");
    }

    private static async Task VerifyR04ConcurrentRuleChangeVsReparentingAndTriggerLockAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd)
    {
        Guid facIso = Guid.NewGuid();
        Guid aisleA, aisleB, sqlA, sqlB;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            db.Facilities.Add(new Facility
            {
                Id = facIso,
                FacilityCode = "FAC-R04-RUL",
                Name = "R04 Rule Race Hub",
                CountryCode = "US",
                Timezone = "America/Chicago",
                DefaultCurrencyCode = "USD",
                IsActive = true
            });
            await db.SaveChangesAsync();

            var auth = new WarehouseMasterAuthorization(db, adminCtx);
            await new UpsertLocationHierarchyRuleCommandHandler(db, auth).Handle(new UpsertLocationHierarchyRuleCommand
            {
                FacilityId = facIso,
                ParentLocationType = LocationType.Aisle,
                ChildLocationType = LocationType.Aisle,
                MaxDepth = 5,
                IsStorableLeaf = false,
                IsActive = true
            }, CancellationToken.None);

            var createLoc = new CreateLocationCommandHandler(db, auth);
            aisleA = (await createLoc.Handle(new CreateLocationCommand { FacilityId = facIso, LocationType = LocationType.Aisle, LocationCode = "RR-A", Barcode = "BC-RR-A" }, CancellationToken.None)).Id;
            aisleB = (await createLoc.Handle(new CreateLocationCommand { FacilityId = facIso, LocationType = LocationType.Aisle, LocationCode = "RR-B", Barcode = "BC-RR-B" }, CancellationToken.None)).Id;
            sqlA = (await createLoc.Handle(new CreateLocationCommand { FacilityId = facIso, LocationType = LocationType.Aisle, LocationCode = "SQL-A", Barcode = "BC-SQL-A" }, CancellationToken.None)).Id;
            sqlB = (await createLoc.Handle(new CreateLocationCommand { FacilityId = facIso, LocationType = LocationType.Aisle, LocationCode = "SQL-B", Barcode = "BC-SQL-B" }, CancellationToken.None)).Id;
        }

        // Part A: Concurrent rule disable (Aisle -> Aisle IsActive=false) vs MoveLocation(aisleA -> aisleB)
        var tDisableRule = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            await new UpsertLocationHierarchyRuleCommandHandler(db1, new WarehouseMasterAuthorization(db1, adminCtx))
                .Handle(new UpsertLocationHierarchyRuleCommand
                {
                    FacilityId = facIso,
                    ParentLocationType = LocationType.Aisle,
                    ChildLocationType = LocationType.Aisle,
                    MaxDepth = 5,
                    IsStorableLeaf = false,
                    IsActive = false
                }, CancellationToken.None);
        });
        var tMove = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            await new MoveLocationCommandHandler(db2, new WarehouseMasterAuthorization(db2, adminCtx))
                .Handle(new MoveLocationCommand(aisleA, aisleB), CancellationToken.None);
        });

        bool ruleOk = false, moveOk = false;
        try { await tDisableRule; ruleOk = true; } catch (DomainException) { }
        try { await tMove; moveOk = true; } catch (DomainException) { }

        if (ruleOk && moveOk)
            throw new InvalidOperationException("PG-20 R04 violation: Both disabling Aisle->Aisle rule and moving Aisle under Aisle succeeded concurrently!");

        // Part B: Direct SQL concurrent opposite reparenting testing upgraded fn_locations_prevent_cycle() advisory lock
        var tSql1 = Task.Run(async () =>
        {
            await using var c1 = new NpgsqlConnection(connStr);
            await c1.OpenAsync();
            await using var cmd = new NpgsqlCommand($"UPDATE locations SET parent_location_id = '{sqlB}' WHERE location_id = '{sqlA}'", c1);
            await cmd.ExecuteNonQueryAsync();
        });
        var tSql2 = Task.Run(async () =>
        {
            await using var c2 = new NpgsqlConnection(connStr);
            await c2.OpenAsync();
            await using var cmd = new NpgsqlCommand($"UPDATE locations SET parent_location_id = '{sqlA}' WHERE location_id = '{sqlB}'", c2);
            await cmd.ExecuteNonQueryAsync();
        });

        int sqlOk = 0, sqlRejected = 0;
        foreach (var t in new[] { tSql1, tSql2 })
        {
            try { await t; sqlOk++; }
            catch (PostgresException pgEx) when (
                pgEx.MessageText.Contains("LOCATION_HIERARCHY_CYCLE", StringComparison.Ordinal) ||
                pgEx.SqlState == "40P01")
            {
                sqlRejected++;
            }
        }
        if (sqlOk != 1 || sqlRejected != 1)
            throw new InvalidOperationException($"PG-20 R04 violation: Expected 1 direct-SQL success and 1 rejection (cycle or deadlock), got ok={sqlOk}, rejected={sqlRejected}.");

        await using (var verifySqlDb = CreatePgDb(connStr, adminCtx))
        {
            var aRow = await verifySqlDb.Locations.AsNoTracking().FirstAsync(l => l.Id == sqlA);
            var bRow = await verifySqlDb.Locations.AsNoTracking().FirstAsync(l => l.Id == sqlB);
            if (aRow.ParentLocationId == sqlB && bRow.ParentLocationId == sqlA)
                throw new InvalidOperationException("PG-20 R04 violation: Direct-SQL opposite reparenting persisted a cycle!");

            await verifySqlDb.Database.ExecuteSqlRawAsync(
                $"UPDATE locations SET parent_location_id = NULL, hierarchy_level = 1, hierarchy_path = '/' || replace(location_id::text, '-', '') || '/' WHERE location_id IN ('{sqlA}', '{sqlB}');");
        }

        Console.WriteLine("  [PASS] PG-20 (R04): Concurrent rule mutation vs reparenting & direct-SQL fn_locations_prevent_cycle() advisory lock verified.");
    }

    private sealed class TestTzResolver : ITimeZoneOffsetResolver
    {
        public int GetUtcOffsetMinutes(string timeZoneId, DateTime utcDateTime) => 0;
        public bool TryValidateTimeZoneId(string timeZoneId) => true;
    }

    private static async Task VerifyF01AggregateGuardsAsync(
        string connStr, Mdm21Identity adminCtx, Guid clientA)
    {
        // PG-21: Facility deactivation blocked by open cycle-count and active allocation
        var facF01 = Guid.NewGuid();
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            db.Facilities.Add(new Facility
            {
                Id = facF01,
                FacilityCode = "FAC-PG21",
                Name = "PG21 Guard Hub",
                CountryCode = "US",
                Timezone = "America/Chicago",
                DefaultCurrencyCode = "USD",
                IsActive = true
            });
            var item = new Item { ClientId = clientA, Sku = "SKU-PG21", Description = "PG21 Item", WeightKg = 1m };
            db.Items.Add(item);
            var zone = new WarehouseZone
            {
                FacilityId = facF01,
                ZoneCode = "ZN-PG22",
                NormalizedZoneCode = "ZN-PG22",
                Name = "Legacy Inconsistent Zone",
                ZoneType = WarehouseZoneType.BulkStorage,
                IsActive = true
            };
            db.WarehouseZones.Add(zone);
            var inactiveBin = new Location
            {
                FacilityId = facF01,
                ZoneId = zone.Id,
                LocationType = LocationType.Bin,
                LocationCode = "PG21-INACT",
                Barcode = "BC-PG21-INACT",
                Status = LocationStatus.Inactive,
                IsActive = false
            };
            db.Locations.Add(inactiveBin);
            var cc = new CycleCount { FacilityId = facF01, Status = CycleCountStatus.InProgress };
            db.CycleCounts.Add(cc);
            db.CycleCountItems.Add(new CycleCountItem { CycleCountId = cc.Id, LocationId = inactiveBin.Id, ItemId = item.Id, ExpectedQty = 3m, IsReconciled = false });
            await db.SaveChangesAsync();

            var updateFac = new UpdateFacilityCommandHandler(db, new CompliancePlatformAuthorization(db, adminCtx), new TestTzResolver());
            bool blockedByCc = false;
            try
            {
                await updateFac.Handle(new UpdateFacilityCommand(facF01, null, "PG21 Guard Hub", null, null, null, null, null, "US", "America/Chicago", null, "USD", null, null, null, 0, IsActive: false), CancellationToken.None);
            }
            catch (DomainException) { blockedByCc = true; }
            if (!blockedByCc)
                throw new InvalidOperationException("PG-21 F01 violation: Facility with open cycle count was deactivated!");

            // PG-22: Zone deactivation blocked by legacy inconsistent inactive location holding unreconciled cycle-count item & active allocation
            var zoneStatus = new UpdateWarehouseZoneStatusCommandHandler(db, new WarehouseMasterAuthorization(db, adminCtx));
            bool zoneBlockedByLegacyCc = false;
            try
            {
                await zoneStatus.Handle(new UpdateWarehouseZoneStatusCommand(zone.Id, IsActive: false), CancellationToken.None);
            }
            catch (DomainException) { zoneBlockedByLegacyCc = true; }
            if (!zoneBlockedByLegacyCc)
                throw new InvalidOperationException("PG-22 F01 violation: Zone with inactive location holding open cycle-count item was deactivated!");

            // Close cycle count, add active allocation on inactiveBin -> MUST still block both Facility & Zone deactivation!
            cc.Status = CycleCountStatus.Completed;
            var pg21Ord = new OutboundOrder { ClientId = clientA, FacilityId = facF01, OrderNumber = "ORD-PG21-AL", CustomerName = "PG21 Cust", Status = OutboundOrderStatus.Completed };
            var pg21OrdLine = new OutboundOrderLine { OutboundOrderId = pg21Ord.Id, ItemId = item.Id, LineNumber = 1, QuantityOrdered = 2m, Status = OutboundOrderLineStatus.Allocated };
            var pg21ZeroStock = new StockOnHand { ClientId = clientA, FacilityId = facF01, ItemId = item.Id, LocationId = inactiveBin.Id, QtyAvailable = 0m };
            db.OutboundOrders.Add(pg21Ord);
            db.OutboundOrderLines.Add(pg21OrdLine);
            db.StockOnHand.Add(pg21ZeroStock);
            var alloc = new InventoryAllocation
            {
                ClientId = clientA,
                FacilityId = facF01,
                ItemId = item.Id,
                LocationId = inactiveBin.Id,
                OutboundOrderId = pg21Ord.Id,
                OutboundOrderLineId = pg21OrdLine.Id,
                StockOnHandId = pg21ZeroStock.Id,
                QuantityRequested = 2m,
                QuantityAllocated = 2m,
                Status = AllocationStatus.Allocated
            };
            db.InventoryAllocations.Add(alloc);
            await db.SaveChangesAsync();

            bool facBlockedByAlloc = false;
            try
            {
                await updateFac.Handle(new UpdateFacilityCommand(facF01, null, "PG21 Guard Hub", null, null, null, null, null, "US", "America/Chicago", null, "USD", null, null, null, 0, IsActive: false), CancellationToken.None);
            }
            catch (DomainException) { facBlockedByAlloc = true; }
            if (!facBlockedByAlloc)
                throw new InvalidOperationException("PG-21 F01 violation: Facility with active inventory allocation was deactivated!");

            bool zoneBlockedByAlloc = false;
            try
            {
                await zoneStatus.Handle(new UpdateWarehouseZoneStatusCommand(zone.Id, IsActive: false), CancellationToken.None);
            }
            catch (DomainException) { zoneBlockedByAlloc = true; }
            if (!zoneBlockedByAlloc)
                throw new InvalidOperationException("PG-22 F01 violation: Zone with inactive location holding active allocation was deactivated!");

            // Clean up allocation so facility can cleanly deactivate if needed later
            alloc.Status = AllocationStatus.Released;
            await db.SaveChangesAsync();
        }

        Console.WriteLine("  [PASS] PG-21 & PG-22 (F01): Facility & Zone aggregate dependency guards (cycle counts, allocations, legacy inactive locations) verified on live PostgreSQL.");
    }

    private static async Task VerifyF02ReturnMoveAndCrossDockEligibilityAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd, Guid facKtm, Guid clientA, Guid clientB)
    {
        await using var db = CreatePgDb(connStr, adminCtx);
        var auth = new WarehouseMasterAuthorization(db, adminCtx);
        var createLoc = new CreateLocationCommandHandler(db, auth);

        // Give clientB access to facOrd so we can create dedicated clientB zones
        if (!await db.ClientFacilityAccesses.AnyAsync(a => a.ClientId == clientB && a.FacilityId == facOrd))
        {
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientB, FacilityId = facOrd, Status = FacilityAccessStatus.Active });
            await db.SaveChangesAsync();
        }

        var zoneB = await new CreateWarehouseZoneCommandHandler(db, auth).Handle(new CreateWarehouseZoneCommand
        {
            FacilityId = facOrd,
            ZoneCode = "ZN-PG-F02B",
            Name = "PG F02 Client B Zone",
            DedicatedClientId = clientB
        }, CancellationToken.None);

        var aisle = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Aisle, LocationCode = "PG-F02-AISLE", Barcode = "BC-PG-F02-AISLE" }, CancellationToken.None);
        var binClientB = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ZoneId = zoneB.Id, LocationType = LocationType.Bin, LocationCode = "PG-F02-BINB", Barcode = "BC-PG-F02-BINB" }, CancellationToken.None);
        var validBin = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Bin, LocationCode = "PG-F02-BINOK", Barcode = "BC-PG-F02-BINOK" }, CancellationToken.None);
        var ktmBin = await createLoc.Handle(new CreateLocationCommand { FacilityId = facKtm, LocationType = LocationType.Bin, LocationCode = "PG-F02-KTMBIN", Barcode = "BC-PG-F02-KTMBIN" }, CancellationToken.None);

        var itemA = new Item { ClientId = clientA, Sku = "SKU-PG-F02A", Description = "F02 Return SKU", WeightKg = 1m };
        db.Items.Add(itemA);

        var origOrder = new OutboundOrder { ClientId = clientA, FacilityId = facOrd, OrderNumber = "ORD-PG23-RET", CustomerName = "PG23 Cust", Status = OutboundOrderStatus.Completed };
        var origPkg = new Package { ClientId = clientA, FacilityId = facOrd, OutboundOrderId = origOrder.Id, PackageNumber = "PKG-PG23-RET", TrackingNumber = "TRK-PG23-RET", Status = PackageStatus.Loaded };
        var retAuth = ReturnAuthorization.Create(clientA, facOrd, facOrd, origOrder.Id, null, origPkg.Id, null, "RA-PG23", ReturnReason.DamagedInTransit, "PG23 Test", adminCtx.UserId!.Value, "RMA-PG23", DateTime.UtcNow.AddDays(7));
        var retShip = new ReturnShipment { ClientId = clientA, ReturnAuthorizationId = retAuth.Id, OrderId = origOrder.Id, PackageId = origPkg.Id, OriginFacilityId = facOrd, DestinationFacilityId = facOrd, CarrierCode = "FEDEX", TrackingNumber = origPkg.TrackingNumber, AuthorizedByUserId = adminCtx.UserId!.Value, Status = ReturnShipmentStatus.DeliveredAtWarehouse, DeliveredAtWarehouseUtc = DateTime.UtcNow.AddHours(-1) };
        var handoff = ReturnInboundHandoff.Create(clientA, facOrd, retShip.Id, retAuth.Id, origPkg.Id, origOrder.Id, "HND-PG23", retAuth.RmaNumber!, retShip.TrackingNumber, retShip.CarrierCode, DateTime.UtcNow.AddHours(-1), "D-1", "Arrived");
        db.OutboundOrders.Add(origOrder);
        db.Packages.Add(origPkg);
        db.ReturnAuthorizations.Add(retAuth);
        db.ReturnShipments.Add(retShip);
        db.ReturnInboundHandoffs.Add(handoff);

        var retSession = new ReturnReceivingSession
        {
            ClientId = clientA,
            FacilityId = facOrd,
            ReturnInboundHandoffId = handoff.Id,
            SessionNumber = "RRS-PG23-001",
            Status = ReturnReceivingSessionStatus.Inspected,
            IsInventoryPosted = false
        };
        var retLine = new ReturnReceivingLine
        {
            ReturnReceivingSessionId = retSession.Id,
            ItemId = itemA.Id,
            Sku = itemA.Sku,
            ExpectedQuantity = 6m,
            ReceivedQuantity = 6m
        };
        retLine.AssignDisposition(6m, 0m, 0m, ReturnDisposition.RestockToAvailable, null, "OK", DateTime.UtcNow);
        retSession.Lines.Add(retLine);
        db.ReturnReceivingSessions.Add(retSession);
        await db.SaveChangesAsync();

        var retSvc = new ReturnReceivingService(db, adminCtx, adminCtx, NullLogger<ReturnReceivingService>.Instance);

        // PG-23: Reject structural Aisle
        bool aisleRejected = false;
        try { await retSvc.PostReturnInventoryAsync(new PostReturnInventoryRequest { SessionId = retSession.Id, OperatorUserId = adminCtx.UserId!.Value, DefaultLocationId = aisle.Id }); }
        catch (ValidationException) { aisleRejected = true; }
        if (!aisleRejected) throw new InvalidOperationException("PG-23 F02-A violation: Return posting accepted structural Aisle!");

        // PG-24: Reject cross-facility & dedicated-client destination, verify rollback/zero ledger, then succeed on validBin
        bool crossFacRejected = false;
        try { await retSvc.PostReturnInventoryAsync(new PostReturnInventoryRequest { SessionId = retSession.Id, OperatorUserId = adminCtx.UserId!.Value, DefaultLocationId = ktmBin.Id }); }
        catch (ValidationException) { crossFacRejected = true; }
        if (!crossFacRejected) throw new InvalidOperationException("PG-24 F02-A violation: Return posting accepted cross-facility bin!");

        bool dedClientRejected = false;
        try { await retSvc.PostReturnInventoryAsync(new PostReturnInventoryRequest { SessionId = retSession.Id, OperatorUserId = adminCtx.UserId!.Value, DefaultLocationId = binClientB.Id }); }
        catch (ValidationException) { dedClientRejected = true; }
        if (!dedClientRejected) throw new InvalidOperationException("PG-24 F02-A violation: Return posting accepted Client-B dedicated bin for Client-A!");

        if (await db.StockOnHand.IgnoreQueryFilters().AnyAsync(s => s.ItemId == itemA.Id))
            throw new InvalidOperationException("PG-24 F02-A violation: Rejected return posting persisted StockOnHand!");

        var posted = await retSvc.PostReturnInventoryAsync(new PostReturnInventoryRequest { SessionId = retSession.Id, OperatorUserId = adminCtx.UserId!.Value, DefaultLocationId = validBin.Id });
        if (!posted.IsSuccess || posted.TotalAcceptedQuantity != 6m)
            throw new InvalidOperationException("PG-24 F02-A violation: Valid return posting failed!");

        // PG-25: MoveLocation into incompatible dedicated zoneB MUST reject (validBin now holds Client-A stock!) and preserve ZoneId = null
        bool moveRejected = false;
        try { await new MoveLocationCommandHandler(db, auth).Handle(new MoveLocationCommand(validBin.Id, null, zoneB.Id), CancellationToken.None); }
        catch (DomainException) { moveRejected = true; }
        if (!moveRejected) throw new InvalidOperationException("PG-25 F02-B violation: MoveLocation allowed bin with Client-A stock into Client-B dedicated zone!");
        if ((await db.Locations.AsNoTracking().FirstAsync(l => l.Id == validBin.Id)).ZoneId != null)
            throw new InvalidOperationException("PG-25 F02-B violation: Rejected MoveLocation mutated ZoneId!");

        // PG-30: Cross-dock structural & dedicated override rejection + rollback conservation + valid staging completion
        var cdSrc = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.StagingLane, LocationCode = "PG30-SRC", Barcode = "BC-PG30-SRC", IsReceiving = true }, CancellationToken.None);
        var cdDst = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.StagingLane, LocationCode = "PG30-DST", Barcode = "BC-PG30-DST" }, CancellationToken.None);
        var cdBadDed = await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ZoneId = zoneB.Id, LocationType = LocationType.StagingLane, LocationCode = "PG30-DSTB", Barcode = "BC-PG30-DSTB" }, CancellationToken.None);

        db.StockOnHand.Add(new StockOnHand { ClientId = clientA, FacilityId = facOrd, ItemId = itemA.Id, LocationId = cdSrc.Id, QtyAllocated = 5m });
        var plan = new CrossDockPlan { ClientId = clientA, FacilityId = facOrd, PlanNumber = "CDP-PG30", TotalPlannedQuantity = 5m, Status = CrossDockPlanStatus.InProgress };
        var planLine = new CrossDockPlanLine { CrossDockPlanId = plan.Id, ItemId = itemA.Id, PlannedQuantity = 5m, Status = CrossDockLineStatus.InProgress };
        var cdTask = new CrossDockTask
        {
            CrossDockPlanId = plan.Id,
            CrossDockPlanLineId = planLine.Id,
            ClientId = clientA,
            FacilityId = facOrd,
            ItemId = itemA.Id,
            TaskNumber = "CDT-PG30",
            Lpn = "LPN-PG30",
            SourceLocationId = cdSrc.Id,
            DestinationLocationId = cdDst.Id,
            Quantity = 5m,
            Status = CrossDockTaskStatus.Pending
        };
        db.CrossDockPlans.Add(plan);
        db.CrossDockPlanLines.Add(planLine);
        db.CrossDockTasks.Add(cdTask);
        await db.SaveChangesAsync();

        var cdHandler = new ConfirmCrossDockTaskCommandHandler(db, adminCtx);
        bool cdAisleRejected = false;
        try { await cdHandler.Handle(new ConfirmCrossDockTaskCommand { TaskId = cdTask.Id, ScannedLpn = "LPN-PG30", ScannedDestinationBarcode = "BC-PG-F02-AISLE", AllowOverride = true }, CancellationToken.None); }
        catch (ValidationException) { cdAisleRejected = true; }
        if (!cdAisleRejected) throw new InvalidOperationException("PG-30 F02-C violation: Cross-dock override accepted structural Aisle!");

        bool cdDedRejected = false;
        try { await cdHandler.Handle(new ConfirmCrossDockTaskCommand { TaskId = cdTask.Id, ScannedLpn = "LPN-PG30", ScannedDestinationBarcode = "BC-PG30-DSTB", AllowOverride = true }, CancellationToken.None); }
        catch (ValidationException) { cdDedRejected = true; }
        if (!cdDedRejected) throw new InvalidOperationException("PG-30 F02-C violation: Cross-dock override accepted Client-B dedicated zone!");

        // Verify source stock remained intact (5m) and destination has 0m before valid confirmation
        var srcBefore = await db.StockOnHand.AsNoTracking().FirstAsync(s => s.LocationId == cdSrc.Id && s.ItemId == itemA.Id);
        if (srcBefore.QtyAllocated != 5m) throw new InvalidOperationException("PG-30 violation: Rejected cross-dock mutated source stock!");

        var cdOk = await cdHandler.Handle(new ConfirmCrossDockTaskCommand { TaskId = cdTask.Id, ScannedLpn = "LPN-PG30", ScannedDestinationBarcode = "BC-PG30-DST", AllowOverride = false }, CancellationToken.None);
        if (cdOk.Status != CrossDockTaskStatus.Completed) throw new InvalidOperationException("PG-30 violation: Valid cross-dock confirmation failed!");
        var srcAfter = await db.StockOnHand.AsNoTracking().FirstAsync(s => s.LocationId == cdSrc.Id && s.ItemId == itemA.Id);
        var dstAfter = await db.StockOnHand.AsNoTracking().FirstAsync(s => s.LocationId == cdDst.Id && s.ItemId == itemA.Id);
        if (srcAfter.QtyAllocated != 0m || dstAfter.QtyAllocated != 5m)
            throw new InvalidOperationException($"PG-30 violation: Inventory conservation failed (src={srcAfter.QtyAllocated}, dst={dstAfter.QtyAllocated}).");

        Console.WriteLine("  [PASS] PG-23, PG-24, PG-25 & PG-30 (F02): Return posting, MoveLocation zone dedication parity, and Cross-Dock eligibility/conservation verified on live PostgreSQL.");
    }

    private static async Task VerifyF03ConcurrentZonePackingAndFacilityBoundariesAsync(
        string connStr, Mdm21Identity adminCtx, Guid facOrd, Guid clientA, Guid clientB)
    {
        // PG-26: Concurrent Zone dedication (Client B) vs Inventory transfer (Client A -> bin in zone)
        Guid z26Id, src26Id, dst26Id, item26Id;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            var auth = new WarehouseMasterAuthorization(db, adminCtx);
            var z = await new CreateWarehouseZoneCommandHandler(db, auth).Handle(new CreateWarehouseZoneCommand { FacilityId = facOrd, ZoneCode = "ZN-PG26", Name = "PG26 Race Zone" }, CancellationToken.None);
            z26Id = z.Id;
            var createLoc = new CreateLocationCommandHandler(db, auth);
            src26Id = (await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, LocationType = LocationType.Bin, LocationCode = "PG26-SRC", Barcode = "BC-PG26-SRC" }, CancellationToken.None)).Id;
            dst26Id = (await createLoc.Handle(new CreateLocationCommand { FacilityId = facOrd, ZoneId = z26Id, LocationType = LocationType.Bin, LocationCode = "PG26-DST", Barcode = "BC-PG26-DST" }, CancellationToken.None)).Id;
            var item = new Item { ClientId = clientA, Sku = "SKU-PG26", Description = "PG26 Item", WeightKg = 1m };
            db.Items.Add(item);
            item26Id = item.Id;
            db.StockOnHand.Add(new StockOnHand { ClientId = clientA, FacilityId = facOrd, ItemId = item26Id, LocationId = src26Id, QtyAvailable = 10m });
            await db.SaveChangesAsync();
        }

        using var barrier26 = new Barrier(2);
        var tZone26 = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            barrier26.SignalAndWait();
            await new UpdateWarehouseZoneCommandHandler(db1, new WarehouseMasterAuthorization(db1, adminCtx)).Handle(new UpdateWarehouseZoneCommand
            {
                Id = z26Id,
                Name = "PG26 Race Zone",
                ZoneType = WarehouseZoneType.BulkStorage,
                DedicatedClientId = clientB
            }, CancellationToken.None);
        });
        var tTransfer26 = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            barrier26.SignalAndWait();
            await new TransferInventoryCommandHandler(db2, adminCtx, new WarehouseOperationalAuthorization(db2, adminCtx)).Handle(new TransferInventoryCommand
            {
                ClientId = clientA,
                FacilityId = facOrd,
                ItemId = item26Id,
                FromLocationId = src26Id,
                ToLocationId = dst26Id,
                Quantity = 5m
            }, CancellationToken.None);
        });

        bool z26Ok = false, tr26Ok = false;
        try { await tZone26; z26Ok = true; } catch (DomainException) { }
        try { await tTransfer26; tr26Ok = true; } catch (DomainException) { }
        if (z26Ok && tr26Ok)
            throw new InvalidOperationException("PG-26 F03-A violation: Both zone dedication (Client B) and Client-A transfer into zone succeeded concurrently!");

        await using (var check26 = CreatePgDb(connStr, adminCtx))
        {
            var z = await check26.WarehouseZones.AsNoTracking().FirstAsync(x => x.Id == z26Id);
            var clientAStock = await check26.StockOnHand.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.LocationId == dst26Id && s.ClientId == clientA)
                .SumAsync(s => s.QtyAvailable + s.QtyAllocated + s.QtyReserved + s.QtyQuarantined + s.QtyDamaged + s.QtyOnHold);
            if (z.DedicatedClientId == clientB && clientAStock > 0m)
                throw new InvalidOperationException("PG-26 F03-A invariant broken: Client-A stock persisted inside Client-B dedicated zone!");
        }

        // PG-27: Concurrent Zone dedication (Client B) vs LocationStorageProfile dedication (Client A)
        Guid z27Id, bin27Id;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            var auth = new WarehouseMasterAuthorization(db, adminCtx);
            z27Id = (await new CreateWarehouseZoneCommandHandler(db, auth).Handle(new CreateWarehouseZoneCommand { FacilityId = facOrd, ZoneCode = "ZN-PG27", Name = "PG27 Race Zone" }, CancellationToken.None)).Id;
            bin27Id = (await new CreateLocationCommandHandler(db, auth).Handle(new CreateLocationCommand { FacilityId = facOrd, ZoneId = z27Id, LocationType = LocationType.Bin, LocationCode = "PG27-BIN", Barcode = "BC-PG27-BIN" }, CancellationToken.None)).Id;
        }

        using var barrier27 = new Barrier(2);
        var tZone27 = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            barrier27.SignalAndWait();
            await new UpdateWarehouseZoneCommandHandler(db1, new WarehouseMasterAuthorization(db1, adminCtx)).Handle(new UpdateWarehouseZoneCommand
            {
                Id = z27Id,
                Name = "PG27 Race Zone",
                ZoneType = WarehouseZoneType.BulkStorage,
                DedicatedClientId = clientB
            }, CancellationToken.None);
        });
        var tProf27 = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            barrier27.SignalAndWait();
            await new UpsertLocationStorageProfileCommandHandler(db2, new WarehouseMasterAuthorization(db2, adminCtx)).Handle(new UpsertLocationStorageProfileCommand
            {
                LocationId = bin27Id,
                DedicatedClientId = clientA
            }, CancellationToken.None);
        });

        bool z27Ok = false, p27Ok = false;
        try { await tZone27; z27Ok = true; } catch (DomainException) { }
        try { await tProf27; p27Ok = true; } catch (DomainException) { }
        if (z27Ok && p27Ok)
            throw new InvalidOperationException("PG-27 F03-A violation: Both zone dedication (Client B) and location-profile dedication (Client A) succeeded concurrently!");

        await using (var check27 = CreatePgDb(connStr, adminCtx))
        {
            var z = await check27.WarehouseZones.AsNoTracking().FirstAsync(x => x.Id == z27Id);
            var p = await check27.LocationStorageProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bin27Id);
            if (z.DedicatedClientId.HasValue && p?.DedicatedClientId.HasValue == true && z.DedicatedClientId != p.DedicatedClientId)
                throw new InvalidOperationException("PG-27 F03-A invariant broken: Conflicting zone & location-profile client ownership persisted!");
        }

        // PG-28: Concurrent actual PackingExecutionService.StartOrResumeSessionAsync vs Station deactivation
        Guid stn28Id, order28Id;
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            var auth = new WarehouseMasterAuthorization(db, adminCtx);
            stn28Id = (await new CreateLocationCommandHandler(db, auth).Handle(new CreateLocationCommand
            {
                FacilityId = facOrd,
                LocationType = LocationType.PackingStation,
                LocationCode = "PG28-PKSTN",
                Barcode = "BC-PG28-PKSTN"
            }, CancellationToken.None)).Id;
            var ord = new OutboundOrder { ClientId = clientA, FacilityId = facOrd, OrderNumber = "ORD-PG28", Status = OutboundOrderStatus.Allocated };
            db.OutboundOrders.Add(ord);
            order28Id = ord.Id;
            await db.SaveChangesAsync();
        }

        using var barrier28 = new Barrier(2);
        var tPack28 = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            barrier28.SignalAndWait();
            await new PackingExecutionService(db1, NullLogger<PackingExecutionService>.Instance).StartOrResumeSessionAsync(new StartPackingSessionRequest
            {
                FacilityId = facOrd,
                PackingStationLocationId = stn28Id,
                OutboundOrderId = order28Id,
                OperatorUserId = adminCtx.UserId!.Value
            });
        });
        var tDeact28 = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            barrier28.SignalAndWait();
            await new UpdateLocationStatusCommandHandler(db2, new WarehouseMasterAuthorization(db2, adminCtx))
                .Handle(new UpdateLocationStatusCommand(stn28Id, LocationStatus.Inactive), CancellationToken.None);
        });

        bool pack28Ok = false, deact28Ok = false;
        try { await tPack28; pack28Ok = true; } catch (DomainException) { }
        try { await tDeact28; deact28Ok = true; } catch (DomainException) { }
        if (pack28Ok && deact28Ok)
            throw new InvalidOperationException("PG-28 F03-B violation: Both PackingExecutionService.StartOrResumeSessionAsync and station deactivation succeeded concurrently!");

        await using (var check28 = CreatePgDb(connStr, adminCtx))
        {
            var stn = await check28.Locations.AsNoTracking().FirstAsync(l => l.Id == stn28Id);
            var openSessions = await check28.PackingSessions.AsNoTracking()
                .CountAsync(ps => ps.PackingStationLocationId == stn28Id && ps.Status != PackingSessionStatus.Completed && ps.Status != PackingSessionStatus.Aborted);
            if (stn.Status != LocationStatus.Active && openSessions > 0)
                throw new InvalidOperationException("PG-28 F03-B invariant broken: Open packing session persisted at inactive packing station!");
        }

        // PG-29: Concurrent Facility deactivation vs CreateLocationCommand (protected warehouse mutation)
        var fac29Id = Guid.NewGuid();
        await using (var db = CreatePgDb(connStr, adminCtx))
        {
            db.Facilities.Add(new Facility
            {
                Id = fac29Id,
                FacilityCode = "FAC-PG29",
                Name = "PG29 Facility Race Hub",
                CountryCode = "US",
                Timezone = "America/Chicago",
                DefaultCurrencyCode = "USD",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        using var barrier29 = new Barrier(2);
        var tDeactFac29 = Task.Run(async () =>
        {
            await using var db1 = CreatePgDb(connStr, adminCtx);
            barrier29.SignalAndWait();
            await new UpdateFacilityCommandHandler(db1, new CompliancePlatformAuthorization(db1, adminCtx), new TestTzResolver()).Handle(
                new UpdateFacilityCommand(fac29Id, null, "PG29 Facility Race Hub", null, null, null, null, null, "US", "America/Chicago", null, "USD", null, null, null, 0, IsActive: false),
                CancellationToken.None);
        });
        var tCreateLoc29 = Task.Run(async () =>
        {
            await using var db2 = CreatePgDb(connStr, adminCtx);
            barrier29.SignalAndWait();
            await new CreateLocationCommandHandler(db2, new WarehouseMasterAuthorization(db2, adminCtx)).Handle(new CreateLocationCommand
            {
                FacilityId = fac29Id,
                LocationType = LocationType.Bin,
                LocationCode = "PG29-BIN",
                Barcode = "BC-PG29-BIN"
            }, CancellationToken.None);
        });

        bool deact29Ok = false, create29Ok = false;
        try { await tDeactFac29; deact29Ok = true; } catch (DomainException) { }
        try { await tCreateLoc29; create29Ok = true; } catch (DomainException) { }
        if (deact29Ok && create29Ok)
            throw new InvalidOperationException("PG-29 F03-C violation: Both facility deactivation and CreateLocationCommand succeeded concurrently!");

        await using (var check29 = CreatePgDb(connStr, adminCtx))
        {
            var fac = await check29.Facilities.AsNoTracking().FirstAsync(f => f.Id == fac29Id);
            var activeLocs = await check29.Locations.AsNoTracking().CountAsync(l => l.FacilityId == fac29Id && !l.IsDeleted && l.Status == LocationStatus.Active);
            if (!fac.IsActive && activeLocs > 0)
                throw new InvalidOperationException("PG-29 F03-C invariant broken: Active location persisted inside inactive facility!");
        }

        Console.WriteLine("  [PASS] PG-26, PG-27, PG-28 & PG-29 (F03): Concurrent zone dedication vs transfer/profile, PackingExecutionService vs station deactivation, and facility deactivation vs location creation verified on live PostgreSQL.");
    }

    private static async Task AssertPgErrorAsync(NpgsqlConnection conn, string sql, string expectedSqlState, string label)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == expectedSqlState)
        {
            return;
        }
        throw new InvalidOperationException($"Expected PostgreSQL SQLSTATE {expectedSqlState} for {label}, but statement succeeded or threw unexpected error.");
    }

    private static async Task AssertPgMessageAsync(NpgsqlConnection conn, string sql, string expectedFragment, string label)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException pgEx) when (pgEx.MessageText.Contains(expectedFragment, StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException($"Expected PostgreSQL error containing '{expectedFragment}' for {label}, but statement succeeded or threw unexpected error.");
    }

    private static ApplicationDbContext CreatePgDb(string connStr, Mdm21Identity? identity = null)
    {
        var id = identity ?? new Mdm21Identity(Guid.Empty, null);
        var audit = new AuditableEntityInterceptor(id, new DateTimeProvider());
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connStr, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(audit)
            .Options;
        return new ApplicationDbContext(opts, id, id, audit);
    }

    private static async Task CreateDbAsync(string adminConnStr, string dbName)
    {
        await using var conn = new NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropDbAsync(string adminConnStr, string dbName)
    {
        await using var conn = new NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();
        await using var term = new NpgsqlCommand(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}' AND pid <> pg_backend_pid();", conn);
        await term.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\"", conn);
        await drop.ExecuteNonQueryAsync();
    }

    private sealed class Mdm21Identity(Guid userId, Guid? facilityId) : ITenantProvider, ICurrentUserContext
    {
        public Guid? TenantId { get; private set; }
        public bool HasTenant => TenantId.HasValue;
        public bool BypassTenantFilter { get; set; } = true;
        public void SetTenantId(Guid tenantId) => TenantId = tenantId;
        public Guid? UserId => userId == Guid.Empty ? null : userId;
        public string? Email => "mdm21-pg@nexus3pl.test";
        public Guid? FacilityId => facilityId;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles { get; } = [SystemRoles.SuperAdmin];
        public IReadOnlyList<string> Permissions { get; } = Warehouse3PL.Domain.Constants.Permissions.GetAllPermissions();
        public bool IsAuthenticated => userId != Guid.Empty;
        public bool IsStaff => true;
        public bool IsSuperAdmin => true;
        public bool HasPermission(string permission) => true;
        public bool IsInRole(string role) => string.Equals(role, SystemRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);
    }
}
