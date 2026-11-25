using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Storage;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;
using Xunit;

namespace Warehouse3PL.UnitTests.Outbound;

public sealed class Mdm02StorageDownloadSecurityTests : IDisposable
{
    private const string JwtSecret = "Nexus3PL-Mdm02-StorageDownload-SigningKey-2026!";
    private const string JwtIssuer = "nexus3pl-mdm02-auth";
    private const string JwtAudience = "nexus3pl-mdm02-api";
    private const string StorageSecret = "Mdm02-Strong-Storage-Download-Hmac-Key-2026-99!";

    private readonly string _storageDir = Path.Combine(Path.GetTempPath(), $"mdm02-storage-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_storageDir))
            {
                Directory.Delete(_storageDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    #region Signing-Key Startup Fail-Closed Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short-key")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(ObjectStorageOptions.RetiredLegacyDefaultKey)]
    public async Task NonDevelopmentStartup_WithMissingWeakOrRetiredSigningKey_FailsClosed(string? invalidKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=postgres;Password=postgres",
            ["ObjectStorage:BaseDirectory"] = _storageDir,
            ["ObjectStorage:SigningSecretKey"] = invalidKey
        });

        builder.Services.AddApplicationServices();
        builder.Services.AddInfrastructureServices(builder.Configuration);

        await using var app = builder.Build();
        await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync());
    }

    [Fact]
    public async Task NonDevelopmentStartup_WithStrongSigningKey_PassesValidation()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Staging });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=postgres;Password=postgres",
            ["ObjectStorage:BaseDirectory"] = _storageDir,
            ["ObjectStorage:SigningSecretKey"] = StorageSecret
        });

        builder.Services.AddApplicationServices();
        builder.Services.AddInfrastructureServices(builder.Configuration);

        await using var app = builder.Build();
        var storage = app.Services.GetRequiredService<IObjectStorageService>();
        var signed = await storage.GeneratePreSignedDownloadUrlAsync("tenants/abc/pod/pkg/sig.png");
        Assert.StartsWith("/api/v1/storage/download?", signed.DownloadUrl);
        Assert.True(storage.ValidatePreSignedToken("tenants/abc/pod/pkg/sig.png", signed.Token, DateTime.UtcNow));
    }

    [Fact]
    public async Task DevelopmentEnvironment_WithoutConfiguredKey_UsesEphemeralRandomKey_NeverStaticFallback()
    {
        var env = new TestHostEnv { EnvironmentName = Environments.Development };
        var opts = Options.Create(new ObjectStorageOptions { BaseDirectory = _storageDir });

        var svc1 = new PrivateObjectStorageService(opts, NullLogger<PrivateObjectStorageService>.Instance, env);
        var svc2 = new PrivateObjectStorageService(opts, NullLogger<PrivateObjectStorageService>.Instance, env);

        var urlFromSvc1 = await svc1.GeneratePreSignedDownloadUrlAsync("tenants/c1/pod/p1/sig.png");
        Assert.True(svc1.ValidatePreSignedToken("tenants/c1/pod/p1/sig.png", urlFromSvc1.Token, DateTime.UtcNow));
        // Distinct ephemeral random keys mean instance 2 must NOT validate instance 1 tokens
        Assert.False(svc2.ValidatePreSignedToken("tenants/c1/pod/p1/sig.png", urlFromSvc1.Token, DateTime.UtcNow));
    }

    #endregion

    #region HTTP Storage Download Security & Ownership Tests

    [Fact]
    public async Task StorageDownload_EnforcesAuth_PathAndTokenValidation_Headers_Ownership_AndAuditTrail()
    {
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();
        var facA = Guid.NewGuid();
        var facB = Guid.NewGuid();
        var superAdminId = Guid.NewGuid();
        var staffFacAId = Guid.NewGuid();
        var staffFacBId = Guid.NewGuid();
        var staffNoPermId = Guid.NewGuid();
        var clientAUserId = Guid.NewGuid();
        var clientBUserId = Guid.NewGuid();
        var driverAssignedId = Guid.NewGuid();
        var driverOtherId = Guid.NewGuid();

        var clock = new ControllableClock(DateTime.UtcNow);
        var auditLogSpy = new SpyLoggerProvider();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(auditLogSpy);
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ObjectStorage:BaseDirectory"] = _storageDir,
            ["ObjectStorage:SigningSecretKey"] = StorageSecret
        });

        builder.Services.AddOptions<ObjectStorageOptions>()
            .Bind(builder.Configuration.GetSection(ObjectStorageOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IObjectStorageService, PrivateObjectStorageService>();
        builder.Services.Configure<JwtOptions>(o =>
        {
            o.SecretKey = JwtSecret;
            o.Issuer = JwtIssuer;
            o.Audience = JwtAudience;
            o.ExpiryMinutes = 60;
        });

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(StorageController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddApplicationServices();
        builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
        builder.Services.AddSingleton<ITokenService, JwtTokenService>();
        builder.Services.AddSingleton<IDateTimeProvider>(clock);
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
            });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITenantProvider, TenantProvider>();
        builder.Services.AddScoped<AuditableEntityInterceptor>();

        var dbName = $"mdm02-http-{Guid.NewGuid():N}";
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

        // Seed Storage Objects + DB Ownership
        var storage = app.Services.GetRequiredService<IObjectStorageService>();
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n%test-return-label");
        var svgBytes = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

        var podKeyA = $"tenants/{clientA}/pod/{Guid.NewGuid()}/signature_active.png";
        var podSupersededSvgKey = $"tenants/{clientA}/pod/{Guid.NewGuid()}/signature_old.svg";
        var retLabelKeyA = $"tenants/{clientA}/returns/{Guid.NewGuid():N}/labels/RET-100.pdf";
        var unregisteredKey = $"tenants/{clientA}/pod/{Guid.NewGuid()}/unregistered_orphan.png";
        var unsupportedKey = "secrets/master_keys.png";
        var tempStagedKey = $"temp/tenants/{clientA}/pod/temp_sig_1.png";

        await storage.UploadStagedAsync("temp_a.png", new MemoryStream(pngBytes), "image/png");
        await storage.FinalizeObjectAsync("temp_a.png", podKeyA);

        await storage.UploadStagedAsync("temp_old.svg", new MemoryStream(svgBytes), "image/svg+xml");
        await storage.FinalizeObjectAsync("temp_old.svg", podSupersededSvgKey);

        await storage.UploadStagedAsync("temp_ret.pdf", new MemoryStream(pdfBytes), "application/pdf");
        await storage.FinalizeObjectAsync("temp_ret.pdf", retLabelKeyA);

        await storage.UploadStagedAsync("temp_unreg.png", new MemoryStream(pngBytes), "image/png");
        await storage.FinalizeObjectAsync("temp_unreg.png", unregisteredKey);

        await storage.UploadStagedAsync("temp_unsup.png", new MemoryStream(pngBytes), "image/png");
        await storage.FinalizeObjectAsync("temp_unsup.png", unsupportedKey);

        await storage.UploadStagedAsync(tempStagedKey, new MemoryStream(pngBytes), "image/png");

        var pkgAId = Guid.NewGuid();
        var shpAId = Guid.NewGuid();
        var ordAId = Guid.NewGuid();
        var huAId = Guid.NewGuid();
        var retAuthId = Guid.NewGuid();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Clients.AddRange(
                new Client { Id = clientA, CompanyName = "Tenant A", AccountCode = "TA", Status = ClientStatus.Active },
                new Client { Id = clientB, CompanyName = "Tenant B", AccountCode = "TB", Status = ClientStatus.Active });
            db.Facilities.AddRange(
                new Facility { Id = facA, FacilityCode = "FAC-A", Name = "Hub A", AddressLine1 = "1 Main", City = "Chicago", CountryCode = "US", IsActive = true },
                new Facility { Id = facB, FacilityCode = "FAC-B", Name = "Hub B", AddressLine1 = "2 Main", City = "Dallas", CountryCode = "US", IsActive = true });
            db.ClientFacilityAccesses.AddRange(
                new ClientFacilityAccess { ClientId = clientA, FacilityId = facA, Status = FacilityAccessStatus.Active },
                new ClientFacilityAccess { ClientId = clientB, FacilityId = facA, Status = FacilityAccessStatus.Active });

            db.OutboundOrders.Add(new OutboundOrder { Id = ordAId, ClientId = clientA, FacilityId = facA, OrderNumber = "SO-M2", CustomerName = "Cust A" });
            db.HandlingUnits.Add(new HandlingUnit { Id = huAId, ClientId = clientA, FacilityId = facA, LpnCode = "HU-M2", UnitType = HandlingUnitType.ShippingCarton });
            db.Shipments.Add(new Shipment
            {
                Id = shpAId,
                ClientId = clientA,
                FacilityId = facA,
                ShipmentNumber = "SHP-M2",
                CarrierCode = "SIM-FEDEX",
                Status = ShipmentStatus.Dispatched,
                AssignedDriverUserId = driverAssignedId,
                CustodyStatus = ShipmentCustodyStatus.Confirmed
            });
            db.Packages.Add(new Package
            {
                Id = pkgAId,
                ClientId = clientA,
                FacilityId = facA,
                OutboundOrderId = ordAId,
                ShipmentId = shpAId,
                HandlingUnitId = huAId,
                PackageNumber = "PKG-M2",
                TrackingNumber = "TRK-M2",
                CarrierCode = "SIM-FEDEX",
                Status = PackageStatus.Loaded
            });

            var newPod = new ProofOfDelivery
            {
                ClientId = clientA,
                FacilityId = facA,
                PackageId = pkgAId,
                ShipmentId = shpAId,
                TrackingNumber = "TRK-M2",
                CarrierCode = "SIM-FEDEX",
                RecipientName = "Alice",
                DeliveryLocationDescription = "Front Desk",
                IsSigned = true,
                SignatureObjectKey = podKeyA,
                SignatureContentType = "image/png",
                SignatureSha256 = "sha",
                IdempotencyKey = "IDEMP-M2-NEW",
                EvidenceSource = PodEvidenceSource.DriverHandheld,
                DeliveredAtUtc = clock.UtcNow,
                CapturedAtUtc = clock.UtcNow
            };

            var oldPod = new ProofOfDelivery
            {
                ClientId = clientA,
                FacilityId = facA,
                PackageId = pkgAId,
                ShipmentId = shpAId,
                TrackingNumber = "TRK-M2",
                CarrierCode = "SIM-FEDEX",
                RecipientName = "Old Recip",
                DeliveryLocationDescription = "Dock",
                IsSigned = true,
                SignatureObjectKey = podSupersededSvgKey,
                SignatureContentType = "image/svg+xml",
                SignatureSha256 = "sha-old",
                IdempotencyKey = "IDEMP-M2-OLD",
                EvidenceSource = PodEvidenceSource.DriverHandheld,
                DeliveredAtUtc = clock.UtcNow.AddMinutes(-10),
                CapturedAtUtc = clock.UtcNow.AddMinutes(-10)
            };
            oldPod.Supersede(newPod.Id, "Replaced", clock.UtcNow);

            db.ProofOfDeliveries.AddRange(oldPod, newPod);
            db.ReturnShipments.Add(new ReturnShipment
            {
                Id = Guid.NewGuid(),
                ClientId = clientA,
                OriginFacilityId = facA,
                DestinationFacilityId = facA,
                OrderId = ordAId,
                OriginalShipmentId = shpAId,
                PackageId = pkgAId,
                DeliveryExceptionId = Guid.NewGuid(),
                ReturnAuthorizationId = retAuthId,
                ReturnRmaNumber = "RMA-M2",
                CarrierCode = "SIM-FEDEX",
                TrackingNumber = "RET-100",
                LabelObjectKey = retLabelKeyA,
                Status = ReturnShipmentStatus.Authorized
            });
            await db.SaveChangesAsync();
        }

        string IssueJwt(Guid uid, UserType userType, Guid? tenantId, Guid? facId, IEnumerable<string> roles, IEnumerable<string> perms)
        {
            using var scope = app.Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var user = new User { Id = uid, Email = $"{uid:N}@test.local", FirstName = "T", LastName = "U", UserType = userType, ClientId = tenantId, AssignedFacilityId = facId };
            return tokens.GenerateTokens(user, roles, perms).AccessToken;
        }

        var jwtSuperAdmin = IssueJwt(superAdminId, UserType.Staff, null, null, [SystemRoles.SuperAdmin], Permissions.GetAllPermissions());
        var jwtStaffA = IssueJwt(staffFacAId, UserType.Staff, null, facA, ["Dispatcher"], [Permissions.Dispatch.View]);
        var jwtStaffB = IssueJwt(staffFacBId, UserType.Staff, null, facB, ["Dispatcher"], [Permissions.Dispatch.View]);
        var jwtStaffNoPerm = IssueJwt(staffNoPermId, UserType.Staff, null, facA, ["Viewer"], [Permissions.Items.View]);
        var jwtClientA = IssueJwt(clientAUserId, UserType.Client, clientA, facA, [SystemRoles.ClientAdmin], [Permissions.Dispatch.View]);
        var jwtClientB = IssueJwt(clientBUserId, UserType.Client, clientB, facA, [SystemRoles.ClientAdmin], [Permissions.Dispatch.View]);
        var jwtDriverOk = IssueJwt(driverAssignedId, UserType.Driver, null, facA, [SystemRoles.Driver], [Permissions.Dispatch.ExecuteDelivery]);
        var jwtDriverOther = IssueJwt(driverOtherId, UserType.Driver, null, facA, [SystemRoles.Driver], [Permissions.Dispatch.ExecuteDelivery]);

        HttpRequestMessage DownloadReq(string url, string? jwt)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (jwt != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            return req;
        }

        var podUrl = (await storage.GeneratePreSignedDownloadUrlAsync(podKeyA, TimeSpan.FromMinutes(15))).DownloadUrl;
        var supersededSvgUrl = (await storage.GeneratePreSignedDownloadUrlAsync(podSupersededSvgKey, TimeSpan.FromMinutes(15))).DownloadUrl;
        var retUrl = (await storage.GeneratePreSignedDownloadUrlAsync(retLabelKeyA, TimeSpan.FromMinutes(15))).DownloadUrl;
        var unregUrl = (await storage.GeneratePreSignedDownloadUrlAsync(unregisteredKey, TimeSpan.FromMinutes(15))).DownloadUrl;
        var unsupUrl = (await storage.GeneratePreSignedDownloadUrlAsync(unsupportedKey, TimeSpan.FromMinutes(15))).DownloadUrl;
        var tempUrl = (await storage.GeneratePreSignedDownloadUrlAsync(tempStagedKey, TimeSpan.FromMinutes(15))).DownloadUrl;

        // 1. Unauthenticated -> 401
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(podUrl)).StatusCode);

        // 2. Missing key or token -> 400
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(DownloadReq("/api/v1/storage/download", jwtClientA))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(DownloadReq($"/api/v1/storage/download?key={Uri.EscapeDataString(podKeyA)}", jwtClientA))).StatusCode);

        // 3. Valid authorized downloads (ClientA, StaffA, AssignedDriver, SuperAdmin) + Security Headers
        foreach (var jwt in new[] { jwtClientA, jwtStaffA, jwtDriverOk, jwtSuperAdmin })
        {
            using var okResp = await http.SendAsync(DownloadReq(podUrl, jwt));
            Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
            Assert.Equal("image/png", okResp.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", okResp.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("nosniff", okResp.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Contains("no-store", okResp.Headers.CacheControl?.ToString() ?? string.Empty);
            Assert.Equal("default-src 'none'; sandbox", okResp.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Equal(pngBytes, await okResp.Content.ReadAsByteArrayAsync());
        }

        // Historical superseded POD evidence remains accessible to ClientA with strict sandbox CSP + attachment headers on SVG
        using (var svgResp = await http.SendAsync(DownloadReq(supersededSvgUrl, jwtClientA)))
        {
            Assert.Equal(HttpStatusCode.OK, svgResp.StatusCode);
            Assert.Equal("image/svg+xml", svgResp.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", svgResp.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("default-src 'none'; sandbox", svgResp.Headers.GetValues("Content-Security-Policy").Single());
        }

        // Return label accessible to ClientA & StaffA, denied to Drivers
        using (var retOk = await http.SendAsync(DownloadReq(retUrl, jwtClientA)))
        {
            Assert.Equal(HttpStatusCode.OK, retOk.StatusCode);
            Assert.Equal("application/pdf", retOk.Content.Headers.ContentType?.MediaType);
            Assert.Equal(pdfBytes, await retOk.Content.ReadAsByteArrayAsync());
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(DownloadReq(retUrl, jwtDriverOk))).StatusCode);

        // 4. Cross-Client, Cross-Facility, Driver Ownership, Unregistered & Permissionless Staff Denied (403, Uniform Non-Disclosure)
        string? firstForbiddenBody = null;
        foreach (var deniedReq in new[]
        {
            DownloadReq(podUrl, jwtClientB),        // Foreign Client holding valid signed URL!
            DownloadReq(retUrl, jwtClientB),        // Foreign Client return label
            DownloadReq(podUrl, jwtStaffB),         // Foreign Facility Staff
            DownloadReq(podUrl, jwtStaffNoPerm),    // Staff lacking Dispatch/Outbound permission
            DownloadReq(podUrl, jwtDriverOther),    // Wrong Driver
            DownloadReq(unregUrl, jwtSuperAdmin),   // Orphan disk object unregistered in DB
            DownloadReq(unsupUrl, jwtSuperAdmin),   // Unsupported namespace (secrets/)
            DownloadReq(tempUrl, jwtSuperAdmin),    // Temporary staged object
            DownloadReq("/api/v1/storage/download?key=" + Uri.EscapeDataString("../etc/passwd") + "&token=abc", jwtSuperAdmin),
            DownloadReq("/api/v1/storage/download?key=" + Uri.EscapeDataString(podKeyA + ".meta.json") + "&token=abc", jwtSuperAdmin),
            DownloadReq("/api/v1/storage/download?key=" + Uri.EscapeDataString($"tenants/{clientA}/pod/a/sig.png") + "&token=tampered-token", jwtClientA)
        })
        {
            using var resp = await http.SendAsync(deniedReq);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Tenant A", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(clientA.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Access to the requested storage object is denied", body);
            firstForbiddenBody ??= body;
        }

        // 5. Expired token check
        clock.Advance(TimeSpan.FromMinutes(20));
        using (var expiredResp = await http.SendAsync(DownloadReq(podUrl, jwtClientA)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, expiredResp.StatusCode);
        }

        // 6. Verify rejected-access security audit logs emitted
        Assert.Contains(auditLogSpy.Messages, m => m.Contains("SECURITY_AUDIT StorageDownloadRejected"));
    }

    #endregion

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TestApp";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class ControllableClock(DateTime initialUtc) : IDateTimeProvider
    {
        private DateTime _utcNow = initialUtc;
        public DateTime UtcNow => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class SpyLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new SpyLogger(Messages);
        public void Dispose() { }

        private sealed class SpyLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception));
        }
    }
}
