using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Configurable Pareto / ABC inventory stratification policy.
/// Supports facility-specific, client-specific, and global scoping with configurable threshold cutoffs
/// (e.g., Class A top 80%, Class B next 15%, Class C remaining 5%), multi-criteria, and versioning.
/// Invariant: ABC policies and classifications are advisory analytics only and never mutate physical inventory ledger or allocation records.
/// DB-SPEC-3PL-2026 Phase 6.3 Inventory Health & Space Utilization.
/// </summary>
public class AbcClassificationPolicy : AuditableEntity<Guid>
{
    public Guid? FacilityId { get; set; }
    public Guid? ClientId { get; set; }
    public string Name { get; set; } = "Standard ABC Classification Policy";
    public string? Description { get; set; }

    public AbcClassificationCriteria Criteria { get; set; } = AbcClassificationCriteria.AnnualConsumptionValue;

    /// <summary>
    /// Cumulative Pareto threshold percentage for Class A items (e.g. 80.00%).
    /// </summary>
    public decimal ClassACumulativeThresholdPercent { get; set; } = 80.00m;

    /// <summary>
    /// Cumulative Pareto threshold percentage for Class B items (e.g. 95.00%).
    /// Class C covers items exceeding this threshold up to 100.00%.
    /// </summary>
    public decimal ClassBCumulativeThresholdPercent { get; set; } = 95.00m;

    public int ObservationWindowDays { get; set; } = 365;
    public VelocityObservationWindow ObservationWindow { get; set; } = VelocityObservationWindow.Days365;

    public InventoryValuationBasis CostValuationBasis { get; set; } = InventoryValuationBasis.UnitCost;

    // Multi-criteria weights for WeightedComposite criteria (must sum to 1.0)
    public decimal WeightAcv { get; set; } = 0.40m;
    public decimal WeightPickFrequency { get; set; } = 0.30m;
    public decimal WeightUnitVelocity { get; set; } = 0.20m;
    public decimal WeightOrderLines { get; set; } = 0.10m;

    public int Version { get; set; } = 1;
    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public AbcClassificationPolicy()
    {
        Id = Guid.NewGuid();
    }

    public static AbcClassificationPolicy Create(
        string name,
        Guid? facilityId = null,
        Guid? clientId = null,
        AbcClassificationCriteria criteria = AbcClassificationCriteria.AnnualConsumptionValue,
        decimal classAThresholdPercent = 80.00m,
        decimal classBThresholdPercent = 95.00m,
        int observationWindowDays = 365,
        VelocityObservationWindow observationWindow = VelocityObservationWindow.Days365,
        InventoryValuationBasis costBasis = InventoryValuationBasis.UnitCost,
        decimal weightAcv = 0.40m,
        decimal weightPickFrequency = 0.30m,
        decimal weightUnitVelocity = 0.20m,
        decimal weightOrderLines = 0.10m,
        int version = 1,
        DateTime? effectiveFromUtc = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException(nameof(name), "Policy name cannot be empty.");

        if (classAThresholdPercent <= 0m || classAThresholdPercent >= 100m)
            throw new ValidationException(nameof(classAThresholdPercent), "Class A cumulative threshold must be strictly between 0 and 100 percent.");

        if (classBThresholdPercent <= classAThresholdPercent || classBThresholdPercent > 100m)
            throw new ValidationException(nameof(classBThresholdPercent), "Class B cumulative threshold must be greater than Class A threshold and at most 100 percent.");

        if (observationWindowDays <= 0)
            throw new ValidationException(nameof(observationWindowDays), "Observation window days must be greater than zero.");

        if (criteria == AbcClassificationCriteria.WeightedComposite)
        {
            if (weightAcv < 0m || weightPickFrequency < 0m || weightUnitVelocity < 0m || weightOrderLines < 0m)
                throw new ValidationException("Weights", "Composite criterion weights cannot be negative.");

            decimal totalWeight = weightAcv + weightPickFrequency + weightUnitVelocity + weightOrderLines;
            if (Math.Abs(totalWeight - 1.0m) > 0.05m && Math.Abs(totalWeight - 100.0m) > 0.5m)
                throw new ValidationException("Weights", "Composite criterion weights must sum to 1.0 (or 100%).");

            // Normalize weights to 1.0 if entered as percentages
            if (totalWeight > 10.0m)
            {
                weightAcv /= totalWeight;
                weightPickFrequency /= totalWeight;
                weightUnitVelocity /= totalWeight;
                weightOrderLines /= totalWeight;
            }
        }

        return new AbcClassificationPolicy
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            ClientId = clientId,
            Name = name.Trim(),
            Description = description?.Trim(),
            Criteria = criteria,
            ClassACumulativeThresholdPercent = classAThresholdPercent,
            ClassBCumulativeThresholdPercent = classBThresholdPercent,
            ObservationWindowDays = observationWindowDays,
            ObservationWindow = observationWindow,
            CostValuationBasis = costBasis,
            WeightAcv = weightAcv,
            WeightPickFrequency = weightPickFrequency,
            WeightUnitVelocity = weightUnitVelocity,
            WeightOrderLines = weightOrderLines,
            Version = version,
            EffectiveFromUtc = effectiveFromUtc ?? DateTime.UtcNow,
            IsActive = true
        };
    }

    /// <summary>
    /// Classifies an item into Class A, B, or C based on its cumulative Pareto percentage.
    /// </summary>
    public AbcClassificationCategory ClassifyCategory(decimal cumulativePercentage)
    {
        if (cumulativePercentage <= ClassACumulativeThresholdPercent)
            return AbcClassificationCategory.ClassA;

        if (cumulativePercentage <= ClassBCumulativeThresholdPercent)
            return AbcClassificationCategory.ClassB;

        return AbcClassificationCategory.ClassC;
    }
}

