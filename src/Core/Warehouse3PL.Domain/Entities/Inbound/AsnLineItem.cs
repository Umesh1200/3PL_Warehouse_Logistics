using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;

namespace Warehouse3PL.Domain.Entities.Inbound;

/// <summary>
/// Manifest SKU line item within an Advance Shipping Notice.
/// DB-SPEC-3PL-2026 Module 5 (asn_line_items).
/// </summary>
public class AsnLineItem : BaseEntity<Guid>
{
    public Guid AsnId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public Guid ItemId { get; set; }
    public decimal ExpectedQty { get; set; }
    public decimal ReceivedQty { get; set; } = 0m;
    public string? ExpectedLotNumber { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public Guid? PackagingUnitId { get; set; }
    public string? Notes { get; set; }

    // Navigations
    public AdvanceShippingNotice AdvanceShippingNotice { get; set; } = null!;
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
    public Item Item { get; set; } = null!;
    public PackagingUnit? PackagingUnit { get; set; }

    public AsnLineItem()
    {
        Id = Guid.NewGuid();
    }
}

