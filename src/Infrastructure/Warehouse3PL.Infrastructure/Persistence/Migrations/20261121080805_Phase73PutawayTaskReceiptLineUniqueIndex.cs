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
            // Assess existing duplicate receipt-line tasks before applying the partial unique index:
            // 1. Unresolved conflicts: If multiple completed tasks exist for the same receipt line with differing destination/actual locations, fail explicitly to prevent silent history corruption.
            // 2. Benign duplicates: Preserve operational history by cancelling superseded duplicates (keeping the most authoritative/latest task), adding an audit note.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    conflict_count INT;
                BEGIN
                    -- Check for unresolved conflicting completed tasks
                    SELECT COUNT(*) INTO conflict_count
                    FROM (
                        SELECT lpn_id
                        FROM putaway_tasks
                        WHERE lpn_id IS NOT NULL AND status = 'Completed'
                        GROUP BY lpn_id
                        HAVING COUNT(DISTINCT COALESCE(actual_location_id, suggested_location_id)) > 1
                    ) conflicts;

                    IF conflict_count > 0 THEN
                        RAISE EXCEPTION 'Migration aborted: Found % unresolved conflicting completed putaway tasks for the same receipt line with differing locations.', conflict_count;
                    END IF;

                    -- Preserve operational history for benign duplicates by marking superseded tasks as Cancelled
                    UPDATE putaway_tasks pt
                    SET status = 'Cancelled',
                        notes = COALESCE(notes || ' | ', '') || 'Superseded by migration deduplication'
                    WHERE pt.lpn_id IS NOT NULL
                      AND pt.status <> 'Cancelled'
                      AND pt.task_id NOT IN (
                          SELECT DISTINCT ON (lpn_id) task_id
                          FROM putaway_tasks
                          WHERE lpn_id IS NOT NULL AND status <> 'Cancelled'
                          ORDER BY lpn_id,
                                   CASE WHEN status = 'Completed' THEN 1 ELSE 2 END,
                                   created_at_utc DESC
                      );
                END $$;
            ");

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
