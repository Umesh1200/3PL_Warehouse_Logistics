using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class ReceiptLineDto
{
    public Guid Id { get; set; }
    public Guid ReceivingSessionId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public Guid? PurchaseOrderLineId { get; set; }
    public Guid? AsnLineItemId { get; set; }
    public string Lpn { get; set; } = string.Empty;
    public decimal QuantityReceived { get; set; }
    public decimal QuantityExpected { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public decimal? CatchWeightKg { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public ReceiptConditionStatus Condition { get; set; }
    public Guid? StagingLocationId { get; set; }
    public string? StagingLocationCode { get; set; }
    public string? Notes { get; set; }
    public DateTime ScannedAtUtc { get; set; }
    public uint Version { get; set; }
}

