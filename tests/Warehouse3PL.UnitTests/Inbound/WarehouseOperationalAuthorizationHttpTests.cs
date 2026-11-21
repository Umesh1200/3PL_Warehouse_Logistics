using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Warehouse3PL.Application;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Security;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;

namespace Warehouse3PL.UnitTests.Inbound;

public sealed class WarehouseOperationalAuthorizationHttpTests : IAsyncLifetime
{
    private static readonly Guid ClientA = Guid.Parse("81000000-0000-0000-0000-000000000001");
    private static readonly Guid ClientB = Guid.Parse("81000000-0000-0000-0000-000000000002");
    private static readonly Guid FacilityA = Guid.Parse("82000000-0000-0000-0000-000000000001");
    private static readonly Guid FacilityB = Guid.Parse("82000000-0000-0000-0000-000000000002");
    private static readonly Guid ItemA = Guid.Parse("83000000-0000-0000-0000-000000000001");
    private static readonly Guid SourceA = Guid.Parse("84000000-0000-0000-0000-000000000001");
    private static readonly Guid DestinationA = Guid.Parse("84000000-0000-0000-0000-000000000002");

    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Host.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(InventoryController).Assembly);
        builder.Services.AddApplicationServices();
        builder.Services.AddAuthentication("WarehouseTest")
            .AddScheme<AuthenticationSchemeOptions, WarehouseTestAuthentication>("WarehouseTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        builder.Services.AddScoped<ITenantProvider>(_ => new TestTenantProvider { BypassTenantFilter = true });
        builder.Services.AddScoped<IDateTimeProvider, DateTimeProvider>();
        builder.Services.AddScoped<AuditableEntityInterceptor>();
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) => options
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        builder.Services.AddScoped<WarehouseOperationalAuthorization>();

        _app = builder.Build();
        _app.UseMiddleware<ExceptionHandlingMiddleware>();
        _app.UseRouting();
        _app.UseAuthenticatedTenantResolution();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _http = _app.GetTestClient();

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Clients.AddRange(
            new Client { Id = ClientA, CompanyName = "Client A", AccountCode = "HTTP-A" },
            new Client { Id = ClientB, CompanyName = "Client B", AccountCode = "HTTP-B" });
        db.Facilities.AddRange(
            new Facility { Id = FacilityA, FacilityCode = "HTTP-FA", Name = "Facility A" },
            new Facility { Id = FacilityB, FacilityCode = "HTTP-FB", Name = "Facility B" });
        db.ClientFacilityAccesses.Add(new ClientFacilityAccess
        {
            ClientId = ClientA,
            FacilityId = FacilityA,
            Status = FacilityAccessStatus.Active
        });
        db.Locations.AddRange(
            new Location { Id = SourceA, FacilityId = FacilityA, LocationCode = "SRC-A", Barcode = "SRC-A", LocationType = LocationType.Bin, IsActive = true },
            new Location { Id = DestinationA, FacilityId = FacilityA, LocationCode = "DST-A", Barcode = "DST-A", LocationType = LocationType.Bin, IsActive = true });
        db.StockOnHand.Add(new StockOnHand
        {
            ClientId = ClientB,
            FacilityId = FacilityA,
            ItemId = ItemA,
            LocationId = SourceA,
            QtyAvailable = 25m,
            Version = 1
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task InventoryList_ClientCannotSelectForeignTenant_ThroughHttp()
    {
        using var request = Request(HttpMethod.Get,
            $"/api/v1/inventory/stock?clientId={ClientB}&facilityId={FacilityA}", "client");
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InventoryList_StaffCannotSpoofForeignFacility_ThroughHttp()
    {
        using var request = Request(HttpMethod.Get,
            $"/api/v1/inventory/stock?facilityId={FacilityB}", "staff");
        request.Headers.Add("X-Facility-Id", FacilityB.ToString());
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InventoryTransfer_ForeignTenantIsRejectedWithoutBalanceOrLedgerChange_ThroughHttp()
    {
        using var request = Request(HttpMethod.Post, "/api/v1/inventory/transfer", "client");
        request.Content = JsonContent.Create(new
        {
            clientId = ClientB,
            facilityId = FacilityA,
            itemId = ItemA,
            fromLocationId = SourceA,
            toLocationId = DestinationA,
            quantity = 5m
        });
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(25m, (await db.StockOnHand.SingleAsync()).QtyAvailable);
        Assert.Empty(await db.InventoryTransactions.ToListAsync());
    }

    [Fact]
    public async Task WarehouseEndpoints_UnauthenticatedRequestIsChallenged_ThroughHttp()
    {
        using var response = await _http.GetAsync($"/api/v1/inventory/stock?facilityId={FacilityA}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string actor)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Warehouse-Actor", actor);
        return request;
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    private sealed class WarehouseTestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var actor = Request.Headers["X-Test-Warehouse-Actor"].ToString();
            if (string.IsNullOrWhiteSpace(actor))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var staff = actor == "staff";
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new("user_type", staff ? "Staff" : "Client"),
                new("facility_id", FacilityA.ToString()),
                new("permissions", Permissions.Inventory.View),
                new("permissions", Permissions.Inventory.Transfer),
                new("permissions", Permissions.Inventory.Adjust)
            };
            if (!staff)
            {
                claims.Add(new Claim("tenant_id", ClientA.ToString()));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
