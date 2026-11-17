using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Constants;
using Warehouse3PL.Infrastructure.Services.Analytics;
namespace Warehouse3PL.UnitTests.Analytics;
public class MonitoringApiTests(TransportationApiFixture fixture) : IClassFixture<TransportationApiFixture>
{
    [Fact]
    public async Task MonitoringHttpPermissionsOwnershipLifecycleAndReadonlyFinance()
    {
        const string root="/api/v1/analytics/monitoring/";
        var before=await fixture.Fingerprint();
        using var anonymous=await fixture.Get(root+"rules",null); Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        using var denied=await fixture.Get(root+"rules","client"); Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var body=new MonitoringRuleRequest(TransportationApiFixture.ClientA,TransportationApiFixture.Facility1,"Transit monitor",MonitoringMetric.ShipmentTransitHours,MonitoringComparison.GreaterThan,0,MonitoringSeverity.High);
        using var foreign=await fixture.SendMonitoring(HttpMethod.Post,root+"rules",body with {ClientId=TransportationApiFixture.ClientB}); Assert.Equal(HttpStatusCode.Forbidden,foreign.StatusCode);
        using var inactive=await fixture.SendMonitoring(HttpMethod.Post,root+"rules",body with {FacilityId=TransportationApiFixture.Facility3}); Assert.Equal(HttpStatusCode.Forbidden,inactive.StatusCode);
        using var created=await fixture.SendMonitoring(HttpMethod.Post,root+"rules",body); Assert.Equal(HttpStatusCode.OK,created.StatusCode);
                var ruleId=(await created.Content.ReadFromJson()).GetProperty("id").GetGuid();
        using var adminCreated=await fixture.SendMonitoring(HttpMethod.Post,root+"rules",body with {ClientId=TransportationApiFixture.ClientB},"admin");
        Assert.Equal(HttpStatusCode.OK,adminCreated.StatusCode);
        var foreignId=(await adminCreated.Content.ReadFromJson()).GetProperty("id").GetGuid();
        using var foreignEvaluate=await fixture.SendMonitoring(HttpMethod.Post,root+$"rules/{foreignId}/evaluate"); Assert.Equal(HttpStatusCode.NotFound,foreignEvaluate.StatusCode);
        using var foreignDisable=await fixture.SendMonitoring(HttpMethod.Put,root+$"rules/{foreignId}/enabled",false); Assert.Equal(HttpStatusCode.NotFound,foreignDisable.StatusCode);
        using var first=await fixture.SendMonitoring(HttpMethod.Post,root+$"rules/{ruleId}/evaluate"); Assert.Equal(HttpStatusCode.OK,first.StatusCode); Assert.Equal(1,(await first.Content.ReadFromJson()).GetProperty("created").GetInt32());
        using var second=await fixture.SendMonitoring(HttpMethod.Post,root+$"rules/{ruleId}/evaluate"); Assert.Equal(0,(await second.Content.ReadFromJson()).GetProperty("created").GetInt32());
        using var list=await fixture.Get(root+"alerts?size=1","client-monitor"); Assert.Equal(HttpStatusCode.OK,list.StatusCode); var alertId=(await list.Content.ReadFromJson()).GetProperty("items")[0].GetProperty("id").GetGuid();
        using var ack=await fixture.SendMonitoring(HttpMethod.Post,root+$"alerts/{alertId}/acknowledge"); Assert.Equal(HttpStatusCode.OK,ack.StatusCode);
        using var emptyNotes=await fixture.SendMonitoring(HttpMethod.Post,root+$"alerts/{alertId}/resolve",new {notes=""}); Assert.Equal(HttpStatusCode.BadRequest,emptyNotes.StatusCode);
        using var resolve=await fixture.SendMonitoring(HttpMethod.Post,root+$"alerts/{alertId}/resolve",new {notes="Reviewed"}); Assert.Equal(HttpStatusCode.OK,resolve.StatusCode);
        using var reopened=await fixture.SendMonitoring(HttpMethod.Post,root+$"rules/{ruleId}/evaluate"); Assert.Equal(1,(await reopened.Content.ReadFromJson()).GetProperty("reopened").GetInt32());
        using var badPage=await fixture.Get(root+"alerts?size=201","client-monitor"); Assert.Equal(HttpStatusCode.BadRequest,badPage.StatusCode);
        Assert.Equal(before,await fixture.Fingerprint());
    }
}
