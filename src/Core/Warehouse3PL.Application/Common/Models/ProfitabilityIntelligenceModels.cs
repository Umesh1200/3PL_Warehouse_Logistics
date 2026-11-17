namespace Warehouse3PL.Application.Common.Models;
public sealed record ProfitabilityContributionDto(Guid ClientId, Guid FacilityId, string Currency, int ObservationDays,
    decimal StorageRevenue, decimal HandlingRevenue, decimal FreightRevenue, decimal VasRevenue,
    decimal Revenue, decimal CarrierCost, decimal EstimatedDirectLaborCost, decimal ObservedCost,
    decimal Contribution, decimal? ContributionPercent, decimal? ObservedCostToRevenuePercent);
public sealed record ProfitabilityReportDto(DateTime StartDateUtc, DateTime EndDateUtc, DateTime PreviousStartDateUtc,
    DateTime PreviousEndDateUtc, IReadOnlyList<ProfitabilityContributionDto> Current,
    IReadOnlyList<ProfitabilityContributionDto> Previous,
    string Basis = "Projected commercial contribution; direct labor is estimated. Overhead, storage operating cost and other unattributed costs are unavailable. This is not net profit. Currency groups are not combined; null-facility aggregates are excluded.")
{
    public IReadOnlyList<ProfitabilityPeriodDeltaDto> Comparisons => Current.Select(current =>
    {
        var previous = Previous.FirstOrDefault(p => p.ClientId == current.ClientId && p.FacilityId == current.FacilityId && p.Currency == current.Currency);
        return new ProfitabilityPeriodDeltaDto(current.ClientId,current.FacilityId,current.Currency,
            previous == null ? null : current.Revenue-previous.Revenue,
            previous == null ? null : current.ObservedCost-previous.ObservedCost,
            previous == null ? null : current.Contribution-previous.Contribution,
            previous?.Revenue > 0 ? Math.Round((current.Revenue-previous.Revenue)/previous.Revenue*100,2) : null);
    }).ToList();
}
public sealed record ProfitabilityPeriodDeltaDto(Guid ClientId,Guid FacilityId,string Currency,decimal? RevenueDelta,decimal? ObservedCostDelta,decimal? ContributionDelta,decimal? RevenueChangePercent);
