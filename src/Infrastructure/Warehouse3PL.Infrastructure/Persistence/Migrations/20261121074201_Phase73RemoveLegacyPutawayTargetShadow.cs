using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase73RemoveLegacyPutawayTargetShadow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data Safety: Ensure any legacy stored target_location_id1 is reconciled into suggested_location_id before dropping
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns 
                        WHERE table_name = 'putaway_tasks' AND column_name = 'target_location_id1'
                    ) THEN
                        UPDATE putaway_tasks
                        SET suggested_location_id = target_location_id1
                        WHERE (suggested_location_id IS NULL OR suggested_location_id = '00000000-0000-0000-0000-000000000000'::uuid)
                          AND target_location_id1 IS NOT NULL
                          AND target_location_id1 <> '00000000-0000-0000-0000-000000000000'::uuid;
                    END IF;
                END $$;
            ");

            migrationBuilder.DropForeignKey(
                name: "fk_putaway_tasks_locations_target_location_id1",
                table: "putaway_tasks");

            migrationBuilder.DropIndex(
                name: "ix_putaway_tasks_target_location_id1",
                table: "putaway_tasks");

            migrationBuilder.DropColumn(
                name: "target_location_id1",
                table: "putaway_tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "target_location_id1",
                table: "putaway_tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql(@"
                UPDATE putaway_tasks
                SET target_location_id1 = suggested_location_id
                WHERE suggested_location_id IS NOT NULL;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_target_location_id1",
                table: "putaway_tasks",
                column: "target_location_id1");

            migrationBuilder.AddForeignKey(
                name: "fk_putaway_tasks_locations_target_location_id1",
                table: "putaway_tasks",
                column: "target_location_id1",
                principalTable: "locations",
                principalColumn: "location_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
