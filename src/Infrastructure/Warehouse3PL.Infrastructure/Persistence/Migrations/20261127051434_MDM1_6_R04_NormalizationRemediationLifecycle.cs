using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_6_R04_NormalizationRemediationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "country_code",
                table: "facilities",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldDefaultValue: "USA");

            migrationBuilder.AddColumn<string>(
                name: "initial_correlation_batch_id",
                table: "country_normalization_audits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "initial_recorded_at_utc",
                table: "country_normalization_audits",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill initial_* provenance columns on pre-existing MDM-1.6 audit rows
            migrationBuilder.Sql("""
                UPDATE country_normalization_audits
                SET initial_correlation_batch_id = CASE WHEN initial_correlation_batch_id = '' THEN correlation_batch_id ELSE initial_correlation_batch_id END,
                    initial_recorded_at_utc = CASE WHEN initial_recorded_at_utc = TIMESTAMPTZ '0001-01-01 00:00:00+00' THEN recorded_at_utc ELSE initial_recorded_at_utc END;
                """);

            // MDM-1.6 R02/R03/R04/R05 Fail-Closed Cutover Guard & Deterministic SQL/C# Parity Reconciliation
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    v_dup_countries INT;
                    v_dup_alpha3 INT;
                    v_dup_currencies INT;
                    v_dup_epochs INT;
                    v_dup_aliases INT;
                    v_orphan_aliases INT;
                    v_total_rows BIGINT;
                    v_has_profiles BOOLEAN;
                    v_has_divisions BOOLEAN;
                BEGIN
                    IF to_regclass('country_profiles') IS NULL
                       OR to_regclass('administrative_divisions') IS NULL
                       OR to_regclass('time_zone_profiles') IS NULL
                       OR to_regclass('configuration_scope_epochs') IS NULL
                       OR to_regclass('configuration_invalidation_events') IS NULL
                       OR to_regclass('currencies') IS NULL
                       OR to_regclass('country_normalization_aliases') IS NULL
                       OR to_regclass('country_normalization_audits') IS NULL THEN
                        RAISE EXCEPTION 'Migration MDM1_6_R04_NormalizationRemediationLifecycle blocked: required MDM-1.1..MDM-1.6 reference, epoch, or audit tables are missing.';
                    END IF;

                    SELECT COUNT(*) INTO v_dup_countries FROM (
                        SELECT UPPER(BTRIM(country_code)) FROM country_profiles WHERE is_active = true GROUP BY 1 HAVING COUNT(*) > 1
                    ) t;
                    IF v_dup_countries > 0 THEN
                        RAISE EXCEPTION 'Migration MDM1_6_R04_NormalizationRemediationLifecycle blocked: duplicate active country_code entries exist in country_profiles.';
                    END IF;

                    SELECT COUNT(*) INTO v_dup_alpha3 FROM (
                        SELECT UPPER(BTRIM(iso_alpha3_code)) FROM country_profiles WHERE is_active = true GROUP BY 1 HAVING COUNT(*) > 1
                    ) t;
                    IF v_dup_alpha3 > 0 THEN
                        RAISE EXCEPTION 'Migration MDM1_6_R04_NormalizationRemediationLifecycle blocked: duplicate active iso_alpha3_code entries exist in country_profiles.';
                    END IF;

                    SELECT COUNT(*) INTO v_dup_currencies FROM (
                        SELECT UPPER(BTRIM(currency_code)) FROM currencies WHERE is_active = true GROUP BY 1 HAVING COUNT(*) > 1
                    ) t;
                    IF v_dup_currencies > 0 THEN
                        RAISE EXCEPTION 'Migration MDM1_6_R04_NormalizationRemediationLifecycle blocked: duplicate active currency_code entries exist in currencies.';
                    END IF;

                    SELECT COUNT(*) INTO v_dup_epochs FROM (
                        SELECT UPPER(BTRIM(scope_type)), UPPER(BTRIM(scope_key)) FROM configuration_scope_epochs GROUP BY 1, 2 HAVING COUNT(*) > 1
                    ) t;
                    IF v_dup_epochs > 0 THEN
                        RAISE EXCEPTION 'Migration MDM1_6_R04_NormalizationRemediationLifecycle blocked: conflicting scope epoch keys exist in configuration_scope_epochs.';
                    END IF;

                    SELECT COUNT(*) INTO v_dup_aliases FROM (
                        SELECT UPPER(BTRIM(alias_key)) FROM country_normalization_aliases WHERE is_active = true GROUP BY 1 HAVING COUNT(*) > 1
                    ) t;
                    IF v_dup_aliases > 0 THEN
                        RAISE EXCEPTION 'Migration MDM1_6_R04_NormalizationRemediationLifecycle blocked: duplicate active alias_key entries exist in country_normalization_aliases.';
                    END IF;

                    SELECT COUNT(*) INTO v_orphan_aliases
                    FROM country_normalization_aliases a
                    LEFT JOIN country_profiles cp ON cp.country_code = a.country_code AND cp.is_active = true
                    WHERE a.is_active = true AND cp.country_code IS NULL;
                    IF v_orphan_aliases > 0 THEN
                        RAISE EXCEPTION 'Migration MDM1_6_R04_NormalizationRemediationLifecycle blocked: active country_normalization_aliases reference missing or inactive CountryProfiles.';
                    END IF;

                    SELECT
                        (SELECT COUNT(*) FROM facilities WHERE is_deleted = false) +
                        (SELECT COUNT(*) FROM customers WHERE is_deleted = false) +
                        (SELECT COUNT(*) FROM suppliers WHERE is_deleted = false) +
                        (SELECT COUNT(*) FROM shipping_addresses WHERE is_deleted = false) +
                        (SELECT COUNT(*) FROM return_shipments) +
                        (SELECT COUNT(*) FROM carrier_contractual_sla_policies) +
                        (SELECT COUNT(*) FROM carrier_sla_compliance_records)
                    INTO v_total_rows;

                    SELECT EXISTS (SELECT 1 FROM country_profiles WHERE is_active = true) INTO v_has_profiles;
                    SELECT EXISTS (
                        SELECT 1 FROM administrative_divisions d
                        INNER JOIN country_profiles cp ON cp.country_code = d.country_code AND cp.is_active = true
                        WHERE d.is_active = true
                    ) INTO v_has_divisions;

                    -- R04: Distinguish fresh empty install vs populated DB lacking required reference catalogs (FAIL CLOSED)
                    IF v_total_rows > 0 AND NOT v_has_profiles THEN
                        RAISE EXCEPTION 'Migration MDM1_6_R04_NormalizationRemediationLifecycle blocked: populated database has % operational records without active CountryProfile reference catalogs.', v_total_rows;
                    END IF;

                    IF v_total_rows > 0 AND NOT v_has_divisions THEN
                        RAISE EXCEPTION 'Migration MDM1_6_R04_NormalizationRemediationLifecycle blocked: populated database has % operational records without active AdministrativeDivision reference catalogs.', v_total_rows;
                    END IF;
                END $$;

                CREATE OR REPLACE FUNCTION pg_temp.mdm16_r04_norm_key(raw_val TEXT)
                RETURNS TEXT
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    v_collapsed TEXT;
                    v_compact TEXT;
                BEGIN
                    IF raw_val IS NULL OR btrim(raw_val) = '' THEN
                        RETURN NULL;
                    END IF;
                    v_collapsed := UPPER(REGEXP_REPLACE(BTRIM(raw_val), '\s+', ' ', 'g'));
                    v_compact := REPLACE(v_collapsed, ' ', '');
                    IF v_compact ~ '^([A-Z]\.){2,}$' THEN
                        RETURN REPLACE(v_compact, '.', '');
                    END IF;
                    RETURN v_collapsed;
                END $$;

                CREATE OR REPLACE FUNCTION pg_temp.mdm16_r04_sanitize_raw(raw_val TEXT)
                RETURNS VARCHAR(120)
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    v_collapsed TEXT;
                BEGIN
                    IF raw_val IS NULL THEN
                        RETURN NULL;
                    END IF;
                    IF BTRIM(raw_val) = '' THEN
                        RETURN '';
                    END IF;
                    v_collapsed := REGEXP_REPLACE(BTRIM(REPLACE(REPLACE(raw_val, CHR(13), ' '), CHR(10), ' ')), '\s+', ' ', 'g');
                    IF LENGTH(v_collapsed) > 64
                       OR POSITION('@' IN v_collapsed) > 0
                       OR LOWER(v_collapsed) LIKE '%http://%'
                       OR LOWER(v_collapsed) LIKE '%https://%'
                       OR LOWER(v_collapsed) LIKE '%mailto:%'
                       OR v_collapsed ~ '(\D*\d){5,}'
                       OR v_collapsed ~ '\+?\d[\d\s\-\(\)]{5,}\d'
                       OR v_collapsed ~* '\y\d+\s+[A-Za-z].*\y(st|street|ave|avenue|rd|road|blvd|boulevard|dr|drive|ln|lane|apt|suite|box)\y'
                       OR raw_val ~ '[[:cntrl:]]' THEN
                        RETURN '[REDACTED_POTENTIAL_PII]';
                    END IF;
                    RETURN LEFT(v_collapsed, 120)::VARCHAR(120);
                END $$;

                -- Seed verified canonical aliases idempotently when active profiles exist
                INSERT INTO country_normalization_aliases (alias_id, alias_key, country_code, source_standard, is_active, created_at)
                SELECT gen_random_uuid(), v.alias_key, cp.country_code, v.source_standard, true, NOW()
                FROM (
                    SELECT pg_temp.mdm16_r04_norm_key(iso_alpha3_code) AS alias_key, country_code, 'ISO-3166-1-Alpha3' AS source_standard FROM country_profiles WHERE is_active = true
                    UNION ALL
                    SELECT pg_temp.mdm16_r04_norm_key(name), country_code, 'ISO-3166-1-ShortName' FROM country_profiles WHERE is_active = true
                    UNION ALL
                    SELECT * FROM (VALUES
                        ('USA', 'US', 'ISO-3166-1-DottedAlias'),
                        ('US', 'US', 'ISO-3166-1-DottedAlias'),
                        ('UNITED STATES OF AMERICA', 'US', 'ISO-3166-1-OfficialName'),
                        ('THE UNITED STATES', 'US', 'VerifiedAlias'),
                        ('THE UNITED STATES OF AMERICA', 'US', 'VerifiedAlias'),
                        ('NPL', 'NP', 'ISO-3166-1-Alpha3'),
                        ('FEDERAL DEMOCRATIC REPUBLIC OF NEPAL', 'NP', 'ISO-3166-1-OfficialName'),
                        ('KINGDOM OF NEPAL', 'NP', 'VerifiedHistoricalName'),
                        ('UK', 'GB', 'ISO-3166-1-ExceptionalReservation'),
                        ('GREAT BRITAIN', 'GB', 'VerifiedAlias'),
                        ('UNITED KINGDOM', 'GB', 'ISO-3166-1-ShortName'),
                        ('UNITED KINGDOM OF GREAT BRITAIN AND NORTHERN IRELAND', 'GB', 'ISO-3166-1-OfficialName'),
                        ('REPUBLIC OF INDIA', 'IN', 'ISO-3166-1-OfficialName'),
                        ('FEDERAL REPUBLIC OF GERMANY', 'DE', 'ISO-3166-1-OfficialName'),
                        ('COMMONWEALTH OF AUSTRALIA', 'AU', 'ISO-3166-1-OfficialName'),
                        ('REPUBLIC OF SINGAPORE', 'SG', 'ISO-3166-1-OfficialName')
                    ) AS known(alias_key, country_code, source_standard)
                ) v
                INNER JOIN country_profiles cp ON cp.country_code = v.country_code AND cp.is_active = true
                WHERE v.alias_key IS NOT NULL AND v.alias_key <> ''
                ON CONFLICT (alias_key) DO NOTHING;

                CREATE OR REPLACE FUNCTION pg_temp.mdm16_r04_resolve_country(
                    raw_val TEXT,
                    state_val TEXT,
                    postal_val TEXT,
                    parent_country TEXT,
                    parent_src TEXT,
                    tz_hint TEXT,
                    allow_tz BOOLEAN,
                    req_parent BOOLEAN
                )
                RETURNS TABLE (
                    is_resolved BOOLEAN,
                    target_code VARCHAR(3),
                    norm_status VARCHAR(30),
                    was_inferred BOOLEAN,
                    inf_source VARCHAR(120),
                    reason VARCHAR(500)
                )
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    v_key TEXT;
                    v_code VARCHAR(3);
                    v_src VARCHAR(120);
                    v_div_count INT;
                    v_div_country VARCHAR(2);
                    v_tz_country VARCHAR(2);
                    v_candidate VARCHAR(2);
                    v_claimed_valid BOOLEAN;
                    v_div_valid BOOLEAN;
                    v_postal_trim TEXT;
                BEGIN
                    v_key := pg_temp.mdm16_r04_norm_key(raw_val);
                    v_postal_trim := COALESCE(BTRIM(postal_val), '');

                    IF allow_tz AND tz_hint IS NOT NULL AND BTRIM(tz_hint) <> '' THEN
                        SELECT UPPER(tz.country_code) INTO v_tz_country
                        FROM time_zone_profiles tz
                        INNER JOIN country_profiles cp ON cp.country_code = UPPER(tz.country_code) AND cp.is_active = true
                        WHERE tz.is_active = true AND tz.time_zone_id = BTRIM(tz_hint) AND tz.country_code IS NOT NULL
                        LIMIT 1;
                    END IF;

                    IF state_val IS NOT NULL AND BTRIM(state_val) <> '' THEN
                        SELECT COUNT(DISTINCT d.country_code), MIN(d.country_code)
                        INTO v_div_count, v_div_country
                        FROM administrative_divisions d
                        INNER JOIN country_profiles cp ON cp.country_code = d.country_code AND cp.is_active = true
                        WHERE d.is_active = true
                          AND pg_temp.mdm16_r04_norm_key(state_val) IN (
                              pg_temp.mdm16_r04_norm_key(d.code),
                              pg_temp.mdm16_r04_norm_key(d.iso_code),
                              pg_temp.mdm16_r04_norm_key(d.name)
                          );
                    ELSE
                        v_div_count := 0;
                        v_div_country := NULL;
                    END IF;

                    -- 1. Explicit non-blank value branch
                    IF v_key IS NOT NULL THEN
                        SELECT cp.country_code, 'CatalogExact' INTO v_code, v_src
                        FROM country_profiles cp WHERE cp.is_active = true AND UPPER(cp.country_code) = v_key LIMIT 1;

                        IF v_code IS NULL THEN
                            SELECT cp.country_code, 'CatalogIsoAlpha3' INTO v_code, v_src
                            FROM country_profiles cp WHERE cp.is_active = true AND UPPER(cp.iso_alpha3_code) = v_key LIMIT 1;
                        END IF;

                        IF v_code IS NULL THEN
                            SELECT cp.country_code, 'CatalogName' INTO v_code, v_src
                            FROM country_profiles cp WHERE cp.is_active = true AND pg_temp.mdm16_r04_norm_key(cp.name) = v_key LIMIT 1;
                        END IF;

                        IF v_code IS NULL THEN
                            SELECT a.country_code, 'CatalogAlias:' || a.source_standard INTO v_code, v_src
                            FROM country_normalization_aliases a
                            INNER JOIN country_profiles cp ON cp.country_code = a.country_code AND cp.is_active = true
                            WHERE a.is_active = true AND a.alias_key = v_key LIMIT 1;
                        END IF;

                        IF v_code IS NULL THEN
                            RETURN QUERY SELECT false, NULL::VARCHAR(3), 'PendingNormalization'::VARCHAR(30), false, NULL::VARCHAR(120),
                                'Unrecognized country token not found in active CountryProfile or CountryNormalizationAlias catalog.'::VARCHAR(500);
                            RETURN;
                        END IF;

                        -- R02: Timezone conflict on explicit Facility country
                        IF allow_tz AND v_tz_country IS NOT NULL AND v_tz_country <> v_code THEN
                            RETURN QUERY SELECT false, NULL::VARCHAR(3), 'PendingNormalization'::VARCHAR(30), false, NULL::VARCHAR(120),
                                LEFT('Conflicting timezone evidence: configured timezone maps to ''' || v_tz_country || ''' while country indicates ''' || v_code || '''.', 500)::VARCHAR(500);
                            RETURN;
                        END IF;

                        IF v_div_count = 1 AND v_div_country <> v_code THEN
                            SELECT (v_postal_trim <> '' AND (v_postal_trim ~ cp.postal_code_regex OR (cp.legacy_postal_code_regex IS NOT NULL AND v_postal_trim ~ cp.legacy_postal_code_regex)))
                            INTO v_claimed_valid FROM country_profiles cp WHERE cp.country_code = v_code;

                            SELECT (v_postal_trim <> '' AND (v_postal_trim ~ cp.postal_code_regex OR (cp.legacy_postal_code_regex IS NOT NULL AND v_postal_trim ~ cp.legacy_postal_code_regex)))
                            INTO v_div_valid FROM country_profiles cp WHERE cp.country_code = v_div_country;

                            IF NOT COALESCE(v_claimed_valid, false) OR COALESCE(v_div_valid, false) THEN
                                RETURN QUERY SELECT false, NULL::VARCHAR(3), 'PendingNormalization'::VARCHAR(30), false, NULL::VARCHAR(120),
                                    LEFT('Conflicting geographic evidence: subdivision matches ''' || v_div_country || ''' while country indicates ''' || v_code || '''.', 500)::VARCHAR(500);
                                RETURN;
                            END IF;
                        END IF;

                        RETURN QUERY SELECT true, v_code, 'Normalized'::VARCHAR(30), false, v_src,
                            LEFT('Normalized to canonical ''' || v_code || ''' via ' || v_src || '.', 500)::VARCHAR(500);
                        RETURN;
                    END IF;

                    -- 2. Blank/null branch
                    IF state_val IS NULL OR BTRIM(state_val) = '' OR v_postal_trim = '' THEN
                        RETURN QUERY SELECT false, NULL::VARCHAR(3), 'PendingNormalization'::VARCHAR(30), false, NULL::VARCHAR(120),
                            'Blank/null country lacks both verified domestic subdivision and postal code evidence.'::VARCHAR(500);
                        RETURN;
                    END IF;

                    IF v_div_count > 1 THEN
                        RETURN QUERY SELECT false, NULL::VARCHAR(3), 'PendingNormalization'::VARCHAR(30), false, NULL::VARCHAR(120),
                            'Ambiguous subdivision matches multiple active countries.'::VARCHAR(500);
                        RETURN;
                    END IF;

                    IF v_div_count = 0 THEN
                        RETURN QUERY SELECT false, NULL::VARCHAR(3), 'PendingNormalization'::VARCHAR(30), false, NULL::VARCHAR(120),
                            'Unverified subdivision prevents deterministic parent-context country inference.'::VARCHAR(500);
                        RETURN;
                    END IF;

                    IF parent_country IS NOT NULL AND BTRIM(parent_country) <> '' THEN
                        IF v_div_country <> UPPER(BTRIM(parent_country)) THEN
                            RETURN QUERY SELECT false, NULL::VARCHAR(3), 'PendingNormalization'::VARCHAR(30), false, NULL::VARCHAR(120),
                                LEFT('Conflicting evidence: subdivision belongs to ''' || v_div_country || ''' while verified parent ''' || COALESCE(parent_src, '') || ''' indicates ''' || parent_country || '''.', 500)::VARCHAR(500);
                            RETURN;
                        END IF;
                        v_candidate := v_div_country;
                        v_src := COALESCE(parent_src, 'ParentContext') || '+AdministrativeDivision+PostalCode';
                    ELSIF NOT req_parent THEN
                        IF allow_tz AND v_tz_country IS NOT NULL AND v_tz_country <> v_div_country THEN
                            RETURN QUERY SELECT false, NULL::VARCHAR(3), 'PendingNormalization'::VARCHAR(30), false, NULL::VARCHAR(120),
                                LEFT('Conflicting timezone evidence: configured timezone maps to ''' || v_tz_country || ''' while subdivision belongs to ''' || v_div_country || '''.', 500)::VARCHAR(500);
                            RETURN;
                        END IF;
                        v_candidate := v_div_country;
                        v_src := CASE WHEN v_tz_country IS NOT NULL THEN 'TimeZoneProfile+AdministrativeDivision+PostalCode' ELSE 'AdministrativeDivision+PostalCode' END;
                    ELSE
                        RETURN QUERY SELECT false, NULL::VARCHAR(3), 'PendingNormalization'::VARCHAR(30), false, NULL::VARCHAR(120),
                            'Orphan or unverified blank country without verified parent and domestic geographic evidence.'::VARCHAR(500);
                        RETURN;
                    END IF;

                    SELECT (v_postal_trim ~ cp.postal_code_regex OR (cp.legacy_postal_code_regex IS NOT NULL AND v_postal_trim ~ cp.legacy_postal_code_regex))
                    INTO v_claimed_valid FROM country_profiles cp WHERE cp.country_code = v_candidate AND cp.is_active = true;

                    IF NOT COALESCE(v_claimed_valid, false) THEN
                        RETURN QUERY SELECT false, NULL::VARCHAR(3), 'PendingNormalization'::VARCHAR(30), false, NULL::VARCHAR(120),
                            LEFT('Unsupported or conflicting postal code syntax for inferred country ''' || v_candidate || '''.', 500)::VARCHAR(500);
                        RETURN;
                    END IF;

                    RETURN QUERY SELECT true, v_candidate::VARCHAR(3), 'Normalized'::VARCHAR(30), true, v_src,
                        LEFT('Deterministically inferred ''' || v_candidate || ''' via ' || v_src || '.', 500)::VARCHAR(500);
                END $$;

                CREATE OR REPLACE FUNCTION pg_temp.mdm16_r04_reconcile_audit(
                    p_entity_type VARCHAR(64),
                    p_entity_id UUID,
                    p_field_name VARCHAR(64),
                    p_raw_val TEXT,
                    p_target_code VARCHAR(3),
                    p_norm_status VARCHAR(30),
                    p_preserved_hist BOOLEAN,
                    p_was_inferred BOOLEAN,
                    p_inf_source VARCHAR(120),
                    p_reason VARCHAR(500),
                    p_facility_id UUID,
                    p_client_id UUID,
                    p_batch_id VARCHAR(64),
                    p_force_insert BOOLEAN
                )
                RETURNS VOID
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    v_existing RECORD;
                    v_sanitized_raw VARCHAR(120);
                BEGIN
                    SELECT audit_id, normalized_country_code, normalization_status, preserved_historical_value,
                           was_inferred_from_parent, inference_source, reason, facility_id, client_id
                    INTO v_existing
                    FROM country_normalization_audits
                    WHERE entity_type = p_entity_type AND entity_id = p_entity_id AND field_name = p_field_name;

                    IF FOUND THEN
                        -- Semantic idempotency: when already Normalized with identical resolved code, historical flag, and tenant/facility scope,
                        -- preserve the normalizing batch provenance untouched.
                        IF v_existing.normalization_status = 'Normalized'
                           AND p_norm_status = 'Normalized'
                           AND v_existing.normalized_country_code IS NOT DISTINCT FROM p_target_code
                           AND v_existing.preserved_historical_value = p_preserved_hist
                           AND v_existing.facility_id IS NOT DISTINCT FROM p_facility_id
                           AND v_existing.client_id IS NOT DISTINCT FROM p_client_id THEN
                            RETURN;
                        END IF;

                        -- Semantic idempotency: only update if state, association, or reason actually changed
                        IF v_existing.normalized_country_code IS NOT DISTINCT FROM p_target_code
                           AND v_existing.normalization_status = p_norm_status
                           AND v_existing.preserved_historical_value = p_preserved_hist
                           AND v_existing.was_inferred_from_parent = p_was_inferred
                           AND v_existing.inference_source IS NOT DISTINCT FROM p_inf_source
                           AND v_existing.facility_id IS NOT DISTINCT FROM p_facility_id
                           AND v_existing.client_id IS NOT DISTINCT FROM p_client_id
                           AND v_existing.reason = p_reason THEN
                            RETURN;
                        END IF;

                        UPDATE country_normalization_audits
                        SET normalized_country_code = p_target_code,
                            normalization_status = p_norm_status,
                            preserved_historical_value = p_preserved_hist,
                            was_inferred_from_parent = p_was_inferred,
                            inference_source = p_inf_source,
                            reason = p_reason,
                            facility_id = p_facility_id,
                            client_id = p_client_id,
                            correlation_batch_id = p_batch_id,
                            recorded_at_utc = NOW()
                        WHERE audit_id = v_existing.audit_id;
                        RETURN;
                    END IF;

                    IF NOT p_force_insert THEN
                        RETURN;
                    END IF;

                    v_sanitized_raw := pg_temp.mdm16_r04_sanitize_raw(p_raw_val);
                    INSERT INTO country_normalization_audits
                        (audit_id, entity_type, entity_id, field_name, original_country_value, normalized_country_code,
                         normalization_status, preserved_historical_value, was_inferred_from_parent, inference_source,
                         reason, facility_id, client_id, correlation_batch_id, recorded_at_utc,
                         initial_correlation_batch_id, initial_recorded_at_utc)
                    VALUES
                        (gen_random_uuid(), p_entity_type, p_entity_id, p_field_name, v_sanitized_raw, p_target_code,
                         p_norm_status, p_preserved_hist, p_was_inferred, p_inf_source,
                         p_reason, p_facility_id, p_client_id, p_batch_id, NOW(),
                         p_batch_id, NOW());
                END $$;

                DO $$
                DECLARE
                    v_batch CONSTANT VARCHAR(64) := '20261127051434_MDM1_6_R04_NormalizationRemediationLifecycle';
                    v_has_profiles BOOLEAN;
                    r RECORD;
                    v_eval RECORD;
                    v_orig RECORD;
                    v_dest RECORD;
                    v_old TEXT;
                    v_new_epoch BIGINT;
                    v_client_single_country VARCHAR(3);
                    v_client_single_fac UUID;
                    v_cust_country VARCHAR(3);
                    v_fac_country VARCHAR(3);
                    v_dest_fac_country VARCHAR(3);
                    v_parent_hint TEXT;
                    v_hint_src TEXT;
                    v_is_hist BOOLEAN;
                    v_force_audit BOOLEAN;
                    v_reason VARCHAR(500);
                BEGIN
                    -- Clean up stale diagnostics for deleted records + redact legacy PII strings if present
                    DELETE FROM country_normalization_audits a
                    WHERE (a.entity_type = 'Facility' AND EXISTS (SELECT 1 FROM facilities f WHERE f.facility_id = a.entity_id AND f.is_deleted = true))
                       OR (a.entity_type = 'Customer' AND EXISTS (SELECT 1 FROM customers c WHERE c.customer_id = a.entity_id AND c.is_deleted = true))
                       OR (a.entity_type = 'Supplier' AND EXISTS (SELECT 1 FROM suppliers s WHERE s.supplier_id = a.entity_id AND s.is_deleted = true))
                       OR (a.entity_type = 'ShippingAddress' AND EXISTS (
                           SELECT 1 FROM shipping_addresses sa
                           LEFT JOIN outbound_orders o ON o.order_id = sa.order_id
                           WHERE sa.shipping_address_id = a.entity_id AND (sa.is_deleted = true OR o.is_deleted = true)
                       ));

                    UPDATE country_normalization_audits
                    SET original_country_value = '[REDACTED_POTENTIAL_PII]'
                    WHERE pg_temp.mdm16_r04_sanitize_raw(original_country_value) = '[REDACTED_POTENTIAL_PII]';

                    SELECT EXISTS (SELECT 1 FROM country_profiles WHERE is_active = true) INTO v_has_profiles;
                    IF NOT v_has_profiles THEN
                        RETURN;
                    END IF;

                    -- 1. Facilities (Mutable Master Data + Atomic MDM-1.5 Epoch & Outbox Propagation)
                    FOR r IN SELECT facility_id, country_code, state_or_province, postal_code, timezone FROM facilities WHERE is_deleted = false LOOP
                        v_old := r.country_code;
                        SELECT * INTO v_eval FROM pg_temp.mdm16_r04_resolve_country(r.country_code, r.state_or_province, r.postal_code, NULL, 'FacilityGeo', r.timezone, true, false);

                        UPDATE facilities
                        SET country_code = CASE WHEN v_eval.is_resolved THEN v_eval.target_code ELSE country_code END,
                            country_normalization_status = v_eval.norm_status,
                            updated_at = CASE WHEN v_eval.is_resolved AND v_eval.target_code IS DISTINCT FROM v_old THEN NOW() ELSE updated_at END
                        WHERE facility_id = r.facility_id;

                        v_force_audit := (v_eval.is_resolved AND (v_eval.target_code IS DISTINCT FROM v_old OR v_eval.was_inferred)) OR NOT v_eval.is_resolved;
                        PERFORM pg_temp.mdm16_r04_reconcile_audit(
                            'Facility', r.facility_id, 'CountryCode', v_old, v_eval.target_code,
                            v_eval.norm_status, false, v_eval.was_inferred, v_eval.inf_source,
                            v_eval.reason, r.facility_id, NULL, v_batch, v_force_audit);

                        IF v_eval.is_resolved AND v_eval.target_code IS DISTINCT FROM v_old THEN
                            INSERT INTO configuration_scope_epochs (epoch_id, scope_type, scope_key, current_epoch, updated_at_utc)
                            VALUES (gen_random_uuid(), 'Facility', UPPER(r.facility_id::text), 1, NOW())
                            ON CONFLICT (scope_type, scope_key) DO UPDATE SET current_epoch = configuration_scope_epochs.current_epoch + 1, updated_at_utc = NOW()
                            RETURNING current_epoch INTO v_new_epoch;
                            INSERT INTO configuration_invalidation_events (scope_type, scope_key, new_epoch, entity_type, entity_id, created_at_utc, dispatch_attempts)
                            VALUES ('Facility', UPPER(r.facility_id::text), v_new_epoch, 'Facility', r.facility_id::text, NOW(), 0);

                            IF v_old IS NOT NULL AND BTRIM(v_old) <> '' THEN
                                INSERT INTO configuration_scope_epochs (epoch_id, scope_type, scope_key, current_epoch, updated_at_utc)
                                VALUES (gen_random_uuid(), 'Country', UPPER(BTRIM(v_old)), 1, NOW())
                                ON CONFLICT (scope_type, scope_key) DO UPDATE SET current_epoch = configuration_scope_epochs.current_epoch + 1, updated_at_utc = NOW()
                                RETURNING current_epoch INTO v_new_epoch;
                                INSERT INTO configuration_invalidation_events (scope_type, scope_key, new_epoch, entity_type, entity_id, created_at_utc, dispatch_attempts)
                                VALUES ('Country', UPPER(BTRIM(v_old)), v_new_epoch, 'Facility', r.facility_id::text, NOW(), 0);
                            END IF;

                            INSERT INTO configuration_scope_epochs (epoch_id, scope_type, scope_key, current_epoch, updated_at_utc)
                            VALUES (gen_random_uuid(), 'Country', UPPER(v_eval.target_code), 1, NOW())
                            ON CONFLICT (scope_type, scope_key) DO UPDATE SET current_epoch = configuration_scope_epochs.current_epoch + 1, updated_at_utc = NOW()
                            RETURNING current_epoch INTO v_new_epoch;
                            INSERT INTO configuration_invalidation_events (scope_type, scope_key, new_epoch, entity_type, entity_id, created_at_utc, dispatch_attempts)
                            VALUES ('Country', UPPER(v_eval.target_code), v_new_epoch, 'Facility', r.facility_id::text, NOW(), 0);

                            INSERT INTO configuration_scope_epochs (epoch_id, scope_type, scope_key, current_epoch, updated_at_utc)
                            VALUES (gen_random_uuid(), 'Global', 'GLOBAL', 1, NOW())
                            ON CONFLICT (scope_type, scope_key) DO UPDATE SET current_epoch = configuration_scope_epochs.current_epoch + 1, updated_at_utc = NOW()
                            RETURNING current_epoch INTO v_new_epoch;
                            INSERT INTO configuration_invalidation_events (scope_type, scope_key, new_epoch, entity_type, entity_id, created_at_utc, dispatch_attempts)
                            VALUES ('Global', 'GLOBAL', v_new_epoch, 'Facility', r.facility_id::text, NOW(), 0);
                        END IF;
                    END LOOP;

                    -- 2. Customers (R02: NEVER discard unresolved facilities from client network!)
                    FOR r IN SELECT customer_id, client_id, default_country_code, default_state_or_province, default_postal_code FROM customers WHERE is_deleted = false LOOP
                        v_old := r.default_country_code;
                        SELECT CASE
                                   WHEN COUNT(*) FILTER (WHERE f.country_normalization_status <> 'Normalized') = 0
                                    AND COUNT(DISTINCT f.country_code) = 1
                                   THEN MIN(f.country_code)
                                   ELSE NULL
                               END,
                               CASE WHEN COUNT(DISTINCT cfa.facility_id) = 1 THEN MIN(cfa.facility_id::text)::uuid ELSE NULL END
                        INTO v_client_single_country, v_client_single_fac
                        FROM client_facility_access cfa
                        INNER JOIN clients cl ON cl.client_id = cfa.client_id AND cl.is_deleted = false AND cl.status = 'Active'
                        INNER JOIN facilities f ON f.facility_id = cfa.facility_id AND f.is_deleted = false AND f.is_active = true
                        WHERE cfa.client_id = r.client_id AND cfa.status = 'Active';

                        SELECT * INTO v_eval FROM pg_temp.mdm16_r04_resolve_country(
                            r.default_country_code, r.default_state_or_province, r.default_postal_code,
                            v_client_single_country, 'ClientDomesticAccess', NULL, false, true);

                        UPDATE customers
                        SET default_country_code = CASE WHEN v_eval.is_resolved THEN v_eval.target_code ELSE default_country_code END,
                            country_normalization_status = v_eval.norm_status,
                            updated_at = CASE WHEN v_eval.is_resolved AND v_eval.target_code IS DISTINCT FROM v_old THEN NOW() ELSE updated_at END
                        WHERE customer_id = r.customer_id;

                        v_force_audit := (v_eval.is_resolved AND (v_eval.target_code IS DISTINCT FROM v_old OR v_eval.was_inferred)) OR NOT v_eval.is_resolved;
                        PERFORM pg_temp.mdm16_r04_reconcile_audit(
                            'Customer', r.customer_id, 'DefaultCountryCode', v_old, v_eval.target_code,
                            v_eval.norm_status, false, v_eval.was_inferred, v_eval.inf_source,
                            v_eval.reason, v_client_single_fac, r.client_id, v_batch, v_force_audit);
                    END LOOP;

                    -- 3. Suppliers
                    FOR r IN SELECT supplier_id, client_id, country, state, postal_code FROM suppliers WHERE is_deleted = false LOOP
                        v_old := r.country;
                        SELECT CASE
                                   WHEN COUNT(*) FILTER (WHERE f.country_normalization_status <> 'Normalized') = 0
                                    AND COUNT(DISTINCT f.country_code) = 1
                                   THEN MIN(f.country_code)
                                   ELSE NULL
                               END,
                               CASE WHEN COUNT(DISTINCT cfa.facility_id) = 1 THEN MIN(cfa.facility_id::text)::uuid ELSE NULL END
                        INTO v_client_single_country, v_client_single_fac
                        FROM client_facility_access cfa
                        INNER JOIN clients cl ON cl.client_id = cfa.client_id AND cl.is_deleted = false AND cl.status = 'Active'
                        INNER JOIN facilities f ON f.facility_id = cfa.facility_id AND f.is_deleted = false AND f.is_active = true
                        WHERE cfa.client_id = r.client_id AND cfa.status = 'Active';

                        SELECT * INTO v_eval FROM pg_temp.mdm16_r04_resolve_country(
                            r.country, r.state, r.postal_code,
                            v_client_single_country, 'ClientDomesticAccess', NULL, false, true);

                        UPDATE suppliers
                        SET country = CASE WHEN v_eval.is_resolved THEN v_eval.target_code ELSE country END,
                            country_normalization_status = v_eval.norm_status,
                            updated_at = CASE WHEN v_eval.is_resolved AND v_eval.target_code IS DISTINCT FROM v_old THEN NOW() ELSE updated_at END
                        WHERE supplier_id = r.supplier_id;

                        v_force_audit := (v_eval.is_resolved AND (v_eval.target_code IS DISTINCT FROM v_old OR v_eval.was_inferred)) OR NOT v_eval.is_resolved;
                        PERFORM pg_temp.mdm16_r04_reconcile_audit(
                            'Supplier', r.supplier_id, 'Country', v_old, v_eval.target_code,
                            v_eval.norm_status, false, v_eval.was_inferred, v_eval.inf_source,
                            v_eval.reason, v_client_single_fac, r.client_id, v_batch, v_force_audit);
                    END LOOP;

                    -- 4. ShippingAddresses (Never inherit solely from origin warehouse; require verified CustomerDefault or StockTransfer DestinationFacility; BOL + Shipment + Package lifecycle)
                    FOR r IN
                        SELECT sa.shipping_address_id, sa.order_id, sa.country_code, sa.state_or_province, sa.postal_code,
                               o.facility_id, o.destination_facility_id, o.order_type, o.client_id, o.customer_id, o.status AS order_status,
                               EXISTS (
                                   SELECT 1 FROM packages p
                                   WHERE p.outbound_order_id = o.order_id AND p.is_deleted = false
                                     AND (p.shipment_id IS NOT NULL OR p.status <> 'Open'
                                          OR EXISTS (SELECT 1 FROM bills_of_lading bol WHERE bol.shipment_id = p.shipment_id AND bol.is_deleted = false))
                               ) AS has_fulfillment,
                               c.customer_id AS linked_cust_id, c.default_country_code AS cust_country, c.country_normalization_status AS cust_status,
                               c.is_active AS cust_active, c.is_deleted AS cust_deleted,
                               f.country_code AS fac_country, f.country_normalization_status AS fac_status, f.is_active AS fac_active, f.is_deleted AS fac_deleted,
                               df.country_code AS dest_fac_country, df.country_normalization_status AS dest_fac_status, df.is_active AS dest_fac_active, df.is_deleted AS dest_fac_deleted
                        FROM shipping_addresses sa
                        INNER JOIN outbound_orders o ON o.order_id = sa.order_id
                        LEFT JOIN customers c ON c.customer_id = o.customer_id
                        LEFT JOIN facilities f ON f.facility_id = o.facility_id
                        LEFT JOIN facilities df ON df.facility_id = o.destination_facility_id
                        WHERE sa.is_deleted = false AND o.is_deleted = false
                    LOOP
                        v_old := r.country_code;
                        v_is_hist := r.has_fulfillment OR r.order_status IN ('Allocated', 'PartiallyAllocated', 'InProgress', 'Completed', 'Cancelled');
                        v_cust_country := CASE WHEN r.linked_cust_id IS NOT NULL AND NOT COALESCE(r.cust_deleted, false) AND COALESCE(r.cust_active, false) AND r.cust_status = 'Normalized' THEN r.cust_country ELSE NULL END;
                        v_fac_country := CASE WHEN NOT COALESCE(r.fac_deleted, false) AND COALESCE(r.fac_active, false) AND r.fac_status = 'Normalized' THEN r.fac_country ELSE NULL END;
                        v_dest_fac_country := CASE WHEN r.order_type = 'StockTransferOrder' AND r.destination_facility_id IS NOT NULL AND NOT COALESCE(r.dest_fac_deleted, false) AND COALESCE(r.dest_fac_active, false) AND r.dest_fac_status = 'Normalized' THEN r.dest_fac_country ELSE NULL END;

                        IF (r.country_code IS NULL OR BTRIM(r.country_code) = '') AND r.linked_cust_id IS NOT NULL AND v_cust_country IS NULL THEN
                            SELECT false AS is_resolved, NULL::VARCHAR(3) AS target_code, 'PendingNormalization'::VARCHAR(30) AS norm_status, false AS was_inferred,
                                   'QuarantinedCustomerParent'::VARCHAR(120) AS inf_source,
                                   'Quarantined or unverified customer master cannot verify child shipping address country.'::VARCHAR(500) AS reason
                            INTO v_eval;
                        ELSE
                            IF r.order_type = 'StockTransferOrder' THEN
                                v_parent_hint := v_dest_fac_country;
                                v_hint_src := 'TransferDestinationFacility';
                            ELSE
                                v_parent_hint := v_cust_country;
                                v_hint_src := 'CustomerDefault';
                            END IF;

                            SELECT * INTO v_eval FROM pg_temp.mdm16_r04_resolve_country(
                                r.country_code, r.state_or_province, r.postal_code,
                                v_parent_hint, v_hint_src, NULL, false, true);
                        END IF;

                        UPDATE shipping_addresses
                        SET country_code = CASE WHEN NOT v_is_hist AND v_eval.is_resolved THEN v_eval.target_code ELSE country_code END,
                            country_normalization_status = v_eval.norm_status
                        WHERE shipping_address_id = r.shipping_address_id;

                        v_reason := CASE WHEN v_is_hist AND v_eval.is_resolved THEN LEFT('Immutable historical record preserved untouched; ' || v_eval.reason, 500)::VARCHAR(500) ELSE v_eval.reason END;
                        v_force_audit := (NOT v_is_hist AND v_eval.is_resolved AND (v_eval.target_code IS DISTINCT FROM v_old OR v_eval.was_inferred))
                                         OR (v_is_hist AND v_eval.is_resolved AND BTRIM(COALESCE(v_old, '')) <> COALESCE(v_eval.target_code, ''))
                                         OR NOT v_eval.is_resolved;
                        PERFORM pg_temp.mdm16_r04_reconcile_audit(
                            'ShippingAddress', r.shipping_address_id, 'CountryCode', v_old, v_eval.target_code,
                            v_eval.norm_status, v_is_hist, v_eval.was_inferred, v_eval.inf_source,
                            v_reason, r.facility_id, r.client_id, v_batch, v_force_audit);
                    END LOOP;

                    -- 5. ReturnShipments (Dual origin & destination with verified non-quarantined parents)
                    FOR r IN
                        SELECT rs.return_shipment_id, rs.client_id, rs.origin_facility_id, rs.destination_facility_id, rs.order_id,
                               rs.status, rs.label_generated_at_utc, rs.carrier_handover_at_utc,
                               rs.origin_country, rs.origin_state, rs.origin_postal_code,
                               rs.destination_country, rs.destination_state, rs.destination_postal_code,
                               ofac.country_code AS orig_fac_country, ofac.country_normalization_status AS orig_fac_status, ofac.is_active AS orig_fac_active, ofac.is_deleted AS orig_fac_deleted,
                               dfac.country_code AS dest_fac_country, dfac.country_normalization_status AS dest_fac_status, dfac.is_active AS dest_fac_active, dfac.is_deleted AS dest_fac_deleted,
                               sa.shipping_address_id AS order_addr_id, sa.country_code AS order_addr_country, sa.country_normalization_status AS order_addr_status
                        FROM return_shipments rs
                        LEFT JOIN facilities ofac ON ofac.facility_id = rs.origin_facility_id
                        LEFT JOIN facilities dfac ON dfac.facility_id = rs.destination_facility_id
                        LEFT JOIN shipping_addresses sa ON sa.order_id = rs.order_id AND sa.is_deleted = false
                    LOOP
                        v_is_hist := r.status <> 'Authorized' OR r.label_generated_at_utc IS NOT NULL OR r.carrier_handover_at_utc IS NOT NULL;
                        v_dest_fac_country := CASE WHEN NOT COALESCE(r.dest_fac_deleted, false) AND COALESCE(r.dest_fac_active, false) AND r.dest_fac_status = 'Normalized' THEN r.dest_fac_country ELSE NULL END;
                        v_fac_country := CASE WHEN NOT COALESCE(r.orig_fac_deleted, false) AND COALESCE(r.orig_fac_active, false) AND r.orig_fac_status = 'Normalized' THEN r.orig_fac_country ELSE NULL END;

                        IF r.origin_facility_id <> r.destination_facility_id
                           AND v_fac_country IS NOT NULL AND v_dest_fac_country IS NOT NULL
                           AND v_fac_country <> v_dest_fac_country
                           AND (r.origin_country IS NULL OR BTRIM(r.origin_country) = '') THEN
                            SELECT false AS is_resolved, NULL::VARCHAR(3) AS target_code, 'PendingNormalization'::VARCHAR(30) AS norm_status, false AS was_inferred,
                                   'CrossBorderReturnContext'::VARCHAR(120) AS inf_source,
                                   'Cross-border return shipment with blank OriginCountry cannot inherit DestinationFacility country.'::VARCHAR(500) AS reason
                            INTO v_orig;
                        ELSIF (r.origin_country IS NULL OR BTRIM(r.origin_country) = '') AND r.order_addr_id IS NOT NULL AND r.order_addr_status <> 'Normalized' THEN
                            SELECT false AS is_resolved, NULL::VARCHAR(3) AS target_code, 'PendingNormalization'::VARCHAR(30) AS norm_status, false AS was_inferred,
                                   'QuarantinedOrderAddress'::VARCHAR(120) AS inf_source,
                                   'Quarantined original order shipping address cannot verify return OriginCountry.'::VARCHAR(500) AS reason
                            INTO v_orig;
                        ELSE
                            v_parent_hint := CASE WHEN r.order_addr_status = 'Normalized' THEN r.order_addr_country ELSE NULL END;
                            SELECT * INTO v_orig FROM pg_temp.mdm16_r04_resolve_country(
                                r.origin_country, r.origin_state, r.origin_postal_code,
                                v_parent_hint, 'OriginalOrderShippingAddress', NULL, false, true);
                        END IF;

                        SELECT * INTO v_dest FROM pg_temp.mdm16_r04_resolve_country(
                            r.destination_country, r.destination_state, r.destination_postal_code,
                            v_dest_fac_country, 'ReturnDestinationFacility', NULL, false, true);

                        UPDATE return_shipments
                        SET origin_country = CASE WHEN NOT v_is_hist AND v_orig.is_resolved THEN v_orig.target_code ELSE origin_country END,
                            destination_country = CASE WHEN NOT v_is_hist AND v_dest.is_resolved THEN v_dest.target_code ELSE destination_country END,
                            country_normalization_status = CASE WHEN v_orig.norm_status = 'Normalized' AND v_dest.norm_status = 'Normalized' THEN 'Normalized' ELSE 'PendingNormalization' END
                        WHERE return_shipment_id = r.return_shipment_id;

                        v_reason := CASE WHEN v_is_hist AND v_orig.is_resolved THEN LEFT('Immutable historical record preserved untouched; ' || v_orig.reason, 500)::VARCHAR(500) ELSE v_orig.reason END;
                        v_force_audit := (NOT v_is_hist AND v_orig.is_resolved AND (v_orig.target_code IS DISTINCT FROM r.origin_country OR v_orig.was_inferred))
                                         OR (v_is_hist AND v_orig.is_resolved AND BTRIM(COALESCE(r.origin_country, '')) <> COALESCE(v_orig.target_code, ''))
                                         OR NOT v_orig.is_resolved;
                        PERFORM pg_temp.mdm16_r04_reconcile_audit(
                            'ReturnShipment', r.return_shipment_id, 'OriginCountry', r.origin_country, v_orig.target_code,
                            v_orig.norm_status, v_is_hist, v_orig.was_inferred, v_orig.inf_source,
                            v_reason, r.origin_facility_id, r.client_id, v_batch, v_force_audit);

                        v_reason := CASE WHEN v_is_hist AND v_dest.is_resolved THEN LEFT('Immutable historical record preserved untouched; ' || v_dest.reason, 500)::VARCHAR(500) ELSE v_dest.reason END;
                        v_force_audit := (NOT v_is_hist AND v_dest.is_resolved AND (v_dest.target_code IS DISTINCT FROM r.destination_country OR v_dest.was_inferred))
                                         OR (v_is_hist AND v_dest.is_resolved AND BTRIM(COALESCE(r.destination_country, '')) <> COALESCE(v_dest.target_code, ''))
                                         OR NOT v_dest.is_resolved;
                        PERFORM pg_temp.mdm16_r04_reconcile_audit(
                            'ReturnShipment', r.return_shipment_id, 'DestinationCountry', r.destination_country, v_dest.target_code,
                            v_dest.norm_status, v_is_hist, v_dest.was_inferred, v_dest.inf_source,
                            v_reason, r.destination_facility_id, r.client_id, v_batch, v_force_audit);
                    END LOOP;

                    -- 6. CarrierContractualSlaPolicy
                    FOR r IN SELECT policy_id, origin_facility_id, client_id, destination_country, destination_state_or_region, destination_postal_prefix, is_active FROM carrier_contractual_sla_policies LOOP
                        v_old := r.destination_country;
                        v_is_hist := NOT r.is_active;
                        SELECT * INTO v_eval FROM pg_temp.mdm16_r04_resolve_country(
                            r.destination_country, r.destination_state_or_region, r.destination_postal_prefix,
                            NULL, 'PolicyDestinationGeo', NULL, false, false);

                        UPDATE carrier_contractual_sla_policies
                        SET destination_country = CASE WHEN NOT v_is_hist AND v_eval.is_resolved THEN v_eval.target_code ELSE destination_country END,
                            country_normalization_status = v_eval.norm_status
                        WHERE policy_id = r.policy_id;

                        v_reason := CASE WHEN v_is_hist AND v_eval.is_resolved THEN LEFT('Immutable historical record preserved untouched; ' || v_eval.reason, 500)::VARCHAR(500) ELSE v_eval.reason END;
                        v_force_audit := (NOT v_is_hist AND v_eval.is_resolved AND (v_eval.target_code IS DISTINCT FROM v_old OR v_eval.was_inferred))
                                         OR (v_is_hist AND v_eval.is_resolved AND BTRIM(COALESCE(v_old, '')) <> COALESCE(v_eval.target_code, ''))
                                         OR NOT v_eval.is_resolved;
                        PERFORM pg_temp.mdm16_r04_reconcile_audit(
                            'CarrierContractualSlaPolicy', r.policy_id, 'DestinationCountry', v_old, v_eval.target_code,
                            v_eval.norm_status, v_is_hist, v_eval.was_inferred, v_eval.inf_source,
                            v_reason, r.origin_facility_id, r.client_id, v_batch, v_force_audit);
                    END LOOP;

                    -- 7. CarrierSlaComplianceRecord
                    FOR r IN SELECT compliance_record_id, facility_id, client_id, destination_country, destination_state_or_region, destination_postal_prefix FROM carrier_sla_compliance_records LOOP
                        v_old := r.destination_country;
                        SELECT * INTO v_eval FROM pg_temp.mdm16_r04_resolve_country(
                            r.destination_country, r.destination_state_or_region, r.destination_postal_prefix,
                            NULL, 'SlaHistoricalGeo', NULL, false, false);

                        UPDATE carrier_sla_compliance_records
                        SET country_normalization_status = v_eval.norm_status
                        WHERE compliance_record_id = r.compliance_record_id;

                        v_reason := CASE WHEN v_eval.is_resolved THEN LEFT('Immutable historical record preserved untouched; ' || v_eval.reason, 500)::VARCHAR(500) ELSE v_eval.reason END;
                        v_force_audit := (v_eval.is_resolved AND BTRIM(COALESCE(v_old, '')) <> COALESCE(v_eval.target_code, '')) OR NOT v_eval.is_resolved;
                        PERFORM pg_temp.mdm16_r04_reconcile_audit(
                            'CarrierSlaComplianceRecord', r.compliance_record_id, 'DestinationCountry', v_old, v_eval.target_code,
                            v_eval.norm_status, true, v_eval.was_inferred, v_eval.inf_source,
                            v_reason, r.facility_id, r.client_id, v_batch, v_force_audit);
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "initial_correlation_batch_id",
                table: "country_normalization_audits");

            migrationBuilder.DropColumn(
                name: "initial_recorded_at_utc",
                table: "country_normalization_audits");

            migrationBuilder.AlterColumn<string>(
                name: "country_code",
                table: "facilities",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USA",
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3,
                oldDefaultValue: "");
        }
    }
}
