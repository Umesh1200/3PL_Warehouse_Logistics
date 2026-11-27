using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_6_F03_F04_SafeProvenanceAndAllowlistedDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION pg_temp.mdm16_f03_norm_key(raw_val TEXT)
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

                -- F04: Fail-closed allowlist diagnostic sanitizer (only verified active catalog tokens or blank; all unrecognized text redacted)
                CREATE OR REPLACE FUNCTION pg_temp.mdm16_f03_sanitize_allowlisted(raw_val TEXT)
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

                    v_key := pg_temp.mdm16_f03_norm_key(v);
                    IF v_key IS NULL THEN
                        RETURN '';
                    END IF;

                    SELECT EXISTS (
                        SELECT 1 FROM country_profiles cp
                        WHERE cp.is_active = true
                          AND (UPPER(cp.country_code) = v_key
                               OR UPPER(cp.iso_alpha3_code) = v_key
                               OR pg_temp.mdm16_f03_norm_key(cp.name) = v_key)
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

                -- F04: Allowlisted predefined diagnostic reason sanitizer
                CREATE OR REPLACE FUNCTION pg_temp.mdm16_f03_sanitize_reason(reason_val TEXT)
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
                    IF POSITION('@' IN v) > 0 OR v ~* 'https?://|mailto:' OR v ~ '(?:\D*\d){5,}' THEN
                        RETURN 'Unrecognized or unverified country value quarantined for steward review.';
                    END IF;
                    IF v LIKE 'Normalized to canonical %'
                       OR v LIKE 'Deterministically inferred %'
                       OR v LIKE 'Immutable historical record preserved untouched%'
                       OR v LIKE 'Unrecognized country token%'
                       OR v LIKE 'Unrecognized or unverified country value%'
                       OR v LIKE 'Blank/null country%'
                       OR v LIKE 'Ambiguous subdivision%'
                       OR v LIKE 'Unverified subdivision%'
                       OR v LIKE 'Conflicting geographic evidence:%'
                       OR v LIKE 'Conflicting timezone evidence:%'
                       OR v LIKE 'Conflicting evidence:%'
                       OR v LIKE 'Orphan or unverified blank country%'
                       OR v LIKE 'Unsupported or conflicting postal code syntax%'
                       OR v LIKE 'Quarantined or unverified customer master%'
                       OR v LIKE 'Quarantined original order shipping address%'
                       OR v LIKE 'Cross-border return shipment%'
                       OR v LIKE 'Cross-border order context%'
                       OR v LIKE 'Country normalization evaluation pending%'
                       OR v LIKE 'Runtime write recorded pending normalization%'
                       OR v LIKE 'Runtime write reconciled to canonical%'
                       OR v LIKE 'Historical country value restored%'
                       OR v LIKE 'Ambiguous historical restoration provenance%'
                       OR v LIKE 'Historical blank country inference preserved%' THEN
                        RETURN LEFT(v, 500)::VARCHAR(500);
                    END IF;
                    RETURN 'Unrecognized or unverified country value quarantined for steward review.';
                END $$;

                DO $$
                DECLARE
                    v_batch CONSTANT VARCHAR(64) := '20261127081525_MDM1_6_F03_F04_SafeProvenance';
                    v_has_profiles BOOLEAN;
                    v_orig_mdm16_ts TIMESTAMPTZ;
                    r RECORD;
                    v_has_pre_mig_fulfillment BOOLEAN;
                    v_has_post_mig_fulfillment BOOLEAN;
                    v_orig_is_verified_catalog BOOLEAN;
                    v_new_epoch BIGINT;
                BEGIN
                    -- 0. Repair sentinel initial_recorded_at_utc (-infinity / 0001-01-01) left when Npgsql mapped DateTime.MinValue to -infinity in 20261127051434
                    SELECT MIN(recorded_at_utc)
                    INTO v_orig_mdm16_ts
                    FROM country_normalization_audits
                    WHERE correlation_batch_id = '20261126143014_MDM1_6_LegacyCountryNormalization'
                      AND recorded_at_utc > TIMESTAMPTZ '1970-01-01 00:00:00+00';

                    UPDATE country_normalization_audits
                    SET initial_recorded_at_utc = CASE
                            WHEN initial_correlation_batch_id = '20261126143014_MDM1_6_LegacyCountryNormalization'
                                 AND v_orig_mdm16_ts IS NOT NULL
                                THEN LEAST(recorded_at_utc, v_orig_mdm16_ts)
                            ELSE recorded_at_utc
                        END
                    WHERE initial_recorded_at_utc <= TIMESTAMPTZ '1970-01-01 00:00:00+00';

                    -- 1. F04: Apply fail-closed allowlisted sanitization to all existing audit rows
                    UPDATE country_normalization_audits
                    SET original_country_value = pg_temp.mdm16_f03_sanitize_allowlisted(original_country_value),
                        reason = pg_temp.mdm16_f03_sanitize_reason(reason)
                    WHERE original_country_value IS DISTINCT FROM pg_temp.mdm16_f03_sanitize_allowlisted(original_country_value)
                       OR reason IS DISTINCT FROM pg_temp.mdm16_f03_sanitize_reason(reason);

                    SELECT EXISTS (SELECT 1 FROM country_profiles WHERE is_active = true) INTO v_has_profiles;
                    IF NOT v_has_profiles THEN
                        RETURN;
                    END IF;

                    -- 2. F03: Safe Historical Restoration Verification on ShippingAddresses
                    -- Never restore OriginalCountryValue merely because it differs from current country_code.
                    -- Require all 4 proofs:
                    --   (a) Initial mutation came from 20261126143014_MDM1_6_LegacyCountryNormalization
                    --   (b) Record was already historically immutable at initial_recorded_at_utc
                    --   (c) Present value remains attributable to that migration (or R04's prior restore)
                    --   (d) No legitimate subsequent amendment or post-migration fulfillment superseded it
                    FOR r IN
                        SELECT sa.shipping_address_id, sa.order_id, sa.country_code, sa.is_amended, sa.amended_at_utc,
                               o.status AS order_status, o.updated_at AS order_updated_at,
                               cna.audit_id, cna.original_country_value, cna.normalized_country_code,
                               cna.preserved_historical_value, cna.was_inferred_from_parent,
                               cna.correlation_batch_id, cna.initial_correlation_batch_id, cna.initial_recorded_at_utc
                        FROM shipping_addresses sa
                        INNER JOIN outbound_orders o ON o.order_id = sa.order_id
                        INNER JOIN country_normalization_audits cna
                            ON cna.entity_type = 'ShippingAddress'
                           AND cna.entity_id = sa.shipping_address_id
                           AND cna.field_name = 'CountryCode'
                        WHERE sa.is_deleted = false AND o.is_deleted = false
                    LOOP
                        -- Case 4: Historically immutable blank country previously inferred -> preserve current value untouched, never guess/fabricate
                        IF (r.was_inferred_from_parent = true OR COALESCE(BTRIM(r.country_code), '') <> '')
                           AND COALESCE(BTRIM(r.original_country_value), '') = ''
                           AND (r.order_status IN ('Allocated', 'PartiallyAllocated', 'InProgress', 'Completed', 'Cancelled')
                                OR EXISTS (SELECT 1 FROM packages p WHERE p.outbound_order_id = r.order_id AND p.is_deleted = false AND (p.status <> 'Open' OR p.shipment_id IS NOT NULL))) THEN
                            UPDATE country_normalization_audits
                            SET reason = 'Historical blank country inference preserved without unverified restoration.',
                                correlation_batch_id = v_batch,
                                recorded_at_utc = NOW()
                            WHERE audit_id = r.audit_id
                              AND reason <> 'Historical blank country inference preserved without unverified restoration.';
                            CONTINUE;
                        END IF;

                        SELECT EXISTS (
                            SELECT 1 FROM packages p
                            WHERE p.outbound_order_id = r.order_id AND p.is_deleted = false
                              AND (p.status <> 'Open' OR p.shipment_id IS NOT NULL
                                   OR EXISTS (SELECT 1 FROM bills_of_lading bol WHERE bol.shipment_id = p.shipment_id AND bol.is_deleted = false))
                              AND p.created_at <= r.initial_recorded_at_utc
                        ) INTO v_has_pre_mig_fulfillment;

                        SELECT EXISTS (
                            SELECT 1 FROM packages p
                            WHERE p.outbound_order_id = r.order_id AND p.is_deleted = false
                              AND (p.status <> 'Open' OR p.shipment_id IS NOT NULL)
                              AND p.created_at > r.initial_recorded_at_utc
                        ) INTO v_has_post_mig_fulfillment;

                        v_orig_is_verified_catalog :=
                            r.original_country_value IS NOT NULL
                            AND BTRIM(r.original_country_value) <> ''
                            AND r.original_country_value <> '[REDACTED_POTENTIAL_PII]';

                        -- Case 1: Proven pre-R04 MDM-1.6 mutation on already-immutable record with no subsequent amendment
                        IF r.initial_correlation_batch_id = '20261126143014_MDM1_6_LegacyCountryNormalization'
                           AND r.was_inferred_from_parent = false
                           AND v_orig_is_verified_catalog
                           AND v_has_pre_mig_fulfillment
                           AND r.is_amended = false
                           AND r.amended_at_utc IS NULL
                           AND (r.order_updated_at IS NULL OR r.order_updated_at <= r.initial_recorded_at_utc)
                           AND r.normalized_country_code IS NOT NULL
                           AND r.original_country_value IS DISTINCT FROM r.normalized_country_code THEN
                            IF r.country_code = r.normalized_country_code THEN
                                UPDATE shipping_addresses
                                SET country_code = r.original_country_value,
                                    country_normalization_status = 'Normalized'
                                WHERE shipping_address_id = r.shipping_address_id;

                                UPDATE country_normalization_audits
                                SET preserved_historical_value = true,
                                    reason = 'Historical country value restored after verifying original MDM-1.6 pre-R04 mutation.',
                                    correlation_batch_id = v_batch,
                                    recorded_at_utc = NOW()
                                WHERE audit_id = r.audit_id;
                            ELSIF r.country_code = r.original_country_value AND r.preserved_historical_value = true THEN
                                -- Already safely preserved/restored
                                NULL;
                            END IF;
                            CONTINUE;
                        END IF;

                        -- Case 2: Subsequent fulfillment occurred AFTER valid MDM-1.6 normalization (not amended) and R04 had reverted it -> keep canonical normalized value
                        IF r.correlation_batch_id = '20261127051434_MDM1_6_R04_NormalizationRemediationLifecycle'
                           AND r.initial_correlation_batch_id = '20261126143014_MDM1_6_LegacyCountryNormalization'
                           AND NOT v_has_pre_mig_fulfillment
                           AND v_has_post_mig_fulfillment
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
                                reason = 'Ambiguous historical restoration provenance: subsequent fulfillment post-dated original normalization; canonical value preserved.',
                                correlation_batch_id = v_batch,
                                recorded_at_utc = NOW()
                            WHERE audit_id = r.audit_id;
                            CONTINUE;
                        END IF;

                        -- Case 3: Legitimate amendment or ambiguous provenance -> preserve current value untouched and record safe remediation diagnostic
                        IF (r.is_amended = true OR r.amended_at_utc IS NOT NULL)
                           AND r.preserved_historical_value = true
                           AND r.correlation_batch_id = '20261127051434_MDM1_6_R04_NormalizationRemediationLifecycle' THEN
                            UPDATE country_normalization_audits
                            SET reason = 'Ambiguous historical restoration provenance: amended record preserved for steward review.',
                                correlation_batch_id = v_batch,
                                recorded_at_utc = NOW()
                            WHERE audit_id = r.audit_id;
                        END IF;
                    END LOOP;

                    -- 3. Verify MDM-1.5 epoch & outbox propagation for Facility status-only normalization updates
                    FOR r IN
                        SELECT f.facility_id, f.country_code, f.country_normalization_status, cna.normalization_status AS audit_status
                        FROM facilities f
                        INNER JOIN country_normalization_audits cna
                            ON cna.entity_type = 'Facility'
                           AND cna.entity_id = f.facility_id
                           AND cna.field_name = 'CountryCode'
                        WHERE f.is_deleted = false
                          AND f.country_normalization_status IS DISTINCT FROM cna.normalization_status
                    LOOP
                        UPDATE facilities
                        SET country_normalization_status = r.audit_status,
                            updated_at = NOW()
                        WHERE facility_id = r.facility_id;

                        INSERT INTO configuration_scope_epochs (epoch_id, scope_type, scope_key, current_epoch, updated_at_utc)
                        VALUES (gen_random_uuid(), 'Facility', UPPER(r.facility_id::text), 1, NOW())
                        ON CONFLICT (scope_type, scope_key) DO UPDATE SET current_epoch = configuration_scope_epochs.current_epoch + 1, updated_at_utc = NOW()
                        RETURNING current_epoch INTO v_new_epoch;
                        INSERT INTO configuration_invalidation_events (scope_type, scope_key, new_epoch, entity_type, entity_id, created_at_utc, dispatch_attempts)
                        VALUES ('Facility', UPPER(r.facility_id::text), v_new_epoch, 'Facility', r.facility_id::text, NOW(), 0);

                        IF r.country_code IS NOT NULL AND BTRIM(r.country_code) <> '' THEN
                            INSERT INTO configuration_scope_epochs (epoch_id, scope_type, scope_key, current_epoch, updated_at_utc)
                            VALUES (gen_random_uuid(), 'Country', UPPER(BTRIM(r.country_code)), 1, NOW())
                            ON CONFLICT (scope_type, scope_key) DO UPDATE SET current_epoch = configuration_scope_epochs.current_epoch + 1, updated_at_utc = NOW()
                            RETURNING current_epoch INTO v_new_epoch;
                            INSERT INTO configuration_invalidation_events (scope_type, scope_key, new_epoch, entity_type, entity_id, created_at_utc, dispatch_attempts)
                            VALUES ('Country', UPPER(BTRIM(r.country_code)), v_new_epoch, 'Facility', r.facility_id::text, NOW(), 0);
                        END IF;

                        INSERT INTO configuration_scope_epochs (epoch_id, scope_type, scope_key, current_epoch, updated_at_utc)
                        VALUES (gen_random_uuid(), 'Global', 'GLOBAL', 1, NOW())
                        ON CONFLICT (scope_type, scope_key) DO UPDATE SET current_epoch = configuration_scope_epochs.current_epoch + 1, updated_at_utc = NOW()
                        RETURNING current_epoch INTO v_new_epoch;
                        INSERT INTO configuration_invalidation_events (scope_type, scope_key, new_epoch, entity_type, entity_id, created_at_utc, dispatch_attempts)
                        VALUES ('Global', 'GLOBAL', v_new_epoch, 'Facility', r.facility_id::text, NOW(), 0);
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Additive safety/sanitization migration: no destructive schema changes to revert.
        }
    }
}
