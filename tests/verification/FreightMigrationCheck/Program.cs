using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Persistence;

// Run only against a disposable local server. Databases are retained for inspection.
var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FREIGHT_MIGRATION_TEST_CONNECTION")
    ?? throw new InvalidOperationException("Set FREIGHT_MIGRATION_TEST_CONNECTION to a disposable local PostgreSQL server."));
if (connection.Host is not ("127.0.0.1" or "localhost")) throw new InvalidOperationException("Local PostgreSQL required.");
connection.Database = "postgres";
await using var admin = new NpgsqlConnection(connection.ConnectionString);
await admin.OpenAsync();
var suffix = Guid.NewGuid().ToString("N");
var scenarios = new List<string> { "fresh", "baseline", "existing", "applied" };
if (args.Length == 1) scenarios.Add("script");
foreach (var scenario in scenarios)
{
    var database = $"phase6442_{scenario}_{suffix}";
    await new NpgsqlCommand($"CREATE DATABASE {database}", admin).ExecuteNonQueryAsync();
    connection.Database = database;
    await using var db = new ApplicationDbContextFactory().CreateDbContext([]);
    db.Database.SetConnectionString(connection.ConnectionString);
    var migrator = db.GetService<IMigrator>();
    if (scenario == "script")
    {
        var script = await File.ReadAllTextAsync(args[0]);
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
    }
    else if (scenario == "fresh")
    {
        await migrator.MigrateAsync();
    }
    else
    {
        await migrator.MigrateAsync("20261115000000_InitialCreate");
        var client = new Client { CompanyName = "Migration evidence", AccountCode = "MIGRATION" };
        var facility = new Facility { FacilityCode = "MIG", Name = "Migration facility" };
        var shipment = new Shipment { ClientId = client.Id, FacilityId = facility.Id, ShipmentNumber = "MIGRATION", CarrierCode = "FEDEX" };
        db.Clients.Add(client);
        db.Facilities.Add(facility);
        db.Shipments.Add(shipment);
        db.CarrierCostRecords.Add(new CarrierCostRecord { ClientId = client.Id, FacilityId = facility.Id,
            ShipmentId = shipment.Id, CarrierCode = "FEDEX", BaseTransportationCost = 37m, TotalCarrierCost = 37m,
            CostAssessedAtUtc = DateTime.UtcNow, Status = CarrierCostStatus.Invoiced });
        db.ClientFreightAgreements.Add(new ClientFreightAgreement { ClientId = client.Id, AgreementCode = "KEEP", Name = "Preserve commercial terms" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        // Raw insert uses the genuine baseline schema, before soft-delete/routing columns exist.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO carrier_rate_agreements
              (carrier_rate_agreement_id, carrier_code, agreement_code, name, facility_id, client_id,
               rate_model, tier_model, base_rate, unit_of_measure, currency, effective_from_utc, is_active, version, created_at)
            VALUES ({Guid.Parse("00000000-0000-0000-0000-000000000064")}, 'FEDEX', 'KEEP', 'Keep contract', {facility.Id}, {client.Id},
               'PerUnit', 'FlatRate', 7.25, 'KG', 'USD', {DateTime.UtcNow}, true, 1, {DateTime.UtcNow});
            INSERT INTO carrier_rate_tiers
              (carrier_rate_tier_id, carrier_rate_agreement_id, tier_number, lower_bound, rate, created_at)
            VALUES ({Guid.NewGuid()}, {Guid.Parse("00000000-0000-0000-0000-000000000064")}, 1, 0, 7.25, {DateTime.UtcNow});
            """);
        var before = await Fingerprints(db);
        if (scenario is "existing" or "applied")
        {
            // Simulate an EnsureCreated-style complete carrier schema without the migration stamp.
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE carrier_rate_agreements ADD COLUMN is_deleted boolean NOT NULL DEFAULT false,
                    ADD COLUMN deleted_at timestamp with time zone, ADD COLUMN deleted_by uuid;
                ALTER TABLE carrier_rate_agreements ADD CONSTRAINT fk_carrier_rate_agreements_clients_client_id
                    FOREIGN KEY (client_id) REFERENCES clients(client_id) ON DELETE RESTRICT;
                ALTER TABLE carrier_rate_agreements ADD CONSTRAINT fk_carrier_rate_agreements_facilities_facility_id
                    FOREIGN KEY (facility_id) REFERENCES facilities(facility_id) ON DELETE SET NULL;
                CREATE INDEX ix_carrier_rate_agreements_carrier_active ON carrier_rate_agreements(carrier_code, is_active, effective_from_utc);
                CREATE INDEX ix_carrier_rate_agreements_client_lookup ON carrier_rate_agreements(client_id, carrier_code, is_active);
                CREATE INDEX ix_carrier_rate_agreements_facility_lookup ON carrier_rate_agreements(facility_id, carrier_code, is_active);
                """);
            if (scenario == "applied")
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
                    VALUES ('20261116080841_Phase6_4_4_2_CarrierRateAgreements', '10.0.4');
                    """);
        }
        await migrator.MigrateAsync();
        if (before != await Fingerprints(db)) throw new Exception($"{scenario}: financial or operational evidence changed.");
        if (await db.CarrierRateAgreements.CountAsync() != 1 || await db.CarrierRateTiers.CountAsync() != 1)
            throw new Exception("Carrier agreement/tier evidence lost.");
        if ((await db.CarrierRateAgreements.SingleAsync()).BaseRate != 7.25m) throw new Exception("Contract changed.");
    }
    await migrator.MigrateAsync(); // Re-running a completed chain must be a no-op.
    await using var sql = new NpgsqlConnection(connection.ConnectionString);
    await sql.OpenAsync();
    var deleteRule = await new NpgsqlCommand("""
        SELECT confdeltype::text FROM pg_constraint
        WHERE conname = 'fk_carrier_rate_agreements_facilities_facility_id'
          AND conrelid = 'carrier_rate_agreements'::regclass
        """, sql).ExecuteScalarAsync();
    if (!Equals(deleteRule, "r")) throw new Exception("Facility scope can be widened by deletion.");
    var count = await new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\"", sql).ExecuteScalarAsync();
    if (!Equals(count, 3L)) throw new Exception("Incomplete migration history.");
    Console.WriteLine($"PASS {scenario}: {database}; migrations=3; facility FK=RESTRICT; evidence preserved");
}

static async Task<string> Fingerprints(ApplicationDbContext db)
{
    var parts = new List<string>();
    await using var sql = new NpgsqlConnection(db.Database.GetConnectionString());
    await sql.OpenAsync();
    foreach (var table in new[] { "clients", "facilities", "shipments", "carrier_cost_records", "client_freight_agreements", "invoices", "invoice_lines", "carrier_rate_tiers" })
    {
        var command = new NpgsqlCommand($"SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text, '[]') FROM {table} t", sql);
        parts.Add((string)(await command.ExecuteScalarAsync())!);
    }
    return string.Join("\n", parts);
}
