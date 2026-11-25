using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_2_LegalEntitiesAndFacilityBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_currency_code",
                table: "facilities",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "default_legal_entity_id",
                table: "facilities",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "legal_entities",
                columns: table => new
                {
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    registration_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    tax_identifier_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    tax_registration_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    registered_address_line1 = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    registered_address_line2 = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    registered_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    registered_state_or_province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    registered_postal_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_cbms_mandated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    iro_software_permit_ref = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    default_currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    config_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
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
                    table.PrimaryKey("pk_legal_entities", x => x.legal_entity_id);
                    table.ForeignKey(
                        name: "fk_legal_entities_country_profiles_country_code",
                        column: x => x.country_code,
                        principalTable: "country_profiles",
                        principalColumn: "country_code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_facilities_default_legal_entity_id",
                table: "facilities",
                column: "default_legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_country_code",
                table: "legal_entities",
                column: "country_code");

            migrationBuilder.CreateIndex(
                name: "ix_legal_entities_legal_code",
                table: "legal_entities",
                column: "legal_code",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.AddForeignKey(
                name: "fk_facilities_legal_entities_default_legal_entity_id",
                table: "facilities",
                column: "default_legal_entity_id",
                principalTable: "legal_entities",
                principalColumn: "legal_entity_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_facilities_legal_entities_default_legal_entity_id",
                table: "facilities");

            migrationBuilder.DropTable(
                name: "legal_entities");

            migrationBuilder.DropIndex(
                name: "ix_facilities_default_legal_entity_id",
                table: "facilities");

            migrationBuilder.DropColumn(
                name: "default_currency_code",
                table: "facilities");

            migrationBuilder.DropColumn(
                name: "default_legal_entity_id",
                table: "facilities");
        }
    }
}
