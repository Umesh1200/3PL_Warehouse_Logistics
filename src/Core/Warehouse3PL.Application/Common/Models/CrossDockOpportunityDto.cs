using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class CrossDockOpportunityDto
{
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public StorageTempClass StorageTempClass { get; set; }

    public Guid? ReceiptLineId { get; set; }
    public string InboundLpn { get; set; } = string.Empty;
    public Guid SourceStagingLocationId { get; set; }
    public string SourceStagingLocationCode { get; set; } = string.Empty;

    public decimal InboundAvailableQty { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpirationDate { get; set; }

    public Guid MatchedOutboundOrderId { get; set; }
    public string MatchedOutboundOrderNumber { get; set; } = string.Empty;
    public Guid MatchedOutboundOrderLineId { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    public decimal OrderDemandedQty { get; set; }
    public decimal OrderRemainingDemandQty { get; set; }
    public decimal RecommendedCrossDockQty { get; set; }
    public decimal RemainingPutawayQty { get; set; }

    public bool IsTemperatureCompatible { get; set; } = true;
    public bool IsLotCompatible { get; set; } = true;
}

