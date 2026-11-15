namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Permitted orthogonal orientations of a rectangular item on a warehouse floor/shelf tier.
/// </summary>
public enum PlanarOrientation
{
    /// <summary>
    /// Item length aligned along X axis, width aligned along Y axis (0 degrees).
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Item length aligned along Y axis, width aligned along X axis (90 degrees).
    /// </summary>
    Rotated = 1
}
