using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Evaluates country/regional/facility working days and calculates contractual transit SLA delivery dates
/// using active <see cref="Domain.Entities.Reference.FacilityCalendar"/>, <see cref="Domain.Entities.Reference.HolidayCalendar"/>,
/// and facility IANA timezones.
/// </summary>
public interface IWorkingCalendarService
{
    Task<WorkingDayEvaluationResult> EvaluateWorkingDayAsync(
        Guid facilityId,
        DateOnly localDate,
        CancellationToken cancellationToken = default);

    Task<DateTime> CalculateCommittedDeliveryDateUtcAsync(
        CarrierContractualSlaPolicy policy,
        DateTime dispatchUtc,
        CancellationToken cancellationToken = default);
}
