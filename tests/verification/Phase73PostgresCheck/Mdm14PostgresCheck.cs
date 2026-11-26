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
using Warehouse3PL.Application.Features.Reference.Queries;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Events.Transportation;
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

internal static class Mdm14PostgresCheck
{
    private const string JwtSecret = "Nexus3PL-Mdm14-PostgreSql-Verification-Key-2026!";
    private const string JwtIssuer = "nexus3pl-mdm14-pg-auth";
    private const string JwtAudience = "nexus3pl-mdm14-pg-api";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static async Task RunAsync(string baseConnectionString)
    {
        Console.WriteLine("\n[MDM-1.4] Running Live PostgreSQL BikramSambat, FacilityCalendar Exclusion/Race & SLA Timezone Verification...");

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" };
        var dbName = $"mdm14_pg_{Guid.NewGuid():N}";

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
            // 1. Clean upgrade MDM1_3 -> MDM1_4 + verify excl_facility_calendars_active_window + 192 contiguous BS months
            await using (var migDb = CreatePgDb(targetConn, null))
            {
                var migrator = migDb.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync("20261126061816_MDM1_3_R03_F02_QuarantineLegacyFxRows");
                await migrator.MigrateAsync();
            }

            await using (var sqlConn = new NpgsqlConnection(targetConn))
            {
                await sqlConn.OpenAsync();
                await using var exclCmd = new NpgsqlCommand(
                    "SELECT pg_get_constraintdef(c.oid) FROM pg_constraint c JOIN pg_class t ON c.conrelid = t.oid WHERE t.relname = 'facility_calendars' AND c.conname = 'excl_facility_calendars_active_window';",
                    sqlConn);
                var def = (await exclCmd.ExecuteScalarAsync())?.ToString()
                    ?? throw new InvalidOperationException("MDM-1.4 PostgreSQL verification failed: excl_facility_calendars_active_window missing.");
                if (!def.Contains("EXCLUDE USING gist", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unexpected FacilityCalendar exclusion constraint: {def}");
                }
            }

            await using (var seedDb = CreatePgDb(targetConn, null))
            {
                await ReferenceCatalogSeeder.SeedAsync(seedDb);
                await ReferenceCatalogSeeder.SeedAsync(seedDb);

                var months = await seedDb.BikramSambatCalendarMonths
                    .OrderBy(m => m.BsYear)
                    .ThenBy(m => m.BsMonth)
                    .ToListAsync();
                if (months.Count != 192)
                {
                    throw new InvalidOperationException($"MDM-1.4 expected 192 BS months (2075-2090), found {months.Count}.");
                }

                for (int i = 1; i < months.Count; i++)
                {
                    if (months[i - 1].GregorianEndDate.AddDays(1) != months[i].GregorianStartDate)
                    {
                        throw new InvalidOperationException(
                            $"MDM-1.4 BS calendar gap/overlap between {months[i - 1].BsYear}-{months[i - 1].BsMonth} and {months[i].BsYear}-{months[i].BsMonth}.");
                    }
                }
            }

            // 2. Setup NP + US facilities, Staff & Client users
            var facNpId = Guid.NewGuid();
            var facUsId = Guid.NewGuid();
            var clientId = Guid.NewGuid();
            var clientUserId = Guid.NewGuid();
            var npStaffId = Guid.NewGuid();
            var usStaffId = Guid.NewGuid();
            var superAdminId = Guid.NewGuid();

            await using (var setupDb = CreatePgDb(targetConn, null))
            {
                setupDb.Facilities.AddRange(
                    new Facility { Id = facNpId, FacilityCode = "PG-NP", Name = "PG Kathmandu", CountryCode = "NP", StateOrProvince = "Bagmati Province", Timezone = "Asia/Kathmandu", IsActive = true },
                    new Facility { Id = facUsId, FacilityCode = "PG-US", Name = "PG Chicago", CountryCode = "US", StateOrProvince = "IL", Timezone = "America/Chicago", IsActive = true });
                setupDb.Clients.Add(new Client { Id = clientId, AccountCode = "PG-M14", CompanyName = "PG M14 Client", Status = ClientStatus.Active });
                setupDb.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientId, FacilityId = facNpId, Status = FacilityAccessStatus.Active });

                var mgrRole = new Role { RoleName = "PgFacMgr14", Description = "Mgr", IsSystemRole = false };
                mgrRole.RolePermissions.Add(new RolePermission { Role = mgrRole, PermissionKey = Permissions.Facilities.Manage, AccessLevel = AccessLevel.Admin });
                var superRole = new Role { RoleName = SystemRoles.SuperAdmin, Description = "Super", IsSystemRole = true };
                setupDb.Roles.AddRange(mgrRole, superRole);

                var uCli = new User { Id = clientUserId, Email = "pgc@m14.test", FirstName = "C", LastName = "U", UserType = UserType.Client, ClientId = clientId, Status = UserStatus.Active, PasswordHash = "x" };
                var uNp = new User { Id = npStaffId, Email = "pgnp@m14.test", FirstName = "N", LastName = "M", UserType = UserType.Staff, AssignedFacilityId = facNpId, Status = UserStatus.Active, PasswordHash = "x" };
                var uUs = new User { Id = usStaffId, Email = "pgus@m14.test", FirstName = "U", LastName = "M", UserType = UserType.Staff, AssignedFacilityId = facUsId, Status = UserStatus.Active, PasswordHash = "x" };
                var uSa = new User { Id = superAdminId, Email = "pgsa@m14.test", FirstName = "S", LastName = "A", UserType = UserType.Staff, Status = UserStatus.Active, PasswordHash = "x" };
                setupDb.Users.AddRange(uCli, uNp, uUs, uSa);
                setupDb.UserRoles.AddRange(
                    new UserRole { User = uNp, Role = mgrRole },
                    new UserRole { User = uUs, Role = mgrRole },
                    new UserRole { User = uSa, Role = superRole });
                await setupDb.SaveChangesAsync();
            }

            // 3. Verify raw PostgreSQL EXCLUDE USING gist (23P01) on facility_calendars
            await using (var rawConn = new NpgsqlConnection(targetConn))
            {
                await rawConn.OpenAsync();
                await using (var insA = new NpgsqlCommand(
                    $"INSERT INTO facility_calendars (facility_calendar_id, facility_id, effective_from_utc, weekly_rest_days_csv, workday_start_local_time, workday_end_local_time, default_cutoff_local_time, config_revision, is_active, created_at) " +
                    $"VALUES ('{Guid.NewGuid()}', '{facUsId}', '2026-01-01T00:00:00Z', 'Saturday,Sunday', '08:00:00', '17:00:00', '17:00:00', 1, true, NOW());",
                    rawConn))
                {
                    await insA.ExecuteNonQueryAsync();
                }

                bool exclBlocked = false;
                try
                {
                    await using var insB = new NpgsqlCommand(
                        $"INSERT INTO facility_calendars (facility_calendar_id, facility_id, effective_from_utc, weekly_rest_days_csv, workday_start_local_time, workday_end_local_time, default_cutoff_local_time, config_revision, is_active, created_at) " +
                        $"VALUES ('{Guid.NewGuid()}', '{facUsId}', '2026-06-01T00:00:00Z', 'Saturday,Sunday', '09:00:00', '18:00:00', '17:00:00', 1, true, NOW());",
                        rawConn);
                    await insB.ExecuteNonQueryAsync();
                }
                catch (PostgresException pgEx) when (pgEx.SqlState == "23P01")
                {
                    exclBlocked = true;
                }
                if (!exclBlocked)
                    throw new InvalidOperationException("MDM-1.4 PostgreSQL EXCLUDE USING gist did not block overlapping active FacilityCalendar.");
            }

            // 4. Real JWT HTTP checks: concurrent overlap race [201, 409], BS Shrawan boundary, Kathmandu timezone, NP vs US SLA, immutable history
            await using var host = await BuildPgHttpAppAsync(targetConn);
            using var http = host.GetTestClient();

            string TokenFor(Guid uid, UserType ut, Guid? cid, Guid? fid, string[] roles, string[] perms)
            {
                using var scope = host.Services.CreateScope();
                return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateTokens(new User
                {
                    Id = uid, Email = $"{uid:N}@m14.test", FirstName = "T", LastName = "U", UserType = ut, ClientId = cid, AssignedFacilityId = fid
                }, roles, perms).AccessToken;
            }

            var jwtClient = TokenFor(clientUserId, UserType.Client, clientId, null, [], []);
            var jwtNp = TokenFor(npStaffId, UserType.Staff, null, facNpId, ["PgFacMgr14"], [Permissions.Facilities.Manage]);
            var jwtUs = TokenFor(usStaffId, UserType.Staff, null, facUsId, ["PgFacMgr14"], [Permissions.Facilities.Manage]);
            var jwtSuper = TokenFor(superAdminId, UserType.Staff, null, null, [SystemRoles.SuperAdmin], []);

            var npBody = new
            {
                effectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                effectiveToUtc = (DateTime?)null,
                weeklyRestDaysCsv = "Saturday",
                workdayStartLocalTime = "09:00:00",
                workdayEndLocalTime = "18:00:00",
                defaultCutoffLocalTime = "17:00:00"
            };

            if ((await http.PostAsJsonAsync($"/api/v1/calendars/facilities/{facNpId}", npBody)).StatusCode != HttpStatusCode.Unauthorized ||
                (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtClient, npBody))).StatusCode != HttpStatusCode.Forbidden ||
                (await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtUs, npBody))).StatusCode != HttpStatusCode.Forbidden ||
                (await http.SendAsync(Authed(HttpMethod.Get, $"/api/v1/calendars/facilities/{facUsId}", jwtClient))).StatusCode != HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException("MDM-1.4 PostgreSQL calendar RBAC / tenant isolation check failed.");
            }

            // Concurrent FacilityCalendar race on facNpId -> strictly [201, 409]
            var raceResults = await Task.WhenAll(
                http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtNp, npBody)),
                http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtNp, npBody)));
            if (raceResults.Count(r => r.StatusCode == HttpStatusCode.Created) != 1 ||
                raceResults.Count(r => r.StatusCode == HttpStatusCode.Conflict) != 1)
            {
                throw new InvalidOperationException($"MDM-1.4 PG concurrent FacilityCalendar race expected [201, 409], got [{string.Join(", ", raceResults.Select(r => r.StatusCode))}]");
            }

            // BS Shrawan boundary in Kathmandu timezone
            var bsBoundary = await (await http.SendAsync(Authed(
                HttpMethod.Get,
                "/api/v1/reference/bikram-sambat/convert?utcInstant=2026-07-16T18:20:00Z&timeZoneId=Asia%2FKathmandu&vatForBsYear=2083&vatForBsMonth=12",
                jwtClient))).Content.ReadFromJsonAsync<BikramSambatConversionResponse>(JsonOptions);
            if (bsBoundary!.ToBs!.FormattedBsDate != "2083-04-01" ||
                bsBoundary.ToBs.FiscalYearLabel != "2083/84" ||
                bsBoundary.VatReturnDueDate != new DateOnly(2027, 5, 8))
            {
                throw new InvalidOperationException($"MDM-1.4 PG BS Shrawan boundary check failed: {bsBoundary.ToBs.FormattedBsDate} / {bsBoundary.ToBs.FiscalYearLabel}");
            }

            // SLA NP (Sat-only) vs US (Sat+Sun) + historical record immutability on PostgreSQL
            await using (var slaDb = CreatePgDb(targetConn, null))
            {
                var wc = new WorkingCalendarService(slaDb);
                var proj = new TransportationProjectionService(slaDb, Microsoft.Extensions.Logging.Abstractions.NullLogger<TransportationProjectionService>.Instance, wc);

                var eff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var npPol = CarrierContractualSlaPolicy.Create("PGNP", CarrierServiceLevel.StandardGround, 2, "1.0.0", TransitDayCalculationType.BusinessDays, clientId, facNpId, "NP", "NP", dailyCutoffTime: new TimeSpan(17, 0, 0), cutoffTimeZone: "Asia/Kathmandu", effectiveFromUtc: eff);
                var usPol = CarrierContractualSlaPolicy.Create("PGUS", CarrierServiceLevel.StandardGround, 2, "1.0.0", TransitDayCalculationType.BusinessDays, clientId, facUsId, "US", "US", dailyCutoffTime: new TimeSpan(17, 0, 0), cutoffTimeZone: "America/Chicago", effectiveFromUtc: eff);
                slaDb.CarrierContractualSlaPolicies.AddRange(npPol, usPol);
                await slaDb.SaveChangesAsync();

                var npShip = Guid.NewGuid();
                var usShip = Guid.NewGuid();
                await proj.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
                {
                    EventId = Guid.NewGuid(), FacilityId = facNpId, ClientId = clientId, ShipmentId = npShip,
                    TrackingNumber = "PG-TRK-NP", CarrierCode = "PGNP", CarrierServiceLevel = CarrierServiceLevel.StandardGround,
                    DispatchedAtUtc = new DateTime(2026, 11, 19, 6, 15, 0, DateTimeKind.Utc), SequenceNumber = 1
                });
                await proj.ProcessShipmentDispatchedAsync(new ShipmentDispatchedEvent
                {
                    EventId = Guid.NewGuid(), FacilityId = facUsId, ClientId = clientId, ShipmentId = usShip,
                    TrackingNumber = "PG-TRK-US", CarrierCode = "PGUS", CarrierServiceLevel = CarrierServiceLevel.StandardGround,
                    DispatchedAtUtc = new DateTime(2026, 11, 19, 18, 0, 0, DateTimeKind.Utc), SequenceNumber = 2
                });

                var npSla = await slaDb.CarrierSlaComplianceRecords.SingleAsync(r => r.ShipmentId == npShip);
                var usSla = await slaDb.CarrierSlaComplianceRecords.SingleAsync(r => r.ShipmentId == usShip);
                if (npSla.EstimatedDeliveryDateUtc != new DateTime(2026, 11, 22, 18, 14, 59, DateTimeKind.Utc) ||
                    usSla.EstimatedDeliveryDateUtc != new DateTime(2026, 11, 24, 5, 59, 59, DateTimeKind.Utc))
                {
                    throw new InvalidOperationException($"MDM-1.4 PG SLA EDD mismatch: NP={npSla.EstimatedDeliveryDateUtc:O}, US={usSla.EstimatedDeliveryDateUtc:O}");
                }

                // Supersede NP calendar to Sat+Sun -> previous NP CarrierSlaComplianceRecord remains untouched!
                if ((await http.SendAsync(Authed(HttpMethod.Post, $"/api/v1/calendars/facilities/{facNpId}", jwtNp, new
                    {
                        effectiveFromUtc = eff, effectiveToUtc = (DateTime?)null, weeklyRestDaysCsv = "Saturday,Sunday",
                        workdayStartLocalTime = "09:00:00", workdayEndLocalTime = "18:00:00", defaultCutoffLocalTime = "17:00:00",
                        supersedeOverlapping = true
                    }))).StatusCode != HttpStatusCode.Created)
                {
                    throw new InvalidOperationException("MDM-1.4 PG FacilityCalendar supersession failed.");
                }

                var preserved = await slaDb.CarrierSlaComplianceRecords.AsNoTracking().SingleAsync(r => r.ShipmentId == npShip);
                if (preserved.EstimatedDeliveryDateUtc != new DateTime(2026, 11, 22, 18, 14, 59, DateTimeKind.Utc))
                {
                    throw new InvalidOperationException("MDM-1.4 PG historical SLA record mutated after calendar supersession!");
                }
            }

            // Confirm unused jwtSuper can create/update regional holiday cleanly
            if ((await http.SendAsync(Authed(HttpMethod.Post, "/api/v1/calendars/holidays", jwtSuper, new
                {
                    countryCode = "US", holidayScope = HolidayScope.Regional, subdivisionCode = "US-IL",
                    holidayDate = "2026-12-26", name = "IL Regional Observance"
                }))).StatusCode != HttpStatusCode.Created)
            {
                throw new InvalidOperationException("MDM-1.4 PG Regional holiday creation failed.");
            }

            Console.WriteLine("PASS PostgreSQL MDM-1.4 Migration, Contiguous BS 2075-2090, FacilityCalendar Exclusion/Race, Holidays & NP/US SLA Verified.");
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
        public string? Email => "pgfin@m14.test";
        public Guid? TenantId => null;
        public Guid? FacilityId => null;
        public Guid? RequestedFacilityId => null;
        public UserType? UserType => Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [Warehouse3PL.Domain.Constants.Permissions.Facilities.Manage];
        public bool IsAuthenticated => true;
        public bool IsStaff => true;
        public bool IsSuperAdmin => false;
        public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
        public bool IsInRole(string role) => false;
    }
}
