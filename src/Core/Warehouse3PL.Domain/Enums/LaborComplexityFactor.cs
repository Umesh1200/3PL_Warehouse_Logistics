namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Operational complexity factors adjusting baseline standard time calculations.
/// DB-SPEC-3PL-2026 Phase 6.2 Labor, Productivity & Workforce Pacing Analytics.
/// </summary>
public enum LaborComplexityFactor
{
    Standard = 1,        // 1.00x multiplier
    Fragile = 2,         // 1.25x multiplier
    Hazmat = 3,          // 1.50x multiplier
    HeavyOverweight = 4, // 1.40x multiplier
    HighSecurity = 5     // 1.30x multiplier
}

