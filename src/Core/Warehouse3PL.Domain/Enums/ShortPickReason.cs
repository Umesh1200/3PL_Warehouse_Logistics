namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Root cause reason when an operator cannot fulfill the full requested quantity during picking.
/// Phase 4.3 Picking & Warehouse Execution.
/// </summary>
public enum ShortPickReason
{
    BinEmpty = 1,               // Zero physical inventory found in bin
    DamagedStock = 2,           // Items found are broken, crushed, leaking, or compromised
    BarcodeUnreadable = 3,      // SKU or packaging barcode label cannot be scanned
    WrongItemInBin = 4,         // Bin contains mismatching product
    LotExpired = 5,             // Stock in bin passed expiration date
    BinInaccessible = 6         // Physical obstruction or equipment maintenance locks bin
}

