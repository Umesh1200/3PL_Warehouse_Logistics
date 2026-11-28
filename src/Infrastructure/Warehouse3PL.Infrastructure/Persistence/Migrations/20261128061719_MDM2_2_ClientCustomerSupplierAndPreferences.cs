using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MDM2_2_ClientCustomerSupplierAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_client_facility_access_client_id",
                table: "client_facility_access");

            migrationBuilder.AddColumn<int>(
                name: "config_revision",
                table: "suppliers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "lead_time_days",
                table: "suppliers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_supplier_code",
                table: "suppliers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "payment_terms_days",
                table: "suppliers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_registration_number",
                table: "suppliers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_address_line1",
                table: "customers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_address_line2",
                table: "customers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_city",
                table: "customers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_country_code",
                table: "customers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_country_normalization_status",
                table: "customers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PendingNormalization");

            migrationBuilder.AddColumn<string>(
                name: "billing_postal_code",
                table: "customers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_state_or_province",
                table: "customers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "config_revision",
                table: "customers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "normalized_customer_code",
                table: "customers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tax_registration_number",
                table: "customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_address_line_1",
                table: "clients",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_address_line_2",
                table: "clients",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_city",
                table: "clients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_country_code",
                table: "clients",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_postal_code",
                table: "clients",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_state_or_province",
                table: "clients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "business_classification",
                table: "clients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "config_revision",
                table: "clients",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "country_normalization_status",
                table: "clients",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PendingNormalization");

            migrationBuilder.AddColumn<string>(
                name: "normalized_account_code",
                table: "clients",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "tax_registration_number",
                table: "clients",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "config_revision",
                table: "client_facility_access",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "remarks",
                table: "client_facility_access",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "revoked_at_utc",
                table: "client_facility_access",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "config_revision",
                table: "client_contacts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "client_contacts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "client_contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "role_title",
                table: "client_contacts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "client_operational_preferences",
                columns: table => new
                {
                    preference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_receiving_zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_shipping_zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    require_asn_before_receiving = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    allow_partial_shipments = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    require_qc_inspection_on_returns = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    allow_lot_mixing_in_location = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    default_order_priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    config_revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_operational_preferences", x => x.preference_id);
                    table.ForeignKey(
                        name: "fk_client_operational_preferences_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_operational_preferences_facilities_facility_id",
                        column: x => x.facility_id,
                        principalTable: "facilities",
                        principalColumn: "facility_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_operational_preferences_warehouse_zones_default_rece",
                        column: x => x.default_receiving_zone_id,
                        principalTable: "warehouse_zones",
                        principalColumn: "zone_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_operational_preferences_warehouse_zones_default_ship",
                        column: x => x.default_shipping_zone_id,
                        principalTable: "warehouse_zones",
                        principalColumn: "zone_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_addresses",
                columns: table => new
                {
                    customer_address_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Delivery"),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    recipient_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line_1 = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    address_line_2 = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state_or_province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    country_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    country_normalization_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PendingNormalization"),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    delivery_instructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    config_revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_addresses", x => x.customer_address_id);
                    table.ForeignKey(
                        name: "fk_customer_addresses_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "customer_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_contacts",
                columns: table => new
                {
                    supplier_contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role_title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    config_revision = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_contacts", x => x.supplier_contact_id);
                    table.ForeignKey(
                        name: "fk_supplier_contacts_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "supplier_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(@"
DO $$
DECLARE
    dup_clients text;
    dup_customers text;
    dup_suppliers text;
    dup_primary_fac text;
BEGIN
    SELECT string_agg(norm_code || ' (' || cnt || ')', ', ')
    INTO dup_clients
    FROM (
        SELECT UPPER(BTRIM(account_code)) AS norm_code, COUNT(*) AS cnt
        FROM clients
        GROUP BY UPPER(BTRIM(account_code))
        HAVING COUNT(*) > 1
    ) t;
    IF dup_clients IS NOT NULL THEN
        RAISE EXCEPTION 'MDM-2.2 preflight failed: duplicate normalized client account_code values detected: %', dup_clients;
    END IF;

    SELECT string_agg(client_id::text || ':' || norm_code || ' (' || cnt || ')', ', ')
    INTO dup_customers
    FROM (
        SELECT client_id, UPPER(BTRIM(customer_code)) AS norm_code, COUNT(*) AS cnt
        FROM customers
        GROUP BY client_id, UPPER(BTRIM(customer_code))
        HAVING COUNT(*) > 1
    ) t;
    IF dup_customers IS NOT NULL THEN
        RAISE EXCEPTION 'MDM-2.2 preflight failed: duplicate normalized customer_code values within tenant detected: %', dup_customers;
    END IF;

    SELECT string_agg(client_id::text || ':' || norm_code || ' (' || cnt || ')', ', ')
    INTO dup_suppliers
    FROM (
        SELECT client_id, UPPER(BTRIM(supplier_code)) AS norm_code, COUNT(*) AS cnt
        FROM suppliers
        GROUP BY client_id, UPPER(BTRIM(supplier_code))
        HAVING COUNT(*) > 1
    ) t;
    IF dup_suppliers IS NOT NULL THEN
        RAISE EXCEPTION 'MDM-2.2 preflight failed: duplicate normalized supplier_code values within tenant detected: %', dup_suppliers;
    END IF;

    -- Deterministically reconcile legacy pre-MDM-2.2 clients that had multiple active primary_operating_facility rows
    -- by preserving the earliest active assignment (effective_date ASC, access_id ASC) as primary.
    WITH ranked AS (
        SELECT access_id,
               ROW_NUMBER() OVER (PARTITION BY client_id ORDER BY effective_date ASC, access_id ASC) AS rn
        FROM client_facility_access
        WHERE primary_operating_facility = TRUE AND status = 'Active'
    )
    UPDATE client_facility_access cfa
    SET primary_operating_facility = FALSE
    FROM ranked r
    WHERE cfa.access_id = r.access_id AND r.rn > 1;
END $$;

UPDATE clients SET normalized_account_code = UPPER(BTRIM(account_code)) WHERE normalized_account_code = '';
UPDATE customers SET normalized_customer_code = UPPER(BTRIM(customer_code)) WHERE normalized_customer_code = '';
UPDATE suppliers SET normalized_supplier_code = UPPER(BTRIM(supplier_code)) WHERE normalized_supplier_code = '';
");

            migrationBuilder.CreateIndex(
                name: "ux_suppliers_client_normalized_code",
                table: "suppliers",
                columns: new[] { "client_id", "normalized_supplier_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_customers_client_normalized_code",
                table: "customers",
                columns: new[] { "client_id", "normalized_customer_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_clients_normalized_account_code",
                table: "clients",
                column: "normalized_account_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_client_facility_access_primary_active",
                table: "client_facility_access",
                column: "client_id",
                unique: true,
                filter: "primary_operating_facility = TRUE AND status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_client_operational_preferences_default_receiving_zone_id",
                table: "client_operational_preferences",
                column: "default_receiving_zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_operational_preferences_default_shipping_zone_id",
                table: "client_operational_preferences",
                column: "default_shipping_zone_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_operational_preferences_facility_id",
                table: "client_operational_preferences",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ux_client_op_prefs_client_default",
                table: "client_operational_preferences",
                column: "client_id",
                unique: true,
                filter: "facility_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_client_op_prefs_client_facility",
                table: "client_operational_preferences",
                columns: new[] { "client_id", "facility_id" },
                unique: true,
                filter: "facility_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customer_addresses_client_customer",
                table: "customer_addresses",
                columns: new[] { "client_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_addresses_customer_id",
                table: "customer_addresses",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_contacts_client_supplier",
                table: "supplier_contacts",
                columns: new[] { "client_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_contacts_supplier_id",
                table: "supplier_contacts",
                column: "supplier_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_operational_preferences");

            migrationBuilder.DropTable(
                name: "customer_addresses");

            migrationBuilder.DropTable(
                name: "supplier_contacts");

            migrationBuilder.DropIndex(
                name: "ux_suppliers_client_normalized_code",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "ux_customers_client_normalized_code",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ux_clients_normalized_account_code",
                table: "clients");

            migrationBuilder.DropIndex(
                name: "ux_client_facility_access_primary_active",
                table: "client_facility_access");

            migrationBuilder.DropColumn(
                name: "config_revision",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "lead_time_days",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "normalized_supplier_code",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "payment_terms_days",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "tax_registration_number",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "billing_address_line1",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "billing_address_line2",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "billing_city",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "billing_country_code",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "billing_country_normalization_status",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "billing_postal_code",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "billing_state_or_province",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "config_revision",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "normalized_customer_code",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "tax_registration_number",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "billing_address_line_1",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "billing_address_line_2",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "billing_city",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "billing_country_code",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "billing_postal_code",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "billing_state_or_province",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "business_classification",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "config_revision",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "country_normalization_status",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "normalized_account_code",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "tax_registration_number",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "config_revision",
                table: "client_facility_access");

            migrationBuilder.DropColumn(
                name: "remarks",
                table: "client_facility_access");

            migrationBuilder.DropColumn(
                name: "revoked_at_utc",
                table: "client_facility_access");

            migrationBuilder.DropColumn(
                name: "config_revision",
                table: "client_contacts");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "client_contacts");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "client_contacts");

            migrationBuilder.DropColumn(
                name: "role_title",
                table: "client_contacts");

            migrationBuilder.CreateIndex(
                name: "ix_client_facility_access_client_id",
                table: "client_facility_access",
                column: "client_id");
        }
    }
}
