namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Lifecycle status of an outbound customer shipping carton/package.
/// Phase 4.4 Packing & Dispatch Preparation.
/// </summary>
public enum PackageStatus
{
    Open = 1,          // Open at packing bench receiving scanned items
    Packed = 2,        // Sealed, gross weight and dimensions recorded
    LabelPrinted = 3,  // Shipping label generated and attached
    Staged = 4,        // Moved to outbound staging / dock door lane
    Loaded = 5,        // Loaded onto carrier vehicle / trailer
    Cancelled = 6      // Aborted / unpacked back into warehouse inventory
}

