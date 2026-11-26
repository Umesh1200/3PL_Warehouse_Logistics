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
        Console.WriteLine("\n[MDM-1.6] Running Live PostgreSQL Legacy Country Normalization, Precheck, Quarantine, Historical Immutability, Epoch Outbox, RBAC & Down/Up Tests...");

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

            var clientNpId = Guid.NewGuid();
            var clientUsId = Guid.NewGuid();

            var custInferNpId = Guid.NewGuid();
            var custUnknownId = Guid.NewGuid();
            var supInferUsId = Guid.NewGuid();

            var draftOrderUsId = Guid.NewGuid();
            var completedOrderNpId = Guid.NewGuid();
            var xbrdOrderId = Guid.NewGuid();

            var draftAddrId = Guid.NewGuid();
            var completedAddrId = Guid.NewGuid();
            var xbrdAddrId = Guid.NewGuid();

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

                // Clients + single-facility access bindings
                const string insertClientSql = """
                    INSERT INTO clients (client_id, account_code, company_name, status, sla_tier, currency, is_deleted, created_at)
                    VALUES (@id, @code, @name, 'Active', 'Standard', 'USD', false, NOW());
                    """;
                await ExecAsync(insertClientSql, ("@id", clientNpId), ("@code", "PG16-CLNP"), ("@name", "PG16 Nepal Client"));
                await ExecAsync(insertClientSql, ("@id", clientUsId), ("@code", "PG16-CLUS"), ("@name", "PG16 US Client"));

                const string insertCfaSql = """
                    INSERT INTO client_facility_access (access_id, client_id, facility_id, effective_date, status, primary_operating_facility, created_at)
                    VALUES (gen_random_uuid(), @cid, @fid, NOW(), 'Active', true, NOW());
                    """;
                await ExecAsync(insertCfaSql, ("@cid", clientNpId), ("@fid", facNpId));
                await ExecAsync(insertCfaSql, ("@cid", clientUsId), ("@fid", facUsId));

                // Customers
                const string insertCustSql = """
                    INSERT INTO customers (customer_id, client_id, customer_code, customer_name, customer_type, email, default_state_or_province, default_postal_code, default_country_code, is_active, version, is_deleted, created_at)
                    VALUES (@id, @cid, @code, @name, 'RetailCustomer', @email, @st, @zip, @cc, true, 1, false, NOW());
                    """;
                await ExecAsync(insertCustSql, ("@id", custInferNpId), ("@cid", clientNpId), ("@code", "C16-NP"), ("@name", "PII Hidden Name"), ("@email", "pii-cust@pg16.test"), ("@st", "Bagmati"), ("@zip", "44600"), ("@cc", ""));
                await ExecAsync(insertCustSql, ("@id", custUnknownId), ("@cid", clientUsId), ("@code", "C16-ZZ"), ("@name", "Unknown Cust"), ("@email", "unk@pg16.test"), ("@st", "Illinois"), ("@zip", "60601"), ("@cc", "ZZZ"));

                // Supplier (blank -> infer US)
                await ExecAsync("""
                    INSERT INTO suppliers (supplier_id, client_id, supplier_code, supplier_name, state, postal_code, country, is_active, is_deleted, created_at)
                    VALUES (@id, @cid, 'S16-US', 'PG16 Supplier', 'Illinois', '60601', NULL, true, false, NOW());
                    """, ("@id", supInferUsId), ("@cid", clientUsId));

                // Outbound orders + ShippingAddresses (Draft vs Historical Completed vs Cross-border blank)
                const string insertOrderSql = """
                    INSERT INTO outbound_orders (order_id, client_id, facility_id, customer_id, order_number, order_type, status, delivery_status, priority, service_level, customer_name, order_date_utc, version, is_deleted, created_at)
                    VALUES (@id, @cid, @fid, @custid, @num, 'B2BSalesOrder', @st, 'PendingShipment', 1, 'StandardGround', 'PII Order Cust', NOW(), 1, false, NOW());
                    """;
                await ExecAsync(insertOrderSql, ("@id", draftOrderUsId), ("@cid", clientUsId), ("@fid", facUsId), ("@custid", custUnknownId), ("@num", "PG16-ORD-DRAFT"), ("@st", "Draft"));
                await ExecAsync(insertOrderSql, ("@id", completedOrderNpId), ("@cid", clientNpId), ("@fid", facNpId), ("@custid", custInferNpId), ("@num", "PG16-ORD-COMP"), ("@st", "Completed"));
                await ExecAsync(insertOrderSql, ("@id", xbrdOrderId), ("@cid", clientNpId), ("@fid", facUsId), ("@custid", custInferNpId), ("@num", "PG16-ORD-XBRD"), ("@st", "Draft"));

                const string insertAddrSql = """
                    INSERT INTO shipping_addresses (shipping_address_id, order_id, recipient_name, address_line1, city, state_or_province, postal_code, country_code, is_amended, created_at_utc, is_deleted)
                    VALUES (@id, @oid, 'PII Recipient Secret', '999 Secret Way', 'City', @st, @zip, @cc, false, NOW(), false);
                    """;
                await ExecAsync(insertAddrSql, ("@id", draftAddrId), ("@oid", draftOrderUsId), ("@st", "Illinois"), ("@zip", "60601"), ("@cc", "USA"));
                await ExecAsync(insertAddrSql, ("@id", completedAddrId), ("@oid", completedOrderNpId), ("@st", "Bagmati"), ("@zip", "44600"), ("@cc", "NPL"));
                await ExecAsync(insertAddrSql, ("@id", xbrdAddrId), ("@oid", xbrdOrderId), ("@st", "Illinois"), ("@zip", "60601"), ("@cc", ""));

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

            // 4. Clean upgrade MDM1_5 -> MDM1_6 against realistic PG rows
            await using (var upDb = CreatePgDb(targetConn))
            {
                var migrator = upDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync(Mdm16Migration);
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

                // Verify inferred + quarantined Customers & Suppliers
                var cInf = await upDb.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custInferNpId);
                var cUnk = await upDb.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == custUnknownId);
                var sInf = await upDb.Suppliers.IgnoreQueryFilters().SingleAsync(s => s.Id == supInferUsId);
                if (cInf.DefaultCountryCode != "NP" || cUnk.DefaultCountryCode != "ZZZ" ||
                    cUnk.CountryNormalizationStatus != CountryNormalizationStatus.PendingNormalization || sInf.Country != "US")
                    throw new InvalidOperationException("PG Customer/Supplier deterministic backfill mismatch.");

                // Verify historical immutability vs mutable draft vs cross-border quarantine
                var aDraft = await upDb.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == draftAddrId);
                var aComp = await upDb.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == completedAddrId);
                var aXbrd = await upDb.ShippingAddresses.IgnoreQueryFilters().SingleAsync(a => a.Id == xbrdAddrId);
                if (aDraft.CountryCode != "US" || aComp.CountryCode != "NPL" ||
                    aComp.CountryNormalizationStatus != CountryNormalizationStatus.Normalized ||
                    aXbrd.CountryNormalizationStatus != CountryNormalizationStatus.PendingNormalization)
                    throw new InvalidOperationException("PG ShippingAddress immutability/cross-border check failed.");

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
                if (auditFacUs.OriginalCountryValue != "USA" || auditFacUs.NormalizedCountryCode != "US")
                    throw new InvalidOperationException("PG audit provenance OriginalCountryValue was overwritten on rerun.");

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
                    (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/reference/normalization-quarantine?facilityId={facUsId}", jwtFacNp))).StatusCode != HttpStatusCode.Forbidden)
                {
                    throw new InvalidOperationException("PG MDM-1.6 RBAC or facility-isolation guard failed.");
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

            Console.WriteLine("[MDM-1.6] ALL Live PostgreSQL Country Normalization, Quarantine, Historical Immutability, Epoch & RBAC checks passed cleanly.");
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
