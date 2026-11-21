using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase73PutawayTaskReceiptLineUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_putaway_tasks_lpn_id",
                table: "putaway_tasks");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_lpn_id",
                table: "putaway_tasks",
                column: "lpn_id",
                unique: true,
                filter: "lpn_id IS NOT NULL AND status <> 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_putaway_tasks_lpn_id",
                table: "putaway_tasks");

            migrationBuilder.CreateIndex(
                name: "ix_putaway_tasks_lpn_id",
                table: "putaway_tasks",
                column: "lpn_id");
        }
    }
}
