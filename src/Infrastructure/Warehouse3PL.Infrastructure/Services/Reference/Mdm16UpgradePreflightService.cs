using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Seed;

namespace Warehouse3PL.Infrastructure.Services.Reference;

/// <summary>
/// Enforces the safe upgrade boundary before <c>20261127051434_MDM1_6_R04_NormalizationRemediationLifecycle</c>
/// executes on databases where <c>20261126143014_MDM1_6_LegacyCountryNormalization</c> was already applied.
/// Captures a fresh, maintenance-locked protected pre-R04 snapshot of <c>shipping_addresses</c> and strict package
/// immutability timestamps so legitimate post-MDM-1.6 amendments and Open-to-Packed transitions cannot be lost
/// when R04 executes, while preventing stale snapshots across interrupted/retried runs and preserving all
/// existing migration files unedited.
/// </summary>
public static class Mdm16UpgradePreflightService
{
    public const string Mdm16InitialMigrationId = "20261126143014_MDM1_6_LegacyCountryNormalization";
    public const string Mdm16R04MigrationId = "20261127051434_MDM1_6_R04_NormalizationRemediationLifecycle";
    public const string Mdm16F03R01MigrationId = "20261127103000_MDM1_6_F03R01_F04R01_SafeUpgradeAndStructuredReasons";

    private static readonly AsyncLocal<bool> AuthorizedSafeUpgradeScope = new();

    internal static bool IsAuthorizedSafeUpgradeActive => AuthorizedSafeUpgradeScope.Value;

    /// <summary>
    /// Captures a fresh protected pre-R04 shipping address snapshot if <see cref="Mdm16InitialMigrationId"/>
    /// has already been applied and <see cref="Mdm16R04MigrationId"/> is still pending.
    /// Any stale snapshot left by a previous interrupted pre-R04 attempt is replaced atomically.
    /// If <see cref="Mdm16R04MigrationId"/> is already applied, returns <c>false</c> without modifying
    /// any existing pre-R04 snapshot so post-R04 retries preserve authoritative pre-R04 evidence.
    /// </summary>
    public static async Task<bool> CaptureProtectedPreR04SnapshotAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
        {
            return false;
        }

        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        if (!applied.Contains(Mdm16InitialMigrationId) || applied.Contains(Mdm16R04MigrationId))
        {
            return false;
        }

        // Fail-closed catalog preflight on populated databases before R04 runs
        await db.Database.ExecuteSqlRawAsync(
            """
            DO $$
            DECLARE
                v_total_rows BIGINT;
                v_has_profiles BOOLEAN;
                v_has_divisions BOOLEAN;
            BEGIN
                SELECT (SELECT COUNT(*) FROM facilities)
                     + (SELECT COUNT(*) FROM customers)
                     + (SELECT COUNT(*) FROM suppliers)
                     + (SELECT COUNT(*) FROM shipping_addresses)
                     + (SELECT COUNT(*) FROM return_shipments)
                     + (SELECT COUNT(*) FROM carrier_contractual_sla_policies)
                     + (SELECT COUNT(*) FROM carrier_sla_compliance_records)
                INTO v_total_rows;

                SELECT EXISTS (SELECT 1 FROM country_profiles WHERE is_active = true) INTO v_has_profiles;
                SELECT EXISTS (SELECT 1 FROM administrative_divisions WHERE is_active = true) INTO v_has_divisions;

                IF v_total_rows > 0 AND (NOT v_has_profiles OR NOT v_has_divisions) THEN
                    RAISE EXCEPTION 'MDM-1.6 safe upgrade preflight blocked: populated database has % operational records without active CountryProfile/AdministrativeDivision reference catalogs.', v_total_rows;
                END IF;
            END $$;
            """,
            cancellationToken);

        // Capture a fresh protected snapshot BEFORE R04 executes.
        // Because R04 is verified NOT applied above, DELETE + INSERT inside a single statement block
        // guarantees any stale snapshot from an earlier interrupted pre-R04 run is atomically replaced
        // with the current live state of shipping_addresses.
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS mdm16_r04_preflight_shipping_snapshots (
                shipping_address_id UUID PRIMARY KEY,
                order_id UUID NOT NULL,
                pre_r04_country_code VARCHAR(100) NOT NULL,
                pre_r04_normalization_status VARCHAR(30) NOT NULL,
                is_amended BOOLEAN NOT NULL,
                amended_at_utc TIMESTAMPTZ NULL,
                order_status VARCHAR(50) NOT NULL,
                order_updated_at TIMESTAMPTZ NULL,
                audit_original_country_value VARCHAR(120) NULL,
                audit_normalized_country_code VARCHAR(3) NULL,
                audit_correlation_batch_id VARCHAR(64) NOT NULL,
                audit_recorded_at_utc TIMESTAMPTZ NOT NULL,
                has_pre_mig_packed_proof BOOLEAN NOT NULL,
                captured_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            DELETE FROM mdm16_r04_preflight_shipping_snapshots;

            INSERT INTO mdm16_r04_preflight_shipping_snapshots (
                shipping_address_id,
                order_id,
                pre_r04_country_code,
                pre_r04_normalization_status,
                is_amended,
                amended_at_utc,
                order_status,
                order_updated_at,
                audit_original_country_value,
                audit_normalized_country_code,
                audit_correlation_batch_id,
                audit_recorded_at_utc,
                has_pre_mig_packed_proof,
                captured_at_utc
            )
            SELECT
                sa.shipping_address_id,
                sa.order_id,
                sa.country_code,
                sa.country_normalization_status,
                sa.is_amended,
                sa.amended_at_utc,
                o.status,
                o.updated_at,
                cna.original_country_value,
                cna.normalized_country_code,
                cna.correlation_batch_id,
                cna.recorded_at_utc,
                EXISTS (
                    SELECT 1 FROM packages p
                    WHERE p.outbound_order_id = o.order_id
                      AND p.is_deleted = false
                      AND (
                          (p.status <> 'Open'
                           AND (
                               (p.packed_at_utc IS NOT NULL AND p.packed_at_utc <= cna.recorded_at_utc)
                               OR (p.packed_at_utc IS NULL AND p.updated_at IS NULL AND p.created_at <= cna.recorded_at_utc)
                           ))
                          OR (p.shipment_id IS NOT NULL AND EXISTS (
                              SELECT 1 FROM shipments s WHERE s.shipment_id = p.shipment_id AND s.is_deleted = false AND s.created_at <= cna.recorded_at_utc
                          ))
                          OR (p.shipment_id IS NOT NULL AND EXISTS (
                              SELECT 1 FROM bills_of_lading bol WHERE bol.shipment_id = p.shipment_id AND bol.is_deleted = false AND bol.created_at <= cna.recorded_at_utc
                          ))
                      )
                ) AS has_pre_mig_packed_proof,
                NOW()
            FROM shipping_addresses sa
            INNER JOIN outbound_orders o ON o.order_id = sa.order_id
            INNER JOIN country_normalization_audits cna
                ON cna.entity_type = 'ShippingAddress'
               AND cna.entity_id = sa.shipping_address_id
               AND cna.field_name = 'CountryCode'
            WHERE sa.is_deleted = false AND o.is_deleted = false;
            """,
            cancellationToken);

        return true;
    }

    /// <summary>
    /// Executes the supported database migration workflow under a PostgreSQL session-level maintenance lock:
    /// optionally seeds missing reference catalogs, captures/refreshes the protected pre-R04 snapshot
    /// immediately before migration, runs EF Core migrations cleanly, and cleans up any pre-R04 snapshot
    /// if the upgrade is interrupted before R04 commits.
    /// </summary>
    public static async Task ExecuteSafeUpgradeAsync(
        ApplicationDbContext db,
        string? targetMigration = null,
        bool seedCatalogsIfMissing = false,
        CancellationToken cancellationToken = default)
    {
        bool isNpgsql = db.Database.IsRelational() &&
                        (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false);
        bool openedConnection = false;
        bool lockAcquired = false;

        if (isNpgsql)
        {
            if (db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            {
                await db.Database.OpenConnectionAsync(cancellationToken);
                openedConnection = true;
            }

            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_lock(hashtextextended('MDM16_SAFE_UPGRADE_ENTRY', 0::bigint));",
                cancellationToken);
            lockAcquired = true;
        }

        try
        {
            if (seedCatalogsIfMissing && db.Database.IsRelational())
            {
                var appliedBefore = (await db.Database.GetAppliedMigrationsAsync(cancellationToken))
                    .ToHashSet(StringComparer.Ordinal);
                if (appliedBefore.Count > 0)
                {
                    var hasProfilesTable = await HasTableAsync(db, "country_profiles", cancellationToken);
                    if (hasProfilesTable && !await db.CountryProfiles.AnyAsync(c => c.IsActive, cancellationToken))
                    {
                        await ReferenceCatalogSeeder.SeedAsync(db, cancellationToken);
                    }
                }
            }

            await CaptureProtectedPreR04SnapshotAsync(db, cancellationToken);

            var prevScope = AuthorizedSafeUpgradeScope.Value;
            AuthorizedSafeUpgradeScope.Value = true;
            try
            {
                var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(targetMigration, cancellationToken);
            }
            catch
            {
                // If interrupted BEFORE R04 committed, drop the preflight snapshot so no stale snapshot lingers.
                if (isNpgsql)
                {
                    try
                    {
                        var appliedAfterFail = (await db.Database.GetAppliedMigrationsAsync(CancellationToken.None))
                            .ToHashSet(StringComparer.Ordinal);
                        if (!appliedAfterFail.Contains(Mdm16R04MigrationId))
                        {
                            await db.Database.ExecuteSqlRawAsync(
                                "DROP TABLE IF EXISTS mdm16_r04_preflight_shipping_snapshots;",
                                CancellationToken.None);
                        }
                    }
                    catch
                    {
                        // Best-effort cleanup; next retry will also DELETE+INSERT fresh if R04 is still pending.
                    }
                }

                throw;
            }
            finally
            {
                AuthorizedSafeUpgradeScope.Value = prevScope;
            }
        }
        finally
        {
            if (lockAcquired)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_unlock(hashtextextended('MDM16_SAFE_UPGRADE_ENTRY', 0::bigint));",
                        CancellationToken.None);
                }
                catch
                {
                    // Connection close also releases session-level advisory locks.
                }
            }

            if (openedConnection)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary>
    /// Verification-only helper used strictly to simulate historical databases where
    /// <see cref="Mdm16R04MigrationId"/> was already applied before the preflight guard existed.
    /// </summary>
    public static async Task ExecuteLegacyUnsafeR04ForDamagedDbSimulationAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken = default)
    {
        var prevScope = AuthorizedSafeUpgradeScope.Value;
        AuthorizedSafeUpgradeScope.Value = true;
        try
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(Mdm16R04MigrationId, cancellationToken);
        }
        finally
        {
            AuthorizedSafeUpgradeScope.Value = prevScope;
        }
    }

    private static async Task<bool> HasTableAsync(
        ApplicationDbContext db,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SELECT to_regclass('{tableName}')::text;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is string s && !string.IsNullOrEmpty(s);
    }
}
