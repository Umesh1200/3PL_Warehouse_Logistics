using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase740BUserAssignedFacility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_facility_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_assigned_facility_id",
                table: "users",
                column: "assigned_facility_id");

            migrationBuilder.AddForeignKey(
                name: "fk_users_facilities_assigned_facility_id",
                table: "users",
                column: "assigned_facility_id",
                principalTable: "facilities",
                principalColumn: "facility_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_facilities_assigned_facility_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_assigned_facility_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "assigned_facility_id",
                table: "users");
        }
    }
}
