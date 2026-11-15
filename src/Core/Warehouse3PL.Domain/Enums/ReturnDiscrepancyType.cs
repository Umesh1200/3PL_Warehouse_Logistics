namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Classification of quantity, item, or condition discrepancies identified during return receiving.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B.4.5: Return Facility Receiving &amp; Inspection.
/// </summary>
public enum ReturnDiscrepancyType
{
    /// <summary>Physically received quantity is less than expected from the original package/RMA.</summary>
    Shortage = 1,

    /// <summary>Physically received quantity exceeds expected quantity authorized for return.</summary>
    Excess = 2,

    /// <summary>Received item does not match the expected SKU authorized on the return.</summary>
    IncorrectItem = 3,

    /// <summary>Expected accessories, manuals, cables, or modular components are missing.</summary>
    MissingComponents = 4,

    /// <summary>Return parcel or item arrived without prior return authorization or RMA linkage.</summary>
    UnannouncedReturn = 5
}

