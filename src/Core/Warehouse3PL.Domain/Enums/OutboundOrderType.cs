namespace Warehouse3PL.Domain.Enums;

/// <summary>
/// Originating source and operational channel for outbound orders.
/// Phase 4.1 Order Management.
/// </summary>
public enum OutboundOrderType
{
    B2BSalesOrder = 1,
    StockTransferOrder = 2,
    EcommerceFulfillment = 3
}

