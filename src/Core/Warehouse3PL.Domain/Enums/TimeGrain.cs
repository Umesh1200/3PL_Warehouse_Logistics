namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Temporal aggregation grain for business intelligence and analytics read models.
/// DB-SPEC-3PL-2026 Phase 6.0 Analytics Foundation &amp; KPI Engine.
/// </summary>
public enum TimeGrain
{
    Hourly = 1,
    Daily = 2,
    Weekly = 3,
    Monthly = 4,
    Quarterly = 5,
    Yearly = 6
}

