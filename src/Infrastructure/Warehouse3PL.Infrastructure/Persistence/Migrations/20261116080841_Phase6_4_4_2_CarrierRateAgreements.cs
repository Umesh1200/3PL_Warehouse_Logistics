using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Warehouse3PL.Infrastructure.Persistence.Migrations;

public partial class Phase6_4_4_2_CarrierRateAgreements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // InitialCreate already owns both rate tables. Upgrade those tables in place.
        // IF NOT EXISTS also supports databases provisioned with the earlier complete schema.
        migrationBuilder.Sql("""
            ALTER TABLE carrier_rate_agreements
                ADD COLUMN IF NOT EXISTS is_deleted boolean NOT NULL DEFAULT false,
                ADD COLUMN IF NOT EXISTS deleted_at timestamp with time zone,
                ADD COLUMN IF NOT EXISTS deleted_by uuid;
            DO $migration$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_carrier_rate_agreements_clients_client_id'
                    AND conrelid = 'carrier_rate_agreements'::regclass) THEN
                    ALTER TABLE carrier_rate_agreements ADD CONSTRAINT fk_carrier_rate_agreements_clients_client_id
                        FOREIGN KEY (client_id) REFERENCES clients(client_id) ON DELETE RESTRICT;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_carrier_rate_agreements_facilities_facility_id'
                    AND conrelid = 'carrier_rate_agreements'::regclass) THEN
                    ALTER TABLE carrier_rate_agreements ADD CONSTRAINT fk_carrier_rate_agreements_facilities_facility_id
                        FOREIGN KEY (facility_id) REFERENCES facilities(facility_id) ON DELETE SET NULL;
                END IF;
            END $migration$;
            CREATE INDEX IF NOT EXISTS ix_carrier_rate_agreements_carrier_active
                ON carrier_rate_agreements (carrier_code, is_active, effective_from_utc);
            CREATE INDEX IF NOT EXISTS ix_carrier_rate_agreements_client_lookup
                ON carrier_rate_agreements (client_id, carrier_code, is_active);
            CREATE INDEX IF NOT EXISTS ix_carrier_rate_agreements_facility_lookup
                ON carrier_rate_agreements (facility_id, carrier_code, is_active);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Compatibility upgrades cannot know whether pre-existing columns contained evidence.
        throw new System.NotSupportedException("Carrier agreement compatibility repair is forward-only; restore a verified backup to downgrade.");
    }
}
