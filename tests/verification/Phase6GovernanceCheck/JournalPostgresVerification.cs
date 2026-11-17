using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Entities.Facilities;
internal static class JournalPostgresVerification
{
    public static async Task Run(string connectionString)
    {
        var cs=new NpgsqlConnectionStringBuilder(connectionString){Database="postgres"};await using var admin=new NpgsqlConnection(cs.ConnectionString);await admin.OpenAsync();
        var name="phase68_replay_"+Guid.NewGuid().ToString("N");await new NpgsqlCommand($"CREATE DATABASE {name}",admin).ExecuteNonQueryAsync();cs.Database=name;
        var counter=new Counter();
        ApplicationDbContext Context(){var user=new VerificationIdentity();var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString,n=>n.EnableRetryOnFailure()).UseSnakeCaseNamingConvention().AddInterceptors(counter).Options;return new(options,new VerificationTenant(),user,new AuditableEntityInterceptor(user,new VerificationClock()));}
        await using var db=Context();await db.Database.MigrateAsync();
        var client=new Client {CompanyName="Journal verification",AccountCode="JOURNAL"};var facility=new Facility {Name="Journal verification",FacilityCode="JOURNAL"};db.Clients.Add(client);db.Facilities.Add(facility);await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var service=new AnalyticsProjectionService(db,NullLogger<AnalyticsProjectionService>.Instance);var day=new DateTime(2026,11,1,0,0,0,DateTimeKind.Utc);
        var events=Enumerable.Range(0,100).Select(_=>Guid.NewGuid()).ToArray();counter.Count=0;var timer=Stopwatch.StartNew();
        for(int i=0;i<events.Length;i++)await service.ProcessOrderReceivedAsync(events[i],i+1,facility.Id,client.Id,day,1);
        timer.Stop();var sequentialMilliseconds=timer.Elapsed.TotalMilliseconds;var sequentialSql=counter.Count;
        var before=await Fingerprint(db);var count=await db.AnalyticsIngestionJournals.CountAsync();if(count!=100)throw new InvalidOperationException("Journal events missing.");
        timer.Restart();counter.Count=0;
        await Task.WhenAll(Enumerable.Range(0,10).Select(async i=>{await using var context=Context();var worker=new AnalyticsProjectionService(context,NullLogger<AnalyticsProjectionService>.Instance);await worker.ProcessOrderReceivedAsync(events[i],i+1,facility.Id,client.Id,day,1);}));timer.Stop();
        if(before!=await Fingerprint(db)||await db.AnalyticsIngestionJournals.CountAsync()!=100)throw new InvalidOperationException("Concurrent duplicate ingestion changed projection.");
        Console.WriteLine(JsonSerializer.Serialize(new{JournalDatabase=name,Events=100,SequentialMilliseconds=sequentialMilliseconds,SequentialSql=sequentialSql,ConcurrentDuplicateContexts=10,ConcurrentMilliseconds=timer.Elapsed.TotalMilliseconds,ConcurrentSql=counter.Count,Locking="Global process semaphore and PostgreSQL transaction advisory lock serialize generic ingestion/replay"}));
        db.ChangeTracker.Clear();var summaries=await db.DailyFacilityProductivitySummaries.ToListAsync();foreach(var row in summaries)row.OrdersReceivedCount=-100;await db.SaveChangesAsync();
        await service.ReplayProjectionsAsync(AnalyticsProjectionService.FacilityProductivityProjection,ProjectionReplayMode.FromBeginning);
        if(before!=await Fingerprint(db))throw new InvalidOperationException("Replay equivalence failed.");
        await service.ReplayProjectionsAsync(AnalyticsProjectionService.FacilityProductivityProjection,ProjectionReplayMode.FromBeginning);
        if(before!=await Fingerprint(db))throw new InvalidOperationException("Repeated replay failed.");
        var journal=await db.AnalyticsIngestionJournals.FirstAsync();journal.ArgumentsJson="tamper";try{await db.SaveChangesAsync();throw new InvalidOperationException("Journal update unexpectedly allowed.");}catch(InvalidOperationException ex)when(!ex.Message.Contains("unexpectedly")){db.ChangeTracker.Clear();}
        Console.WriteLine("PASS PostgreSQL durable journal equivalence, repeated rebuild, duplicate ingestion and immutable update rejection");
    }
    private static async Task<string> Fingerprint(ApplicationDbContext db)=>JsonSerializer.Serialize(await db.DailyFacilityProductivitySummaries.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.FacilityId,x.DateUtc,x.TimeGrain,x.OrdersReceivedCount}).ToListAsync());
    private sealed class Counter:DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default){Interlocked.Increment(ref Count);return ValueTask.FromResult(result);}
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<int> result,CancellationToken cancellationToken=default){Interlocked.Increment(ref Count);return ValueTask.FromResult(result);}
    }
}
