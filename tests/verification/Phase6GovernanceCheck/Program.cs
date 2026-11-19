using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Warehouse3PL.Infrastructure.Persistence;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Entities.Outbound;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Infrastructure.Services.Analytics;
using Warehouse3PL.Infrastructure.Persistence.Interceptors;

var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PHASE6_VERIFICATION_CONNECTION")
    ?? throw new InvalidOperationException("Set PHASE6_VERIFICATION_CONNECTION to a disposable local server."));
if (connection.Host is not ("127.0.0.1" or "localhost")) throw new InvalidOperationException("Local PostgreSQL required.");
if(args.Contains("--replay")){await JournalPostgresVerification.Run(connection.ConnectionString);return;}
if(args.Contains("--benchmark")){await ProfitabilityBenchmark.Run(connection.ConnectionString,args.Contains("--large")?200:20);return;}
connection.Database="postgres";
await using var admin=new NpgsqlConnection(connection.ConnectionString);
await admin.OpenAsync();
var suffix=Guid.NewGuid().ToString("N");
foreach (var scenario in new[]{"fresh","upgrade"})
{
    var database=$"phase6_{scenario}_{suffix}";
    await new NpgsqlCommand($"CREATE DATABASE {database}",admin).ExecuteNonQueryAsync();
    connection.Database=database;
    await using var db=CreateDb(connection.ConnectionString);
    db.Database.SetConnectionString(connection.ConnectionString);
    var migrator=db.GetService<IMigrator>();
    if(scenario=="upgrade") await migrator.MigrateAsync("20261116154753_Phase6_4_4_2_RoutingAndScopeIntegrity");
    await migrator.MigrateAsync();
    await migrator.MigrateAsync();
    var client=new Client {AccountCode="VERIFY",CompanyName="Verification"}; var facility=new Facility {FacilityCode="VERIFY",Name="Verification"};
    db.Clients.Add(client);db.Facilities.Add(facility);
    db.Shipments.Add(new Shipment {ClientId=client.Id,FacilityId=facility.Id,ShipmentNumber="VERIFY",CarrierCode="VERIFY",Status=ShipmentStatus.Dispatched,DispatchedAtUtc=DateTime.UtcNow.AddDays(-1)});
    var rule=new MonitoringRule {ClientId=client.Id,FacilityId=facility.Id,Name="Concurrent verification",Metric=MonitoringMetric.ShipmentTransitHours,Comparison=MonitoringComparison.GreaterThan,Threshold=0,Severity=MonitoringSeverity.High};
    db.MonitoringRules.Add(rule);await db.SaveChangesAsync();db.ChangeTracker.Clear();
    async Task Evaluate()
    {
        await using var context=CreateDb(connection.ConnectionString);context.Database.SetConnectionString(connection.ConnectionString);
        var identity=new VerificationIdentity();var service=new MonitoringService(context,new(context,identity),identity);
        await service.EvaluateAsync(rule.Id);
    }
    await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Evaluate()));
    if(await db.MonitoringAlerts.CountAsync(x=>x.RuleId==rule.Id)!=1)throw new InvalidOperationException("Concurrent evaluation created duplicates.");
    Console.WriteLine("PASS concurrent PostgreSQL rule evaluation: four contexts, one alert");
    var applied=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
    Console.WriteLine($"PASS {scenario}: {database}; {applied.Length} applied migrations");
}

static ApplicationDbContext CreateDb(string connectionString)
{
    var identity=new VerificationIdentity();
    var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString,n=>n.EnableRetryOnFailure()).UseSnakeCaseNamingConvention().Options;
    return new(options,new VerificationTenant(),identity,new AuditableEntityInterceptor(identity,new VerificationClock()));
}
sealed class VerificationTenant : ITenantProvider
{
 public Guid? TenantId=>null;public bool HasTenant=>false;public bool BypassTenantFilter{get;set;}=true;public void SetTenantId(Guid id){}
}
sealed class VerificationClock : IDateTimeProvider {public DateTime UtcNow=>DateTime.UtcNow;}
sealed class VerificationIdentity : ICurrentUserContext
{
 public Guid? UserId=>null;public string? Email=>null;public Guid? TenantId=>null;public Guid? FacilityId=>null;public Guid? RequestedFacilityId=>null;
 public UserType? UserType=>Warehouse3PL.Domain.Enums.UserType.Staff;
 public IReadOnlyList<string> Roles=>["SuperAdmin"];public IReadOnlyList<string> Permissions=>[];
 public bool IsAuthenticated=>true;public bool IsStaff=>true;public bool IsSuperAdmin=>true;
 public bool HasPermission(string permission)=>true;public bool IsInRole(string role)=>true;
}
