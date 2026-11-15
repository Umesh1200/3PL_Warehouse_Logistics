namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Specifies whether carrier transit duration commitments are computed across calendar days or business days.
/// DB-SPEC-3PL-2026 Phase 6.4 Transportation & Carrier SLA Analytics.
/// </summary>
public enum TransitDayCalculationType
{
    /// <summary>
    /// Calculated using all standard 24-hour calendar days including weekends and public holidays.
    /// </summary>
    CalendarDays = 1,

    /// <summary>
    /// Calculated excluding non-operational weekend days (Saturday/Sunday) and recognized carrier holidays.
    /// </summary>
    BusinessDays = 2
}
