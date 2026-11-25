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
        var pubAt = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
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
            benchmarkSeriesId = "NRB-FX-INR-NPR"
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/v1/fx-rates", draftBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtClient, draftBody))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtDriver, draftBody))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtPlainSpoof, draftBody))).StatusCode);

        // 3. Draft FX creation starts inactive; conversion fails closed 422 before approval!
        var createNrb = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, draftBody));
        Assert.Equal(HttpStatusCode.Created, createNrb.StatusCode);
        var nrbDraft = (await createNrb.Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
        Assert.Equal(FxRateApprovalStatus.Draft, nrbDraft.ApprovalStatus);
        Assert.False(nrbDraft.IsActive);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
            {
                amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
                effectiveDateUtc = effStart.AddHours(6), rateType = FxRateType.Mid
            }))).StatusCode);

        // Approve without evidence -> 422; approve with evidence -> 200
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft.Id}/approve", jwtFin, new
            {
                evidenceVerified = false, verificationNotes = "Missing attestation"
            }))).StatusCode);

        var approveNrb = await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft.Id}/approve", jwtFin, new
        {
            evidenceVerified = true, verificationNotes = "Verified against NRB daily fixing circular."
        }));
        Assert.Equal(HttpStatusCode.OK, approveNrb.StatusCode);
        var nrbApproved = (await approveNrb.Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;
        Assert.Equal(FxRateApprovalStatus.VerifiedApproved, nrbApproved.ApprovalStatus);
        Assert.True(nrbApproved.IsActive);
        Assert.Equal(1.60075m, nrbApproved.EffectiveMidUnitRate);

        // 4. UnitMultiplier=100 conversion (1000 INR -> 1600.75 Mid / 1600.00 Buy / 1601.50 Sell NPR)
        var convMidResp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
            effectiveDateUtc = effStart.AddHours(6), rateType = FxRateType.Mid,
            asOfUtc = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)
        }));
        Assert.Equal(HttpStatusCode.OK, convMidResp.StatusCode);
        var convMid = (await convMidResp.Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions))!;
        Assert.Equal(1600.75m, convMid.ConvertedAmount);
        Assert.Equal(100, convMid.UnitMultiplier);
        Assert.True(convMid.ResolvedFromDatabase);

        var convBuy = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
            effectiveDateUtc = effStart.AddHours(6), rateType = FxRateType.Buy,
            asOfUtc = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)
        }))).Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions);
        Assert.Equal(1600.00m, convBuy!.ConvertedAmount);

        var convSell = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 1000m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
            effectiveDateUtc = effStart.AddHours(6), rateType = FxRateType.Sell,
            asOfUtc = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)
        }))).Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions);
        Assert.Equal(1601.50m, convSell!.ConvertedAmount);

        // Valuation BEFORE PublishedAtUtc fails closed 422 (no look-ahead!)
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
            {
                amount = 100m, fromCurrencyCode = "INR", toCurrencyCode = "NPR",
                effectiveDateUtc = effStart.AddHours(6),
                asOfUtc = pubAt.AddMinutes(-1)
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

        // 6. Retroactive PUT / DELETE prohibited -> 409 Conflict; overlapping POST without supersede -> 409; supersede -> 200; void -> 200 + fails conversion 422
        Assert.Equal(HttpStatusCode.Conflict, (await http.SendAsync(Authed(HttpMethod.Put, $"/api/v1/fx-rates/{nrbApproved.Id}", jwtFin, new { }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.SendAsync(Authed(HttpMethod.Delete, $"/api/v1/fx-rates/{nrbApproved.Id}", jwtFin))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Delete, $"/api/v1/fx-rates/{nrbApproved.Id}", jwtPlainSpoof))).StatusCode);

        var draft2Resp = await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
        {
            baseCurrencyCode = "INR", quoteCurrencyCode = "NPR",
            effectiveFromUtc = effStart.AddDays(1), effectiveToUtc = (DateTime?)null,
            unitMultiplier = 100, buyRate = 160.10m, sellRate = 160.25m, midRate = 160.175m,
            sourceAuthority = FxRateSourceAuthority.NRB, bulletinVersion = "NRB-20261121",
            publishedAtUtc = effStart.AddDays(1)
        }));
        var nrbDraft2 = (await draft2Resp.Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions))!;

        Assert.Equal(HttpStatusCode.Conflict,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft2.Id}/approve", jwtFin, new
            {
                evidenceVerified = true, verificationNotes = "Attempt overlap without supersede", supersedeOverlapping = false
            }))).StatusCode);

        var supResp = await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{nrbDraft2.Id}/approve", jwtFin, new
        {
            evidenceVerified = true, verificationNotes = "Official NRB revision", supersedeOverlapping = true, expectedActiveRateId = nrbApproved.Id
        }));
        Assert.Equal(HttpStatusCode.OK, supResp.StatusCode);

        var prevAfterSup = await (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/fx-rates/{nrbApproved.Id}", jwtClient))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions);
        Assert.Equal(FxRateApprovalStatus.Superseded, prevAfterSup!.ApprovalStatus);
        Assert.False(prevAfterSup.IsActive);

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

        // 7. Currency minor-unit rounding precision (JPY = 0 decimals, KWD = 3 decimals AwayFromZero)
        var jpyDraft = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates", jwtFin, new
        {
            baseCurrencyCode = "USD", quoteCurrencyCode = "JPY",
            effectiveFromUtc = effStart, effectiveToUtc = (DateTime?)null,
            unitMultiplier = 1, buyRate = 152.10m, sellRate = 152.90m, midRate = 152.50m,
            sourceAuthority = FxRateSourceAuthority.ManualApproved, bulletinVersion = "MAN-JPY-1", publishedAtUtc = pubAt
        }))).Content.ReadFromJsonAsync<FxRateRecordDto>(JsonOptions);
        await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/fx-rates/{jpyDraft!.Id}/approve", jwtFin, new { evidenceVerified = true, verificationNotes = "OK" }));

        var jpyConv = await (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/fx-rates/convert", jwtClient, new
        {
            amount = 1.00m, fromCurrencyCode = "USD", toCurrencyCode = "JPY",
            effectiveDateUtc = effStart.AddHours(1), asOfUtc = effStart.AddHours(1)
        }))).Content.ReadFromJsonAsync<FxConversionResultDto>(JsonOptions);
        Assert.Equal(153m, jpyConv!.ConvertedAmount); // 152.50 rounded to 0 decimals AwayFromZero = 153!
        Assert.Equal(0, jpyConv.TargetMinorUnits);

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
        var nrbRate = FxRateRecord.CreateDraft(
            "INR", "NPR",
            new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc),
            null,
            100,
            160.00m, 160.15m, 160.075m,
            FxRateSourceAuthority.NRB,
            "NRB-20261125",
            new DateTime(2026, 11, 25, 4, 0, 0, DateTimeKind.Utc),
            "NRB-INR-NPR");
        nrbRate.Approve(approverId, new DateTime(2026, 11, 25, 4, 5, 0, DateTimeKind.Utc), "Verified NRB circular.");
        db.FxRateRecords.Add(nrbRate);
        await db.SaveChangesAsync();

        var fxProvider = FxRateProvider.ForDbContext(db);
        var effectiveInrNpr = fxProvider.GetHistoricalExchangeRate(
            "INR", "NPR",
            new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc),
            out var src, out var ver, out _, out _, out _);
        Assert.Equal(1.60075m, effectiveInrNpr);
        Assert.Equal("NRB", src);
        Assert.Equal("NRB-20261125", ver);

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
