using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Analytics;

namespace Warehouse3PL.Application.Common.Interfaces;

/// <summary>
/// Evaluates country/regional/facility working days and calculates contractual transit SLA delivery dates
/// using active <see cref="Domain.Entities.Reference.FacilityCalendar"/>, <see cref="Domain.Entities.Reference.HolidayCalendar"/>,
/// and facility/policy IANA timezones.
/// <para>
/// Contractual SLA semantics:
/// 1. Origin pickup &amp; daily cutoff timing use the configured <c>CutoffTimeZone</c> (or origin facility IANA timezone
///    when policy cutoff timezone is unset/<c>UTC</c>) and origin facility working calendar/holidays.
/// 2. Business-day transit &amp; destination delivery commitments use <c>DestinationCountry</c> (and <c>DestinationStateOrRegion</c>)
///    holidays/rest rules for cross-border lanes, or the bound facility calendar for domestic lanes.
/// 3. Ambiguous <see cref="DateTimeKind.Unspecified"/> timestamps and invalid configured timezones fail closed.
/// </para>
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
