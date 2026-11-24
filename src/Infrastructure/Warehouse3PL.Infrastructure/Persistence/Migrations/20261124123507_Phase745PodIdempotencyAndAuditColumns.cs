using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase745PodIdempotencyAndAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_proof_of_deliveries_facility_id",
                table: "proof_of_deliveries");

            migrationBuilder.AddColumn<string>(
                name: "consignee_acknowledgment_text",
                table: "proof_of_deliveries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_explicit_delivered_at",
                table: "proof_of_deliveries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "verification_reason",
                table: "proof_of_deliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE proof_of_deliveries
                SET idempotency_key = NULLIF(UPPER(BTRIM(idempotency_key)), '')
                WHERE idempotency_key IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_proof_of_deliveries_facility_idempotency",
                table: "proof_of_deliveries",
                columns: new[] { "facility_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_proof_of_deliveries_facility_idempotency",
                table: "proof_of_deliveries");

            migrationBuilder.DropColumn(
                name: "consignee_acknowledgment_text",
                table: "proof_of_deliveries");

            migrationBuilder.DropColumn(
                name: "has_explicit_delivered_at",
                table: "proof_of_deliveries");

            migrationBuilder.DropColumn(
                name: "verification_reason",
                table: "proof_of_deliveries");

            migrationBuilder.CreateIndex(
                name: "ix_proof_of_deliveries_facility_id",
                table: "proof_of_deliveries",
                column: "facility_id");
        }
    }
}
