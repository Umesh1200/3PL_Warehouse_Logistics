namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Inventory disposition outcomes assigned upon warehouse receiving inspection of returned goods.
/// DB-SPEC-3PL-2026 Phase 4.5 Step 3B: Delivery Exception Workflows &amp; Returns Management.
/// </summary>
public enum ReturnDisposition
{
    /// <summary>Merchandise inspected and verified in pristine, new condition. Returned to sellable stock.</summary>
    RestockToAvailable = 1,

    /// <summary>Minor packaging wear; routed to secondary / refurbished inventory grade.</summary>
    RestockToRefurbished = 2,

    /// <summary>Damaged merchandise moved to quarantine location pending vendor claim or repair.</summary>
    QuarantineDamaged = 3,

    /// <summary>Unsalvageable merchandise authorized for physical scrap and write-off.</summary>
    ScrapOrDestroy = 4,

    /// <summary>Merchandise designated for cross-docking or return-to-vendor (RTV) freight transfer.</summary>
    ReturnToVendor = 5,

    /// <summary>Quarantine / further technical inspection or testing required before disposition.</summary>
    QuarantineForQA = 6,

    /// <summary>Incorrect item received or critical components missing; cannot be restocked.</summary>
    IncorrectItemOrMissing = 7
}

