using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Outbound demand aggregate root representing sales orders, stock transfers, or e-commerce fulfillment orders.
/// Governs order lifecycle, carrier specifications, destination snapshot, and audit history.
/// DB-SPEC-3PL-2026 Phase 4.1 Order Management.
/// </summary>
public class OutboundOrder : AuditableEntity<Guid>, ITenantEntity, ISoftDeletable
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    /// <summary>
    /// Destination warehouse facility when OrderType is StockTransferOrder.
    /// </summary>
    public Guid? DestinationFacilityId { get; set; }

    /// <summary>
    /// Optional reference to client customer master entity.
    /// </summary>
    public Guid? CustomerId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;
    public string? ExternalOrderReference { get; set; }

    public OutboundOrderType OrderType { get; set; } = OutboundOrderType.B2BSalesOrder;
    public OutboundOrderStatus Status { get; set; } = OutboundOrderStatus.Released;

    /// <summary>
    /// Carrier delivery milestone rolled up across all order packages.
    /// Strictly separated from Status (which represents warehouse fulfillment lifecycle).
    /// Phase 4.5 Outbound Shipment Tracking & Delivery Confirmation.
    /// </summary>
    public OrderDeliveryStatus DeliveryStatus { get; set; } = OrderDeliveryStatus.PendingShipment;
    public DateTime? DeliveryStatusUpdatedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }

    /// <summary>
    /// Preserves the active operational status prior to an administrative/credit hold.
    /// When released, the order is restored to this status rather than being forced to Released.
    /// </summary>
    public OutboundOrderStatus? StatusBeforeHold { get; set; }

    public int Priority { get; set; } = 1; // 1 = Normal, 2 = High, 3 = Critical

    public CarrierServiceLevel ServiceLevel { get; set; } = CarrierServiceLevel.StandardGround;
    public string? CarrierCode { get; set; }

    // Fast-lookup denormalized fields (kept in sync with Customer/ShippingAddress)
    public string CustomerName { get; set; } = string.Empty;
    public string? DestinationAddress { get; set; }

    public DateTime OrderDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RequiredShipDateUtc { get; set; }
    public DateTime? EarliestShipDateUtc { get; set; }

    public string? HoldReason { get; set; }
    public DateTime? HeldAtUtc { get; set; }

    public string? CancellationReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Idempotency key from the most recent allocation request to prevent duplicate reservation on retried requests.
    /// </summary>
    public string? AllocationIdempotencyKey { get; set; }

    /// <summary>
    /// Concurrency token for optimistic locking on status changes, allocation, and line mutations.
    /// </summary>
    public uint Version { get; set; } = 1;

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public Facility? DestinationFacility { get; set; }
    public Customer? Customer { get; set; }
    public ShippingAddress? ShippingAddress { get; set; }

    public ICollection<OutboundOrderLine> Lines { get; set; } = [];
    public ICollection<OrderStatusHistory> StatusHistories { get; set; } = [];
    public ICollection<InventoryAllocation> Allocations { get; set; } = [];
    public ICollection<AllocationShortage> Shortages { get; set; } = [];
    public ICollection<Package> Packages { get; set; } = [];
    public ICollection<ShipmentDeliveryException> DeliveryExceptions { get; set; } = [];
    public ICollection<ReturnShipment> ReturnShipments { get; set; } = [];

    public bool IsStockTransfer => OrderType == OutboundOrderType.StockTransferOrder;
    public bool IsOnHold => Status == OutboundOrderStatus.OnHold;
    public bool CanBeAllocated => (Status == OutboundOrderStatus.Released || Status == OutboundOrderStatus.PartiallyAllocated) && !IsOnHold;

    public OutboundOrder()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Associates the initial immutable destination snapshot for this order.
    /// </summary>
    public void SetShippingAddress(ShippingAddress address)
    {
        ShippingAddress = address;
        ShippingAddress.OutboundOrderId = Id;
        DestinationAddress = address.ToFormattedSingleLine();
    }

    /// <summary>
    /// Validates stock transfer specific requirements.
    /// </summary>
    public void ValidateStockTransferRules()
    {
        if (OrderType == OutboundOrderType.StockTransferOrder)
        {
            if (!DestinationFacilityId.HasValue || DestinationFacilityId.Value == Guid.Empty)
            {
                throw new DomainException($"Stock Transfer Order '{OrderNumber}' must specify a valid DestinationFacilityId.");
            }

            if (DestinationFacilityId.Value == FacilityId)
            {
                throw new DomainException($"Stock Transfer Order '{OrderNumber}' source and destination facilities must be different.");
            }
        }
    }

    /// <summary>
    /// Places the order on hold, recording the prior status so it can be accurately restored.
    /// An on-hold order is disqualified from allocation candidate pools.
    /// </summary>
    public void Hold(string reason, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason must be provided when placing an order on hold.");
        }

        if (Status == OutboundOrderStatus.Completed)
        {
            throw new InvalidDomainStateException($"Cannot place completed order '{OrderNumber}' on hold.");
        }

        if (Status == OutboundOrderStatus.Cancelled)
        {
            throw new InvalidDomainStateException($"Cannot place cancelled order '{OrderNumber}' on hold.");
        }

        if (Status == OutboundOrderStatus.OnHold)
        {
            throw new InvalidDomainStateException($"Order '{OrderNumber}' is already on hold.");
        }

        var fromStatus = Status;
        StatusBeforeHold = Status;
        Status = OutboundOrderStatus.OnHold;
        HoldReason = reason;
        HeldAtUtc = DateTime.UtcNow;

        StatusHistories.Add(new OrderStatusHistory
        {
            OutboundOrderId = Id,
            FromStatus = fromStatus,
            ToStatus = OutboundOrderStatus.OnHold,
            Reason = reason,
            ChangedByUserId = userId,
            ChangedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Releases an order from hold, restoring it to its pre-hold status.
    /// </summary>
    public void ReleaseHold(Guid userId, string? notes = null)
    {
        if (Status != OutboundOrderStatus.OnHold)
        {
            throw new InvalidDomainStateException($"Order '{OrderNumber}' is not currently on hold (Current status: {Status}).");
        }

        var restoredStatus = StatusBeforeHold ?? OutboundOrderStatus.Released;
        var fromStatus = Status;

        Status = restoredStatus;
        StatusBeforeHold = null;
        HoldReason = null;
        HeldAtUtc = null;

        StatusHistories.Add(new OrderStatusHistory
        {
            OutboundOrderId = Id,
            FromStatus = fromStatus,
            ToStatus = restoredStatus,
            Reason = "Released from hold",
            Notes = notes,
            ChangedByUserId = userId,
            ChangedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Cancels the outbound order and all open lines.
    /// Disallowed if fulfillment has already commenced on the warehouse floor (InProgress) or finished (Completed).
    /// </summary>
    public void Cancel(string reason, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException(nameof(reason), "A reason must be provided when cancelling an order.");
        }

        if (Status == OutboundOrderStatus.InProgress)
        {
            throw new InvalidDomainStateException($"Cannot cancel order '{OrderNumber}' because fulfillment/picking is already InProgress on the warehouse floor.");
        }

        if (Status == OutboundOrderStatus.Completed)
        {
            throw new InvalidDomainStateException($"Cannot cancel order '{OrderNumber}' because it has already been completed/shipped.");
        }

        if (Status == OutboundOrderStatus.Cancelled)
        {
            throw new InvalidDomainStateException($"Order '{OrderNumber}' is already cancelled.");
        }

        var fromStatus = Status;
        Status = OutboundOrderStatus.Cancelled;
        CancellationReason = reason;
        CancelledAtUtc = DateTime.UtcNow;

        foreach (var line in Lines.Where(l => l.Status != OutboundOrderLineStatus.Completed && l.Status != OutboundOrderLineStatus.Cancelled))
        {
            line.Cancel();
        }

        StatusHistories.Add(new OrderStatusHistory
        {
            OutboundOrderId = Id,
            FromStatus = fromStatus,
            ToStatus = OutboundOrderStatus.Cancelled,
            Reason = reason,
            ChangedByUserId = userId,
            ChangedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Dynamically recalculates the order status based on active line allocations.
    /// Ensures orders with 0 active allocations return to Released, partial allocations become PartiallyAllocated,
    /// and fully allocated orders become Allocated. If the order is OnHold, updates StatusBeforeHold
    /// so unholding will accurately reflect current allocation state without dropping the hold prematurely.
    /// </summary>
    public void RecalculateAllocationStatus(Guid? userId = null, string? reason = null)
    {
        if (Status == OutboundOrderStatus.Completed || Status == OutboundOrderStatus.Cancelled)
        {
            return;
        }

        var activeLines = Lines.Where(l => !l.IsDeleted && l.Status != OutboundOrderLineStatus.Cancelled).ToList();
        if (activeLines.Count == 0)
        {
            return;
        }

        var totalOrdered = activeLines.Sum(l => l.QuantityOrdered);
        var totalAllocated = activeLines.Sum(l => l.QuantityAllocated);

        OutboundOrderStatus targetStatus;
        if (totalAllocated == 0)
        {
            targetStatus = OutboundOrderStatus.Released;
        }
        else if (totalAllocated < totalOrdered)
        {
            targetStatus = OutboundOrderStatus.PartiallyAllocated;
        }
        else
        {
            targetStatus = OutboundOrderStatus.Allocated;
        }

        if (Status == OutboundOrderStatus.OnHold)
        {
            StatusBeforeHold = targetStatus;
            return;
        }

        if (Status != targetStatus)
        {
            var fromStatus = Status;
            Status = targetStatus;

            StatusHistories.Add(new OrderStatusHistory
            {
                OutboundOrderId = Id,
                FromStatus = fromStatus,
                ToStatus = targetStatus,
                Reason = reason ?? $"Allocation status recalculated (Allocated: {totalAllocated}/{totalOrdered})",
                ChangedByUserId = userId ?? Guid.Empty,
                ChangedAtUtc = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Derives order-level delivery status across all packages fulfilling this outbound order.
    /// Phase 4.5 Outbound Shipment Tracking & Delivery Confirmation.
    /// </summary>
    public void RecalculateDeliveryStatus()
    {
        if (Packages.Count == 0)
        {
            DeliveryStatus = Status == OutboundOrderStatus.Completed ? OrderDeliveryStatus.Dispatched : OrderDeliveryStatus.PendingShipment;
            return;
        }

        var allDelivered = Packages.All(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.Delivered);
        var anyException = Packages.Any(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.DeliveryException ||
                                              p.CurrentTrackingStatus == TrackingMilestoneStatus.ReturnedToSender);
        var deliveredCount = Packages.Count(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.Delivered);
        var inTransitCount = Packages.Count(p => p.CurrentTrackingStatus == TrackingMilestoneStatus.InTransit ||
                                                 p.CurrentTrackingStatus == TrackingMilestoneStatus.OutForDelivery ||
                                                 p.CurrentTrackingStatus == TrackingMilestoneStatus.Accepted);

        if (anyException)
        {
            DeliveryStatus = OrderDeliveryStatus.DeliveryException;
            DeliveryStatusUpdatedAtUtc = DateTime.UtcNow;
        }
        else if (allDelivered)
        {
            DeliveryStatus = OrderDeliveryStatus.Delivered;
            DeliveredAtUtc = Packages.Max(p => p.DeliveredAtUtc) ?? DateTime.UtcNow;
            DeliveryStatusUpdatedAtUtc = DeliveredAtUtc;
        }
        else if (deliveredCount > 0 && deliveredCount < Packages.Count)
        {
            DeliveryStatus = OrderDeliveryStatus.PartiallyDelivered;
            DeliveryStatusUpdatedAtUtc = DateTime.UtcNow;
        }
        else if (inTransitCount > 0)
        {
            DeliveryStatus = OrderDeliveryStatus.InTransit;
            DeliveryStatusUpdatedAtUtc = DateTime.UtcNow;
        }
        else if (Status == OutboundOrderStatus.Completed)
        {
            DeliveryStatus = OrderDeliveryStatus.Dispatched;
            DeliveryStatusUpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            DeliveryStatus = OrderDeliveryStatus.PendingShipment;
            DeliveryStatusUpdatedAtUtc = DateTime.UtcNow;
        }
    }
}
