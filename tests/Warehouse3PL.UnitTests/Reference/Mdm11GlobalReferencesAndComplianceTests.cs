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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Reference;
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
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;

namespace Warehouse3PL.UnitTests.Reference;

public sealed class Mdm11GlobalReferencesAndComplianceTests
{
    private const string TestSecret = "Nexus3PL-Mdm11-Compliance-JwtSecret-2026-Strong!";
    private const string TestIssuer = "nexus3pl-mdm11-auth";
    private const string TestAudience = "nexus3pl-mdm11-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Seeder_IsIdempotent_SeedsAuthoritativeUsAndNepalCatalogs_AndKeepsPoliciesDraft()
    {
        var dbName = $"mdm11-seed-{Guid.NewGuid():N}";
        using var db = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        await ReferenceCatalogSeeder.SeedAsync(db);
        await ReferenceCatalogSeeder.SeedAsync(db); // second invocation must be idempotent

        var countries = await db.CountryProfiles.AsNoTracking().OrderBy(c => c.CountryCode).ToListAsync();
        Assert.Equal(2, countries.Count);

        var np = countries.Single(c => c.CountryCode == "NP");
        Assert.Equal("NPL", np.IsoAlpha3Code);
        Assert.Equal("524", np.NumericCode);
        Assert.Equal("NPR", np.DefaultCurrencyCode);
        Assert.Equal("Asia/Kathmandu", np.PrimaryTimeZoneId);
        Assert.Equal(AddressSchemaType.NepalProvinceDistrictPalika, np.AddressSchemaType);
        Assert.Equal(PrimaryCalendarSystem.BikramSambatDual, np.PrimaryCalendarSystem);
        Assert.Equal(FiscalCalendarSystem.BikramSambat, np.FiscalCalendarSystem);
        Assert.Equal(4, np.DefaultFiscalYearStartMonth); // BS month 4 = Shrawan

        var us = countries.Single(c => c.CountryCode == "US");
        Assert.Equal("USA", us.IsoAlpha3Code);
        Assert.Equal("840", us.NumericCode);
        Assert.Equal("USD", us.DefaultCurrencyCode);
        Assert.Equal("America/Chicago", us.PrimaryTimeZoneId);
        Assert.Equal(AddressSchemaType.UsStateZip, us.AddressSchemaType);
        Assert.Equal(PrimaryCalendarSystem.Gregorian, us.PrimaryCalendarSystem);
        Assert.Equal(FiscalCalendarSystem.Gregorian, us.FiscalCalendarSystem);
        Assert.Equal(1, us.DefaultFiscalYearStartMonth);

        var usStates = await db.AdministrativeDivisions
            .Where(d => d.CountryCode == "US" && d.DivisionLevel == AdministrativeDivisionLevel.State)
            .ToListAsync();
        Assert.Equal(51, usStates.Count); // 50 states + DC
        Assert.Contains(usStates, s => s.IsoCode == "US-IL" && s.Code == "IL" && s.Name == "Illinois");
        Assert.Contains(usStates, s => s.IsoCode == "US-DC" && s.Code == "DC");

        var npProvinces = await db.AdministrativeDivisions
            .Where(d => d.CountryCode == "NP" && d.DivisionLevel == AdministrativeDivisionLevel.Province)
            .ToListAsync();
        Assert.Equal(7, npProvinces.Count);
        Assert.Contains(npProvinces, p => p.IsoCode == "NP-P3" && p.Name == "Bagmati");

        var ktmDistrict = await db.AdministrativeDivisions
            .SingleAsync(d => d.CountryCode == "NP" && d.DivisionLevel == AdministrativeDivisionLevel.District && d.Code == "KTM");
        Assert.Equal(npProvinces.Single(p => p.Code == "P3").Id, ktmDistrict.ParentDivisionId);

        var ktmPalika = await db.AdministrativeDivisions
            .SingleAsync(d => d.CountryCode == "NP" && d.DivisionLevel == AdministrativeDivisionLevel.LocalLevelPalika && d.Code == "KTM-KMC");
        Assert.Equal(ktmDistrict.Id, ktmPalika.ParentDivisionId);

        var zones = await db.TimeZoneProfiles.ToListAsync();
        Assert.Contains(zones, z => z.TimeZoneId == "Asia/Kathmandu" && !z.ObservesDst);
        Assert.Contains(zones, z => z.TimeZoneId == "America/Chicago" && z.ObservesDst);

        // MDM11-D01: Seeded policies must start strictly Draft & IsActive = false
        var policies = await db.CountryCompliancePolicies.OrderBy(p => p.PolicyCode).ToListAsync();
        Assert.Equal(3, policies.Count);
        Assert.All(policies, p =>
        {
            Assert.Equal(CompliancePolicyApprovalStatus.Draft, p.ApprovalStatus);
            Assert.False(p.IsActive);
            Assert.Null(p.ApprovedByUserId);
            Assert.Null(p.ApprovedAtUtc);
        });
        Assert.Contains(policies, p => p.PolicyCode == "NP-DRI-VCTS");
        Assert.Contains(policies, p => p.PolicyCode == "NP-IRD-CBMS");
        Assert.Contains(policies, p => p.PolicyCode == "NP-IRD-VAT");
    }

    [Fact]
    public void CountryProfile_And_TimeZoneOffsetResolver_ValidatePostalSyntaxAndDynamicDstOffsets()
    {
        var us = CountryProfile.Create(
            "US", "USA", "840", "United States", "USD", "America/Chicago",
            AddressSchemaType.UsStateZip, @"^\d{5}(-\d{4})?$", null,
            PrimaryCalendarSystem.Gregorian, FiscalCalendarSystem.Gregorian, 1);

        var np = CountryProfile.Create(
            "NP", "NPL", "524", "Nepal", "NPR", "Asia/Kathmandu",
            AddressSchemaType.NepalProvinceDistrictPalika, @"^\d{5}(\d{2})?$", @"^\d{5}$",
            PrimaryCalendarSystem.BikramSambatDual, FiscalCalendarSystem.BikramSambat, 4);

        Assert.True(us.IsSyntacticallyValidPostalCode("60601", out var usLegacy));
        Assert.False(usLegacy);
        Assert.True(us.IsSyntacticallyValidPostalCode("60601-1234", out _));
        Assert.False(us.IsSyntacticallyValidPostalCode("ABCDE", out _));

        Assert.True(np.IsSyntacticallyValidPostalCode("44600", out _));
        Assert.True(np.IsSyntacticallyValidPostalCode("4460001", out _));
        Assert.False(np.IsSyntacticallyValidPostalCode("4460", out _));
        Assert.False(np.IsSyntacticallyValidPostalCode("KTM-446", out _));

        var resolver = new TimeZoneOffsetResolver();
        var winterUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var summerUtc = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(-360, resolver.GetUtcOffsetMinutes("America/Chicago", winterUtc));
        Assert.Equal(-300, resolver.GetUtcOffsetMinutes("America/Chicago", summerUtc));
        Assert.Equal(345, resolver.GetUtcOffsetMinutes("Asia/Kathmandu", winterUtc));
        Assert.Equal(345, resolver.GetUtcOffsetMinutes("Asia/Kathmandu", summerUtc));
    }

    [Fact]
    public void ConfigResolutionState_PreservesSpecification21_WithoutOverloadingComplianceExemption()
    {
        var states = Enum.GetNames<ConfigResolutionState>();
        Assert.Equal(
            ["ExplicitValue", "Inherited", "ExplicitNull", "Disabled", "Prohibited"],
            states);
        Assert.DoesNotContain("NotApplicable", states);
        Assert.Equal(
            ["Required", "NotRequired", "Indeterminate"],
            Enum.GetNames<ComplianceApplicabilityStatus>());
    }

    [Fact]
    public async Task Evaluator_EnforcesDraftIsolation_AndDistinguishesRequiredNotRequiredAndIndeterminate()
    {
        var dbName = $"mdm11-eval-{Guid.NewGuid():N}";
        using var db = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        await ReferenceCatalogSeeder.SeedAsync(db);
        var evaluator = new CountryComplianceEvaluator(db, new DateTimeProvider());

        // 1. Draft seeded policy MUST NOT enforce -> Indeterminate (never NotRequired)
        var draftEval = await evaluator.EvaluateAsync(new ComplianceEvaluationRequest(
            "NP",
            "NP-DRI-VCTS",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            BusinessClassification: "B2BWholesale",
            OrderCategory: "CommercialWholesaleDispatch",
            CargoClass: "CommercialGoods"));

        Assert.Equal(ComplianceApplicabilityStatus.Indeterminate, draftEval.Status);
        Assert.Contains("ActiveVerifiedPolicy", draftEval.MissingOrAmbiguousInputs);
        Assert.False(draftEval.AssertsAuthorityVerified);

        // 2. Approved synthetic test policy verifies all 3 typed outcomes cleanly
        var syntheticPredicate = ReferenceMappings.SerializePredicate(new ComplianceApplicabilityPredicate
        {
            RequiredBusinessClassifications = ["SyntheticRequiredEntity"],
            ExemptBusinessClassifications = ["SyntheticExcludedEntity"],
            RequiredOrderCategories = ["SyntheticStandardDispatch"],
            ExemptOrderCategories = ["SyntheticExcludedOrder"],
            TreatUnlistedValuesAsIndeterminate = true,
            Notes = "Synthetic unit-test predicate; not a legal compliance determination."
        });

        var policy = CountryCompliancePolicy.CreateDraft(
            "NP",
            "SYNTH-TEST-POLICY",
            1,
            "Synthetic Evaluation Scenario",
            "Synthetic Test Standard Sec. 1",
            "Synthetic Notice 2026-01",
            syntheticPredicate,
            ComplianceEnforcementMode.MandatoryFailClosed,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        policy.Approve(Guid.NewGuid(), DateTime.UtcNow, "Verified synthetic unit-test provenance record.");
        db.CountryCompliancePolicies.Add(policy);
        await db.SaveChangesAsync();

        var required = await evaluator.EvaluateAsync(new ComplianceEvaluationRequest(
            "NP", "SYNTH-TEST-POLICY",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            BusinessClassification: "SyntheticRequiredEntity",
            OrderCategory: "SyntheticStandardDispatch"));

        Assert.Equal(ComplianceApplicabilityStatus.Required, required.Status);
        Assert.Equal(1, required.EvaluatedPolicyVersion);
        Assert.Equal(ComplianceEvidenceRequirement.ManualReferenceRecorded, required.EvidenceRequirement);
        Assert.False(required.AssertsAuthorityVerified);
        Assert.NotEmpty(required.MatchedPredicates);

        var notRequired = await evaluator.EvaluateAsync(new ComplianceEvaluationRequest(
            "NP", "SYNTH-TEST-POLICY",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            BusinessClassification: "SyntheticExcludedEntity",
            OrderCategory: "SyntheticExcludedOrder"));

        Assert.Equal(ComplianceApplicabilityStatus.NotRequired, notRequired.Status);
        Assert.Equal(ComplianceEvidenceRequirement.None, notRequired.EvidenceRequirement);

        // Missing classification MUST return Indeterminate, NEVER NotRequired
        var missingInput = await evaluator.EvaluateAsync(new ComplianceEvaluationRequest(
            "NP", "SYNTH-TEST-POLICY",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            BusinessClassification: null,
            OrderCategory: "SyntheticStandardDispatch"));

        Assert.Equal(ComplianceApplicabilityStatus.Indeterminate, missingInput.Status);
        Assert.Contains("BusinessClassification", missingInput.MissingOrAmbiguousInputs);

        // Unrecognized/ambiguous classification MUST return Indeterminate
        var ambiguousInput = await evaluator.EvaluateAsync(new ComplianceEvaluationRequest(
            "NP", "SYNTH-TEST-POLICY",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            BusinessClassification: "UnverifiedMixedTrade",
            OrderCategory: "SyntheticStandardDispatch"));

        Assert.Equal(ComplianceApplicabilityStatus.Indeterminate, ambiguousInput.Status);
        Assert.Contains(ambiguousInput.MissingOrAmbiguousInputs, m => m.StartsWith("BusinessClassification:Unrecognized", StringComparison.Ordinal));

        // Conflicting dimension match -> Indeterminate
        var conflicting = await evaluator.EvaluateAsync(new ComplianceEvaluationRequest(
            "NP", "SYNTH-TEST-POLICY",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            BusinessClassification: "SyntheticRequiredEntity",
            OrderCategory: "SyntheticExcludedOrder"));

        Assert.Equal(ComplianceApplicabilityStatus.Indeterminate, conflicting.Status);
        Assert.Contains("ConflictingPredicateMatch", conflicting.MissingOrAmbiguousInputs);

        // Out-of-window instant -> Indeterminate
        var outOfWindow = await evaluator.EvaluateAsync(new ComplianceEvaluationRequest(
            "NP", "SYNTH-TEST-POLICY",
            new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            BusinessClassification: "SyntheticRequiredEntity",
            OrderCategory: "SyntheticStandardDispatch"));

        Assert.Equal(ComplianceApplicabilityStatus.Indeterminate, outOfWindow.Status);
    }

    [Fact]
    public async Task HttpJwtEndpoints_EnforceAuth_DbBackedStaffIdentity_DraftEvidenceGate_AndSupersession()
    {
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var clientUserId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var plainStaffId = Guid.NewGuid();
        var viewStaffId = Guid.NewGuid();
        var manageStaffId = Guid.NewGuid();

        await using var host = await BuildJwtTestAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);

            db.Clients.Add(new Client { Id = clientId, AccountCode = "MDM11-CL", CompanyName = "MDM11 Tenant", Status = ClientStatus.Active });
            db.Facilities.Add(new Facility { Id = facilityId, FacilityCode = "MDM11-FAC", Name = "MDM11 Hub", Timezone = "Asia/Kathmandu", CountryCode = "NP", IsActive = true });

            var complianceViewRole = new Role { RoleName = "ComplianceViewer", Description = "Viewer", IsSystemRole = false };
            complianceViewRole.RolePermissions.Add(new RolePermission { Role = complianceViewRole, PermissionKey = Permissions.Global.ComplianceView, AccessLevel = AccessLevel.Read });

            var complianceMgrRole = new Role { RoleName = "ComplianceManager", Description = "Manager", IsSystemRole = false };
            complianceMgrRole.RolePermissions.Add(new RolePermission { Role = complianceMgrRole, PermissionKey = Permissions.Global.ComplianceView, AccessLevel = AccessLevel.Read });
            complianceMgrRole.RolePermissions.Add(new RolePermission { Role = complianceMgrRole, PermissionKey = Permissions.Global.ComplianceManage, AccessLevel = AccessLevel.Admin });

            var opRole = new Role { RoleName = SystemRoles.WarehouseOperator, Description = "Operator", IsSystemRole = true };

            db.Roles.AddRange(complianceViewRole, complianceMgrRole, opRole);

            var uClient = new User { Id = clientUserId, Email = "client@mdm11.test", FirstName = "C", LastName = "U", UserType = UserType.Client, ClientId = clientId, Status = UserStatus.Active, PasswordHash = "x" };
            var uDriver = new User { Id = driverUserId, Email = "driver@mdm11.test", FirstName = "D", LastName = "U", UserType = UserType.Driver, Status = UserStatus.Active, PasswordHash = "x" };
            var uPlainStaff = new User { Id = plainStaffId, Email = "plainstaff@mdm11.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facilityId, Status = UserStatus.Active, PasswordHash = "x" };
            var uViewStaff = new User { Id = viewStaffId, Email = "viewstaff@mdm11.test", FirstName = "V", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facilityId, Status = UserStatus.Active, PasswordHash = "x" };
            var uManageStaff = new User { Id = manageStaffId, Email = "mgrstaff@mdm11.test", FirstName = "M", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facilityId, Status = UserStatus.Active, PasswordHash = "x" };

            db.Users.AddRange(uClient, uDriver, uPlainStaff, uViewStaff, uManageStaff);
            db.UserRoles.AddRange(
                new UserRole { User = uPlainStaff, Role = opRole },
                new UserRole { User = uViewStaff, Role = complianceViewRole },
                new UserRole { User = uManageStaff, Role = complianceMgrRole });

            await db.SaveChangesAsync();
        });

        using var http = host.GetTestClient();
        string JwtFor(Guid id, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
        {
            using var scope = host.Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            return tokens.GenerateTokens(new User
            {
                Id = id,
                Email = $"{id:N}@mdm11.test",
                FirstName = "T",
                LastName = "U",
                UserType = ut,
                ClientId = cid,
                AssignedFacilityId = fid
            }, roles, perms).AccessToken;
        }

        // Real DB-aligned JWTs
        var jwtClientSpoof = JwtFor(clientUserId, UserType.Client, clientId, null, [SystemRoles.SuperAdmin], [Permissions.Global.ComplianceManage]);
        var jwtClientLegit = JwtFor(clientUserId, UserType.Client, clientId, null, [], []);
        var jwtDriverSpoof = JwtFor(driverUserId, UserType.Driver, null, null, [], [Permissions.Global.ComplianceManage, Permissions.Global.ComplianceView]);
        var jwtPlainStaffSpoofPerms = JwtFor(plainStaffId, UserType.Staff, null, facilityId, [SystemRoles.WarehouseOperator], [Permissions.Global.ComplianceManage, Permissions.Global.ComplianceView]);
        var jwtViewStaff = JwtFor(viewStaffId, UserType.Staff, null, facilityId, ["ComplianceViewer"], [Permissions.Global.ComplianceView]);
        var jwtManageStaff = JwtFor(manageStaffId, UserType.Staff, null, facilityId, ["ComplianceManager"], [Permissions.Global.ComplianceView, Permissions.Global.ComplianceManage]);

        // 1. Anonymous rejected with 401
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/reference/countries")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/compliance-policies")).StatusCode);

        // 2. Client/Driver legitimate reference reads work, reference mutations fail 405
        var clientCountriesReq = Authed(HttpMethod.Get, "/api/v1/reference/countries", jwtClientLegit);
        var countriesResp = await http.SendAsync(clientCountriesReq);
        Assert.Equal(HttpStatusCode.OK, countriesResp.StatusCode);

        var tzWinterResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/timezones?atUtc=2026-01-15T12:00:00Z", jwtClientLegit));
        var tzWinter = (await tzWinterResp.Content.ReadFromJsonAsync<List<TimeZoneProfileDto>>(JsonOptions))!;
        Assert.Equal(-360, tzWinter.Single(z => z.TimeZoneId == "America/Chicago").CurrentUtcOffsetMinutes);
        Assert.Equal(345, tzWinter.Single(z => z.TimeZoneId == "Asia/Kathmandu").CurrentUtcOffsetMinutes);

        var tzSummerResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/timezones?atUtc=2026-07-15T12:00:00Z", jwtClientLegit));
        var tzSummer = await tzSummerResp.Content.ReadFromJsonAsync<List<TimeZoneProfileDto>>(JsonOptions);
        Assert.Equal(-300, tzSummer!.Single(z => z.TimeZoneId == "America/Chicago").CurrentUtcOffsetMinutes);

        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await http.SendAsync(Authed(HttpMethod.Put, "/api/v1/reference/countries/US", jwtManageStaff, new { name = "Mutated" }))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await http.SendAsync(Authed(HttpMethod.Delete, "/api/v1/reference/countries/NP", jwtManageStaff))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/reference/countries", jwtManageStaff, new { countryCode = "XX" }))).StatusCode);

        // 3. Spoofed Client/Driver & plain Staff claiming compliance permissions fail fail-closed (401/403)
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/compliance-policies", jwtClientSpoof))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/compliance-policies", jwtDriverSpoof))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/compliance-policies", jwtPlainStaffSpoofPerms))).StatusCode);

        // View-only Staff can read policies, but gets 403 on mutations
        var listResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/compliance-policies?countryCode=NP", jwtViewStaff));
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var seededList = await listResp.Content.ReadFromJsonAsync<List<CountryCompliancePolicyDto>>(JsonOptions);
        Assert.Equal(3, seededList!.Count);

        var seededVcts = seededList.Single(p => p.PolicyCode == "NP-DRI-VCTS");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/compliance-policies/{seededVcts.Id}/approve", jwtViewStaff,
                new { evidenceVerified = true, verificationEvidenceNotes = "Should be forbidden for viewer" }))).StatusCode);

        // 4. Approve seeded Draft with placeholder notice citation or unverified flag MUST fail 422!
        var failPlaceholder = await http.SendAsync(Authed(
            HttpMethod.Post,
            $"/api/v1/compliance-policies/{seededVcts.Id}/approve",
            jwtManageStaff,
            new { evidenceVerified = true, verificationEvidenceNotes = "Reviewed draft fields" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failPlaceholder.StatusCode);

        var failUnverified = await http.SendAsync(Authed(
            HttpMethod.Post,
            $"/api/v1/compliance-policies/{seededVcts.Id}/approve",
            jwtManageStaff,
            new
            {
                evidenceVerified = false,
                verificationEvidenceNotes = "Verified against official gazette.",
                officialNoticeCitation = "Nepal Gazette Notice 2082-04-01"
            }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failUnverified.StatusCode);

        // 5. Valid Staff approval succeeds and binds authoritative DB Staff user ID
        var approveV1 = await http.SendAsync(Authed(
            HttpMethod.Post,
            $"/api/v1/compliance-policies/{seededVcts.Id}/approve",
            jwtManageStaff,
            new
            {
                evidenceVerified = true,
                verificationEvidenceNotes = "Verified against DRI Nepal circular archive.",
                officialNoticeCitation = "DRI Notice 2082/03 — VCTS Threshold Schedule"
            }));
        Assert.Equal(HttpStatusCode.OK, approveV1.StatusCode);
        var v1Dto = await approveV1.Content.ReadFromJsonAsync<CountryCompliancePolicyDto>(JsonOptions);
        Assert.Equal(CompliancePolicyApprovalStatus.VerifiedApproved, v1Dto!.ApprovalStatus);
        Assert.True(v1Dto.IsActive);
        Assert.Equal(manageStaffId, v1Dto.ApprovedByUserId);

        // 6. Create Draft v2 with overlapping window
        var createV2 = await http.SendAsync(Authed(
            HttpMethod.Post,
            "/api/v1/compliance-policies",
            jwtManageStaff,
            new
            {
                countryCode = "NP",
                policyCode = "NP-DRI-VCTS",
                title = "Nepal DRI VCTS Revised v2",
                statutorySourceRef = "Revenue Leakage Act (Amended)",
                officialNoticeCitation = "DRI Notice 2083/01 — Updated Schedule",
                applicabilityPredicateJson = seededVcts.ApplicabilityPredicateJson,
                enforcementMode = ComplianceEnforcementMode.MandatoryFailClosed,
                effectiveFromUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                effectiveToUtc = (DateTime?)null
            }));
        Assert.Equal(HttpStatusCode.Created, createV2.StatusCode);
        var v2Dto = await createV2.Content.ReadFromJsonAsync<CountryCompliancePolicyDto>(JsonOptions);
        Assert.Equal(2, v2Dto!.Version);
        Assert.Equal(CompliancePolicyApprovalStatus.Draft, v2Dto.ApprovalStatus);
        Assert.False(v2Dto.IsActive);

        // Approving v2 WITHOUT SupersedeOverlapping rejects 409 Conflict!
        var conflictResp = await http.SendAsync(Authed(
            HttpMethod.Post,
            $"/api/v1/compliance-policies/{v2Dto.Id}/approve",
            jwtManageStaff,
            new
            {
                evidenceVerified = true,
                verificationEvidenceNotes = "Competing overlap without supersession",
                supersedeOverlapping = false
            }));
        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);

        // Approving v2 WITH SupersedeOverlapping = true succeeds and preserves v1 as Superseded!
        var supersedeResp = await http.SendAsync(Authed(
            HttpMethod.Post,
            $"/api/v1/compliance-policies/{v2Dto.Id}/approve",
            jwtManageStaff,
            new
            {
                evidenceVerified = true,
                verificationEvidenceNotes = "Intentional supersession verified against DRI Notice 2083/01.",
                supersedeOverlapping = true,
                expectedActivePolicyId = v1Dto.Id
            }));
        Assert.Equal(HttpStatusCode.OK, supersedeResp.StatusCode);
        var approvedV2 = await supersedeResp.Content.ReadFromJsonAsync<CountryCompliancePolicyDto>(JsonOptions);
        Assert.Equal(CompliancePolicyApprovalStatus.VerifiedApproved, approvedV2!.ApprovalStatus);
        Assert.True(approvedV2.IsActive);

        var historyResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/compliance-policies?countryCode=NP&policyCode=NP-DRI-VCTS", jwtViewStaff));
        var history = await historyResp.Content.ReadFromJsonAsync<List<CountryCompliancePolicyDto>>(JsonOptions);
        Assert.Equal(2, history!.Count);
        var historicalV1 = history.Single(h => h.Version == 1);
        Assert.Equal(CompliancePolicyApprovalStatus.Superseded, historicalV1.ApprovalStatus);
        Assert.False(historicalV1.IsActive);
        Assert.Equal(approvedV2.Id, historicalV1.SupersededByPolicyId);

        // Published / superseded versions cannot be mutated or deleted via PUT/DELETE (405)
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/compliance-policies/{approvedV2.Id}", jwtManageStaff, new { title = "Tamper" }))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await http.SendAsync(Authed(HttpMethod.Delete, $"/api/v1/compliance-policies/{historicalV1.Id}", jwtManageStaff))).StatusCode);
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string jwt, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (body != null)
        {
            req.Content = JsonContent.Create(body);
        }
        return req;
    }

    private static async Task<WebApplication> BuildJwtTestAppAsync(Func<ApplicationDbContext, Task> seedAsync)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<JwtOptions>(o =>
        {
            o.SecretKey = TestSecret;
            o.Issuer = TestIssuer;
            o.Audience = TestAudience;
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
                    ValidIssuer = TestIssuer,
                    ValidAudience = TestAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
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

        var dbName = $"mdm11-http-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ApplicationDbContext>((sp, opts) =>
            opts.UseInMemoryDatabase(dbName).AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthenticatedTenantResolution();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await seedAsync(db);
        return app;
    }
}
