using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM2_1_WarehouseMasterAndLocationHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A07 Fail-closed pre-cutover validation: abort cleanly before any schema mutation
            // if legacy rows contain cycles, cross-facility links, orphans, invalid bounds, or normalized code collisions.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    v_count integer;
                BEGIN
                    -- 1. Self-parent cycle check
                    SELECT COUNT(*) INTO v_count FROM locations WHERE parent_location_id = location_id;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % self-parenting location(s) in locations.', v_count;
                    END IF;

                    -- 2. Orphaned parent_location_id check
                    SELECT COUNT(*) INTO v_count
                    FROM locations c
                    LEFT JOIN locations p ON p.location_id = c.parent_location_id
                    WHERE c.parent_location_id IS NOT NULL AND p.location_id IS NULL;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % location(s) with orphaned parent_location_id.', v_count;
                    END IF;

                    -- 3. Cross-facility parent_location_id check
                    SELECT COUNT(*) INTO v_count
                    FROM locations c
                    JOIN locations p ON p.location_id = c.parent_location_id
                    WHERE c.facility_id <> p.facility_id;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % cross-facility parent location reference(s).', v_count;
                    END IF;

                    -- 4. Multi-hop cycle or excessive depth (>10) check
                    WITH RECURSIVE walk AS (
                        SELECT location_id, parent_location_id, ARRAY[location_id] AS visited, 1 AS depth, false AS has_cycle
                        FROM locations
                        WHERE parent_location_id IS NOT NULL
                        UNION ALL
                        SELECT w.location_id, p.parent_location_id, w.visited || p.location_id, w.depth + 1,
                               (p.location_id = ANY(w.visited)) AS has_cycle
                        FROM walk w
                        JOIN locations p ON p.location_id = w.parent_location_id
                        WHERE NOT w.has_cycle AND w.depth <= 12
                    )
                    SELECT COUNT(*) INTO v_count FROM walk WHERE has_cycle OR depth > 10;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % location hierarchy cycle or depth > 10 violation(s).', v_count;
                    END IF;

                    -- 5. Orphaned or cross-facility zone_id check
                    SELECT COUNT(*) INTO v_count
                    FROM locations l
                    LEFT JOIN warehouse_zones z ON z.zone_id = l.zone_id
                    WHERE l.zone_id IS NOT NULL AND (z.zone_id IS NULL OR z.facility_id <> l.facility_id);
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % orphaned or cross-facility location zone reference(s).', v_count;
                    END IF;

                    -- 6. Normalized location_code collisions within facility
                    SELECT COUNT(*) INTO v_count FROM (
                        SELECT facility_id, UPPER(BTRIM(location_code))
                        FROM locations
                        GROUP BY facility_id, UPPER(BTRIM(location_code))
                        HAVING COUNT(*) > 1
                    ) dup;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % normalized location_code collision(s) within a facility.', v_count;
                    END IF;

                    -- 7. Normalized barcode collisions within facility
                    SELECT COUNT(*) INTO v_count FROM (
                        SELECT facility_id, UPPER(BTRIM(barcode))
                        FROM locations
                        GROUP BY facility_id, UPPER(BTRIM(barcode))
                        HAVING COUNT(*) > 1
                    ) dup;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % normalized barcode collision(s) within a facility.', v_count;
                    END IF;

                    -- 8. Normalized zone_code collisions within facility
                    SELECT COUNT(*) INTO v_count FROM (
                        SELECT facility_id, UPPER(BTRIM(zone_code))
                        FROM warehouse_zones
                        GROUP BY facility_id, UPPER(BTRIM(zone_code))
                        HAVING COUNT(*) > 1
                    ) dup;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % normalized zone_code collision(s) within a facility.', v_count;
                    END IF;

                    -- 9. Invalid zone temperature bounds
                    SELECT COUNT(*) INTO v_count
                    FROM warehouse_zones
                    WHERE temp_min_c IS NOT NULL AND temp_max_c IS NOT NULL AND temp_min_c > temp_max_c;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % warehouse_zones row(s) with temp_min_c > temp_max_c.', v_count;
                    END IF;

                    -- 10. Normalized dock_doors.door_number collisions within facility
                    SELECT COUNT(*) INTO v_count FROM (
                        SELECT facility_id, UPPER(BTRIM(door_number))
                        FROM dock_doors
                        GROUP BY facility_id, UPPER(BTRIM(door_number))
                        HAVING COUNT(*) > 1
                    ) dup;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % normalized dock door_number collision(s) within a facility.', v_count;
                    END IF;

                    -- 11. Normalized packaging_materials.code collisions (facility-wide and client-specific)
                    SELECT COUNT(*) INTO v_count FROM (
                        SELECT facility_id, UPPER(BTRIM(code))
                        FROM packaging_materials
                        WHERE client_id IS NULL
                        GROUP BY facility_id, UPPER(BTRIM(code))
                        HAVING COUNT(*) > 1
                    ) dup;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % facility-wide packaging_materials code collision(s).', v_count;
                    END IF;

                    SELECT COUNT(*) INTO v_count FROM (
                        SELECT facility_id, client_id, UPPER(BTRIM(code))
                        FROM packaging_materials
                        WHERE client_id IS NOT NULL
                        GROUP BY facility_id, client_id, UPPER(BTRIM(code))
                        HAVING COUNT(*) > 1
                    ) dup;
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % client-specific packaging_materials code collision(s).', v_count;
                    END IF;

                    -- 12. Active stock residing in purely structural non-storable nodes
                    SELECT COUNT(*) INTO v_count
                    FROM stock_on_hand s
                    JOIN locations l ON l.location_id = s.location_id
                    WHERE (s.qty_available + s.qty_allocated + s.qty_quarantined) > 0
                      AND l.location_type IN ('Facility', 'Zone', 'Aisle', 'Rack', 'ShelfLevel', 'Mezzanine');
                    IF v_count > 0 THEN
                        RAISE EXCEPTION 'MDM21_PREFLIGHT_FAILED: Found % active stock_on_hand row(s) in structural non-storable location nodes.', v_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_locations_locations_parent_location_id",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "fk_locations_warehouse_zones_zone_id",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_warehouse_zones_facility_id_zone_code",
                table: "warehouse_zones");

            migrationBuilder.DropIndex(
                name: "ix_locations_facility_id_barcode",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_locations_facility_id_location_code",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_dock_doors_facility_id_door_number",
                table: "dock_doors");

            migrationBuilder.AddColumn<long>(
                name: "config_revision",
                table: "warehouse_zones",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<Guid>(
                name: "dedicated_client_id",
                table: "warehouse_zones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_zone_code",
                table: "warehouse_zones",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "remarks",
                table: "warehouse_zones",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "config_revision",
                table: "packaging_materials",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "currency_code",
                table: "packaging_materials",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_code",
                table: "packaging_materials",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "config_revision",
                table: "locations",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<int>(
                name: "hierarchy_level",
                table: "locations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "hierarchy_path",
                table: "locations",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_storable",
                table: "locations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "location_name",
                table: "locations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "normalized_barcode",
                table: "locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "normalized_location_code",
                table: "locations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "remarks",
                table: "locations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "locations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<long>(
                name: "config_revision",
                table: "dock_doors",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                table: "dock_doors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_trailer_length_cm",
                table: "dock_doors",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_door_number",
                table: "dock_doors",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Deterministic backfill of normalized codes, canonical metric dock length, location status/storability,
            // and recursive hierarchy_level / ID-based hierarchy_path BEFORE applying unique indexes & constraints.
            migrationBuilder.Sql("""
                UPDATE warehouse_zones
                SET normalized_zone_code = UPPER(BTRIM(zone_code));

                UPDATE packaging_materials
                SET normalized_code = UPPER(BTRIM(code));

                UPDATE dock_doors
                SET normalized_door_number = UPPER(BTRIM(door_number)),
                    max_trailer_length_cm = CASE
                        WHEN max_trailer_length_feet IS NOT NULL AND max_trailer_length_feet > 0
                        THEN ROUND((max_trailer_length_feet * 30.48)::numeric, 2)
                        ELSE NULL
                    END;

                UPDATE locations
                SET normalized_location_code = UPPER(BTRIM(location_code)),
                    normalized_barcode = UPPER(BTRIM(barcode)),
                    location_name = COALESCE(NULLIF(BTRIM(location_name), ''), location_code),
                    status = CASE WHEN is_active THEN 'Active' ELSE 'Inactive' END,
                    is_storable = CASE
                        WHEN location_type IN ('Facility', 'Zone', 'Aisle', 'Rack', 'ShelfLevel', 'Mezzanine', 'DockDoor', 'StagingLane', 'PackingStation')
                        THEN false
                        ELSE true
                    END;

                WITH RECURSIVE loc_tree AS (
                    SELECT location_id,
                           1 AS computed_level,
                           '/' || replace(location_id::text, '-', '') || '/' AS computed_path
                    FROM locations
                    WHERE parent_location_id IS NULL
                    UNION ALL
                    SELECT c.location_id,
                           t.computed_level + 1 AS computed_level,
                           t.computed_path || replace(c.location_id::text, '-', '') || '/' AS computed_path
                    FROM locations c
                    JOIN loc_tree t ON c.parent_location_id = t.location_id
                    WHERE t.computed_level < 10
                )
                UPDATE locations l
                SET hierarchy_level = t.computed_level,
                    hierarchy_path = t.computed_path
                FROM loc_tree t
                WHERE l.location_id = t.location_id;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_warehouse_zones_zone_id_facility_id",
                table: "warehouse_zones",
                columns: new[] { "zone_id", "facility_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_locations_location_id_facility_id",
                table: "locations",
                columns: new[] { "location_id", "facility_id" });

            migrationBuilder.CreateTable(
                name: "location_equipment_requirements",
                columns: table => new
                {
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipment_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    config_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_equipment_requirements", x => x.requirement_id);
                    table.ForeignKey(
                        name: "fk_location_equipment_requirements_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_equipment_requirements_locations_location_id_facil",
                        columns: x => new { x.location_id, x.facility_id },
                        principalTable: "locations",
                        principalColumns: new[] { "location_id", "facility_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "location_hierarchy_rules",
                columns: table => new
                {
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_location_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    child_location_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    max_depth = table.Column<int>(type: "integer", nullable: false, defaultValue: 6),
                    is_storable_leaf = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    config_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_hierarchy_rules", x => x.rule_id);
                    table.CheckConstraint("ck_location_hierarchy_rules_max_depth", "max_depth >= 1 AND max_depth <= 10");
                    table.ForeignKey(
                        name: "fk_location_hierarchy_rules_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "location_operational_profiles",
                columns: table => new
                {
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    supports_scale = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    supports_label_printer = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    supports_conveyor = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    max_concurrent_sessions = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    default_staging_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    config_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_operational_profiles", x => x.location_id);
                    table.CheckConstraint("ck_location_operational_profiles_max_sessions", "max_concurrent_sessions >= 1");
                    table.ForeignKey(
                        name: "fk_location_operational_profiles_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_operational_profiles_locations_default_staging_loc",
                        columns: x => new { x.default_staging_location_id, x.facility_id },
                        principalTable: "locations",
                        principalColumns: new[] { "location_id", "facility_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_operational_profiles_locations_location_id_facilit",
                        columns: x => new { x.location_id, x.facility_id },
                        principalTable: "locations",
                        principalColumns: new[] { "location_id", "facility_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "location_storage_profiles",
                columns: table => new
                {
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dedicated_client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    max_handling_units = table.Column<int>(type: "integer", nullable: true),
                    allow_mixed_sku = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    allow_mixed_lot = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_putaway_allowed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    config_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_location_storage_profiles", x => x.location_id);
                    table.CheckConstraint("ck_location_storage_profiles_max_hu", "max_handling_units IS NULL OR max_handling_units > 0");
                    table.ForeignKey(
                        name: "fk_location_storage_profiles_clients_dedicated_client_id",
                        column: x => x.dedicated_client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_storage_profiles_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_location_storage_profiles_locations_location_id_facility_id",
                        columns: x => new { x.location_id, x.facility_id },
                        principalTable: "locations",
                        principalColumns: new[] { "location_id", "facility_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_equipment",
                columns: table => new
                {
                    equipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipment_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_equipment_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    equipment_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    max_lift_height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    max_load_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    requires_certification = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    config_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouse_equipment", x => x.equipment_id);
                    table.ForeignKey(
                        name: "fk_warehouse_equipment_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_zones_dedicated_client_id",
                table: "warehouse_zones",
                column: "dedicated_client_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_zones_facility_normalized_code",
                table: "warehouse_zones",
                columns: new[] { "facility_id", "normalized_zone_code" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_warehouse_zones_temp_bounds",
                table: "warehouse_zones",
                sql: "temp_min_c IS NULL OR temp_max_c IS NULL OR temp_min_c <= temp_max_c");

            migrationBuilder.CreateIndex(
                name: "ix_packaging_materials_client_code",
                table: "packaging_materials",
                columns: new[] { "facility_id", "client_id", "normalized_code" },
                unique: true,
                filter: "client_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_packaging_materials_facility_wide_code",
                table: "packaging_materials",
                columns: new[] { "facility_id", "normalized_code" },
                unique: true,
                filter: "client_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_packaging_materials_cost_non_negative",
                table: "packaging_materials",
                sql: "cost_per_unit IS NULL OR cost_per_unit >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_packaging_materials_positive_dims",
                table: "packaging_materials",
                sql: "length_cm > 0 AND width_cm > 0 AND height_cm > 0 AND max_weight_kg > 0 AND tare_weight_kg >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_locations_facility_hierarchy_path",
                table: "locations",
                columns: new[] { "facility_id", "hierarchy_path" });

            migrationBuilder.CreateIndex(
                name: "ix_locations_facility_normalized_barcode",
                table: "locations",
                columns: new[] { "facility_id", "normalized_barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_facility_normalized_code",
                table: "locations",
                columns: new[] { "facility_id", "normalized_location_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_facility_status_storable",
                table: "locations",
                columns: new[] { "facility_id", "status", "is_storable" });

            migrationBuilder.CreateIndex(
                name: "ix_locations_parent_location_id_facility_id",
                table: "locations",
                columns: new[] { "parent_location_id", "facility_id" });

            migrationBuilder.CreateIndex(
                name: "ix_locations_zone_id_facility_id",
                table: "locations",
                columns: new[] { "zone_id", "facility_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_locations_hierarchy_level",
                table: "locations",
                sql: "hierarchy_level >= 1 AND hierarchy_level <= 10");

            migrationBuilder.AddCheckConstraint(
                name: "ck_locations_no_self_parent",
                table: "locations",
                sql: "parent_location_id IS NULL OR parent_location_id <> location_id");

            migrationBuilder.CreateIndex(
                name: "ix_dock_doors_facility_normalized_door_number",
                table: "dock_doors",
                columns: new[] { "facility_id", "normalized_door_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dock_doors_location_id",
                table: "dock_doors",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_dock_doors_location_id_facility_id",
                table: "dock_doors",
                columns: new[] { "location_id", "facility_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_dock_doors_max_trailer_length_cm",
                table: "dock_doors",
                sql: "max_trailer_length_cm IS NULL OR max_trailer_length_cm > 0");

            migrationBuilder.CreateIndex(
                name: "ix_location_equipment_requirements_facility_id",
                table: "location_equipment_requirements",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_equipment_requirements_location_id_facility_id",
                table: "location_equipment_requirements",
                columns: new[] { "location_id", "facility_id" });

            migrationBuilder.CreateIndex(
                name: "uq_location_equipment_requirements_loc_type",
                table: "location_equipment_requirements",
                columns: new[] { "location_id", "equipment_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_loc_hier_rules_facility_root",
                table: "location_hierarchy_rules",
                columns: new[] { "facility_id", "child_location_type" },
                unique: true,
                filter: "facility_id IS NOT NULL AND parent_location_type IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_loc_hier_rules_facility_transition",
                table: "location_hierarchy_rules",
                columns: new[] { "facility_id", "parent_location_type", "child_location_type" },
                unique: true,
                filter: "facility_id IS NOT NULL AND parent_location_type IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_loc_hier_rules_global_root",
                table: "location_hierarchy_rules",
                column: "child_location_type",
                unique: true,
                filter: "facility_id IS NULL AND parent_location_type IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_loc_hier_rules_global_transition",
                table: "location_hierarchy_rules",
                columns: new[] { "parent_location_type", "child_location_type" },
                unique: true,
                filter: "facility_id IS NULL AND parent_location_type IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_location_operational_profiles_default_staging_location_id_f",
                table: "location_operational_profiles",
                columns: new[] { "default_staging_location_id", "facility_id" });

            migrationBuilder.CreateIndex(
                name: "ix_location_operational_profiles_facility_id",
                table: "location_operational_profiles",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_operational_profiles_location_id_facility_id",
                table: "location_operational_profiles",
                columns: new[] { "location_id", "facility_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_location_storage_profiles_dedicated_client_id",
                table: "location_storage_profiles",
                column: "dedicated_client_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_storage_profiles_facility_id",
                table: "location_storage_profiles",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_storage_profiles_location_id_facility_id",
                table: "location_storage_profiles",
                columns: new[] { "location_id", "facility_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_equipment_facility_id",
                table: "warehouse_equipment",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "uq_warehouse_equipment_fac_norm_code",
                table: "warehouse_equipment",
                columns: new[] { "facility_id", "normalized_equipment_code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_dock_doors_locations_location_id_facility_id",
                table: "dock_doors",
                columns: new[] { "location_id", "facility_id" },
                principalTable: "locations",
                principalColumns: new[] { "location_id", "facility_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_locations_locations_parent_location_id_facility_id",
                table: "locations",
                columns: new[] { "parent_location_id", "facility_id" },
                principalTable: "locations",
                principalColumns: new[] { "location_id", "facility_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_locations_warehouse_zones_zone_id_facility_id",
                table: "locations",
                columns: new[] { "zone_id", "facility_id" },
                principalTable: "warehouse_zones",
                principalColumns: new[] { "zone_id", "facility_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_warehouse_zones_clients_dedicated_client_id",
                table: "warehouse_zones",
                column: "dedicated_client_id",
                principalTable: "clients",
                principalColumn: "client_id",
                onDelete: ReferentialAction.Restrict);

            // A02 Database-layer multi-hop cycle-prevention trigger on locations
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

                DROP TRIGGER IF EXISTS trg_locations_prevent_cycle ON locations;
                CREATE TRIGGER trg_locations_prevent_cycle
                    BEFORE INSERT OR UPDATE OF parent_location_id ON locations
                    FOR EACH ROW
                    EXECUTE FUNCTION fn_locations_prevent_cycle();
                """);

            // A04 Default global location hierarchy rules supporting both compact (Kathmandu) and deep (Chicago) topologies
            migrationBuilder.Sql("""
                INSERT INTO location_hierarchy_rules
                    (rule_id, facility_id, parent_location_type, child_location_type, max_depth, is_storable_leaf, is_active, config_revision, created_at)
                VALUES
                    -- Global root rules (parent_location_type IS NULL)
                    ('21000000-0000-0000-0000-000000000001', NULL, NULL, 'Facility',       10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000002', NULL, NULL, 'Zone',           10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000003', NULL, NULL, 'Mezzanine',      10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000004', NULL, NULL, 'Aisle',          10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000005', NULL, NULL, 'Rack',           10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000006', NULL, NULL, 'ShelfLevel',     10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000007', NULL, NULL, 'Bin',            10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000008', NULL, NULL, 'BulkFloor',      10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000009', NULL, NULL, 'Cantilever',     10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000010', NULL, NULL, 'DockDoor',       10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000011', NULL, NULL, 'StagingLane',    10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000012', NULL, NULL, 'PackingStation', 10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    -- Global transition rules
                    ('21000000-0000-0000-0000-000000000101', NULL, 'Facility',   'Zone',           10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000102', NULL, 'Facility',   'Mezzanine',      10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000103', NULL, 'Facility',   'Aisle',          10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000104', NULL, 'Facility',   'Rack',           10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000105', NULL, 'Facility',   'Bin',            10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000106', NULL, 'Facility',   'BulkFloor',      10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000107', NULL, 'Facility',   'Cantilever',     10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000108', NULL, 'Facility',   'DockDoor',       10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000109', NULL, 'Facility',   'StagingLane',    10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000110', NULL, 'Facility',   'PackingStation', 10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000111', NULL, 'Zone',       'Mezzanine',      10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000112', NULL, 'Zone',       'Aisle',          10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000113', NULL, 'Zone',       'Rack',           10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000114', NULL, 'Zone',       'Bin',            10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000115', NULL, 'Zone',       'BulkFloor',      10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000116', NULL, 'Zone',       'Cantilever',     10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000117', NULL, 'Zone',       'DockDoor',       10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000118', NULL, 'Zone',       'StagingLane',    10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000119', NULL, 'Zone',       'PackingStation', 10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000120', NULL, 'Mezzanine',  'Aisle',          10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000121', NULL, 'Mezzanine',  'Rack',           10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000122', NULL, 'Mezzanine',  'ShelfLevel',     10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000123', NULL, 'Mezzanine',  'Bin',            10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000124', NULL, 'Mezzanine',  'BulkFloor',      10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000125', NULL, 'Mezzanine',  'PackingStation', 10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000126', NULL, 'Aisle',      'Rack',           10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000127', NULL, 'Aisle',      'ShelfLevel',     10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000128', NULL, 'Aisle',      'Bin',            10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000129', NULL, 'Aisle',      'Cantilever',     10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000130', NULL, 'Aisle',      'BulkFloor',      10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000131', NULL, 'Rack',       'ShelfLevel',     10, false, true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000132', NULL, 'Rack',       'Bin',            10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000133', NULL, 'Rack',       'Cantilever',     10, true,  true, 1, NOW() AT TIME ZONE 'UTC'),
                    ('21000000-0000-0000-0000-000000000134', NULL, 'ShelfLevel', 'Bin',            10, true,  true, 1, NOW() AT TIME ZONE 'UTC')
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_locations_prevent_cycle ON locations;
                DROP FUNCTION IF EXISTS fn_locations_prevent_cycle();
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_dock_doors_locations_location_id_facility_id",
                table: "dock_doors");

            migrationBuilder.DropForeignKey(
                name: "fk_locations_locations_parent_location_id_facility_id",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "fk_locations_warehouse_zones_zone_id_facility_id",
                table: "locations");

            migrationBuilder.DropForeignKey(
                name: "fk_warehouse_zones_clients_dedicated_client_id",
                table: "warehouse_zones");

            migrationBuilder.DropTable(
                name: "location_equipment_requirements");

            migrationBuilder.DropTable(
                name: "location_hierarchy_rules");

            migrationBuilder.DropTable(
                name: "location_operational_profiles");

            migrationBuilder.DropTable(
                name: "location_storage_profiles");

            migrationBuilder.DropTable(
                name: "warehouse_equipment");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_warehouse_zones_zone_id_facility_id",
                table: "warehouse_zones");

            migrationBuilder.DropIndex(
                name: "ix_warehouse_zones_dedicated_client_id",
                table: "warehouse_zones");

            migrationBuilder.DropIndex(
                name: "ix_warehouse_zones_facility_normalized_code",
                table: "warehouse_zones");

            migrationBuilder.DropCheckConstraint(
                name: "ck_warehouse_zones_temp_bounds",
                table: "warehouse_zones");

            migrationBuilder.DropIndex(
                name: "ix_packaging_materials_client_code",
                table: "packaging_materials");

            migrationBuilder.DropIndex(
                name: "ix_packaging_materials_facility_wide_code",
                table: "packaging_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_packaging_materials_cost_non_negative",
                table: "packaging_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_packaging_materials_positive_dims",
                table: "packaging_materials");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_locations_location_id_facility_id",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_locations_facility_hierarchy_path",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_locations_facility_normalized_barcode",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_locations_facility_normalized_code",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_locations_facility_status_storable",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_locations_parent_location_id_facility_id",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_locations_zone_id_facility_id",
                table: "locations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_locations_hierarchy_level",
                table: "locations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_locations_no_self_parent",
                table: "locations");

            migrationBuilder.DropIndex(
                name: "ix_dock_doors_facility_normalized_door_number",
                table: "dock_doors");

            migrationBuilder.DropIndex(
                name: "ix_dock_doors_location_id",
                table: "dock_doors");

            migrationBuilder.DropIndex(
                name: "ix_dock_doors_location_id_facility_id",
                table: "dock_doors");

            migrationBuilder.DropCheckConstraint(
                name: "ck_dock_doors_max_trailer_length_cm",
                table: "dock_doors");

            migrationBuilder.DropColumn(
                name: "config_revision",
                table: "warehouse_zones");

            migrationBuilder.DropColumn(
                name: "dedicated_client_id",
                table: "warehouse_zones");

            migrationBuilder.DropColumn(
                name: "normalized_zone_code",
                table: "warehouse_zones");

            migrationBuilder.DropColumn(
                name: "remarks",
                table: "warehouse_zones");

            migrationBuilder.DropColumn(
                name: "config_revision",
                table: "packaging_materials");

            migrationBuilder.DropColumn(
                name: "currency_code",
                table: "packaging_materials");

            migrationBuilder.DropColumn(
                name: "normalized_code",
                table: "packaging_materials");

            migrationBuilder.DropColumn(
                name: "config_revision",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "hierarchy_level",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "hierarchy_path",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "is_storable",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "location_name",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "normalized_barcode",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "normalized_location_code",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "remarks",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "status",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "config_revision",
                table: "dock_doors");

            migrationBuilder.DropColumn(
                name: "location_id",
                table: "dock_doors");

            migrationBuilder.DropColumn(
                name: "max_trailer_length_cm",
                table: "dock_doors");

            migrationBuilder.DropColumn(
                name: "normalized_door_number",
                table: "dock_doors");

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_zones_facility_id_zone_code",
                table: "warehouse_zones",
                columns: new[] { "facility_id", "zone_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_facility_id_barcode",
                table: "locations",
                columns: new[] { "facility_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_facility_id_location_code",
                table: "locations",
                columns: new[] { "facility_id", "location_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dock_doors_facility_id_door_number",
                table: "dock_doors",
                columns: new[] { "facility_id", "door_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_locations_locations_parent_location_id",
                table: "locations",
                column: "parent_location_id",
                principalTable: "locations",
                principalColumn: "location_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_locations_warehouse_zones_zone_id",
                table: "locations",
                column: "zone_id",
                principalTable: "warehouse_zones",
                principalColumn: "zone_id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
