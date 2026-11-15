namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Physical outer condition of a returned package upon arrival at the warehouse dock.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4.5: Return Facility Receiving &amp; Inspection.
/// </summary>
public enum ReturnPackageCondition
{
    /// <summary>Package outer carton and seals appear intact and undamaged.</summary>
    Intact = 1,

    /// <summary>Outer carton shows visible tears, punctures, or dents.</summary>
    DamagedBox = 2,

    /// <summary>Package is crushed or severely deformed.</summary>
    Crushed = 3,

    /// <summary>Outer carton tape or manufacturer seal was opened or tampered with.</summary>
    OpenedOrTampered = 4,

    /// <summary>Package shows visible liquid staining or water damage.</summary>
    WaterOrLiquidDamaged = 5
}

