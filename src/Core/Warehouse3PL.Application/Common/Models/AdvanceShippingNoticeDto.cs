using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class AdvanceShippingNoticeDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public string FacilityCode { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public Guid? PurchaseOrderId { get; set; }
    public string? PoNumber { get; set; }
    public string AsnNumber { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? CarrierName { get; set; }
    public string? TrackingReference { get; set; }
    public string? TrailerOrContainerNumber { get; set; }
    public DateTime? EstimatedArrival { get; set; }
    public DateTime? ActualArrival { get; set; }
    public AsnStatus Status { get; set; }
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public uint Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<AsnLineItemDto> Lines { get; set; } = [];
}

public class AsnLineItemDto
{
    public Guid Id { get; set; }
    public Guid AsnId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public int? PoLineNumber { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public decimal ExpectedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public string? ExpectedLotNumber { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public Guid? PackagingUnitId { get; set; }
    public string? PackagingUnitName { get; set; }
    public string? Notes { get; set; }
}

