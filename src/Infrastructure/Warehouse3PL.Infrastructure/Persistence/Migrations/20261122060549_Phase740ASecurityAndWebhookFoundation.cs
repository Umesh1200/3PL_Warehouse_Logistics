using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase740ASecurityAndWebhookFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carrier_webhook_audit_logs",
                columns: table => new
                {
                    audit_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    nonce = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    outcome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    http_status_code = table.Column<int>(type: "integer", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_webhook_audit_logs", x => x.audit_log_id);
                });

            migrationBuilder.CreateTable(
                name: "carrier_webhook_nonces",
                columns: table => new
                {
                    nonce_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    nonce = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    webhook_timestamp_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carrier_webhook_nonces", x => x.nonce_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_webhook_audit_logs_carrier_recorded_at",
                table: "carrier_webhook_audit_logs",
                columns: new[] { "carrier_code", "recorded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_carrier_webhook_nonces_carrier_code_nonce",
                table: "carrier_webhook_nonces",
                columns: new[] { "carrier_code", "nonce" },
                unique: true);

            // Idempotently reconcile ClientAdmin permissions: remove obsolete warehouse floor execution permissions
            // and ensure client order-management permissions are present.
            migrationBuilder.Sql("""
                DELETE FROM role_permissions rp
                USING roles r
                WHERE rp.role_id = r.role_id
                  AND r.role_name = 'ClientAdmin'
                  AND r.is_system_role = TRUE
                  AND rp.permission_key IN (
                      'outbound:pick',
                      'outbound:ship',
                      'outbound:pack',
                      'outbound:tasks:manage',
                      'outbound:waves:manage',
                      'outbound:orders:allocate'
                  );

                INSERT INTO role_permissions (role_id, permission_key, access_level, created_at)
                SELECT r.role_id, v.permission_key, 'Admin', CURRENT_TIMESTAMP
                FROM roles r
                CROSS JOIN (VALUES
                    ('outbound:orders:view'),
                    ('outbound:orders:create'),
                    ('outbound:orders:import'),
                    ('outbound:orders:cancel')
                ) AS v(permission_key)
                WHERE r.role_name = 'ClientAdmin'
                  AND r.is_system_role = TRUE
                ON CONFLICT (role_id, permission_key) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carrier_webhook_audit_logs");

            migrationBuilder.DropTable(
                name: "carrier_webhook_nonces");
        }
    }
}
