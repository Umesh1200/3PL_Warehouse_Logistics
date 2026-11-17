using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
namespace Warehouse3PL.Infrastructure.Services.Analytics;

public sealed class DecisionIntelligenceService(IApplicationDbContext db, TransportationAnalyticsAuthorization authorization,
    ICurrentUserContext user, ICarrierScorecardService scorecards) : IDecisionIntelligenceService
{
    private static (DateTime Start, DateTime End) Window(DateTime start, DateTime end)
    {
        start = (start.Kind == DateTimeKind.Local ? start.ToUniversalTime() : start).Date;
        end = (end.Kind == DateTimeKind.Local ? end.ToUniversalTime() : end).Date;
        if (end < start || (end-start).TotalDays > 365 || end.Year == 9999)
            throw new ValidationException("dates", "Use an ordered window of at most 366 UTC calendar days before year 9999.");
        return (DateTime.SpecifyKind(start, DateTimeKind.Utc), DateTime.SpecifyKind(end, DateTimeKind.Utc));
    }
    public async Task<LaborDecisionDto> GetLaborAdviceAsync(Guid facilityId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated) throw new UnauthorizedException("Authentication required.");
        if (!user.IsSuperAdmin && user.UserType != UserType.Staff) throw new ForbiddenException("Facility labor advice requires staff access.");
        if (facilityId == Guid.Empty) throw new ValidationException("facilityId", "A facility is required.");
        var (start,end) = Window(startDateUtc,endDateUtc);
        await authorization.ResolveAsync(null,facilityId,ct);
        var now=DateTime.UtcNow;
        var tasks=await db.PickTasks.AsNoTracking().Where(x=>x.FacilityId==facilityId&&!x.IsDeleted&&
            (x.Status==PickTaskStatus.Pending||x.Status==PickTaskStatus.Assigned||x.Status==PickTaskStatus.InProgress))
            .Select(x=>new {x.QuantityToPick,x.QuantityPicked,x.AssignedUserId}).ToListAsync(ct);
        // Workforce execution logs have no client identity: facility advice is staff-only.
        var history=await db.WorkerTaskExecutionLogs.AsNoTracking().Where(x=>x.FacilityId==facilityId&&
            x.ActivityType==WarehouseLaborActivityType.Picking&&x.StartedAtUtc>=start&&x.CompletedAtUtc<end.AddDays(1)&&
            x.CompletedAtUtc<=now&&x.CompletedAtUtc>x.StartedAtUtc&&x.ActualDurationSeconds>0&&x.UnitsProcessed>0)
            .Select(x=>new {x.UnitsProcessed,x.ActualDurationSeconds}).ToListAsync(ct);
        var remaining=tasks.Sum(x=>Math.Max(0,x.QuantityToPick-x.QuantityPicked));
        var hours=history.Sum(x=>x.ActualDurationSeconds)/3600m; var units=history.Sum(x=>(decimal)x.UnitsProcessed);
        decimal? rate=hours>0?units/hours:null;
        var unassigned=tasks.Count(x=>x.AssignedUserId==null);
        var advice=new List<string>();
        if(unassigned>0) advice.Add($"Review assignment of {unassigned} open picking tasks before moving staff from other work.");
        if(remaining>0) advice.Add("Prioritize the observed picking backlog; confirm worker qualifications, availability and other stage demand before rebalancing.");
        if(rate==null) advice.Add("Insufficient positive historical picking execution evidence for a clearance estimate.");
        if(tasks.Count==0) advice.Add("No open picking tasks observed; no picking rebalancing is recommended.");
        return new(facilityId,now,start,end,tasks.Count,unassigned,remaining,history.Count,units,hours,rate,
            rate>0?remaining/rate:null,advice);
    }
    public async Task<CarrierDecisionDto> GetCarrierAdviceAsync(IReadOnlyList<string> carrierCodes, DateTime startDateUtc, DateTime endDateUtc,
        Guid? clientId, Guid? facilityId, CarrierServiceLevel? serviceLevel, CancellationToken ct = default)
    {
        var (start,end)=Window(startDateUtc,endDateUtc);
        if(clientId==Guid.Empty||facilityId==Guid.Empty||serviceLevel.HasValue&&!Enum.IsDefined(serviceLevel.Value)||
            carrierCodes==null||carrierCodes.Count<1||carrierCodes.Count>20||carrierCodes.Any(x=>string.IsNullOrWhiteSpace(x)||!Regex.IsMatch(x,"^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$")))
            throw new ValidationException("candidates","Supply 1..20 valid carrier codes, valid scope identifiers and service level.");
        var scope=await authorization.ResolveAsync(clientId,facilityId,ct);
        var rows=new List<CarrierDecisionCandidateDto>();
        foreach(var code in carrierCodes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.Ordinal))
        {
            var s=await scorecards.GetCarrierScorecardAsync(code,start,end,scope.FacilityId,scope.ClientId,serviceLevel,ct);
            var eligible=s.IsStatisticallyReliable&&s.SampleSize>0&&s.OnTimeDeliveryRatePercent.HasValue;
            rows.Add(new(code,s.SampleSize,s.IsStatisticallyReliable,s.OnTimeDeliveryRatePercent,s.AverageTransitDays,null,
                eligible?"Eligible for historical performance comparison.":"Insufficient reliable historical evidence; unranked."));
        }
        var ranked=rows.Where(x=>x.IsStatisticallyReliable&&x.SampleSize>0&&x.OnTimeDeliveryRatePercent.HasValue)
            .OrderByDescending(x=>x.OnTimeDeliveryRatePercent).ThenBy(x=>x.AverageTransitDays??decimal.MaxValue).ThenBy(x=>x.CarrierCode,StringComparer.Ordinal)
            .Select((x,i)=>x with {Rank=i+1}).ToList();
        return new(start,end,scope.ClientId,scope.FacilityId,serviceLevel,ranked.Concat(rows.Where(x=>!ranked.Any(r=>r.CarrierCode==x.CarrierCode))).ToList());
    }
}
