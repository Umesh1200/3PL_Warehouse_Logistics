using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Inventory;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
namespace Warehouse3PL.UnitTests.Analytics;
public class MonitoringDetectorTests
{
    [Theory]
    [InlineData(MonitoringMetric.StockAvailable,MonitoringComparison.LessThan,6)]
    [InlineData(MonitoringMetric.DamagedStockPercent,MonitoringComparison.GreaterThan,10)]
    [InlineData(MonitoringMetric.ShipmentTransitHours,MonitoringComparison.GreaterThan,1)]
    [InlineData(MonitoringMetric.SlaLateHours,MonitoringComparison.GreaterThan,1)]
    [InlineData(MonitoringMetric.FreightVariancePercent,MonitoringComparison.GreaterThan,10)]
    public async Task EachDetectorUsesScopedEvidenceAndDeduplicates(MonitoringMetric metric,MonitoringComparison comparison,decimal threshold)
    {
        var user=new TestCurrentUserContext{RolesList=["SuperAdmin"]};
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user,Guid.NewGuid().ToString());
        var client=Guid.NewGuid();var facility=Guid.NewGuid();var now=DateTime.UtcNow;
        var shipment=new Shipment{ClientId=client,FacilityId=facility,Status=ShipmentStatus.Dispatched,DispatchedAtUtc=now.AddDays(-3)};
        var rule=new MonitoringRule{ClientId=client,FacilityId=facility,Name="test",Metric=metric,Comparison=comparison,Threshold=threshold,Severity=MonitoringSeverity.High};
        db.AddRange(rule,shipment,new StockOnHand{ClientId=client,FacilityId=facility,QtyAvailable=5,QtyDamaged=5},new CarrierSlaComplianceRecord{ShipmentId=shipment.Id,ClientId=client,FacilityId=facility,SlaPolicyId=Guid.NewGuid(),EstimatedDeliveryDateUtc=now.AddDays(-1)},new CarrierFreightCostSummary{ClientId=client,FacilityId=facility,DateUtc=now.Date,TotalQuotedAmountUsd=100,TotalBilledAmountUsd=150,TotalShipmentsQuotedCount=1,TotalShipmentsBilledCount=1});
        db.Add(new StockOnHand{ClientId=Guid.NewGuid(),FacilityId=facility,QtyAvailable=0,QtyDamaged=999});
        await db.SaveChangesAsync();
        var service=new MonitoringService(db,new(db,user),user);
        var first=await service.EvaluateAsync(rule.Id);Assert.Equal(1,first.Created);Assert.Equal(1,first.Observations);
        var second=await service.EvaluateAsync(rule.Id);Assert.Equal(0,second.Created);Assert.Single(db.MonitoringAlerts);
        var alert=db.MonitoringAlerts.Single();Assert.Equal(client,alert.ClientId);
        await service.TransitionAsync(alert.Id,false,"",default);Assert.Equal(MonitoringAlertStatus.Acknowledged,alert.Status);
        await service.EvaluateAsync(rule.Id);Assert.Equal(MonitoringAlertStatus.Acknowledged,alert.Status);
        await service.TransitionAsync(alert.Id,true,"reviewed",default);
        Assert.Equal(1,(await service.EvaluateAsync(rule.Id)).Reopened);
    }
    [Fact]
    public async Task MissingBaselineDoesNotInventZeroAndEqualityIsNotBreach()
    {
        var user=new TestCurrentUserContext{RolesList=["SuperAdmin"]};using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user,Guid.NewGuid().ToString());
        var client=Guid.NewGuid();var facility=Guid.NewGuid();var rule=new MonitoringRule{ClientId=client,FacilityId=facility,Metric=MonitoringMetric.FreightVariancePercent,Comparison=MonitoringComparison.LessThan,Threshold=1};
        db.AddRange(rule,new CarrierFreightCostSummary{ClientId=client,FacilityId=facility,DateUtc=DateTime.UtcNow.Date,TotalBilledAmountUsd=50,TotalShipmentsBilledCount=1});await db.SaveChangesAsync();
        var service=new MonitoringService(db,new(db,user),user);Assert.Equal(0,(await service.EvaluateAsync(rule.Id)).Observations);Assert.Empty(db.MonitoringAlerts);
        rule.Metric=MonitoringMetric.StockAvailable;rule.Threshold=5;db.Add(new StockOnHand{ClientId=client,FacilityId=facility,QtyAvailable=5});await db.SaveChangesAsync();Assert.Equal(0,(await service.EvaluateAsync(rule.Id)).Breaches);
    }
}
