using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase740BRoleEscalationReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role_reconciliation_audit_logs",
                columns: table => new
                {
                    audit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    role_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    reconciled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_reconciliation_audit_logs", x => x.audit_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_reconciliation_audit_logs_reconciled_at_utc",
                table: "role_reconciliation_audit_logs",
                column: "reconciled_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_role_reconciliation_audit_logs_user_id",
                table: "role_reconciliation_audit_logs",
                column: "user_id");

            migrationBuilder.Sql("""
                INSERT INTO role_reconciliation_audit_logs (
                    audit_id, user_id, user_type, role_id, role_name, action, reason, reconciled_at_utc)
                SELECT gen_random_uuid(),
                       u.user_id,
                       u.user_type,
                       r.role_id,
                       r.role_name,
                       'RemovedPlatformRoleFromNonStaff',
                       'Migration reconciliation removed disallowed role ' || r.role_name || ' from ' || u.user_type || ' user ' || u.email,
                       CURRENT_TIMESTAMP
                FROM user_roles ur
                JOIN users u ON u.user_id = ur.user_id
                JOIN roles r ON r.role_id = ur.role_id
                WHERE (u.user_type = 'Client' AND LOWER(r.role_name) NOT IN ('clientadmin', 'clientuser'))
                   OR (u.user_type = 'Driver' AND LOWER(r.role_name) <> 'driver');

                UPDATE users u
                SET refresh_token = NULL,
                    refresh_token_expiry_time = NULL
                WHERE u.user_id IN (
                    SELECT ur.user_id
                    FROM user_roles ur
                    JOIN users iu ON iu.user_id = ur.user_id
                    JOIN roles r ON r.role_id = ur.role_id
                    WHERE (iu.user_type = 'Client' AND LOWER(r.role_name) NOT IN ('clientadmin', 'clientuser'))
                       OR (iu.user_type = 'Driver' AND LOWER(r.role_name) <> 'driver')
                );

                DELETE FROM user_roles ur
                USING users u, roles r
                WHERE ur.user_id = u.user_id
                  AND ur.role_id = r.role_id
                  AND ((u.user_type = 'Client' AND LOWER(r.role_name) NOT IN ('clientadmin', 'clientuser'))
                    OR (u.user_type = 'Driver' AND LOWER(r.role_name) <> 'driver'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_reconciliation_audit_logs");
        }
    }
}
