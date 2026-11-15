namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Physical, environmental, operational, and regulatory constraints evaluated during warehouse slotting.
/// </summary>
public enum SlottingConstraintType
{
    /// <summary>
    /// SKU unit dimensions (Length, Width, Height) must not exceed location physical dimensions.
    /// </summary>
    PhysicalDimensions = 1,

    /// <summary>
    /// Total SKU stock volume must not exceed usable location cubic capacity.
    /// </summary>
    VolumetricCapacity = 2,

    /// <summary>
    /// Total SKU stock weight must not exceed location maximum load capacity (kg).
    /// </summary>
    WeightCapacity = 3,

    /// <summary>
    /// Product temperature classification (Ambient, Refrigerated, Frozen) must match zone temperature limits.
    /// </summary>
    TemperatureClass = 4,

    /// <summary>
    /// Hazardous and flammable products must be segregated according to safety regulations.
    /// </summary>
    HazardSegregation = 5,

    /// <summary>
    /// Vertical stacking factor must not exceed product stackability factor or location height.
    /// </summary>
    Stackability = 6,

    /// <summary>
    /// Strict tenant exclusivity: client-dedicated locations cannot store competing client SKUs.
    /// </summary>
    ClientTenancy = 7,

    /// <summary>
    /// Target location must be active, pickable, and not damaged, locked, or undergoing maintenance.
    /// </summary>
    OperationalLocationStatus = 8,

    /// <summary>
    /// Location type compatibility (e.g., pallet racks vs bins vs cantilevers).
    /// </summary>
    LocationTypeCompatibility = 9
}
