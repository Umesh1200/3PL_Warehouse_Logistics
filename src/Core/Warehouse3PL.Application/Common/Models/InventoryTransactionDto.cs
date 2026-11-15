using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class InventoryTransactionDto
{
    public long TxId { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }

    public Guid? FromLocationId { get; set; }
    public string? FromLocationCode { get; set; }
    public Guid? ToLocationId { get; set; }
    public string? ToLocationCode { get; set; }

    public InventoryTxType TxType { get; set; }
    public decimal Quantity { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public Guid? UserId { get; set; }
    public string? Notes { get; set; }
    public DateTime TxTimestamp { get; set; }
}

