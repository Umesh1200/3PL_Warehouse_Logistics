namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Configurable dimension used to execute Pareto / ABC stratification.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public enum AbcClassificationCriteria
{
    AnnualConsumptionValue = 1,
    PickFrequency = 2,
    UnitVelocity = 3,
    OrderLineContribution = 4,
    WeightedComposite = 5
}

