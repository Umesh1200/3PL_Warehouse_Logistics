using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.UnitTests.Common;
namespace Warehouse3PL.UnitTests.Analytics;

public class Phase68JournalGovernanceTests
{
    [Fact]
    public async Task CompleteRecordedEffectsRepairProjectionAndRemainStableAcrossRepeatedReplay()
    {
        var user=new TestCurrentUserContext{RolesList=["SuperAdmin"]};
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user);
        var service=new AnalyticsProjectionService(db,NullLogger<AnalyticsProjectionService>.Instance);
        var day=new DateTime(2026,11,17,12,0,0,DateTimeKind.Utc);var facility=Guid.NewGuid();var client=Guid.NewGuid();
        await service.ProcessOrderReceivedAsync(Guid.NewGuid(),1,facility,client,day,3);
        await service.ProcessOrderReceivedAsync(Guid.NewGuid(),2,facility,client,day,7);
        var journalBefore=await db.AnalyticsIngestionJournals.OrderBy(x=>x.IngestionOrdinal).Select(x=>x.PayloadHash).ToListAsync();
        var row=await db.DailyFacilityProductivitySummaries.SingleAsync();row.OrdersReceivedCount=999;
        await db.SaveChangesAsync();
        Assert.Equal(2,await service.ReplayProjectionsAsync(AnalyticsProjectionService.FacilityProductivityProjection,ProjectionReplayMode.FromBeginning));
        Assert.Equal(10,(await db.DailyFacilityProductivitySummaries.SingleAsync()).OrdersReceivedCount);
        await service.ReplayProjectionsAsync(AnalyticsProjectionService.FacilityProductivityProjection,ProjectionReplayMode.FromBeginning);
        Assert.Equal(10,(await db.DailyFacilityProductivitySummaries.SingleAsync()).OrdersReceivedCount);
        Assert.Equal(journalBefore,await db.AnalyticsIngestionJournals.OrderBy(x=>x.IngestionOrdinal).Select(x=>x.PayloadHash).ToListAsync());
    }
    [Fact]
    public async Task MissingLegacyPayloadFailsBeforeProjectionOrCheckpointMutation()
    {
        var user=new TestCurrentUserContext{RolesList=["SuperAdmin"]};
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user);
        var service=new AnalyticsProjectionService(db,NullLogger<AnalyticsProjectionService>.Instance);
        var day=new DateTime(2026,11,17,0,0,0,DateTimeKind.Utc);
        await service.ProcessOrderReceivedAsync(Guid.NewGuid(),1,Guid.NewGuid(),Guid.NewGuid(),day,5);
        db.AnalyticsEventDeduplicationRecords.Add(AnalyticsEventDeduplicationRecord.Create(AnalyticsProjectionService.FacilityProductivityProjection,Guid.NewGuid(),"LegacyUnknown"));
        await db.SaveChangesAsync();var before=await db.ProjectionCheckpoints.Select(x=>x.LastProcessedSequenceNumber).ToListAsync();
        await Assert.ThrowsAsync<ValidationException>(()=>service.ReplayProjectionsAsync(AnalyticsProjectionService.FacilityProductivityProjection,ProjectionReplayMode.FromBeginning));
        Assert.Equal(5,(await db.DailyFacilityProductivitySummaries.SingleAsync()).OrdersReceivedCount);
        Assert.Equal(before,await db.ProjectionCheckpoints.Select(x=>x.LastProcessedSequenceNumber).ToListAsync());
        Assert.Equal(2,await db.AnalyticsEventDeduplicationRecords.CountAsync());
    }
    [Fact]
    public async Task ClientCannotInvokeGlobalMaintenanceEvenWithAmbientBypass()
    {
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},new(){TenantId=Guid.NewGuid(),UserType=UserType.Client});
        var service=new AnalyticsProjectionService(db,NullLogger<AnalyticsProjectionService>.Instance);
        await Assert.ThrowsAsync<ForbiddenException>(()=>service.ReplayProjectionsAsync(AnalyticsProjectionService.FacilityProductivityProjection,ProjectionReplayMode.FromBeginning));
        Assert.Empty(db.ProjectionCheckpoints);
    }
    [Fact]
    public async Task JournalCannotBeEditedOrDeletedThroughApplicationSaves()
    {
        var user=new TestCurrentUserContext{RolesList=["SuperAdmin"]};
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user);
        var service=new AnalyticsProjectionService(db,NullLogger<AnalyticsProjectionService>.Instance);
        await service.ProcessOrderReceivedAsync(Guid.NewGuid(),1,Guid.NewGuid(),Guid.NewGuid(),DateTime.UtcNow,1);
        var journal=await db.AnalyticsIngestionJournals.SingleAsync();journal.EffectsJson="[]";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
        db.ChangeTracker.Clear();journal=await db.AnalyticsIngestionJournals.SingleAsync();db.Remove(journal);
        Assert.Throws<InvalidOperationException>(()=>db.SaveChanges());
    }
    [Fact]
    public async Task PendingUnrelatedProjectionChangesCannotBeCapturedAsAnEvent()
    {
        var user=new TestCurrentUserContext{RolesList=["SuperAdmin"]};
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user);
        var service=new AnalyticsProjectionService(db,NullLogger<AnalyticsProjectionService>.Instance);
        var unrelated=DailyFacilityProductivitySummary.Create(Guid.NewGuid(),DateTime.UtcNow.Date);
        unrelated.OrdersReceivedCount=99;db.Add(unrelated);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ProcessOrderReceivedAsync(Guid.NewGuid(),1,Guid.NewGuid(),Guid.NewGuid(),DateTime.UtcNow,1));
        Assert.Empty(db.AnalyticsIngestionJournals);
        Assert.Empty(db.DailyFacilityProductivitySummaries.AsNoTracking());
    }
    [Fact]
    public async Task RecalculatedVelocityOutputIsDurableAndRestoredWithoutInventingSourceHistory()
    {
        var user=new TestCurrentUserContext{RolesList=["SuperAdmin"]};
        using var db=TestDbContextFactory.Create(new(){BypassTenantFilter=true},user);
        var service=new AnalyticsProjectionService(db,NullLogger<AnalyticsProjectionService>.Instance);
        var facility=Guid.NewGuid();var client=Guid.NewGuid();var item=Guid.NewGuid();
        var day=new DateTime(2026,11,17,12,0,0,DateTimeKind.Utc);
        await service.ProcessInventoryMovementEventAsync(Guid.NewGuid(),1,facility,client,item,"DURABLE",InventoryMovementEventType.OutboundPick,12,day.AddDays(-1));
        var calculated=await service.CalculateSkuVelocityAsync(facility,client,item,"DURABLE",asOfUtc:day);
        var originalId=calculated.Id;var demand=calculated.TotalUnitsDemanded;
        db.Remove(calculated);await db.SaveChangesAsync();
        await service.ReplayProjectionsAsync(AnalyticsProjectionService.SkuVelocityProjection,ProjectionReplayMode.FromBeginning);
        var restored=await db.SkuVelocityClassificationRecords.SingleAsync();
        Assert.Equal(originalId,restored.Id);Assert.Equal(demand,restored.TotalUnitsDemanded);
        Assert.Single(db.InventoryMovementEventLogs);
        Assert.Contains(await db.AnalyticsIngestionJournals.ToListAsync(),x=>x.EventKind==nameof(AnalyticsProjectionService.CalculateSkuVelocityAsync));
    }
}
