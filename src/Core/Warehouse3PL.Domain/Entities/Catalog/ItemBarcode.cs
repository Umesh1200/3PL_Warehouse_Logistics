using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Catalog;

/// <summary>
/// Barcodes mapped to a SKU (UPC, EAN-13, GS1-128, etc.).
/// </summary>
public class ItemBarcode : BaseEntity<Guid>
{
    public Guid ItemId { get; set; }
    public string BarcodeValue { get; set; } = string.Empty;
    public BarcodeSymbology Symbology { get; set; } = BarcodeSymbology.EAN_13;
    public bool IsPrimary { get; set; } = false;

    // Navigation
    public Item Item { get; set; } = null!;

    public ItemBarcode()
    {
        Id = Guid.NewGuid();
    }
}

