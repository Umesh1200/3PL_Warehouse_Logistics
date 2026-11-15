using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Configurable warehouse slotting policy governing SKU-to-location placement recommendations.
/// Invariant: Slotting policies and recommendations are advisory simulations only and never mutate
/// operational stock ledger records (StockOnHand) or physical locations until an authorized WMS relocation workflow executes.
/// Phase 6.3.7 ABC-Based Intelligent Warehouse Slotting Optimization.
/// </summary>
public class SlottingPolicy : AuditableEntity<Guid>
{
    public Guid? FacilityId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? ZoneId { get; set; }

    public string Name { get; set; } = "Standard Warehouse Slotting Policy";
    public string? Description { get; set; }

    public SlottingOptimizationGoal PrimaryGoal { get; set; } = SlottingOptimizationGoal.ErgonomicGoldenZone;
    public SlottingOptimizationGoal? SecondaryGoal { get; set; } = SlottingOptimizationGoal.MinimizeTravelDistance;

    // Ergonomic Golden Zone Boundaries (VIS-01 elevation awareness: waist-to-shoulder height)
    public decimal GoldenZoneMinZMeters { get; set; } = 0.80m;
    public decimal GoldenZoneMaxZMeters { get; set; } = 1.50m;
    public string GoldenZoneShelfLevels { get; set; } = "1,2";

    // Velocity & ABC Tier Mapping Directives
    public bool TargetClassAPickFace { get; set; } = true;
    public bool TargetClassBIntermediate { get; set; } = true;
    public bool TargetClassCReserve { get; set; } = true;

    /// <summary>
    /// Safeguard: Dead stock and slow-moving inventory must not automatically be relocated
    /// simply because of a low velocity classification unless resolving critical overcapacity.
    /// </summary>
    public bool ProtectDeadStockFromUnnecessaryRelocation { get; set; } = true;

    // Movement Disruption & Cost Dampening Controls
    public int MaxRelocationsPerBatch { get; set; } = 50;
    public decimal MinLaborSavingsUsd { get; set; } = 5.00m;
    public decimal EstimatedRelocationLaborMinutes { get; set; } = 6.00m;
    public decimal LaborHourlyRateUsd { get; set; } = 25.00m;
    public decimal DisruptionPenaltyWeight { get; set; } = 0.20m;

    // Composite Weights for 5 Soft Scoring Dimensions (summing to 1.0)
    public decimal WeightTravelDistance { get; set; } = 0.25m;
    public decimal WeightErgonomics { get; set; } = 0.25m;
    public decimal WeightPickDensity { get; set; } = 0.15m;
    public decimal WeightAbcAffinity { get; set; } = 0.20m;
    public decimal WeightVelocityAffinity { get; set; } = 0.15m;
    public decimal WeightCubeFill { get; set; } = 0.0m;
    public decimal WeightProductAffinity { get; set; } = 0.0m;

    // Phase 6.3.7.4 Multi-Factor Relocation Prioritization Weights (summing to 1.0)
    public decimal WeightFinancialBenefit { get; set; } = 0.35m;
    public decimal WeightOperationalUrgency { get; set; } = 0.30m;
    public decimal WeightPlacementSuitability { get; set; } = 0.20m;
    public decimal WeightDisruptionPenalty { get; set; } = 0.15m;

    // Phase 6.3.7.4 Transparent Economic & Relocation Cost Parameters
    public string NegativeNetBenefitPolicy { get; set; } = "AllowIfHighUrgency";
    public decimal UrgencyThresholdForNegativeBenefit { get; set; } = 70.0m;
    public decimal TargetMonthlySavingsUsd { get; set; } = 50.0m;
    public decimal MaxAcceptableRelocationCostUsd { get; set; } = 100.0m;
    public decimal MaxAcceptableDisruptionCostUsd { get; set; } = 30.0m;
    public bool UseTotalRelocationCostForDisruptionPenalty { get; set; } = false;
    public decimal HandlingCostPerUnitUsd { get; set; } = 0.10m;
    public decimal HeavyHandlingSurchargePerTierUsd { get; set; } = 5.00m;
    public decimal BaseDisruptionCostUsd { get; set; } = 15.00m;
    public decimal GoldenZoneOpportunityGainUsd { get; set; } = 8.50m;
    public decimal AmortizationMonths { get; set; } = 3.0m;

    /// <summary>
    /// Maximum allowable age (in days) for authoritative velocity projections before flagging staleness and applying catalog fallback.
    /// </summary>
    public int MaxProjectionAgeDays { get; set; } = 30;

    public int Version { get; set; } = 1;
    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public SlottingPolicy()
    {
        Id = Guid.NewGuid();
    }

    public static SlottingPolicy Create(
        string name,
        Guid? facilityId = null,
        Guid? clientId = null,
        Guid? zoneId = null,
        SlottingOptimizationGoal primaryGoal = SlottingOptimizationGoal.ErgonomicGoldenZone,
        SlottingOptimizationGoal? secondaryGoal = SlottingOptimizationGoal.MinimizeTravelDistance,
        decimal goldenZoneMinZMeters = 0.80m,
        decimal goldenZoneMaxZMeters = 1.50m,
        string goldenZoneShelfLevels = "1,2",
        bool targetClassAPickFace = true,
        bool targetClassBIntermediate = true,
        bool targetClassCReserve = true,
        bool protectDeadStock = true,
        int maxRelocationsPerBatch = 50,
        decimal minLaborSavingsUsd = 5.00m,
        decimal estimatedRelocationLaborMinutes = 6.00m,
        decimal laborHourlyRateUsd = 25.00m,
        decimal disruptionPenaltyWeight = 0.20m,
        decimal weightTravelDistance = 0.25m,
        decimal weightErgonomics = 0.25m,
        decimal weightPickDensity = 0.15m,
        decimal weightAbcAffinity = 0.20m,
        decimal weightVelocityAffinity = 0.15m,
        decimal weightCubeFill = 0.0m,
        decimal weightProductAffinity = 0.0m,
        int maxProjectionAgeDays = 30,
        int version = 1,
        DateTime? effectiveFromUtc = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException(nameof(name), "Slotting policy name cannot be empty.");

        if (goldenZoneMinZMeters < 0m || goldenZoneMaxZMeters <= goldenZoneMinZMeters)
            throw new ValidationException(nameof(goldenZoneMinZMeters), "Golden zone boundaries must be non-negative with MaxZ greater than MinZ.");

        if (maxRelocationsPerBatch <= 0 || maxRelocationsPerBatch > 500)
            throw new ValidationException(nameof(maxRelocationsPerBatch), "Max relocations per batch must be between 1 and 500.");

        if (estimatedRelocationLaborMinutes <= 0m)
            throw new ValidationException(nameof(estimatedRelocationLaborMinutes), "Estimated relocation labor minutes must be greater than zero.");

        if (laborHourlyRateUsd <= 0m)
            throw new ValidationException(nameof(laborHourlyRateUsd), "Labor hourly rate must be greater than zero.");

        if (primaryGoal == SlottingOptimizationGoal.WeightedComposite)
        {
            if (weightTravelDistance < 0m || weightErgonomics < 0m || weightPickDensity < 0m ||
                weightAbcAffinity < 0m || weightVelocityAffinity < 0m || weightCubeFill < 0m || weightProductAffinity < 0m)
                throw new ValidationException("Weights", "Composite weights cannot be negative.");

            decimal sum = weightTravelDistance + weightErgonomics + weightPickDensity + weightAbcAffinity + weightVelocityAffinity + weightCubeFill + weightProductAffinity;
            if (Math.Abs(sum - 1.0m) > 0.05m && Math.Abs(sum - 100.0m) > 0.5m)
                throw new ValidationException("Weights", "Composite goal weights must sum to 1.0 (or 100%).");

            if (sum > 10.0m)
            {
                weightTravelDistance /= sum;
                weightErgonomics /= sum;
                weightPickDensity /= sum;
                weightAbcAffinity /= sum;
                weightVelocityAffinity /= sum;
                weightCubeFill /= sum;
                weightProductAffinity /= sum;
            }
        }

        return new SlottingPolicy
        {
            Name = name.Trim(),
            FacilityId = facilityId,
            ClientId = clientId,
            ZoneId = zoneId,
            PrimaryGoal = primaryGoal,
            SecondaryGoal = secondaryGoal,
            GoldenZoneMinZMeters = goldenZoneMinZMeters,
            GoldenZoneMaxZMeters = goldenZoneMaxZMeters,
            GoldenZoneShelfLevels = string.IsNullOrWhiteSpace(goldenZoneShelfLevels) ? "1,2" : goldenZoneShelfLevels.Trim(),
            TargetClassAPickFace = targetClassAPickFace,
            TargetClassBIntermediate = targetClassBIntermediate,
            TargetClassCReserve = targetClassCReserve,
            ProtectDeadStockFromUnnecessaryRelocation = protectDeadStock,
            MaxRelocationsPerBatch = maxRelocationsPerBatch,
            MinLaborSavingsUsd = minLaborSavingsUsd,
            EstimatedRelocationLaborMinutes = estimatedRelocationLaborMinutes,
            LaborHourlyRateUsd = laborHourlyRateUsd,
            DisruptionPenaltyWeight = disruptionPenaltyWeight,
            WeightTravelDistance = weightTravelDistance,
            WeightErgonomics = weightErgonomics,
            WeightPickDensity = weightPickDensity,
            WeightAbcAffinity = weightAbcAffinity,
            WeightVelocityAffinity = weightVelocityAffinity,
            WeightCubeFill = weightCubeFill,
            WeightProductAffinity = weightProductAffinity,
            MaxProjectionAgeDays = maxProjectionAgeDays > 0 ? maxProjectionAgeDays : 30,
            Version = version > 0 ? version : 1,
            EffectiveFromUtc = effectiveFromUtc ?? DateTime.UtcNow,
            Description = description?.Trim(),
            IsActive = true
        };
    }
}
