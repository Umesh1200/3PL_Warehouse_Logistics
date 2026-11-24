using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Application.Features.Facilities.Queries.GetFacilityConfiguration;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Shipping;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public sealed class Mdm01FacilityOriginAndDtoTests
{
    private const string TestSecret = "Nexus3PL-Mdm01-Hardening-SigningKey-2026!";
    private const string TestIssuer = "nexus3pl-mdm01-auth";
    private const string TestAudience = "nexus3pl-mdm01-api";

    #region ShippingLabelService Facility-Origin & Destination Validation Tests

    [Fact]
    public async Task GetRatesAndGenerateLabel_WithLegacyUsaFacility_NormalizesToUsAtBoundaryWithoutMutatingDatabase()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var usFacilityId = Guid.NewGuid();
        var pkgId = await SeedLabelPackageAsync(
            dbName,
            clientId,
            new Facility
            {
                Id = usFacilityId,
                FacilityCode = "FAC-ORD-01",
                Name = "Chicago Central Hub",
                AddressLine1 = "1000 Logistics Parkway",
                AddressLine2 = "Suite 200",
                City = "Chicago",
                StateOrProvince = "IL",
                PostalCode = "60601",
                CountryCode = "USA", // legacy representation preserved in DB
                Timezone = "America/Chicago",
                ContactPhone = "+1-312-555-0100",
                IsActive = true
            },
            new ShippingAddress
            {
                RecipientName = "Alice Buyer",
                CompanyName = "Midwest Retail",
                AddressLine1 = "500 Pine St",
                City = "Milwaukee",
                StateOrProvince = "WI",
                PostalCode = "53202",
                CountryCode = "U.S.A.",
                PhoneNumber = "+1-414-555-0199"
            });

        var spyCarrier = new SpyShippingCarrierService();

        using (var context = TestDbContextFactory.Create(
                   new TestTenantProvider { BypassTenantFilter = true },
                   new TestCurrentUserContext { UserType = UserType.Staff },
                   dbName))
        {
            var service = new ShippingLabelService(context, spyCarrier, NullLogger<ShippingLabelService>.Instance);

            var quotes = await service.GetRatesForPackageAsync(new GetRatesForPackageRequest { PackageId = pkgId });
            Assert.NotEmpty(quotes);
            Assert.NotNull(spyCarrier.LastRateRequest);
            Assert.Equal("Chicago Central Hub", spyCarrier.LastRateRequest!.OriginAddress.Name);
            Assert.Equal("1000 Logistics Parkway", spyCarrier.LastRateRequest.OriginAddress.StreetLine1);
            Assert.Equal("Suite 200", spyCarrier.LastRateRequest.OriginAddress.StreetLine2);
            Assert.Equal("Chicago", spyCarrier.LastRateRequest.OriginAddress.City);
            Assert.Equal("IL", spyCarrier.LastRateRequest.OriginAddress.StateOrProvince);
            Assert.Equal("60601", spyCarrier.LastRateRequest.OriginAddress.PostalCode);
            Assert.Equal("US", spyCarrier.LastRateRequest.OriginAddress.CountryCode);
            Assert.Equal("+1-312-555-0100", spyCarrier.LastRateRequest.OriginAddress.Phone);
            Assert.Equal("US", spyCarrier.LastRateRequest.DestinationAddress.CountryCode);

            var labelRes = await service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = pkgId,
                CarrierCode = "SIM-FEDEX",
                ServiceLevel = CarrierServiceLevel.StandardGround,
                IdempotencyKey = "mdm01-us-origin-1",
                UserId = Guid.NewGuid()
            });

            Assert.True(labelRes.IsSuccess);
            Assert.NotNull(spyCarrier.LastShipmentRequest);
            Assert.Equal("1000 Logistics Parkway", spyCarrier.LastShipmentRequest!.OriginAddress.StreetLine1);
            Assert.Equal("Chicago", spyCarrier.LastShipmentRequest.OriginAddress.City);
            Assert.Equal("US", spyCarrier.LastShipmentRequest.OriginAddress.CountryCode);
            Assert.Equal("500 Pine St", spyCarrier.LastShipmentRequest.DestinationAddress.StreetLine1);
            Assert.Equal("US", spyCarrier.LastShipmentRequest.DestinationAddress.CountryCode);
        }

        // Verify persisted facility & shipping address CountryCode rows were NOT rewritten silently
        using (var verify = TestDbContextFactory.Create(
                   new TestTenantProvider { BypassTenantFilter = true },
                   new TestCurrentUserContext { UserType = UserType.Staff },
                   dbName))
        {
            var fac = await verify.Facilities.SingleAsync(f => f.Id == usFacilityId);
            Assert.Equal("USA", fac.CountryCode);
            var pkg = await verify.Packages.Include(p => p.OutboundOrder).ThenInclude(o => o.ShippingAddress).SingleAsync(p => p.Id == pkgId);
            Assert.Equal("U.S.A.", pkg.OutboundOrder.ShippingAddress!.CountryCode);
        }
    }

    [Fact]
    public async Task GetRatesAndGenerateLabel_WithNepalFacility_UsesExactPersistedOriginAndDestination()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var npFacilityId = Guid.NewGuid();
        var pkgId = await SeedLabelPackageAsync(
            dbName,
            clientId,
            new Facility
            {
                Id = npFacilityId,
                FacilityCode = "FAC-KTM-01",
                Name = "Kathmandu Central Hub",
                AddressLine1 = "Ring Road, Kalanki",
                AddressLine2 = "Ward 14",
                City = "Kathmandu",
                StateOrProvince = "Bagmati",
                PostalCode = "44600",
                CountryCode = "NP",
                Timezone = "Asia/Kathmandu",
                ContactEmail = "ops.ktm@nexus3pl.com",
                ContactPhone = "+977-1-5550100",
                TotalAreaSqM = 4500m,
                DockDoorCount = 6,
                IsActive = true
            },
            new ShippingAddress
            {
                RecipientName = "Sita Sharma",
                AddressLine1 = "Lakeside Marg, Ward 6",
                City = "Pokhara",
                StateOrProvince = "Gandaki",
                PostalCode = "33700",
                CountryCode = "NPL",
                PhoneNumber = "+977-61-555012"
            });

        var spyCarrier = new SpyShippingCarrierService();

        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new ShippingLabelService(context, spyCarrier, NullLogger<ShippingLabelService>.Instance);

        await service.GetRatesForPackageAsync(new GetRatesForPackageRequest { PackageId = pkgId });
        Assert.Equal("Kathmandu Central Hub", spyCarrier.LastRateRequest!.OriginAddress.Name);
        Assert.Equal("Ring Road, Kalanki", spyCarrier.LastRateRequest.OriginAddress.StreetLine1);
        Assert.Equal("Kathmandu", spyCarrier.LastRateRequest.OriginAddress.City);
        Assert.Equal("Bagmati", spyCarrier.LastRateRequest.OriginAddress.StateOrProvince);
        Assert.Equal("44600", spyCarrier.LastRateRequest.OriginAddress.PostalCode);
        Assert.Equal("NP", spyCarrier.LastRateRequest.OriginAddress.CountryCode);
        Assert.Equal("Pokhara", spyCarrier.LastRateRequest.DestinationAddress.City);
        Assert.Equal("NP", spyCarrier.LastRateRequest.DestinationAddress.CountryCode);

        var result = await service.GenerateLabelAsync(new GenerateShippingLabelRequest
        {
            PackageId = pkgId,
            CarrierCode = "SIM-FEDEX",
            ServiceLevel = CarrierServiceLevel.StandardGround,
            IdempotencyKey = "mdm01-np-origin-1",
            UserId = Guid.NewGuid()
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Ring Road, Kalanki", spyCarrier.LastShipmentRequest!.OriginAddress.StreetLine1);
        Assert.Equal("NP", spyCarrier.LastShipmentRequest.OriginAddress.CountryCode);
        Assert.Equal("Lakeside Marg, Ward 6", spyCarrier.LastShipmentRequest.DestinationAddress.StreetLine1);
        Assert.Equal("NP", spyCarrier.LastShipmentRequest.DestinationAddress.CountryCode);
    }

    [Theory]
    [InlineData(null, "Chicago", "US", "Recipient", "10 Dest Rd", "Dallas", "US")]
    [InlineData("100 Hub Rd", null, "US", "Recipient", "10 Dest Rd", "Dallas", "US")]
    [InlineData("100 Hub Rd", "Chicago", "ZZZ", "Recipient", "10 Dest Rd", "Dallas", "US")]
    [InlineData("100 Hub Rd", "Chicago", "US", "Recipient", null, "Dallas", "US")]
    [InlineData("100 Hub Rd", "Chicago", "US", "Recipient", "10 Dest Rd", null, "US")]
    [InlineData("100 Hub Rd", "Chicago", "US", " ", "10 Dest Rd", "Dallas", "US")]
    [InlineData("100 Hub Rd", "Chicago", "US", "Recipient", "10 Dest Rd", "Dallas", "99")]
    public async Task GetRatesAndGenerateLabel_WhenOriginOrDestinationInvalid_FailsClosedWithZeroCarrierCalls(
        string? facAddr,
        string? facCity,
        string facCountry,
        string? destName,
        string? destAddr,
        string? destCity,
        string destCountry)
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var pkgId = await SeedLabelPackageAsync(
            dbName,
            clientId,
            new Facility
            {
                Id = facilityId,
                FacilityCode = "FAC-BAD",
                Name = "Incomplete Hub",
                AddressLine1 = facAddr,
                City = facCity,
                CountryCode = facCountry,
                IsActive = true
            },
            new ShippingAddress
            {
                RecipientName = destName ?? string.Empty,
                AddressLine1 = destAddr ?? string.Empty,
                City = destCity ?? string.Empty,
                StateOrProvince = "TX",
                PostalCode = "75201",
                CountryCode = destCountry
            });

        var spyCarrier = new SpyShippingCarrierService();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new ShippingLabelService(context, spyCarrier, NullLogger<ShippingLabelService>.Instance);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GetRatesForPackageAsync(new GetRatesForPackageRequest { PackageId = pkgId }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = pkgId,
                CarrierCode = "SIM-FEDEX",
                ServiceLevel = CarrierServiceLevel.StandardGround,
                IdempotencyKey = "should-not-book",
                UserId = Guid.NewGuid()
            }));

        Assert.Equal(0, spyCarrier.TotalCalls);
        Assert.Empty(await context.CarrierOperationLogs.ToListAsync());
    }

    [Fact]
    public async Task GetRatesAndGenerateLabel_WhenOrderLacksShippingAddress_FailsClosedWithoutFallback()
    {
        var dbName = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var pkgId = await SeedLabelPackageAsync(
            dbName,
            clientId,
            new Facility
            {
                Id = facilityId,
                FacilityCode = "FAC-OK",
                Name = "Complete Hub",
                AddressLine1 = "100 Valid Pkwy",
                City = "Chicago",
                StateOrProvince = "IL",
                PostalCode = "60601",
                CountryCode = "US",
                IsActive = true
            },
            shippingAddress: null);

        var spyCarrier = new SpyShippingCarrierService();
        using var context = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        var service = new ShippingLabelService(context, spyCarrier, NullLogger<ShippingLabelService>.Instance);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GetRatesForPackageAsync(new GetRatesForPackageRequest { PackageId = pkgId }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateLabelAsync(new GenerateShippingLabelRequest
            {
                PackageId = pkgId,
                CarrierCode = "SIM-FEDEX",
                ServiceLevel = CarrierServiceLevel.StandardGround,
                UserId = Guid.NewGuid()
            }));

        Assert.Equal(0, spyCarrier.TotalCalls);
    }

    #endregion

    #region Facility DTO Separation & Authorization HTTP + Handler Tests

    [Fact]
    public async Task FacilityEndpoints_EnforceDtoSeparation_AndBlockPrivilegedConfigurationFromClientsDriversAndUnauthorizedStaff()
    {
        var clientA = Guid.NewGuid();
        var facA = Guid.NewGuid();
        var facB = Guid.NewGuid();
        var staffManageFacA = Guid.NewGuid();
        var staffViewOnly = Guid.NewGuid();
        var staffCrossFacManage = Guid.NewGuid();
        var clientLegacyManage = Guid.NewGuid();
        var driverUser = Guid.NewGuid();
        var superAdmin = Guid.NewGuid();

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
            .AddApplicationPart(typeof(FacilitiesController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
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
            });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        builder.Services.AddScoped<IDateTimeProvider, DateTimeProvider>();
        builder.Services.AddScoped<AuditableEntityInterceptor>();

        var dbName = $"mdm01-fac-{Guid.NewGuid():N}";
        builder.Services.AddDbContext<ApplicationDbContext>((sp, opts) =>
            opts.UseInMemoryDatabase(dbName).AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        await using var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthenticatedTenantResolution();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var http = app.GetTestClient();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Clients.Add(new Client { Id = clientA, AccountCode = "CL-MDM", CompanyName = "Himal Trade", Status = ClientStatus.Active });
            db.Facilities.AddRange(
                new Facility
                {
                    Id = facA,
                    FacilityCode = "FAC-KTM1",
                    Name = "Kathmandu Main Hub",
                    AddressLine1 = "Ring Road 12",
                    AddressLine2 = "Gate 3",
                    City = "Kathmandu",
                    StateOrProvince = "Bagmati",
                    PostalCode = "44600",
                    CountryCode = "NP",
                    Timezone = "Asia/Kathmandu",
                    ContactEmail = "ktm@nexus3pl.test",
                    ContactPhone = "+977-1-4000100",
                    TotalAreaSqM = 6200.50m,
                    DockDoorCount = 8,
                    OperatingHoursJson = "{\"satClosed\":true,\"secretCutoff\":\"16:30\"}",
                    ConfigurationJson = "{\"internalScalePort\":9100,\"sensitiveWebhookMode\":\"strict\"}",
                    IsActive = true
                },
                new Facility
                {
                    Id = facB,
                    FacilityCode = "FAC-ORD1",
                    Name = "Chicago Foreign Hub",
                    AddressLine1 = "1000 Logistics Pkwy",
                    City = "Chicago",
                    StateOrProvince = "IL",
                    PostalCode = "60601",
                    CountryCode = "USA",
                    Timezone = "America/Chicago",
                    OperatingHoursJson = "{\"sunClosed\":true}",
                    ConfigurationJson = "{\"internalScalePort\":9200}",
                    IsActive = true
                });
            db.ClientFacilityAccesses.Add(new ClientFacilityAccess { ClientId = clientA, FacilityId = facA, Status = FacilityAccessStatus.Active });
            await db.SaveChangesAsync();
        }

        string IssueToken(Guid uid, UserType userType, Guid? tenantId, Guid? facId, IEnumerable<string> roles, IEnumerable<string> perms)
        {
            using var scope = app.Services.CreateScope();
            var tokenSvc = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var u = new User { Id = uid, Email = $"{uid:N}@test.local", FirstName = "T", LastName = "U", UserType = userType, ClientId = tenantId, AssignedFacilityId = facId };
            return tokenSvc.GenerateTokens(u, roles, perms).AccessToken;
        }

        HttpRequestMessage AuthedGet(string path, string jwt)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            return req;
        }

        var jwtClient = IssueToken(clientLegacyManage, UserType.Client, clientA, facA, [SystemRoles.SuperAdmin], [Permissions.Facilities.View, Permissions.Facilities.Manage]);
        var jwtDriver = IssueToken(driverUser, UserType.Driver, null, facA, [SystemRoles.Driver], [Permissions.Facilities.View, Permissions.Facilities.Manage]);
        var jwtStaffView = IssueToken(staffViewOnly, UserType.Staff, null, facA, [SystemRoles.WarehouseOperator], [Permissions.Facilities.View]);
        var jwtStaffManage = IssueToken(staffManageFacA, UserType.Staff, null, facA, [SystemRoles.OperationsManager], [Permissions.Facilities.View, Permissions.Facilities.Manage]);
        var jwtCrossStaff = IssueToken(staffCrossFacManage, UserType.Staff, null, facB, [SystemRoles.OperationsManager], [Permissions.Facilities.View, Permissions.Facilities.Manage, Permissions.Analytics.MultiFacilityView]);
        var jwtSuperAdmin = IssueToken(superAdmin, UserType.Staff, null, null, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions());

        // 1. Reader-safe FacilityDto returns full address/contact metadata but NEVER exposes configurationJson or operatingHoursJson
        foreach (var readerJwt in new[] { jwtClient, jwtStaffView })
        {
            using var listResp = await http.SendAsync(AuthedGet("/api/v1/facilities", readerJwt));
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            var listBody = await listResp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("configurationJson", listBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("operatingHoursJson", listBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sensitiveWebhookMode", listBody, StringComparison.OrdinalIgnoreCase);

            using var detailResp = await http.SendAsync(AuthedGet($"/api/v1/facilities/{facA}", readerJwt));
            Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);
            var detailBody = await detailResp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("configurationJson", detailBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("operatingHoursJson", detailBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("internalScalePort", detailBody, StringComparison.OrdinalIgnoreCase);

            var dto = JsonSerializer.Deserialize<FacilityDto>(detailBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Equal("Ring Road 12", dto.AddressLine1);
            Assert.Equal("Gate 3", dto.AddressLine2);
            Assert.Equal("Kathmandu", dto.City);
            Assert.Equal("Bagmati", dto.StateOrProvince);
            Assert.Equal("44600", dto.PostalCode);
            Assert.Equal("NP", dto.CountryCode);
            Assert.Equal("Asia/Kathmandu", dto.Timezone);
            Assert.Equal("ktm@nexus3pl.test", dto.ContactEmail);
            Assert.Equal("+977-1-4000100", dto.ContactPhone);
            Assert.Equal(6200.50m, dto.TotalAreaSqM);
            Assert.Equal(8, dto.DockDoorCount);
        }

        // 2. Privileged /configuration endpoint MUST reject Client (even with spoofed/legacy SuperAdmin/manage claims), Driver, ViewOnly Staff, and foreign-facility Staff!
        foreach (var deniedJwt in new[] { jwtClient, jwtDriver, jwtStaffView, jwtCrossStaff })
        {
            using var denied = await http.SendAsync(AuthedGet($"/api/v1/facilities/{facA}/configuration", deniedJwt));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        // 3. Assigned Staff with facilities:manage and verified Staff SuperAdmin succeed
        foreach (var allowedJwt in new[] { jwtStaffManage, jwtSuperAdmin })
        {
            using var ok = await http.SendAsync(AuthedGet($"/api/v1/facilities/{facA}/configuration", allowedJwt));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var cfg = JsonSerializer.Deserialize<FacilityConfigurationDto>(
                await ok.Content.ReadAsStringAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Contains("internalScalePort", cfg.ConfigurationJson);
            Assert.Contains("satClosed", cfg.OperatingHoursJson);
        }
    }

    [Fact]
    public async Task GetFacilityConfigurationQueryHandler_DirectInvocation_EnforcesFailClosedBoundaries()
    {
        var dbName = Guid.NewGuid().ToString();
        var facA = Guid.NewGuid();
        using var db = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        db.Facilities.Add(new Facility { Id = facA, FacilityCode = "FA", Name = "Hub A", ConfigurationJson = "{\"secret\":1}", IsActive = true });
        await db.SaveChangesAsync();

        var clientCaller = new TestCurrentUserContext { UserType = UserType.Client, TenantId = Guid.NewGuid(), FacilityId = facA, RolesList = [SystemRoles.SuperAdmin], PermissionsList = [Permissions.Facilities.Manage] };
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new GetFacilityConfigurationQueryHandler(db, clientCaller).Handle(new GetFacilityConfigurationQuery(facA), CancellationToken.None));

        var driverCaller = new TestCurrentUserContext { UserType = UserType.Driver, FacilityId = facA, PermissionsList = [Permissions.Facilities.Manage] };
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new GetFacilityConfigurationQueryHandler(db, driverCaller).Handle(new GetFacilityConfigurationQuery(facA), CancellationToken.None));
    }

    #endregion

    #region Helpers & Spy Carrier

    private static async Task<Guid> SeedLabelPackageAsync(
        string dbName,
        Guid clientId,
        Facility facility,
        ShippingAddress? shippingAddress)
    {
        var orderId = Guid.NewGuid();
        var huId = Guid.NewGuid();
        var pkgId = Guid.NewGuid();

        using var seed = TestDbContextFactory.Create(
            new TestTenantProvider { BypassTenantFilter = true },
            new TestCurrentUserContext { UserType = UserType.Staff },
            dbName);

        seed.Clients.Add(new Client { Id = clientId, CompanyName = "MDM Client", AccountCode = $"CL-{clientId:N}"[..10] });
        seed.Facilities.Add(facility);

        var order = new OutboundOrder
        {
            Id = orderId,
            ClientId = clientId,
            FacilityId = facility.Id,
            OrderNumber = $"SO-{orderId:N}"[..12],
            CustomerName = shippingAddress?.RecipientName ?? "Order Customer",
            Status = OutboundOrderStatus.InProgress,
            ShippingAddress = shippingAddress
        };
        if (shippingAddress != null)
        {
            shippingAddress.OutboundOrderId = orderId;
        }
        seed.OutboundOrders.Add(order);

        var hu = new HandlingUnit
        {
            Id = huId,
            ClientId = clientId,
            FacilityId = facility.Id,
            LpnCode = $"CTN-{huId:N}"[..12],
            UnitType = HandlingUnitType.ShippingCarton,
            Status = HandlingUnitStatus.InUse,
            CurrentOutboundOrderId = orderId
        };
        seed.HandlingUnits.Add(hu);

        seed.Packages.Add(new Package
        {
            Id = pkgId,
            ClientId = clientId,
            FacilityId = facility.Id,
            OutboundOrderId = orderId,
            HandlingUnitId = huId,
            PackageNumber = $"PKG-{pkgId:N}"[..12],
            LengthCm = 30m,
            WidthCm = 20m,
            HeightCm = 15m,
            GrossWeightKg = 2.5m,
            TareWeightKg = 0.3m,
            NetWeightKg = 2.2m,
            Status = PackageStatus.Packed
        });

        await seed.SaveChangesAsync();
        return pkgId;
    }

    private sealed class SpyShippingCarrierService : IShippingCarrierService
    {
        public string CarrierIdentifier => "SPY-SIM";
        public CarrierRateRequest? LastRateRequest { get; private set; }
        public CarrierShipmentRequest? LastShipmentRequest { get; private set; }
        public int TotalCalls { get; private set; }

        public Task<IReadOnlyList<CarrierRateQuote>> GetRatesAsync(CarrierRateRequest request, CancellationToken cancellationToken = default)
        {
            TotalCalls++;
            LastRateRequest = request;
            return Task.FromResult<IReadOnlyList<CarrierRateQuote>>(
            [
                new CarrierRateQuote
                {
                    CarrierCode = "SIM-FEDEX",
                    CarrierName = "Spy Carrier",
                    ServiceLevel = CarrierServiceLevel.StandardGround,
                    TotalAmount = 15.00m,
                    Currency = "USD",
                    EstimatedDeliveryDays = 2
                }
            ]);
        }

        public Task<CarrierShipmentResponse> CreateShipmentAsync(CarrierShipmentRequest request, CancellationToken cancellationToken = default)
        {
            TotalCalls++;
            LastShipmentRequest = request;
            return Task.FromResult(new CarrierShipmentResponse
            {
                IsSuccess = true,
                CarrierCode = request.CarrierCode,
                ServiceLevel = request.ServiceLevel,
                TrackingNumber = $"SPY-TRK-{request.PackageNumber}",
                LabelFormat = request.LabelFormat,
                LabelData = $"^XA^FD{request.OriginAddress.City},{request.OriginAddress.CountryCode}^XZ",
                RoutingCode = $"HUB-{request.DestinationAddress.CountryCode}",
                RateCharged = 15.00m,
                IsSimulated = true
            });
        }

        public Task<CarrierVoidResponse> VoidShipmentAsync(CarrierVoidRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CarrierVoidResponse { IsSuccess = true, TrackingNumber = request.TrackingNumber });

        public Task<CarrierTrackingResponse> TrackShipmentAsync(string trackingNumber, string carrierCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CarrierTrackingResponse { IsSuccess = true, TrackingNumber = trackingNumber, CarrierCode = carrierCode });
    }

    #endregion
}
