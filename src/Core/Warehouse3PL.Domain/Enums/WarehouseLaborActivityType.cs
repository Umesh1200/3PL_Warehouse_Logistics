namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational labor activity categories for engineered standard-time evaluation and workforce pacing.
/// DB-SPEC-3PL-2026 Phase 6.2 Labor, Productivity & Workforce Pacing Analytics.
/// </summary>
public enum WarehouseLaborActivityType
{
    Receiving = 1,
    Putaway = 2,
    Picking = 3,
    Packing = 4,
    StagingAndLoading = 5,
    CycleCounting = 6,
    ReturnProcessing = 7,
    KittingAndVas = 8
}

