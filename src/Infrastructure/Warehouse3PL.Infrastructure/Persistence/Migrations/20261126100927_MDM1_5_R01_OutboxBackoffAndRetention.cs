using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_5_R01_OutboxBackoffAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "next_retry_at_utc",
                table: "configuration_invalidation_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_configuration_invalidation_events_dispatched_to_redis_at_ut1",
                table: "configuration_invalidation_events",
                columns: new[] { "dispatched_to_redis_at_utc", "next_retry_at_utc", "event_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_configuration_invalidation_events_dispatched_to_redis_at_ut1",
                table: "configuration_invalidation_events");

            migrationBuilder.DropColumn(
                name: "next_retry_at_utc",
                table: "configuration_invalidation_events");
        }
    }
}
