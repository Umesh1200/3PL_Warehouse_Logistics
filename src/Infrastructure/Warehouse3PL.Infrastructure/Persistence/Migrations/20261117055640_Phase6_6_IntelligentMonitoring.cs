using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_6_IntelligentMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monitoring_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    metric = table.Column<int>(type: "integer", nullable: false),
                    comparison = table.Column<int>(type: "integer", nullable: false),
                    threshold = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_rules", x => x.id);
                    table.UniqueConstraint("ak_monitoring_rules_id_client_id_facility_id", x => new { x.id, x.client_id, x.facility_id });
                });

            migrationBuilder.CreateTable(
                name: "monitoring_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    observed_value = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    first_observed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_observed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    acknowledged_by = table.Column<Guid>(type: "uuid", nullable: true),
                    acknowledged_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_monitoring_alerts_monitoring_rules_rule_id_client_id_facili",
                        columns: x => new { x.rule_id, x.client_id, x.facility_id },
                        principalTable: "monitoring_rules",
                        principalColumns: new[] { "id", "client_id", "facility_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_alerts_client_id_facility_id_status_severity",
                table: "monitoring_alerts",
                columns: new[] { "client_id", "facility_id", "status", "severity" });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_alerts_rule_id_client_id_facility_id",
                table: "monitoring_alerts",
                columns: new[] { "rule_id", "client_id", "facility_id" });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_alerts_rule_id_evidence_key",
                table: "monitoring_alerts",
                columns: new[] { "rule_id", "evidence_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_rules_client_id_facility_id_enabled",
                table: "monitoring_rules",
                columns: new[] { "client_id", "facility_id", "enabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_alerts");

            migrationBuilder.DropTable(
                name: "monitoring_rules");
        }
    }
}
