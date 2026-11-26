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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
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
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;

namespace Warehouse3PL.UnitTests.Reference;

public sealed class Mdm13CurrencyFxAndUomTests
{
    private const string TestSecret = "Nexus3PL-Mdm13-CurrencyFxUom-JwtSecret-2026-Strong!";
    private const string TestIssuer = "nexus3pl-mdm13-auth";
    private const string TestAudience = "nexus3pl-mdm13-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Currencies_UomCatalog_FxRates_Rbac_UnitMultiplier_Rounding_And_FailClosed_WorkEndToEnd()
    {
        var clientId = Guid.NewGuid();
        var facId = Guid.NewGuid();
        var clientUserId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var plainStaffId = Guid.NewGuid();
        var financeStaffId = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);
            // Verify second pass is strictly idempotent
            await ReferenceCatalogSeeder.SeedAsync(db);

            db.Clients.Add(new Client { Id = clientId, AccountCode = "MDM13-C", CompanyName = "MDM13 Client", Status = ClientStatus.Active });
            db.Facilities.Add(new Facility { Id = facId, FacilityCode = "FAC-13", Name = "Hub 13", CountryCode = "NP", Timezone = "Asia/Kathmandu", IsActive = true });

            var opRole = new Role { RoleName = SystemRoles.WarehouseOperator, Description = "Op", IsSystemRole = true };
            var finRole = new Role { RoleName = "FinanceFxManager", Description = "FX Mgr", IsSystemRole = false };
            finRole.RolePermissions.Add(new RolePermission { Role = finRole, PermissionKey = Permissions.Billing.ManageRates, AccessLevel = AccessLevel.Admin });
            finRole.RolePermissions.Add(new RolePermission { Role = finRole, PermissionKey = Permissions.Global.ManageFxRates, AccessLevel = AccessLevel.Admin });
            db.Roles.AddRange(opRole, finRole);

            var uClient = new User { Id = clientUserId, Email = "c@mdm13.test", FirstName = "C", LastName = "U", UserType = UserType.Client, ClientId = clientId, Status = UserStatus.Active, PasswordHash = "x" };
            var uDriver = new User { Id = driverUserId, Email = "d@mdm13.test", FirstName = "D", LastName = "U", UserType = UserType.Driver, Status = UserStatus.Active, PasswordHash = "x" };
            var uPlain = new User { Id = plainStaffId, Email = "p@mdm13.test", FirstName = "P", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facId, Status = UserStatus.Active, PasswordHash = "x" };
            var uFin = new User { Id = financeStaffId, Email = "f@mdm13.test", FirstName = "F", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facId, Status = UserStatus.Active, PasswordHash = "x" };
            db.Users.AddRange(uClient, uDriver, uPlain, uFin);
            db.UserRoles.AddRange(
                new UserRole { User = uPlain, Role = opRole },
                new UserRole { User = uFin, Role = finRole });

            await db.SaveChangesAsync();
        });

        using var http = host.GetTestClient();
        string JwtFor(Guid id, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
        {
            using var scope = host.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateTokens(new User
            {
                Id = id, Email = $"{id:N}@mdm13.test", FirstName = "T", LastName = "U", UserType = ut, ClientId = cid, AssignedFacilityId = fid
            }, roles, perms).AccessToken;
        }

        var jwtClient = JwtFor(clientUserId, UserType.Client, clientId, null, [], []);
        var jwtDriver = JwtFor(driverUserId, UserType.Driver, null, null, [], [Permissions.Global.ManageFxRates]);
        var jwtPlainSpoof = JwtFor(plainStaffId, UserType.Staff, null, facId, [SystemRoles.WarehouseOperator], [Permissions.Global.ManageFxRates, Permissions.Billing.ManageRates]);
        var jwtFin = JwtFor(financeStaffId, UserType.Staff, null, facId, ["FinanceFxManager"], [Permissions.Global.ManageFxRates, Permissions.Billing.ManageRates]);

        // 1. Immutable CurrencyMaster & UOM catalogs: anonymous -> 401; authenticated -> 200; mutations -> 405
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/currencies")).StatusCode);
        var currenciesResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/currencies", jwtClient));
        Assert.Equal(HttpStatusCode.OK, currenciesResp.StatusCode);
        var currencies = (await currenciesResp.Content.ReadFromJsonAsync<List<CurrencyMasterDto>>(JsonOptions))!;
        Assert.Equal(12, currencies.Count);
        Assert.Equal(2, currencies.Single(c => c.CurrencyCode == "NPR").MinorUnits);
        Assert.Equal("524", currencies.Single(c => c.CurrencyCode == "NPR").NumericCode);
        Assert.Equal(0, currencies.Single(c => c.CurrencyCode == "JPY").MinorUnits);
        Assert.Equal(3, currencies.Single(c => c.CurrencyCode == "KWD").MinorUnits);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/currencies", jwtFin, new { currencyCode = "ZZZ" }))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/uom/definitions", jwtFin, new { uomCode = "ZZ" }))).StatusCode);

        // 2. FX rate RBAC: Client/Driver/spoofed Staff denied 403
        var effStart = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        var pubAt = new DateTime(2026, 11, 19, 10, 11, 57, DateTimeKind.Utc);
        var nrbPayload1 = ReadEmbeddedFxArtifact("NRB_API_V1_20261120.json");
        var draftBody = new
        {
            baseCurrencyCode = "INR",
            quoteCurrencyCode = "NPR",
            effectiveFromUtc = effStart,
            effectiveToUtc = (DateTime?)null,
            unitMultiplier = 100,
            buyRate = 160.00m,
            sellRate = 160.15m,
            midRate = 160.075m,
            sourceAuthority = FxRateSourceAuthority.NRB,
            bulletinVersion = "NRB-20261120",
            publishedAtUtc = pubAt,
            benchmarkSeriesId = "NRB-FOREX-V1-2026-11-20",
            sourceArtifactPayload = nrbPayload1
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/v1/fx-rates", draftBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtClient, draftBody))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtDriver, draftBody))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtPlainSpoof, draftBody))).StatusCode);

        // MDM13-R03: Official authority (NRB) with untrusted bulletin, fabricated self-hashed payload, altered quotation,
        // placeholder digest, or mismatched SHA-256 MUST fail closed 422!
        var fakePayload = "{\"bulletinVersion\":\"NRB-FABRICATED-999\",\"rates\":[{\"base\":\"INR\",\"quote\":\"NPR\",\"mid\":999}]}";
        var fakeSha256 = $"SHA256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fakePayload))).ToLowerInvariant()}";
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
            {
                baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
                effectiveFromUtc = effStart, effectiveToUtc = (DateTime?)null,
                unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 160.075m,
                sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-FABRICATED-999",
                publishedAtUtc = pubAt, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-20",
                integrityHash = fakeSha256,
                sourceArtifactPayload = fakePayload
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
            {
                baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
                effectiveFromUtc = effStart, effectiveToUtc = (DateTime?)null,
                unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 165.000m,
                sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261120",
                publishedAtUtc = pubAt, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-20",
                sourceArtifactPayload = nrbPayload1
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
            {
                baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
                effectiveFromUtc = effStart, effectiveToUtc = (DateTime?)null,
                unitMultiplier = 1, buyRate = 160.00m, sellRate = 160.15m, midRate = 160.075m,
                sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261120",
                publishedAtUtc = pubAt, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-20"
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
            {
                baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
                effectiveFromUtc = effStart, effectiveToUtc = (DateTime?)null,
                unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 160.075m,
                sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261120",
                publishedAtUtc = pubAt, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-20",
                integrityHash = "SHA256:0000000000000000000000000000000000000000000000000000000000000000"
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
            {
                baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
                effectiveFromUtc = effStart, effectiveToUtc = (DateTime?)null,
                unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 160.075m,
                sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261120",
                publishedAtUtc = pubAt, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-20",
                integrityHash = "SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                sourceArtifactPayload = nrbPayload1
            }))).StatusCode);

        // 3. Draft FX creation starts inactive; conversion fails closed 422 before approval!
        var createNrb = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, draftBody));
        Assert.Equal(HttpStatusCode.Created, createNrb.StatusCode);
        var nrbDraft = (await createNrb.Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
        Assert.Equal(FxRateApprovalStatus.Draft, nrbDraft.ApprovalStatus);
        Assert.False(nrbDraft.IsActive);
        Assert.Equal("https://www.nrb.org.np/api/forex/v1/rates?from=2026-11-20&to=2026-11-20&per_page=10&page=1#NRB_API_V1_20261120.json", nrbDraft.EvidenceReference);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
            {
                amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
                effectiveDateUtc = effStart.AddHours(6), rateType = FxRateType.Mid
            }))).StatusCode);

        // Approve without evidence or with short/placeholder verification notes -> 422; tampered approval payload -> 422
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft.Id}/approve", jwtFin, new
            {
                evidenceVerified = false, verificationNotes = "Missing attestation notes"
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft.Id}/approve", jwtFin, new
            {
                evidenceVerified = true, verificationNotes = "TBD"
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft.Id}/approve", jwtFin, new
            {
                evidenceVerified = true, verificationNotes = "Verified against NRB circular payload.", sourceArtifactPayload = "TAMPERED"
            }))).StatusCode);

        var approveNrb = await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft.Id}/approve", jwtFin, new
        {
            evidenceVerified = true,
            verificationNotes = "Verified against NRB daily fixing circular.",
            sourceArtifactPayload = nrbPayload1
        }));
        Assert.Equal(HttpStatusCode.OK, approveNrb.StatusCode);
        var nrbApproved = (await approveNrb.Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
        Assert.Equal(FxRateApprovalStatus.VerifiedApproved, nrbApproved.ApprovalStatus);
        Assert.True(nrbApproved.IsActive);
        Assert.Equal(1.60075m, nrbApproved.EffectiveMidUnitRate);
        Assert.Equal("NRB-FOREX-V1-2026-11-20", nrbApproved.BenchmarkSeriesId);
        Assert.Equal("SHA256:7f80df741991c96937167f852c5e3654418777cf871159a5233869f25483b0ad", nrbApproved.IntegrityHash);
        Assert.Equal("https://www.nrb.org.np/api/forex/v1/rates?from=2026-11-20&to=2026-11-20&per_page=10&page=1#NRB_API_V1_20261120.json", nrbApproved.EvidenceReference);
        Assert.Contains("raw=NRB_API_V1_20261120.json(data.payload[0].rates[INR])", nrbApproved.TransformationTrace);

        // 4. UnitMultiplier=100 conversion (1000 INR -> 1600.75 Mid / 1600.00 Buy / 1601.50 Sell NPR)
        var convMidResp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
            effectiveDateUtc = effStart.AddHours(6), rateType = FxRateType.Mid
        }));
        Assert.Equal(HttpStatusCode.OK, convMidResp.StatusCode);
        var convMid = (await convMidResp.Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions))!;
        Assert.Equal(1600.75m, convMid.ConvertedAmount);
        Assert.Equal(100, convMid.UnitMultiplier);
        Assert.True(convMid.ResolvedFromDatabase);

        var convBuy = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
            effectiveDateUtc = effStart.AddHours(6), rateType = FxRateType.Buy
        }))).Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions);
        Assert.Equal(1600.00m, convBuy!.ConvertedAmount);

        var convSell = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
            effectiveDateUtc = effStart.AddHours(6), rateType = FxRateType.Sell
        }))).Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions);
        Assert.Equal(1601.50m, convSell!.ConvertedAmount);

        // R01: HistoricalAsKnown BEFORE ApprovedAtUtc (even if after PublishedAtUtc) rejects 422, whereas Retrospective succeeds!
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
            {
                amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
                effectiveDateUtc = effStart.AddHours(6),
                asOfUtc = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc),
                valuationMode = FxValuationMode.HistoricalAsKnown
            }))).StatusCode);

        var retroResp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
            effectiveDateUtc = effStart.AddHours(6),
            asOfUtc = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc),
            valuationMode = FxValuationMode.Retrospective
        }));
        Assert.Equal(HttpStatusCode.OK, retroResp.StatusCode);
        Assert.Equal(1600.75m, (await retroResp.Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions))!.ConvertedAmount);

        // Valuation BEFORE PublishedAtUtc fails closed 422 in BOTH modes (no look-ahead!)
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
            {
                amount = 100m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
                effectiveDateUtc = effStart.AddHours(6),
                asOfUtc = pubAt.AddMinutes(-1),
                valuationMode = FxValuationMode.Retrospective
            }))).StatusCode);

        // 5. Fallback to verified historical ECB bulletin for exact bulletin date; missing/future dates fail closed 422
        var ecbFallback = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 100.00m, fromCurrencyCode = "EUR", toCurrencyCode = "USD",
            effectiveDateUtc = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc),
            asOfUtc = new DateTime(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc)
        }));
        Assert.Equal(HttpStatusCode.OK, ecbFallback.StatusCode);
        var ecbRes = (await ecbFallback.Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions))!;
        Assert.Equal(112.67m, ecbRes.ConvertedAmount);
        Assert.False(ecbRes.ResolvedFromDatabase);
        Assert.Equal("ECB-20261104", ecbRes.BulletinVersion);

        // 6. Retroactive PUT / DELETE -> 409; R02 future-effective supersession rejected (422, keeps active rate intact); immediate supersede -> 200; void -> 422
        Assert.Equal(HttpStatusCode.Conflict, (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/fx-rates/{nrbApproved.Id}", jwtFin, new { }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.SendAsync(Authed(HttpMethod.Delete, $"/api/v1/fx-rates/{nrbApproved.Id}", jwtFin))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Delete, $"/api/v1/fx-rates/{nrbApproved.Id}", jwtPlainSpoof))).StatusCode);

        var nrbPub2 = new DateTime(2026, 11, 20, 10, 9, 20, DateTimeKind.Utc);
        var nrbPayload2 = ReadEmbeddedFxArtifact("NRB_API_V1_20261121.json");
        var futureDraft = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
        {
            baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
            effectiveFromUtc = DateTime.UtcNow.AddDays(10), effectiveToUtc = (DateTime?)null,
            unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 160.075m,
            sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261121",
            publishedAtUtc = nrbPub2, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-21", sourceArtifactPayload = nrbPayload2
        }))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{futureDraft!.Id}/approve", jwtFin, new
            {
                evidenceVerified = true, verificationNotes = "Attempt future supersession gap", supersedeOverlapping = true
            }))).StatusCode);
        // Active nrbApproved remains untouched!
        Assert.True((await (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/fx-rates/{nrbApproved.Id}", jwtClient))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!.IsActive);

        var draft2Resp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
        {
            baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
            effectiveFromUtc = effStart.AddDays(1), effectiveToUtc = (DateTime?)null,
            unitMultiplier = 100, buyRate = 160.00m, sellRate = 160.15m, midRate = 160.075m,
            sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261121",
            publishedAtUtc = nrbPub2, benchmarkSeriesId = "NRB-FOREX-V1-2026-11-21", sourceArtifactPayload = nrbPayload2
        }));
        Assert.Equal(HttpStatusCode.Created, draft2Resp.StatusCode);
        var nrbDraft2 = (await draft2Resp.Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;

        Assert.Equal(HttpStatusCode.Conflict,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft2.Id}/approve", jwtFin, new
            {
                evidenceVerified = true, verificationNotes = "Attempt overlap without supersede flag", supersedeOverlapping = false
            }))).StatusCode);

        var supResp = await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft2.Id}/approve", jwtFin, new
        {
            evidenceVerified = true, verificationNotes = "Official NRB daily circular revision", supersedeOverlapping = true, expectedActiveRateId = nrbApproved.Id
        }));
        Assert.Equal(HttpStatusCode.OK, supResp.StatusCode);

        var prevAfterSup = await (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/fx-rates/{nrbApproved.Id}", jwtClient))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions);
        Assert.Equal(FxRateApprovalStatus.Superseded, prevAfterSup!.ApprovalStatus);
        Assert.False(prevAfterSup.IsActive);

        // R02 Historical Continuity: HistoricalAsKnown BEFORE v2 Supersession recovers v1 (1600.75) even though v1 is now Superseded!
        var asKnownBeforeSup = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
            effectiveDateUtc = effStart.AddDays(2),
            asOfUtc = prevAfterSup.SupersededAtUtc!.Value.AddMilliseconds(-1),
            valuationMode = FxValuationMode.HistoricalAsKnown
        }));
        Assert.Equal(HttpStatusCode.OK, asKnownBeforeSup.StatusCode);
        Assert.Equal(1600.75m, (await asKnownBeforeSup.Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions))!.ConvertedAmount);

        Assert.Equal(HttpStatusCode.OK,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft2.Id}/void", jwtFin, new
            {
                voidReason = "Corrected NRB bulletin retraction"
            }))).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
            {
                amount = 100m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
                effectiveDateUtc = effStart.AddDays(2)
            }))).StatusCode);

        // 7. Currency minor-unit rounding precision (JPY = 0 decimals AwayFromZero) with ManualApproved explicitly identified
        var jpyDraft = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
        {
            baseCurrencyCode = "USD", quoteCurrencyCode = "JPY",
            effectiveFromUtc = effStart, effectiveToUtc = (DateTime?)null,
            unitMultiplier = 1, buyRate = 152.10m, sellRate = 152.90m, midRate = 152.50m,
            sourceAuthority = FxRateSourceAuthority.ManualApproved, bulletinVersion = "MAN-JPY-1", publishedAtUtc = pubAt
        }))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions);
        var jpyAppResp = await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{jpyDraft!.Id}/approve", jwtFin, new
        {
            evidenceVerified = true, verificationNotes = "Manual treasury desk JPY fixing approved."
        }));
        Assert.Equal(HttpStatusCode.OK, jpyAppResp.StatusCode);
        var jpyApproved = (await jpyAppResp.Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
        Assert.Equal("MANUAL_GOVERNANCE_OVERRIDE", jpyApproved.EvidenceReference);
        Assert.Contains("MANUAL_GOVERNANCE_OVERRIDE", jpyApproved.TransformationTrace);

        // Trusted ECB embedded XML bulletin (ECB-20261113) verifies without caller supplying any hash/payload!
        var ecbMeta = FxBulletinManifest.Entries["ECB-20261113"];
        var ecbDraftResp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
        {
            baseCurrencyCode = "EUR", quoteCurrencyCode = "USD",
            effectiveFromUtc = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), effectiveToUtc = (DateTime?)null,
            unitMultiplier = 1, buyRate = 1.1052m, sellRate = 1.1052m, midRate = 1.1052m,
            sourceAuthority = FxRateSourceAuthority.ECB, bulletinVersion = "ECB-20261113",
            publishedAtUtc = ecbMeta.PublishedAtUtc
        }));
        Assert.Equal(HttpStatusCode.Created, ecbDraftResp.StatusCode);
        var ecbDraft = (await ecbDraftResp.Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
        var ecbAppResp = await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{ecbDraft.Id}/approve", jwtFin, new
        {
            evidenceVerified = true, verificationNotes = "Verified against ECB SDMX official artifact."
        }));
        Assert.Equal(HttpStatusCode.OK, ecbAppResp.StatusCode);
        var ecbApproved = (await ecbAppResp.Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
        Assert.Equal(ecbMeta.BenchmarkSeriesId, ecbApproved.BenchmarkSeriesId);
        Assert.Equal(ecbMeta.ExpectedSha256Digest, ecbApproved.IntegrityHash);
        Assert.Equal($"{ecbMeta.SourceUrl}#{ecbMeta.ArtifactFileName}", ecbApproved.EvidenceReference);

        var jpyConv = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 1.00m, fromCurrencyCode = "USD", toCurrencyCode = "JPY",
            effectiveDateUtc = effStart.AddHours(1)
        }))).Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions);
        Assert.Equal(153m, jpyConv!.ConvertedAmount); // 152.50 rounded to 0 decimals AwayFromZero = 153!
        Assert.Equal(0, jpyConv.TargetMinorUnits);
        Assert.Equal("ManualApproved", jpyConv.SourceAuthority);

        // 8. UOM Catalog & Conversions (canonical preservation, inverse, 2-hop, cross-dimension fail closed)
        var uomDefsResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/uom/definitions", jwtClient));
        var uomDefs = (await uomDefsResp.Content.ReadFromJsonAsync<List<UomDefinitionDto>>(JsonOptions))!;
        Assert.Contains(uomDefs, d => d.UomCode == "KG" && d.UneceRec20Code == "KGM" && d.IsCanonicalBase);
        Assert.Contains(uomDefs, d => d.UomCode == "CM" && d.UneceRec20Code == "CMT" && d.IsCanonicalBase);
        Assert.Contains(uomDefs, d => d.UomCode == "CM3" && d.UneceRec20Code == "CMQ" && d.IsCanonicalBase);

        var kgToLb = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/uom/convert", jwtClient, new
        {
            quantity = 10m, fromUomCode = "KG", toUomCode = "LB"
        }))).Content.ReadFromJsonAsync<UomConversionResultDto>(JsonOptions);
        Assert.Equal(22.0462m, kgToLb!.ConvertedQuantity);

        var twoHopGtoLb = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/uom/convert", jwtClient, new
        {
            quantity = 1000m, fromUomCode = "G", toUomCode = "LB"
        }))).Content.ReadFromJsonAsync<UomConversionResultDto>(JsonOptions);
        Assert.Equal(2.2046m, twoHopGtoLb!.ConvertedQuantity);
        Assert.Contains("->KG->", twoHopGtoLb.ResolutionPath, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/uom/convert", jwtClient, new
            {
                quantity = 5m, fromUomCode = "KG", toUomCode = "CM"
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/uom/convert", jwtClient, new
            {
                quantity = 2m, fromUomCode = "CS", toUomCode = "EA"
            }))).StatusCode);
    }

    [Fact]
    public async Task FxRateProvider_And_UomCanonicalService_PreserveDatabasePrecedence_And_MetricBases()
    {
        var dbName = $"mdm13-svc-{Guid.NewGuid():N}";
        using var db = Warehouse3PL.UnitTests.Common.TestDbContextFactory.Create(
            new Warehouse3PL.UnitTests.Common.TestTenantProvider { BypassTenantFilter = true },
            new Warehouse3PL.UnitTests.Common.TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        await ReferenceCatalogSeeder.SeedAsync(db);

        var approverId = Guid.NewGuid();
        var verifier = new OfficialFxBulletinVerifier();
        var vNrb20Inr = verifier.VerifyOfficialQuotation(
            FxRateSourceAuthority.NRB, "NRB-20261120", "INR", "NPR", FxRateDirection.QuotePerUnitBase,
            100, 160.00m, 160.15m, 160.075m, new DateTime(2026, 11, 19, 10, 11, 57, DateTimeKind.Utc));
        var nrbRate = FxRateRecord.CreateDraft(
            "INR", "NPR",
            new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
            null,
            100,
            160.00m, 160.15m, 160.075m,
            FxRateSourceAuthority.NRB,
            "NRB-20261120",
            vNrb20Inr.PublishedAtUtc,
            vNrb20Inr.BenchmarkSeriesId,
            vNrb20Inr.VerifiedIntegrityHash,
            FxRateDirection.QuotePerUnitBase,
            id: null,
            evidenceReference: vNrb20Inr.EvidenceReference,
            transformationTrace: vNrb20Inr.TransformationTrace);
        nrbRate.Approve(approverId, new DateTime(2026, 11, 19, 10, 15, 0, DateTimeKind.Utc), "Verified authentic NRB Nov 20 API payload.");
        db.FxRateRecords.Add(nrbRate);
        await db.SaveChangesAsync();

        var fxProvider = FxRateProvider.ForDbContext(db);
        var effectiveInrNpr = fxProvider.GetHistoricalExchangeRate(
            "INR", "NPR",
            new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc),
            out var src, out var ver, out _, out _, out _);
        Assert.Equal(1.60075m, effectiveInrNpr);
        Assert.Equal("NRB", src);
        Assert.Equal("NRB-20261120", ver);

        // Historical embedded fallback still returns ECB verified rate cleanly
        var ecbRate = fxProvider.GetHistoricalExchangeRate(
            "EUR", "USD",
            new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc),
            out src, out ver);
        Assert.Equal(1.1267m, ecbRate);
        Assert.Equal("ECB-20261104", ver);

        var uomSvc = new UomConversionService(db);
        var canonMass = await uomSvc.ConvertToCanonicalMetricAsync(10m, "LB");
        Assert.Equal("KG", canonMass.ToUomCode);
        Assert.Equal(4.5359m, canonMass.ConvertedQuantity);

        var canonLen = await uomSvc.ConvertToCanonicalMetricAsync(12m, "IN");
        Assert.Equal("CM", canonLen.ToUomCode);
        Assert.Equal(30.4800m, canonLen.ConvertedQuantity);

        await Assert.ThrowsAsync<DomainException>(() => uomSvc.ConvertAsync(1m, "M3", "KG"));
    }

    [Fact]
    public async Task HistoricalAsKnown_And_Retrospective_Preserve_Publication_Approval_And_Authority_Precedence_Across_Both_Interfaces()
    {
        var dbName = $"mdm13-bitemporal-{Guid.NewGuid():N}";
        using var db = Warehouse3PL.UnitTests.Common.TestDbContextFactory.Create(
            new Warehouse3PL.UnitTests.Common.TestTenantProvider { BypassTenantFilter = true },
            new Warehouse3PL.UnitTests.Common.TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        await ReferenceCatalogSeeder.SeedAsync(db);

        var approverId = Guid.NewGuid();
        var verifier = new OfficialFxBulletinVerifier();
        var nov19 = new DateTime(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);
        var nov20 = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        var nov20Early = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var nov23 = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
        var nov25 = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc);

        // Authority A (ManualApproved override, synthetic test value): effective Nov 19, published Nov 19 08:00Z, approved Nov 19 -> 148.50
        var authA = FxRateRecord.CreateDraft(
            "USD", "NPR", nov19, null, 1, 148.20m, 148.80m, 148.50m,
            FxRateSourceAuthority.ManualApproved, "MAN-NOV19", nov19,
            evidenceReference: "MANUAL_GOVERNANCE_OVERRIDE",
            transformationTrace: "MANUAL_GOVERNANCE_OVERRIDE: treasury override test fixture");
        authA.Approve(approverId, nov19, "Manual FX override verified on Nov 19.", evidenceReference: "MANUAL_GOVERNANCE_OVERRIDE");

        // Authority B (Authentic NRB-20261121 USD/NPR 147.98/148.58 -> 148.28): published Nov 20 10:09:20Z (> nov20Early), approved Nov 23
        var vNrb21 = verifier.VerifyOfficialQuotation(
            FxRateSourceAuthority.NRB, "NRB-20261121", "USD", "NPR", FxRateDirection.QuotePerUnitBase,
            1, 147.98m, 148.58m, 148.280000m, new DateTime(2026, 11, 20, 10, 9, 20, DateTimeKind.Utc));
        var authB = FxRateRecord.CreateDraft(
            "USD", "NPR", nov20, null, 1, 147.98m, 148.58m, 148.280000m,
            FxRateSourceAuthority.NRB, "NRB-20261121", vNrb21.PublishedAtUtc, vNrb21.BenchmarkSeriesId, vNrb21.VerifiedIntegrityHash,
            FxRateDirection.QuotePerUnitBase, null, vNrb21.EvidenceReference, vNrb21.TransformationTrace);
        authB.Approve(approverId, nov23, "Verified authentic NRB Nov 21 API payload.");

        // Authority C (Authentic NRB-20261120 USD/NPR 148.97/149.57 -> 149.27): published Nov 19 10:11:57Z (<= nov20Early), BUT approved Nov 25!
        var vNrb20 = verifier.VerifyOfficialQuotation(
            FxRateSourceAuthority.NRB, "NRB-20261120", "USD", "NPR", FxRateDirection.QuotePerUnitBase,
            1, 148.97m, 149.57m, 149.270000m, new DateTime(2026, 11, 19, 10, 11, 57, DateTimeKind.Utc));
        var authC = FxRateRecord.CreateDraft(
            "USD", "NPR", nov20, null, 1, 148.97m, 149.57m, 149.270000m,
            FxRateSourceAuthority.NRB, "NRB-20261120", vNrb20.PublishedAtUtc, vNrb20.BenchmarkSeriesId, vNrb20.VerifiedIntegrityHash,
            FxRateDirection.QuotePerUnitBase, null, vNrb20.EvidenceReference, vNrb20.TransformationTrace);
        authC.Approve(approverId, nov25, "Late internal verification of authentic NRB Nov 20 API payload.");

        // MDM13-R03-F02: Unverified legacy official row lacking EvidenceReference MUST NEVER be resolved by FxDatabaseRateResolver!
        var legacyUnverifiedNrb = FxRateRecord.CreateDraft(
            "EUR", "NPR", nov20, null, 1, 160.00m, 161.00m, 160.50m,
            FxRateSourceAuthority.NRB, "NRB-LEGACY-UNVERIFIED", nov19);
        legacyUnverifiedNrb.Approve(approverId, nov19, "Pre-R03 unverified official entry.");

        db.FxRateRecords.AddRange(authA, authB, authC, legacyUnverifiedNrb);
        await db.SaveChangesAsync();

        var fxProvider = FxRateProvider.ForDbContext(db);
        var convSvc = new FxConversionService(db, fxProvider, new DateTimeProvider());

        // 1) At AsOfUtc = Nov 20 09:00Z (HistoricalAsKnown):
        //    Auth B (Published Nov 20 10:09:20Z > 09:00Z) and Auth C (Approved Nov 25 > Nov 20) MUST BOTH be filtered out BEFORE selection,
        //    so BOTH interfaces select Authority A (148.50)!
        var asKnownConv = await convSvc.ConvertAmountAsync(
            10m, "USD", "NPR", nov20Early, FxRateType.Mid, nov20Early, valuationMode: FxValuationMode.HistoricalAsKnown);
        var asKnownProviderRate = fxProvider.GetHistoricalExchangeRate(
            "USD", "NPR", nov20Early, nov20Early, FxValuationMode.HistoricalAsKnown, null,
            out var pSrc, out var pVer, out _, out _, out _);

        Assert.Equal(1485.00m, asKnownConv.ConvertedAmount);
        Assert.Equal("ManualApproved", asKnownConv.SourceAuthority);
        Assert.Equal(148.50m, asKnownProviderRate);
        Assert.Equal(asKnownConv.SourceAuthority, pSrc);
        Assert.Equal(asKnownConv.BulletinVersion, pVer);

        // 2) At AsOfUtc = Nov 20 09:00Z (Retrospective):
        //    Auth C (NRB-20261120) was published Nov 19 10:11:57Z (<= nov20Early) and later verified -> Retrospective selects Auth C (149.27) cleanly!
        var retroConv = await convSvc.ConvertAmountAsync(
            10m, "USD", "NPR", nov20Early, FxRateType.Mid, nov20Early, valuationMode: FxValuationMode.Retrospective);
        var retroProviderRate = fxProvider.GetHistoricalExchangeRate(
            "USD", "NPR", nov20Early, nov20Early, FxValuationMode.Retrospective, null,
            out var rSrc, out var rVer, out _, out _, out _);

        Assert.Equal(1492.70m, retroConv.ConvertedAmount);
        Assert.Equal("NRB", retroConv.SourceAuthority);
        Assert.Equal("NRB-20261120", retroConv.BulletinVersion);
        Assert.Equal(149.27m, retroProviderRate);
        Assert.Equal(retroConv.SourceAuthority, rSrc);
        Assert.Equal(retroConv.BulletinVersion, rVer);

        // 3) At AsOfUtc = Nov 24 (after Nov 23, before Nov 25) HistoricalAsKnown sees NRB-20261121 (148.28), whereas explicit ManualApproved sees Auth A (148.50)
        var nov24 = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        var nrbWinsConv = await convSvc.ConvertAmountAsync(
            10m, "USD", "NPR", nov24, FxRateType.Mid, nov24, valuationMode: FxValuationMode.HistoricalAsKnown);
        Assert.Equal(1482.80m, nrbWinsConv.ConvertedAmount);
        Assert.Equal("NRB", nrbWinsConv.SourceAuthority);
        Assert.Equal("NRB-20261121", nrbWinsConv.BulletinVersion);

        var explicitManConv = await convSvc.ConvertAmountAsync(
            10m, "USD", "NPR", nov24, FxRateType.Mid, nov24, preferredAuthority: FxRateSourceAuthority.ManualApproved, valuationMode: FxValuationMode.HistoricalAsKnown);
        Assert.Equal(1485.00m, explicitManConv.ConvertedAmount);
        Assert.Equal("ManualApproved", explicitManConv.SourceAuthority);

        // 4) MDM13-R03-F02 Defense-in-Depth: EUR/NPR legacy row lacking EvidenceReference MUST NOT be resolved in either mode!
        await Assert.ThrowsAsync<DomainException>(() =>
            convSvc.ConvertAmountAsync(10m, "EUR", "NPR", nov24, FxRateType.Mid, nov24, valuationMode: FxValuationMode.HistoricalAsKnown));
        await Assert.ThrowsAsync<DomainException>(() =>
            convSvc.ConvertAmountAsync(10m, "EUR", "NPR", nov24, FxRateType.Mid, nov24, valuationMode: FxValuationMode.Retrospective));
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

    private static async Task<WebApplication> BuildJwtAppAsync(Func<ApplicationDbContext, Task> seedAsync)
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

        var dbName = $"mdm13-http-{Guid.NewGuid():N}";
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
