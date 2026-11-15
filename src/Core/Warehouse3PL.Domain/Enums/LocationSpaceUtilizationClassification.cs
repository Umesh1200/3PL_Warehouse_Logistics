namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational capacity classification of warehouse storage locations.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public enum LocationSpaceUtilizationClassification
{
    Empty = 1,
    Underutilized = 2,
    Optimal = 3,
    NearCapacity = 4,
    FullOrOverfilled = 5,
    OverCapacity = 6,
    BlockedOrUnavailable = 7
}

