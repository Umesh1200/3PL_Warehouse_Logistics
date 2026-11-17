using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
namespace Warehouse3PL.UnitTests.Analytics;
public class Phase68MonitoringResilienceTests
{
    [Fact]
    public async Task FailedRuleDoesNotStarveLaterScopedRules()
    {
        var user=new TestCurrentUserContext{RolesList=["SuperAdmin"]};
        var badClient=Guid.NewGuid();var goodClient=Guid.NewGuid();var facility=Guid.NewGuid();
        var failure=new RejectSource(badClient);
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user,queryInterceptor:failure);
        var bad=new MonitoringRule{Id=Guid.Parse("00000000-0000-0000-0000-000000000001"),ClientId=badClient,FacilityId=facility,Metric=MonitoringMetric.StockAvailable,Comparison=MonitoringComparison.LessThan,Threshold=5};
        var good=new MonitoringRule{Id=Guid.Parse("00000000-0000-0000-0000-000000000002"),ClientId=goodClient,FacilityId=facility,Metric=MonitoringMetric.StockAvailable,Comparison=MonitoringComparison.LessThan,Threshold=5};
        db.AddRange(bad,good,new StockOnHand{ClientId=badClient,FacilityId=facility,QtyAvailable=0},new StockOnHand{ClientId=goodClient,FacilityId=facility,QtyAvailable=0});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var service=new MonitoringService(db,new(db,user),user);
        // Internal worker entry, invoked directly to exercise one cycle without timers.
        var method=typeof(MonitoringService).GetMethod("EvaluateScheduledAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
        await (Task)method.Invoke(service,[CancellationToken.None])!;
        Assert.Equal(1,failure.Rejected);
        var alert=Assert.Single(db.MonitoringAlerts);Assert.Equal(good.Id,alert.RuleId);Assert.Equal(goodClient,alert.ClientId);
    }
    private sealed class RejectSource(Guid client) : IMaterializationInterceptor
    {
        public int Rejected {get;private set;}
        public object InitializedInstance(MaterializationInterceptionData data,object entity)
        {
            if(entity is StockOnHand stock&&stock.ClientId==client){Rejected++;throw new InvalidOperationException("Injected source failure for one rule.");}
            return entity;
        }
    }
}
