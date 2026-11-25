using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_1_GlobalReferencesAndCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "country_profiles",
                columns: table => new
                {
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    iso_alpha3_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    numeric_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    default_currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    primary_time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    address_schema_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    postal_code_regex = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    legacy_postal_code_regex = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    primary_calendar_system = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fiscal_calendar_system = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    default_fiscal_year_start_month = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    seed_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_country_profiles", x => x.country_code);
                    table.CheckConstraint("ck_country_profiles_fiscal_month", "default_fiscal_year_start_month BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "time_zone_profiles",
                columns: table => new
                {
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    observes_dst = table.Column<bool>(type: "boolean", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_zone_profiles", x => x.time_zone_id);
                });

            migrationBuilder.CreateTable(
                name: "administrative_divisions",
                columns: table => new
                {
                    division_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    parent_division_id = table.Column<Guid>(type: "uuid", nullable: true),
                    division_level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    iso_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    local_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    postal_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_administrative_divisions", x => x.division_id);
                    table.ForeignKey(
                        name: "fk_administrative_divisions_administrative_divisions_parent_di",
                        column: x => x.parent_division_id,
                        principalTable: "administrative_divisions",
                        principalColumn: "division_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_administrative_divisions_country_profiles_country_code",
                        column: x => x.country_code,
                        principalTable: "country_profiles",
                        principalColumn: "country_code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "country_compliance_policies",
                columns: table => new
                {
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    policy_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    statutory_source_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    official_notice_citation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    applicability_predicate_json = table.Column<string>(type: "jsonb", nullable: false),
                    enforcement_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approval_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    verification_evidence_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    superseded_by_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    config_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_country_compliance_policies", x => x.policy_id);
                    table.CheckConstraint("ck_country_compliance_policies_version_positive", "version >= 1");
                    table.CheckConstraint("ck_country_compliance_policies_window", "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc");
                    table.ForeignKey(
                        name: "fk_country_compliance_policies_country_compliance_policies_sup",
                        column: x => x.superseded_by_policy_id,
                        principalTable: "country_compliance_policies",
                        principalColumn: "policy_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_country_compliance_policies_country_profiles_country_code",
                        column: x => x.country_code,
                        principalTable: "country_profiles",
                        principalColumn: "country_code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_administrative_divisions_country_code_division_level_code",
                table: "administrative_divisions",
                columns: new[] { "country_code", "division_level", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_administrative_divisions_country_code_parent_division_id",
                table: "administrative_divisions",
                columns: new[] { "country_code", "parent_division_id" });

            migrationBuilder.CreateIndex(
                name: "ix_administrative_divisions_iso_code",
                table: "administrative_divisions",
                column: "iso_code",
                unique: true,
                filter: "iso_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_administrative_divisions_parent_division_id",
                table: "administrative_divisions",
                column: "parent_division_id");

            migrationBuilder.CreateIndex(
                name: "ix_country_compliance_policies_country_code_policy_code_approv",
                table: "country_compliance_policies",
                columns: new[] { "country_code", "policy_code", "approval_status", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_country_compliance_policies_country_code_policy_code_version",
                table: "country_compliance_policies",
                columns: new[] { "country_code", "policy_code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_country_compliance_policies_superseded_by_policy_id",
                table: "country_compliance_policies",
                column: "superseded_by_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_country_profiles_iso_alpha3_code",
                table: "country_profiles",
                column: "iso_alpha3_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_time_zone_profiles_country_code",
                table: "time_zone_profiles",
                column: "country_code");

            // Install PostgreSQL btree_gist safely (succeeds when already pre-installed under
            // restricted migration roles, installs automatically when permitted, and fails closed
            // with an explicit error if neither condition holds).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist') THEN
                        BEGIN
                            CREATE EXTENSION IF NOT EXISTS btree_gist;
                        EXCEPTION WHEN insufficient_privilege THEN
                            RAISE EXCEPTION 'Migration MDM1_1_GlobalReferencesAndCompliance requires PostgreSQL extension btree_gist. Pre-install btree_gist as superuser or grant extension creation privilege before migrating.';
                        END;
                    END IF;
                END $$;
                """);

            // Database-enforced active effective-window exclusion constraint matching EF Core
            // string-converted ApprovalStatus ('VerifiedApproved') and IsActive = true.
            migrationBuilder.Sql("""
                ALTER TABLE country_compliance_policies
                ADD CONSTRAINT excl_country_compliance_policies_active_window
                EXCLUDE USING gist (
                    country_code WITH =,
                    policy_code WITH =,
                    tstzrange(effective_from_utc, COALESCE(effective_to_utc, 'infinity'::timestamptz), '[)') WITH &&
                )
                WHERE (approval_status = 'VerifiedApproved' AND is_active = true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE country_compliance_policies
                DROP CONSTRAINT IF EXISTS excl_country_compliance_policies_active_window;
                """);

            migrationBuilder.DropTable(
                name: "administrative_divisions");

            migrationBuilder.DropTable(
                name: "country_compliance_policies");

            migrationBuilder.DropTable(
                name: "time_zone_profiles");

            migrationBuilder.DropTable(
                name: "country_profiles");
        }
    }
}
