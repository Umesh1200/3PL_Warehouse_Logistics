using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_8_DurableAnalyticsJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analytics_ingestion_journal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    projection_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    effective_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ingestion_ordinal = table.Column<long>(type: "bigint", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    arguments_json = table.Column<string>(type: "text", nullable: false),
                    effects_json = table.Column<string>(type: "text", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_ingestion_journal", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analytics_ingestion_journal_projection_name_event_id",
                table: "analytics_ingestion_journal",
                columns: new[] { "projection_name", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_analytics_ingestion_journal_projection_name_ingestion_ordin",
                table: "analytics_ingestion_journal",
                columns: new[] { "projection_name", "ingestion_ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analytics_ingestion_journal");
        }
    }
}
