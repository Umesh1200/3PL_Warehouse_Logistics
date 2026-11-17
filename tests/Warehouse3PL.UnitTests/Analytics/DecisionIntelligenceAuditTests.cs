using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
namespace Warehouse3PL.UnitTests.Analytics;
public class DecisionIntelligenceAuditTests
{
    [Fact]
    public async Task LaborUsesWeightedHoursAndExactFacilityWithoutFutureOrOtherActivity()
    {
        var facility=Guid.NewGuid();var user=new TestCurrentUserContext{UserType=UserType.Staff,FacilityId=facility};
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user,Guid.NewGuid().ToString());
        var day=DateTime.UtcNow.Date.AddDays(-1);
        WorkerTaskExecutionLog Log(Guid f,int units,int hours,WarehouseLaborActivityType activity=WarehouseLaborActivityType.Picking)=>new(){FacilityId=f,ActivityType=activity,UnitsProcessed=units,ActualDurationSeconds=hours*3600,StartedAtUtc=day,CompletedAtUtc=day.AddHours(hours)};
        db.AddRange(Log(facility,100,1),Log(facility,100,3),Log(Guid.NewGuid(),999,1),Log(facility,999,1,WarehouseLaborActivityType.Packing),new PickTask{FacilityId=facility,ClientId=Guid.NewGuid(),QuantityToPick=120,QuantityPicked=20},new PickTask{FacilityId=Guid.NewGuid(),ClientId=Guid.NewGuid(),QuantityToPick=999});
        await db.SaveChangesAsync();
        var service=new DecisionIntelligenceService(db,new(db,user),user,new Scores());
        var result=await service.GetLaborAdviceAsync(facility,day,day);
        Assert.Equal(2,result.HistoricalExecutions);Assert.Equal(50,result.UnitsPerLaborHour);Assert.Equal(100,result.RemainingUnits);Assert.Equal(2,result.EstimatedLaborHoursToClear);
        await Assert.ThrowsAsync<ForbiddenException>(()=>service.GetLaborAdviceAsync(Guid.NewGuid(),day,day));
        user.UserType=UserType.Client;await Assert.ThrowsAsync<ForbiddenException>(()=>service.GetLaborAdviceAsync(facility,day,day));
    }
    [Fact]
    public async Task CarrierRanksRealRatesThenTransitThenCodeAndLeavesMissingEvidenceUnranked()
    {
        var user=new TestCurrentUserContext{RolesList=["SuperAdmin"]};using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user,Guid.NewGuid().ToString());
        var scores=new Scores();var service=new DecisionIntelligenceService(db,new(db,user),user,scores);var day=DateTime.UtcNow.Date;
        var result=await service.GetCarrierAdviceAsync(["C","B","A","MISSING","A"],day,day,null,null,null);
        Assert.Equal(new[]{"A","B","C","MISSING"},result.Candidates.Select(x=>x.CarrierCode));Assert.Equal(new int?[]{1,2,3,null},result.Candidates.Select(x=>x.Rank));Assert.Equal(4,scores.Calls);
        Assert.Contains("not a quotation",result.Basis);
    }
    private sealed class Scores:ICarrierScorecardService
    {
        public int Calls;
        public Task<CarrierScorecardDto> GetCarrierScorecardAsync(string code,DateTime start,DateTime end,Guid? facilityId=null,Guid? clientId=null,CarrierServiceLevel? serviceLevel=null,CancellationToken cancellationToken=default)
        {Calls++;return Task.FromResult(new CarrierScorecardDto{CarrierCode=code,SampleSize=code=="MISSING"?0:30,IsStatisticallyReliable=code!="MISSING",OnTimeDeliveryRatePercent=code=="MISSING"?null:code=="C"?80:90,AverageTransitDays=2});}
        public Task<TransitTimeDistributionDto> GetTransitTimeDistributionAsync(string c,DateTime s,DateTime e,Guid? f=null,Guid? t=null,CarrierServiceLevel? l=null,string? country=null,string? state=null,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<DeliveryExceptionIntelligenceDto> GetDeliveryExceptionIntelligenceAsync(string c,DateTime s,DateTime e,Guid? f=null,Guid? t=null,CancellationToken ct=default)=>throw new NotSupportedException();
    }
}
