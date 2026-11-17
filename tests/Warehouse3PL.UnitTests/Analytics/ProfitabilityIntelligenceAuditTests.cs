using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;

namespace Warehouse3PL.UnitTests.Analytics;

public class ProfitabilityIntelligenceAuditTests
{
    [Fact]
    public async Task CurrencyAndFacilityPartitionsExcludeAggregateAndOtherGrains()
    {
        var user = new TestCurrentUserContext { RolesList = ["SuperAdmin"] };
        using var db = TestDbContextFactory.Create(new() { BypassTenantFilter = true }, user, Guid.NewGuid().ToString());
        var client = Guid.NewGuid(); var facility = Guid.NewGuid(); var day = new DateTime(2026,11,17,0,0,0,DateTimeKind.Utc);
        DailyClientProfitabilitySummary Row(string currency, decimal revenue, Guid? f, TimeGrain grain = TimeGrain.Daily) => new()
        { ClientId = client, FacilityId = f, DateUtc = day, Currency = currency, TimeGrain = grain, BilledHandlingRevenue = revenue, CarrierFreightCost = 20, DirectLaborEstimatedCost = 10 };
        db.DailyClientProfitabilitySummaries.AddRange(Row("USD",100,facility),Row("EUR",200,facility),Row("USD",999,null),Row("USD",999,facility,TimeGrain.Monthly));
        var prior = Row("USD",50,facility); prior.DateUtc=day.AddDays(-1); db.Add(prior);
        await db.SaveChangesAsync();
        var result = await new ProfitabilityIntelligenceService(db,new(db,user)).GetReportAsync(day,day,null,null);
        Assert.Equal(2,result.Current.Count);
        var usd=Assert.Single(result.Current.Where(x=>x.Currency=="USD"));
        Assert.Equal(100,usd.Revenue); Assert.Equal(70,usd.Contribution); Assert.Equal(70,usd.ContributionPercent); Assert.Equal(30,usd.ObservedCostToRevenuePercent);
        Assert.Equal(50,Assert.Single(result.Previous).Revenue);
        Assert.Contains("estimated",result.Basis); Assert.Contains("not net profit",result.Basis);
    }

    [Theory]
    [InlineData(0)] [InlineData(-10)]
    public async Task NonpositiveRevenueHasNoPercentage(decimal revenue)
    {
        var user=new TestCurrentUserContext {RolesList=["SuperAdmin"]};
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user,Guid.NewGuid().ToString());
        var day=new DateTime(2026,11,17);
        db.Add(new DailyClientProfitabilitySummary {ClientId=Guid.NewGuid(),FacilityId=Guid.NewGuid(),DateUtc=day,BilledHandlingRevenue=revenue,CarrierFreightCost=10});
        await db.SaveChangesAsync();
        var row=Assert.Single((await new ProfitabilityIntelligenceService(db,new(db,user)).GetReportAsync(day,day,null,null)).Current);
        Assert.Null(row.ContributionPercent); Assert.Null(row.ObservedCostToRevenuePercent); Assert.Equal(revenue-10,row.Contribution);
    }

    [Fact]
    public async Task AssignedStaffScopeExcludesOtherFacilityEvenWithAmbientBypass()
    {
        var facility=Guid.NewGuid(); var user=new TestCurrentUserContext {UserType=UserType.Staff,FacilityId=facility};
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user,Guid.NewGuid().ToString());
        var day=new DateTime(2026,11,17);
        db.AddRange(new DailyClientProfitabilitySummary {ClientId=Guid.NewGuid(),FacilityId=facility,DateUtc=day,BilledHandlingRevenue=5},new DailyClientProfitabilitySummary {ClientId=Guid.NewGuid(),FacilityId=Guid.NewGuid(),DateUtc=day,BilledHandlingRevenue=999});
        await db.SaveChangesAsync();
        var service=new ProfitabilityIntelligenceService(db,new(db,user));
        Assert.Equal(5,Assert.Single((await service.GetReportAsync(day,day,null,null)).Current).Revenue);
        await Assert.ThrowsAsync<ForbiddenException>(()=>service.GetReportAsync(day,day,null,Guid.NewGuid()));
        await Assert.ThrowsAsync<ValidationException>(()=>service.GetReportAsync(DateTime.MinValue,DateTime.MinValue,null,null));
    }
}
