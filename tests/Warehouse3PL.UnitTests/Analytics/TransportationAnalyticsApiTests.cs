using System.Net.Http.Json;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Billing;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Authentication;
using Warehouse3PL.Infrastructure.Authorization;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
using Warehouse3PL.WebApi.Controllers;
using Warehouse3PL.WebApi.Middleware;

namespace Warehouse3PL.UnitTests.Analytics;

public class TransportationAnalyticsApiTests(TransportationApiFixture fixture) : IClassFixture<TransportationApiFixture>
{
    [Fact]
    public async Task Profitability_HttpContract_RequiresIndependentPermissionAndScope_AndNeverSaves()
    {
        const string root = "/api/v1/analytics/profitability/";
        var range = $"contributions?startDateUtc={DateTime.UtcNow.AddDays(-2):yyyy-MM-dd}&endDateUtc={DateTime.UtcNow:yyyy-MM-dd}";
        var before = await fixture.Fingerprint();
        using var anonymous = await fixture.Get(root+range,null);
        Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        using var otherPermission = await fixture.Get(root+range,"client");
        Assert.Equal(HttpStatusCode.Forbidden,otherPermission.StatusCode);
        using var valid = await fixture.Get(root+range,"client-profit");
        Assert.Equal(HttpStatusCode.OK,valid.StatusCode);
        var contributionRows = (await valid.Content.ReadFromJson()).GetProperty("current");
        Assert.Equal(2,contributionRows.GetArrayLength());
        Assert.All(contributionRows.EnumerateArray(), row => Assert.Equal(TransportationApiFixture.ClientA,row.GetProperty("clientId").GetGuid()));
        using var foreign = await fixture.Get(root+range+$"&clientId={TransportationApiFixture.ClientB}","client-profit");
        Assert.Equal(HttpStatusCode.Forbidden,foreign.StatusCode);
        using var facility = await fixture.Get(root+range+$"&facilityId={TransportationApiFixture.Facility3}","client-profit");
        Assert.Equal(HttpStatusCode.Forbidden,facility.StatusCode);
        using var invalid = await fixture.Get(root+"contributions?startDateUtc=2026-11-10&endDateUtc=2026-11-01","client-profit");
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using var shipment = await fixture.Get(root+$"shipments/{TransportationApiFixture.ShipmentA}","client-profit");
        Assert.Equal(HttpStatusCode.OK,shipment.StatusCode);
        using var foreignShipment = await fixture.Get(root+$"shipments/{TransportationApiFixture.ShipmentB}","client-profit");
        Assert.Equal(HttpStatusCode.NotFound,foreignShipment.StatusCode);
        Assert.Equal(before,await fixture.Fingerprint());
    }
    public static IEnumerable<object[]> Endpoints => TransportationApiFixture.Endpoints.Select(path => new object[] { path });

    [Theory, MemberData(nameof(Endpoints))]
    public async Task Queries_RunThroughHttpPipeline_AndRequireAuthentication(string path)
    {
        using var anonymous = await fixture.Get(path, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var authorized = await fixture.Get(path);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    [Theory, MemberData(nameof(Endpoints))]
    public async Task EveryQuery_RejectsMissingPermission(string path)
    {
        using var response = await fixture.Get(path, "nopermission");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ClientScope_ExcludesOtherClientsAndInactiveFacilities_BeforeMaterialization()
    {
        using var response = await fixture.Get(TransportationApiFixture.Scorecard);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJson();
        Assert.Equal(2, json.GetProperty("totalDeliveredCount").GetInt32());
        Assert.Equal(2, json.GetProperty("totalDispatchedCount").GetInt32()); // Excludes the tenant-null 999 summary.
        Assert.Equal(TransportationApiFixture.ClientA, json.GetProperty("clientId").GetGuid());
        using var assigned = await fixture.Get(TransportationApiFixture.Scorecard, "client-assigned");
        Assert.Equal(1, (await assigned.Content.ReadFromJson()).GetProperty("totalDeliveredCount").GetInt32());
    }

    [Theory]
    [InlineData("scorecard")]
    [InlineData("transit-time-distribution")]
    [InlineData("exceptions")]
    [InlineData("freight/variance")]
    [InlineData("freight/profitability")]
    [InlineData("freight/accessorials")]
    [InlineData("freight/discrepancies")]
    [InlineData("freight/rate-card-compliance")]
    public async Task ClientCannotBroadenClientOrFacilityFilters(string resource)
    {
        var url = TransportationApiFixture.Carrier(resource);
        using var otherClient = await fixture.Get(url + $"&clientId={TransportationApiFixture.ClientB}");
        Assert.Equal(HttpStatusCode.Forbidden, otherClient.StatusCode);
        using var otherFacility = await fixture.Get(url + $"&facilityId={TransportationApiFixture.Facility3}");
        Assert.Equal(HttpStatusCode.Forbidden, otherFacility.StatusCode);
    }

    [Fact]
    public async Task HeaderSpoofing_IsCheckedAfterAuthentication_AndCannotSupplyMissingIdentity()
    {
        using var spoof = await fixture.Get(TransportationApiFixture.Scorecard, tenantHeader: TransportationApiFixture.ClientB);
        Assert.Equal(HttpStatusCode.Forbidden, spoof.StatusCode);
        using var missingIdentity = await fixture.Get(TransportationApiFixture.Scorecard, "missingtenant", TransportationApiFixture.ClientA);
        Assert.Equal(HttpStatusCode.Forbidden, missingIdentity.StatusCode);
    }

    [Theory]
    [InlineData("claims-readiness")]
    [InlineData("financial-reconciliation")]
    public async Task ShipmentLookups_DoNotRevealOtherTenantOrFacilityRecords(string resource)
    {
        foreach (var id in new[] { TransportationApiFixture.ShipmentB, TransportationApiFixture.ShipmentInactive, Guid.NewGuid() })
        {
            using var response = await fixture.Get($"shipments/{id}/{resource}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task StaffFacilityScope_RequiresExplicitCrossFacilityPermission()
    {
        using var local = await fixture.Get(TransportationApiFixture.Scorecard, "staff");
        Assert.Equal(2, (await local.Content.ReadFromJson()).GetProperty("totalDeliveredCount").GetInt32());
        using var forbidden = await fixture.Get(TransportationApiFixture.Scorecard + $"&facilityId={TransportationApiFixture.Facility2}", "staff");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var cross = await fixture.Get(TransportationApiFixture.Scorecard, "staff-cross");
        Assert.Equal(4, (await cross.Content.ReadFromJson()).GetProperty("totalDeliveredCount").GetInt32());
        using var noAssignment = await fixture.Get(TransportationApiFixture.Scorecard, "staff-unassigned");
        Assert.Equal(HttpStatusCode.Forbidden, noAssignment.StatusCode);
        using var admin = await fixture.Get(TransportationApiFixture.Scorecard, "admin");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task FinancialAndClaimsPermissions_AreSeparateFromOperationalView()
    {
        using var finance = await fixture.Get(TransportationApiFixture.Carrier("freight/variance"), "client-view");
        Assert.Equal(HttpStatusCode.Forbidden, finance.StatusCode);
        using var claims = await fixture.Get($"shipments/{TransportationApiFixture.ShipmentA}/claims-readiness", "client-view");
        Assert.Equal(HttpStatusCode.Forbidden, claims.StatusCode);
        using var operational = await fixture.Get(TransportationApiFixture.Scorecard, "client-finance");
        Assert.Equal(HttpStatusCode.Forbidden, operational.StatusCode);
    }

    [Theory]
    [InlineData("carriers/FEDEX/scorecard")]
    [InlineData("carriers/FEDEX/scorecard?startDateUtc=2026-01-02&endDateUtc=2026-01-01")]
    [InlineData("carriers/FEDEX/scorecard?startDateUtc=2024-01-01&endDateUtc=2026-01-01")]
    [InlineData("carriers/FEDEX/scorecard?startDateUtc=invalid&endDateUtc=2026-01-01")]
    [InlineData("carriers/FEDEX/scorecard?startDateUtc=9999-01-01&endDateUtc=9999-01-01")]
    [InlineData("carriers/FEDEX/scorecard?startDateUtc=2026-01-01&endDateUtc=2026-01-02&serviceLevel=999")]
    [InlineData("carriers/FEDEX/freight/variance?startDateUtc=2026-01-01&endDateUtc=2026-01-02&asOfUtc=bad")]
    [InlineData("carriers/FEDEX/freight/discrepancies?startDateUtc=2026-01-01&endDateUtc=2026-01-02&pageSize=201")]
    [InlineData("carriers/FEDEX/freight/discrepancies?startDateUtc=2026-01-01&endDateUtc=2026-01-02&pageNumber=0")]
    [InlineData("carriers/FEDEX/freight/discrepancies?startDateUtc=2026-01-01&endDateUtc=2026-01-02&minimumDiscrepancyUsd=-1")]
    [InlineData("carriers/FEDEX/freight/discrepancies?startDateUtc=2026-01-01&endDateUtc=2026-01-02&statusFilter=999")]
    [InlineData("carriers/FEDEX/scorecard?startDateUtc=2026-01-01&endDateUtc=2026-01-02&clientId=00000000-0000-0000-0000-000000000000")]
    public async Task InvalidQueries_ReturnValidationProblem(string path)
    {
        using var response = await fixture.Get(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task ClaimsPage_IsDeterministicBoundedAndConfined_AndValidatesClaimType()
    {
        var path = $"facilities/{TransportationApiFixture.Facility1}/claims-readiness?pageSize=1";
        using var first = await fixture.Get(path);
        var json = await first.Content.ReadFromJson();
        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal(TransportationApiFixture.ClientA, json.GetProperty("items")[0].GetProperty("clientId").GetGuid());
        using var emptyPage = await fixture.Get(path + "&pageNumber=2");
        Assert.Empty((await emptyPage.Content.ReadFromJson()).GetProperty("items").EnumerateArray());
        using var invalid = await fixture.Get($"shipments/{TransportationApiFixture.ShipmentA}/claims-readiness?claimType=999");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task StaffRateCardQuery_HonorsClientFilter()
    {
        using var response = await fixture.Get(TransportationApiFixture.Carrier("freight/rate-card-compliance") + $"&clientId={TransportationApiFixture.ClientB}", "staff-cross");
        Assert.Equal(1, (await response.Content.ReadFromJson()).GetProperty("totalAuditedShipmentsCount").GetInt32());
    }

    [Fact]
    public async Task AllQueries_PreserveFinancialAndOperationalEvidence()
    {
        var before = await fixture.Fingerprint();
        foreach (var endpoint in TransportationApiFixture.Endpoints)
        {
            using var response = await fixture.Get(endpoint);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal(before, await fixture.Fingerprint());
    }

    [Fact]
    public async Task FinancialEvidence_RejectsInconsistentChildScopeBeforeMaterialization()
    {
        // Fixture includes foreign tenant/facility records linked to this authorized shipment,
        // with tenant-filter bypass enabled and a materialization guard rejecting foreign rows.
        using var response = await fixture.Get($"shipments/{TransportationApiFixture.ShipmentA}/financial-reconciliation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJson();
        Assert.Equal(11m, json.GetProperty("netInvoicedCarrierCostUsd").GetDecimal());
        Assert.Equal(0m, json.GetProperty("invoicedClientFreightUsd").GetDecimal());
        Assert.Equal(0m, json.GetProperty("estimatedClientFreightUsd").GetDecimal());
        Assert.Empty(json.GetProperty("adjustmentAuditTrail").EnumerateArray());
    }
}

public sealed class TransportationApiFixture : IAsyncLifetime
{
    public static readonly Guid ClientA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid ClientB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid Facility1 = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid Facility2 = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid Facility3 = Guid.Parse("20000000-0000-0000-0000-000000000003");
    public static readonly Guid ShipmentA = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid ShipmentB = Guid.Parse("30000000-0000-0000-0000-000000000002");
    public static readonly Guid ShipmentInactive = Guid.Parse("30000000-0000-0000-0000-000000000004");
    public static string Dates => $"startDateUtc={DateTime.UtcNow.AddDays(-2):yyyy-MM-dd}&endDateUtc={DateTime.UtcNow:yyyy-MM-dd}";
    public static string Carrier(string resource) => $"carriers/FEDEX/{resource}?{Dates}";
    public static string Scorecard => Carrier("scorecard");
    public static IReadOnlyList<string> Endpoints => [Scorecard, Carrier("transit-time-distribution"), Carrier("exceptions"),
        $"shipments/{ShipmentA}/claims-readiness", $"facilities/{Facility1}/claims-readiness", $"shipments/{ShipmentA}/financial-reconciliation",
        Carrier("freight/variance"), Carrier("freight/profitability"), Carrier("freight/accessorials"), Carrier("freight/discrepancies"), Carrier("freight/rate-card-compliance")];
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        var services = builder.Services;
        services.AddControllers().AddApplicationPart(typeof(TransportationAnalyticsController).Assembly).AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TransportationTestAuthentication>("Test", _ => { });
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<ITenantProvider>(_ => new TestTenantProvider { BypassTenantFilter = true });
        services.AddScoped<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddSingleton<TransportationReadOnlyGuard>();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>((sp, options) => options.UseInMemoryDatabase(dbName)
            .AddInterceptors(sp.GetRequiredService<TransportationReadOnlyGuard>()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<TransportationAnalyticsAuthorization>();
        services.AddScoped<ICarrierScorecardService, CarrierScorecardService>();
        services.AddScoped<IClaimsReadinessService, ClaimsReadinessService>();
        services.AddScoped<IDelayAttributionService, DelayAttributionService>();
        services.AddScoped<IFreightReconciliationService, FreightReconciliationService>();
        services.AddScoped<IFreightCostVarianceService, FreightCostVarianceService>();
        services.AddScoped<IProfitabilityIntelligenceService, ProfitabilityIntelligenceService>();
        services.AddScoped<IMonitoringService, MonitoringService>();
        services.AddScoped<IDecisionIntelligenceService, DecisionIntelligenceService>();
        services.AddSingleton<IFxRateProvider, FxRateProvider>();
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
        foreach (var id in new[] { ClientA, ClientB }) db.Clients.Add(new Client { Id = id, AccountCode = id.ToString(), CompanyName = id.ToString() });
        foreach (var id in new[] { Facility1, Facility2, Facility3 }) db.Facilities.Add(new Facility { Id = id, FacilityCode = id.ToString(), Name = id.ToString() });
        db.ClientFacilityAccesses.AddRange(new ClientFacilityAccess { ClientId = ClientA, FacilityId = Facility1, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = Facility2, Status = FacilityAccessStatus.Active },
            new ClientFacilityAccess { ClientId = ClientA, FacilityId = Facility3, Status = FacilityAccessStatus.Revoked });
        var date = DateTime.UtcNow.AddDays(-1);
        foreach (var (id, client, facility, cost) in new[] { (ShipmentA, ClientA, Facility1, 11m), (ShipmentB, ClientB, Facility1, 99m),
            (Guid.NewGuid(), ClientA, Facility2, 22m), (ShipmentInactive, ClientA, Facility3, 33m) })
        {
            db.Shipments.Add(new Shipment { Id = id, ClientId = client, FacilityId = facility, CarrierCode = "FEDEX", ShipmentNumber = id.ToString(), DispatchedAtUtc = date, Status = ShipmentStatus.Dispatched });
            db.DailyClientProfitabilitySummaries.Add(new DailyClientProfitabilitySummary { ClientId=client,FacilityId=facility,DateUtc=date.Date,BilledHandlingRevenue=cost,TimeGrain=TimeGrain.Daily });
            var compliance = CarrierSlaComplianceRecord.Create(id, id.ToString(), "FEDEX", CarrierServiceLevel.StandardGround, facility, client, date, date.AddDays(1), 2, "v1");
            compliance.DeliveredAtUtc = date.AddHours(2); compliance.ActualTransitDays = 1m; compliance.IsOnTime = true;
            db.CarrierSlaComplianceRecords.Add(compliance);
            db.CarrierLostShipmentAlerts.Add(new CarrierLostShipmentAlert { ShipmentId = id, ClientId = client, FacilityId = facility,
                CarrierCode = "FEDEX", TrackingNumber = id.ToString(), DispatchedAtUtc = date, AlertStatus = LostShipmentAlertStatus.Active });
            db.CarrierCostRecords.Add(new CarrierCostRecord { ClientId = client, FacilityId = facility, ShipmentId = id, CarrierCode = "FEDEX",
                BaseTransportationCost = cost, TotalCarrierCost = cost, Currency = "USD", Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date });
        }
        db.DailyCarrierPerformanceRecords.Add(new DailyCarrierPerformanceRecord { CarrierCode = "FEDEX", FacilityId = Facility1,
            ClientId = null, DateUtc = date.Date, DispatchedShipmentsCount = 999 });
        db.CarrierRateAgreements.Add(CarrierRateAgreement.Create("FEDEX", "API", "API test flat fee", 11m, "USD", "SHIPMENT", RateModelType.FlatFee,
            PricingTierCalculationModel.FlatRate, date.AddDays(-1)));

        // Separate foreign keys do not guarantee matching tenant/facility values on child records.
        // Poison valid shipment references deliberately to verify explicit query confinement.
        db.Packages.Add(new Package { ShipmentId = ShipmentA, ClientId = ClientB, FacilityId = Facility1,
            PackageNumber = "FOREIGN", GrossWeightKg = 999 });
        var validCost = db.CarrierCostRecords.Local.Single(c => c.ShipmentId == ShipmentA);
        validCost.BaseTransportationCost -= 1m;
        var validCharge = new CarrierAccessorialCharge { ClientId = ClientA, FacilityId = Facility1,
            ShipmentId = ShipmentA, CarrierCostRecordId = validCost.Id, CarrierAmount = 1, Currency = "USD", AssessedAtUtc = date };
        db.CarrierAccessorialCharges.Add(validCharge);
        foreach (var (client, facility) in new[] { (ClientB, Facility1), (ClientA, Facility3) })
        {
            db.CarrierCostRecords.Add(new CarrierCostRecord { ClientId = client, FacilityId = facility, ShipmentId = ShipmentA,
                CarrierCode = "FEDEX", BaseTransportationCost = 999, TotalCarrierCost = 999, Currency = "USD",
                Status = CarrierCostStatus.Invoiced, CostAssessedAtUtc = date });
            db.CarrierCostAdjustments.Add(new CarrierCostAdjustment { ClientId = client, FacilityId = facility,
                CarrierCostRecordId = validCost.Id, CarrierCostDelta = 999, Currency = "USD", AdjustedAtUtc = date });
            db.CarrierAccessorialCharges.Add(new CarrierAccessorialCharge { ClientId = client, FacilityId = facility,
                ShipmentId = ShipmentA, CarrierCostRecordId = validCost.Id, CarrierAmount = 999, Currency = "USD", AssessedAtUtc = date });
            db.ShipmentFreightMarginRecords.Add(new ShipmentFreightMarginRecord { ClientId = client, FacilityId = facility,
                ShipmentId = ShipmentA, TotalClientFreight = 999, Currency = "USD", RatedAtUtc = date });
            var activity = new BillableActivity { ClientId = client, FacilityId = facility, SourceId = ShipmentA,
                Currency = "USD", OccurredAtUtc = date };
            var invoice = new Invoice { ClientId = client, FacilityId = facility, InvoiceNumber = "FOREIGN-" + client + facility,
                Status = InvoiceStatus.Issued, InvoiceDate = date, Currency = "USD" };
            db.BillableActivities.Add(activity);
            db.Invoices.Add(invoice);
            db.InvoiceLines.Add(new InvoiceLine { InvoiceId = invoice.Id, BillableActivityId = activity.Id,
                ChargeCategory = InvoiceChargeCategory.Freight, FacilityId = facility, TotalAmount = 999 });
        }
        var validActivity = new BillableActivity { ClientId = ClientA, FacilityId = Facility1, SourceId = ShipmentA,
            Currency = "USD", OccurredAtUtc = date };
        var foreignInvoice = new Invoice { ClientId = ClientB, FacilityId = Facility1, InvoiceNumber = "FOREIGN-INVOICE",
            Status = InvoiceStatus.Issued, InvoiceDate = date, Currency = "USD" };
        db.BillableActivities.Add(validActivity);
        db.Invoices.Add(foreignInvoice);
        db.InvoiceLines.Add(new InvoiceLine { InvoiceId = foreignInvoice.Id, BillableActivityId = validActivity.Id,
            ChargeCategory = InvoiceChargeCategory.Freight, FacilityId = Facility1, TotalAmount = 999 });
        var validChargeActivity = new BillableActivity { ClientId = ClientA, FacilityId = Facility1, SourceId = validCharge.Id,
            Currency = "USD", OccurredAtUtc = date };
        db.BillableActivities.Add(validChargeActivity);
        db.InvoiceLines.Add(new InvoiceLine { InvoiceId = foreignInvoice.Id, BillableActivityId = validChargeActivity.Id,
            ChargeCategory = InvoiceChargeCategory.Freight, FacilityId = Facility1, TotalAmount = 999 });
        var foreignChargeActivity = new BillableActivity { ClientId = ClientB, FacilityId = Facility1, SourceId = validCharge.Id,
            Currency = "USD", OccurredAtUtc = date };
        db.BillableActivities.Add(foreignChargeActivity);
        db.InvoiceLines.Add(new InvoiceLine { InvoiceId = foreignInvoice.Id, BillableActivityId = foreignChargeActivity.Id,
            ChargeCategory = InvoiceChargeCategory.Freight, FacilityId = Facility1, TotalAmount = 999 });
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<TransportationReadOnlyGuard>().ReadOnly = true;
    }

    public Task<HttpResponseMessage> Get(string path, string? actor = "client", Guid? tenantHeader = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path.StartsWith("/") ? path : "/api/v1/analytics/transportation/" + path);
        if (actor != null) request.Headers.Add("X-Test-Actor", actor);
        if (tenantHeader.HasValue) request.Headers.Add("X-Tenant-Id", tenantHeader.ToString());
        return _http.SendAsync(request);
    }

    public Task<HttpResponseMessage> SendMonitoring(HttpMethod method,string path,object? body=null,string actor="client-monitor")
    {
        var request=new HttpRequestMessage(method,path);
        request.Headers.Add("X-Test-Actor",actor);
        if(body!=null) request.Content=JsonContent.Create(body);
        return _http.SendAsync(request);
    }
    public async Task<string> Fingerprint()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return JsonSerializer.Serialize(new { Shipments = await db.Shipments.AsNoTracking().OrderBy(s => s.Id).Select(s => new { s.Id, s.Status, s.TotalWeightKg }).ToListAsync(),
            Costs = await db.CarrierCostRecords.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Status, c.TotalCarrierCost, c.BaseTransportationCost }).ToListAsync(),
            InvoiceCount = await db.Invoices.CountAsync(), MarginCount = await db.ShipmentFreightMarginRecords.CountAsync() });
    }

    public async Task DisposeAsync() { _http.Dispose(); await _app.DisposeAsync(); }
}

public sealed class TransportationReadOnlyGuard(IHttpContextAccessor accessor) : SaveChangesInterceptor, IMaterializationInterceptor
{
    public bool ReadOnly { get; set; }
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (ReadOnly && eventData.Context!.ChangeTracker.Entries().Any(e => (e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) && e.Entity is not MonitoringRule && e.Entity is not MonitoringAlert)) throw new InvalidOperationException("Analytics must not save financial or operational records.");
        return result;
    }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (ReadOnly && eventData.Context!.ChangeTracker.Entries().Any(e => (e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) && e.Entity is not MonitoringRule && e.Entity is not MonitoringAlert)) throw new InvalidOperationException("Analytics must not save financial or operational records.");
        return ValueTask.FromResult(result);
    }

    public object InitializedInstance(MaterializationInterceptionData data, object entity)
    {
        var user = new CurrentUserContext(accessor);
        if (user.IsAuthenticated && user.UserType == UserType.Client)
        {
            if (entity is Warehouse3PL.Domain.Common.ITenantEntity tenantEntity)
            {
                Assert.Equal(user.TenantId, tenantEntity.ClientId);
                if (entity.GetType().GetProperty("FacilityId")?.GetValue(entity) is Guid facility)
                {
                    Assert.Contains(facility, new[] { TransportationApiFixture.Facility1, TransportationApiFixture.Facility2 });
                    if (user.FacilityId.HasValue) Assert.Equal(user.FacilityId, facility);
                }
            }
            if (entity is DailyCarrierPerformanceRecord daily) Assert.Equal(user.TenantId, daily.ClientId);
        }
        return entity;
    }
}

public sealed class TransportationTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var actor = Request.Headers["X-Test-Actor"].ToString();
        if (actor.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
        bool staff = actor.StartsWith("staff") || actor == "admin";
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new("user_type", staff ? "Staff" : "Client") };
        if (!staff && actor != "missingtenant") claims.Add(new("tenant_id", TransportationApiFixture.ClientA.ToString()));
        if (actor is "staff" or "staff-decisions" or "client-assigned") claims.Add(new("facility_id", TransportationApiFixture.Facility1.ToString()));
        if (actor == "admin") claims.Add(new(ClaimTypes.Role, "SuperAdmin"));
        if (actor is "client-decisions" or "staff-decisions") { claims.Add(new("permissions", Permissions.Analytics.DecisionLaborView)); claims.Add(new("permissions", Permissions.Analytics.DecisionTransportationView)); }
        if (actor == "client-monitor") { claims.Add(new("permissions", Permissions.Analytics.MonitoringView)); claims.Add(new("permissions", Permissions.Analytics.MonitoringManage)); }
        if (actor == "client-profit") claims.Add(new("permissions", Permissions.Analytics.ProfitabilityView));
        if (actor != "nopermission")
        {
            if (actor != "client-finance") claims.Add(new("permissions", Permissions.Analytics.TransportationView));
            if (actor != "client-view")
            {
                claims.Add(new("permissions", Permissions.Analytics.TransportationFinancialView));
                claims.Add(new("permissions", Permissions.Analytics.TransportationClaimsView));
            }
        }
        if (actor == "staff-cross") claims.Add(new("permissions", Permissions.Analytics.MultiFacilityView));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}

internal static class TransportationHttpJson
{
    public static async Task<JsonElement> ReadFromJson(this HttpContent content) => JsonSerializer.Deserialize<JsonElement>(await content.ReadAsStringAsync());
}
