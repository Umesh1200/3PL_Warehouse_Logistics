using Warehouse3PL.Domain.Common;
using Warehouse3PL.Domain.Entities.Catalog;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Domain.Entities.Outbound;

/// <summary>
/// Shortage log record tracking unfulfilled SKU demand when on-hand stock or lot/shelf-life constraints prevent full allocation.
/// DB-SPEC-3PL-2026 Phase 4.2 Allocation Engine.
/// </summary>
public class AllocationShortage : BaseEntity<Guid>, ITenantEntity
{
    public Guid ClientId { get; set; }
    public Guid FacilityId { get; set; }

    public Guid OutboundOrderId { get; set; }
    public Guid OutboundOrderLineId { get; set; }
    public Guid ItemId { get; set; }

    public decimal QuantityRequested { get; set; }
    public decimal QuantityAllocated { get; set; }
    public decimal ShortageQuantity { get; set; }

    public ShortageReason Reason { get; set; } = ShortageReason.InsufficientStock;
    public string? Notes { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
    public bool IsResolved { get; set; }

    // Navigations
    public Client Client { get; set; } = null!;
    public Facility Facility { get; set; } = null!;
    public OutboundOrder OutboundOrder { get; set; } = null!;
    public OutboundOrderLine OutboundOrderLine { get; set; } = null!;
    public Item Item { get; set; } = null!;

    public AllocationShortage()
    {
        Id = Guid.NewGuid();
    }

    public void Resolve(DateTime? resolvedAt = null)
    {
        IsResolved = true;
        ResolvedAtUtc = resolvedAt ?? DateTime.UtcNow;
    }
}

