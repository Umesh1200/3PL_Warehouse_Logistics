using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Catalog;

/// <summary>
/// Unit of Measurement (UOM) / Packaging unit conversion factor (e.g. Case of 24 Eaches).
/// </summary>
public class PackagingUnit : BaseEntity<Guid>
{
    public Guid ItemId { get; set; }
    public PackagingUnitLevel UnitLevel { get; set; } = PackagingUnitLevel.Each;
    public string UnitName { get; set; } = "Each";
    
    /// <summary>
    /// Conversion quantity in base 'EACH' units. (e.g. Case = 24, Pallet = 1200).
    /// </summary>
    public decimal ConversionQty { get; set; } = 1m;
    
    public decimal WeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal VolumeCm3 { get; set; }
    public string? Barcode { get; set; }

    // Navigation
    public Item Item { get; set; } = null!;

    public PackagingUnit()
    {
        Id = Guid.NewGuid();
    }
}

