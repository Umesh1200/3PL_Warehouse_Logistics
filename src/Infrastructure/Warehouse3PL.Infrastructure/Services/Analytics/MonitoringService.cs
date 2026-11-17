using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Entities.Analytics;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
namespace Warehouse3PL.Infrastructure.Services.Analytics;
public sealed class MonitoringService(IApplicationDbContext db,TransportationAnalyticsAuthorization authorization,ICurrentUserContext user) : IMonitoringService
{
    private static readonly SemaphoreSlim[] Gates=Enumerable.Range(0,256).Select(_=>new SemaphoreSlim(1,1)).ToArray();
    public async Task<MonitoringRule> CreateRuleAsync(MonitoringRuleRequest r,CancellationToken ct=default)
    {
        if(r.ClientId==Guid.Empty||r.FacilityId==Guid.Empty||string.IsNullOrWhiteSpace(r.Name)||r.Name.Length>150||!Enum.IsDefined(r.Metric)||!Enum.IsDefined(r.Comparison)||!Enum.IsDefined(r.Severity)||r.Threshold<0||r.Threshold>1000000000m)
            throw new ValidationException("rule","Provide exact scope, valid metric/comparison/severity, name up to 150 characters and threshold 0..1,000,000,000.");
        await authorization.ResolveAsync(r.ClientId,r.FacilityId,ct);
        if(!await db.Clients.AnyAsync(x=>x.Id==r.ClientId,ct)||!await db.Facilities.AnyAsync(x=>x.Id==r.FacilityId,ct)) throw new ValidationException("scope","Client and facility must exist.");
        var rule=new MonitoringRule {ClientId=r.ClientId,FacilityId=r.FacilityId,Name=r.Name.Trim(),Metric=r.Metric,Comparison=r.Comparison,Threshold=r.Threshold,Severity=r.Severity,Enabled=r.Enabled};
        db.MonitoringRules.Add(rule); await db.SaveChangesAsync(ct); return rule;
    }
    public async Task<IReadOnlyList<MonitoringRule>> GetRulesAsync(Guid? clientId,Guid? facilityId,CancellationToken ct=default)
    {
        var scope=await authorization.ResolveAsync(clientId,facilityId,ct);
        return await scope.Apply(db.MonitoringRules.AsNoTracking()).OrderBy(x=>x.Name).ThenBy(x=>x.Id).Take(1000).ToListAsync(ct);
    }
    private async Task<MonitoringRule> Rule(Guid id,CancellationToken ct)
    {
        var scope=await authorization.ResolveAsync(null,null,ct);
        return await scope.Apply(db.MonitoringRules).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new EntityNotFoundException(nameof(MonitoringRule),id);
    }
    public async Task<MonitoringRule> SetEnabledAsync(Guid id,bool enabled,CancellationToken ct=default)
    {var r=await Rule(id,ct);return await WithRuleLock(r,async()=>{r.Enabled=enabled;await db.SaveChangesAsync(ct);return r;},ct);}
    public async Task<MonitoringRule> UpdateRuleAsync(Guid id,MonitoringRuleRequest request,CancellationToken ct=default)
    {
        var r=await Rule(id,ct);
        if(request.ClientId!=r.ClientId||request.FacilityId!=r.FacilityId||request.Metric!=r.Metric||!Enum.IsDefined(request.Comparison)||!Enum.IsDefined(request.Severity)||request.Threshold<0||request.Threshold>1000000000m||string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>150)
            throw new ValidationException("rule","Scope/metric are immutable; supply valid name, comparison, severity and threshold.");
        return await WithRuleLock(r,async()=>{r.Name=request.Name.Trim();r.Comparison=request.Comparison;r.Threshold=request.Threshold;r.Severity=request.Severity;r.Enabled=request.Enabled;await db.SaveChangesAsync(ct);return r;},ct);
    }
    public async Task<MonitoringEvaluationResult> EvaluateAsync(Guid id,CancellationToken ct=default)
    {var rule=await Rule(id,ct);return await EvaluateScopedAsync(rule,ct);}
    // Internal worker entry: loads each persisted exact scope itself; never accepts caller-selected bypass scope.
    internal async Task EvaluateScheduledAsync(CancellationToken ct)
    {
        Guid? after=null;
        while(true){var query=db.MonitoringRules.IgnoreQueryFilters().Where(x=>x.Enabled);if(after.HasValue)query=query.Where(x=>x.Id.CompareTo(after.Value)>0);var rules=await query.OrderBy(x=>x.Id).Take(100).ToListAsync(ct);if(rules.Count==0)break;foreach(var rule in rules)await EvaluateScopedAsync(rule,ct);after=rules[^1].Id;db.ClearChangeTracker();}
    }
    private Task<MonitoringEvaluationResult> EvaluateScopedAsync(MonitoringRule rule,CancellationToken ct)=>WithRuleLock(rule,()=>EvaluateCoreAsync(rule,ct),ct);
    private async Task<T> WithRuleLock<T>(MonitoringRule rule,Func<Task<T>> action,CancellationToken ct)
    {
        var gate=Gates[(int)((uint)rule.Id.GetHashCode()%256)]; await gate.WaitAsync(ct);
        try
        {
            if(db is Warehouse3PL.Infrastructure.Persistence.ApplicationDbContext relational && relational.Database.IsRelational())
            {
                var attempt=0;
                return await relational.Database.CreateExecutionStrategy().ExecuteAsync(()=>db.ExecuteInTransactionAsync(async()=>{
                    if(attempt++>0){relational.ChangeTracker.Clear();relational.Attach(rule);}
                    // PostgreSQL transaction advisory locks serialize rule state across API/worker instances.
                    var key=BitConverter.ToInt64(rule.Id.ToByteArray(),0);
                    await relational.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})",ct);
                    await relational.Entry(rule).ReloadAsync(ct);
                    return await action();
                },ct));
            }
            return await action();
        } finally {gate.Release();}
    }
    private async Task<MonitoringEvaluationResult> EvaluateCoreAsync(MonitoringRule rule,CancellationToken ct)
    {
        if(!rule.Enabled) throw new ValidationException("rule","Disabled rules cannot be evaluated.");
        var now=DateTime.UtcNow;var cutoff=now.AddDays(-366);
        var scope=new TransportationQueryScope(rule.ClientId,rule.FacilityId,null);
        var observations=new List<(string Key,decimal Value)>();
        switch(rule.Metric)
        {
            case MonitoringMetric.StockAvailable:
            case MonitoringMetric.DamagedStockPercent:
                var stocks=await scope.Apply(db.StockOnHand.IgnoreQueryFilters().AsNoTracking()).ToListAsync(ct);
                foreach(var s in stocks) { if(rule.Metric==MonitoringMetric.StockAvailable) observations.Add(($"stock:{s.Id}",s.QtyAvailable)); else if(s.TotalQuantity>0) observations.Add(($"stock:{s.Id}",s.QtyDamaged/s.TotalQuantity*100)); } break;
            case MonitoringMetric.ShipmentTransitHours:
                var shipments=await scope.Apply(db.Shipments.IgnoreQueryFilters().AsNoTracking()).Where(x=>!x.IsDeleted&&x.Status==ShipmentStatus.Dispatched&&x.DeliveredAtUtc==null&&x.DispatchedAtUtc!=null&&x.DispatchedAtUtc<=now).ToListAsync(ct);
                observations.AddRange(shipments.Select(s=>($"shipment:{s.Id}",(decimal)(now-s.DispatchedAtUtc!.Value).TotalHours))); break;
            case MonitoringMetric.SlaLateHours:
                var sla=await scope.Apply(db.CarrierSlaComplianceRecords.IgnoreQueryFilters().AsNoTracking()).Where(x=>x.DeliveredAtUtc==null&&x.EstimatedDeliveryDateUtc!=null&&x.EstimatedDeliveryDateUtc>=cutoff&&x.EstimatedDeliveryDateUtc<=now&&x.SlaPolicyId!=null&&x.EvaluationStatus==SlaEvaluationStatus.Evaluated&&db.Shipments.IgnoreQueryFilters().Any(s=>s.Id==x.ShipmentId&&s.ClientId==x.ClientId&&s.FacilityId==x.FacilityId&&!s.IsDeleted&&s.DeliveredAtUtc==null)).ToListAsync(ct);
                observations.AddRange(sla.Where(x=>x.DeliveredAtUtc==null||x.DeliveredAtUtc<=now).Select(s=>($"sla:{s.Id}",Math.Max(0,(decimal)((s.DeliveredAtUtc??now)-s.EstimatedDeliveryDateUtc!.Value).TotalHours)))); break;
            case MonitoringMetric.FreightVariancePercent:
                var costs=await scope.Apply(db.CarrierFreightCostSummaries.IgnoreQueryFilters().AsNoTracking()).Where(x=>x.DateUtc>=cutoff&&x.DateUtc<=now&&x.TimeGrain==TimeGrain.Daily&&x.TotalQuotedAmountUsd>0&&x.TotalShipmentsQuotedCount>0&&x.TotalShipmentsBilledCount>0).ToListAsync(ct);
                observations.AddRange(costs.Select(s=>($"freight:{s.Id}",(s.TotalBilledAmountUsd-s.TotalQuotedAmountUsd)/s.TotalQuotedAmountUsd*100))); break;
        }
        var existing=await scope.Apply(db.MonitoringAlerts.IgnoreQueryFilters()).Where(x=>x.RuleId==rule.Id).ToListAsync(ct);
        int created=0,reopened=0,breaches=0;
        foreach(var (key,value) in observations)
        {
            bool breach=rule.Comparison==MonitoringComparison.GreaterThan?value>rule.Threshold:value<rule.Threshold;
            var alert=existing.SingleOrDefault(x=>x.EvidenceKey==key);
            if(!breach){if(alert!=null&&alert.Status!=MonitoringAlertStatus.Resolved){alert.Status=MonitoringAlertStatus.Resolved;alert.ResolvedAtUtc=now;alert.ResolutionNotes="Threshold no longer breached.";}continue;}
            breaches++;
            if(alert==null){alert=new(){RuleId=rule.Id,ClientId=rule.ClientId,FacilityId=rule.FacilityId,EvidenceKey=key,FirstObservedAtUtc=now};db.MonitoringAlerts.Add(alert);created++;}
            else if(alert.Status==MonitoringAlertStatus.Resolved){alert.Status=MonitoringAlertStatus.Active;alert.AcknowledgedBy=null;alert.AcknowledgedAtUtc=null;alert.ResolvedAtUtc=null;alert.ResolutionNotes="";reopened++;}
            alert.LastObservedAtUtc=now;alert.ObservedValue=value;alert.Severity=rule.Severity;alert.Explanation=$"{rule.Metric}: {value} {rule.Comparison} {rule.Threshold}. Advisory evidence only.";
        }
        await db.SaveChangesAsync(ct);
        return new(observations.Count,breaches,created,reopened,"Current scoped source observations; missing sources/denominators produce no observation. Freight compares projected billed/quoted cohorts and is indicative, not invoice audit.");
    }
    public async Task<MonitoringAlertPage> GetAlertsAsync(Guid? clientId,Guid? facilityId,int page,int size,CancellationToken ct=default)
    {
        if(page<1||page>1000000||size<1||size>200)throw new ValidationException("page","Page must be 1..1,000,000 and size 1..200.");
        var scope=await authorization.ResolveAsync(clientId,facilityId,ct);var q=scope.Apply(db.MonitoringAlerts.AsNoTracking());
        return new(await q.OrderBy(x=>x.Status).ThenByDescending(x=>x.Severity).ThenByDescending(x=>x.LastObservedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*size).Take(size).ToListAsync(ct),await q.CountAsync(ct),page,size);
    }
    public async Task<MonitoringAlert> TransitionAsync(Guid id,bool resolve,string notes,CancellationToken ct=default)
    {
        notes??="";
        if(notes.Length>1000||(resolve&&string.IsNullOrWhiteSpace(notes)))throw new ValidationException("notes","Resolution requires notes up to 1000 characters.");
        var scope=await authorization.ResolveAsync(null,null,ct);var alert=await scope.Apply(db.MonitoringAlerts).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new EntityNotFoundException(nameof(MonitoringAlert),id);
        var rule=await Rule(alert.RuleId,ct);
        return await WithRuleLock(rule,async()=>{
        if(db is Warehouse3PL.Infrastructure.Persistence.ApplicationDbContext context&&context.Database.IsRelational()){if(context.Entry(alert).State==EntityState.Detached)context.Attach(alert);await context.Entry(alert).ReloadAsync(ct);}
        if(resolve){alert.Status=MonitoringAlertStatus.Resolved;alert.ResolvedAtUtc=DateTime.UtcNow;alert.ResolutionNotes=notes.Trim();}
        else if(alert.Status!=MonitoringAlertStatus.Resolved){alert.Status=MonitoringAlertStatus.Acknowledged;if(alert.AcknowledgedAtUtc==null){alert.AcknowledgedBy=user.UserId;alert.AcknowledgedAtUtc=DateTime.UtcNow;}}
        else throw new ValidationException("status","Resolved alerts cannot be acknowledged; a fresh breach reopens them.");
        await db.SaveChangesAsync(ct);return alert;
        },ct);
    }
}
