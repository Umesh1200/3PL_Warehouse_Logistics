using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public string FacilityCode { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string PoNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal TotalEstimatedCost { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public uint Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<PurchaseOrderLineDto> Lines { get; set; } = [];
}

public class PurchaseOrderLineDto
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public decimal ExpectedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public PurchaseOrderLineStatus Status { get; set; }
    public string? Notes { get; set; }
}

