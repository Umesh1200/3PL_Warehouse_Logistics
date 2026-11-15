using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Traceable, append-only historical record of outbound order status transitions.
/// Captures initiating user, timestamps, previous/new status, and business justification.
/// Phase 4.1 Order Management.
/// </summary>
public class OrderStatusHistory : BaseEntity<Guid>
{
    public Guid OutboundOrderId { get; set; }
    public OutboundOrderStatus FromStatus { get; set; }
    public OutboundOrderStatus ToStatus { get; set; }

    public string? Reason { get; set; }
    public string? Notes { get; set; }

    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public OutboundOrder OutboundOrder { get; set; } = null!;

    public OrderStatusHistory()
    {
        Id = Guid.NewGuid();
    }
}

