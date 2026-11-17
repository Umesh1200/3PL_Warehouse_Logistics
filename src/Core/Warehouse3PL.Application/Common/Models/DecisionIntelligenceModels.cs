using Warehouse3PL.Domain.Enums;
namespace Warehouse3PL.Application.Common.Models;

public sealed record LaborDecisionDto(Guid FacilityId, DateTime ObservedAtUtc, DateTime HistoryStartUtc,
    DateTime HistoryEndUtc, int OpenTasks, int UnassignedTasks, decimal RemainingUnits,
    int HistoricalExecutions, decimal ObservedUnits, decimal ObservedLaborHours,
    decimal? UnitsPerLaborHour, decimal? EstimatedLaborHoursToClear, IReadOnlyList<string> Recommendations,
    string Basis = "Picking workload and historical execution evidence. Clearance estimates are labor hours, not elapsed time or available staffing. No assignments are changed.");
public sealed record CarrierDecisionCandidateDto(string CarrierCode, int SampleSize, bool IsStatisticallyReliable,
    decimal? OnTimeDeliveryRatePercent, decimal? AverageTransitDays, int? Rank, string Explanation);
public sealed record CarrierDecisionDto(DateTime StartDateUtc, DateTime EndDateUtc, Guid? ClientId, Guid? FacilityId,
    CarrierServiceLevel? ServiceLevel, IReadOnlyList<CarrierDecisionCandidateDto> Candidates,
    string Basis = "Historical performance ranking; sufficient evidence requires a reliable sample and a defined on-time rate. This is not a quotation, route eligibility check or future SLA guarantee.");
