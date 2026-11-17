using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;
using Warehouse3PL.Infrastructure.Services.Analytics;

internal static class ProfitabilityBenchmark
{
    public static async Task Run(string connectionString,int clientCount=20)
    {
        var cs=new NpgsqlConnectionStringBuilder(connectionString){Database="postgres"};
        await using var admin=new NpgsqlConnection(cs.ConnectionString);await admin.OpenAsync();
        var name="phase68_benchmark_"+Guid.NewGuid().ToString("N");
        await new NpgsqlCommand($"CREATE DATABASE {name}",admin).ExecuteNonQueryAsync();cs.Database=name;
        var counter=new QueryCounter();
        var identity=new BenchmarkIdentity();
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString,n=>n.EnableRetryOnFailure()).UseSnakeCaseNamingConvention().AddInterceptors(counter).Options;
        await using var db=new ApplicationDbContext(options,new VerificationTenant(),identity,new AuditableEntityInterceptor(identity,new VerificationClock()));
        await db.Database.MigrateAsync();
        var clients=Enumerable.Range(0,clientCount).Select(i=>new Client {AccountCode=$"BENCH{i}",CompanyName=$"Bench{i}"}).ToArray();
        var facilities=Enumerable.Range(0,5).Select(i=>new Facility {FacilityCode=$"BENCH{i}",Name=$"Bench{i}"}).ToArray();
        db.Clients.AddRange(clients);db.Facilities.AddRange(facilities);
        var start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
        foreach(var client in clients)foreach(var facility in facilities)foreach(var day in Enumerable.Range(0,100))
            db.DailyClientProfitabilitySummaries.Add(new(){ClientId=client.Id,FacilityId=facility.Id,DateUtc=start.AddDays(day),BilledHandlingRevenue=100,CarrierFreightCost=20,DirectLaborEstimatedCost=10});
        foreach(var client in clients)db.DailyClientProfitabilitySummaries.Add(new(){ClientId=client.Id,FacilityId=null,DateUtc=start,BilledHandlingRevenue=999999});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        foreach(var narrow in new[]{false,true})
        {
            identity.Tenant=narrow?clients[0].Id:null;identity.Assigned=narrow?facilities[0].Id:null;
            if(narrow){db.ClientFacilityAccesses.Add(new(){ClientId=clients[0].Id,FacilityId=facilities[0].Id,Status=Warehouse3PL.Domain.Enums.FacilityAccessStatus.Active});await db.SaveChangesAsync();db.ChangeTracker.Clear();}
            var svc=new ProfitabilityIntelligenceService(db,new(db,identity));
            var samples=new List<double>();var counts=new List<int>();var allocations=new List<long>();
            for(int run=0;run<8;run++)
            {
                counter.Count=0;var before=GC.GetTotalAllocatedBytes(false);var timer=Stopwatch.StartNew();
                var report=await svc.GetReportAsync(start,start.AddDays(99),null,null);
                timer.Stop();
                var expected=narrow?10000m:clientCount*50000m;
                if(report.Current.Sum(x=>x.Revenue)!=expected)throw new InvalidOperationException("Benchmark scope arithmetic failed.");
                if(report.Current.Any(x=>x.FacilityId==Guid.Empty))throw new InvalidOperationException("Aggregate row leaked.");
                if(run>0){samples.Add(timer.Elapsed.TotalMilliseconds);counts.Add(counter.Count);allocations.Add(GC.GetTotalAllocatedBytes(false)-before);}
            }
            samples.Sort();Console.WriteLine(JsonSerializer.Serialize(new {Benchmark="profitability",Database=name,Rows=clientCount*501,Scope=narrow?"client+facility":"platform",Samples=7,Warmups=1,MedianMilliseconds=samples[3],P95Milliseconds=samples[^1],SqlCounts=counts,MedianAllocatedBytes=allocations.Order().ElementAt(3),Runtime=Environment.Version.ToString(),LogicalProcessors=Environment.ProcessorCount}));
        }
        if(await db.DailyClientProfitabilitySummaries.CountAsync()!=clientCount*501)throw new InvalidOperationException("Read benchmark changed data.");
    }
    private sealed class QueryCounter:DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default){Count++;return ValueTask.FromResult(result);}
    }
    private sealed class BenchmarkIdentity:ICurrentUserContext
    {
        public Guid? Tenant;public Guid? Assigned;
        public Guid? UserId=>null;public string? Email=>null;public Guid? TenantId=>Tenant;public Guid? FacilityId=>Assigned;
        public Warehouse3PL.Domain.Enums.UserType? UserType=>Tenant.HasValue?Warehouse3PL.Domain.Enums.UserType.Client:Warehouse3PL.Domain.Enums.UserType.Staff;
        public IReadOnlyList<string> Roles=>Tenant.HasValue?[]:["SuperAdmin"];public IReadOnlyList<string> Permissions=>[];
        public bool IsAuthenticated=>true;public bool IsStaff=>!Tenant.HasValue;public bool IsSuperAdmin=>!Tenant.HasValue;
        public bool HasPermission(string permission)=>!Tenant.HasValue;public bool IsInRole(string role)=>!Tenant.HasValue;
    }
}
