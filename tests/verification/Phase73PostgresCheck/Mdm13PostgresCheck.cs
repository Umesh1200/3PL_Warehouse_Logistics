using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Application.Features.Reference.Commands;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;

internal static class Mdm13PostgresCheck
{
    private const string JwtSecret = "Nexus3PL-Mdm13-PostgreSql-Verification-Key-2026!";
    private const string JwtIssuer = "nexus3pl-mdm13-pg-auth";
    private const string JwtAudience = "nexus3pl-mdm13-pg-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static async Task RunAsync(string baseConnectionString)
    {
        Console.WriteLine("\n[MDM-1.3] Running Live PostgreSQL CurrencyMaster, FxRateRecord Exclusion/Race & UOM Verification...");

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm13_pg_{Guid.NewGuid():N}";

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
            // 1. Clean upgrade MDM1_2 -> pre-R03 MDM1_3 with seeded legacy VerifiedApproved rows -> upgrade to latest ->
            //    verify historical preservation + safe quarantine of unverified legacy official FX + ManualApproved backfill
            var legacyApproverId = Guid.NewGuid();
            var legacyNrbId = Guid.NewGuid();
            var legacyManualId = Guid.NewGuid();
            await using (var migDb = CreatePgDb(targetConn, null))
            {
                var migrator = migDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync("20261125151152_MDM1_3_CurrencyFxAndUomStandards");
            }

            await using (var preSeedDb = CreatePgDb(targetConn, null))
            {
                await ReferenceCatalogSeeder.SeedAsync(preSeedDb);
            }

            await using (var legacyConn = new NpgsqlConnection(targetConn))
            {
                await legacyConn.OpenAsync();
                await using var insLegacy = new NpgsqlCommand(
                    $"INSERT INTO fx_rates (fx_rate_id, base_currency_code, quote_currency_code, effective_from_utc, unit_multiplier, buy_rate, sell_rate, mid_rate, rate_direction, source_authority, bulletin_version, benchmark_series_id, integrity_hash, published_at_utc, approval_status, approved_by_user_id, approved_at_utc, verification_notes, config_revision, is_active, created_at) VALUES " +
                    $"('{legacyNrbId}', 'USD', 'NPR', '2026-11-20T00:00:00Z', 1, 135.20, 135.80, 135.50, 'QuotePerUnitBase', 'NRB', 'NRB-PRE-R03-LEGACY', 'LEGACY-SERIES', NULL, '2026-11-19T10:00:00Z', 'VerifiedApproved', '{legacyApproverId}', '2026-11-19T10:05:00Z', 'Pre-R03 official entry lacking artifact verification.', 1, true, NOW()), " +
                    $"('{legacyManualId}', 'AUD', 'NPR', '2026-11-20T00:00:00Z', 1, 96.00, 97.00, 96.50, 'QuotePerUnitBase', 'ManualApproved', 'MAN-PRE-R03-LEGACY', NULL, NULL, '2026-11-19T10:00:00Z', 'VerifiedApproved', '{legacyApproverId}', '2026-11-19T10:05:00Z', 'Pre-R03 treasury desk manual override approved.', 1, true, NOW());",
                    legacyConn);
                await insLegacy.ExecuteNonQueryAsync();
            }

            await using (var migUpDb = CreatePgDb(targetConn, null))
            {
                var migrator = migUpDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync();
            }

            await using (var sqlConn = new NpgsqlConnection(targetConn))
            {
                await sqlConn.OpenAsync();
                await using var exclCmd = new NpgsqlCommand(
                    "SELECT pg_get_constraintdef(c.oid) FROM pg_constraint c JOIN pg_class t ON c.conrelid = t.oid WHERE t.relname = 'fx_rates' AND c.conname = 'excl_fx_rates_active_window';",
                    sqlConn);
                var def = (await exclCmd.ExecuteScalarAsync())?.ToString()
                    ?? throw new InvalidOperationException("MDM-1.3 PostgreSQL verification failed: excl_fx_rates_active_window missing.");
                if (!def.Contains("EXCLUDE USING gist", StringComparison.OrdinalIgnoreCase) ||
                    !def.Contains("VerifiedApproved", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Unexpected FX exclusion constraint definition: {def}");
                }

                // Verify F02 legacy quarantine preserved historical NRB record without deletion & freed active window
                await using var qChk = new NpgsqlCommand(
                    $"SELECT approval_status, is_active, evidence_reference, verification_notes FROM fx_rates WHERE fx_rate_id = '{legacyNrbId}';",
                    sqlConn);
                await using (var reader = await qChk.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                        throw new InvalidOperationException("MDM13-R03-F02 upgrade deleted legacy NRB row instead of preserving it!");
                    var qStatus = reader.GetString(0);
                    var qActive = reader.GetBoolean(1);
                    var qEvid = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var qNotes = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    if (qStatus != "Quarantined" || qActive || qEvid != "QUARANTINED_UNVERIFIED_LEGACY" || !qNotes.Contains("QUARANTINED_LEGACY_UNVERIFIED", StringComparison.Ordinal))
                        throw new InvalidOperationException($"MDM13-R03-F02 quarantine state mismatch: status={qStatus}, active={qActive}, evidence={qEvid}, notes={qNotes}");
                }

                // Verify legacy ManualApproved row retained explicit MANUAL_GOVERNANCE_OVERRIDE labeling
                await using var mChk = new NpgsqlCommand(
                    $"SELECT approval_status, evidence_reference FROM fx_rates WHERE fx_rate_id = '{legacyManualId}';",
                    sqlConn);
                await using (var reader = await mChk.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync() || reader.GetString(0) != "VerifiedApproved" || reader.GetString(1) != "MANUAL_GOVERNANCE_OVERRIDE")
                        throw new InvalidOperationException("MDM13-R03-F02 legacy ManualApproved upgrade preservation check failed.");
                }
            }

            await using (var seedDb = CreatePgDb(targetConn, null))
            {
                await ReferenceCatalogSeeder.SeedAsync(seedDb);
                await ReferenceCatalogSeeder.SeedAsync(seedDb);

                var curCount = await seedDb.Currencies.CountAsync();
                var uomCount = await seedDb.UomDefinitions.CountAsync();
                var convCount = await seedDb.UomConversions.CountAsync();
                if (curCount != 12 || uomCount != 15 || convCount != 15)
                {
                    throw new InvalidOperationException($"MDM-1.3 seed count mismatch: cur={curCount}, uom={uomCount}, conv={convCount}");
                }
            }

            // 2. Verify raw PostgreSQL FK to currencies (23503) & EXCLUDE USING gist (23P01)
            await using (var rawConn = new NpgsqlConnection(targetConn))
            {
                await rawConn.OpenAsync();
                bool fkRejected = false;
                try
                {
                    await using var badFk = new NpgsqlCommand(
                        $"INSERT INTO fx_rates (fx_rate_id, base_currency_code, quote_currency_code, effective_from_utc, unit_multiplier, buy_rate, sell_rate, mid_rate, rate_direction, source_authority, bulletin_version, published_at_utc, approval_status, config_revision, is_active, created_at) " +
                        $"VALUES ('{Guid.NewGuid()}', 'ZZZ', 'NPR', '2026-11-01T00:00:00Z', 1, 1.0, 1.0, 1.0, 'QuotePerUnitBase', 'ManualApproved', 'BAD-FK', NOW(), 'Draft', 1, false, NOW());",
                        rawConn);
                    await badFk.ExecuteNonQueryAsync();
                }
                catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
                {
                    fkRejected = true;
                }
                if (!fkRejected)
                    throw new InvalidOperationException("MDM-1.3 PostgreSQL FK to currencies did not block invalid currency.");

                await using (var insA = new NpgsqlCommand(
                    $"INSERT INTO fx_rates (fx_rate_id, base_currency_code, quote_currency_code, effective_from_utc, unit_multiplier, buy_rate, sell_rate, mid_rate, rate_direction, source_authority, bulletin_version, published_at_utc, approval_status, config_revision, is_active, created_at) " +
                    $"VALUES ('{Guid.NewGuid()}', 'EUR', 'NPR', '2026-01-01T00:00:00Z', 1, 142.0, 143.0, 142.5, 'QuotePerUnitBase', 'NRB', 'PG-EXCL-1', NOW(), 'VerifiedApproved', 1, true, NOW());",
                    rawConn))
                {
                    await insA.ExecuteNonQueryAsync();
                }

                bool exclRejected = false;
                try
                {
                    await using var insB = new NpgsqlCommand(
                        $"INSERT INTO fx_rates (fx_rate_id, base_currency_code, quote_currency_code, effective_from_utc, unit_multiplier, buy_rate, sell_rate, mid_rate, rate_direction, source_authority, bulletin_version, published_at_utc, approval_status, config_revision, is_active, created_at) " +
                        $"VALUES ('{Guid.NewGuid()}', 'EUR', 'NPR', '2026-06-01T00:00:00Z', 1, 144.0, 145.0, 144.5, 'QuotePerUnitBase', 'NRB', 'PG-EXCL-2', NOW(), 'VerifiedApproved', 1, true, NOW());",
                        rawConn);
                    await insB.ExecuteNonQueryAsync();
                }
                catch (PostgresException pgEx) when (pgEx.SqlState == "23P01")
                {
                    exclRejected = true;
                }
                if (!exclRejected)
                    throw new InvalidOperationException("MDM-1.3 PostgreSQL EXCLUDE USING gist did not block overlapping active FxRateRecord.");

                await using var cleanupExcl = new NpgsqlCommand("DELETE FROM fx_rates WHERE bulletin_version = 'PG-EXCL-1';", rawConn);
                await cleanupExcl.ExecuteNonQueryAsync();
            }

            // 3. Real JWT HTTP checks + NRB 100 INR -> NPR + 1 USD -> NPR + Concurrent Approval Race + Rollback + UOM
            var clientId = Guid.NewGuid();
            var facilityId = Guid.NewGuid();
            var clientUserId = Guid.NewGuid();
            var plainStaffId = Guid.NewGuid();
            var finStaffId = Guid.NewGuid();

            await using var host = await BuildPgHttpAppAsync(targetConn);
            using var http = host.GetTestClient();

            await using (var setupDb = CreatePgDb(targetConn, null))
            {
                setupDb.Clients.Add(new Client { Id = clientId, AccountCode = "PG-M13", CompanyName = "PG M13 Client", Status = ClientStatus.Active });
                setupDb.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "PG-M13-FAC", Name = "PG Birgunj", CountryCode = "NP", Timezone = "Asia/Kathmandu", IsActive = true });

                var opRole = new Role { RoleName = SystemRoles.WarehouseOperator, Description = "Op", IsSystemRole = true };
                var finRole = new Role { RoleName = "PgFinanceFxMgr", Description = "Fin", IsSystemRole = false };
                finRole.RolePermissions.Add(new RolePermission { Role = finRole, PermissionKey = Permissions.Global.ManageFxRates, AccessLevel = AccessLevel.Admin });
                finRole.RolePermissions.Add(new RolePermission { Role = finRole, PermissionKey = Permissions.Billing.ManageRates, AccessLevel = AccessLevel.Admin });
                setupDb.Roles.AddRange(opRole, finRole);

                var uClient = new User { Id = clientUserId, Email = "pgc@m13.test", FirstName = "C", LastName = "U", UserType = UserType.Client, ClientId = clientId, Status = UserStatus.Active, PasswordHash = "x" };
                var uPlain = new User { Id = plainStaffId, Email = "pgp@m13.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facilityId, Status = UserStatus.Active, PasswordHash = "x" };
                var uFin = new User { Id = finStaffId, Email = "pgf@m13.test", FirstName = "F", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facilityId, Status = UserStatus.Active, PasswordHash = "x" };
                setupDb.Users.AddRange(uClient, uPlain, uFin);
                setupDb.UserRoles.AddRange(
                    new UserRole { User = uPlain, Role = opRole },
                    new UserRole { User = uFin, Role = finRole });
                await setupDb.SaveChangesAsync();
            }

            string TokenFor(Guid uid, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
            {
                using var scope = host.Services.CreateScope();
                return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateTokens(new User
                {
                    Id = uid, Email = $"{uid:N}@m13.test", FirstName = "T", LastName = "U", UserType = ut, ClientId = cid, AssignedFacilityId = fid
                }, roles, perms).AccessToken;
            }

            var jwtClient = TokenFor(clientUserId, UserType.Client, clientId, null, [], []);
            var jwtPlainSpoof = TokenFor(plainStaffId, UserType.Staff, null, facilityId, [SystemRoles.WarehouseOperator], [Permissions.Global.ManageFxRates, Permissions.Billing.ManageRates]);
            var jwtFin = TokenFor(finStaffId, UserType.Staff, null, facilityId, ["PgFinanceFxMgr"], [Permissions.Global.ManageFxRates, Permissions.Billing.ManageRates]);

            var effFrom = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
            var pubAt1 = new DateTime(2026, 11, 19, 10, 11, 57, DateTimeKind.Utc);
            var pubAt2 = new DateTime(2026, 11, 20, 10, 9, 20, DateTimeKind.Utc);
            var pgNrbPayloadA = ReadEmbeddedFxArtifact("NRB_API_V1_20261120.json");
            var pgNrbPayloadB = ReadEmbeddedFxArtifact("NRB_API_V1_20261121.json");

            // F02 confirmation before authentic USD/NPR onboarding: quarantined legacy USD/NPR row MUST fail closed 422!
            if ((await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
                {
                    amount = 10m, fromCurrencyCode = "USD", toCurrencyCode = "NPR",
                    effectiveDateUtc = effFrom.AddHours(6), valuationMode = FxValuationMode.Retrospective
                }))).StatusCode != HttpStatusCode.UnprocessableEntity)
            {
                throw new InvalidOperationException("MDM13-R03-F02 quarantined legacy NRB row leaked into conversion resolution!");
            }

            // RBAC + R03 provenance checks
            var raceBodyA = new
            {
                baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
                effectiveFromUtc = effFrom, effectiveToUtc = (DateTime?)null,
                unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 160.075m,
                sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261120",
                publishedAtUtc = pubAt1, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-20", sourceArtifactPayload = pgNrbPayloadA
            };
            if ((await http.PostAsJsonAsync("/api/v1/fx-rates", raceBodyA)).StatusCode != HttpStatusCode.Unauthorized ||
                (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtClient, raceBodyA))).StatusCode != HttpStatusCode.Forbidden ||
                (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtPlainSpoof, raceBodyA))).StatusCode != HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException("MDM-1.3 PostgreSQL FX RBAC check failed.");
            }

            // MDM13-R03 negative checks on PostgreSQL HTTP API: fabricated bulletin even with valid self-SHA256 & tampered rate fail closed 422
            var fakePgPayload = "{\"bulletinVersion\":\"NRB-PG-FABRICATED\",\"midRate\":999}";
            var fakePgSha = $"SHA256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fakePgPayload))).ToLowerInvariant()}";
            if ((await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
                {
                    baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
                    effectiveFromUtc = effFrom, effectiveToUtc = (DateTime?)null,
                    unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 160.075m,
                    sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-PG-FABRICATED",
                    publishedAtUtc = pubAt1, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-20",
                    integrityHash = fakePgSha, sourceArtifactPayload = fakePgPayload
                }))).StatusCode != HttpStatusCode.UnprocessableEntity ||
                (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
                {
                    baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
                    effectiveFromUtc = effFrom, effectiveToUtc = (DateTime?)null,
                    unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 188.888m,
                    sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261120",
                    publishedAtUtc = pubAt1, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-20"
                }))).StatusCode != HttpStatusCode.UnprocessableEntity)
            {
                throw new InvalidOperationException("MDM13-R03 PostgreSQL official authority authenticity check did not fail closed.");
            }

            // Create two overlapping Draft INR/NPR rates & approve concurrently (SupersedeOverlapping=false -> strictly 1x 200, 1x 409)
            var d1 = (await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, raceBodyA)))
                .Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
            var d2 = (await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
            {
                baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
                effectiveFromUtc = effFrom.AddHours(2), effectiveToUtc = (DateTime?)null,
                unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 160.075m,
                sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261121",
                publishedAtUtc = pubAt2, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-21", sourceArtifactPayload = pgNrbPayloadB
            }))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;

            var raceStatuses = await Task.WhenAll(
                http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{d1.Id}/approve", jwtFin, new { evidenceVerified = true, verificationNotes = "Race approval 1 verified." })),
                http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{d2.Id}/approve", jwtFin, new { evidenceVerified = true, verificationNotes = "Race approval 2 verified." })));

            if (raceStatuses.Count(r => r.StatusCode == HttpStatusCode.OK) != 1 ||
                raceStatuses.Count(r => r.StatusCode == HttpStatusCode.Conflict) != 1)
            {
                throw new InvalidOperationException($"MDM-1.3 PostgreSQL concurrent FX approval race expected [200, 409], got [{string.Join(", ", raceStatuses.Select(r => r.StatusCode))}]");
            }

            Guid winningRateId;
            string winningBulletinVersion;
            await using (var chkDb = CreatePgDb(targetConn, null))
            {
                var winner = await chkDb.FxRateRecords
                    .Where(r => r.BaseCurrencyCode == "INR" && r.QuoteCurrencyCode == "NPR" && r.SourceAuthority == FxRateSourceAuthority.NRB && r.ApprovalStatus == FxRateApprovalStatus.VerifiedApproved && r.IsActive)
                    .Select(r => new { r.Id, r.BulletinVersion, r.EvidenceReference, r.TransformationTrace })
                    .SingleAsync();
                if (string.IsNullOrWhiteSpace(winner.EvidenceReference) || string.IsNullOrWhiteSpace(winner.TransformationTrace))
                    throw new InvalidOperationException("MDM13-R03 PostgreSQL official rate missing persisted EvidenceReference or TransformationTrace.");
                winningRateId = winner.Id;
                winningBulletinVersion = winner.BulletinVersion;
            }

            // Rollback atomicity during intentional FX supersession via ThrowAfterSaveInterceptor
            var useSecondForRollback = string.Equals(winningBulletinVersion, "NRB-20261120", StringComparison.Ordinal);
            var draftSupersede = (await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
            {
                baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
                effectiveFromUtc = effFrom.AddDays(1), effectiveToUtc = (DateTime?)null,
                unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 160.075m,
                sourceAuthority = FxRateSourceAuthority.NRB,
                bulletinVersion = useSecondForRollback ? "NRB-20261121" : "NRB-20261120",
                publishedAtUtc = useSecondForRollback ? pubAt2 : pubAt1,
                benchmarkSeriesId = useSecondForRollback ? "NRB-FOREX-V1-2026-11-21" : "NRB-FOREX-V1-2026-11-20",
                sourceArtifactPayload = useSecondForRollback ? pgNrbPayloadB : pgNrbPayloadA
            }))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;

            var rbUser = new PgStaffUserContext(finStaffId);
            await using (var rbDb = CreatePgDb(targetConn, new ThrowAfterSaveInterceptor(), rbUser))
            {
                var h = new ApproveFxRateCommandHandler(rbDb, new CompliancePlatformAuthorization(rbDb, rbUser), new DateTimeProvider(), new OfficialFxBulletinVerifier());
                try
                {
                    await h.Handle(new ApproveFxRateCommand(
                        draftSupersede.Id, true, "Rollback test FX supersession.", SupersedeOverlapping: true, ExpectedActiveRateId: winningRateId),
                        CancellationToken.None);
                    throw new InvalidOperationException("Expected ForcedRollbackException did not throw during FX supersession.");
                }
                catch (ForcedRollbackException)
                {
                    // Expected
                }
            }

            // MDM13-R02 PostgreSQL: Future-effective supersession coverage gap MUST be rejected 422 while preserving authentic USD/NPR active v1 (148.97/149.57 -> 149.27)!
            var usdNprDraft = (await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
            {
                baseCurrencyCode = "USD", quoteCurrencyCode = "NPR",
                effectiveFromUtc = effFrom, effectiveToUtc = (DateTime?)null,
                unitMultiplier = 1, buyRate = 148.97m, sellRate = 149.57m, midRate = 149.27m,
                sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261120",
                publishedAtUtc = pubAt1, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-20", sourceArtifactPayload = pgNrbPayloadA
            }))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
            if ((await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{usdNprDraft.Id}/approve", jwtFin, new
            {
                evidenceVerified = true, verificationNotes = "Approve authentic NRB Nov 20 USD/NPR fixing."
            }))).StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Failed approving PG USD/NPR rate.");

            var usdNprFuture = (await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
            {
                baseCurrencyCode = "USD", quoteCurrencyCode = "NPR",
                effectiveFromUtc = DateTime.UtcNow.AddDays(15), effectiveToUtc = (DateTime?)null,
                unitMultiplier = 1, buyRate = 147.98m, sellRate = 148.58m, midRate = 148.28m,
                sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261121",
                publishedAtUtc = pubAt2, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-21", sourceArtifactPayload = pgNrbPayloadB
            }))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
            if ((await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{usdNprFuture.Id}/approve", jwtFin, new
            {
                evidenceVerified = true, verificationNotes = "Attempt future USD/NPR gap supersession.", supersedeOverlapping = true
            }))).StatusCode != HttpStatusCode.UnprocessableEntity)
                throw new InvalidOperationException("MDM13-R02 PostgreSQL future-effective FX supersession did not fail closed.");

            // Immediate USD/NPR supersession with authentic NRB Nov 21 (147.98/148.58 -> 148.28) -> HistoricalAsKnown before v2 recovers v1 (1492.70), current recovers v2 (1482.80)!
            var usdNprV1 = (await (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/fx-rates/{usdNprDraft.Id}", jwtClient))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
            await Task.Delay(30);
            var usdNprSup2 = (await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
            {
                baseCurrencyCode = "USD", quoteCurrencyCode = "NPR",
                effectiveFromUtc = effFrom.AddDays(1), effectiveToUtc = (DateTime?)null,
                unitMultiplier = 1, buyRate = 147.98m, sellRate = 148.58m, midRate = 148.28m,
                sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261121",
                publishedAtUtc = pubAt2, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-21", sourceArtifactPayload = pgNrbPayloadB
            }))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
            var sup2Approve = await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{usdNprSup2.Id}/approve", jwtFin, new
            {
                evidenceVerified = true, verificationNotes = "Approve immediate USD/NPR supersession.", supersedeOverlapping = true, expectedActiveRateId = usdNprV1.Id
            }));
            if (sup2Approve.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"PG USD/NPR v2 supersession failed: {sup2Approve.StatusCode}");
            var sup2Dto = (await sup2Approve.Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
            if (string.IsNullOrWhiteSpace(sup2Dto.EvidenceReference) || !sup2Dto.EvidenceReference.Contains("NRB_API_V1_20261121.json", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(sup2Dto.TransformationTrace) || !sup2Dto.TransformationTrace.Contains("NRB_API_V1_20261121.json", StringComparison.Ordinal))
                throw new InvalidOperationException($"PG USD/NPR v2 missing expected EvidenceReference/TransformationTrace: {sup2Dto.EvidenceReference}");

            var asKnownBeforeSup = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
            {
                amount = 10m, fromCurrencyCode = "USD", toCurrencyCode = "NPR",
                effectiveDateUtc = effFrom.AddDays(2),
                asOfUtc = sup2Dto.ApprovedAtUtc!.Value.AddMilliseconds(-5),
                rateType = FxRateType.Mid,
                valuationMode = FxValuationMode.HistoricalAsKnown
            }))).Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions);
            var usdNprConv = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
            {
                amount = 10m, fromCurrencyCode = "USD", toCurrencyCode = "NPR",
                effectiveDateUtc = effFrom.AddDays(2),
                rateType = FxRateType.Mid
            }))).Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions);
            if (asKnownBeforeSup?.ConvertedAmount != 1492.70m || usdNprConv?.ConvertedAmount != 1482.80m || !usdNprConv.ResolvedFromDatabase)
                throw new InvalidOperationException($"MDM13-R01/R02 PG historical continuity check failed: before={asKnownBeforeSup?.ConvertedAmount}, current={usdNprConv?.ConvertedAmount}");

            // MDM13-R01 PG check: Future-published ManualApproved (published+approved in future) cannot shadow already-published rate!
            var lateManual = FxRateRecord.CreateDraft(
                "USD", "NPR", effFrom.AddDays(3), null, 1, 150.00m, 151.00m, 150.50m,
                FxRateSourceAuthority.ManualApproved, "PG-MAN-LATE", DateTime.UtcNow.AddDays(2),
                evidenceReference: "MANUAL_GOVERNANCE_OVERRIDE");
            lateManual.Approve(finStaffId, DateTime.UtcNow, "Manual override pre-staged for future publication.", evidenceReference: "MANUAL_GOVERNANCE_OVERRIDE");
            await using (var extraDb = CreatePgDb(targetConn, null))
            {
                extraDb.FxRateRecords.Add(lateManual);
                await extraDb.SaveChangesAsync();
            }
            var shadowCheck = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
            {
                amount = 10m, fromCurrencyCode = "USD", toCurrencyCode = "NPR",
                effectiveDateUtc = effFrom.AddDays(4),
                valuationMode = FxValuationMode.HistoricalAsKnown
            }))).Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions);
            if (shadowCheck!.ConvertedAmount != 1482.80m || shadowCheck.SourceAuthority != "NRB")
                throw new InvalidOperationException($"MDM13-R01 PG future-published authority shadowed NRB: {shadowCheck.SourceAuthority}={shadowCheck.ConvertedAmount}");

            // Missing FX pair fails closed 422; retroactive PUT/DELETE return 409; UOM canonical + cross-dim checks
            if ((await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
                {
                    amount = 100m, fromCurrencyCode = "GBP", toCurrencyCode = "NPR",
                    effectiveDateUtc = effFrom.AddHours(6)
                }))).StatusCode != HttpStatusCode.UnprocessableEntity ||
                (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/fx-rates/{usdNprSup2.Id}", jwtFin, new { }))).StatusCode != HttpStatusCode.Conflict ||
                (await http.SendAsync(Authed(HttpMethod.Delete, $"/api/v1/fx-rates/{usdNprSup2.Id}", jwtFin))).StatusCode != HttpStatusCode.Conflict ||
                (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/uom/convert", jwtClient, new { quantity = 5m, fromUomCode = "KG", toUomCode = "M" }))).StatusCode != HttpStatusCode.UnprocessableEntity)
            {
                throw new InvalidOperationException("MDM-1.3 PostgreSQL fail-closed / immutability / UOM guard failed.");
            }

            var uomConv = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/uom/convert", jwtClient, new
            {
                quantity = 10m, fromUomCode = "KG", toUomCode = "LB"
            }))).Content.ReadFromJsonAsync<UomConversionResultDto>(JsonOptions);
            if (uomConv!.ConvertedQuantity != 22.0462m)
                throw new InvalidOperationException($"Unexpected PG KG->LB conversion: {uomConv.ConvertedQuantity}");

            Console.WriteLine("PASS PostgreSQL MDM-1.3 Migration, CurrencyMaster, Dynamic FX Exclusion/Race, UnitMultiplier, Rollback & UOM verified cleanly.");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var dropConn = new NpgsqlConnection(adminBuilder.ConnectionString);
            await dropConn.OpenAsync();
            await using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", dropConn);
            await dropCmd.ExecuteNonQueryAsync();
        }
    }

    private static string ReadEmbeddedFxArtifact(string fileName)
    {
        var assembly = typeof(FxRateProvider).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string jwt, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    private static ApplicationDbContext CreatePgDb(string connStr, IInterceptor? interceptor, ICurrentUserContext? userOverride = null)
    {
        var user = userOverride ?? new PgStaffUserContext(Guid.NewGuid());
        var audit = new AuditableEntityInterceptor(user, new DateTimeProvider());
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connStr, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(audit);
        if (interceptor != null) builder.AddInterceptors(interceptor);
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
            .AddApplicationPart(typeof(FxRatesController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddSingleton<IOfficialFxBulletinVerifier, OfficialFxBulletinVerifier>();
        builder.Services.AddSingleton<IFxRateProvider>(sp => new FxRateProvider(sp.GetService<IServiceScopeFactory>()));
        builder.Services.AddScoped<IFxConversionService, FxConversionService>();
        builder.Services.AddScoped<IUomConversionService, UomConversionService>();
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

    private sealed class PgStaffUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "pgfin@m13.test";
        public Guid? TenantId => null;
        public Guid? FacilityId => null;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions =>
        [
            Warehouse3PL.Domain.Constants.Permissions.Global.ManageFxRates,
            Warehouse3PL.Domain.Constants.Permissions.Billing.ManageRates
        ];
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => false;
        public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
        public bool IsInRole(string role) => false;
    }
}
