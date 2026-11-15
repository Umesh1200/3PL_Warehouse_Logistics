using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Inbound;

/// <summary>
/// Detailed SKU line item within a client Purchase Order.
/// </summary>
public class PurchaseOrderLine : BaseEntity<Guid>
{
    public Guid PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    public Guid ItemId { get; set; }
    public decimal ExpectedQty { get; set; }
    public decimal ReceivedQty { get; set; } = 0m;
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public PurchaseOrderLineStatus Status { get; set; } = PurchaseOrderLineStatus.Open;
    public string? Notes { get; set; }

    // Navigations
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public Item Item { get; set; } = null!;

    public PurchaseOrderLine()
    {
        Id = Guid.NewGuid();
    }
}

