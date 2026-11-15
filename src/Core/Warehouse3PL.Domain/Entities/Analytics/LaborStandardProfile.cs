using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Engineered labor standard configuration defining baseline and variable task duration benchmarks.
/// DB-SPEC-3PL-2026 Phase 6.2 Labor, Productivity & Workforce Pacing Analytics.
/// </summary>
public class LaborStandardProfile : AuditableEntity<Guid>
{
    public Guid? FacilityId { get; set; }
    public Guid? ZoneId { get; set; }
    public WarehouseZoneType? ZoneType { get; set; }
    public WarehouseLaborActivityType ActivityType { get; set; }
    public string Department { get; set; } = string.Empty;

    public decimal BaseSetupSeconds { get; set; }
    public decimal SecondsPerUnit { get; set; }
    public decimal SecondsPerLine { get; set; }
    public decimal SecondsPerCarton { get; set; }
    public decimal SecondsPerPallet { get; set; }
    public decimal SecondsPerTravelMeter { get; set; }

    public decimal EquipmentFactor { get; set; } = 1.0m;
    public decimal ComplexityMultiplier { get; set; } = 1.0m;

    public int Version { get; set; } = 1;
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public LaborStandardProfile()
    {
        Id = Guid.NewGuid();
    }

    public static LaborStandardProfile Create(
        WarehouseLaborActivityType activityType,
        string department,
        decimal baseSetupSeconds,
        decimal secondsPerUnit = 0m,
        decimal secondsPerLine = 0m,
        decimal secondsPerCarton = 0m,
        decimal secondsPerPallet = 0m,
        decimal secondsPerTravelMeter = 0m,
        Guid? facilityId = null,
        Guid? zoneId = null,
        WarehouseZoneType? zoneType = null,
        decimal equipmentFactor = 1.0m,
        decimal complexityMultiplier = 1.0m,
        int version = 1,
        DateTime? effectiveFromUtc = null)
    {
        if (baseSetupSeconds < 0m || secondsPerUnit < 0m || secondsPerLine < 0m || secondsPerCarton < 0m || secondsPerPallet < 0m || secondsPerTravelMeter < 0m)
            throw new InvalidDomainStateException("Standard time durations cannot be negative.");

        if (equipmentFactor <= 0m || complexityMultiplier <= 0m)
            throw new InvalidDomainStateException("Equipment factor and complexity multiplier must be positive non-zero values.");

        if (version < 1)
            throw new InvalidDomainStateException("Profile version must be a positive integer.");

        return new LaborStandardProfile
        {
            FacilityId = facilityId,
            ZoneId = zoneId,
            ZoneType = zoneType,
            ActivityType = activityType,
            Department = string.IsNullOrWhiteSpace(department) ? activityType.ToString() : department.Trim(),
            BaseSetupSeconds = baseSetupSeconds,
            SecondsPerUnit = secondsPerUnit,
            SecondsPerLine = secondsPerLine,
            SecondsPerCarton = secondsPerCarton,
            SecondsPerPallet = secondsPerPallet,
            SecondsPerTravelMeter = secondsPerTravelMeter,
            EquipmentFactor = equipmentFactor,
            ComplexityMultiplier = complexityMultiplier,
            Version = version,
            EffectiveFromUtc = effectiveFromUtc ?? DateTime.UtcNow,
            IsActive = true
        };
    }

    /// <summary>
    /// Calculates the engineered standard duration in seconds for a specific task volume, travel distance, and complexity.
    /// Formula: StandardSeconds = [BaseSetup + (Units * SecUnit) + (Lines * SecLine) + (Cartons * SecCarton) + (Pallets * SecPallet) + (TravelMeters * SecMeter)] * Equip * Comp
    /// </summary>
    public decimal CalculateStandardDurationSeconds(
        int units,
        int lines,
        int cartons,
        int pallets,
        decimal travelDistanceMeters = 0m,
        decimal? customEquipmentFactor = null,
        decimal? customComplexityMultiplier = null)
    {
        var rawVariableSeconds = (Math.Max(0, units) * SecondsPerUnit)
                               + (Math.Max(0, lines) * SecondsPerLine)
                               + (Math.Max(0, cartons) * SecondsPerCarton)
                               + (Math.Max(0, pallets) * SecondsPerPallet)
                               + (Math.Max(0m, travelDistanceMeters) * SecondsPerTravelMeter);

        var totalBaseAndVariable = BaseSetupSeconds + rawVariableSeconds;
        var equip = customEquipmentFactor.HasValue && customEquipmentFactor.Value > 0m ? customEquipmentFactor.Value : EquipmentFactor;
        var comp = customComplexityMultiplier.HasValue && customComplexityMultiplier.Value > 0m ? customComplexityMultiplier.Value : ComplexityMultiplier;

        var standardSeconds = totalBaseAndVariable * equip * comp;
        return Math.Max(1.0m, Math.Round(standardSeconds, 2, MidpointRounding.AwayFromZero));
    }
}

