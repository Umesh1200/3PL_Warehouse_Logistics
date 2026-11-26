using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_3_R03_FxOfficialProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "evidence_reference",
                table: "fx_rates",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "evidence_reference",
                table: "fx_rates");
        }
    }
}
