namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Workforce labor efficiency performance tiers based strictly on the ratio of engineered standard labor time to actual productive clock duration.
/// NOTE: These are engineered time-efficiency pacing classifications for operational capacity planning and throughput velocity;
/// they are NOT holistic human HR appraisals or punitive evaluations.
/// DB-SPEC-3PL-2026 Phase 6.2 Labor, Productivity & Workforce Pacing Analytics.
/// </summary>
public enum LaborPerformanceTier
{
    Underperforming = 1, // < 80% of standard efficiency
    Developing = 2,      // 80.0% - 94.99% of standard efficiency
    Standard = 3,        // 95.0% - 119.99% of standard efficiency
    Exceptional = 4      // >= 120.0% of standard efficiency
}

