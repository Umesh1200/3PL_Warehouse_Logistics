using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase740AWebhookNonceRetryRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                table: "carrier_webhook_nonces",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_attempt_at_utc",
                table: "carrier_webhook_nonces",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "last_failure_reason",
                table: "carrier_webhook_nonces",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_http_status_code",
                table: "carrier_webhook_nonces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "processed_at_utc",
                table: "carrier_webhook_nonces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "processing_lease_expires_at_utc",
                table: "carrier_webhook_nonces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "carrier_webhook_nonces",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Processing");

            // Backfill any pre-existing nonces from prior 7.4.0A deployment as permanently Succeeded so they cannot be replayed.
            migrationBuilder.Sql("""
                UPDATE carrier_webhook_nonces
                SET status = 'Succeeded',
                    processed_at_utc = received_at_utc,
                    last_attempt_at_utc = received_at_utc,
                    last_http_status_code = 200
                WHERE status = 'Processing'
                  AND processed_at_utc IS NULL;

                INSERT INTO role_permissions (role_id, permission_key, access_level, created_at)
                SELECT r.role_id, 'dispatch:view', 'Admin', CURRENT_TIMESTAMP
                FROM roles r
                WHERE r.role_name = 'ClientAdmin'
                  AND r.is_system_role = TRUE
                ON CONFLICT (role_id, permission_key) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attempt_count",
                table: "carrier_webhook_nonces");

            migrationBuilder.DropColumn(
                name: "last_attempt_at_utc",
                table: "carrier_webhook_nonces");

            migrationBuilder.DropColumn(
                name: "last_failure_reason",
                table: "carrier_webhook_nonces");

            migrationBuilder.DropColumn(
                name: "last_http_status_code",
                table: "carrier_webhook_nonces");

            migrationBuilder.DropColumn(
                name: "processed_at_utc",
                table: "carrier_webhook_nonces");

            migrationBuilder.DropColumn(
                name: "processing_lease_expires_at_utc",
                table: "carrier_webhook_nonces");

            migrationBuilder.DropColumn(
                name: "status",
                table: "carrier_webhook_nonces");
        }
    }
}
