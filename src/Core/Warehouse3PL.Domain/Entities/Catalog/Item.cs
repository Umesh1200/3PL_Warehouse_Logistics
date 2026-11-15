using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Catalog;

/// <summary>
/// Master SKU / Product Catalog Entity.
/// Tenant-partitioned by ClientId.
/// </summary>
public class Item : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    
    // Physical Characteristics & Storage Requirements
    public decimal UnitPrice { get; set; }
    public decimal WeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal VolumeCm3 { get; set; }
    
    // Classifications
    public AbcVelocity AbcVelocity { get; set; } = AbcVelocity.B;
    public StorageTempClass StorageTempClass { get; set; } = StorageTempClass.Ambient;
    public string? HazardClass { get; set; }
    public int StackabilityFactor { get; set; } = 3;
    public bool AllowPlanarRotation { get; set; } = true;
    
    // Tracking Directives
    public bool IsLotTracked { get; set; } = false;
    public bool IsSerialized { get; set; } = false;
    public bool RequiresExpiryDate { get; set; } = false;
    public int ShelfLifeDays { get; set; } = 0;
    
    public ItemStatus Status { get; set; } = ItemStatus.Active;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public ICollection<ItemBarcode> Barcodes { get; set; } = [];
    public ICollection<PackagingUnit> PackagingUnits { get; set; } = [];
    public ICollection<ItemCategoryMapping> CategoryMappings { get; set; } = [];

    public Item()
    {
        Id = Guid.NewGuid();
    }

    public void RecalculateVolume()
    {
        VolumeCm3 = LengthCm * WidthCm * HeightCm;
    }
}

