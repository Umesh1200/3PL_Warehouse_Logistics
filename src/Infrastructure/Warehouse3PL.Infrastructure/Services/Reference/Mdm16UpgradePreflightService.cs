using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Warehouse3PL.Infrastructure.Persistence;

namespace Warehouse3PL.Infrastructure.Services.Reference;

/// <summary>
/// Enforces the safe upgrade boundary before <c>20261127051434_MDM1_6_R04_NormalizationRemediationLifecycle</c>
/// executes on databases where <c>20261126143014_MDM1_6_LegacyCountryNormalization</c> was already applied.
/// Captures a protected pre-R04 snapshot of <c>shipping_addresses</c> and strict package immutability timestamps
/// so legitimate post-MDM-1.6 amendments and Open-to-Packed transitions cannot be lost when R04 executes,
/// while preserving all existing migration files unedited.
/// </summary>
public static class Mdm16UpgradePreflightService
{
    public const string Mdm16InitialMigrationId = "20261126143014_MDM1_6_LegacyCountryNormalization";
    public const string Mdm16R04MigrationId = "20261127051434_MDM1_6_R04_NormalizationRemediationLifecycle";
    public const string Mdm16F03R01MigrationId = "20261127103000_MDM1_6_F03R01_F04R01_SafeUpgradeAndStructuredReasons";

    /// <summary>
    /// Captures the protected pre-R04 shipping address snapshot if <see cref="Mdm16InitialMigrationId"/>
    /// has already been applied and <see cref="Mdm16R04MigrationId"/> is still pending.
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

        // Capture durable protected snapshot BEFORE R04 executes so legitimate amendments (e.g. USA -> US -> NP + Packed)
        // and Open->Packed transitions after MDM-1.6 are preserved deterministically by F03-R01.
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
            WHERE sa.is_deleted = false AND o.is_deleted = false
            ON CONFLICT (shipping_address_id) DO NOTHING;
            """,
            cancellationToken);

        return true;
    }

    /// <summary>
    /// Executes the supported MDM-1.6 upgrade path: captures the pre-R04 protected snapshot when needed,
    /// then migrates forward through R04, F03, and F03-R01/F04-R01 to the latest schema.
    /// </summary>
    public static async Task ExecuteSafeUpgradeAsync(
        ApplicationDbContext db,
        string? targetMigration = null,
        CancellationToken cancellationToken = default)
    {
        await CaptureProtectedPreR04SnapshotAsync(db, cancellationToken);
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync(targetMigration, cancellationToken);
    }
}
