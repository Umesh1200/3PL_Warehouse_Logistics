using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_4_CalendarsAndBikramSambat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bikram_sambat_calendar_months",
                columns: table => new
                {
                    bs_year = table.Column<int>(type: "integer", nullable: false),
                    bs_month = table.Column<int>(type: "integer", nullable: false),
                    month_name_en = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    month_name_ne = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    days_in_month = table.Column<int>(type: "integer", nullable: false),
                    gregorian_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    gregorian_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fiscal_year_label = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_citation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    seed_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bikram_sambat_calendar_months", x => new { x.bs_year, x.bs_month });
                    table.CheckConstraint("ck_bs_months_days_range", "days_in_month >= 29 AND days_in_month <= 32");
                    table.CheckConstraint("ck_bs_months_month_range", "bs_month >= 1 AND bs_month <= 12");
                    table.CheckConstraint("ck_bs_months_span", "gregorian_end_date >= gregorian_start_date");
                    table.CheckConstraint("ck_bs_months_year_range", "bs_year >= 2075 AND bs_year <= 2090");
                });

            migrationBuilder.CreateTable(
                name: "facility_calendars",
                columns: table => new
                {
                    facility_calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    weekly_rest_days_csv = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    workday_start_local_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    workday_end_local_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    default_cutoff_local_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    config_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facility_calendars", x => x.facility_calendar_id);
                    table.CheckConstraint("ck_facility_calendars_hours", "workday_end_local_time > workday_start_local_time AND default_cutoff_local_time >= workday_start_local_time AND default_cutoff_local_time <= workday_end_local_time");
                    table.CheckConstraint("ck_facility_calendars_window", "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc");
                    table.ForeignKey(
                        name: "fk_facility_calendars_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "holiday_calendars",
                columns: table => new
                {
                    holiday_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    subdivision_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    holiday_date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    holiday_scope = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    is_working_day_override = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    config_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holiday_calendars", x => x.holiday_id);
                    table.CheckConstraint("ck_holiday_calendars_scope_binding", "(holiday_scope = 'National' AND subdivision_code IS NULL AND facility_id IS NULL) OR (holiday_scope = 'Regional' AND subdivision_code IS NOT NULL AND facility_id IS NULL) OR (holiday_scope = 'Facility' AND facility_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_holiday_calendars_country_profiles_country_code",
                        column: x => x.country_code,
                        principalTable: "country_profiles",
                        principalColumn: "country_code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_holiday_calendars_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bikram_sambat_calendar_months_gregorian_end_date",
                table: "bikram_sambat_calendar_months",
                column: "gregorian_end_date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bikram_sambat_calendar_months_gregorian_start_date",
                table: "bikram_sambat_calendar_months",
                column: "gregorian_start_date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bikram_sambat_calendar_months_gregorian_start_date_gregoria",
                table: "bikram_sambat_calendar_months",
                columns: new[] { "gregorian_start_date", "gregorian_end_date" });

            migrationBuilder.CreateIndex(
                name: "ix_facility_calendars_facility_id_is_active_effective_from_utc",
                table: "facility_calendars",
                columns: new[] { "facility_id", "is_active", "effective_from_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendars_country_code_holiday_date_is_active",
                table: "holiday_calendars",
                columns: new[] { "country_code", "holiday_date", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_holiday_calendars_facility_id_holiday_date_is_active",
                table: "holiday_calendars",
                columns: new[] { "facility_id", "holiday_date", "is_active" });

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.Sql("""
                ALTER TABLE facility_calendars
                ADD CONSTRAINT excl_facility_calendars_active_window
                EXCLUDE USING gist (
                    facility_id WITH =,
                    tstzrange(effective_from_utc, COALESCE(effective_to_utc, 'infinity'::timestamptz), '[)') WITH &&
                )
                WHERE (is_active = true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE facility_calendars DROP CONSTRAINT IF EXISTS excl_facility_calendars_active_window;");

            migrationBuilder.DropTable(
                name: "bikram_sambat_calendar_months");

            migrationBuilder.DropTable(
                name: "facility_calendars");

            migrationBuilder.DropTable(
                name: "holiday_calendars");
        }
    }
}
