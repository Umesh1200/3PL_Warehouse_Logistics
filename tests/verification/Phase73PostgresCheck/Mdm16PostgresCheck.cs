using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inbound;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;

internal static class Mdm16PostgresCheck
{
    private const string Mdm15Migration = "20261126100927_MDM1_5_R01_OutboxBackoffAndRetention";
    private const string Mdm16Migration = "20261126143014_MDM1_6_LegacyCountryNormalization";
    private const string Mdm16R04Migration = "20261127051434_MDM1_6_R04_NormalizationRemediationLifecycle";
    private const string JwtSecret = "Nexus3PL-Mdm16-PostgreSql-Verification-Key-2026!";
    private const string JwtIssuer = "nexus3pl-mdm16-pg-auth";
    private const string JwtAudience = "nexus3pl-mdm16-pg-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static async Task RunAsync(string baseConnectionString)
    {
        Console.WriteLine("\n[MDM-1.6] Running Live PostgreSQL Legacy Country Normalization, R01-R05 Remediation, Precheck, Quarantine, Historical Immutability, Epoch Outbox, RBAC & Down/Up Tests...");

        // R04 Precheck: Empty DB without catalogs succeeds; Populated DB without catalogs FAILS CLOSED cleanly
        await VerifyR04EmptyAndMissingCatalogGuardsAsync(baseConnectionString);

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm16_pg_{Guid.NewGuid():N}";

        await using (var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConn.OpenAsync();
            await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", adminConn);
            await createCmd.ExecuteNonQueryAsync();
        }

        var targetBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = dbName };
        var targetConn = targetBuilder.ConnectionString;

        try
        {
            // 1. Migrate cleanly to MDM-1.5 and seed canonical catalogs (US + NP) + extensible 3rd country (CA)
            await using (var migDb = CreatePgDb(targetConn))
            {
                var migrator = migDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(Mdm15Migration);
                await ReferenceCatalogSeeder.SeedAsync(migDb);

                migDb.CountryProfiles.Add(CountryProfile.Create(
                    countryCode: "CA",
                    isoAlpha3Code: "CAN",
                    numericCode: "124",
                    name: "Canada",
                    defaultCurrencyCode: "CAD",
                    primaryTimeZoneId: "America/Toronto",
                    addressSchemaType: AddressSchemaType.UsStateZip,
                    postalCodeRegex: @"^[A-Z]\d[A-Z][ -]?\d[A-Z]\d$",
                    legacyPostalCodeRegex: null,
                    primaryCalendarSystem: PrimaryCalendarSystem.Gregorian,
                    fiscalCalendarSystem: FiscalCalendarSystem.Gregorian,
                    defaultFiscalYearStartMonth: 4,
                    seedVersion: 1));
                await migDb.SaveChangesAsync();
            }

            // 2. Insert realistic legacy pre-cutover records directly in PostgreSQL (MDM-1.5 schema)
            var facUsId = Guid.NewGuid();
            var facNpId = Guid.NewGuid();
            var facCaId = Guid.NewGuid();
            var facOrphanId = Guid.NewGuid();
            var facConflictId = Guid.NewGuid();
            var facUnresolvedId = Guid.NewGuid();

            var clientNpId = Guid.NewGuid();
            var clientUsId = Guid.NewGuid();
            var clientMixedId = Guid.NewGuid();

            var custInferNpId = Guid.NewGuid();
            var custUnknownId = Guid.NewGuid();
            var custMixedBlankId = Guid.NewGuid();
            var custPiiId = Guid.NewGuid();
            var supInferUsId = Guid.NewGuid();

            var draftOrderUsId = Guid.NewGuid();
            var completedOrderNpId = Guid.NewGuid();
            var xbrdOrderId = Guid.NewGuid();
            var noCustOrderId = Guid.NewGuid();
            var packedOrderId = Guid.NewGuid();

            var draftAddrId = Guid.NewGuid();
            var completedAddrId = Guid.NewGuid();
            var xbrdAddrId = Guid.NewGuid();
            var noCustAddrId = Guid.NewGuid();
            var packedAddrId = Guid.NewGuid();
            var closedShipmentId = Guid.NewGuid();
            var closedBolId = Guid.NewGuid();
            var closedInvoiceId = Guid.NewGuid();

            var activeSlaId = Guid.NewGuid();
            var inactiveSlaId = Guid.NewGuid();
            var slaRecordId = Guid.NewGuid();

            await using (var conn = new NpgsqlConnection(targetConn))
            {
                await conn.OpenAsync();

                async Task ExecAsync(string sql, params (string Name, object? Val)[] parameters)
                {
                    await using var cmd = new NpgsqlCommand(sql, conn);
                    foreach (var (name, val) in parameters)
                    {
                        cmd.Parameters.AddWithValue(name, val ?? DBNull.Value);
                    }
                    await cmd.ExecuteNonQueryAsync();
                }

                // Facilities: USA, NPL, CAN (3rd country), Orphan blank, Geo-conflicting US+Bagmati/44600
                const string insertFacSql = """
                    INSERT INTO facilities (facility_id, facility_code, name, country_code, state_or_province, postal_code, timezone, is_active, is_deleted, created_at)
                    VALUES (@id, @code, @name, @cc, @st, @zip, @tz, true, false, NOW());
                    """;
                await ExecAsync(insertFacSql, ("@id", facUsId), ("@code", "PG16-US"), ("@name", "PG16 US"), ("@cc", "USA"), ("@st", "Illinois"), ("@zip", "60601"), ("@tz", "America/Chicago"));
                await ExecAsync(insertFacSql, ("@id", facNpId), ("@code", "PG16-NP"), ("@name", "PG16 NP"), ("@cc", "NPL"), ("@st", "Bagmati"), ("@zip", "44600"), ("@tz", "Asia/Kathmandu"));
                await ExecAsync(insertFacSql, ("@id", facCaId), ("@code", "PG16-CA"), ("@name", "PG16 CA"), ("@cc", "CAN"), ("@st", "Ontario"), ("@zip", "M5V 2T6"), ("@tz", "America/Toronto"));
                await ExecAsync(insertFacSql, ("@id", facOrphanId), ("@code", "PG16-ORP"), ("@name", "PG16 Orphan"), ("@cc", ""), ("@st", ""), ("@zip", ""), ("@tz", "UTC"));
                await ExecAsync(insertFacSql, ("@id", facConflictId), ("@code", "PG16-CNF"), ("@name", "PG16 Conflict"), ("@cc", "US"), ("@st", "Bagmati"), ("@zip", "44600"), ("@tz", "Asia/Kathmandu"));
                await ExecAsync(insertFacSql, ("@id", facUnresolvedId), ("@code", "PG16-UNR"), ("@name", "PG16 Unresolved"), ("@cc", "ZZZ"), ("@st", ""), ("@zip", ""), ("@tz", "UTC"));

                // Clients + facility access bindings
                const string insertClientSql = """
                    INSERT INTO clients (client_id, account_code, company_name, status, sla_tier, currency, is_deleted, created_at)
                    VALUES (@id, @code, @name, 'Active', 'Standard', 'USD', false, NOW());
                    """;
                await ExecAsync(insertClientSql, ("@id", clientNpId), ("@code", "PG16-CLNP"), ("@name", "PG16 Nepal Client"));
                await ExecAsync(insertClientSql, ("@id", clientUsId), ("@code", "PG16-CLUS"), ("@name", "PG16 US Client"));
                await ExecAsync(insertClientSql, ("@id", clientMixedId), ("@code", "PG16-CLMX"), ("@name", "PG16 Mixed Resolved/Unresolved Client"));

                const string insertCfaSql = """
                    INSERT INTO client_facility_access (client_id, facility_id, effective_date, status, primary_operating_facility, created_at)
                    VALUES (@cid, @fid, NOW(), 'Active', true, NOW());
                    """;
                await ExecAsync(insertCfaSql, ("@cid", clientNpId), ("@fid", facNpId));
                await ExecAsync(insertCfaSql, ("@cid", clientUsId), ("@fid", facUsId));
                await ExecAsync(insertCfaSql, ("@cid", clientMixedId), ("@fid", facUsId));
                await ExecAsync(insertCfaSql, ("@cid", clientMixedId), ("@fid", facUnresolvedId));

                // Customers (including unresolved-network customer & synthetic PII token)
                const string insertCustSql = """
                    INSERT INTO customers (customer_id, client_id, customer_code, customer_name, customer_type, email, default_state_or_province, default_postal_code, default_country_code, is_active, version, is_deleted, created_at)
                    VALUES (@id, @cid, @code, @name, 'RetailCustomer', @email, @st, @zip, @cc, true, 1, false, NOW());
                    """;
                await ExecAsync(insertCustSql, ("@id", custInferNpId), ("@cid", clientNpId), ("@code", "C16-NP"), ("@name", "PII Hidden Name"), ("@email", "pii-cust@pg16.test"), ("@st", "Bagmati"), ("@zip", "44600"), ("@cc", ""));
                await ExecAsync(insertCustSql, ("@id", custUnknownId), ("@cid", clientUsId), ("@code", "C16-ZZ"), ("@name", "Unknown Cust"), ("@email", "unk@pg16.test"), ("@st", "Illinois"), ("@zip", "60601"), ("@cc", "ZZZ"));
                await ExecAsync(insertCustSql, ("@id", custMixedBlankId), ("@cid", clientMixedId), ("@code", "C16-MX"), ("@name", "Mixed Blank Cust"), ("@email", "mix@pg16.test"), ("@st", "Illinois"), ("@zip", "60601"), ("@cc", ""));
                await ExecAsync(insertCustSql, ("@id", custPiiId), ("@cid", clientNpId), ("@code", "C16-PII"), ("@name", "PII Cust"), ("@email", "p@pg16.test"), ("@st", "Bagmati"), ("@zip", "44600"), ("@cc", "a@b.test"));

                // Supplier (blank -> infer US)
                await ExecAsync("""
                    INSERT INTO suppliers (supplier_id, client_id, supplier_code, supplier_name, state, postal_code, country, is_active, is_deleted, created_at)
                    VALUES (@id, @cid, 'S16-US', 'PG16 Supplier', 'Illinois', '60601', NULL, true, false, NOW());
                    """, ("@id", supInferUsId), ("@cid", clientUsId));

                // Outbound orders + ShippingAddresses (Draft vs Historical Completed vs Cross-border blank vs No-Customer blank vs Packed package historical)
                const string insertOrderSql = """
                    INSERT INTO outbound_orders (order_id, client_id, facility_id, customer_id, order_number, order_type, status, delivery_status, priority, service_level, customer_name, order_date_utc, version, is_deleted, created_at)
                    VALUES (@id, @cid, @fid, @custid, @num, 'B2BSalesOrder', @st, 'PendingShipment', 1, 'StandardGround', 'PII Order Cust', NOW(), 1, false, NOW());
                    """;
                await ExecAsync(insertOrderSql, ("@id", draftOrderUsId), ("@cid", clientUsId), ("@fid", facUsId), ("@custid", custUnknownId), ("@num", "PG16-ORD-DRAFT"), ("@st", "Draft"));
                await ExecAsync(insertOrderSql, ("@id", completedOrderNpId), ("@cid", clientNpId), ("@fid", facNpId), ("@custid", custInferNpId), ("@num", "PG16-ORD-COMP"), ("@st", "Completed"));
                await ExecAsync(insertOrderSql, ("@id", xbrdOrderId), ("@cid", clientNpId), ("@fid", facUsId), ("@custid", custInferNpId), ("@num", "PG16-ORD-XBRD"), ("@st", "Draft"));
                await ExecAsync(insertOrderSql, ("@id", noCustOrderId), ("@cid", clientUsId), ("@fid", facUsId), ("@custid", null), ("@num", "PG16-ORD-NOCUST"), ("@st", "Draft"));
                await ExecAsync(insertOrderSql, ("@id", packedOrderId), ("@cid", clientUsId), ("@fid", facUsId), ("@custid", custUnknownId), ("@num", "PG16-ORD-PACK"), ("@st", "Draft"));

                const string insertAddrSql = """
                    INSERT INTO shipping_addresses (shipping_address_id, order_id, recipient_name, address_line1, city, state_or_province, postal_code, country_code, is_amended, created_at_utc, is_deleted)
                    VALUES (@id, @oid, 'PII Recipient Secret', '999 Secret Way', 'City', @st, @zip, @cc, false, NOW(), false);
                    """;
                await ExecAsync(insertAddrSql, ("@id", draftAddrId), ("@oid", draftOrderUsId), ("@st", "Illinois"), ("@zip", "60601"), ("@cc", "USA"));
                await ExecAsync(insertAddrSql, ("@id", completedAddrId), ("@oid", completedOrderNpId), ("@st", "Bagmati"), ("@zip", "44600"), ("@cc", "NPL"));
                await ExecAsync(insertAddrSql, ("@id", xbrdAddrId), ("@oid", xbrdOrderId), ("@st", "Illinois"), ("@zip", "60601"), ("@cc", ""));
                await ExecAsync(insertAddrSql, ("@id", noCustAddrId), ("@oid", noCustOrderId), ("@st", "Illinois"), ("@zip", "60601"), ("@cc", ""));
                await ExecAsync(insertAddrSql, ("@id", packedAddrId), ("@oid", packedOrderId), ("@st", "Illinois"), ("@zip", "60601"), ("@cc", "USA"));

                // CarrierContractualSlaPolicies & Historical CarrierSlaComplianceRecord
                const string insertSlaSql = """
                    INSERT INTO carrier_contractual_sla_policies (policy_id, carrier_code, carrier_service_level, client_id, origin_facility_id, origin_region, destination_country, destination_state_or_region, destination_postal_prefix, transit_commitment_days, calculation_type, daily_cutoff_time, cutoff_time_zone, effective_from_utc, sla_policy_version, is_active, notes, created_at)
                    VALUES (@id, 'UPS', 'StandardGround', @cid, @fid, 'NA', @cc, @st, @zip, 2, 'BusinessDays', '17:00:00', 'UTC', NOW(), '1.0.0', @act, '', NOW());
                    """;
                await ExecAsync(insertSlaSql, ("@id", activeSlaId), ("@cid", clientUsId), ("@fid", facUsId), ("@cc", "USA"), ("@st", "Illinois"), ("@zip", "606"), ("@act", true));
                await ExecAsync(insertSlaSql, ("@id", inactiveSlaId), ("@cid", clientNpId), ("@fid", facNpId), ("@cc", "Nepal"), ("@st", "Bagmati"), ("@zip", "446"), ("@act", false));

                await ExecAsync("""
                    INSERT INTO carrier_sla_compliance_records (compliance_record_id, shipment_id, tracking_number, carrier_code, carrier_service_level, facility_id, client_id, origin_region, destination_country, destination_state_or_region, destination_postal_prefix, dispatched_at_utc, attempt_count, is_first_attempt_success, contractual_transit_days, has_delivery_exception, qualifying_exceptions_count, sla_policy_version, evaluation_status, created_at)
                    VALUES (@id, @sid, 'PG16-HIST-TRK', 'NPX', 'StandardGround', @fid, @cid, 'NP', 'NPL', 'Bagmati', '44600', NOW() - INTERVAL '10 days', 1, true, 2, false, 0, '1.0.0', 'Evaluated', NOW());
                    """, ("@id", slaRecordId), ("@sid", Guid.NewGuid()), ("@fid", facNpId), ("@cid", clientNpId));

                // 3. Inject pre-cutover defect (duplicate case-insensitive scope key in configuration_scope_epochs) & assert FAIL-CLOSED rollback
                var badEpochId = Guid.NewGuid();
                await ExecAsync("""
                    INSERT INTO configuration_scope_epochs (epoch_id, scope_type, scope_key, current_epoch, updated_at_utc)
                    VALUES (@eid1, 'Country', 'MDM16PRECHECK', 1, NOW()),
                           (@eid2, 'Country', 'mdm16precheck', 1, NOW());
                    """, ("@eid1", badEpochId), ("@eid2", Guid.NewGuid()));

                bool blockedAsExpected = false;
                try
                {
                    await using var badMigDb = CreatePgDb(targetConn);
                    var migrator = badMigDb.GetInfrastructure().GetRequiredService<IMigrator>();
                    await migrator.MigrateAsync(Mdm16Migration);
                }
                catch (Exception ex) when (ex.Message.Contains("conflicting scope epoch keys", StringComparison.OrdinalIgnoreCase) ||
                                           (ex.InnerException?.Message.Contains("conflicting scope epoch keys", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    blockedAsExpected = true;
                }

                if (!blockedAsExpected)
                {
                    throw new InvalidOperationException("MDM-1.6 pre-cutover guard failed to block duplicate scope_epochs.");
                }

                // Verify full rollback left facilities.country_code untouched and created no country_normalization_audits table
                await using (var chkCmd = new NpgsqlCommand("SELECT country_code FROM facilities WHERE facility_id = @id", conn))
                {
                    chkCmd.Parameters.AddWithValue("@id", facUsId);
                    var ccBefore = (string?)await chkCmd.ExecuteScalarAsync();
                    if (ccBefore != "USA")
                        throw new InvalidOperationException($"Expected pre-cutover failure rollback to preserve 'USA', got '{ccBefore}'.");
                }
                await using (var tblCmd = new NpgsqlCommand("SELECT to_regclass('country_normalization_audits')::text", conn))
                {
                    if ((await tblCmd.ExecuteScalarAsync()) is string)
                        throw new InvalidOperationException("Expected pre-cutover failure rollback to leave country_normalization_audits uncreated.");
                }

                // Fix pre-cutover defect before real upgrade
                await ExecAsync("DELETE FROM configuration_scope_epochs WHERE scope_key IN ('MDM16PRECHECK', 'mdm16precheck');");
            }

            // 2b. Seed Closed Shipment, BillOfLading, HandlingUnit+Packed Package & Issued Invoice snapshots via EF at MDM-1.5 schema
            await using (var snapDb = CreatePgDb(targetConn))
            {
                var huId = Guid.NewGuid();
                var bpId = Guid.NewGuid();
                snapDb.HandlingUnits.Add(new HandlingUnit
                {
                    Id = huId,
                    ClientId = clientUsId,
                    FacilityId = facUsId,
                    LpnCode = "PG16-HU-1",
                    UnitType = HandlingUnitType.ShippingCarton,
                    Status = HandlingUnitStatus.Available
                });
                snapDb.Packages.Add(new Package
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientUsId,
                    FacilityId = facUsId,
                    OutboundOrderId = packedOrderId,
                    HandlingUnitId = huId,
                    PackageNumber = "PG16-PKG-1",
                    Status = PackageStatus.Packed
                });
                snapDb.Shipments.Add(new Shipment
                {
                    Id = closedShipmentId,
                    ClientId = clientUsId,
                    FacilityId = facUsId,
                    ShipmentNumber = "PG16-SHP-1",
                    CarrierCode = "UPS",
                    Status = ShipmentStatus.Dispatched,
                    BillOfLadingNumber = "PG16-BOL-1"
                });
                snapDb.BillsOfLading.Add(new BillOfLading
                {
                    Id = closedBolId,
                    ClientId = clientUsId,
                    FacilityId = facUsId,
                    ShipmentId = closedShipmentId,
                    BolNumber = "PG16-BOL-1",
                    CarrierCode = "UPS",
                    CarrierName = "UPS",
                    ConsigneeName = "Consignee",
                    ConsigneeAddressSnapshot = "999 Legacy Way, Chicago IL, USA",
                    DocumentSnapshotJson = "{\"country\":\"USA\"}"
                });
                snapDb.BillingPeriods.Add(new BillingPeriod
                {
                    Id = bpId,
                    ClientId = clientUsId,
                    PeriodCode = "2026-11",
                    Name = "Nov 2026",
                    StartDateUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDateUtc = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc)
                });
                snapDb.Invoices.Add(new Invoice
                {
                    Id = closedInvoiceId,
                    ClientId = clientUsId,
                    FacilityId = facUsId,
                    BillingPeriodId = bpId,
                    InvoiceNumber = "PG16-INV-1",
                    Status = InvoiceStatus.Issued,
                    Currency = "USD",
                    InvoiceDate = DateTime.UtcNow.AddDays(-2),
                    DueDate = DateTime.UtcNow.AddDays(28)
                });
                await snapDb.SaveChangesAsync();
            }

            // 4. Clean upgrade MDM1_5 -> MDM1_6 -> MDM1_6_R04 against realistic PG rows
            await using (var upDb = CreatePgDb(targetConn))
            {
                var migrator = upDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(Mdm16Migration);
                await migrator.MigrateAsync();
                await ReferenceCatalogSeeder.SeedAsync(upDb);

                // Verify facilities
                var fUs = await upDb.Facilities.IgnoreQueryFilters().SingleAsync(f => f.Id == facUsId);
                var fNp = await upDb.Facilities.IgnoreQueryFilters().SingleAsync(f => f.Id == facNpId);
                var fCa = await upDb.Facilities.IgnoreQueryFilters().SingleAsync(f => f.Id == facCaId);
                var fOrp = await upDb.Facilities.IgnoreQueryFilters().SingleAsync(f => f.Id == facOrphanId);
                var fCnf = await upDb.Facilities.IgnoreQueryFilters().SingleAsync(f => f.Id == facConflictId);
                if (fUs.CountryCode != "US" || fNp.CountryCode != "NP" || fCa.CountryCode != "CA")
                    throw new InvalidOperationException($"PG facility normalization mismatch: US={fUs.CountryCode}, NP={fNp.CountryCode}, CA={fCa.CountryCode}");
                if (fOrp.CountryNormalizationStatus != CountryNormalizationStatus.PendingNormalization ||
                    fCnf.CountryNormalizationStatus != CountryNormalizationStatus.PendingNormalization)
                    throw new InvalidOperationException("PG orphan/conflict facility was not quarantined to PendingNormalization.");

                // Verify inferred + quarantined Customers & Suppliers (including R02 unresolved multi-facility network & R05 PII redaction)
                var cInf = await upDb.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custInferNpId);
                var cUnk = await upDb.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custUnknownId);
                var cMix = await upDb.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custMixedBlankId);
                var cPii = await upDb.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custPiiId);
                var sInf = await upDb.Suppliers.IgnoreQueryFilters().SingleAsync(s => s.Id == supInferUsId);
                if (cInf.DefaultCountryCode != "NP" || cUnk.DefaultCountryCode != "ZZZ" ||
                    cUnk.CountryNormalizationStatus != CountryNormalizationStatus.PendingNormalization ||
                    cMix.DefaultCountryCode != "" || cMix.CountryNormalizationStatus != CountryNormalizationStatus.PendingNormalization ||
                    cPii.CountryNormalizationStatus != CountryNormalizationStatus.PendingNormalization || sInf.Country != "US")
                    throw new InvalidOperationException("PG Customer/Supplier deterministic backfill mismatch.");

                var piiAudit = await upDb.CountryNormalizationAudits.SingleAsync(a => a.EntityType == "Customer" && a.EntityId == custPiiId);
                if (piiAudit.OriginalCountryValue != CountryDiagnosticSanitizer.RedactedPlaceholder ||
                    piiAudit.Reason.Contains("a@b.test", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(piiAudit.InitialCorrelationBatchId))
                    throw new InvalidOperationException("PG SQL PII redaction or initial provenance tracking failed.");

                // Verify historical immutability vs mutable draft vs cross-border quarantine vs no-origin-inference vs packed package/BOL/invoice snapshot
                var aDraft = await upDb.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == draftAddrId);
                var aComp = await upDb.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == completedAddrId);
                var aXbrd = await upDb.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == xbrdAddrId);
                var aNoCust = await upDb.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == noCustAddrId);
                var aPacked = await upDb.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == packedAddrId);
                var bolRow = await upDb.BillsOfLading.IgnoreQueryFilters().SingleAsync(b => b.Id == closedBolId);
                var invRow = await upDb.Invoices.IgnoreQueryFilters().SingleAsync(i => i.Id == closedInvoiceId);
                if (aDraft.CountryCode != "US" || aComp.CountryCode != "NPL" ||
                    aComp.CountryNormalizationStatus != CountryNormalizationStatus.Normalized ||
                    aXbrd.CountryNormalizationStatus != CountryNormalizationStatus.PendingNormalization ||
                    aNoCust.CountryCode != "" || aNoCust.CountryNormalizationStatus != CountryNormalizationStatus.PendingNormalization ||
                    aPacked.CountryCode != "USA" || aPacked.CountryNormalizationStatus != CountryNormalizationStatus.Normalized ||
                    bolRow.ConsigneeAddressSnapshot != "999 Legacy Way, Chicago IL, USA" ||
                    invRow.Currency != "USD")
                    throw new InvalidOperationException(
                        $"PG ShippingAddress/BOL/Invoice immutability or R02 inference check failed: Draft={aDraft.CountryCode}, Comp={aComp.CountryCode}/{aComp.CountryNormalizationStatus}, Xbrd={aXbrd.CountryCode}/{aXbrd.CountryNormalizationStatus}, NoCust={aNoCust.CountryCode}/{aNoCust.CountryNormalizationStatus}, Packed={aPacked.CountryCode}/{aPacked.CountryNormalizationStatus}, Bol={bolRow.ConsigneeAddressSnapshot}, Inv={invRow.Currency}");

                var pAct = await upDb.CarrierContractualSlaPolicies.IgnoreQueryFilters().SingleAsync(p => p.Id == activeSlaId);
                var pInact = await upDb.CarrierContractualSlaPolicies.IgnoreQueryFilters().SingleAsync(p => p.Id == inactiveSlaId);
                var slaRec = await upDb.CarrierSlaComplianceRecords.IgnoreQueryFilters().SingleAsync(r => r.Id == slaRecordId);
                if (pAct.DestinationCountry != "US" || pInact.DestinationCountry != "Nepal" || slaRec.DestinationCountry != "NPL")
                    throw new InvalidOperationException("PG SLA historical immutability check failed.");

                // Verify MDM-1.5 Epoch & Outbox rows emitted by SQL migration when Facility country changed
                var hasGlobalEpoch = await upDb.ConfigurationScopeEpochs.AnyAsync(e => e.ScopeType == ConfigurationScopeType.Global && e.ScopeKey == "GLOBAL" && e.CurrentEpoch >= 1);
                var hasFacOutbox = await upDb.ConfigurationInvalidationEvents.AnyAsync(e => e.EntityType == "Facility" && e.EntityId == facUsId.ToString());
                if (!hasGlobalEpoch || !hasFacOutbox)
                    throw new InvalidOperationException("PG MDM-1.5 epoch/outbox propagation missing after Facility country mutation.");

                // 5. Idempotent C# backfill re-run must mutate 0 rows and preserve OriginalCountryValue
                var svc = new LegacyCountryNormalizationService(upDb);
                var rerun = await svc.BackfillAllAsync("PG16-RERUN");
                if (rerun.NormalizedMutatedCount != 0)
                    throw new InvalidOperationException($"Expected 0 mutations on idempotent PG rerun, got {rerun.NormalizedMutatedCount}.");

                var auditFacUs = await upDb.CountryNormalizationAudits.SingleAsync(a => a.EntityType == "Facility" && a.EntityId == facUsId);
                var initialBatchBefore = auditFacUs.InitialCorrelationBatchId;
                if (auditFacUs.OriginalCountryValue != "USA" || auditFacUs.NormalizedCountryCode != "US")
                    throw new InvalidOperationException("PG audit provenance OriginalCountryValue was overwritten on rerun.");

                // 5a-bis. R03 Audit lifecycle transition (Pending -> Normalized) & Deleted entity audit cleanup on PG
                var facOrphEntity = await upDb.Facilities.SingleAsync(f => f.Id == facOrphanId);
                facOrphEntity.CountryCode = "NP";
                facOrphEntity.StateOrProvince = "Bagmati";
                facOrphEntity.PostalCode = "44600";
                facOrphEntity.Timezone = "Asia/Kathmandu";
                var custPiiEntity = await upDb.Customers.SingleAsync(c => c.Id == custPiiId);
                custPiiEntity.IsDeleted = true;
                await upDb.SaveChangesAsync();

                // F01: Runtime SaveChangesAsync must atomically reconcile Pending->Normalized and clean up soft-deleted audits without waiting for BackfillAllAsync
                var orphRuntimeAudit = await upDb.CountryNormalizationAudits.SingleAsync(a => a.EntityType == "Facility" && a.EntityId == facOrphanId);
                if (orphRuntimeAudit.NormalizationStatus != CountryNormalizationStatus.Normalized ||
                    orphRuntimeAudit.NormalizedCountryCode != "NP" ||
                    orphRuntimeAudit.CorrelationBatchId != "RUNTIME-WRITE" ||
                    orphRuntimeAudit.InitialCorrelationBatchId == "RUNTIME-WRITE")
                    throw new InvalidOperationException($"PG F01 runtime Pending->Normalized audit reconciliation or initial provenance failed: Status={orphRuntimeAudit.NormalizationStatus}, Code={orphRuntimeAudit.NormalizedCountryCode}, Batch={orphRuntimeAudit.CorrelationBatchId}, Initial={orphRuntimeAudit.InitialCorrelationBatchId}");
                if (await upDb.CountryNormalizationAudits.AnyAsync(a => a.EntityType == "Customer" && a.EntityId == custPiiId))
                    throw new InvalidOperationException("PG F01 runtime soft-deleted entity audit cleanup failed.");

                await svc.BackfillAllAsync("PG16-R03-RECONCILE");
                var orphAudit = await upDb.CountryNormalizationAudits.SingleAsync(a => a.EntityType == "Facility" && a.EntityId == facOrphanId);
                if (orphAudit.NormalizationStatus != CountryNormalizationStatus.Normalized ||
                    orphAudit.NormalizedCountryCode != "NP" ||
                    orphAudit.InitialCorrelationBatchId == "PG16-R03-RECONCILE" ||
                    orphAudit.InitialCorrelationBatchId == "RUNTIME-WRITE")
                    throw new InvalidOperationException("PG R03 Pending->Normalized audit reconciliation or initial provenance failed.");

                // Restore facOrphan back to PendingNormalization for remaining checks
                facOrphEntity.CountryCode = "";
                facOrphEntity.Timezone = "UTC";
                await upDb.SaveChangesAsync();
                await svc.BackfillAllAsync("PG16-R03-RESTORE");

                // 5b. Transactional rollback safety check for caller-controlled RollbackAsync
                await using (var tx = await upDb.Database.BeginTransactionAsync())
                {
                    upDb.Facilities.Add(new Facility
                    {
                        Id = Guid.NewGuid(),
                        FacilityCode = "PG16-RB",
                        Name = "PG16 Rollback Probe",
                        CountryCode = "USA",
                        Timezone = "America/Chicago",
                        IsActive = true
                    });
                    await upDb.SaveChangesAsync();
                    await svc.BackfillAllAsync("PG16-ROLLBACK-TX");
                    await tx.RollbackAsync();
                }

                if (await upDb.Facilities.IgnoreQueryFilters().AnyAsync(f => f.FacilityCode == "PG16-RB"))
                    throw new InvalidOperationException("Expected aborted backfill transaction to roll back completely.");
            }

            // 6. Real JWT HTTP RBAC, Facility Isolation & Zero-PII verification against live PostgreSQL
            var staffPlatId = Guid.NewGuid();
            var staffFacNpId = Guid.NewGuid();
            var clientUserId = Guid.NewGuid();
            var driverUserId = Guid.NewGuid();

            await using (var authDb = CreatePgDb(targetConn))
            {
                var platRole = new Role { RoleName = "Pg16Plat", Description = "Plat", IsSystemRole = false };
                platRole.RolePermissions.Add(new RolePermission { Role = platRole, PermissionKey = Permissions.Global.NormalizationView, AccessLevel = AccessLevel.Read });
                var facRole = new Role { RoleName = "Pg16Fac", Description = "Fac", IsSystemRole = false };
                facRole.RolePermissions.Add(new RolePermission { Role = facRole, PermissionKey = Permissions.Facilities.Manage, AccessLevel = AccessLevel.Admin });
                authDb.Roles.AddRange(platRole, facRole);

                var uPlat = new User { Id = staffPlatId, Email = "pg16plat@m16.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };
                var uFac = new User { Id = staffFacNpId, Email = "pg16fac@m16.test", FirstName = "F", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facNpId, Status = UserStatus.Active, PasswordHash = "x" };
                var uCli = new User { Id = clientUserId, Email = "pg16cli@m16.test", FirstName = "C", LastName = "U", UserType = UserType.Client, ClientId = clientNpId, Status = UserStatus.Active, PasswordHash = "x" };
                var uDrv = new User { Id = driverUserId, Email = "pg16drv@m16.test", FirstName = "D", LastName = "U", UserType = UserType.Driver, Status = UserStatus.Active, PasswordHash = "x" };
                authDb.Users.AddRange(uPlat, uFac, uCli, uDrv);
                authDb.UserRoles.AddRange(new UserRole { User = uPlat, Role = platRole }, new UserRole { User = uFac, Role = facRole });
                await authDb.SaveChangesAsync();
            }

            await using (var host = await BuildPgHttpAppAsync(targetConn))
            {
                using var http = host.GetTestClient();
                string Token(Guid uid, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
                {
                    using var scope = host.Services.CreateScope();
                    return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateTokens(new User
                    {
                        Id = uid, Email = $"{uid:N}@pg16.test", FirstName = "T", LastName = "U", UserType = ut, ClientId = cid, AssignedFacilityId = fid
                    }, roles, perms).AccessToken;
                }

                var jwtPlat = Token(staffPlatId, UserType.Staff, null, null, ["Pg16Plat"], [Permissions.Global.NormalizationView]);
                var jwtFacNp = Token(staffFacNpId, UserType.Staff, null, facNpId, ["Pg16Fac"], [Permissions.Facilities.Manage]);
                var jwtCli = Token(clientUserId, UserType.Client, clientNpId, null, [], [Permissions.Global.NormalizationView]);
                var jwtDrv = Token(driverUserId, UserType.Driver, null, null, [], [Permissions.Global.NormalizationView]);

                if ((await http.GetAsync("/api/v1/reference/normalization-quarantine")).StatusCode != HttpStatusCode.Unauthorized ||
                    (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine", jwtCli))).StatusCode != HttpStatusCode.Forbidden ||
                    (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine", jwtDrv))).StatusCode != HttpStatusCode.Forbidden ||
                    (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/reference/normalization-quarantine?facilityId={facUsId}", jwtFacNp))).StatusCode != HttpStatusCode.Forbidden ||
                    (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?pageNumber=2147483647&pageSize=20", jwtPlat))).StatusCode != HttpStatusCode.BadRequest)
                {
                    throw new InvalidOperationException("PG MDM-1.6 RBAC, facility-isolation, or pagination guard failed.");
                }

                var hdrConflictReq = Authed(HttpMethod.Get, $"/api/v1/reference/normalization-quarantine?facilityId={facNpId}", jwtPlat);
                hdrConflictReq.Headers.Add("X-Facility-Id", facUsId.ToString());
                if ((await http.SendAsync(hdrConflictReq)).StatusCode != HttpStatusCode.BadRequest)
                {
                    throw new InvalidOperationException("PG MDM-1.6 conflicting facilityId/header guard failed.");
                }

                var facResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?includeAllStatuses=true&pageSize=100", jwtFacNp));
                var platResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/normalization-quarantine?includeAllStatuses=true&pageSize=100", jwtPlat));
                if (facResp.StatusCode != HttpStatusCode.OK || platResp.StatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException("PG MDM-1.6 diagnostics query failed.");

                var facJson = await facResp.Content.ReadAsStringAsync();
                var platJson = await platResp.Content.ReadAsStringAsync();
                if (platJson.Contains("PII ", StringComparison.OrdinalIgnoreCase) || platJson.Contains("pii-cust@pg16.test", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("PG MDM-1.6 diagnostics endpoint leaked PII!");

                var facDto = JsonSerializer.Deserialize<PagedNormalizationQuarantineResponse>(facJson, JsonOptions)!;
                var platDto = JsonSerializer.Deserialize<PagedNormalizationQuarantineResponse>(platJson, JsonOptions)!;
                if (facDto.Items.Any(i => i.FacilityId == facUsId || i.FacilityId == facOrphanId) ||
                    platDto.PendingCount <= facDto.PendingCount)
                    throw new InvalidOperationException("PG MDM-1.6 facility scoping leaked cross-facility rows or counts.");
            }

            // 7. Clean Downgrade/Rollback verification (MDM1_6 -> MDM1_5) + Re-upgrade to latest cleanly
            await using (var downDb = CreatePgDb(targetConn))
            {
                var migrator = downDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(Mdm15Migration);

                await using var conn = new NpgsqlConnection(targetConn);
                await conn.OpenAsync();
                await using var chkDown = new NpgsqlCommand("SELECT to_regclass('country_normalization_audits')::text", conn);
                if ((await chkDown.ExecuteScalarAsync()) is string)
                    throw new InvalidOperationException("PG MDM-1.6 Down rollback failed to drop country_normalization_audits.");

                await migrator.MigrateAsync();
            }

            // 8. F02 Real PostgreSQL Concurrency & Transaction Boundary Verification (independent DbContexts)
            await VerifyF02RealPostgresConcurrencyAndTransactionsAsync(baseConnectionString);

            // 9. F03 Safe Historical Restoration Fixtures, F04 Allowlist Privacy & Independent SQL vs C# Parity
            await VerifyF03SafeHistoricalRestorationAndSqlCSharpParityAsync(baseConnectionString);

            Console.WriteLine("[MDM-1.6] ALL Live PostgreSQL Country Normalization, F01-F04 Remediation, Concurrency, SQL/C# Parity, Quarantine, Historical Immutability, Epoch & RBAC checks passed cleanly.");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            try
            {
                await using var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString);
                await adminConn.OpenAsync();
                await using var termCmd = new NpgsqlCommand(
                    $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}' AND pid <> pg_backend_pid();",
                    adminConn);
                await termCmd.ExecuteNonQueryAsync();
                await using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\";", adminConn);
                await dropCmd.ExecuteNonQueryAsync();
            }
            catch
            {
            }
        }
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string jwt)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return req;
    }

    private static ApplicationDbContext CreatePgDb(string connStr)
    {
        var user = new PgAdminUserContext(Guid.NewGuid());
        var audit = new AuditableEntityInterceptor(user, new DateTimeProvider());
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connStr, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(audit);
        return new ApplicationDbContext(builder.Options, new PgAdminTenantProvider(), user, audit);
    }

    private static async Task<WebApplication> BuildPgHttpAppAsync(string connStr)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<JwtOptions>(o =>
        {
            o.SecretKey = JwtSecret;
            o.Issuer = JwtIssuer;
            o.Audience = JwtAudience;
            o.ExpiryMinutes = 60;
        });

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ReferenceController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddScoped<ILegacyCountryNormalizationService, LegacyCountryNormalizationService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = JwtIssuer,
                    ValidAudience = JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = JwtStaffFacilityValidator.ValidateAsync
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        builder.Services.AddScoped<IDateTimeProvider, DateTimeProvider>();
        builder.Services.AddScoped<AuditableEntityInterceptor>();
        builder.Services.AddDbContext<ApplicationDbContext>(opts =>
            opts.UseNpgsql(connStr).UseSnakeCaseNamingConvention());
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthenticatedTenantResolution();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static async Task VerifyR04EmptyAndMissingCatalogGuardsAsync(string baseConnectionString)
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var emptyDbName = $"mdm16_r04_empty_{Guid.NewGuid():N}";
        var noCatDbName = $"mdm16_r04_nocat_{Guid.NewGuid():N}";

        async Task CreateTempDbAsync(string name)
        {
            await using var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString);
            await adminConn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", adminConn);
            await cmd.ExecuteNonQueryAsync();
        }

        async Task DropTempDbAsync(string name)
        {
            NpgsqlConnection.ClearAllPools();
            try
            {
                await using var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString);
                await adminConn.OpenAsync();
                await using var term = new NpgsqlCommand($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{name}' AND pid <> pg_backend_pid();", adminConn);
                await term.ExecuteNonQueryAsync();
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\";", adminConn);
                await drop.ExecuteNonQueryAsync();
            }
            catch
            {
            }
        }

        try
        {
            // 1. Fresh empty DB with NO CountryProfile catalog rows MUST succeed cleanly through Mdm16R04Migration
            await CreateTempDbAsync(emptyDbName);
            var emptyConn = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = emptyDbName }.ConnectionString;
            await using (var emptyDb = CreatePgDb(emptyConn))
            {
                var emptyMigrator = emptyDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await emptyMigrator.MigrateAsync();
            }

            // 2. Populated DB (has a Facility) without CountryProfile catalog MUST fail closed on Mdm16R04Migration
            await CreateTempDbAsync(noCatDbName);
            var noCatConn = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = noCatDbName }.ConnectionString;
            await using (var noCatDb = CreatePgDb(noCatConn))
            {
                var noCatMigrator = noCatDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await noCatMigrator.MigrateAsync(Mdm15Migration);
                await using var conn = new NpgsqlConnection(noCatConn);
                await conn.OpenAsync();
                await using var ins = new NpgsqlCommand("INSERT INTO facilities (facility_id, facility_code, name, country_code, timezone, is_active, is_deleted, created_at) VALUES (@id, 'NOCAT-PG', 'No Catalog PG', 'USA', 'America/Chicago', true, false, NOW());", conn);
                ins.Parameters.AddWithValue("@id", Guid.NewGuid());
                await ins.ExecuteNonQueryAsync();

                bool blocked = false;
                try
                {
                    await noCatMigrator.MigrateAsync(Mdm16R04Migration);
                }
                catch (Exception ex) when (ex.Message.Contains("CountryProfile", StringComparison.OrdinalIgnoreCase) ||
                                           (ex.InnerException?.Message.Contains("CountryProfile", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    blocked = true;
                }

                if (!blocked)
                {
                    throw new InvalidOperationException("R04 migration failed to abort on populated PostgreSQL DB missing CountryProfile/AdministrativeDivision catalogs.");
                }
            }
        }
        finally
        {
            await DropTempDbAsync(emptyDbName);
            await DropTempDbAsync(noCatDbName);
        }
    }

    private static async Task VerifyF02RealPostgresConcurrencyAndTransactionsAsync(string baseConnectionString)
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm16_f02_conc_{Guid.NewGuid():N}";

        await using (var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", adminConn);
            await cmd.ExecuteNonQueryAsync();
        }

        var connStr = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = dbName }.ConnectionString;

        try
        {
            var facUsId = Guid.NewGuid();
            var facParentId = Guid.NewGuid();
            var clientUsId = Guid.NewGuid();
            var clientParentId = Guid.NewGuid();
            var orderLifecycleId = Guid.NewGuid();
            var addrLifecycleId = Guid.NewGuid();
            var huLifecycleId = Guid.NewGuid();
            var custParentChildId = Guid.NewGuid();

            await using (var seedDb = CreatePgDb(connStr))
            {
                var migrator = seedDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync();
                await ReferenceCatalogSeeder.SeedAsync(seedDb);

                seedDb.Facilities.AddRange(
                    new Facility { Id = facUsId, FacilityCode = "F02-US", Name = "F02 US", CountryCode = "USA", StateOrProvince = "Illinois", PostalCode = "60601", Timezone = "America/Chicago", IsActive = true },
                    new Facility { Id = facParentId, FacilityCode = "F02-PAR", Name = "F02 Parent", CountryCode = "US", StateOrProvince = "Illinois", PostalCode = "60601", Timezone = "America/Chicago", IsActive = true });
                seedDb.Clients.AddRange(
                    new Client { Id = clientUsId, AccountCode = "F02-CLUS", CompanyName = "F02 US Client", Status = ClientStatus.Active },
                    new Client { Id = clientParentId, AccountCode = "F02-CLPR", CompanyName = "F02 Parent Client", Status = ClientStatus.Active });
                seedDb.ClientFacilityAccesses.AddRange(
                    new ClientFacilityAccess { ClientId = clientUsId, FacilityId = facUsId, Status = FacilityAccessStatus.Active },
                    new ClientFacilityAccess { ClientId = clientParentId, FacilityId = facParentId, Status = FacilityAccessStatus.Active });
                seedDb.Customers.Add(new Customer
                {
                    Id = custParentChildId,
                    ClientId = clientParentId,
                    CustomerCode = "F02-C-PAR",
                    CustomerName = "Parent Race Cust",
                    DefaultCountryCode = "",
                    DefaultStateOrProvince = "Bagmati",
                    DefaultPostalCode = "44600"
                });
                seedDb.OutboundOrders.Add(new OutboundOrder
                {
                    Id = orderLifecycleId,
                    ClientId = clientUsId,
                    FacilityId = facUsId,
                    OrderNumber = "F02-ORD-LC",
                    OrderType = OutboundOrderType.B2BSalesOrder,
                    Status = OutboundOrderStatus.Draft,
                    CustomerName = "Lifecycle Race"
                });
                seedDb.ShippingAddresses.Add(new ShippingAddress
                {
                    Id = addrLifecycleId,
                    OutboundOrderId = orderLifecycleId,
                    RecipientName = "Lifecycle Recip",
                    AddressLine1 = "1 Race Rd",
                    City = "Chicago",
                    StateOrProvince = "Illinois",
                    PostalCode = "60601",
                    CountryCode = "USA"
                });
                seedDb.HandlingUnits.Add(new HandlingUnit
                {
                    Id = huLifecycleId,
                    ClientId = clientUsId,
                    FacilityId = facUsId,
                    LpnCode = "F02-HU-LC",
                    UnitType = HandlingUnitType.ShippingCarton,
                    Status = HandlingUnitStatus.Available
                });
                await seedDb.SaveChangesAsync();
            }

            // 1. Concurrent BackfillAllAsync attempts across 2 independent DbContexts (service-owned transactions)
            var t1 = Task.Run(async () =>
            {
                await using var db1 = CreatePgDb(connStr);
                return await new LegacyCountryNormalizationService(db1).BackfillAllAsync("F02-CONC-1");
            });
            var t2 = Task.Run(async () =>
            {
                await using var db2 = CreatePgDb(connStr);
                return await new LegacyCountryNormalizationService(db2).BackfillAllAsync("F02-CONC-2");
            });
            await Task.WhenAll(t1, t2);

            await using (var chkConc = CreatePgDb(connStr))
            {
                var audits = await chkConc.CountryNormalizationAudits.ToListAsync();
                if (audits.Count != audits.Select(a => (a.EntityType, a.EntityId, a.FieldName)).Distinct().Count())
                    throw new InvalidOperationException("F02 concurrent backfill produced duplicate audit rows.");
            }

            // 2. Concurrent order/package lifecycle change: order becomes Completed + Packed BEFORE backfill acquires lock -> NO historical mutation!
            var freshOrderId = Guid.NewGuid();
            var freshAddrId = Guid.NewGuid();
            var freshHuId = Guid.NewGuid();
            await using (var prepDb = CreatePgDb(connStr))
            {
                prepDb.OutboundOrders.Add(new OutboundOrder
                {
                    Id = freshOrderId,
                    ClientId = clientUsId,
                    FacilityId = facUsId,
                    OrderNumber = "F02-ORD-IMM",
                    OrderType = OutboundOrderType.B2BSalesOrder,
                    Status = OutboundOrderStatus.Draft,
                    CustomerName = "Imm Race"
                });
                prepDb.ShippingAddresses.Add(new ShippingAddress
                {
                    Id = freshAddrId,
                    OutboundOrderId = freshOrderId,
                    RecipientName = "Imm Recip",
                    AddressLine1 = "2 Race Rd",
                    City = "Chicago",
                    StateOrProvince = "Illinois",
                    PostalCode = "60601",
                    CountryCode = "USA"
                });
                prepDb.HandlingUnits.Add(new HandlingUnit
                {
                    Id = freshHuId,
                    ClientId = clientUsId,
                    FacilityId = facUsId,
                    LpnCode = "F02-HU-IMM",
                    UnitType = HandlingUnitType.ShippingCarton,
                    Status = HandlingUnitStatus.Available
                });
                await prepDb.SaveChangesAsync();
            }

            var lockHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var lifecycleWriter = Task.Run(async () =>
            {
                await using var wDb = CreatePgDb(connStr);
                await using var tx = await wDb.Database.BeginTransactionAsync();
                var ord = await wDb.OutboundOrders.IgnoreQueryFilters().SingleAsync(o => o.Id == freshOrderId);
                ord.Status = OutboundOrderStatus.Completed;
                wDb.Packages.Add(new Package
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientUsId,
                    FacilityId = facUsId,
                    OutboundOrderId = freshOrderId,
                    HandlingUnitId = freshHuId,
                    PackageNumber = "F02-PKG-IMM",
                    Status = PackageStatus.Packed
                });
                await wDb.SaveChangesAsync();
                lockHeld.SetResult();
                await releaseCommit.Task;
                await tx.CommitAsync();
            });

            await lockHeld.Task;
            var backfillWhileLifecycle = Task.Run(async () =>
            {
                await using var bDb = CreatePgDb(connStr);
                return await new LegacyCountryNormalizationService(bDb).BackfillAllAsync("F02-LC-BACKFILL");
            });

            // Backfill must block on SHARE ROW EXCLUSIVE until writer commits
            await Task.Delay(200);
            if (backfillWhileLifecycle.IsCompleted)
                throw new InvalidOperationException("F02 backfill did not block on concurrent order/package lifecycle transaction.");

            releaseCommit.SetResult();
            await Task.WhenAll(lifecycleWriter, backfillWhileLifecycle);

            await using (var chkImm = CreatePgDb(connStr))
            {
                var immAddr = await chkImm.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == freshAddrId);
                var immAudit = await chkImm.CountryNormalizationAudits.SingleAsync(a => a.EntityType == "ShippingAddress" && a.EntityId == freshAddrId);
                if (immAddr.CountryCode != "USA" || !immAudit.PreservedHistoricalValue)
                    throw new InvalidOperationException($"F02 historical address was mutated despite concurrent immutability transition: Country={immAddr.CountryCode}, Preserved={immAudit.PreservedHistoricalValue}");
            }

            // 3. Concurrent parent-country change: Facility switches to NP inside a tx while BackfillAllAsync waits -> Child Customer infers NP
            var parentLockHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseParent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var parentWriter = Task.Run(async () =>
            {
                await using var pDb = CreatePgDb(connStr);
                await using var tx = await pDb.Database.BeginTransactionAsync();
                var fac = await pDb.Facilities.SingleAsync(f => f.Id == facParentId);
                fac.CountryCode = "NP";
                fac.StateOrProvince = "Bagmati";
                fac.PostalCode = "44600";
                fac.Timezone = "Asia/Kathmandu";
                await pDb.SaveChangesAsync();
                parentLockHeld.SetResult();
                await releaseParent.Task;
                await tx.CommitAsync();
            });

            await parentLockHeld.Task;
            var backfillWhileParent = Task.Run(async () =>
            {
                await using var bDb = CreatePgDb(connStr);
                return await new LegacyCountryNormalizationService(bDb).BackfillAllAsync("F02-PAR-BACKFILL");
            });

            await Task.Delay(200);
            if (backfillWhileParent.IsCompleted)
                throw new InvalidOperationException("F02 backfill did not block on concurrent parent Facility update.");

            releaseParent.SetResult();
            await Task.WhenAll(parentWriter, backfillWhileParent);

            await using (var chkPar = CreatePgDb(connStr))
            {
                var custChild = await chkPar.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custParentChildId);
                if (custChild.DefaultCountryCode != "NP" || custChild.CountryNormalizationStatus != CountryNormalizationStatus.Normalized)
                    throw new InvalidOperationException($"F02 concurrent parent-country update not observed consistently: Country={custChild.DefaultCountryCode}, Status={custChild.CountryNormalizationStatus}");
            }

            // 4. Caller-owned transaction rollback + service-owned retry
            var rbFacId = Guid.NewGuid();
            await using (var rbDb = CreatePgDb(connStr))
            {
                await using var tx = await rbDb.Database.BeginTransactionAsync();
                rbDb.Facilities.Add(new Facility { Id = rbFacId, FacilityCode = "F02-RB", Name = "RB Fac", CountryCode = "USA", StateOrProvince = "Illinois", PostalCode = "60601", Timezone = "America/Chicago", IsActive = true });
                await rbDb.SaveChangesAsync();
                await new LegacyCountryNormalizationService(rbDb).BackfillAllAsync("F02-ROLLBACK");
                await tx.RollbackAsync();
            }

            await using (var retryDb = CreatePgDb(connStr))
            {
                if (await retryDb.Facilities.AnyAsync(f => f.Id == rbFacId))
                    throw new InvalidOperationException("F02 caller-owned transaction rollback failed.");
                retryDb.Facilities.Add(new Facility { Id = rbFacId, FacilityCode = "F02-RB", Name = "RB Fac", CountryCode = "USA", StateOrProvince = "Illinois", PostalCode = "60601", Timezone = "America/Chicago", IsActive = true });
                await retryDb.SaveChangesAsync();
                await new LegacyCountryNormalizationService(retryDb).BackfillAllAsync("F02-RETRY");
                var fOk = await retryDb.Facilities.SingleAsync(f => f.Id == rbFacId);
                if (fOk.CountryCode != "US" || fOk.CountryNormalizationStatus != CountryNormalizationStatus.Normalized)
                    throw new InvalidOperationException("F02 retry after rollback failed.");
            }

            Console.WriteLine("PASS PostgreSQL MDM-1.6 F02 Concurrent Backfill, Order/Package Lifecycle Race, Parent-Country Race & Rollback/Retry verified cleanly.");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            try
            {
                await using var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString);
                await adminConn.OpenAsync();
                await using var term = new NpgsqlCommand($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}' AND pid <> pg_backend_pid();", adminConn);
                await term.ExecuteNonQueryAsync();
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\";", adminConn);
                await drop.ExecuteNonQueryAsync();
            }
            catch { }
        }
    }

    private static async Task VerifyF03SafeHistoricalRestorationAndSqlCSharpParityAsync(string baseConnectionString)
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var f03DbName = $"mdm16_f03_rest_{Guid.NewGuid():N}";
        var paritySqlDbName = $"mdm16_par_sql_{Guid.NewGuid():N}";
        var parityCsDbName = $"mdm16_par_cs_{Guid.NewGuid():N}";

        async Task CreateDbAsync(string name)
        {
            await using var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString);
            await adminConn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", adminConn);
            await cmd.ExecuteNonQueryAsync();
        }

        async Task DropDbAsync(string name)
        {
            NpgsqlConnection.ClearAllPools();
            try
            {
                await using var adminConn = new NpgsqlConnection(adminBuilder.ConnectionString);
                await adminConn.OpenAsync();
                await using var term = new NpgsqlCommand($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{name}' AND pid <> pg_backend_pid();", adminConn);
                await term.ExecuteNonQueryAsync();
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\";", adminConn);
                await drop.ExecuteNonQueryAsync();
            }
            catch { }
        }

        try
        {
            // Part A: F03 Historical Restoration Fixtures (1..8) + F04 Allowlist Privacy on PG
            await CreateDbAsync(f03DbName);
            var f03Conn = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = f03DbName }.ConnectionString;

            var facId = Guid.NewGuid();
            var clientId = Guid.NewGuid();
            var ordPrePackedId = Guid.NewGuid();
            var addrPrePackedId = Guid.NewGuid();
            var ordPostFulfillId = Guid.NewGuid();
            var addrPostFulfillId = Guid.NewGuid();
            var ordAmendedId = Guid.NewGuid();
            var addrAmendedId = Guid.NewGuid();
            var ordHistBlankId = Guid.NewGuid();
            var addrHistBlankId = Guid.NewGuid();
            var ordAmbiguousId = Guid.NewGuid();
            var addrAmbiguousId = Guid.NewGuid();
            var custNamePiiId = Guid.NewGuid();

            await using (var db = CreatePgDb(f03Conn))
            {
                var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(Mdm15Migration);
                await ReferenceCatalogSeeder.SeedAsync(db);
            }

            // Seed pre-MDM-1.6 fixtures directly against the MDM-1.5 schema via Npgsql
            await using (var conn = new NpgsqlConnection(f03Conn))
            {
                await conn.OpenAsync();
                async Task ExecF03Async(string sql)
                {
                    await using var cmd = new NpgsqlCommand(sql, conn);
                    await cmd.ExecuteNonQueryAsync();
                }

                await ExecF03Async($"""
                    INSERT INTO facilities (facility_id, facility_code, name, country_code, state_or_province, postal_code, timezone, is_active, is_deleted, created_at)
                    VALUES ('{facId}', 'F03-US', 'F03 US', 'USA', 'Illinois', '60601', 'America/Chicago', true, false, NOW());

                    INSERT INTO clients (client_id, account_code, company_name, status, sla_tier, currency, is_deleted, created_at)
                    VALUES ('{clientId}', 'F03-CL', 'F03 Client', 'Active', 'Standard', 'USD', false, NOW());

                    INSERT INTO client_facility_access (client_id, facility_id, effective_date, status, primary_operating_facility, created_at)
                    VALUES ('{clientId}', '{facId}', NOW(), 'Active', true, NOW());

                    INSERT INTO customers (customer_id, client_id, customer_code, customer_name, customer_type, email, default_state_or_province, default_postal_code, default_country_code, is_active, version, is_deleted, created_at)
                    VALUES ('{custNamePiiId}', '{clientId}', 'F03-PII', 'John', 'RetailCustomer', 'f03@m16.test', 'Illinois', '60601', 'John Doe', true, 1, false, NOW());

                    INSERT INTO outbound_orders (order_id, client_id, facility_id, customer_id, order_number, order_type, status, delivery_status, priority, service_level, customer_name, order_date_utc, version, is_deleted, created_at)
                    VALUES
                        ('{ordPrePackedId}', '{clientId}', '{facId}', NULL, 'F03-PRE', 'B2BSalesOrder', 'Draft', 'PendingShipment', 1, 'StandardGround', 'Pre', NOW(), 1, false, NOW()),
                        ('{ordPostFulfillId}', '{clientId}', '{facId}', NULL, 'F03-POST', 'B2BSalesOrder', 'Draft', 'PendingShipment', 1, 'StandardGround', 'Post', NOW(), 1, false, NOW()),
                        ('{ordAmendedId}', '{clientId}', '{facId}', NULL, 'F03-AMD', 'B2BSalesOrder', 'Draft', 'PendingShipment', 1, 'StandardGround', 'Amd', NOW(), 1, false, NOW()),
                        ('{ordHistBlankId}', '{clientId}', '{facId}', NULL, 'F03-BLK', 'B2BSalesOrder', 'Draft', 'PendingShipment', 1, 'StandardGround', 'Blk', NOW(), 1, false, NOW()),
                        ('{ordAmbiguousId}', '{clientId}', '{facId}', NULL, 'F03-AMB', 'B2BSalesOrder', 'Draft', 'PendingShipment', 1, 'StandardGround', 'Amb', NOW(), 1, false, NOW());

                    INSERT INTO shipping_addresses (shipping_address_id, order_id, recipient_name, address_line1, city, state_or_province, postal_code, country_code, is_amended, created_at_utc, is_deleted)
                    VALUES
                        ('{addrPrePackedId}', '{ordPrePackedId}', 'R1', '1 St', 'Chicago', 'Illinois', '60601', 'USA', false, NOW(), false),
                        ('{addrPostFulfillId}', '{ordPostFulfillId}', 'R2', '2 St', 'Chicago', 'Illinois', '60601', 'USA', false, NOW(), false),
                        ('{addrAmendedId}', '{ordAmendedId}', 'R3', '3 St', 'Chicago', 'Illinois', '60601', 'USA', false, NOW(), false),
                        ('{addrHistBlankId}', '{ordHistBlankId}', 'R4', '4 St', 'Chicago', 'Illinois', '60601', '', false, NOW(), false),
                        ('{addrAmbiguousId}', '{ordAmbiguousId}', 'R5', '5 St', 'Chicago', 'Illinois', '60601', 'US', false, NOW(), false);
                    """);
            }

            await using (var pkgDb = CreatePgDb(f03Conn))
            {
                var hu1 = Guid.NewGuid();
                pkgDb.HandlingUnits.Add(new HandlingUnit { Id = hu1, ClientId = clientId, FacilityId = facId, LpnCode = "F03-HU1", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.Available });
                pkgDb.Packages.Add(new Package { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facId, OutboundOrderId = ordPrePackedId, HandlingUnitId = hu1, PackageNumber = "F03-PKG1", Status = PackageStatus.Packed });
                await pkgDb.SaveChangesAsync();
            }

            // Apply original 20261126143014 migration
            await using (var db = CreatePgDb(f03Conn))
            {
                var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(Mdm16Migration);
            }

            // Post-MDM-1.6 legitimate events BEFORE R04 + F03 run (executed against Mdm16 schema):
            // Fixture 2: Legitimate amendment on ordAmendedId -> NP
            // Fixture 3: Subsequent fulfillment AFTER MDM-1.6 on ordPostFulfillId (package created strictly after initial_recorded_at_utc)
            // Fixture 4: ordHistBlankId became Completed after MDM-1.6 blank inference
            // Fixture 5: Ambiguous manual audit on ordAmbiguousId
            await Task.Delay(100);
            await using (var pkgDb2 = CreatePgDb(f03Conn))
            {
                var hu2 = Guid.NewGuid();
                pkgDb2.HandlingUnits.Add(new HandlingUnit { Id = hu2, ClientId = clientId, FacilityId = facId, LpnCode = "F03-HU2", UnitType = HandlingUnitType.ShippingCarton, Status = HandlingUnitStatus.Available });
                pkgDb2.Packages.Add(new Package { Id = Guid.NewGuid(), ClientId = clientId, FacilityId = facId, OutboundOrderId = ordPostFulfillId, HandlingUnitId = hu2, PackageNumber = "F03-PKG2", Status = PackageStatus.Packed });
                await pkgDb2.SaveChangesAsync();
            }
            await using (var conn = new NpgsqlConnection(f03Conn))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand($"""
                    UPDATE shipping_addresses
                    SET city = 'Kathmandu', state_or_province = 'Bagmati', postal_code = '44600', country_code = 'NP',
                        is_amended = true, amended_at_utc = NOW(), amendment_reason = 'Legit customer move'
                    WHERE shipping_address_id = '{addrAmendedId}';

                    UPDATE outbound_orders
                    SET status = 'Completed', updated_at = NOW()
                    WHERE order_id = '{ordHistBlankId}';

                    INSERT INTO country_normalization_audits (audit_id, entity_type, entity_id, field_name, original_country_value, normalized_country_code, normalization_status, was_inferred_from_parent, preserved_historical_value, reason, facility_id, client_id, correlation_batch_id, recorded_at_utc)
                    VALUES ('{Guid.NewGuid()}', 'ShippingAddress', '{addrAmbiguousId}', 'CountryCode', 'USA', 'US', 'Normalized', false, false, 'Normalized to canonical ''US'' via CatalogAlias.', '{facId}', '{clientId}', 'MANUAL-BATCH', NOW() - INTERVAL '5 minutes')
                    ON CONFLICT (entity_type, entity_id, field_name) DO UPDATE
                    SET original_country_value = 'USA', normalized_country_code = 'US', correlation_batch_id = 'MANUAL-BATCH', recorded_at_utc = NOW() - INTERVAL '5 minutes';
                    """, conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // Apply R04 + F03 migrations sequentially and verify all F03/F04 assertions
            await using (var db = CreatePgDb(f03Conn))
            {
                var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync();

                var rPre = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == addrPrePackedId);
                var rPost = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == addrPostFulfillId);
                var rAmd = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == addrAmendedId);
                var rAmb = await db.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == addrAmbiguousId);
                var aBlk = await db.CountryNormalizationAudits.SingleAsync(a => a.EntityType == "ShippingAddress" && a.EntityId == addrHistBlankId);
                var aPiiName = await db.CountryNormalizationAudits.SingleAsync(a => a.EntityType == "Customer" && a.EntityId == custNamePiiId);

                if (rPre.CountryCode != "USA")
                    throw new InvalidOperationException($"F03 Fixture 1 failed: pre-MDM-1.6 packed address expected 'USA', got '{rPre.CountryCode}'.");
                if (rPost.CountryCode != "US")
                    throw new InvalidOperationException($"F03 Fixture 3 failed: post-MDM-1.6 fulfilled address expected 'US', got '{rPost.CountryCode}'.");
                if (rAmd.CountryCode != "NP")
                    throw new InvalidOperationException($"F03 Fixture 2 failed: legitimately amended address expected 'NP', got '{rAmd.CountryCode}'.");
                if (rAmb.CountryCode != "US")
                    throw new InvalidOperationException($"F03 Fixture 5 failed: ambiguous provenance address expected 'US', got '{rAmb.CountryCode}'.");
                if (!aBlk.Reason.Contains("Historical blank country inference preserved", StringComparison.Ordinal))
                    throw new InvalidOperationException($"F03 Fixture 4 failed: expected safe blank-inference diagnostic, got '{aBlk.Reason}'.");
                if (aPiiName.OriginalCountryValue != CountryDiagnosticSanitizer.RedactedPlaceholder)
                    throw new InvalidOperationException($"F04 PG allowlist failed: personal name 'John Doe' was not redacted, got '{aPiiName.OriginalCountryValue}'.");
            }

            // Part B: Independent SQL vs C# Parity on identical original legacy fixtures
            await CreateDbAsync(paritySqlDbName);
            await CreateDbAsync(parityCsDbName);
            var sqlConn = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = paritySqlDbName }.ConnectionString;
            var csConn = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = parityCsDbName }.ConnectionString;

            var pFacUs = Guid.NewGuid();
            var pFacConflict = Guid.NewGuid();
            var pClientUs = Guid.NewGuid();
            var pCustInfer = Guid.NewGuid();
            var pCustNamePii = Guid.NewGuid();
            var pSupInfer = Guid.NewGuid();

            // DB A: Pure SQL migration path (MDM1_5 -> seed via raw SQL -> latest)
            await using (var sDb = CreatePgDb(sqlConn))
            {
                var mig = sDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await mig.MigrateAsync(Mdm15Migration);
                await ReferenceCatalogSeeder.SeedAsync(sDb);
            }
            await using (var conn = new NpgsqlConnection(sqlConn))
            {
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand($"""
                    INSERT INTO facilities (facility_id, facility_code, name, country_code, state_or_province, postal_code, timezone, is_active, is_deleted, created_at)
                    VALUES
                        ('{pFacUs}', 'PAR-US', 'Par US', 'USA', 'Illinois', '60601', 'America/Chicago', true, false, NOW()),
                        ('{pFacConflict}', 'PAR-CNF', 'Par Conflict', 'US', 'Bagmati', '44600', 'Asia/Kathmandu', true, false, NOW());

                    INSERT INTO clients (client_id, account_code, company_name, status, sla_tier, currency, is_deleted, created_at)
                    VALUES ('{pClientUs}', 'PAR-CL', 'Par Client', 'Active', 'Standard', 'USD', false, NOW());

                    INSERT INTO client_facility_access (client_id, facility_id, effective_date, status, primary_operating_facility, created_at)
                    VALUES ('{pClientUs}', '{pFacUs}', NOW(), 'Active', true, NOW());

                    INSERT INTO customers (customer_id, client_id, customer_code, customer_name, customer_type, email, default_state_or_province, default_postal_code, default_country_code, is_active, version, is_deleted, created_at)
                    VALUES
                        ('{pCustInfer}', '{pClientUs}', 'PAR-C1', 'C1', 'RetailCustomer', 'c1@par.test', 'Illinois', '60601', '', true, 1, false, NOW()),
                        ('{pCustNamePii}', '{pClientUs}', 'PAR-C2', 'C2', 'RetailCustomer', 'c2@par.test', 'Illinois', '60601', 'Jane Roe', true, 1, false, NOW());

                    INSERT INTO suppliers (supplier_id, client_id, supplier_code, supplier_name, state, postal_code, country, is_active, is_deleted, created_at)
                    VALUES ('{pSupInfer}', '{pClientUs}', 'PAR-S1', 'S1', 'Illinois', '60601', NULL, true, false, NOW());
                    """, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var sDb = CreatePgDb(sqlConn))
            {
                var mig = sDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await mig.MigrateAsync();
            }

            // DB B: Pure C# BackfillAllAsync path (latest empty -> seed -> BackfillAllAsync)
            await using (var cDb = CreatePgDb(csConn))
            {
                var mig = cDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await mig.MigrateAsync();
                await ReferenceCatalogSeeder.SeedAsync(cDb);
                cDb.Facilities.AddRange(
                    new Facility { Id = pFacUs, FacilityCode = "PAR-US", Name = "Par US", CountryCode = "USA", StateOrProvince = "Illinois", PostalCode = "60601", Timezone = "America/Chicago", IsActive = true },
                    new Facility { Id = pFacConflict, FacilityCode = "PAR-CNF", Name = "Par Conflict", CountryCode = "US", StateOrProvince = "Bagmati", PostalCode = "44600", Timezone = "Asia/Kathmandu", IsActive = true });
                cDb.Clients.Add(new Client { Id = pClientUs, AccountCode = "PAR-CL", CompanyName = "Par Client", Status = ClientStatus.Active });
                cDb.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = pClientUs, FacilityId = pFacUs, Status = FacilityAccessStatus.Active });
                cDb.Customers.AddRange(
                    new Customer { Id = pCustInfer, ClientId = pClientUs, CustomerCode = "PAR-C1", CustomerName = "C1", DefaultCountryCode = "", DefaultStateOrProvince = "Illinois", DefaultPostalCode = "60601" },
                    new Customer { Id = pCustNamePii, ClientId = pClientUs, CustomerCode = "PAR-C2", CustomerName = "C2", DefaultCountryCode = "Jane Roe", DefaultStateOrProvince = "Illinois", DefaultPostalCode = "60601" });
                cDb.Suppliers.Add(new Supplier { Id = pSupInfer, ClientId = pClientUs, SupplierCode = "PAR-S1", SupplierName = "S1", Country = null, State = "Illinois", PostalCode = "60601" });
                await cDb.SaveChangesAsync();
                await new LegacyCountryNormalizationService(cDb).BackfillAllAsync("PARITY-CS");
            }

            // Compare outcomes across SQL DB vs C# DB
            await using (var sDb = CreatePgDb(sqlConn))
            await using (var cDb = CreatePgDb(csConn))
            {
                var sFacs = await sDb.Facilities.IgnoreQueryFilters().OrderBy(f => f.Id).Select(f => new { f.Id, f.CountryCode, f.CountryNormalizationStatus }).ToListAsync();
                var cFacs = await cDb.Facilities.IgnoreQueryFilters().OrderBy(f => f.Id).Select(f => new { f.Id, f.CountryCode, f.CountryNormalizationStatus }).ToListAsync();
                var sCusts = await sDb.Customers.IgnoreQueryFilters().OrderBy(c => c.Id).Select(c => new { c.Id, c.DefaultCountryCode, c.CountryNormalizationStatus }).ToListAsync();
                var cCusts = await cDb.Customers.IgnoreQueryFilters().OrderBy(c => c.Id).Select(c => new { c.Id, c.DefaultCountryCode, c.CountryNormalizationStatus }).ToListAsync();
                var sSups = await sDb.Suppliers.IgnoreQueryFilters().OrderBy(s => s.Id).Select(s => new { s.Id, s.Country, s.CountryNormalizationStatus }).ToListAsync();
                var cSups = await cDb.Suppliers.IgnoreQueryFilters().OrderBy(s => s.Id).Select(s => new { s.Id, s.Country, s.CountryNormalizationStatus }).ToListAsync();
                var sAuds = await sDb.CountryNormalizationAudits.OrderBy(a => a.EntityType).ThenBy(a => a.EntityId).Select(a => new { a.EntityType, a.EntityId, a.FieldName, a.OriginalCountryValue, a.NormalizedCountryCode, a.NormalizationStatus }).ToListAsync();
                var cAuds = await cDb.CountryNormalizationAudits.OrderBy(a => a.EntityType).ThenBy(a => a.EntityId).Select(a => new { a.EntityType, a.EntityId, a.FieldName, a.OriginalCountryValue, a.NormalizedCountryCode, a.NormalizationStatus }).ToListAsync();

                if (!sFacs.SequenceEqual(cFacs) || !sCusts.SequenceEqual(cCusts) || !sSups.SequenceEqual(cSups) || !sAuds.SequenceEqual(cAuds))
                {
                    throw new InvalidOperationException("SQL vs C# normalization parity mismatch detected across identical legacy fixtures.");
                }
            }

            Console.WriteLine("PASS PostgreSQL MDM-1.6 F03 Safe Historical Restoration Fixtures, F04 Allowlist Privacy & Independent SQL/C# Parity verified cleanly.");
        }
        finally
        {
            await DropDbAsync(f03DbName);
            await DropDbAsync(paritySqlDbName);
            await DropDbAsync(parityCsDbName);
        }
    }

    private sealed class PgAdminTenantProvider : ITenantProvider
    {
        public Guid? TenantId => null;
        public bool HasTenant => false;
        public bool BypassTenantFilter { get; set; } = true;
        public void SetTenantId(Guid tenantId) { }
    }

    private sealed class PgAdminUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "pgadmin@m16.test";
        public Guid? TenantId => null;
        public Guid? FacilityId => null;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles => [SystemRoles.SuperAdmin];
        public IReadOnlyList<string> Permissions => Warehouse3PL.Domain.Constants.Permissions.GetAllPermissions();
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => true;
        public bool HasPermission(string permission) => true;
        public bool IsInRole(string role) => string.Equals(role, SystemRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);
    }
}
