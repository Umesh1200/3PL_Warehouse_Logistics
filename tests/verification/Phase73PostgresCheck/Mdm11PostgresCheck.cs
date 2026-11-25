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
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;

internal static class Mdm11PostgresCheck
{
    private const string JwtSecret = "Nexus3PL-Mdm11-PostgreSql-Verification-Key-2026!";
    private const string JwtIssuer = "nexus3pl-mdm11-pg-auth";
    private const string JwtAudience = "nexus3pl-mdm11-pg-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static async Task RunAsync(string baseConnectionString)
    {
        Console.WriteLine("\n[MDM-1.1] Running Live PostgreSQL Migration, Exclusion Constraint, Concurrency & JWT Verification...");

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm11_pg_{Guid.NewGuid():N}";

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
            // 1. Clean migration upgrade from Phase745PodIdempotencyAndAuditColumns -> MDM1_1_GlobalReferencesAndCompliance
            await using (var migDb = CreatePgDb(targetConn, null))
            {
                var migrator = migDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync("20261124123507_Phase745PodIdempotencyAndAuditColumns");
                await migrator.MigrateAsync();
            }

            // 2. Verify btree_gist extension and EXCLUDE USING gist constraint exist in pg_catalog
            await using (var sqlConn = new NpgsqlConnection(targetConn))
            {
                await sqlConn.OpenAsync();
                await using var extCmd = new NpgsqlCommand("SELECT COUNT(*) FROM pg_extension WHERE extname = 'btree_gist';", sqlConn);
                if (Convert.ToInt32(await extCmd.ExecuteScalarAsync()) != 1)
                    throw new InvalidOperationException("MDM-1.1 PostgreSQL verification failed: btree_gist extension missing.");

                await using var exclCmd = new NpgsqlCommand(
                    "SELECT pg_get_constraintdef(c.oid) FROM pg_constraint c JOIN pg_class t ON c.conrelid = t.oid WHERE t.relname = 'country_compliance_policies' AND c.conname = 'excl_country_compliance_policies_active_window';",
                    sqlConn);
                var def = (await exclCmd.ExecuteScalarAsync())?.ToString()
                    ?? throw new InvalidOperationException("MDM-1.1 PostgreSQL verification failed: excl_country_compliance_policies_active_window missing.");
                if (!def.Contains("EXCLUDE USING gist", StringComparison.OrdinalIgnoreCase) ||
                    !def.Contains("VerifiedApproved", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Unexpected exclusion constraint definition: {def}");
                }
            }

            // 3. Idempotent ReferenceCatalogSeeder (two passes)
            await using (var seedDb = CreatePgDb(targetConn, null))
            {
                await ReferenceCatalogSeeder.SeedAsync(seedDb);
                await ReferenceCatalogSeeder.SeedAsync(seedDb);

                var countryCount = await seedDb.CountryProfiles.CountAsync();
                var usStateCount = await seedDb.AdministrativeDivisions.CountAsync(d => d.CountryCode == "US" && d.DivisionLevel == AdministrativeDivisionLevel.State);
                var npProvinceCount = await seedDb.AdministrativeDivisions.CountAsync(d => d.CountryCode == "NP" && d.DivisionLevel == AdministrativeDivisionLevel.Province);
                var tzCount = await seedDb.TimeZoneProfiles.CountAsync();
                var draftPolicies = await seedDb.CountryCompliancePolicies.CountAsync(p => p.ApprovalStatus == CompliancePolicyApprovalStatus.Draft && !p.IsActive);

                if (countryCount != 2 || usStateCount != 51 || npProvinceCount != 7 || tzCount != 9 || draftPolicies != 3)
                {
                    throw new InvalidOperationException(
                        $"MDM-1.1 PostgreSQL seed mismatch: countries={countryCount}, usStates={usStateCount}, npProvinces={npProvinceCount}, tz={tzCount}, drafts={draftPolicies}");
                }
            }

            // 4. Verify DB-level EXCLUDE USING gist enforces active window uniqueness even against raw SQL
            await using (var exclTestConn = new NpgsqlConnection(targetConn))
            {
                await exclTestConn.OpenAsync();
                var pA = Guid.NewGuid();
                var pB = Guid.NewGuid();
                await using (var insA = new NpgsqlCommand(
                    $"INSERT INTO country_compliance_policies (policy_id, country_code, policy_code, version, title, statutory_source_ref, official_notice_citation, applicability_predicate_json, enforcement_mode, effective_from_utc, approval_status, config_revision, is_active, created_at) " +
                    $"VALUES ('{pA}', 'US', 'PG-EXCL-RAW', 1, 'Raw A', 'Ref A', 'Citation A', '{{\"requiresTaxpayerMandateFlag\":true}}'::jsonb, 'MandatoryFailClosed', '2026-01-01T00:00:00Z', 'VerifiedApproved', 1, true, NOW());",
                    exclTestConn))
                {
                    await insA.ExecuteNonQueryAsync();
                }

                bool caughtExclusion = false;
                try
                {
                    await using var insB = new NpgsqlCommand(
                        $"INSERT INTO country_compliance_policies (policy_id, country_code, policy_code, version, title, statutory_source_ref, official_notice_citation, applicability_predicate_json, enforcement_mode, effective_from_utc, approval_status, config_revision, is_active, created_at) " +
                        $"VALUES ('{pB}', 'US', 'PG-EXCL-RAW', 2, 'Raw B', 'Ref B', 'Citation B', '{{\"requiresTaxpayerMandateFlag\":true}}'::jsonb, 'MandatoryFailClosed', '2026-06-01T00:00:00Z', 'VerifiedApproved', 1, true, NOW());",
                        exclTestConn);
                    await insB.ExecuteNonQueryAsync();
                }
                catch (PostgresException pgEx) when (pgEx.SqlState == "23P01")
                {
                    caughtExclusion = true;
                }

                if (!caughtExclusion)
                    throw new InvalidOperationException("MDM-1.1 PostgreSQL EXCLUDE USING gist did not block overlapping VerifiedApproved rows.");
            }

            // 5. Real JWT HTTP checks + Concurrent First-Approval Contention + Rollback Atomicity + Supersession
            var facilityId = Guid.NewGuid();
            var clientId = Guid.NewGuid();
            var clientUserId = Guid.NewGuid();
            var plainStaffId = Guid.NewGuid();
            var manageStaffId = Guid.NewGuid();
            var draftRace1Id = Guid.NewGuid();
            var draftRace2Id = Guid.NewGuid();
            var draftSupersedeId = Guid.NewGuid();

            await using var host = await BuildPgHttpAppAsync(targetConn);
            using var http = host.GetTestClient();

            await using (var setupDb = CreatePgDb(targetConn, null))
            {
                setupDb.Clients.Add(new Client { Id = clientId, AccountCode = "PG-M11", CompanyName = "PG MDM11 Client", Status = ClientStatus.Active });
                setupDb.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "PG-M11-FAC", Name = "PG KTM Hub", CountryCode = "NP", Timezone = "Asia/Kathmandu", IsActive = true });

                var opRole = new Role { RoleName = SystemRoles.WarehouseOperator, Description = "Op", IsSystemRole = true };
                var mgrRole = new Role { RoleName = "ComplianceManagerPg", Description = "Mgr", IsSystemRole = false };
                mgrRole.RolePermissions.Add(new RolePermission { Role = mgrRole, PermissionKey = Permissions.Global.ComplianceView, AccessLevel = AccessLevel.Read });
                mgrRole.RolePermissions.Add(new RolePermission { Role = mgrRole, PermissionKey = Permissions.Global.ComplianceManage, AccessLevel = AccessLevel.Admin });
                setupDb.Roles.AddRange(opRole, mgrRole);

                var pgClient = new User { Id = clientUserId, Email = "pgclient@mdm11.test", FirstName = "C", LastName = "U", UserType = UserType.Client, ClientId = clientId, Status = UserStatus.Active, PasswordHash = "x" };
                var pgPlain = new User { Id = plainStaffId, Email = "pgplain@mdm11.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facilityId, Status = UserStatus.Active, PasswordHash = "x" };
                var pgMgr = new User { Id = manageStaffId, Email = "pgmgr@mdm11.test", FirstName = "M", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facilityId, Status = UserStatus.Active, PasswordHash = "x" };
                setupDb.Users.AddRange(pgClient, pgPlain, pgMgr);

                setupDb.UserRoles.AddRange(
                    new UserRole { User = pgPlain, Role = opRole },
                    new UserRole { User = pgMgr, Role = mgrRole });

                var pred = """{"requiresTaxpayerMandateFlag":true,"treatUnlistedValuesAsIndeterminate":true}""";
                setupDb.CountryCompliancePolicies.AddRange(
                    CountryCompliancePolicy.CreateDraft("NP", "PG-RACE-POLICY", 1, "Race v1", "Statute Ref 1", "Notice 2082-01", pred, ComplianceEnforcementMode.MandatoryFailClosed, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, draftRace1Id),
                    CountryCompliancePolicy.CreateDraft("NP", "PG-RACE-POLICY", 2, "Race v2", "Statute Ref 2", "Notice 2082-02", pred, ComplianceEnforcementMode.MandatoryFailClosed, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), null, draftRace2Id),
                    CountryCompliancePolicy.CreateDraft("NP", "PG-RACE-POLICY", 3, "Supersede v3", "Statute Ref 3", "Notice 2083-01", pred, ComplianceEnforcementMode.MandatoryFailClosed, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), null, draftSupersedeId));

                await setupDb.SaveChangesAsync();
            }

            string TokenFor(Guid uid, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
            {
                using var scope = host.Services.CreateScope();
                return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateTokens(new User
                {
                    Id = uid, Email = $"{uid:N}@mdm11.test", FirstName = "T", LastName = "U", UserType = ut, ClientId = cid, AssignedFacilityId = fid
                }, roles, perms).AccessToken;
            }

            var jwtClient = TokenFor(clientUserId, UserType.Client, clientId, null, [], []);
            var jwtPlainStaffSpoof = TokenFor(plainStaffId, UserType.Staff, null, facilityId, [SystemRoles.WarehouseOperator], [Permissions.Global.ComplianceManage]);
            var jwtMgr = TokenFor(manageStaffId, UserType.Staff, null, facilityId, ["ComplianceManagerPg"], [Permissions.Global.ComplianceView, Permissions.Global.ComplianceManage]);

            // HTTP checks
            if ((await http.GetAsync("/api/v1/reference/countries")).StatusCode != HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Anonymous reference GET did not return 401.");
            if ((await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/compliance-policies", jwtPlainStaffSpoof))).StatusCode != HttpStatusCode.Forbidden)
                throw new InvalidOperationException("Unprivileged Staff with spoofed JWT permission did not return 403.");
            if ((await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/countries", jwtClient))).StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Authenticated Client reference GET did not return 200.");

            var tzResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/timezones?atUtc=2026-07-15T12:00:00Z", jwtClient));
            var tzList = (await tzResp.Content.ReadFromJsonAsync<List<TimeZoneProfileDto>>(JsonOptions))!;
            if (tzList.Single(z => z.TimeZoneId == "America/Chicago").CurrentUtcOffsetMinutes != -300 ||
                tzList.Single(z => z.TimeZoneId == "Asia/Kathmandu").CurrentUtcOffsetMinutes != 345)
                throw new InvalidOperationException("MDM-1.1 PostgreSQL dynamic timezone check failed.");

            // Concurrent first-approval race (SupersedeOverlapping = false): strictly 1x 200 OK, 1x 409 Conflict
            var raceStatuses = await Task.WhenAll(
                SendApproveAsync(http, draftRace1Id, jwtMgr, supersedeOverlapping: false),
                SendApproveAsync(http, draftRace2Id, jwtMgr, supersedeOverlapping: false));

            if (raceStatuses.Count(s => s == HttpStatusCode.OK) != 1 ||
                raceStatuses.Count(s => s == HttpStatusCode.Conflict) != 1)
            {
                throw new InvalidOperationException($"MDM-1.1 PostgreSQL concurrent approval race expected [200, 409], got [{string.Join(", ", raceStatuses)}]");
            }

            Guid winningId;
            await using (var checkRaceDb = CreatePgDb(targetConn, null))
            {
                var activeRows = await checkRaceDb.CountryCompliancePolicies
                    .AsNoTracking()
                    .Where(p => p.CountryCode == "NP" && p.PolicyCode == "PG-RACE-POLICY" && p.ApprovalStatus == CompliancePolicyApprovalStatus.VerifiedApproved && p.IsActive)
                    .ToListAsync();
                if (activeRows.Count != 1)
                    throw new InvalidOperationException($"Expected 1 active row after concurrent approval race, found {activeRows.Count}");
                winningId = activeRows[0].Id;
            }

            // Rollback atomicity during intentional supersession via ThrowAfterSaveInterceptor
            var rollbackUser = new PgStaffUserContext(manageStaffId);
            await using (var rbDb = CreatePgDb(targetConn, new ThrowAfterSaveInterceptor(), rollbackUser))
            {
                var handler = new ApproveCompliancePolicyCommandHandler(
                    rbDb,
                    new CompliancePlatformAuthorization(rbDb, rollbackUser),
                    new DateTimeProvider());
                try
                {
                    await handler.Handle(new ApproveCompliancePolicyCommand(
                        draftSupersedeId,
                        EvidenceVerified: true,
                        VerificationEvidenceNotes: "Rollback atomicity test supersession",
                        SupersedeOverlapping: true,
                        ExpectedActivePolicyId: winningId), CancellationToken.None);
                    throw new InvalidOperationException("Expected ForcedRollbackException did not throw during policy supersession.");
                }
                catch (ForcedRollbackException)
                {
                    // Expected
                }
            }

            await using (var verifyRbDb = CreatePgDb(targetConn, null))
            {
                var winnerAfterRb = await verifyRbDb.CountryCompliancePolicies.AsNoTracking().SingleAsync(p => p.Id == winningId);
                var v3AfterRb = await verifyRbDb.CountryCompliancePolicies.AsNoTracking().SingleAsync(p => p.Id == draftSupersedeId);
                if (winnerAfterRb.ApprovalStatus != CompliancePolicyApprovalStatus.VerifiedApproved || !winnerAfterRb.IsActive ||
                    v3AfterRb.ApprovalStatus != CompliancePolicyApprovalStatus.Draft || v3AfterRb.IsActive)
                {
                    throw new InvalidOperationException("MDM-1.1 PostgreSQL supersession rollback leaked partial state!");
                }
            }

            // Clean retry of intentional supersession over HTTP succeeds and marks winner Superseded
            var retrySupersede = await http.SendAsync(Authed(
                HttpMethod.Post,
                $"/api/v1/compliance-policies/{draftSupersedeId}/approve",
                jwtMgr,
                new
                {
                    evidenceVerified = true,
                    verificationEvidenceNotes = "Verified intentional supersession retry after rollback.",
                    supersedeOverlapping = true,
                    expectedActivePolicyId = winningId
                }));
            if (retrySupersede.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"MDM-1.1 PostgreSQL supersession retry failed with {retrySupersede.StatusCode}");

            // Verify historical v1 + v3 state AND test concurrent intentional supersession race (v4 vs v5 expecting v3)
            var draftRaceV4Id = Guid.NewGuid();
            var draftRaceV5Id = Guid.NewGuid();
            await using (var finalDb = CreatePgDb(targetConn, null))
            {
                var prevWinner = await finalDb.CountryCompliancePolicies.AsNoTracking().SingleAsync(p => p.Id == winningId);
                var newActive = await finalDb.CountryCompliancePolicies.AsNoTracking().SingleAsync(p => p.Id == draftSupersedeId);
                if (prevWinner.ApprovalStatus != CompliancePolicyApprovalStatus.Superseded || prevWinner.IsActive ||
                    prevWinner.SupersededByPolicyId != draftSupersedeId ||
                    newActive.ApprovalStatus != CompliancePolicyApprovalStatus.VerifiedApproved || !newActive.IsActive)
                {
                    throw new InvalidOperationException("MDM-1.1 PostgreSQL final supersession provenance mismatch.");
                }

                var pred = """{"requiresTaxpayerMandateFlag":true,"treatUnlistedValuesAsIndeterminate":true}""";
                finalDb.CountryCompliancePolicies.AddRange(
                    CountryCompliancePolicy.CreateDraft("NP", "PG-RACE-POLICY", 4, "Supersede Race v4", "Statute Ref 4", "Notice 2083-02A", pred, ComplianceEnforcementMode.MandatoryFailClosed, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), null, draftRaceV4Id),
                    CountryCompliancePolicy.CreateDraft("NP", "PG-RACE-POLICY", 5, "Supersede Race v5", "Statute Ref 5", "Notice 2083-02B", pred, ComplianceEnforcementMode.MandatoryFailClosed, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), null, draftRaceV5Id));
                await finalDb.SaveChangesAsync();
            }

            // Concurrent intentional supersession race expecting draftSupersedeId -> strictly 1x 200 OK, 1x 409 Conflict
            var supRace = await Task.WhenAll(
                SendApproveAsync(http, draftRaceV4Id, jwtMgr, supersedeOverlapping: true, expectedActivePolicyId: draftSupersedeId),
                SendApproveAsync(http, draftRaceV5Id, jwtMgr, supersedeOverlapping: true, expectedActivePolicyId: draftSupersedeId));

            if (supRace.Count(s => s == HttpStatusCode.OK) != 1 ||
                supRace.Count(s => s == HttpStatusCode.Conflict) != 1)
            {
                throw new InvalidOperationException($"MDM-1.1 PostgreSQL concurrent supersession race expected [200, 409], got [{string.Join(", ", supRace)}]");
            }

            // 6. MDM11-R02 & MDM11-R03 Live PostgreSQL Verification:
            //    a) Reject malformed/null/contradictory predicate JSON & naive Unspecified UTC timestamps
            var badPredResp = await http.SendAsync(Authed(
                HttpMethod.Post,
                "/api/v1/compliance-policies",
                jwtMgr,
                new
                {
                    countryCode = "NP",
                    policyCode = "PG-BAD-PRED",
                    title = "Bad Predicate",
                    statutorySourceRef = "Ref",
                    officialNoticeCitation = "Citation",
                    applicabilityPredicateJson = """{"requiredBusinessClassifications":null,"requiresTaxpayerMandateFlag":true}""",
                    enforcementMode = ComplianceEnforcementMode.MandatoryFailClosed,
                    effectiveFromUtc = DateTime.UtcNow.AddDays(-1),
                    effectiveToUtc = (DateTime?)null
                }));
            if (badPredResp.StatusCode != HttpStatusCode.UnprocessableEntity)
                throw new InvalidOperationException($"Expected 422 for null predicate array on PG HTTP, got {badPredResp.StatusCode}");

            var badUtcResp = await http.SendAsync(Authed(
                HttpMethod.Post,
                "/api/v1/compliance-policies",
                jwtMgr,
                new
                {
                    countryCode = "NP",
                    policyCode = "PG-BAD-UTC",
                    title = "Bad UTC",
                    statutorySourceRef = "Ref",
                    officialNoticeCitation = "Citation",
                    applicabilityPredicateJson = """{"requiresTaxpayerMandateFlag":true}""",
                    enforcementMode = ComplianceEnforcementMode.MandatoryFailClosed,
                    effectiveFromUtc = "2026-06-01T00:00:00",
                    effectiveToUtc = (string?)null
                }));
            if (badUtcResp.StatusCode != HttpStatusCode.BadRequest)
                throw new InvalidOperationException($"Expected 400 for Unspecified DateTimeKind on PG HTTP, got {badUtcResp.StatusCode}");

            //    b) Future-dated overlapping replacement rejected (coverage gap guard), disjoint future replacement allowed,
            //       immediate replacement succeeds and historical as-of evaluation recovers Superseded v1!
            var pgNow = DateTime.UtcNow;
            var predJson = """{"requiresTaxpayerMandateFlag":true,"treatUnlistedValuesAsIndeterminate":true}""";

            var cV1 = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/compliance-policies", jwtMgr, new
            {
                countryCode = "NP", policyCode = "PG-TEMPORAL", title = "Temporal v1",
                statutorySourceRef = "Statute T1", officialNoticeCitation = "Notice T1",
                applicabilityPredicateJson = predJson, enforcementMode = ComplianceEnforcementMode.MandatoryFailClosed,
                effectiveFromUtc = pgNow.AddDays(-40), effectiveToUtc = (DateTime?)null
            }));
            var tV1 = (await cV1.Content.ReadFromJsonAsync<CountryCompliancePolicyDto>(JsonOptions))!;
            if ((await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/compliance-policies/{tV1.Id}/approve", jwtMgr,
                new { evidenceVerified = true, verificationEvidenceNotes = "PG temporal v1 baseline approval" }))).StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Failed approving PG temporal v1.");

            var cFutureOverlap = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/compliance-policies", jwtMgr, new
            {
                countryCode = "NP", policyCode = "PG-TEMPORAL", title = "Future Overlap v2",
                statutorySourceRef = "Statute T2", officialNoticeCitation = "Notice T2",
                applicabilityPredicateJson = predJson, enforcementMode = ComplianceEnforcementMode.MandatoryFailClosed,
                effectiveFromUtc = pgNow.AddDays(30), effectiveToUtc = (DateTime?)null
            }));
            var tFutureOverlap = (await cFutureOverlap.Content.ReadFromJsonAsync<CountryCompliancePolicyDto>(JsonOptions))!;
            var gapResp = await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/compliance-policies/{tFutureOverlap.Id}/approve", jwtMgr, new
            {
                evidenceVerified = true,
                verificationEvidenceNotes = "Attempt overlapping future supersession",
                supersedeOverlapping = true
            }));
            if (gapResp.StatusCode != HttpStatusCode.UnprocessableEntity)
                throw new InvalidOperationException($"Expected 422 coverage gap rejection for future overlapping supersession, got {gapResp.StatusCode}");

            // Immediate replacement v3 supersedes v1 atomically; historical evaluation at pgNow-20d recovers v1!
            var cImmediateV3 = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/compliance-policies", jwtMgr, new
            {
                countryCode = "NP", policyCode = "PG-TEMPORAL", title = "Immediate Replacement v3",
                statutorySourceRef = "Statute T3", officialNoticeCitation = "Notice T3",
                applicabilityPredicateJson = predJson, enforcementMode = ComplianceEnforcementMode.AdvisoryWarning,
                effectiveFromUtc = pgNow.AddSeconds(-2), effectiveToUtc = (DateTime?)null
            }));
            var tImmediateV3 = (await cImmediateV3.Content.ReadFromJsonAsync<CountryCompliancePolicyDto>(JsonOptions))!;
            var immResp = await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/compliance-policies/{tImmediateV3.Id}/approve", jwtMgr, new
            {
                evidenceVerified = true,
                verificationEvidenceNotes = "Immediate PG cutover verified.",
                supersedeOverlapping = true,
                expectedActivePolicyId = tV1.Id
            }));
            if (immResp.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException($"Immediate PG replacement failed with {immResp.StatusCode}");

            // Disjoint future-dated schedule on a clean policy code: [now-40d, now+30d) + [now+30d, null) BOTH active simultaneously in PostgreSQL!
            var cDisjoint1 = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/compliance-policies", jwtMgr, new
            {
                countryCode = "NP", policyCode = "PG-DISJOINT", title = "Disjoint v1",
                statutorySourceRef = "Statute D1", officialNoticeCitation = "Notice D1",
                applicabilityPredicateJson = predJson, enforcementMode = ComplianceEnforcementMode.MandatoryFailClosed,
                effectiveFromUtc = pgNow.AddDays(-40), effectiveToUtc = pgNow.AddDays(30)
            }));
            var dV1 = (await cDisjoint1.Content.ReadFromJsonAsync<CountryCompliancePolicyDto>(JsonOptions))!;
            var cDisjoint2 = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/compliance-policies", jwtMgr, new
            {
                countryCode = "NP", policyCode = "PG-DISJOINT", title = "Disjoint Future v2",
                statutorySourceRef = "Statute D2", officialNoticeCitation = "Notice D2",
                applicabilityPredicateJson = predJson, enforcementMode = ComplianceEnforcementMode.ConditionalRequired,
                effectiveFromUtc = pgNow.AddDays(30), effectiveToUtc = (DateTime?)null
            }));
            var dV2 = (await cDisjoint2.Content.ReadFromJsonAsync<CountryCompliancePolicyDto>(JsonOptions))!;

            if ((await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/compliance-policies/{dV1.Id}/approve", jwtMgr,
                new { evidenceVerified = true, verificationEvidenceNotes = "Approve disjoint window v1" }))).StatusCode != HttpStatusCode.OK ||
                (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/compliance-policies/{dV2.Id}/approve", jwtMgr,
                new { evidenceVerified = true, verificationEvidenceNotes = "Approve disjoint future window v2" }))).StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException("Disjoint future-dated PostgreSQL approvals failed.");
            }

            await using (var evalDb = CreatePgDb(targetConn, null))
            {
                var pgEvaluator = new CountryComplianceEvaluator(evalDb, new DateTimeProvider());

                var hist = await pgEvaluator.EvaluateAsync(new ComplianceEvaluationRequest(
                    "NP", "PG-TEMPORAL", pgNow.AddDays(-20), TaxpayerMandateFlag: true));
                var curr = await pgEvaluator.EvaluateAsync(new ComplianceEvaluationRequest(
                    "NP", "PG-TEMPORAL", pgNow.AddMinutes(5), TaxpayerMandateFlag: true));
                var unspec = await pgEvaluator.EvaluateAsync(new ComplianceEvaluationRequest(
                    "NP", "PG-TEMPORAL", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Unspecified), TaxpayerMandateFlag: true));

                if (hist.Status != ComplianceApplicabilityStatus.Required || hist.EvaluatedPolicyVersion != 1 ||
                    curr.Status != ComplianceApplicabilityStatus.Required || curr.EvaluatedPolicyVersion != 3 ||
                    unspec.Status != ComplianceApplicabilityStatus.Indeterminate)
                {
                    throw new InvalidOperationException($"PG temporal/UTC evaluation mismatch: hist={hist.EvaluatedPolicyVersion}, curr={curr.EvaluatedPolicyVersion}, unspec={unspec.Status}");
                }

                var djNow = await pgEvaluator.EvaluateAsync(new ComplianceEvaluationRequest(
                    "NP", "PG-DISJOINT", pgNow, TaxpayerMandateFlag: true));
                var djFuture = await pgEvaluator.EvaluateAsync(new ComplianceEvaluationRequest(
                    "NP", "PG-DISJOINT", pgNow.AddDays(31), TaxpayerMandateFlag: true));

                if (djNow.EvaluatedPolicyVersion != 1 || djFuture.EvaluatedPolicyVersion != 2)
                {
                    throw new InvalidOperationException($"PG disjoint future schedule evaluation mismatch: now={djNow.EvaluatedPolicyVersion}, future={djFuture.EvaluatedPolicyVersion}");
                }
            }

            Console.WriteLine("PASS PostgreSQL MDM-1.1 Migration, btree_gist Exclusion, Idempotent Seed, Concurrent Approval & Supersession Races, Temporal Continuity, Rollback & JWT Security verified cleanly.");
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

    private static async Task<HttpStatusCode> SendApproveAsync(
        HttpClient http,
        Guid policyId,
        string jwt,
        bool supersedeOverlapping,
        Guid? expectedActivePolicyId = null)
    {
        using var resp = await http.SendAsync(Authed(
            HttpMethod.Post,
            $"/api/v1/compliance-policies/{policyId}/approve",
            jwt,
            new
            {
                evidenceVerified = true,
                verificationEvidenceNotes = "Concurrent approval contention test run.",
                supersedeOverlapping,
                expectedActivePolicyId
            }));
        return resp.StatusCode;
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
            .AddApplicationPart(typeof(ReferenceController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddSingleton<ITimeZoneOffsetResolver, TimeZoneOffsetResolver>();
        builder.Services.AddScoped<ICountryComplianceEvaluator, CountryComplianceEvaluator>();
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
        public string? Email => "pgstaff@mdm11.test";
        public Guid? TenantId => null;
        public Guid? FacilityId => null;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions =>
        [
            Warehouse3PL.Domain.Constants.Permissions.Global.ComplianceManage,
            Warehouse3PL.Domain.Constants.Permissions.Global.ComplianceView
        ];
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => false;
        public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
        public bool IsInRole(string role) => false;
    }
}
