using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Persistence;
namespace Warehouse3PL.Infrastructure.Services.Analytics;
public partial class AnalyticsProjectionService
{
    private static readonly SemaphoreSlim JournalGate=new(1,1);
    private static readonly Dictionary<string,Type[]> EffectTypes=new()
    {
        [FacilityProductivityProjection]=[typeof(DailyFacilityProductivitySummary),typeof(HourlyFacilityThroughputSnapshot)],
        [ClientProfitabilityProjection]=[typeof(DailyClientProfitabilitySummary)],
        [InventoryUtilizationProjection]=[typeof(DailyInventoryUtilizationSnapshot)],
        [CarrierPerformanceProjection]=[typeof(DailyCarrierPerformanceRecord)],
        [DockYardProjection]=[typeof(FacilityDockYardSnapshot)],
        [WorkforceProductivityProjection]=[typeof(WorkerTaskExecutionLog),typeof(WorkerIdleIntervalRecord),typeof(DailyWorkerProductivitySummary)],
        [InventoryAgingProjection]=[typeof(DailyInventoryAgingSnapshot)],
        [LocationSpaceUtilizationProjection]=[typeof(DailyLocationSpaceUtilizationRecord)],
        [SkuVelocityProjection]=[typeof(SkuVelocityClassificationRecord)]
    };
    private sealed record Effect(string Type,Guid Id,Dictionary<string,JsonElement> Values,bool Deleted=false);
    private static string Hash(AnalyticsIngestionJournal j)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{j.ProjectionName}|{j.EventKind}|{j.EventId}|{j.SequenceNumber}|{j.EffectiveAtUtc:O}|{j.IngestionOrdinal}|{j.SchemaVersion}|{j.ArgumentsJson}|{j.EffectsJson}")));
    private ApplicationDbContext JournalDb=>_dbContext as ApplicationDbContext??throw new InvalidOperationException("Durable journaling requires the application EF context.");
    private async Task<T> JournalAtomic<T>(Func<Task<T>> action,CancellationToken ct)
    {
        await JournalGate.WaitAsync(ct);
        var db=JournalDb;
        try
        {
            if(!db.Database.IsRelational())return await action();
            if(db.Database.CurrentTransaction!=null)throw new InvalidOperationException("Journal ingestion/replay owns its transaction.");
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>{
                await using var tx=await db.Database.BeginTransactionAsync(ct);
                try {await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(63880001)",ct);var value=await action();await tx.CommitAsync(ct);return value;}
                catch {await tx.RollbackAsync(CancellationToken.None);db.ChangeTracker.Clear();throw;}
            });
        } finally {JournalGate.Release();}
    }
    private async Task JournalIngestAsync(string projection,string kind,Guid eventId,long sequence,DateTime effective,object arguments,Func<Task> action,CancellationToken ct)
    {
        await JournalAtomic(async()=>{
            var db=JournalDb;
            db.ChangeTracker.DetectChanges();
            if(db.ChangeTracker.Entries().Any(x=>x.State==EntityState.Added||x.State==EntityState.Modified||x.State==EntityState.Deleted))throw new InvalidOperationException("Journal ingestion requires a clean unit of work.");
            if(await db.AnalyticsIngestionJournals.AnyAsync(x=>x.ProjectionName==projection&&x.EventId==eventId,ct))return 0;
            // A legacy dedup identity has no source payload: preserve it and reject destructive reconstruction later.
            if(await HasEventBeenProcessedAsync(projection,eventId,ct))return 0;
            var effects=new Dictionary<(string,Guid),Effect>();
            void Capture(object? sender,SavingChangesEventArgs args)
            {
                db.ChangeTracker.DetectChanges();
                foreach(var entry in db.ChangeTracker.Entries().Where(e=>EffectTypes[projection].Contains(e.Metadata.ClrType)&&(e.State==EntityState.Added||e.State==EntityState.Modified||e.State==EntityState.Deleted)))
                {
                    var id=(Guid)entry.Property("Id").CurrentValue!;
                    var values=entry.Properties.Where(p=>!p.Metadata.IsShadowProperty()).ToDictionary(p=>p.Metadata.Name,p=>JsonSerializer.SerializeToElement(p.CurrentValue,p.Metadata.ClrType));
                    effects[(entry.Metadata.ClrType.Name,id)]=new(entry.Metadata.ClrType.Name,id,values,entry.State==EntityState.Deleted);
                }
            }
            void CaptureFinal(object? sender,SavedChangesEventArgs args)
            {
                foreach(var entry in db.ChangeTracker.Entries().Where(e=>EffectTypes[projection].Contains(e.Metadata.ClrType)))
                {
                    var id=(Guid)entry.Property("Id").CurrentValue!;var key=(entry.Metadata.ClrType.Name,id);
                    if(effects.TryGetValue(key,out var prior)&&!prior.Deleted)effects[key]=prior with {Values=entry.Properties.Where(p=>!p.Metadata.IsShadowProperty()).ToDictionary(p=>p.Metadata.Name,p=>JsonSerializer.SerializeToElement(p.CurrentValue,p.Metadata.ClrType))};
                }
            }
            db.SavingChanges+=Capture;
            db.SavedChanges+=CaptureFinal;
            try{await action();}finally{db.SavingChanges-=Capture;db.SavedChanges-=CaptureFinal;}
            var ordinal=(await db.AnalyticsIngestionJournals.Where(x=>x.ProjectionName==projection).MaxAsync(x=>(long?)x.IngestionOrdinal,ct)??0)+1;
            var journal=new AnalyticsIngestionJournal{ProjectionName=projection,EventKind=kind,EventId=eventId,SequenceNumber=sequence,EffectiveAtUtc=DateTime.SpecifyKind(effective,DateTimeKind.Utc),IngestionOrdinal=ordinal,ArgumentsJson=JsonSerializer.Serialize(arguments),EffectsJson=JsonSerializer.Serialize(effects.Values.OrderBy(x=>x.Type).ThenBy(x=>x.Id))};
            journal.PayloadHash=Hash(journal);db.AnalyticsIngestionJournals.Add(journal);await db.SaveChangesAsync(ct);return 0;
        },ct);
    }
    private async Task<int> RestoreJournalAsync(string projection,ProjectionReplayMode mode,DateTime? from,DateTime? to,Guid? facility,CancellationToken ct)
    {
        if(!JournalDb.IsPrivilegedAnalyticsMaintenance)throw new ForbiddenException("Projection restoration requires an authenticated SuperAdmin maintenance context.");
        projection=projection?.Trim()??"";
        if(!EffectTypes.ContainsKey(projection)||!Enum.IsDefined(mode)||from>to||facility==Guid.Empty)throw new ValidationException("replay","Unknown projection/mode or invalid replay scope/window.");
        return await JournalAtomic(async()=>{
            var db=JournalDb;
            var journal=await db.AnalyticsIngestionJournals.Where(x=>x.ProjectionName==projection).OrderBy(x=>x.IngestionOrdinal).ToListAsync(ct);
            var dedups=await db.AnalyticsEventDeduplicationRecords.Where(x=>x.ProjectionName==projection).ToListAsync(ct);
            if(projection==InventoryAgingProjection&&journal.Count==0&&await db.InventoryMovementEventLogs.AnyAsync(x=>!facility.HasValue||x.FacilityId==facility,ct))throw new ValidationException("journal","Movement logs do not contain full aging snapshot evidence; restoration requires captured journal effects.");
            if(dedups.Any(d=>!journal.Any(j=>j.EventId==d.EventId)))throw new ValidationException("journal","Historical payload gap: replay rejected before mutation.");
            var decoded=new List<(AnalyticsIngestionJournal Journal,List<Effect> Effects)>();
            foreach(var j in journal)
            {
                if(j.SchemaVersion!=1||j.PayloadHash!=Hash(j)||!JournalKinds.TryGetValue(j.EventKind,out var family)||family!=projection)throw new ValidationException("journal","Invalid journal version/hash/kind.");
                var effects=JsonSerializer.Deserialize<List<Effect>>(j.EffectsJson)??throw new ValidationException("journal","Missing effects.");
                foreach(var effect in effects)
                {
                    var type=EffectTypes[projection].SingleOrDefault(x=>x.Name==effect.Type)??throw new ValidationException("journal","Unrecognized effect type.");
                    var model=db.Model.FindEntityType(type)!;
                    if(!effect.Values.TryGetValue("Id",out var id)||id.GetGuid()!=effect.Id||effect.Values.Keys.Any(k=>model.FindProperty(k)==null))throw new ValidationException("journal","Invalid effect identity/property.");
                    if(model.GetProperties().Where(p=>!p.IsShadowProperty()).Any(p=>!effect.Values.ContainsKey(p.Name)))throw new ValidationException("journal","Incomplete effect properties.");
                    foreach(var (name,value) in effect.Values)_=value.Deserialize(model.FindProperty(name)!.ClrType);
                }
                decoded.Add((j,effects));
            }
            bool InScope(Effect e)=>!facility.HasValue||(e.Values.TryGetValue("FacilityId",out var f)&&f.ValueKind!=JsonValueKind.Null&&f.GetGuid()==facility);
            var selected=decoded.Where(x=>(!from.HasValue||x.Journal.EffectiveAtUtc>=from)&&(!to.HasValue||x.Journal.EffectiveAtUtc<=to)&&x.Effects.Any(InScope)).ToList();
            if(mode==ProjectionReplayMode.FromWatermark)selected=selected.Where(x=>!dedups.Any(d=>d.EventId==x.Journal.EventId)).ToList();
            // A date window selects affected complete row partitions; restore latest effect for each selected key using all captured inputs.
            var keys=selected.SelectMany(x=>x.Effects).Where(InScope).Select(x=>(x.Type,x.Id)).ToHashSet();
            var latest=decoded.SelectMany(x=>x.Effects).Where(x=>keys.Contains((x.Type,x.Id))).GroupBy(x=>(x.Type,x.Id)).Select(g=>g.Last()).ToList();
            foreach(var effect in latest)
            {
                var type=EffectTypes[projection].Single(x=>x.Name==effect.Type);
                var entity=await db.FindAsync(type,[effect.Id],ct);
                if(effect.Deleted){if(entity!=null)db.Remove(entity);continue;}
                if(entity==null){entity=Activator.CreateInstance(type)!;db.Add(entity);}
                var entry=db.Entry(entity);
                foreach(var (name,value) in effect.Values){var property=entry.Property(name);property.CurrentValue=value.Deserialize(property.Metadata.ClrType);}
            }
            foreach(var item in selected.Where(x=>!dedups.Any(d=>d.EventId==x.Journal.EventId)))db.AnalyticsEventDeduplicationRecords.Add(AnalyticsEventDeduplicationRecord.Create(projection,item.Journal.EventId,item.Journal.EventKind));
            if(journal.Count>0){var checkpoint=await GetOrCreateCheckpointAsync(projection,ct);var last=journal.OrderBy(x=>x.SequenceNumber).ThenBy(x=>x.IngestionOrdinal).Last();checkpoint.LastProcessedEventId=last.EventId;checkpoint.LastProcessedSequenceNumber=last.SequenceNumber;checkpoint.LastProcessedTimestampUtc=last.EffectiveAtUtc;checkpoint.ProcessedCount=journal.Count;checkpoint.MarkActive();}
            await db.SaveChangesAsync(ct);
            return selected.Count;
        },ct);
    }
}
