using System.Net;
namespace Warehouse3PL.UnitTests.Analytics;
public class DecisionIntelligenceApiTests(TransportationApiFixture fixture) : IClassFixture<TransportationApiFixture>
{
    [Fact]
    public async Task CarrierAdviceRequiresIndependentGrantAndConstrainedScope()
    {
        const string path="/api/v1/analytics/decisions/carriers?carrierCodes=FEDEX&startDateUtc=2026-11-01&endDateUtc=2026-11-30";
        var before=await fixture.Fingerprint();
        using var anonymous=await fixture.Get(path,null);Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        using var operational=await fixture.Get(path,"client");Assert.Equal(HttpStatusCode.Forbidden,operational.StatusCode);
        using var result=await fixture.Get(path,"client-decisions");Assert.Equal(HttpStatusCode.OK,result.StatusCode);
        Assert.Equal(TransportationApiFixture.ClientA,(await result.Content.ReadFromJson()).GetProperty("clientId").GetGuid());
        using var foreign=await fixture.Get(path+$"&clientId={TransportationApiFixture.ClientB}","client-decisions");Assert.Equal(HttpStatusCode.Forbidden,foreign.StatusCode);
        using var inactive=await fixture.Get(path+$"&facilityId={TransportationApiFixture.Facility3}","client-decisions");Assert.Equal(HttpStatusCode.Forbidden,inactive.StatusCode);
        using var invalid=await fixture.Get(path+"&serviceLevel=999","client-decisions");Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using var noCandidates=await fixture.Get("/api/v1/analytics/decisions/carriers?startDateUtc=2026-11-01&endDateUtc=2026-11-30","client-decisions");Assert.Equal(HttpStatusCode.BadRequest,noCandidates.StatusCode);
        Assert.Equal(before,await fixture.Fingerprint());
    }
    [Fact]
    public async Task LaborAdviceRestrictsFacilityAndStaffAndKeepsEmptyForecastUnknown()
    {
        var path=$"/api/v1/analytics/decisions/facilities/{TransportationApiFixture.Facility1}/labor?startDateUtc=2026-11-01&endDateUtc=2026-11-30";
        using var client=await fixture.Get(path,"client-decisions");Assert.Equal(HttpStatusCode.Forbidden,client.StatusCode);
        using var staff=await fixture.Get(path,"staff-decisions");Assert.Equal(HttpStatusCode.OK,staff.StatusCode);
        var json=await staff.Content.ReadFromJson();Assert.Equal(TransportationApiFixture.Facility1,json.GetProperty("facilityId").GetGuid());
        using var foreign=await fixture.Get(path.Replace(TransportationApiFixture.Facility1.ToString(),TransportationApiFixture.Facility2.ToString()),"staff-decisions");Assert.Equal(HttpStatusCode.Forbidden,foreign.StatusCode);
        using var conflicting=await fixture.Get(path+$"&facilityId={TransportationApiFixture.Facility2}","staff-decisions");Assert.Equal(HttpStatusCode.BadRequest,conflicting.StatusCode);
    }
}
