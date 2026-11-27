using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Application.Common.Models;

public class OutboundOrderDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public Guid? DestinationFacilityId { get; set; }
    public string? DestinationFacilityName { get; set; }

    public Guid? CustomerId { get; set; }
    public string? CustomerCode { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;
    public string? ExternalOrderReference { get; set; }

    public OutboundOrderType OrderType { get; set; }
    public OutboundOrderStatus Status { get; set; }
    public OutboundOrderStatus? StatusBeforeHold { get; set; }

    public int Priority { get; set; }
    public CarrierServiceLevel ServiceLevel { get; set; }
    public string? CarrierCode { get; set; }

    public string? DestinationAddress { get; set; }
    public DateTime OrderDateUtc { get; set; }
    public DateTime? RequiredShipDateUtc { get; set; }
    public DateTime? EarliestShipDateUtc { get; set; }

    public string? HoldReason { get; set; }
    public DateTime? HeldAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? Notes { get; set; }

    public uint Version { get; set; }
    public DateTime CreatedAt { get; set; }

    public ShippingAddressDto? ShippingAddress { get; set; }
    public List<OutboundOrderLineDto> Lines { get; set; } = [];
    public List<OrderStatusHistoryDto> StatusHistories { get; set; } = [];
}

public class OutboundOrderLineDto
{
    public Guid Id { get; set; }
    public Guid OutboundOrderId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;

    public Guid? PackagingUnitId { get; set; }
    public string? PackagingUnitName { get; set; }

    public int LineNumber { get; set; }
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityAllocated { get; set; }
    public decimal QuantityShipped { get; set; }
    public decimal RemainingDemand { get; set; }

    public string? RequiredLotNumber { get; set; }
    public DateTime? MinExpiryDateUtc { get; set; }
    public OutboundOrderLineStatus Status { get; set; }
    public string? Notes { get; set; }
}

public class ShippingAddressDto
{
    public string RecipientName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string StateOrProvince { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public Warehouse3PL.Domain.Enums.CountryNormalizationStatus CountryNormalizationStatus { get; set; } = Warehouse3PL.Domain.Enums.CountryNormalizationStatus.PendingNormalization;
    public string? PhoneNumber { get; set; }
    public string? DeliveryInstructions { get; set; }

    public bool IsAmended { get; set; }
    public string? OriginalAddressSnapshot { get; set; }
    public string? AmendmentReason { get; set; }
    public DateTime? AmendedAtUtc { get; set; }
}

public class OrderStatusHistoryDto
{
    public OutboundOrderStatus FromStatus { get; set; }
    public OutboundOrderStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}

public class StockAvailabilityAssessmentDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public bool IsFullyAvailable { get; set; }
    public decimal OverallFulfillmentPercent { get; set; }
    public List<LineStockAvailabilityDto> Lines { get; set; } = [];
}

public class LineStockAvailabilityDto
{
    public Guid LineId { get; set; }
    public int LineNumber { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public decimal QuantityRequested { get; set; }
    public decimal QuantityAvailable { get; set; }
    public decimal ShortageQuantity { get; set; }
    public bool IsFullyAvailable { get; set; }
    public string? RequiredLotNumber { get; set; }
    public DateTime? MinExpiryDateUtc { get; set; }
}

public class OrderImportBatchDto
{
    public Guid BatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int SuccessfulRows { get; set; }
    public int FailedRows { get; set; }
    public int DuplicateRows { get; set; }
    public ImportBatchStatus Status { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public List<OrderImportBatchRowDto> Rows { get; set; } = [];
}

public class OrderImportBatchRowDto
{
    public int RowNumber { get; set; }
    public string? ExternalOrderReference { get; set; }
    public bool IsSuccess { get; set; }
    public bool IsDuplicate { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? CreatedOutboundOrderId { get; set; }
}

