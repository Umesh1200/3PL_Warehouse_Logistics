using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM1_3_CurrencyFxAndUomStandards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    numeric_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    minor_units = table.Column<int>(type: "integer", nullable: false),
                    symbol_before_amount = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    seed_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.currency_code);
                    table.CheckConstraint("ck_currencies_minor_units", "minor_units BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "uom_definitions",
                columns: table => new
                {
                    uom_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    unece_rec20_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    dimension = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    system_of_measure = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    is_canonical_base = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    rounding_precision = table.Column<int>(type: "integer", nullable: false, defaultValue: 4),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uom_definitions", x => x.uom_code);
                    table.CheckConstraint("ck_uom_definitions_precision", "rounding_precision BETWEEN 0 AND 8");
                });

            migrationBuilder.CreateTable(
                name: "fx_rates",
                columns: table => new
                {
                    fx_rate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    quote_currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unit_multiplier = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    buy_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    sell_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    mid_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    rate_direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_authority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bulletin_version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    benchmark_series_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    integrity_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approval_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    verification_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    superseded_by_rate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    config_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fx_rates", x => x.fx_rate_id);
                    table.CheckConstraint("ck_fx_rates_distinct_currencies", "base_currency_code <> quote_currency_code");
                    table.CheckConstraint("ck_fx_rates_positive_rates", "buy_rate > 0 AND sell_rate > 0 AND mid_rate > 0 AND buy_rate <= sell_rate");
                    table.CheckConstraint("ck_fx_rates_unit_multiplier_positive", "unit_multiplier > 0");
                    table.CheckConstraint("ck_fx_rates_window", "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc");
                    table.ForeignKey(
                        name: "fk_fx_rates_currencies_base_currency_code",
                        column: x => x.base_currency_code,
                        principalTable: "currencies",
                        principalColumn: "currency_code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fx_rates_currencies_quote_currency_code",
                        column: x => x.quote_currency_code,
                        principalTable: "currencies",
                        principalColumn: "currency_code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fx_rates_fx_rates_superseded_by_rate_id",
                        column: x => x.superseded_by_rate_id,
                        principalTable: "fx_rates",
                        principalColumn: "fx_rate_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "uom_conversions",
                columns: table => new
                {
                    from_uom_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    to_uom_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    dimension = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    conversion_factor = table.Column<decimal>(type: "numeric(18,10)", precision: 18, scale: 10, nullable: false),
                    inverse_factor = table.Column<decimal>(type: "numeric(18,10)", precision: 18, scale: 10, nullable: false),
                    is_exact = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_uom_conversions", x => new { x.from_uom_code, x.to_uom_code });
                    table.CheckConstraint("ck_uom_conversions_distinct_uoms", "from_uom_code <> to_uom_code");
                    table.CheckConstraint("ck_uom_conversions_positive_factors", "conversion_factor > 0 AND inverse_factor > 0");
                    table.ForeignKey(
                        name: "fk_uom_conversions_uom_definitions_from_uom_code",
                        column: x => x.from_uom_code,
                        principalTable: "uom_definitions",
                        principalColumn: "uom_code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_uom_conversions_uom_definitions_to_uom_code",
                        column: x => x.to_uom_code,
                        principalTable: "uom_definitions",
                        principalColumn: "uom_code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_currencies_numeric_code",
                table: "currencies",
                column: "numeric_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fx_rates_base_currency_code_quote_currency_code_source_auth",
                table: "fx_rates",
                columns: new[] { "base_currency_code", "quote_currency_code", "source_authority", "approval_status", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_fx_rates_quote_currency_code",
                table: "fx_rates",
                column: "quote_currency_code");

            migrationBuilder.CreateIndex(
                name: "ix_fx_rates_superseded_by_rate_id",
                table: "fx_rates",
                column: "superseded_by_rate_id");

            migrationBuilder.CreateIndex(
                name: "ix_uom_conversions_to_uom_code",
                table: "uom_conversions",
                column: "to_uom_code");

            migrationBuilder.CreateIndex(
                name: "ix_uom_definitions_dimension",
                table: "uom_definitions",
                column: "dimension");

            migrationBuilder.Sql("""
                ALTER TABLE fx_rates
                ADD CONSTRAINT excl_fx_rates_active_window
                EXCLUDE USING gist (
                    base_currency_code WITH =,
                    quote_currency_code WITH =,
                    source_authority WITH =,
                    tstzrange(effective_from_utc, COALESCE(effective_to_utc, 'infinity'::timestamptz), '[)') WITH &&
                )
                WHERE (approval_status = 'VerifiedApproved' AND is_active = true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE fx_rates
                DROP CONSTRAINT IF EXISTS excl_fx_rates_active_window;
                """);

            migrationBuilder.DropTable(
                name: "fx_rates");

            migrationBuilder.DropTable(
                name: "uom_conversions");

            migrationBuilder.DropTable(
                name: "currencies");

            migrationBuilder.DropTable(
                name: "uom_definitions");
        }
    }
}
