using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_4_4_2_CarrierRateAgreements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carrier_rate_agreements",
                columns: table => new
                {
                    carrier_rate_agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_service_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    agreement_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_or_lane = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rate_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tier_model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    base_rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    minimum_charge = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    dimensional_weight_divisor = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    weight_rounding_increment_kg = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_rate_agreements", x => x.carrier_rate_agreement_id);
                    table.ForeignKey(
                        name: "fk_carrier_rate_agreements_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_carrier_rate_agreements_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "carrier_rate_tiers",
                columns: table => new
                {
                    carrier_rate_tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_rate_agreement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_number = table.Column<int>(type: "integer", nullable: false),
                    lower_bound = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    upper_bound = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_rate_tiers", x => x.carrier_rate_tier_id);
                    table.ForeignKey(
                        name: "fk_carrier_rate_tiers_carrier_rate_agreements_carrier_rate_agr",
                        column: x => x.carrier_rate_agreement_id,
                        principalTable: "carrier_rate_agreements",
                        principalColumn: "carrier_rate_agreement_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_rate_agreements_carrier_active",
                table: "carrier_rate_agreements",
                columns: new[] { "carrier_code", "is_active", "effective_from_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_rate_agreements_client_lookup",
                table: "carrier_rate_agreements",
                columns: new[] { "client_id", "carrier_code", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_rate_agreements_facility_lookup",
                table: "carrier_rate_agreements",
                columns: new[] { "facility_id", "carrier_code", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_rate_tiers_carrier_rate_agreement_id_tier_number",
                table: "carrier_rate_tiers",
                columns: new[] { "carrier_rate_agreement_id", "tier_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carrier_rate_tiers");

            migrationBuilder.DropTable(
                name: "carrier_rate_agreements");
        }
    }
}
