namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Status classification for physical warehouse location spatial coordinates.
/// DB-SPEC-3PL-2026 Phase 6.3.6 Dynamic Warehouse Heatmaps.
/// </summary>
public enum HeatmapCoordinateStatus
{
    /// <summary>
    /// Coordinates are defined, valid, and within facility envelope boundaries.
    /// </summary>
    Valid = 1,

    /// <summary>
    /// Location lacks physical coordinates. Invariant: coordinates must never be fabricated.
    /// </summary>
    MissingCoordinates = 2,

    /// <summary>
    /// Location coordinates lie outside the facility's bounding spatial envelope.
    /// </summary>
    OutOfBounds = 3,

    /// <summary>
    /// Multiple distinct storage locations share the exact same spatial coordinates.
    /// </summary>
    DuplicateCoordinates = 4
}
