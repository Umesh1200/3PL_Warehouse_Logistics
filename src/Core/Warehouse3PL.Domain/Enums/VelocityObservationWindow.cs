namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Configurable observation time window for SKU velocity, movement frequency, and turnover aggregation.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public enum VelocityObservationWindow
{
    Days30 = 30,
    Days90 = 90,
    Days180 = 180,
    Days365 = 365
}

