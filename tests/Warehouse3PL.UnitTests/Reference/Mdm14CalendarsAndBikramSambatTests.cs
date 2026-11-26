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
using Warehouse3PL.Application.Features.Reference.Queries;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Events.Transportation;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Persistence.Seed;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.Infrastructure.Services.Reference;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;

namespace Warehouse3PL.UnitTests.Reference;

public sealed class Mdm14CalendarsAndBikramSambatTests
{
    private const string TestSecret = "Nexus3PL-Mdm14-Calendars-BikramSambat-JwtSecret-2026!";
    private const string TestIssuer = "nexus3pl-mdm14-auth";
    private const string TestAudience = "nexus3pl-mdm14-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task BikramSambat_Months_ShrawanBoundary_TimezoneMidnight_And_VatDueDate_Are_Contiguous_And_Exact()
    {
        var facNpId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);
            // Idempotent re-seed check
            await ReferenceCatalogSeeder.SeedAsync(db);

            db.Facilities.Add(new Facility { Id = facNpId, FacilityCode = "FAC-KTM", Name = "Kathmandu Hub", CountryCode = "NP", Timezone = "Asia/Kathmandu", IsActive = true });
            var mgrRole = new Role { RoleName = "MgrM14", Description = "Mgr", IsSystemRole = false };
            mgrRole.RolePermissions.Add(new RolePermission { Role = mgrRole, PermissionKey = Permissions.Facilities.Manage, AccessLevel = AccessLevel.Admin });
            db.Roles.Add(mgrRole);
            var u = new User { Id = staffId, Email = "ktm@mdm14.test", FirstName = "K", LastName = "S", UserType = UserType.Staff, AssignedFacilityId = facNpId, Status = UserStatus.Active, PasswordHash = "x" };
            db.Users.Add(u);
            db.UserRoles.Add(new UserRole { User = u, Role = mgrRole });
            await db.SaveChangesAsync();
        });

        using var http = host.GetTestClient();
        var jwt = TokenFor(host, staffId, UserType.Staff, null, facNpId, ["MgrM14"], [Permissions.Facilities.Manage]);

        // 1. Seeded BS months cover 2075-2090 (192 contiguous months, zero gaps/overlaps); mutations return 405
        var monthsResp = await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/bikram-sambat/months", jwt));
        Assert.Equal(HttpStatusCode.OK, monthsResp.StatusCode);
        var months = (await monthsResp.Content.ReadFromJsonAsync<List<BikramSambatCalendarMonthDto>>(JsonOptions))!;
        Assert.Equal(16 * 12, months.Count);

        for (int i = 1; i < months.Count; i++)
        {
            Assert.Equal(months[i - 1].GregorianEndDate.AddDays(1), months[i].GregorianStartDate);
            Assert.Equal(months[i].DaysInMonth, months[i].GregorianEndDate.DayNumber - months[i].GregorianStartDate.DayNumber + 1);
            Assert.Contains("amitgaru/nepali-datetime", months[i].SourceCitation, StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/reference/bikram-sambat/months", jwt, new { }))).StatusCode);

        // 2. Ashadh End -> Shrawan 1 fiscal boundary & Asia/Kathmandu (+05:45) midnight rollover
        var beforeMidResp = await http.SendAsync(Authed(
            HttpMethod.Get,
            "/api/v1/reference/bikram-sambat/convert?utcInstant=2026-07-16T18:10:00Z&timeZoneId=Asia%2FKathmandu",
            jwt));
        Assert.Equal(HttpStatusCode.OK, beforeMidResp.StatusCode);
        var beforeMid = (await beforeMidResp.Content.ReadFromJsonAsync<BikramSambatConversionResponse>(JsonOptions))!;
        Assert.Equal("2083-03-32", beforeMid.ToBs!.FormattedBsDate);
        Assert.Equal("2082/83", beforeMid.ToBs.FiscalYearLabel);

        var afterMidResp = await http.SendAsync(Authed(
            HttpMethod.Get,
            "/api/v1/reference/bikram-sambat/convert?utcInstant=2026-07-16T18:20:00Z&timeZoneId=Asia%2FKathmandu",
            jwt));
        var afterMid = (await afterMidResp.Content.ReadFromJsonAsync<BikramSambatConversionResponse>(JsonOptions))!;
        Assert.Equal("2083-04-01", afterMid.ToBs!.FormattedBsDate);
        Assert.Equal("2083/84", afterMid.ToBs.FiscalYearLabel);

        // 2026-11-26 == 2083-08-10 (Mangsir 10, 2083) & Chaitra VAT return due date (Baishakh 25, 2084)
        var todayConv = await (await http.SendAsync(Authed(
            HttpMethod.Get,
            "/api/v1/reference/bikram-sambat/convert?gregorianDate=2026-11-26&vatForBsYear=2083&vatForBsMonth=12",
            jwt))).Content.ReadFromJsonAsync<BikramSambatConversionResponse>(JsonOptions);
        Assert.Equal("2083-08-10", todayConv!.ToBs!.FormattedBsDate);
        Assert.Equal(new DateOnly(2027, 5, 8), todayConv.VatReturnDueDate); // Baishakh 25, 2084 (Apr 14 + 24d = May 8, 2027)

        // Out-of-range or invalid BS day -> 422
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/bikram-sambat/convert?bsYear=2083&bsMonth=4&bsDay=33", jwt))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await http.SendAsync(Authed(HttpMethod.Get, "/api/v1/reference/bikram-sambat/convert?bsYear=2070&bsMonth=1&bsDay=1", jwt))).StatusCode);
    }

    [Fact]
    public async Task Holiday_And_FacilityCalendar_Rbac_Overlap_SlaWeekendDifferences_And_HistoricalPreservation_Work()
    {
        var facNpId = Guid.NewGuid();
        var facUsId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var clientUserId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var npMgrUserId = Guid.NewGuid();
        var usMgrUserId = Guid.NewGuid();
        var superAdminId = Guid.NewGuid();

        await using var host = await BuildJwtAppAsync(async db =>
        {
            await ReferenceCatalogSeeder.SeedAsync(db);

            db.Facilities.AddRange(
                new Facility { Id = facNpId, FacilityCode = "FAC-NP", Name = "Kathmandu Hub", CountryCode = "NP", StateOrProvince = "Bagmati Province", Timezone = "Asia/Kathmandu", IsActive = true },
                new Facility { Id = facUsId, FacilityCode = "FAC-US", Name = "Chicago Hub", CountryCode = "US", StateOrProvince = "IL", Timezone = "America/Chicago", IsActive = true });

            db.Clients.Add(new Client { Id = clientId, AccountCode = "CLI-14", CompanyName = "Client 14", Status = ClientStatus.Active });
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facNpId, Status = FacilityAccessStatus.Active });

            var facMgrRole = new Role { RoleName = "FacMgr14", Description = "FacMgr", IsSystemRole = false };
            facMgrRole.RolePermissions.Add(new RolePermission { Role = facMgrRole, PermissionKey = Permissions.Facilities.Manage, AccessLevel = AccessLevel.Admin });
            var superRole = new Role { RoleName = SystemRoles.SuperAdmin, Description = "Super", IsSystemRole = true };
            db.Roles.AddRange(facMgrRole, superRole);

            var uClient = new User { Id = clientUserId, Email = "c@m14.test", FirstName = "C", LastName = "U", UserType = UserType.Client, ClientId = clientId, Status = UserStatus.Active, PasswordHash = "x" };
            var uDriver = new User { Id = driverUserId, Email = "d@m14.test", FirstName = "D", LastName = "U", UserType = UserType.Driver, Status = UserStatus.Active, PasswordHash = "x" };
            var uNpMgr = new User { Id = npMgrUserId, Email = "np@m14.test", FirstName = "N", LastName = "M", UserType = UserType.Staff, AssignedFacilityId = facNpId, Status = UserStatus.Active, PasswordHash = "x" };
            var uUsMgr = new User { Id = usMgrUserId, Email = "us@m14.test", FirstName = "U", LastName = "M", UserType = UserType.Staff, AssignedFacilityId = facUsId, Status = UserStatus.Active, PasswordHash = "x" };
            var uSuper = new User { Id = superAdminId, Email = "sa@m14.test", FirstName = "S", LastName = "A", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };
            db.Users.AddRange(uClient, uDriver, uNpMgr, uUsMgr, uSuper);
            db.UserRoles.AddRange(
                new UserRole { User = uNpMgr, Role = facMgrRole },
                new UserRole { User = uUsMgr, Role = facMgrRole },
                new UserRole { User = uSuper, Role = superRole });
            await db.SaveChangesAsync();
        });

        using var http = host.GetTestClient();
        var jwtClient = TokenFor(host, clientUserId, UserType.Client, clientId, null, [], []);
        var jwtDriver = TokenFor(host, driverUserId, UserType.Driver, null, null, [], [Permissions.Facilities.Manage]);
        var jwtNpMgr = TokenFor(host, npMgrUserId, UserType.Staff, null, facNpId, ["FacMgr14"], [Permissions.Facilities.Manage]);
        var jwtUsMgr = TokenFor(host, usMgrUserId, UserType.Staff, null, facUsId, ["FacMgr14"], [Permissions.Facilities.Manage]);
        var jwtSuper = TokenFor(host, superAdminId, UserType.Staff, null, null, [SystemRoles.SuperAdmin], []);

        var effFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var npCalReq = new
        {
            effectiveFromUtc = effFrom,
            effectiveToUtc = (DateTime?)null,
            weeklyRestDaysCsv = "Saturday",
            workdayStartLocalTime = "09:00:00",
            workdayEndLocalTime = "18:00:00",
            defaultCutoffLocalTime = "17:00:00"
        };

        // 1. RBAC: anon -> 401; Client / Driver / foreign-facility Manager -> 403; assigned Facility Manager -> 201
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync($"/api/v1/calendars/facilities/{facNpId}", npCalReq)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtClient, npCalReq))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtDriver, npCalReq))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtUsMgr, npCalReq))).StatusCode);

        // Tenant isolation on reads: client can read authorized facNpId, denied 403 on unauthorized facUsId
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/calendars/facilities/{facNpId}", jwtClient))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/calendars/facilities/{facUsId}", jwtClient))).StatusCode);

        // Facility-level manager cannot create National holidays -> 403; SuperAdmin -> 201
        Assert.Equal(HttpStatusCode.Forbidden,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/calendars/holidays", jwtNpMgr, new
            {
                countryCode = "NP", holidayScope = HolidayScope.National, holidayDate = "2026-10-20", name = "National Dashain"
            }))).StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtNpMgr, npCalReq))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facUsId}", jwtUsMgr, new
            {
                effectiveFromUtc = effFrom,
                effectiveToUtc = (DateTime?)null,
                weeklyRestDaysCsv = "Saturday,Sunday",
                workdayStartLocalTime = "08:00:00",
                workdayEndLocalTime = "17:00:00",
                defaultCutoffLocalTime = "17:00:00"
            }))).StatusCode);

        // Overlapping FacilityCalendar without SupersedeOverlapping -> 409 Conflict
        Assert.Equal(HttpStatusCode.Conflict,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtNpMgr, npCalReq))).StatusCode);

        // Evaluate Sunday 2026-11-22: NP is working day, US is weekly rest day!
        var npSunEval = await (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/calendars/facilities/{facNpId}/working-day?localDate=2026-11-22", jwtNpMgr))).Content.ReadFromJsonAsync<WorkingDayEvaluationResult>(JsonOptions);
        var usSunEval = await (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/calendars/facilities/{facUsId}/working-day?localDate=2026-11-22", jwtUsMgr))).Content.ReadFromJsonAsync<WorkingDayEvaluationResult>(JsonOptions);
        Assert.True(npSunEval!.IsWorkingDay);
        Assert.False(usSunEval!.IsWorkingDay);

        // National holiday (Fri Nov 20, 2026 in NP) overridden by Facility IsWorkingDayOverride=true -> NP Nov 20 IS working!
        Assert.Equal(HttpStatusCode.Created,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/calendars/holidays", jwtSuper, new
            {
                countryCode = "NP", holidayScope = HolidayScope.National, holidayDate = "2026-11-20", name = "National Bank Holiday"
            }))).StatusCode);
        Assert.False((await (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/calendars/facilities/{facNpId}/working-day?localDate=2026-11-20", jwtNpMgr))).Content.ReadFromJsonAsync<WorkingDayEvaluationResult>(JsonOptions))!.IsWorkingDay);

        Assert.Equal(HttpStatusCode.Created,
            (await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/calendars/holidays", jwtNpMgr, new
            {
                countryCode = "NP", holidayScope = HolidayScope.Facility, facilityId = facNpId,
                holidayDate = "2026-11-20", name = "Peak Season Hub Shift Open", isWorkingDayOverride = true
            }))).StatusCode);
        var overrideEval = await (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/calendars/facilities/{facNpId}/working-day?localDate=2026-11-20", jwtNpMgr))).Content.ReadFromJsonAsync<WorkingDayEvaluationResult>(JsonOptions);
        Assert.True(overrideEval!.IsWorkingDay);
        Assert.True(overrideEval.IsWorkingDayOverride);

        // 2. SLA Transit Calculation & Historical Preservation:
        //    NP Hub: Thu 2026-11-19 12:00 NST (06:15 UTC) + 2 business days -> skips Sat Nov 21, counts Fri Nov 20 (#1, facility override) + Sun Nov 22 (#2) => Sun 2026-11-22 23:59:59 NST (18:14:59 UTC)!
        //    US Hub: Thu 2026-11-19 12:00 CST (18:00 UTC) + 2 business days -> counts Fri Nov 20 (#1), skips Sat+Sun, counts Mon Nov 23 (#2) => Mon 2026-11-23 23:59:59 CST (Tue 05:59:59 UTC)!
        using var scope = host.Services.CreateScope();
        var dbOpts = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        var workerUser = new TestCurrentUserContext { UserType = UserType.Staff };
        await using var db = new ApplicationDbContext(
            dbOpts,
            new TestTenantProvider { BypassTenantFilter = true },
            workerUser,
            new AuditableEntityInterceptor(workerUser, new DateTimeProvider()));
        var projSvc = new TransportationProjectionService(
            db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TransportationProjectionService>.Instance,
            new WorkingCalendarService(db));

        var npPolicy = CarrierContractualSlaPolicy.Create(
            "SIM-NP", CarrierServiceLevel.StandardGround, 2, "1.0.0",
            TransitDayCalculationType.BusinessDays, clientId, facNpId, "NP", "NP",
            dailyCutoffTime: new TimeSpan(17, 0, 0), cutoffTimeZone: "Asia/Kathmandu",
            effectiveFromUtc: effFrom);
        var usPolicy = CarrierContractualSlaPolicy.Create(
            "SIM-US", CarrierServiceLevel.StandardGround, 2, "1.0.0",
            TransitDayCalculationType.BusinessDays, clientId, facUsId, "US", "US",
            dailyCutoffTime: new TimeSpan(17, 0, 0), cutoffTimeZone: "America/Chicago",
            effectiveFromUtc: effFrom);
        db.CarrierContractualSlaPolicies.AddRange(npPolicy, usPolicy);
        await db.SaveChangesAsync();

        var shipNpId = Guid.NewGuid();
        var shipUsId = Guid.NewGuid();
        await projSvc.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = facNpId,
            ClientId = clientId,
            ShipmentId = shipNpId,
            TrackingNumber = "TRK-NP-1",
            CarrierCode = "SIM-NP",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            DispatchedAtUtc = new DateTime(2026, 11, 19, 6, 15, 0, DateTimeKind.Utc), // 12:00 NST
            SequenceNumber = 1
        });
        await projSvc.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
        {
            EventId = Guid.NewGuid(),
            FacilityId = facUsId,
            ClientId = clientId,
            ShipmentId = shipUsId,
            TrackingNumber = "TRK-US-1",
            CarrierCode = "SIM-US",
            CarrierServiceLevel = CarrierServiceLevel.StandardGround,
            DispatchedAtUtc = new DateTime(2026, 11, 19, 18, 0, 0, DateTimeKind.Utc), // 12:00 CST
            SequenceNumber = 2
        });

        var npRec = await db.CarrierSlaComplianceRecords.SingleAsync(r => r.ShipmentId == shipNpId);
        var usRec = await db.CarrierSlaComplianceRecords.SingleAsync(r => r.ShipmentId == shipUsId);
        var originalNpEdd = npRec.EstimatedDeliveryDateUtc;

        Assert.Equal(new DateTime(2026, 11, 22, 18, 14, 59, DateTimeKind.Utc), originalNpEdd); // 2026-11-22 23:59:59 +05:45
        Assert.Equal(new DateTime(2026, 11, 24, 5, 59, 59, DateTimeKind.Utc), usRec.EstimatedDeliveryDateUtc); // 2026-11-23 23:59:59 -06:00

        // Superseding FacilityCalendar later MUST NOT rewrite previously recorded CarrierSlaComplianceRecord!
        Assert.Equal(HttpStatusCode.Created,
            (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtNpMgr, new
            {
                effectiveFromUtc = effFrom,
                effectiveToUtc = (DateTime?)null,
                weeklyRestDaysCsv = "Saturday,Sunday",
                workdayStartLocalTime = "09:00:00",
                workdayEndLocalTime = "18:00:00",
                defaultCutoffLocalTime = "17:00:00",
                supersedeOverlapping = true
            }))).StatusCode);

        var npRecAfter = await db.CarrierSlaComplianceRecords.AsNoTracking().SingleAsync(r => r.ShipmentId == shipNpId);
        Assert.Equal(originalNpEdd, npRecAfter.EstimatedDeliveryDateUtc);
    }

    private static string TokenFor(WebApplication host, Guid id, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
    {
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateTokens(new User
        {
            Id = id, Email = $"{id:N}@m14.test", FirstName = "T", LastName = "U", UserType = ut, ClientId = cid, AssignedFacilityId = fid
        }, roles, perms).AccessToken;
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
            .AddApplicationPart(typeof(CalendarsController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddScoped<IBikramSambatCalendarService, BikramSambatCalendarService>();
        builder.Services.AddScoped<IWorkingCalendarService, WorkingCalendarService>();
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

        var dbName = $"mdm14-http-{Guid.NewGuid():N}";
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
