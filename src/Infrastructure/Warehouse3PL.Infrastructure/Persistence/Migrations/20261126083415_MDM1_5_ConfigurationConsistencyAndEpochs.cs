using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_5_ConfigurationConsistencyAndEpochs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "configuration_invalidation_events",
                columns: table => new
                {
                    event_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    new_epoch = table.Column<long>(type: "bigint", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    dispatched_to_redis_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dispatch_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_dispatch_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuration_invalidation_events", x => x.event_id);
                    table.CheckConstraint("ck_configuration_invalidation_events_epoch_positive", "new_epoch >= 1");
                });

            migrationBuilder.CreateTable(
                name: "configuration_scope_epochs",
                columns: table => new
                {
                    epoch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    current_epoch = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuration_scope_epochs", x => x.epoch_id);
                    table.CheckConstraint("ck_configuration_scope_epochs_positive", "current_epoch >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_configuration_invalidation_events_dispatched_to_redis_at_ut",
                table: "configuration_invalidation_events",
                columns: new[] { "dispatched_to_redis_at_utc", "event_id" });

            migrationBuilder.CreateIndex(
                name: "ix_configuration_invalidation_events_scope_type_scope_key_new_",
                table: "configuration_invalidation_events",
                columns: new[] { "scope_type", "scope_key", "new_epoch" });

            migrationBuilder.CreateIndex(
                name: "ix_configuration_scope_epochs_scope_type_scope_key",
                table: "configuration_scope_epochs",
                columns: new[] { "scope_type", "scope_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuration_invalidation_events");

            migrationBuilder.DropTable(
                name: "configuration_scope_epochs");
        }
    }
}
