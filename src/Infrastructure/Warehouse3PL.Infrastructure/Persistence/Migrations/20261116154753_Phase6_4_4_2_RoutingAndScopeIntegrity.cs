using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_4_4_2_RoutingAndScopeIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_carrier_rate_agreements_facilities_facility_id",
                table: "carrier_rate_agreements");

            migrationBuilder.AddColumn<string>(
                name: "destination_country_code",
                table: "carrier_rate_agreements",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_facility_code",
                table: "carrier_rate_agreements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_postal_code_prefix",
                table: "carrier_rate_agreements",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_state_or_province",
                table: "carrier_rate_agreements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_carrier_rate_agreements_facilities_facility_id",
                table: "carrier_rate_agreements",
                column: "facility_id",
                principalTable: "facilities",
                principalColumn: "facility_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_carrier_rate_agreements_facilities_facility_id",
                table: "carrier_rate_agreements");

            migrationBuilder.DropColumn(
                name: "destination_country_code",
                table: "carrier_rate_agreements");

            migrationBuilder.DropColumn(
                name: "destination_facility_code",
                table: "carrier_rate_agreements");

            migrationBuilder.DropColumn(
                name: "destination_postal_code_prefix",
                table: "carrier_rate_agreements");

            migrationBuilder.DropColumn(
                name: "destination_state_or_province",
                table: "carrier_rate_agreements");

            migrationBuilder.AddForeignKey(
                name: "fk_carrier_rate_agreements_facilities_facility_id",
                table: "carrier_rate_agreements",
                column: "facility_id",
                principalTable: "facilities",
                principalColumn: "facility_id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
