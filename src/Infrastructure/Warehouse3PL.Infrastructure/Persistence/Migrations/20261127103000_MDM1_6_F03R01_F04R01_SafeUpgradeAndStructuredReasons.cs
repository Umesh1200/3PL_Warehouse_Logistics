using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_6_F03R01_F04R01_SafeUpgradeAndStructuredReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION pg_temp.mdm16_f04r01_norm_key(raw_val TEXT)
                RETURNS TEXT
                LANGUAGE plpgsql
                IMMUTABLE
                AS $$
                DECLARE
                    v TEXT;
                BEGIN
                    IF raw_val IS NULL THEN RETURN NULL; END IF;
                    v := UPPER(REGEXP_REPLACE(BTRIM(raw_val), '\s+', ' ', 'g'));
                    IF v = '' THEN RETURN NULL; END IF;
                    IF v ~ '^([A-Z]\.){2,4}[A-Z]?$' THEN
                        v := REPLACE(v, '.', '');
                    END IF;
                    RETURN v;
                END $$;

                -- F04-R01: Strict fail-closed allowlisted catalog token sanitizer
                CREATE OR REPLACE FUNCTION pg_temp.mdm16_f04r01_sanitize_allowlisted(raw_val TEXT)
                RETURNS VARCHAR(120)
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    v TEXT;
                    v_key TEXT;
                    v_is_verified BOOLEAN := false;
                BEGIN
                    IF raw_val IS NULL THEN RETURN NULL; END IF;
                    v := REGEXP_REPLACE(REPLACE(REPLACE(BTRIM(raw_val), E'\r', ' '), E'\n', ' '), '\s+', ' ', 'g');
                    IF v = '' THEN RETURN ''; END IF;
                    IF v = '[REDACTED_POTENTIAL_PII]' THEN RETURN '[REDACTED_POTENTIAL_PII]'; END IF;

                    IF LENGTH(v) > 64
                       OR POSITION('@' IN v) > 0
                       OR v ~* 'https?://|mailto:'
                       OR v ~ '[\x00-\x1F\x7F]'
                       OR v ~ '(?:\D*\d){5,}'
                       OR v ~ '\+?\d[\d\s\-\(\)]{5,}\d'
                       OR v ~* '\m\d+\s+[A-Za-z].*\m(st|street|ave|avenue|rd|road|blvd|boulevard|dr|drive|ln|lane|apt|suite|box)\M' THEN
                        RETURN '[REDACTED_POTENTIAL_PII]';
                    END IF;

                    v_key := pg_temp.mdm16_f04r01_norm_key(v);
                    IF v_key IS NULL THEN RETURN ''; END IF;

                    SELECT EXISTS (
                        SELECT 1 FROM country_profiles cp
                        WHERE cp.is_active = true
                          AND (UPPER(cp.country_code) = v_key
                               OR UPPER(cp.iso_alpha3_code) = v_key
                               OR pg_temp.mdm16_f04r01_norm_key(cp.name) = v_key)
                    ) OR EXISTS (
                        SELECT 1 FROM country_normalization_aliases a
                        INNER JOIN country_profiles cp ON cp.country_code = a.country_code AND cp.is_active = true
                        WHERE a.is_active = true AND a.alias_key = v_key
                    ) INTO v_is_verified;

                    IF NOT v_is_verified THEN
                        RETURN '[REDACTED_POTENTIAL_PII]';
                    END IF;

                    RETURN LEFT(v, 120)::VARCHAR(120);
                END $$;

                -- F04-R01: Closed structured reason mapping (NEVER returns arbitrary suffix text or interpolated input)
                CREATE OR REPLACE FUNCTION pg_temp.mdm16_f04r01_sanitize_reason(reason_val TEXT)
                RETURNS VARCHAR(500)
                LANGUAGE plpgsql
                IMMUTABLE
                AS $$
                DECLARE
                    v TEXT;
                BEGIN
                    IF reason_val IS NULL OR BTRIM(reason_val) = '' THEN
                        RETURN 'Country normalization evaluation pending.';
                    END IF;
                    v := REGEXP_REPLACE(REPLACE(REPLACE(BTRIM(reason_val), E'\r', ' '), E'\n', ' '), '\s+', ' ', 'g');

                    IF v ILIKE 'HISTORICAL_BLANK_INFERENCE_PRESERVED' OR v ILIKE 'Historical blank country inference preserved%' THEN
                        RETURN 'Historical blank country inference preserved without unverified restoration.';
                    ELSIF v ILIKE 'HISTORICAL_RESTORED' OR v ILIKE 'Historical country value restored%' THEN
                        RETURN 'Historical country value restored after verifying pre-remediation provenance.';
                    ELSIF v ILIKE 'HISTORICAL_PROVENANCE_REVIEW'
                       OR v ILIKE 'Ambiguous historical restoration provenance%'
                       OR v ILIKE 'Historical provenance requires steward review%' THEN
                        RETURN 'Historical provenance requires steward review; current value preserved.';
                    ELSIF v ILIKE 'HISTORICAL_PRESERVED' OR v ILIKE 'Immutable historical record preserved untouched%' THEN
                        RETURN 'Immutable historical record preserved untouched.';
                    ELSIF v ILIKE 'RUNTIME_NORMALIZED' OR v ILIKE 'Runtime write reconciled to canonical%' THEN
                        RETURN 'Runtime write reconciled to canonical ISO 3166-1 country code.';
                    ELSIF v ILIKE 'RUNTIME_PENDING'
                       OR v ILIKE 'Runtime write recorded pending normalization%'
                       OR v ILIKE 'Runtime write quarantined pending%' THEN
                        RETURN 'Runtime write quarantined pending country normalization review.';
                    ELSIF v ILIKE 'INFERRED_FROM_PARENT' OR v ILIKE 'Deterministically inferred%' THEN
                        RETURN 'Deterministically inferred canonical ISO 3166-1 country code from verified parent context.';
                    ELSIF v ILIKE 'NORMALIZED_CANONICAL' OR v ILIKE 'Normalized to canonical%' OR v ILIKE 'Resolved to %' THEN
                        RETURN 'Normalized to canonical ISO 3166-1 country code.';
                    ELSIF v ILIKE 'CONFLICTING_TIMEZONE_EVIDENCE' OR v ILIKE 'Conflicting timezone evidence%' THEN
                        RETURN 'Conflicting timezone evidence quarantined for steward review.';
                    ELSIF v ILIKE 'CONFLICTING_GEOGRAPHIC_EVIDENCE'
                       OR v ILIKE 'Conflicting geographic evidence%'
                       OR v ILIKE 'Conflicting evidence%' THEN
                        RETURN 'Conflicting geographic evidence quarantined for steward review.';
                    ELSIF v ILIKE 'CONFLICTING_POSTAL_SYNTAX' OR v ILIKE 'Unsupported or conflicting postal code syntax%' THEN
                        RETURN 'Unsupported or conflicting postal code syntax quarantined for steward review.';
                    ELSIF v ILIKE 'AMBIGUOUS_SUBDIVISION' OR v ILIKE 'Ambiguous subdivision%' THEN
                        RETURN 'Ambiguous subdivision requires manual country confirmation.';
                    ELSIF v ILIKE 'UNVERIFIED_SUBDIVISION' OR v ILIKE 'Unverified subdivision%' THEN
                        RETURN 'Unverified subdivision requires manual country confirmation.';
                    ELSIF v ILIKE 'QUARANTINED_CUSTOMER_PARENT' OR v ILIKE 'Quarantined or unverified customer master%' THEN
                        RETURN 'Quarantined or unverified customer master cannot verify child address country.';
                    ELSIF v ILIKE 'QUARANTINED_ORDER_PARENT' OR v ILIKE 'Quarantined original order shipping address%' THEN
                        RETURN 'Quarantined original order shipping address cannot verify return country.';
                    ELSIF v ILIKE 'CROSS_BORDER_RETURN' OR v ILIKE 'Cross-border return shipment%' THEN
                        RETURN 'Cross-border return shipment requires explicit country confirmation.';
                    ELSIF v ILIKE 'CROSS_BORDER_ORDER' OR v ILIKE 'Cross-border order context%' THEN
                        RETURN 'Cross-border order context requires explicit country confirmation.';
                    ELSIF v ILIKE 'BLANK_COUNTRY_UNVERIFIED'
                       OR v ILIKE 'Blank/null country%'
                       OR v ILIKE 'Blank or null country%'
                       OR v ILIKE 'Blank or missing country%'
                       OR v ILIKE 'Orphan or unverified blank country%' THEN
                        RETURN 'Blank or missing country value could not be deterministically verified.';
                    ELSIF v ILIKE 'EVALUATION_PENDING' OR v ILIKE 'Country normalization evaluation pending%' THEN
                        RETURN 'Country normalization evaluation pending.';
                    ELSE
                        RETURN 'Unrecognized or unverified country value quarantined for steward review.';
                    END IF;
                END $$;

                DO $$
                DECLARE
                    v_batch CONSTANT VARCHAR(64) := '20261127103000_MDM1_6_F03R01_F04R01';
                    v_has_profiles BOOLEAN;
                    v_has_preflight_snapshot BOOLEAN;
                    r RECORD;
                    v_is_valid_canonical BOOLEAN;
                    v_has_pre_mig_packed_proof BOOLEAN;
                    v_has_post_mig_packed_proof BOOLEAN;
                BEGIN
                    -- 1. F04-R01: Sanitize all persisted audit reasons and original values using strict closed registries
                    UPDATE country_normalization_audits
                    SET original_country_value = pg_temp.mdm16_f04r01_sanitize_allowlisted(original_country_value),
                        reason = pg_temp.mdm16_f04r01_sanitize_reason(reason)
                    WHERE original_country_value IS DISTINCT FROM pg_temp.mdm16_f04r01_sanitize_allowlisted(original_country_value)
                       OR reason IS DISTINCT FROM pg_temp.mdm16_f04r01_sanitize_reason(reason);

                    SELECT EXISTS (SELECT 1 FROM country_profiles WHERE is_active = true) INTO v_has_profiles;
                    IF NOT v_has_profiles THEN
                        DROP TABLE IF EXISTS mdm16_r04_preflight_shipping_snapshots;
                        RETURN;
                    END IF;

                    v_has_preflight_snapshot := (to_regclass('mdm16_r04_preflight_shipping_snapshots') IS NOT NULL);

                    IF v_has_preflight_snapshot THEN
                        -- Branch A: Safe staged upgrade via Mdm16UpgradePreflightService (protected pre-R04 snapshot available)
                        FOR r IN
                            SELECT snap.shipping_address_id, snap.order_id, snap.pre_r04_country_code,
                                   snap.pre_r04_normalization_status, snap.is_amended, snap.amended_at_utc,
                                   snap.audit_original_country_value, snap.audit_normalized_country_code,
                                   snap.audit_correlation_batch_id, snap.audit_recorded_at_utc,
                                   snap.has_pre_mig_packed_proof,
                                   sa.country_code AS current_country_code,
                                   cna.audit_id
                            FROM mdm16_r04_preflight_shipping_snapshots snap
                            INNER JOIN shipping_addresses sa ON sa.shipping_address_id = snap.shipping_address_id AND sa.is_deleted = false
                            INNER JOIN country_normalization_audits cna
                                ON cna.entity_type = 'ShippingAddress'
                               AND cna.entity_id = snap.shipping_address_id
                               AND cna.field_name = 'CountryCode'
                        LOOP
                            -- Case A1: Legitimate post-MDM-1.6 amendment (even if subsequently packed/fulfilled) -> preserve pre-R04 amended value!
                            IF r.is_amended = true
                               OR r.amended_at_utc IS NOT NULL
                               OR (r.pre_r04_country_code IS DISTINCT FROM r.audit_normalized_country_code
                                   AND r.pre_r04_country_code IS DISTINCT FROM r.audit_original_country_value) THEN
                                SELECT (r.pre_r04_country_code ~ '^[A-Z]{2}$'
                                        AND EXISTS (SELECT 1 FROM country_profiles cp WHERE cp.is_active = true AND cp.country_code = r.pre_r04_country_code))
                                INTO v_is_valid_canonical;

                                UPDATE shipping_addresses
                                SET country_code = r.pre_r04_country_code,
                                    country_normalization_status = CASE WHEN v_is_valid_canonical THEN 'Normalized' ELSE 'PendingNormalization' END
                                WHERE shipping_address_id = r.shipping_address_id;

                                UPDATE country_normalization_audits
                                SET normalized_country_code = CASE WHEN v_is_valid_canonical THEN r.pre_r04_country_code::VARCHAR(3) ELSE NULL END,
                                    normalization_status = CASE WHEN v_is_valid_canonical THEN 'Normalized' ELSE 'PendingNormalization' END,
                                    preserved_historical_value = false,
                                    reason = CASE WHEN v_is_valid_canonical
                                                  THEN 'Normalized to canonical ISO 3166-1 country code.'
                                                  ELSE 'Historical provenance requires steward review; current value preserved.' END,
                                    correlation_batch_id = v_batch,
                                    recorded_at_utc = NOW()
                                WHERE audit_id = r.audit_id;
                                CONTINUE;
                            END IF;

                            -- Case A2: Unamended order whose package was NOT proven packed before MDM-1.6 (e.g. Open->Packed after MDM-1.6)
                            IF NOT r.has_pre_mig_packed_proof
                               AND r.pre_r04_country_code = r.audit_normalized_country_code
                               AND r.current_country_code IS DISTINCT FROM r.pre_r04_country_code THEN
                                UPDATE shipping_addresses
                                SET country_code = r.pre_r04_country_code,
                                    country_normalization_status = 'Normalized'
                                WHERE shipping_address_id = r.shipping_address_id;

                                UPDATE country_normalization_audits
                                SET preserved_historical_value = false,
                                    reason = 'Historical provenance requires steward review; current value preserved.',
                                    correlation_batch_id = v_batch,
                                    recorded_at_utc = NOW()
                                WHERE audit_id = r.audit_id;
                                CONTINUE;
                            END IF;
                        END LOOP;

                        DROP TABLE IF EXISTS mdm16_r04_preflight_shipping_snapshots;
                    ELSE
                        -- Branch B: Earlier R04 migration already executed without preflight snapshot
                        FOR r IN
                            SELECT sa.shipping_address_id, sa.order_id, sa.country_code, sa.is_amended, sa.amended_at_utc,
                                   o.status AS order_status,
                                   cna.audit_id, cna.original_country_value, cna.normalized_country_code,
                                   cna.preserved_historical_value, cna.correlation_batch_id,
                                   cna.initial_correlation_batch_id, cna.initial_recorded_at_utc
                            FROM shipping_addresses sa
                            INNER JOIN outbound_orders o ON o.order_id = sa.order_id
                            INNER JOIN country_normalization_audits cna
                                ON cna.entity_type = 'ShippingAddress'
                               AND cna.entity_id = sa.shipping_address_id
                               AND cna.field_name = 'CountryCode'
                            WHERE sa.is_deleted = false AND o.is_deleted = false
                        LOOP
                            -- Strict pre-migration packed proof: Package.CreatedAt alone is NEVER sufficient
                            SELECT EXISTS (
                                SELECT 1 FROM packages p
                                WHERE p.outbound_order_id = r.order_id
                                  AND p.is_deleted = false
                                  AND (
                                      (p.status <> 'Open'
                                       AND (
                                           (p.packed_at_utc IS NOT NULL AND p.packed_at_utc <= r.initial_recorded_at_utc)
                                           OR (p.packed_at_utc IS NULL AND p.updated_at IS NULL AND p.created_at <= r.initial_recorded_at_utc)
                                       ))
                                      OR (p.shipment_id IS NOT NULL AND EXISTS (
                                          SELECT 1 FROM shipments s WHERE s.shipment_id = p.shipment_id AND s.is_deleted = false AND s.created_at <= r.initial_recorded_at_utc
                                      ))
                                      OR (p.shipment_id IS NOT NULL AND EXISTS (
                                          SELECT 1 FROM bills_of_lading bol WHERE bol.shipment_id = p.shipment_id AND bol.is_deleted = false AND bol.created_at <= r.initial_recorded_at_utc
                                      ))
                                  )
                            ) INTO v_has_pre_mig_packed_proof;

                            SELECT EXISTS (
                                SELECT 1 FROM packages p
                                WHERE p.outbound_order_id = r.order_id
                                  AND p.is_deleted = false
                                  AND p.status <> 'Open'
                                  AND (
                                      (p.packed_at_utc IS NOT NULL AND p.packed_at_utc > r.initial_recorded_at_utc)
                                      OR (p.updated_at IS NOT NULL AND p.updated_at > r.initial_recorded_at_utc)
                                      OR p.created_at > r.initial_recorded_at_utc
                                  )
                            ) INTO v_has_post_mig_packed_proof;

                            -- Case B1: Previously damaged amended record where R04 already overwrote the amended value -> NEVER guess; flag steward review
                            IF (r.is_amended = true OR r.amended_at_utc IS NOT NULL)
                               AND r.initial_correlation_batch_id = '20261126143014_MDM1_6_LegacyCountryNormalization'
                               AND r.country_code = r.original_country_value
                               AND r.original_country_value IS DISTINCT FROM r.normalized_country_code THEN
                                UPDATE shipping_addresses
                                SET country_normalization_status = 'PendingNormalization'
                                WHERE shipping_address_id = r.shipping_address_id;

                                UPDATE country_normalization_audits
                                SET normalization_status = 'PendingNormalization',
                                    reason = 'Historical provenance requires steward review; current value preserved.',
                                    correlation_batch_id = v_batch,
                                    recorded_at_utc = NOW()
                                WHERE audit_id = r.audit_id;
                                CONTINUE;
                            END IF;

                            -- Case B2: Unamended record where package was created Open before MDM-1.6 and transitioned Packed after -> restore canonical normalized value
                            IF r.initial_correlation_batch_id = '20261126143014_MDM1_6_LegacyCountryNormalization'
                               AND NOT v_has_pre_mig_packed_proof
                               AND v_has_post_mig_packed_proof
                               AND r.is_amended = false
                               AND r.amended_at_utc IS NULL
                               AND r.normalized_country_code IS NOT NULL
                               AND r.country_code = r.original_country_value
                               AND r.country_code IS DISTINCT FROM r.normalized_country_code THEN
                                UPDATE shipping_addresses
                                SET country_code = r.normalized_country_code,
                                    country_normalization_status = 'Normalized'
                                WHERE shipping_address_id = r.shipping_address_id;

                                UPDATE country_normalization_audits
                                SET preserved_historical_value = false,
                                    reason = 'Historical provenance requires steward review; current value preserved.',
                                    correlation_batch_id = v_batch,
                                    recorded_at_utc = NOW()
                                WHERE audit_id = r.audit_id;
                            END IF;
                        END LOOP;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS mdm16_r04_preflight_shipping_snapshots;");
        }
    }
}
