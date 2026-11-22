using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase740CDriverCustodyAndDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_driver_user_id",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_driver_user_id",
                table: "shipment_custody_transfers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_assigned_driver_user_id",
                table: "shipments",
                column: "assigned_driver_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_custody_transfers_assigned_driver_user_id",
                table: "shipment_custody_transfers",
                column: "assigned_driver_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_shipment_custody_transfers_users_assigned_driver_user_id",
                table: "shipment_custody_transfers",
                column: "assigned_driver_user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_shipments_users_assigned_driver_user_id",
                table: "shipments",
                column: "assigned_driver_user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.SetNull);

            // Idempotently ensure Driver role exists and holds dispatch:execute-delivery ONLY (excluding warehouse/management grants).
            migrationBuilder.Sql("""
                INSERT INTO roles (role_name, description, is_system_role, created_at)
                VALUES ('Driver', 'Courier and last-mile delivery driver.', TRUE, CURRENT_TIMESTAMP)
                ON CONFLICT (role_name) DO NOTHING;

                DELETE FROM role_permissions rp
                USING roles r
                WHERE rp.role_id = r.role_id
                  AND r.role_name = 'Driver'
                  AND r.is_system_role = TRUE
                  AND rp.permission_key IN (
                      'outbound:pick',
                      'outbound:pack',
                      'outbound:ship',
                      'outbound:tasks:view',
                      'outbound:tasks:manage',
                      'outbound:waves:manage',
                      'outbound:orders:view',
                      'outbound:orders:create',
                      'outbound:orders:import',
                      'outbound:orders:cancel',
                      'outbound:orders:allocate',
                      'dispatch:manage-runs'
                  );

                INSERT INTO role_permissions (role_id, permission_key, access_level, created_at)
                SELECT r.role_id, 'dispatch:execute-delivery', 'Execute', CURRENT_TIMESTAMP
                FROM roles r
                WHERE r.role_name = 'Driver'
                  AND r.is_system_role = TRUE
                ON CONFLICT (role_id, permission_key) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_shipment_custody_transfers_users_assigned_driver_user_id",
                table: "shipment_custody_transfers");

            migrationBuilder.DropForeignKey(
                name: "fk_shipments_users_assigned_driver_user_id",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_assigned_driver_user_id",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipment_custody_transfers_assigned_driver_user_id",
                table: "shipment_custody_transfers");

            migrationBuilder.DropColumn(
                name: "assigned_driver_user_id",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "assigned_driver_user_id",
                table: "shipment_custody_transfers");
        }
    }
}
