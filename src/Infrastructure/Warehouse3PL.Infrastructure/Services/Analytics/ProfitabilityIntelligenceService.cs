using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
namespace Warehouse3PL.Infrastructure.Services.Analytics;
public sealed class ProfitabilityIntelligenceService(IApplicationDbContext db, TransportationAnalyticsAuthorization authorization) : IProfitabilityIntelligenceService
{
    public async Task<ProfitabilityReportDto> GetReportAsync(DateTime start, DateTime end, Guid? clientId, Guid? facilityId, CancellationToken ct = default)
    {
        start = UtcDate(start); end = UtcDate(end);
        if (clientId == Guid.Empty || facilityId == Guid.Empty || end < start || (end-start).TotalDays > 365 || end.Year == 9999 || start < DateTime.MinValue.AddDays((end-start).Days+1))
            throw new ValidationException("range", "Use nonempty scope identifiers and an ordered range of at most 366 days with an available previous period.");
        var scope = await authorization.ResolveAsync(clientId, facilityId, ct);
        var previousEnd = start.AddDays(-1); var previousStart = start.AddDays(-(end-start).Days-1);
        // Nullable facility projections require explicit predicates; TransportationQueryScope.Apply expects nonnullable FacilityId.
        var query = db.DailyClientProfitabilitySummaries.AsNoTracking().Where(x => x.FacilityId.HasValue && x.TimeGrain == TimeGrain.Daily && x.DateUtc >= previousStart && x.DateUtc < end.AddDays(1));
        if (scope.ClientId.HasValue) query = query.Where(x => x.ClientId == scope.ClientId);
        if (scope.FacilityId.HasValue) query = query.Where(x => x.FacilityId == scope.FacilityId);
        if (scope.AuthorizedFacilities != null) query = query.Where(x => scope.AuthorizedFacilities.Contains(x.FacilityId!.Value));
        var rows = await query.ToListAsync(ct);
        IReadOnlyList<ProfitabilityContributionDto> Summarize(bool current) => rows.Where(x => current ? x.DateUtc >= start : x.DateUtc < start)
            .GroupBy(x => new { x.ClientId, FacilityId=x.FacilityId!.Value, x.Currency })
            .OrderBy(g => g.Key.ClientId).ThenBy(g => g.Key.FacilityId).ThenBy(g => g.Key.Currency, StringComparer.Ordinal)
            .Select(g => { var storage=g.Sum(x=>x.BilledStorageRevenue); var handling=g.Sum(x=>x.BilledHandlingRevenue); var freight=g.Sum(x=>x.BilledFreightRevenue); var vas=g.Sum(x=>x.BilledVasRevenue);
                var revenue=storage+handling+freight+vas; var carrier=g.Sum(x=>x.CarrierFreightCost); var labor=g.Sum(x=>x.DirectLaborEstimatedCost); var cost=carrier+labor;
                return new ProfitabilityContributionDto(g.Key.ClientId,g.Key.FacilityId,g.Key.Currency,g.Select(x=>x.DateUtc.Date).Distinct().Count(),storage,handling,freight,vas,revenue,carrier,labor,cost,revenue-cost,revenue>0?Math.Round((revenue-cost)/revenue*100,2):null,revenue>0?Math.Round(cost/revenue*100,2):null); }).ToList();
        return new(start,end,previousStart,previousEnd,Summarize(true),Summarize(false));
    }
    private static DateTime UtcDate(DateTime value) => (value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc)).Date;
}
