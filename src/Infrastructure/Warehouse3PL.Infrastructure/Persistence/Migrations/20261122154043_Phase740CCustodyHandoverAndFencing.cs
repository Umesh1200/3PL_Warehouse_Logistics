using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase740CCustodyHandoverAndFencing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custody_status",
                table: "shipments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Unassigned");

            migrationBuilder.AddColumn<string>(
                name: "custody_status",
                table: "shipment_custody_transfers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Confirmed");

            // Neutralize any pre-existing synthetic supervisor-reassignment rows and backfill confirmed dispatched shipments.
            migrationBuilder.Sql("""
                UPDATE shipment_custody_transfers
                SET custody_status = 'Revoked'
                WHERE driver_signature_acknowledgment LIKE 'SUPERVISOR_CUSTODY_REASSIGNMENT:%';

                UPDATE shipments s
                SET custody_status = CASE
                    WHEN s.status = 'Dispatched' AND s.assigned_driver_user_id IS NOT NULL AND EXISTS (
                        SELECT 1
                        FROM shipment_custody_transfers t
                        WHERE t.shipment_id = s.shipment_id
                          AND t.assigned_driver_user_id = s.assigned_driver_user_id
                          AND t.custody_status = 'Confirmed'
                          AND t.driver_signature_acknowledgment NOT LIKE 'SUPERVISOR_CUSTODY_REASSIGNMENT:%'
                          AND NOT EXISTS (
                              SELECT 1
                              FROM shipment_custody_transfers newer
                              WHERE newer.shipment_id = t.shipment_id
                                AND (newer.transferred_at_utc > t.transferred_at_utc
                                     OR (newer.transferred_at_utc = t.transferred_at_utc AND newer.transfer_id > t.transfer_id))
                          )
                    ) THEN 'Confirmed'
                    WHEN s.status = 'Dispatched' AND s.assigned_driver_user_id IS NOT NULL THEN 'PendingHandover'
                    ELSE 'Unassigned'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custody_status",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "custody_status",
                table: "shipment_custody_transfers");
        }
    }
}
