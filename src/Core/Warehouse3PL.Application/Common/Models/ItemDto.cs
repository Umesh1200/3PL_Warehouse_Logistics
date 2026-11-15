using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class ItemDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    
    public decimal UnitPrice { get; set; }
    public decimal WeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal VolumeCm3 { get; set; }
    
    public AbcVelocity AbcVelocity { get; set; }
    public StorageTempClass StorageTempClass { get; set; }
    public string? HazardClass { get; set; }
    
    public bool IsLotTracked { get; set; }
    public bool IsSerialized { get; set; }
    public bool RequiresExpiryDate { get; set; }
    public int ShelfLifeDays { get; set; }
    public ItemStatus Status { get; set; }
    
    public List<string> Barcodes { get; set; } = [];
    public List<PackagingUnitDto> PackagingUnits { get; set; } = [];
}

public class PackagingUnitDto
{
    public Guid Id { get; set; }
    public PackagingUnitLevel UnitLevel { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionQty { get; set; }
    public decimal WeightKg { get; set; }
    public decimal VolumeCm3 { get; set; }
    public string? Barcode { get; set; }
}

