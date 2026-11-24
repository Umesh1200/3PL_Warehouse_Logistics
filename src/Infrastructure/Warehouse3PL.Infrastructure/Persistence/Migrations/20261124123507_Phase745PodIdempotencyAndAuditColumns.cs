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

            // Fail-closed pre-migration collision guard:
            // Before normalizing historical idempotency_key values or creating ix_proof_of_deliveries_facility_idempotency,
            // verify that no (facility_id, UPPER(BTRIM(idempotency_key))) group contains multiple ProofOfDelivery rows.
            // If collisions exist, abort cleanly BEFORE mutating any row or creating the index so historical POD evidence
            // stays completely intact. Operational resolution procedure: inspect reported pod_ids, retain all historical
            // proof_of_deliveries rows, and disambiguate duplicate legacy keys (e.g. 'LEGACY-CONFLICT:' || pod_id::text)
            // under operational audit sign-off before re-running MigrateAsync().
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    collision_count INT;
                    collision_summary TEXT;
                BEGIN
                    SELECT COUNT(*),
                           COALESCE(STRING_AGG(facility_id::text || '::' || norm_key || ' (rows=' || row_count::text || ', ids=' || pod_ids || ')', '; '), '')
                    INTO collision_count, collision_summary
                    FROM (
                        SELECT facility_id,
                               UPPER(BTRIM(idempotency_key)) AS norm_key,
                               COUNT(*) AS row_count,
                               STRING_AGG(pod_id::text, ',' ORDER BY captured_at_utc ASC, pod_id ASC) AS pod_ids
                        FROM proof_of_deliveries
                        WHERE idempotency_key IS NOT NULL
                          AND BTRIM(idempotency_key) <> ''
                        GROUP BY facility_id, UPPER(BTRIM(idempotency_key))
                        HAVING COUNT(*) > 1
                    ) collisions;

                    IF collision_count > 0 THEN
                        RAISE EXCEPTION 'Migration Phase745PodIdempotencyAndAuditColumns blocked: Found % facility-scoped POD idempotency key collision group(s) after canonical normalization [%]. All ProofOfDelivery rows remain preserved; resolve colliding legacy keys under audit sign-off before re-running migration.',
                            collision_count, collision_summary;
                    END IF;
                END $$;
                """);

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
