namespace Warehouse3PL.Application.Common.Models;

public class StockOnHandDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    
    public Guid LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public string LocationBarcode { get; set; } = string.Empty;
    
    public Guid? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpirationDate { get; set; }

    public decimal QtyAvailable { get; set; }
    public decimal QtyAllocated { get; set; }
    public decimal QtyReserved { get; set; }
    public decimal QtyQuarantined { get; set; }
    public decimal QtyDamaged { get; set; }
    public decimal QtyOnHold { get; set; }
    public decimal TotalQuantity { get; set; }
    
    public DateTime? LastCountedAt { get; set; }
    public uint Version { get; set; }
}

