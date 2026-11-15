using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Analytics;

/// <summary>
/// Physical coordinate placement of an item stack on a storage location floor.
/// Models 2D collision geometry, orientation, and vertical tiering.
/// </summary>
public class VirtualPlacement
{
    public Guid ItemId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal LengthCm { get; init; }
    public decimal WidthCm { get; init; }
    public decimal HeightCm { get; init; }
    public decimal XCoordCm { get; set; }
    public decimal YCoordCm { get; set; }
    public PlanarOrientation Orientation { get; set; } = PlanarOrientation.Normal;
    public int VerticalTierCount { get; init; }
    public int StackabilityFactor { get; init; }
    public bool IsExistingPhysicalStock { get; set; }

    /// <summary>
    /// Effective length along X axis based on orientation.
    /// </summary>
    public decimal FootprintLengthCm => Orientation == PlanarOrientation.Normal ? LengthCm : WidthCm;

    /// <summary>
    /// Effective width along Y axis based on orientation.
    /// </summary>
    public decimal FootprintWidthCm => Orientation == PlanarOrientation.Normal ? WidthCm : LengthCm;

    /// <summary>
    /// Evaluates 2D AABB bounding-box intersection with another placement.
    /// Two rectangles overlap if they intersect with strictly non-zero area.
    /// Touching at an edge or corner does not constitute an overlap.
    /// </summary>
    public bool Overlaps(VirtualPlacement other)
    {
        return Overlaps(other.XCoordCm, other.YCoordCm, other.FootprintLengthCm, other.FootprintWidthCm);
    }

    /// <summary>
    /// Evaluates 2D AABB intersection against specified rectangular bounds.
    /// </summary>
    public bool Overlaps(decimal x, decimal y, decimal len, decimal wid)
    {
        return XCoordCm < x + len &&
               x < XCoordCm + FootprintLengthCm &&
               YCoordCm < y + wid &&
               y < YCoordCm + FootprintWidthCm;
    }
}
