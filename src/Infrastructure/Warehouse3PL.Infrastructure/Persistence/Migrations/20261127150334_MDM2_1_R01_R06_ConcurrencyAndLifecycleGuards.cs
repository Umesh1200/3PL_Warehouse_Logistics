using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM2_1_R01_R06_ConcurrencyAndLifecycleGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Align seeded global StagingLane hierarchy rules with Location.DefaultStorableForType(StagingLane) = true
            // (StagingLanes hold staged received inventory and are storable leaves by default).
            migrationBuilder.Sql("""
                UPDATE location_hierarchy_rules
                SET is_storable_leaf = true,
                    config_revision = config_revision + 1
                WHERE facility_id IS NULL
                  AND child_location_type = 'StagingLane'
                  AND is_storable_leaf = false;
                """);

            // MDM21-R01 / R02 / R04 Fail-closed corrective preflight guards:
            // 1. All 6 StockOnHand quantity buckets in non-storable non-receiving/staging locations
            // 2. Existing non-deleted locations must comply with effective location_hierarchy_rules (MaxDepth & IsStorableLeaf)
            // 3. No active non-deleted location may reside beneath an inactive or decommissioned parent
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    v_count integer;
                    v_sample text;
                BEGIN
                    -- 1. R01 Complete 6-bucket stock check in non-storable, non-receiving/staging locations
                    SELECT COUNT(*), MIN(l.location_code) INTO v_count, v_sample
                    FROM stock_on_hand s
                    JOIN locations l ON l.location_id = s.location_id
                    WHERE (s.qty_available + s.qty_allocated + s.qty_reserved + s.qty_quarantined + s.qty_damaged + s.qty_on_hold) > 0
                      AND l.is_storable = false
                      AND l.is_receiving = false
                      AND l.location_type NOT IN ('StagingLane', 'DockDoor', 'PackingStation');
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_R01_PREFLIGHT_FAILED: Found % stock_on_hand row(s) across 6 quantity buckets in non-storable location(s) (e.g. %).', v_count, v_sample;
                    END IF;

                    -- 2. R02 Hierarchy rule compliance check (facility override -> global default)
                    IF EXISTS (SELECT 1 FROM location_hierarchy_rules) THEN
                        SELECT COUNT(*), MIN(l.location_code) INTO v_count, v_sample
                        FROM locations l
                        LEFT JOIN locations p ON p.location_id = l.parent_location_id
                        LEFT JOIN LATERAL (
                            SELECT r.max_depth, r.is_storable_leaf, r.is_active
                            FROM location_hierarchy_rules r
                            WHERE (r.facility_id = l.facility_id OR r.facility_id IS NULL)
                              AND r.parent_location_type IS NOT DISTINCT FROM p.location_type
                              AND r.child_location_type = l.location_type
                            ORDER BY CASE WHEN r.facility_id IS NOT NULL THEN 0 ELSE 1 END
                            LIMIT 1
                        ) eff ON true
                        WHERE l.is_deleted = false
                          AND (
                              eff.is_active IS NULL
                              OR eff.is_active = false
                              OR l.hierarchy_level > eff.max_depth
                              OR (l.is_storable = true AND eff.is_storable_leaf = false)
                          );
                        IF v_count > 0 THEN
                            RAISE EXCEPTION 'MDM21_R02_PREFLIGHT_FAILED: Found % location(s) violating effective location_hierarchy_rules (MaxDepth/IsStorableLeaf/IsActive), e.g. %.', v_count, v_sample;
                        END IF;
                    END IF;

                    -- 3. R04 Active child beneath inactive or decommissioned parent check
                    SELECT COUNT(*), MIN(c.location_code) INTO v_count, v_sample
                    FROM locations c
                    JOIN locations p ON p.location_id = c.parent_location_id
                    WHERE c.is_deleted = false
                      AND c.status = 'Active'
                      AND (p.status <> 'Active' OR p.is_active = false OR p.is_deleted = true);
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_R04_PREFLIGHT_FAILED: Found % active location(s) beneath an inactive or decommissioned parent (e.g. %).', v_count, v_sample;
                    END IF;
                END $$;
                """);

            // MDM21-R04: Upgrade PostgreSQL cycle-prevention trigger to acquire the canonical per-facility
            // advisory transaction lock before walking ancestry so even direct-SQL concurrent opposite reparenting
            // (A->B vs B->A) is serialized at the database boundary.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION fn_locations_prevent_cycle()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    v_cycle_found boolean := false;
                    v_max_depth integer := 1;
                    v_lock_key text;
                BEGIN
                    IF NEW.parent_location_id IS NULL THEN
                        RETURN NEW;
                    END IF;

                    IF NEW.parent_location_id = NEW.location_id THEN
                        RAISE EXCEPTION 'LOCATION_HIERARCHY_CYCLE: Location % cannot reference itself as parent.', NEW.location_id;
                    END IF;

                    v_lock_key := 'FACILITY_HIERARCHY:' || replace(NEW.facility_id::text, '-', '');
                    PERFORM pg_advisory_xact_lock(hashtextextended(v_lock_key, 0::bigint));

                    WITH RECURSIVE ancestors AS (
                        SELECT location_id, parent_location_id, 2 AS depth
                        FROM locations
                        WHERE location_id = NEW.parent_location_id
                        UNION ALL
                        SELECT p.location_id, p.parent_location_id, a.depth + 1
                        FROM ancestors a
                        JOIN locations p ON p.location_id = a.parent_location_id
                        WHERE a.depth <= 11
                    )
                    SELECT
                        COALESCE(BOOL_OR(location_id = NEW.location_id OR parent_location_id = NEW.location_id), false),
                        COALESCE(MAX(depth), 2)
                    INTO v_cycle_found, v_max_depth
                    FROM ancestors;

                    IF v_cycle_found THEN
                        RAISE EXCEPTION 'LOCATION_HIERARCHY_CYCLE: Reparenting location % under % would introduce a cycle.', NEW.location_id, NEW.parent_location_id;
                    END IF;

                    IF v_max_depth > 10 THEN
                        RAISE EXCEPTION 'LOCATION_HIERARCHY_MAX_DEPTH: Location % exceeds maximum hierarchy depth of 10.', NEW.location_id;
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE location_hierarchy_rules
                SET is_storable_leaf = false
                WHERE rule_id IN (
                    '21000000-0000-0000-0000-000000000011',
                    '21000000-0000-0000-0000-000000000109',
                    '21000000-0000-0000-0000-000000000118'
                );
                """);

            // Restore original MDM-2.1 fn_locations_prevent_cycle without touching tables, IDs, or FKs
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION fn_locations_prevent_cycle()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    v_cycle_found boolean := false;
                    v_max_depth integer := 1;
                BEGIN
                    IF NEW.parent_location_id IS NULL THEN
                        RETURN NEW;
                    END IF;

                    IF NEW.parent_location_id = NEW.location_id THEN
                        RAISE EXCEPTION 'LOCATION_HIERARCHY_CYCLE: Location % cannot reference itself as parent.', NEW.location_id;
                    END IF;

                    WITH RECURSIVE ancestors AS (
                        SELECT location_id, parent_location_id, 2 AS depth
                        FROM locations
                        WHERE location_id = NEW.parent_location_id
                        UNION ALL
                        SELECT p.location_id, p.parent_location_id, a.depth + 1
                        FROM ancestors a
                        JOIN locations p ON p.location_id = a.parent_location_id
                        WHERE a.depth <= 11
                    )
                    SELECT
                        COALESCE(BOOL_OR(location_id = NEW.location_id OR parent_location_id = NEW.location_id), false),
                        COALESCE(MAX(depth), 2)
                    INTO v_cycle_found, v_max_depth
                    FROM ancestors;

                    IF v_cycle_found THEN
                        RAISE EXCEPTION 'LOCATION_HIERARCHY_CYCLE: Reparenting location % under % would introduce a cycle.', NEW.location_id, NEW.parent_location_id;
                    END IF;

                    IF v_max_depth > 10 THEN
                        RAISE EXCEPTION 'LOCATION_HIERARCHY_MAX_DEPTH: Location % exceeds maximum hierarchy depth of 10.', NEW.location_id;
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);
        }
    }
}
